// Unit test for StaggerBudgetController — the frametime-budget capture-budget
// regulator. Verifies it converges the budget to fit a kerbcam-ms target,
// cuts fast / restores slowly, respects a deadband (no hunting), and is bounded
// by the camera count. msPerCam ≈ 3ms from the Deck trace (2026-06-07).
//
// Exit code 0 = pass, 1 = fail.

using System;
using Kerbcam;

int failures = 0;
void Check(bool cond, string msg)
{
    if (cond) Console.WriteLine("  ok   " + msg);
    else { Console.Error.WriteLine("  FAIL " + msg); failures++; }
}

const double MsPerCam = 3.0; // measured on the Deck

// Helper: simulate a steady scene where cost = budget * msPerCam, driving the
// controller until it settles. Returns the settled budget.
int Settle(StaggerBudgetController c, int camCount, double msPerCam, double budgetMs, double maxT = 120.0)
{
    double dt = 0.25;
    int last = c.Budget;
    for (double t = 0.0; t < maxT; t += dt)
    {
        double cost = c.Budget * msPerCam;
        last = c.Evaluate(cost, msPerCam, camCount, 60.0, t); // gameFps high: floor inactive
    }
    return last;
}

// --- 1. Converges to the budget that fits the target. 12ms / 3ms ≈ 4 cams. ---
{
    var c = new StaggerBudgetController(budgetMs: 12.0, initialBudget: 8);
    int b = Settle(c, 8, MsPerCam, 12.0);
    Check(b == 4, $"settles to 4 cams for a 12ms budget at 3ms/cam (got {b})");
}

// --- 2. A tighter budget settles lower; a looser one higher. ---
{
    var tight = new StaggerBudgetController(budgetMs: 6.0, initialBudget: 8);
    var loose = new StaggerBudgetController(budgetMs: 21.0, initialBudget: 1);
    int bt = Settle(tight, 8, MsPerCam, 6.0);
    int bl = Settle(loose, 8, MsPerCam, 21.0);
    Check(bt == 2, $"6ms budget -> 2 cams (got {bt})");
    // Restoring up from 1, it stops at the first budget whose cost enters the
    // ±15% deadband: 6 cams = 18ms >= 17.85ms lower edge. (7 would be dead-on
    // 21ms, but the deadband halts the climb one step early — conservative,
    // stays at/under budget.)
    Check(bl == 6, $"21ms budget -> 6 cams (deadband halts climb at the lower edge) (got {bl})");
}

// --- 3. Never exceeds the camera count, never drops below 1. ---
{
    var c = new StaggerBudgetController(budgetMs: 100.0, initialBudget: 1);
    int b = Settle(c, 5, MsPerCam, 100.0);
    Check(b == 5, $"huge budget clamps to camCount=5 (got {b})");
    var c2 = new StaggerBudgetController(budgetMs: 0.5, initialBudget: 8);
    int b2 = Settle(c2, 8, MsPerCam, 0.5);
    Check(b2 == 1, $"tiny budget floors at 1 (never freezes) (got {b2})");
}

// --- 4. Cuts FAST (one attack dwell), restores SLOWLY (one cam per release
//        dwell). From over-budget at 8 cams it should reach the target in a
//        single cut; recovering upward takes multiple slow steps. ---
{
    var c = new StaggerBudgetController(budgetMs: 12.0, initialBudget: 8,
        attackDwellSeconds: 1.0, releaseDwellSeconds: 4.0);
    // overloaded: cost = 8*3 = 24ms >> 12ms. First eval after attack dwell cuts
    // straight to 4.
    c.Evaluate(24.0, MsPerCam, 8, 60.0, 0.0);          // t=0 first decision
    int afterCut = c.Evaluate(24.0, MsPerCam, 8, 60.0, 1.0); // attack dwell elapsed
    Check(afterCut == 4, $"cuts straight to target in one attack step (got {afterCut})");
    // now scene lightens (msPerCam drops to 1ms => 4 cams = 4ms < under-band).
    // Restores one cam at a time, gated by the 4s release dwell.
    int r1 = c.Evaluate(4.0 * 1.0, 1.0, 8, 60.0, 1.0 + 4.0);  // one release dwell
    Check(r1 == 5, $"restores one cam after a release dwell (got {r1})");
    int r2soon = c.Evaluate(5.0 * 1.0, 1.0, 8, 60.0, 1.0 + 4.0 + 1.0); // too soon
    Check(r2soon == 5, "no second restore inside the release dwell");
}

// --- 5. Deadband: cost within ±15% of budget -> hold, no twitching. ---
{
    var c = new StaggerBudgetController(budgetMs: 12.0, initialBudget: 4,
        deadbandFrac: 0.15);
    // 4 cams * 3ms = 12ms, dead on target. Feed mild noise within ±15% (10.2..13.8).
    int prev = c.Budget, changes = 0;
    double dt = 0.25;
    var rngVals = new double[] { 12.0, 13.5, 10.5, 12.8, 11.2, 13.0, 10.4, 12.2 };
    for (int i = 0; i < 200; i++)
    {
        double cost = rngVals[i % rngVals.Length];
        int b = c.Evaluate(cost, MsPerCam, 8, 60.0, i * dt);
        if (b != prev) { changes++; prev = b; }
    }
    Check(changes == 0, $"holds budget steady inside the deadband — no hunting (changes={changes})");
}

// --- 6. No cost signal yet -> permits all cameras (don't stall feeds at start). ---
{
    var c = new StaggerBudgetController(budgetMs: 12.0, initialBudget: 1);
    int b = c.Evaluate(0.0, 0.0, 8, 60.0, 0.0);
    Check(b == 8, $"no measurement yet -> full budget (got {b})");
}

// --- 7. Physics floor (one-way): cuts BELOW the ms-budget when game fps is
//        under the floor, even though kerbcam is within its cost budget. ---
{
    // Generous 100ms budget (cost never triggers a cut), floor 20fps, 8 cams.
    var c = new StaggerBudgetController(budgetMs: 100.0, initialBudget: 8,
        attackDwellSeconds: 0.0, minKspFps: 20.0, floorHystFps: 4.0);
    int b1 = c.Evaluate(24.0, MsPerCam, 8, 15.0, 0.0); // way under budget, fps < floor
    Check(b1 == 7, $"below floor steps budget down despite being under ms-budget (got {b1})");
    int b2 = c.Evaluate(21.0, MsPerCam, 8, 16.0, 1.0); // still below floor
    Check(b2 == 6, $"keeps cutting while still below the floor (got {b2})");
}

// --- 8. Floor gates restore: won't add cameras back until fps clears
//        floor + hysteresis, so it never relaxes into time-dilation. ---
{
    var c = new StaggerBudgetController(budgetMs: 100.0, initialBudget: 4,
        attackDwellSeconds: 0.0, releaseDwellSeconds: 0.0, minKspFps: 20.0, floorHystFps: 4.0);
    int held = c.Evaluate(12.0, MsPerCam, 8, 21.0, 0.0); // under budget but fps < 24 (floor+hyst)
    Check(held == 4, $"restore blocked while fps below floor+hyst (got {held})");
    int up = c.Evaluate(12.0, MsPerCam, 8, 30.0, 1.0); // fps clears floor+hyst
    Check(up == 5, $"restore proceeds once fps clears floor+hyst (got {up})");
}

// --- 9. Floor disabled (minKspFps=0): pure ms-budget, ignores game fps. ---
{
    var c = new StaggerBudgetController(budgetMs: 100.0, initialBudget: 8,
        attackDwellSeconds: 0.0, minKspFps: 0.0);
    int b = c.Evaluate(24.0, MsPerCam, 8, 5.0, 0.0); // catastrophic fps, but no floor
    Check(b == 8, $"floor disabled -> ignores low fps, holds at budget (got {b})");
}

Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
return failures == 0 ? 0 : 1;
