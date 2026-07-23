using System;
using System.Collections.Generic;
using System.Linq;
using Debug = UnityEngine.Debug;

namespace Kerbcast
{
    /* Enumerates crew as KerbalFaceCameras, one per ProtoCrewMember: seated crew
       from each part's protoModuleCrew, plus EVA kerbals swept from the loaded
       EVA vessels. Tourists are skipped (no operator interest). Dedupe on
       existingFlightIds keeps a seated kerbal who goes EVA on ONE camera/ring
       (that camera switches backend in place). Per-camera construction is
       isolated in try/catch so one bad kerbal can't drop the rest, matching
       HullcamProvider. */
    internal sealed class CrewProvider : ICameraSourceProvider
    {
        private readonly string _ringDir;
        private readonly int _ringSlots;
        private readonly int _width;
        private readonly int _height;

        public CrewProvider(string ringDir, int ringSlots, int width, int height)
        {
            _ringDir = ringDir;
            _ringSlots = ringSlots;
            _width = width;
            _height = height;
        }

        public IEnumerable<ICamera> Enumerate(Vessel vessel, IReadOnlyCollection<uint> existingFlightIds)
        {
            var result = new List<ICamera>();
            /* One dedupe set for BOTH loops, seeded from the already-tracked ids and
               grown as we build. existingFlightIds alone is not enough: when this
               runs against an EVA vessel, the seated loop below walks the KerbalEVA
               part and adds the kerbal, then the EVA sweep finds the same vessel —
               and the new camera is only in `result`, not in existingFlightIds, so a
               set keyed on existingFlightIds would build a SECOND camera on the same
               ring (MmapFrameRing.Create truncates+remaps rather than throwing, so
               the try/catch wouldn't catch it). */
            var seen = new HashSet<uint>(existingFlightIds);
            /* wire-id -> name for cameras built THIS call, so a name-hash collision
               (two different names mapping to the same wire-id) is logged rather
               than silently dropped. Only names we build here are known; a collision
               against an already-tracked (existingFlightIds) kerbal is skipped
               quietly, but that is astronomically rare (31-bit hash, tiny roster). */
            var seenName = new System.Collections.Generic.Dictionary<uint, string>();
            foreach (var part in vessel.parts)
            {
                foreach (var pcm in part.protoModuleCrew)
                {
                    if (pcm.type == ProtoCrewMember.KerbalType.Tourist) continue;
                    // Name is the stable+unique identity key; skip an empty/null name
                    // (dev-tool phantoms). A phantom sharing a real kerbal's name
                    // dedups to one camera, which is fine.
                    if (string.IsNullOrEmpty(pcm.name)) continue;
                    uint flightId = CameraId.KerbalWireId(pcm.name);
                    if (seen.Contains(flightId))
                    {
                        LogIfCollision(seenName, flightId, pcm.name);
                        continue;
                    }
                    seen.Add(flightId);
                    seenName[flightId] = pcm.name;

                    try
                    {
                        result.Add(new KerbalFaceCamera(pcm, part, _ringDir, _ringSlots, _width, _height));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Kerbcast] skipping kerbal cam flightId={flightId} (name={pcm.name}) on part {part.partInfo?.name}: {ex.Message}");
                    }
                }
            }

            /* EVA kerbals are their own one-part vessels, so the per-vessel seated
               sweep above misses anyone already on EVA (or on a vessel not being
               scanned this pass). Sweep loaded EVA vessels so each gets a
               KerbalFaceCamera at the SAME name-derived wire-id. Dedupe via `seen`
               (which now includes anything the seated loop just added) means a kerbal
               tracked while seated who then walks out the hatch is NOT rebuilt here:
               its existing camera switches to the EVA backend in place
               (KerbalFaceCamera.ResolveLocation), keeping one ring with no teardown. */
            var loaded = FlightGlobals.VesselsLoaded;
            if (loaded != null)
            {
                foreach (var v in loaded)
                {
                    if (v == null || !v.isEVA) continue;
                    var eva = v.evaController;
                    if (eva == null || eva.part == null) continue;
                    var crew = eva.part.protoModuleCrew;
                    if (crew.Count == 0) continue;
                    var pcm = crew[0];
                    if (pcm == null || pcm.type == ProtoCrewMember.KerbalType.Tourist) continue;
                    if (string.IsNullOrEmpty(pcm.name)) continue;
                    uint flightId = CameraId.KerbalWireId(pcm.name);
                    if (seen.Contains(flightId))
                    {
                        LogIfCollision(seenName, flightId, pcm.name);
                        continue;
                    }
                    seen.Add(flightId);
                    seenName[flightId] = pcm.name;

                    try
                    {
                        result.Add(new KerbalFaceCamera(pcm, eva.part, _ringDir, _ringSlots, _width, _height));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Kerbcast] skipping EVA kerbal cam flightId={flightId} (name={pcm.name}): {ex.Message}");
                    }
                }
            }

            return result;
        }

        // Warn when a dedup skip is actually a name-hash collision: two DIFFERENT
        // names mapping to the same wire-id (the second kerbal wouldn't stream).
        // A skip where the stored name matches is the normal same-kerbal dedup
        // (e.g. the seated cam preserved while the EVA sweep finds the same kerbal).
        private static void LogIfCollision(
            System.Collections.Generic.Dictionary<uint, string> seenName, uint flightId, string name)
        {
            if (seenName.TryGetValue(flightId, out var other) && other != name)
                Debug.LogWarning(
                    $"[Kerbcast] kerbal name-hash collision: '{name}' and '{other}' both map to "
                    + $"wire-id {flightId}; '{name}' not streamed");
        }
    }
}
