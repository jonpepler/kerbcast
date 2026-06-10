//! Pixel-format conversion shared by the hardware encoder backends.
//!
//! Hardware H.264 encoders (VAAPI on Linux, Media Foundation on Windows)
//! want NV12 input (planar Y followed by interleaved UV at half
//! resolution); `RawFrame` carries RGBA8. The conversion is pure CPU math
//! with no OS dependencies, so it lives here once and the unit tests run
//! on every platform.

/// BT.601 limited-range RGBA to NV12 conversion into caller-provided
/// planes. Writes the Y plane into `y_plane` and the interleaved UV plane
/// into `uv_plane`, respecting each plane's stride. We do BT.601 (not
/// BT.709) for consistency with libavutil's default for SD/low-resolution
/// content; camera tiles in the 768x768 range live in that bucket and
/// matching the decoder's default avoids a colour-space mismatch in the
/// browser.
///
/// Invariants (callers validate before calling):
/// - `rgba.len() == width * height * 4`
/// - `width` and `height` are even (NV12 chroma subsampling)
/// - `y_plane.len() >= y_stride * height`
/// - `uv_plane.len() >= uv_stride * height / 2`
#[cfg_attr(not(any(target_os = "linux", target_os = "windows")), allow(dead_code))]
#[allow(clippy::too_many_arguments)]
pub(crate) fn rgba_to_nv12_planes(
    rgba: &[u8],
    width: u32,
    height: u32,
    y_plane: &mut [u8],
    y_stride: usize,
    uv_plane: &mut [u8],
    uv_stride: usize,
) {
    let w = width as usize;
    let h = height as usize;

    // Y plane: per-pixel.
    for y in 0..h {
        let row = y * y_stride;
        for x in 0..w {
            let src = (y * w + x) * 4;
            let r = rgba[src] as i32;
            let g = rgba[src + 1] as i32;
            let b = rgba[src + 2] as i32;
            // BT.601 limited-range Y' coefficients, fixed-point Q8.
            // Y' = 16 + (66R + 129G + 25B + 128) >> 8
            let yv = (66 * r + 129 * g + 25 * b + 128) >> 8;
            y_plane[row + x] = (yv + 16).clamp(0, 255) as u8;
        }
    }

    // UV plane: 2x2-averaged chroma, interleaved U then V per sample.
    let cw = w / 2;
    let ch = h / 2;
    for cy in 0..ch {
        for cx in 0..cw {
            // Average a 2x2 RGB block to reduce chroma aliasing, same
            // approach the standard NV12 conversion uses.
            let mut sr = 0i32;
            let mut sg = 0i32;
            let mut sb = 0i32;
            for dy in 0..2 {
                for dx in 0..2 {
                    let px = (cx * 2 + dx, cy * 2 + dy);
                    let src = (px.1 * w + px.0) * 4;
                    sr += rgba[src] as i32;
                    sg += rgba[src + 1] as i32;
                    sb += rgba[src + 2] as i32;
                }
            }
            let r = sr / 4;
            let g = sg / 4;
            let b = sb / 4;
            // BT.601 limited-range U/V coefficients, fixed-point Q8.
            let u = ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
            let v = ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
            let row = cy * uv_stride;
            uv_plane[row + cx * 2] = u.clamp(0, 255) as u8;
            uv_plane[row + cx * 2 + 1] = v.clamp(0, 255) as u8;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Convert a uniform RGBA image and return (y, u, v) of the first
    /// sample of each plane.
    fn convert_uniform(r: u8, g: u8, b: u8) -> (u8, u8, u8) {
        let (w, h) = (4u32, 4u32);
        let mut rgba = Vec::with_capacity((w * h * 4) as usize);
        for _ in 0..(w * h) {
            rgba.extend_from_slice(&[r, g, b, 0xFF]);
        }
        let mut y = vec![0u8; (w * h) as usize];
        let mut uv = vec![0u8; (w * h / 2) as usize];
        rgba_to_nv12_planes(&rgba, w, h, &mut y, w as usize, &mut uv, w as usize);
        (y[0], uv[0], uv[1])
    }

    #[test]
    fn black_maps_to_limited_range_floor() {
        // BT.601 limited range: black is Y=16, chroma neutral at 128.
        let (y, u, v) = convert_uniform(0, 0, 0);
        assert_eq!(y, 16);
        assert_eq!(u, 128);
        assert_eq!(v, 128);
    }

    #[test]
    fn white_maps_to_limited_range_ceiling() {
        // BT.601 limited range: white lands at Y=235 (plus or minus
        // a rounding step), chroma neutral.
        let (y, u, v) = convert_uniform(255, 255, 255);
        assert!((234..=236).contains(&y), "white Y = {y}");
        assert!((127..=129).contains(&u), "white U = {u}");
        assert!((127..=129).contains(&v), "white V = {v}");
    }

    #[test]
    fn pure_red_pushes_v_high_and_u_low() {
        let (_, u, v) = convert_uniform(255, 0, 0);
        assert!(v > 200, "red V should be high, got {v}");
        assert!(u < 110, "red U should be below neutral, got {u}");
    }

    #[test]
    fn respects_plane_strides() {
        // 2x2 white image into planes with stride 8: only the first
        // `width` bytes of each row may be written.
        let (w, h) = (2u32, 2u32);
        let rgba = vec![0xFFu8; (w * h * 4) as usize];
        let stride = 8usize;
        let mut y = vec![0u8; stride * h as usize];
        let mut uv = vec![0u8; stride * (h as usize / 2)];
        rgba_to_nv12_planes(&rgba, w, h, &mut y, stride, &mut uv, stride);
        assert_ne!(y[0], 0, "Y row 0 written");
        assert_ne!(y[stride], 0, "Y row 1 written at stride offset");
        assert_eq!(
            y[w as usize..stride],
            vec![0u8; stride - w as usize][..],
            "Y row padding untouched"
        );
        assert_eq!(
            uv[w as usize..stride],
            vec![0u8; stride - w as usize][..],
            "UV row padding untouched"
        );
    }
}
