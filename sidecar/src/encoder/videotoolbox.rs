//! macOS VideoToolbox H.264 backend — TIER-2 experimental.
//!
//! Stub for now. Real implementation will use `VTCompressionSession` via
//! the `coremedia-sys`/`videotoolbox-sys` crates. Tier-2 means functional
//! parity is community-supported but perf budgets aren't enforced.

use super::{EncodeConfig, EncodeError, EncoderBackend, Nal, RawFrame};

pub struct VideoToolbox {
    initialised: bool,
}

impl VideoToolbox {
    pub fn new() -> Self {
        Self { initialised: false }
    }
}

impl Default for VideoToolbox {
    fn default() -> Self {
        Self::new()
    }
}

impl EncoderBackend for VideoToolbox {
    fn name(&self) -> &'static str {
        "videotoolbox (stub)"
    }

    fn is_available(&self) -> bool {
        // TODO: probe VTIsHardwareEncodeSupported(kCMVideoCodecType_H264).
        // Off until the spike lands.
        false
    }

    fn init(&mut self, _cfg: EncodeConfig) -> Result<(), EncodeError> {
        Err(EncodeError::Unavailable)
    }

    fn encode(&mut self, _frame: &RawFrame<'_>) -> Result<Vec<Nal>, EncodeError> {
        Err(EncodeError::Unavailable)
    }

    fn request_keyframe(&mut self) {}

    fn close(&mut self) {
        self.initialised = false;
    }
}
