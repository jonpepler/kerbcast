//! Control + metadata message schemas for the WebRTC data channel between
//! the sidecar and a gonogo (or any) browser client.
//!
//! Wire format: JSON-per-message over an `RTCDataChannel`. Serde-derived
//! so we'll also be able to codegen TypeScript types via `ts-rs` later.

use serde::{Deserialize, Serialize};

/// Messages sent FROM the client TO the sidecar.
///
/// `tag = "type"` adds the discriminator; the outer `rename_all` renames
/// variant tags to camelCase (`subscribe`, `setQuality`, …). Inline
/// struct-variant fields need their own `rename_all` to get camelCase —
/// serde's enum-level attribute doesn't cascade into variant fields.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "camelCase")]
pub enum ClientToSidecar {
    /// First message on a new data channel. Sidecar replies with `Hello +
    /// Cameras` so the client gets the camera list and version on a single
    /// round-trip.
    Hello,
    /// List active cameras (alternative to `Hello` for re-syncs).
    ListCameras,
    /// Subscribe to a camera. Multiple subscribers per camera are allowed.
    /// `maxWidth` lets the client tell the sidecar the maximum render-target
    /// resolution it'll ever display this stream at — sidecar picks the
    /// highest such value across active subscribers (see §2.3 / §6.5).
    #[serde(rename_all = "camelCase")]
    Subscribe { camera_id: String, max_width: u32 },
    /// Release a previous subscription. Reference-counted on the sidecar.
    #[serde(rename_all = "camelCase")]
    Unsubscribe { camera_id: String },
    /// Adjust the quality cap on an existing subscription without
    /// resubscribing. Forces a keyframe on the next emit.
    #[serde(rename_all = "camelCase")]
    SetQuality { camera_id: String, max_width: u32 },
    /// Adjust the live FOV on a `MuMechModuleHullCameraZoom` part.
    #[serde(rename_all = "camelCase")]
    SetFov { camera_id: String, fov_degrees: f32 },
    /// Request an immediate IDR (Instantaneous Decoder Refresh) frame on the
    /// next encode tick. Browser sends this if it dropped frames and needs
    /// to recover.
    #[serde(rename_all = "camelCase")]
    RequestKeyframe { camera_id: String },
}

/// Messages sent FROM the sidecar TO the client.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "type", rename_all = "camelCase")]
pub enum SidecarToClient {
    #[serde(rename_all = "camelCase")]
    Hello {
        sidecar_version: String,
        encoder_backend: String,
    },
    Cameras {
        cameras: Vec<CameraMetadata>,
    },
    #[serde(rename_all = "camelCase")]
    Subscribed {
        camera_id: String,
    },
    #[serde(rename_all = "camelCase")]
    Unsubscribed {
        camera_id: String,
    },
    /// Frame-adjacent metadata. Emitted on every vessel change AND when
    /// configuration changes (FOV, quality cap, etc.) so the browser can
    /// keep its widget chrome in sync with the active stream.
    #[serde(rename_all = "camelCase")]
    Metadata {
        camera_id: String,
        metadata: CameraMetadata,
    },
    /// Adaptive scaling notice. Sidecar dropped quality (framerate or
    /// resolution) under load — operator should see *why*, not just *that*
    /// the picture changed. See rebuild doc §7.2.
    #[serde(rename_all = "camelCase")]
    QualityChange {
        camera_id: String,
        new_fps: Option<u32>,
        new_width: Option<u32>,
        reason: String,
    },
    Error {
        message: String,
    },
}

/// Per-camera metadata snapshot — mirrors the Hullcam VDS fields a client
/// might need to render the right chrome (camera-mode badge, FOV slider
/// bounds, etc.). Updated on vessel change.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CameraMetadata {
    pub camera_id: String,
    pub camera_name: String,
    /// 0..=8 — selects one of the Hullcam VDS `CameraFilter*` classes. See
    /// rebuild doc §3.2 for the full table.
    pub camera_mode: u8,
    pub camera_fov: f32,
    pub camera_fov_min: f32,
    pub camera_fov_max: f32,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn subscribe_roundtrips() {
        let msg = ClientToSidecar::Subscribe {
            camera_id: "cam-1".into(),
            max_width: 720,
        };
        let s = serde_json::to_string(&msg).unwrap();
        assert!(s.contains("\"subscribe\""));
        assert!(s.contains("\"cameraId\":\"cam-1\""));
        let back: ClientToSidecar = serde_json::from_str(&s).unwrap();
        match back {
            ClientToSidecar::Subscribe {
                camera_id,
                max_width,
            } => {
                assert_eq!(camera_id, "cam-1");
                assert_eq!(max_width, 720);
            }
            _ => panic!("wrong variant"),
        }
    }

    #[test]
    fn metadata_roundtrips_with_camel_case() {
        let m = SidecarToClient::Metadata {
            camera_id: "cam-2".into(),
            metadata: CameraMetadata {
                camera_id: "cam-2".into(),
                camera_name: "NavCam".into(),
                camera_mode: 4,
                camera_fov: 60.0,
                camera_fov_min: 20.0,
                camera_fov_max: 100.0,
            },
        };
        let s = serde_json::to_string(&m).unwrap();
        assert!(s.contains("\"cameraId\":\"cam-2\""));
        assert!(s.contains("\"cameraMode\":4"));
        assert!(s.contains("\"cameraFovMin\":20"));
    }

    #[test]
    fn unknown_variants_fail_cleanly() {
        let bad = r#"{"type":"nonexistent","fields":"whatever"}"#;
        let parsed: Result<ClientToSidecar, _> = serde_json::from_str(bad);
        assert!(parsed.is_err());
    }
}
