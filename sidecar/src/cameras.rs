//! Per-camera state owned by the sidecar.
//!
//! The plugin (writer side) creates one ring file per Hullcam VDS part,
//! keyed by KSP's stable `Part.flightID`. The sidecar (reader side) globs
//! `<shm_dir>/*.ring`, parses the filename's stem as a u32 flight ID, and
//! maintains a registry of `CameraState` keyed by that ID.
//!
//! Each `CameraState` owns one mmap ring, one lazy-initialised
//! `EncoderBackend` (created on first frame so it sees the actual ring
//! dimensions), and a list of `TrackLocalStaticSample` weak refs — one per
//! peer-track subscribed to this camera. The consume loop iterates the
//! registry once per tick, encodes only cameras with `subscribers > 0`,
//! and fans the resulting NALs out to every alive track.

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::{Arc, Weak};

use serde::Serialize;
use tokio::sync::{Mutex, RwLock};
use tracing::{info, warn};
use webrtc::track::track_local::track_local_static_sample::TrackLocalStaticSample;

use crate::encoder::EncoderBackend;
use crate::shared_mem::{MmapFrameRing, MmapRingConfig};

/// Public shape returned by `GET /cameras` — what a browser sees before
/// it picks a subscription set.
#[derive(Debug, Clone, Serialize)]
pub struct CameraInfo {
    pub flight_id: u32,
    pub max_width: u32,
    pub max_height: u32,
}

pub struct CameraState {
    pub flight_id: u32,
    pub ring: MmapFrameRing,
    pub max_width: u32,
    pub max_height: u32,
    /// Lazy: created on first encoded frame so the encoder sees the
    /// actual frame dimensions, not the ring's max.
    pub encoder: Mutex<Option<Box<dyn EncoderBackend>>>,
    pub last_sequence: AtomicU64,
    /// Count of peer-tracks currently subscribed. Encoder runs only when > 0.
    pub subscribers: AtomicUsize,
    /// One `TrackLocalStaticSample` per subscribed peer-track. Weak refs so
    /// dropped peers get GC'd from this list naturally — no manual unsub
    /// bookkeeping per peer needed.
    pub tracks: RwLock<Vec<Weak<TrackLocalStaticSample>>>,
}

impl CameraState {
    /// Add a track to this camera and bump the subscriber count. Returns
    /// the count after the increment so the caller can request a keyframe
    /// on the 0→1 transition.
    pub async fn add_track(&self, track: Arc<TrackLocalStaticSample>) -> usize {
        let mut tracks = self.tracks.write().await;
        tracks.push(Arc::downgrade(&track));
        self.subscribers.fetch_add(1, Ordering::AcqRel) + 1
    }

    /// Drop the subscriber count by `n` (used when a peer goes away with
    /// multiple subscribed tracks). Stale weak refs get pruned by the
    /// consume loop on the next tick.
    pub fn release(&self, n: usize) {
        self.subscribers.fetch_sub(n, Ordering::AcqRel);
    }
}

/// Registry of cameras. Owns the rescan logic that discovers new rings
/// and forgets dead ones.
pub struct CameraRegistry {
    shm_dir: PathBuf,
    ring_cfg: MmapRingConfig,
    pub cameras: RwLock<HashMap<u32, Arc<CameraState>>>,
}

impl CameraRegistry {
    pub fn new(shm_dir: PathBuf, ring_cfg: MmapRingConfig) -> Self {
        Self {
            shm_dir,
            ring_cfg,
            cameras: RwLock::new(HashMap::new()),
        }
    }

    /// Walk `shm_dir`, attach any new `<flight_id>.ring` files, drop any
    /// that have disappeared from disk. Tolerant of the directory not yet
    /// existing — the plugin may not have created it.
    pub async fn rescan(&self) {
        let mut found: HashMap<u32, PathBuf> = HashMap::new();
        let mut entries = match tokio::fs::read_dir(&self.shm_dir).await {
            Ok(e) => e,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => return,
            Err(e) => {
                warn!(dir = %self.shm_dir.display(), error = %e, "rescan read_dir failed");
                return;
            }
        };
        while let Ok(Some(entry)) = entries.next_entry().await {
            let path = entry.path();
            if path.extension().and_then(|s| s.to_str()) != Some("ring") {
                continue;
            }
            let stem = match path.file_stem().and_then(|s| s.to_str()) {
                Some(s) => s,
                None => continue,
            };
            if let Ok(id) = stem.parse::<u32>() {
                found.insert(id, path);
            }
        }

        let mut cameras = self.cameras.write().await;

        // Attach new rings.
        for (id, path) in &found {
            if cameras.contains_key(id) {
                continue;
            }
            match MmapFrameRing::open(path, self.ring_cfg) {
                Ok(ring) => {
                    info!(
                        flight_id = id,
                        path = %path.display(),
                        max_dims = format!("{}x{}", self.ring_cfg.max_width, self.ring_cfg.max_height),
                        "camera ring attached",
                    );
                    cameras.insert(
                        *id,
                        Arc::new(CameraState {
                            flight_id: *id,
                            ring,
                            max_width: self.ring_cfg.max_width,
                            max_height: self.ring_cfg.max_height,
                            encoder: Mutex::new(None),
                            last_sequence: AtomicU64::new(0),
                            subscribers: AtomicUsize::new(0),
                            tracks: RwLock::new(Vec::new()),
                        }),
                    );
                }
                Err(e) => {
                    warn!(flight_id = id, path = %path.display(), error = %e, "failed to open ring");
                }
            }
        }

        // Drop rings that have disappeared.
        let mut removed = Vec::new();
        cameras.retain(|id, _| {
            let still = found.contains_key(id);
            if !still {
                removed.push(*id);
            }
            still
        });
        for id in removed {
            info!(flight_id = id, "camera ring removed");
        }
    }

    pub async fn list(&self) -> Vec<CameraInfo> {
        let cams = self.cameras.read().await;
        let mut out: Vec<_> = cams
            .values()
            .map(|s| CameraInfo {
                flight_id: s.flight_id,
                max_width: s.max_width,
                max_height: s.max_height,
            })
            .collect();
        // Stable ordering for tests + UX (a refresh shouldn't shuffle).
        out.sort_by_key(|c| c.flight_id);
        out
    }

    pub async fn get(&self, flight_id: u32) -> Option<Arc<CameraState>> {
        self.cameras.read().await.get(&flight_id).cloned()
    }

    /// Snapshot of all camera Arcs — used by the consume loop to iterate
    /// without holding the registry's RwLock while encoding.
    pub async fn snapshot(&self) -> Vec<Arc<CameraState>> {
        self.cameras.read().await.values().cloned().collect()
    }
}
