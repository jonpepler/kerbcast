using System;
using kOS.Safe;

namespace Kerbcast.Kos
{
    /* Drives AIM sources from inside the kOS CPU cycle. Registered on the CPU's
       shared.UpdateHandler as an IUpdateObserver, exactly like kOS's own vecdraw
       (VectorRenderer): its KOSUpdate runs at the safe point in the CPU update
       where TriggerOnFutureUpdate is valid.

       This is deliberately NOT a Unity MonoBehaviour. Ticking the delegate from a
       raw Unity Update() schedules triggers outside the CPU's execution context,
       which corrupts the shared VM stack whenever another recurring trigger
       (cooked LOCK STEERING) is active — surfacing as KOSArgMarkerType /
       toggleflybywire argument-count errors and re-arms that silently stop. */
    internal sealed class KerbcastAimObserver : IUpdateObserver
    {
        readonly AimSourceRegistry registry;
        readonly Action<uint, double, double, double> apply;
        readonly Action<string> log;

        /* Stall watchdog: quiet while healthy, one line when tracking stops
           updating (the kOS interpreter steering-lock quirk) and one when it
           resumes — instead of a per-tick heartbeat. */
        long applyCount;
        double sinceApply;
        bool stalled;
        uint lastId;

        const double StallSeconds = 3.0;

        public KerbcastAimObserver(AimSourceRegistry registry)
        {
            this.registry = registry;
            log = s => UnityEngine.Debug.Log("[KerbcastKos] " + s);
            /* Read Control at call time so a preset test seam / the real adapter
               swap is honoured without re-wiring. */
            apply = (id, x, y, z) =>
            {
                applyCount++;
                lastId = id;
                KerbcastAddon.Control.AimAt(id, x, y, z);
            };
        }

        public void KOSUpdate(double deltaTime)
        {
            long before = applyCount;
            registry.Tick(apply, log);

            if (applyCount > before)
            {
                sinceApply = 0;
                if (stalled) { log("aim tracking resumed"); stalled = false; }
            }
            else if (registry.Count > 0)
            {
                sinceApply += deltaTime;
                if (!stalled && sinceApply >= StallSeconds)
                {
                    log($"aim id={lastId} tracking stalled — no updates for {StallSeconds:0}s. "
                        + "Usually a kOS interpreter steering-lock quirk: REBOOT the CPU, "
                        + "or run tracking from a program (see the addon README).");
                    stalled = true;
                }
            }
            else
            {
                sinceApply = 0;   // no sources — not a stall
            }
        }

        public void Dispose() { }
    }
}
