using UnityEngine;

namespace Kerbcast
{
    /* Zoom as an optional capability. Core fetches the limits to clamp + present,
       then calls SetFov with a clamped target in the standard frame; the source
       applies it to its concrete camera. */
    public interface IZoomCapability
    {
        float FovMin { get; }
        float FovMax { get; }
        float Fov { get; }
        void SetFov(float fov);
    }

    /* Pan as an optional capability. Core fetches yaw/pitch limits to clamp +
       present, then calls Steer with a clamped target in the standard (visual)
       frame; the source maps it to its joints, absorbing flip/axis quirks.

       Core's standardised steer (rate integration, slew, aim-solve, near-cam
       parenting) also reads the resolved mount frame + rates below through this
       contract, so core stays source-agnostic and binds to the interface, never
       a concrete pan type. A source that supports pan implements all of it; a
       source that can't pan returns null Pan (SupportsPan == Pan != null) and
       core never touches these. */
    public interface IPanCapability
    {
        float YawMin { get; }
        float YawMax { get; }
        float PitchMin { get; }
        float PitchMax { get; }
        float Yaw { get; }
        float Pitch { get; }
        void Steer(float yaw, float pitch);

        // Rate config for core's per-frame target integration + slew toward it.
        float SlewDegPerSec { get; }
        float PanRateDegPerSec { get; }

        /* Resolved mount frame core reads: the yaw joint the near camera parents
           to and the aim solve works in (with its rest rotation), the sign flip
           the aim solve must match, and an optional near-camera mount offset. */
        bool YawInvert { get; }
        Vector3? CameraMountLocal { get; }
        Transform YawJoint { get; }
        Quaternion YawJointRestRot { get; }
    }

    /* A camera's placement + identity + optional capabilities, decoupled from
       MuMechModuleHullCamera. Zoom/Pan are null when the source can't do them;
       KerbcastCamera derives SupportsZoom/SupportsPan from that. */
    public interface ICameraMountSource
    {
        // Mount transform + local pose
        Transform PartTransform { get; }
        Transform FindModelTransform(string name);
        string CameraTransformName { get; }
        Vector3 CameraPosition { get; }
        Vector3 CameraForward { get; }
        Vector3 CameraUp { get; }

        /* Fixed render params. DefaultFieldOfView is the authored FoV every
           camera has (zoom or not); the ctor seeds its initial Fov from it and
           the Zoom capability drives it thereafter. */
        float DefaultFieldOfView { get; }
        float NearClip { get; }
        int FilterMode { get; }

        // Optional capabilities (null == unsupported)
        IZoomCapability Zoom { get; }
        IPanCapability Pan { get; }

        // Owning vessel (FX / integration frame-state / sun)
        Vessel Vessel { get; }

        // Identity (cached once at KerbcastCamera construction)
        string PartName { get; }
        string PartTitle { get; }
        string CameraName { get; }
        string VesselDisplayName { get; }

        // Liveness + ownership, used by KerbcastCore churn
        bool IsAlive { get; }
        bool OwnsPart(Part part);

        /* Owning part, surfaced for the kOS control facade's camera view (which
           hands a bare Part handle to the addon). Null once the part is gone. */
        Part OwningPart { get; }
    }
}
