//! Linux/Steam Deck VA-API H.264 backend — TIER-1.
//!
//! Stub for now. Real implementation pending the §8 spike #2 work — will
//! use libva via either `ffmpeg-next` (for h264_vaapi) or direct VA-API
//! bindings via `cros-libva`. Decision deferred until we have a working
//! synthetic-frame harness to test against.

use super::{EncodeConfig, EncodeError, EncoderBackend, Nal, RawFrame};

pub struct Libva {
    initialised: bool,
}

impl Libva {
    pub fn new() -> Self {
        Self { initialised: false }
    }
}

impl Default for Libva {
    fn default() -> Self {
        Self::new()
    }
}

impl EncoderBackend for Libva {
    fn name(&self) -> &'static str {
        "libva (stub)"
    }

    fn is_available(&self) -> bool {
        // TODO: probe /dev/dri/renderD128 + VAEntrypointEncSlice support for
        // VAProfileH264{Main,High}. For now we report false on every
        // platform so `auto_select` walks past us to the software fallback.
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
