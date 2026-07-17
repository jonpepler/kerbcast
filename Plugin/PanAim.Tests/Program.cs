using System;
using Kerbcast;

/* Exe test harness (kerbcast Plugin/*.Tests convention). Asserts the aim
   convention on known directions in the camera's rest-local frame:
   forward=+Z, right=+X, up=+Y. Exits non-zero on any failure. */
static class Program
{
    static int failures;

    static void Approx(string name, float actual, float expected)
    {
        if (Math.Abs(actual - expected) > 0.01f)
        {
            Console.WriteLine($"FAIL {name}: expected {expected}, got {actual}");
            failures++;
        }
        else
        {
            Console.WriteLine($"ok   {name} = {actual}");
        }
    }

    static int Main()
    {
        float yaw, pitch;

        PanAim.YawPitch(new Vec3(0, 0, 1), out yaw, out pitch);
        Approx("straightAhead.yaw", yaw, 0f);
        Approx("straightAhead.pitch", pitch, 0f);

        PanAim.YawPitch(new Vec3(1, 0, 1), out yaw, out pitch);
        Approx("right.yaw", yaw, 45f);
        Approx("right.pitch", pitch, 0f);

        PanAim.YawPitch(new Vec3(-1, 0, 1), out yaw, out pitch);
        Approx("left.yaw", yaw, -45f);

        PanAim.YawPitch(new Vec3(0, 1, 1), out yaw, out pitch);
        Approx("up.pitch", pitch, 45f);
        Approx("up.yaw", yaw, 0f);

        PanAim.YawPitch(new Vec3(0, -1, 1), out yaw, out pitch);
        Approx("down.pitch", pitch, -45f);

        // Directly to the right (no forward component) is +90 yaw.
        PanAim.YawPitch(new Vec3(1, 0, 0), out yaw, out pitch);
        Approx("hardRight.yaw", yaw, 90f);

        Console.WriteLine(failures == 0 ? "ALL PASS" : $"{failures} FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }
}
