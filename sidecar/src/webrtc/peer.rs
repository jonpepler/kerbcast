//! WebRTC peer for the kerbcam sidecar. One `KerbcamPeer` per browser
//! connection; each carries one H.264 video track per camera the browser
//! subscribed to. webrtc-rs handles ICE/DTLS/SRTP/RTP packetisation
//! internally.
//!
//! Track lifecycle: the peer owns Arcs to its tracks for the duration of
//! its RTCPeerConnection. Each camera's registry entry holds a Weak ref
//! to the same track + a subscriber count. When the peer is dropped,
//! the Arcs go with it; the camera-side consume loop notices the dead
//! Weaks on its next tick and decrements its subscriber count.

use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

use anyhow::{anyhow, Result};
use tokio::sync::Notify;
use tracing::{debug, info, warn};

use webrtc::api::media_engine::{MediaEngine, MIME_TYPE_H264};
use webrtc::api::APIBuilder;
use webrtc::ice_transport::ice_server::RTCIceServer;
use webrtc::peer_connection::configuration::RTCConfiguration;
use webrtc::peer_connection::peer_connection_state::RTCPeerConnectionState;
use webrtc::peer_connection::sdp::session_description::RTCSessionDescription;
use webrtc::peer_connection::RTCPeerConnection;
use webrtc::rtp_transceiver::rtp_codec::RTCRtpCodecCapability;
use webrtc::track::track_local::track_local_static_sample::TrackLocalStaticSample;
use webrtc::track::track_local::TrackLocal;

use crate::cameras::CameraRegistry;

pub struct KerbcamPeer {
    pc: Arc<RTCPeerConnection>,
    /// Arcs held for the lifetime of the peer connection. Dropping the
    /// peer drops these Arcs; the matching Weak refs in each camera's
    /// `tracks` list become stale and are pruned on the next encode tick.
    _tracks: Vec<Arc<TrackLocalStaticSample>>,
    /// flight_ids the peer subscribed to — surfaced for logging.
    pub subscribed: Vec<u32>,
    connected: Arc<Notify>,
    /// Flipped to `false` when the underlying RTCPeerConnection reaches a
    /// terminal state. The daemon polls `is_alive()` each consume tick.
    alive: Arc<AtomicBool>,
}

impl KerbcamPeer {
    /// Build a peer with one H.264 track per requested camera that exists
    /// in the registry. Unknown camera IDs are dropped with a warning —
    /// the caller can still return a useful answer to the browser with
    /// the surviving tracks.
    pub async fn new(registry: &CameraRegistry, requested: &[u32]) -> Result<Self> {
        let mut media_engine = MediaEngine::default();
        media_engine.register_default_codecs()?;
        let api = APIBuilder::new().with_media_engine(media_engine).build();

        let config = RTCConfiguration {
            ice_servers: vec![RTCIceServer {
                urls: vec!["stun:stun.l.google.com:19302".to_owned()],
                ..Default::default()
            }],
            ..Default::default()
        };

        let pc = Arc::new(api.new_peer_connection(config).await?);

        let mut owned_tracks = Vec::with_capacity(requested.len());
        let mut subscribed = Vec::with_capacity(requested.len());

        for &flight_id in requested {
            let cam = match registry.get(flight_id).await {
                Some(c) => c,
                None => {
                    warn!(flight_id, "requested camera not found, skipping track");
                    continue;
                }
            };

            let track = Arc::new(TrackLocalStaticSample::new(
                RTCRtpCodecCapability {
                    mime_type: MIME_TYPE_H264.to_owned(),
                    ..Default::default()
                },
                format!("video-{flight_id}"),
                format!("kerbcam-{flight_id}"),
            ));

            let rtp_sender = pc
                .add_track(track.clone() as Arc<dyn TrackLocal + Send + Sync>)
                .await?;

            // webrtc-rs requires us to drain the RTCP feedback stream from
            // each sender, otherwise NACK / PLI / REMB break silently.
            // Spawned per track; loop exits when the sender closes.
            tokio::spawn(async move {
                let mut rtcp_buf = vec![0u8; 1500];
                while rtp_sender.read(&mut rtcp_buf).await.is_ok() {}
                debug!("RTCP drain loop exited");
            });

            cam.add_track(track.clone()).await;
            owned_tracks.push(track);
            subscribed.push(flight_id);
        }

        let connected = Arc::new(Notify::new());
        let alive = Arc::new(AtomicBool::new(true));
        let connected_for_handler = connected.clone();
        let alive_for_handler = alive.clone();
        pc.on_peer_connection_state_change(Box::new(move |state: RTCPeerConnectionState| {
            info!(?state, "peer connection state");
            if state == RTCPeerConnectionState::Connected {
                connected_for_handler.notify_waiters();
            }
            if matches!(
                state,
                RTCPeerConnectionState::Disconnected
                    | RTCPeerConnectionState::Failed
                    | RTCPeerConnectionState::Closed
            ) {
                alive_for_handler.store(false, Ordering::Release);
            }
            Box::pin(async {})
        }));

        Ok(Self {
            pc,
            _tracks: owned_tracks,
            subscribed,
            connected,
            alive,
        })
    }

    /// Browser-initiated SDP flow used by the HTTP signalling endpoint.
    /// Browser POSTs its offer, we set it as the remote description, then
    /// create + return our answer.
    pub async fn answer_to_offer(&self, offer_sdp: String) -> Result<String> {
        let offer = RTCSessionDescription::offer(offer_sdp)?;
        self.pc.set_remote_description(offer).await?;

        let answer = self.pc.create_answer(None).await?;
        self.pc.set_local_description(answer).await?;

        let mut gather_complete = self.pc.gathering_complete_promise().await;
        let _ = gather_complete.recv().await;

        let local = self
            .pc
            .local_description()
            .await
            .ok_or_else(|| anyhow!("local description missing after set_local_description"))?;
        Ok(local.sdp)
    }

    pub fn is_alive(&self) -> bool {
        self.alive.load(Ordering::Acquire)
    }

    /// Block until the peer reaches the Connected state. Used by tests.
    #[allow(dead_code)]
    pub async fn wait_connected(&self) {
        self.connected.notified().await;
    }

    /// Tear down the peer cleanly. Idempotent.
    #[allow(dead_code)]
    pub async fn close(&self) -> Result<()> {
        self.pc.close().await?;
        Ok(())
    }
}
