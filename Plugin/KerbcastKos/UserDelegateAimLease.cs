using kOS.Safe.Encapsulation;
using kOS.Safe.Execution;

namespace Kerbcast.Kos
{
    /* Real IAimLease over a kOS UserDelegate. Wraps the delegate plus the
       TriggerInfo for its most recent evaluation. The poll/re-arm cadence
       mirrors kOS's own vecdraw VECTORUPDATER (a UserDelegate returning a Vector,
       re-evaluated every frame): re-trigger with InterruptPriority.Recurring
       each time the previous call finishes. Recurring — NOT CallbackOnce, which
       is for one-shot event callbacks — is what lets this coexist with other
       recurring triggers like cooked LOCK STEERING; re-arming a CallbackOnce
       every frame corrupts the shared VM stack (KOSArgMarkerType assert). The
       callback is expected to RETURN a kOS Vector (the aim target). */
    internal sealed class UserDelegateAimLease : IAimLease
    {
        readonly UserDelegate del;
        TriggerInfo trigger;
        bool dead;

        public UserDelegateAimLease(UserDelegate del)
        {
            this.del = del;
        }

        /* No trigger yet means nothing is pending, so it is safe to arm. */
        public bool Finished => trigger == null || trigger.CallbackFinished;

        /* TriggerOnFutureUpdate returns null once the delegate's kOS context is
           gone (CheckForDead). Latch that so the registry drops us. */
        public bool Dead => dead;

        public bool TryResult(out double x, out double y, out double z)
        {
            x = y = z = 0;
            if (trigger != null && trigger.ReturnValue is kOS.Suffixed.Vector v)
            {
                x = v.X;
                y = v.Y;
                z = v.Z;
                return true;
            }
            return false;
        }

        public void Rearm()
        {
            trigger = del.TriggerOnFutureUpdate(InterruptPriority.Recurring);
            if (trigger == null) dead = true;   // delegate's context popped; give up
        }
    }
}
