namespace Kerbcast
{
    /* Unity-free camera wire-id helpers. Kept out of Unity-bound code so the
       pure id math is unit-testable and reusable by camera-source providers. */
    public static class CameraId
    {
        /* Bit 31 is the namespace tag: part ids clear it, kerbal ids set it, so
           the two id spaces are disjoint BY CONSTRUCTION. This does NOT rely on
           KSP flightIDs being "small" — they aren't:
           ShipConstruction.GetUniqueFlightID returns (uint)Guid.GetHashCode(), a
           full 32-bit value that sets bit 31 ~half the time. Masking part ids to
           31 bits is what guarantees the split; without it a multi-camera part's
           hash (or a raw flightID) could land on a kerbal id. */
        private const uint KerbalTag = 0x80000000u;
        private const uint PartMask = 0x7FFFFFFFu;

        /* Stable wire id for a camera module, in the PART namespace (bit 31 clear).
           Module 0 uses the part flightID; modules 1+ get a Knuth-hash mix of id +
           index + name so a multi-camera part yields distinct, stable ids. Masked
           to 31 bits so a part id can never collide with a kerbal id. */
        public static uint Synthetic(uint baseId, int moduleIdx, string cameraName)
        {
            if (moduleIdx == 0) return baseId & PartMask;
            unchecked
            {
                uint h = baseId;
                h = h * 2654435761u + (uint)moduleIdx;
                if (!string.IsNullOrEmpty(cameraName))
                {
                    foreach (var ch in cameraName) h = h * 2654435761u + ch;
                }
                return h & PartMask;
            }
        }

        /* Wire id for a kerbal camera, in the KERBAL namespace (bit 31 set).
           Derived from a stable hash of the kerbal's roster name, which is unique
           and stable across seat<->EVA — unlike persistentID, which KSP reassigns
           on EVA (GetUniquepersistentId). So the SAME kerbal maps to the SAME id
           whether seated or on EVA, giving one continuous feed. Uses the same
           deterministic Knuth mix as Synthetic, NOT String.GetHashCode (which is
           not stable across Mono / runs). */
        public static uint KerbalWireId(string kerbalName)
        {
            return (NameHash(kerbalName) & PartMask) | KerbalTag;
        }

        private static uint NameHash(string name)
        {
            unchecked
            {
                uint h = 2166136261u; // fixed non-zero seed
                if (!string.IsNullOrEmpty(name))
                    foreach (var ch in name) h = h * 2654435761u + ch;
                return h;
            }
        }
    }
}
