//! Software-only H.264 fallback. Always available so `auto_select` never
//! returns `None`. Stub today; will wrap `openh264-sys2` or `x264` later.

use super::{EncodeConfig, EncodeError, EncoderBackend, Nal, RawFrame};

pub struct Software {
    cfg: Option<EncodeConfig>,
    keyframe_requested: bool,
}

impl Software {
    pub fn new() -> Self {
        Self {
            cfg: None,
            keyframe_requested: false,
        }
    }
}

impl Default for Software {
    fn default() -> Self {
        Self::new()
    }
}

impl EncoderBackend for Software {
    fn name(&self) -> &'static str {
        "software (stub)"
    }

    fn is_available(&self) -> bool {
        // The software fallback is the contract — `auto_select` walks the
        // tier list and stops at the first `is_available`. If this returned
        // false there would be no encoder on hosts without hardware support.
        true
    }

    fn init(&mut self, cfg: EncodeConfig) -> Result<(), EncodeError> {
        if cfg.width == 0 || cfg.height == 0 {
            return Err(EncodeError::Invalid("zero-sized dimensions".into()));
        }
        if cfg.fps == 0 {
            return Err(EncodeError::Invalid("fps == 0".into()));
        }
        self.cfg = Some(cfg);
        Ok(())
    }

    fn encode(&mut self, frame: &RawFrame<'_>) -> Result<Vec<Nal>, EncodeError> {
        let cfg = self
            .cfg
            .as_ref()
            .ok_or_else(|| EncodeError::Runtime("encode before init".into()))?;
        let expected = (frame.width as usize) * (frame.height as usize) * 4;
        if frame.data.len() != expected {
            return Err(EncodeError::Invalid(format!(
                "frame size {} != width*height*4 ({})",
                frame.data.len(),
                expected
            )));
        }
        if frame.width != cfg.width || frame.height != cfg.height {
            return Err(EncodeError::Invalid(format!(
                "frame dims {}x{} != configured {}x{}",
                frame.width, frame.height, cfg.width, cfg.height
            )));
        }
        // Real encode goes here. For now: just consume the keyframe-requested
        // flag so callers exercising the API see a well-behaved stub.
        self.keyframe_requested = false;
        Ok(Vec::new())
    }

    fn request_keyframe(&mut self) {
        self.keyframe_requested = true;
    }

    fn close(&mut self) {
        self.cfg = None;
        self.keyframe_requested = false;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn cfg() -> EncodeConfig {
        EncodeConfig {
            width: 4,
            height: 4,
            fps: 30,
            bitrate_bps: 1_000_000,
        }
    }

    #[test]
    fn always_available() {
        assert!(Software::new().is_available());
    }

    #[test]
    fn init_then_encode_a_correctly_sized_frame_returns_empty_nals() {
        let mut e = Software::new();
        e.init(cfg()).unwrap();
        let buf = vec![0u8; 4 * 4 * 4];
        let nals = e
            .encode(&RawFrame {
                width: 4,
                height: 4,
                data: &buf,
                capture_ts_ms: 0.0,
            })
            .unwrap();
        assert!(nals.is_empty());
    }

    #[test]
    fn encode_before_init_errors() {
        let mut e = Software::new();
        let buf = vec![0u8; 4 * 4 * 4];
        let err = e
            .encode(&RawFrame {
                width: 4,
                height: 4,
                data: &buf,
                capture_ts_ms: 0.0,
            })
            .unwrap_err();
        matches!(err, EncodeError::Runtime(_));
    }

    #[test]
    fn wrong_dimensions_errors() {
        let mut e = Software::new();
        e.init(cfg()).unwrap();
        let buf = vec![0u8; 8 * 8 * 4];
        let err = e
            .encode(&RawFrame {
                width: 8,
                height: 8,
                data: &buf,
                capture_ts_ms: 0.0,
            })
            .unwrap_err();
        matches!(err, EncodeError::Invalid(_));
    }

    #[test]
    fn keyframe_request_doesnt_panic() {
        let mut e = Software::new();
        e.init(cfg()).unwrap();
        e.request_keyframe();
        let buf = vec![0u8; 4 * 4 * 4];
        e.encode(&RawFrame {
            width: 4,
            height: 4,
            data: &buf,
            capture_ts_ms: 0.0,
        })
        .unwrap();
    }
}
