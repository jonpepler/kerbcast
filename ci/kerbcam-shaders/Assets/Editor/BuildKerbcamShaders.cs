// Editor build script invoked by .github/workflows/build-kerbcam-shaders.yml
// and release.yml. Run via: -executeMethod KerbcamCI.BuildKerbcamShaders.BuildAll
// Output: Bundles-<platform>/kerbcam-shaders, shipped as
// GameData/Kerbcam/kerbcam-shaders (Linux, legacy unsuffixed name) plus
// kerbcam-shaders.windows and kerbcam-shaders.osx.
using System.IO;
using UnityEditor;
namespace KerbcamCI
{
    public static class BuildKerbcamShaders
    {
        // Shader variants are compiled per BuildTarget: a bundle built for one
        // platform cross-loads elsewhere but its shaders have no variant for
        // the running graphics API (Unity renders magenta). So build one
        // bundle per supported KSP platform; all three targets build on the
        // Linux editor as long as the player-support modules are installed.
        public static void BuildAll()
        {
            Build("Bundles-linux", BuildTarget.StandaloneLinux64);
            Build("Bundles-windows", BuildTarget.StandaloneWindows64);
            Build("Bundles-osx", BuildTarget.StandaloneOSX);
        }

        private static void Build(string outputDir, BuildTarget target)
        {
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            BuildPipeline.BuildAssetBundles(
                outputDir,
                BuildAssetBundleOptions.UncompressedAssetBundle,
                target);
        }
    }
}
