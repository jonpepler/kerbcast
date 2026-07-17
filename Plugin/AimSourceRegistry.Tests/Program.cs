using System;
using System.Collections.Generic;
using Kerbcast.Kos;

/* Exe test harness (kerbcast Plugin/*.Tests convention). Drives
   AimSourceRegistry with a fake IAimLease and asserts the poll/apply/re-arm
   lifecycle. Exits non-zero on any failure. */
static class Program
{
    static int failures;

    /* Settable Finished/Dead + canned result + a Rearm counter. DieOnRearm
       flips Dead when Rearm runs, modelling a delegate whose context is gone. */
    sealed class FakeLease : IAimLease
    {
        public bool Finished { get; set; }
        public bool Dead { get; set; }
        public bool DieOnRearm;
        public bool HasResult;
        public double Rx, Ry, Rz;
        public int Rearms;

        public bool TryResult(out double x, out double y, out double z)
        {
            x = Rx; y = Ry; z = Rz;
            return HasResult;
        }

        public void Rearm() { Rearms++; if (DieOnRearm) Dead = true; }
    }

    static void Check(string name, bool ok)
    {
        if (ok) { Console.WriteLine($"ok   {name}"); }
        else { Console.WriteLine($"FAIL {name}"); failures++; }
    }

    static int Main()
    {
        // 1. A Finished lease applies its result and rearms.
        {
            var reg = new AimSourceRegistry();
            var lease = new FakeLease { Finished = true, HasResult = true, Rx = 1, Ry = 2, Rz = 3 };
            reg.SetSource(7, lease);

            var applied = new List<(uint, double, double, double)>();
            reg.Tick((id, x, y, z) => applied.Add((id, x, y, z)));

            Check("finished lease applies its result", applied.Count == 1 && applied[0] == (7u, 1.0, 2.0, 3.0));
            Check("finished lease rearms", lease.Rearms == 1);
        }

        // 2. A not-Finished lease does nothing (no apply, no rearm).
        {
            var reg = new AimSourceRegistry();
            var lease = new FakeLease { Finished = false, HasResult = true, Rx = 1, Ry = 2, Rz = 3 };
            reg.SetSource(7, lease);

            var applied = 0;
            reg.Tick((id, x, y, z) => applied++);

            Check("pending lease does not apply", applied == 0);
            Check("pending lease does not rearm", lease.Rearms == 0);
        }

        // 2b. A Finished lease with no usable result rearms but does not apply
        //     (mirrors a freshly-armed lease's first tick).
        {
            var reg = new AimSourceRegistry();
            var lease = new FakeLease { Finished = true, HasResult = false };
            reg.SetSource(7, lease);

            var applied = 0;
            reg.Tick((id, x, y, z) => applied++);

            Check("finished-no-result lease does not apply", applied == 0);
            Check("finished-no-result lease still rearms", lease.Rearms == 1);
        }

        // 3. SetSource(id, null) clears the source.
        {
            var reg = new AimSourceRegistry();
            var lease = new FakeLease { Finished = true, HasResult = true };
            reg.SetSource(7, lease);
            Check("source registered", reg.HasSource(7));

            reg.SetSource(7, null);
            Check("SetSource null clears", !reg.HasSource(7));

            var applied = 0;
            reg.Tick((id, x, y, z) => applied++);
            Check("cleared source is not ticked", applied == 0 && lease.Rearms == 0);
        }

        // 4. Replacing a source swaps it (old lease no longer driven).
        {
            var reg = new AimSourceRegistry();
            var oldLease = new FakeLease { Finished = true, HasResult = true, Rx = 9, Ry = 9, Rz = 9 };
            var newLease = new FakeLease { Finished = true, HasResult = true, Rx = 1, Ry = 2, Rz = 3 };
            reg.SetSource(7, oldLease);
            reg.SetSource(7, newLease);

            var applied = new List<(uint, double, double, double)>();
            reg.Tick((id, x, y, z) => applied.Add((id, x, y, z)));

            Check("replaced source uses the new lease", applied.Count == 1 && applied[0] == (7u, 1.0, 2.0, 3.0));
            Check("old lease not driven after replace", oldLease.Rearms == 0);
            Check("new lease driven after replace", newLease.Rearms == 1);
        }

        // 5. A lease that dies on rearm is dropped, so it stops being ticked and
        //    a fresh SET AIM can take over the same id.
        {
            var reg = new AimSourceRegistry();
            var dying = new FakeLease { Finished = true, HasResult = true, DieOnRearm = true };
            reg.SetSource(7, dying);

            reg.Tick((id, x, y, z) => { });          // applies once, rearm marks it Dead -> dropped
            Check("dead lease removed after tick", !reg.HasSource(7));

            var fresh = new FakeLease { Finished = true, HasResult = true, Rx = 5, Ry = 6, Rz = 7 };
            reg.SetSource(7, fresh);
            var applied = new List<(uint, double, double, double)>();
            reg.Tick((id, x, y, z) => applied.Add((id, x, y, z)));
            Check("fresh lease drives after a dead one was dropped",
                applied.Count == 1 && applied[0] == (7u, 5.0, 6.0, 7.0));
        }

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }
}
