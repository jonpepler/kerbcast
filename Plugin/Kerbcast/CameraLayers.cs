using System;

namespace Kerbcast
{
    [Flags]
    internal enum CameraLayers
    {
        None    = 0,
        Near    = 1,
        Scaled  = 2,
        Galaxy  = 4,
        Far     = 8,
        All     = Near | Far | Scaled | Galaxy,
    }
}
