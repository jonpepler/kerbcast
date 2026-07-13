using System.IO;

namespace Kerbcast
{
    /* Unity-free helper for the plugin->sidecar in-flight file. Kept pure
       so it unit-tests without a KSP/Unity runtime; the host owns the
       HighLogic.LoadedSceneIsFlight read and the ~1Hz cadence. */
    internal static class InFlightSignal
    {
        public static string Format(bool inFlight)
        {
            return inFlight ? "1" : "0";
        }

        /* Atomic write: .tmp + rename so the sidecar never reads a
           half-written body. Mirrors KerbcastCore.MaybeWriteStatusFile. */
        public static void Write(string path, bool inFlight)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, Format(inFlight));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
