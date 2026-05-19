//! HTTP signalling endpoint: browsers POST an SDP offer, the daemon
//! creates a fresh `KerbcamPeer`, returns the answer SDP, and registers
//! the peer in the subscriber set. The consume loop in main.rs then fans
//! NAL units out to every alive subscriber.
//!
//! Wire format is intentionally tiny: one JSON field for the SDP, one for
//! the answer. Future iterations might bolt on ICE candidate trickle, a
//! per-camera selector, or a control data-channel — none of that's needed
//! for the "open the browser, see frames" MVP.

use std::sync::Arc;

use axum::extract::State;
use axum::http::StatusCode;
use axum::response::IntoResponse;
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use tokio::sync::RwLock;
use tower_http::cors::{Any, CorsLayer};
use tracing::{info, warn};

use crate::webrtc::KerbcamPeer;

/// Shared application state: the active peer subscriber set.
#[derive(Clone)]
pub struct AppState {
    pub peers: Arc<RwLock<Vec<Arc<KerbcamPeer>>>>,
}

#[derive(Debug, Deserialize)]
pub struct OfferRequest {
    pub sdp: String,
}

#[derive(Debug, Serialize)]
pub struct AnswerResponse {
    pub sdp: String,
}

/// Build the axum router. The bundled `webrtc_peer.html` test page is
/// served at `/` so a browser visit goes straight to a connect button —
/// no copy-paste of localhost paths.
pub fn router(state: AppState) -> Router {
    Router::new()
        .route("/", get(serve_index))
        .route("/health", get(health))
        .route("/offer", post(offer))
        // Permissive CORS so the test page can also run from `file://` or
        // a different origin during dev. Tighten before public release.
        .layer(
            CorsLayer::new()
                .allow_origin(Any)
                .allow_methods(Any)
                .allow_headers(Any),
        )
        .with_state(state)
}

async fn health() -> impl IntoResponse {
    (StatusCode::OK, "ok\n")
}

async fn serve_index() -> impl IntoResponse {
    (
        StatusCode::OK,
        [("content-type", "text/html; charset=utf-8")],
        include_str!("./signalling_index.html"),
    )
}

async fn offer(State(state): State<AppState>, Json(req): Json<OfferRequest>) -> impl IntoResponse {
    match handle_offer(state, req.sdp).await {
        Ok(answer_sdp) => {
            (StatusCode::OK, Json(AnswerResponse { sdp: answer_sdp })).into_response()
        }
        Err(e) => {
            warn!(error = %e, "offer handling failed");
            (
                StatusCode::INTERNAL_SERVER_ERROR,
                format!("offer handling failed: {e}"),
            )
                .into_response()
        }
    }
}

async fn handle_offer(state: AppState, offer_sdp: String) -> anyhow::Result<String> {
    let peer = Arc::new(KerbcamPeer::new().await?);
    let answer_sdp = peer.answer_to_offer(offer_sdp).await?;

    let peer_count = {
        let mut peers = state.peers.write().await;
        peers.push(peer.clone());
        peers.len()
    };
    info!(peer_count, "peer registered, returning answer");

    Ok(answer_sdp)
}
