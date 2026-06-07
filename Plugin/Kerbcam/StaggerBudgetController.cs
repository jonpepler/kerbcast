// StaggerBudgetController — regulates the per-frame capture budget (how many
// cameras render + read back this tick) to hold kerbcam's OWN main-thread cost
// within a frametime budget. Deliberately Unity-free (same approach as
// ShedController / ReadbackScheduler) so the decision is unit-tested without KSP.
//
// Why a budget, not an fps target: cost per capturing camera (msPerCam) is
// ~constant regardless of how many capture (measured ≈3 ms on the Deck), so the
// budget that fits a target is a direct division — budget = budgetMs / msPerCam.
// Targeting kerbcam's own cost (not game fps) is inherently KSP-independent, so
// there's no need to chase an fps the game can't reach (the heavy-vessel
// failure mode) and no cost-share gate. It's also a LOSSLESS temporal degrade:
// each camera still renders full-resolution with all its layers, just less often.
//
// Stability: msPerCam is budget-independent, so the loop doesn't perturb its own
// input — convergence is direct, not a hunt. A ±deadband around the budget plus
// asymmetric timing (cut fast to protect frame time, restore cameras slowly so
// feed rates don't pulse) keeps it from twitching on per-frame noise.

using System;

namespace Kerbcam
{
    public sealed class StaggerBudgetController
    {
        private readonly double _budgetMs;
        private readonly double _deadbandFrac;     // ± tolerance band around the budget
        private readonly double _attackDwellSeconds; // min seconds between cuts
        private readonly double _releaseDwellSeconds; // min seconds between (1-step) restores
        // One-way physics-floor safety. When game fps drops below _minKspFps,
        // kerbcam staggers HARDER than the ms-budget to keep KSP above its
        // time-dilation threshold (below which game-time slows + the stream
        // itself goes slow-motion). It only ever TIGHTENS / gates restore — it
        // never relaxes toward a target, so it can't set up the headroom-chasing
        // limit cycle a bidirectional fps target would. 0 = disabled.
        private readonly double _minKspFps;
        private readonly double _floorHystFps; // restore only once fps is this far above the floor

        private int _budget;
        private double _lastChangeTime = double.NegativeInfinity;

        public const double DefaultBudgetMs = 12.0;
        public const double DefaultDeadbandFrac = 0.15;
        public const double DefaultAttackDwellSeconds = 1.0;
        public const double DefaultReleaseDwellSeconds = 4.0;
        public const double DefaultFloorHystFps = 4.0;

        public StaggerBudgetController(
            double budgetMs = DefaultBudgetMs,
            int initialBudget = 1,
            double deadbandFrac = DefaultDeadbandFrac,
            double attackDwellSeconds = DefaultAttackDwellSeconds,
            double releaseDwellSeconds = DefaultReleaseDwellSeconds,
            double minKspFps = 0.0,
            double floorHystFps = DefaultFloorHystFps)
        {
            _budgetMs = Math.Max(0.1, budgetMs);
            _deadbandFrac = Math.Max(0.0, deadbandFrac);
            _attackDwellSeconds = Math.Max(0.0, attackDwellSeconds);
            _releaseDwellSeconds = Math.Max(0.0, releaseDwellSeconds);
            _minKspFps = Math.Max(0.0, minKspFps);
            _floorHystFps = Math.Max(0.0, floorHystFps);
            _budget = initialBudget < 1 ? 1 : initialBudget;
        }

        /// <summary>The current capture budget (cameras permitted per tick).</summary>
        public int Budget => _budget;

        /// <summary>
        /// Update the budget from the measured per-camera cost.
        /// </summary>
        /// <param name="kerbcamFrameMs">Measured kerbcam main-thread cost this
        /// frame (the capture loop's wall-time, EMA-smoothed by the caller).</param>
        /// <param name="msPerCam">Cost per capturing camera (≈ kerbcamFrameMs /
        /// cameras that actually captured). Budget-independent; the division
        /// target. If &lt;= 0 (nothing measured yet) the budget is left at camCount.</param>
        /// <param name="camCount">Cameras currently present (the budget ceiling).</param>
        /// <param name="gameFps">Current rolling game fps, for the physics-floor
        /// safety. Ignored when minKspFps is 0.</param>
        /// <param name="now">Monotonic seconds clock (unscaled time).</param>
        public int Evaluate(double kerbcamFrameMs, double msPerCam, int camCount, double gameFps, double now)
        {
            if (camCount <= 0) { _budget = 0; return 0; }
            if (_budget > camCount) _budget = camCount;
            if (_budget < 1) _budget = 1;

            // No cost signal yet — allow everything; the cost measurement on the
            // next frames will pull it in.
            if (kerbcamFrameMs <= 0.0 || msPerCam <= 0.0)
            {
                _budget = camCount;
                return _budget;
            }

            double over = _budgetMs * (1.0 + _deadbandFrac);
            double under = _budgetMs * (1.0 - _deadbandFrac);
            // Physics-floor safety (one-way). belowFloor adds a CUT trigger;
            // aboveFloor is a precondition on RESTORE. When the floor is disabled
            // (_minKspFps == 0) belowFloor is always false and aboveFloor always
            // true, so the controller behaves exactly as the pure ms-budget.
            bool floorOn = _minKspFps > 0.0 && gameFps > 0.0;
            bool belowFloor = floorOn && gameFps < _minKspFps;
            bool aboveFloor = !floorOn || gameFps >= _minKspFps + _floorHystFps;

            // Cut fast when over the ms-budget OR under the physics floor.
            if (_budget > 1 && (kerbcamFrameMs > over || belowFloor))
            {
                if (now - _lastChangeTime >= _attackDwellSeconds)
                {
                    int desired = _budget;
                    // Over budget: jump straight to the budget that fits the
                    // target (msPerCam is budget-independent — no overshoot).
                    if (kerbcamFrameMs > over)
                    {
                        int fit = (int)Math.Floor(_budgetMs / msPerCam);
                        if (fit < desired) desired = fit;
                    }
                    // Below the floor: step down one (game fps is a noisy signal,
                    // so converge gently rather than jump on a transient dip).
                    if (belowFloor && desired >= _budget) desired = _budget - 1;
                    if (desired < 1) desired = 1;
                    if (desired > camCount) desired = camCount;
                    if (desired < _budget)
                    {
                        _budget = desired;
                        _lastChangeTime = now;
                    }
                }
                return _budget;
            }

            // Restore ONE at a time, slowly — only when there's room on BOTH
            // counts (kerbcam under budget AND game fps comfortably above the
            // floor), so we never relax back into time-dilation.
            if (_budget < camCount && kerbcamFrameMs < under && aboveFloor)
            {
                if (now - _lastChangeTime >= _releaseDwellSeconds)
                {
                    _budget++;
                    _lastChangeTime = now;
                }
                return _budget;
            }

            // Within the deadband (and above the floor) — hold.
            return _budget;
        }
    }
}
