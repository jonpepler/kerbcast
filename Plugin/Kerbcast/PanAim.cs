using System;

namespace Kerbcast
{
    /* Unity-free 3-vector so the aim math is unit-testable without UnityEngine.
       KerbcastCamera converts to/from UnityEngine.Vector3 at the boundary. */
    public readonly struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length { get { return Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public Vec3 Normalized
        {
            get { double l = Length; return l < 1e-9 ? this : new Vec3(X / l, Y / l, Z / l); }
        }
    }

    /* Converts a target direction, expressed in the camera's REST-LOCAL frame
       (i.e. relative to KerbcastCamera._baseRotation, +Z forward / +X right /
       +Y up), into the (yaw, pitch) degrees the pan target uses.

       This is the exact inverse of the rotation the plugin applies each frame:
       Refresh() aims the camera with baseRot * Euler(-pitch, yaw, 0), and for a
       forward (0,0,1) that yields the rest-local direction
         (cos(pitch)*sin(yaw), sin(pitch), cos(pitch)*cos(yaw)).
       Inverting: pitch = asin(y), yaw = atan2(x, z). +yaw = toward rest-right,
       +pitch = toward rest-up. Pinned by PanAim.Tests. */
    public static class PanAim
    {
        public static void YawPitch(Vec3 restLocalDir, out float yaw, out float pitch)
        {
            Vec3 d = restLocalDir.Normalized;
            double y = d.Y < -1 ? -1 : (d.Y > 1 ? 1 : d.Y);
            pitch = (float)(Math.Asin(y) * 180.0 / Math.PI);
            yaw = (float)(Math.Atan2(d.X, d.Z) * 180.0 / Math.PI);
        }
    }
}
