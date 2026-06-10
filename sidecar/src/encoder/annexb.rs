//! Annex-B bytestream helpers shared by the hardware encoder backends.
//!
//! Both libva (Linux) and Media Foundation (Windows) hand back raw H.264
//! Annex-B bytestreams (NALs delimited by `00 00 01` / `00 00 00 01` start
//! codes). The WebRTC packetiser wants individual `Nal` units, so each
//! backend splits its packets through here. Pure byte-twiddling, no OS
//! dependencies: the unit tests run on every platform.

use super::Nal;

/// Split an Annex-B bytestream into individual NAL units by scanning for
/// 3-byte (`00 00 01`) and 4-byte (`00 00 00 01`) start codes. Each
/// emitted `Nal` includes its leading start code so the WebRTC
/// packetiser can parse the NAL header byte directly.
#[cfg_attr(not(any(target_os = "linux", target_os = "windows")), allow(dead_code))]
pub(crate) fn split_annexb_into(bytes: &[u8], out: &mut Vec<Nal>) {
    // Find start-code offsets, then carve the buffer between consecutive
    // offsets.
    let mut starts: Vec<usize> = Vec::new();
    let mut i = 0;
    while i + 3 <= bytes.len() {
        // 4-byte first so we don't double-count.
        if i + 4 <= bytes.len() && bytes[i..i + 4] == [0, 0, 0, 1] {
            starts.push(i);
            i += 4;
        } else if bytes[i..i + 3] == [0, 0, 1] {
            starts.push(i);
            i += 3;
        } else {
            i += 1;
        }
    }
    if starts.is_empty() {
        // No start codes found: emit the entire packet as a single NAL.
        // Hardware encoders shouldn't produce this, but defensive: better
        // one possibly-malformed NAL than silently dropping data.
        if !bytes.is_empty() {
            out.push(Nal(bytes.to_vec()));
        }
        return;
    }
    for w in starts.windows(2) {
        let (a, b) = (w[0], w[1]);
        out.push(Nal(bytes[a..b].to_vec()));
    }
    // Final NAL runs to end of buffer.
    let last = *starts.last().unwrap();
    out.push(Nal(bytes[last..].to_vec()));
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn split_annexb_handles_3byte_and_4byte_start_codes() {
        // Two NALs: first with 4-byte start code, second with 3-byte.
        // NAL header bytes 0x67 (SPS) and 0x68 (PPS) are valid H.264.
        let buf = [
            0x00, 0x00, 0x00, 0x01, 0x67, 0xAA, 0xBB, // NAL 1 (SPS-shaped)
            0x00, 0x00, 0x01, 0x68, 0xCC, // NAL 2 (PPS-shaped)
        ];
        let mut out: Vec<Nal> = Vec::new();
        split_annexb_into(&buf, &mut out);
        assert_eq!(out.len(), 2, "expected 2 NALs, got {}", out.len());
        // NAL header is the first byte after the start code. Mask off
        // the forbidden + ref_idc bits to read the nal_unit_type.
        assert_eq!(out[0].0[4] & 0x1F, 7, "first NAL should be SPS (type 7)");
        assert_eq!(out[1].0[3] & 0x1F, 8, "second NAL should be PPS (type 8)");
    }

    #[test]
    fn split_annexb_emits_full_buffer_when_no_start_code() {
        // Hardware encoders shouldn't do this, but the defensive branch
        // must still emit data rather than dropping it on the floor.
        let buf = [0xDE, 0xAD, 0xBE, 0xEF];
        let mut out: Vec<Nal> = Vec::new();
        split_annexb_into(&buf, &mut out);
        assert_eq!(out.len(), 1);
        assert_eq!(out[0].0, buf);
    }

    #[test]
    fn split_annexb_empty_input_emits_nothing() {
        let mut out: Vec<Nal> = Vec::new();
        split_annexb_into(&[], &mut out);
        assert!(out.is_empty());
    }
}
