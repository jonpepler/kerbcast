//! kerbcam sidecar binary entry. Parses CLI args, initialises logging, picks
//! an encoder backend, and (eventually) wires the shared-memory frame source
//! to the WebRTC peer. v0.1 scaffold: just dispatches based on `--encoder`
//! and exits — real ingest/encode/transmit comes online in later spikes.

use anyhow::Result;
use clap::{Parser, ValueEnum};
use tracing::{info, warn};

use kerbcam_sidecar::encoder::{self, EncoderBackend};
use kerbcam_sidecar::VERSION;

#[derive(Copy, Clone, Debug, ValueEnum)]
enum EncoderChoice {
    /// Pick the best available backend at startup (tier-1 if present, fall
    /// through to software). Default.
    Auto,
    /// Linux/Steam Deck VA-API hardware encode. Tier-1.
    Libva,
    /// macOS hardware encode. Tier-2.
    Videotoolbox,
    /// Windows NVIDIA hardware encode. Tier-2.
    Nvenc,
    /// OpenH264 / x264 software fallback.
    Software,
}

#[derive(Parser, Debug)]
#[command(name = "kerbcam-sidecar", version = VERSION, about)]
struct Cli {
    /// Encoder backend to use. `auto` enumerates capabilities at startup.
    #[arg(long, value_enum, default_value_t = EncoderChoice::Auto)]
    encoder: EncoderChoice,

    /// Path the KSP plugin uses for the shared-memory ring buffer.
    /// Plugin and sidecar must agree.
    #[arg(long, default_value = "/tmp/kerbcam-frames")]
    shm_path: String,
}

#[tokio::main(flavor = "multi_thread")]
async fn main() -> Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info")),
        )
        .with_target(false)
        .init();

    let cli = Cli::parse();
    info!(version = VERSION, shm_path = %cli.shm_path, "kerbcam sidecar starting");

    let backend: Box<dyn EncoderBackend> = match cli.encoder {
        EncoderChoice::Auto => encoder::auto_select(),
        EncoderChoice::Libva => Box::new(encoder::Libva::new()),
        EncoderChoice::Videotoolbox => Box::new(encoder::VideoToolbox::new()),
        EncoderChoice::Nvenc => Box::new(encoder::Nvenc::new()),
        EncoderChoice::Software => Box::new(encoder::Software::new()),
    };
    info!(backend = backend.name(), "encoder selected");

    if !backend.is_available() {
        warn!(
            backend = backend.name(),
            "selected encoder is not available on this platform; sidecar would normally fall back to software here"
        );
    }

    // v0.1 scaffold: nothing to do yet beyond verifying the wiring. Stop
    // here cleanly. Subsequent spikes add the shared-mem ingest loop, the
    // WebRTC peer, and the control-channel handler.
    info!("scaffold-only build — nothing else wired yet; exiting");
    Ok(())
}
