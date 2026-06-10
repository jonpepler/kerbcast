//! Windows + NVIDIA NVENC H.264 backend — TIER-2 experimental.
//!
//! Stub for now. Real implementation pending; will likely use `nvenc-sys`
//! or wrap the NVENC SDK directly.

use super::{EncodeConfig, EncodeError, EncoderBackend, Nal, RawFrame};

pub struct Nvenc {
    initialised: bool,
}

impl Nvenc {
    pub fn new() -> Self {
        Self { initialised: false }
    }
}

impl Default for Nvenc {
    fn default() -> Self {
        Self::new()
    }
}

impl EncoderBackend for Nvenc {
    fn name(&self) -> &'static str {
        "nvenc (stub)"
    }

    fn is_available(&self) -> bool {
        false
    }

    fn is_hardware(&self) -> bool {
        true
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
