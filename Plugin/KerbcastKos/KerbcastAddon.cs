using System;
using kOS;
using kOS.Safe.Encapsulation;
using kOS.Safe.Encapsulation.Suffixes;
using kOS.Safe.Utilities;

namespace Kerbcast.Kos
{
    /* The ADDONS:KERBCAST suffix surface. Registered by kOS via the
       [kOSAddon] attribute + AssemblyWalk. Reaches the plugin only through
       the Unity-free IKerbcastControl seam; Control defaults lazily to the
       in-game RealKerbcastControl, and a test presetting it never JITs the
       real adapter. */
    [kOS.AddOns.kOSAddon("KERBCAST")]
    [KOSNomenclature("KerbcastAddon")]
    public class KerbcastAddon : kOS.Suffixed.Addon
    {
        public static IKerbcastControl Control;   // test seam; defaulted lazily below

        /* AIM sources for this CPU's cameras, driven by an IUpdateObserver
           registered on the CPU's own UpdateHandler (see SetAim). Per-instance,
           not static: a delegate must be triggered from the cycle of the CPU it
           belongs to, and each kOS CPU gets its own addon instance. */
        readonly AimSourceRegistry aimRegistry = new AimSourceRegistry();
        KerbcastAimObserver observer;

        public KerbcastAddon(SharedObjects shared) : base(shared)
        {
            if (Control == null) Control = new RealKerbcastControl();
            AddSuffix("CAMERAS", new Suffix<ListValue>(GetCameras));
            AddSuffix("CAMERA", new OneArgsSuffix<KerbcastCameraStruct, StringValue>(GetCamera));
        }

        /* Register/replace/clear the AIM source for a camera. A null or
           NoDelegate delegate clears it. On first use, registers the update
           observer on THIS CPU's UpdateHandler so the delegate is polled from
           the CPU cycle (vecdraw's pattern) rather than a Unity Update. */
        public void SetAim(uint id, UserDelegate del)
        {
            if (observer == null) observer = new KerbcastAimObserver(aimRegistry);
            // Re-add every time (HashSet, so idempotent): a CPU reboot /
            // ClearAllObservers drops it, and only a re-add revives tracking.
            shared.UpdateHandler.AddObserver(observer);
            bool track = !(del == null || del is NoDelegate);
            aimRegistry.SetSource(id, track ? new UserDelegateAimLease(del) : null);
            UnityEngine.Debug.Log($"[KerbcastKos] SetAim id={id} {(track ? "track (delegate)" : "clear")}"
                + $"; observer registered, {aimRegistry.Count} source(s)");
        }

        public override BooleanValue Available() => Control.IsActive && shared.Vessel != null;

        ListValue GetCameras()
        {
            var list = new ListValue();
            foreach (var v in Control.CamerasFor(shared.Vessel))
                list.Add(new KerbcastCameraStruct(shared, v.FlightId, Control, this));
            return list;
        }

        KerbcastCameraStruct GetCamera(StringValue uid) =>
            new KerbcastCameraStruct(shared, Convert.ToUInt32((string)uid), Control, this);
    }
}
