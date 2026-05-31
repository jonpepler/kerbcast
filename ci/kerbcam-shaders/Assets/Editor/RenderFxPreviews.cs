// Headless preview renderer for the kerbcam FX shaders.
//
// Invoked from CI by:
//   "$UNITY_EDITOR_PATH" -batchmode -nographics -quit \
//      -projectPath . \
//      -executeMethod KerbcamCI.RenderFxPreviews.RenderAll \
//      -buildTarget Linux64 -logFile -
//
// For each *.json under ci/kerbcam-shaders/Fixtures/, build a proxy "rocket"
// (cylinder body + cone nose), set the camera per the fixture, apply the
// KerbcamPlasma material via a CommandBuffer at CameraEvent.AfterForwardAlpha
// (mirroring CoreSheathEffect.cs runtime path), set shader globals + material
// uniforms from the fixture, render once to a 1024×576 RenderTexture, encode
// to PNG, and save under Previews/{fixtureName}.png. The PNGs are uploaded
// as a CI artifact.
//
// Placeholders are deliberate: `fxMainTex`/`fxDepthMap` in fixtures default
// to procedural noise / flat depth when their path is empty. The same
// fixture format will be emitted by the in-game FxCapture hotkey once that
// lands; at that point we drop captured JSON + texture PNGs into Fixtures/
// and the renderer picks them up unchanged.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace KerbcamCI
{
    public static class RenderFxPreviews
    {
        private const int _outWidth = 1024;
        private const int _outHeight = 576;
        private const string _fixturesDir = "Fixtures";
        private const string _outputDir = "Previews";

        // CI entry point — same -executeMethod pattern as BuildKerbcamShaders.
        public static void RenderAll()
        {
            if (!Directory.Exists(_outputDir)) Directory.CreateDirectory(_outputDir);

            var fixtureFiles = Directory.GetFiles(_fixturesDir, "*.json");
            System.Array.Sort(fixtureFiles);
            Debug.Log($"[Kerbcam-CI] RenderFxPreviews: {fixtureFiles.Length} fixture(s)");

            var shader = Shader.Find("Kerbcam/Plasma");
            if (shader == null)
            {
                Debug.LogError("[Kerbcam-CI] Kerbcam/Plasma shader not found. Did the shader compile?");
                EditorApplication.Exit(2);
                return;
            }

            foreach (var path in fixtureFiles)
            {
                var json = File.ReadAllText(path);
                var fx = JsonUtility.FromJson<FxFixture>(json);
                if (fx == null || string.IsNullOrEmpty(fx.name))
                {
                    Debug.LogWarning($"[Kerbcam-CI] skipping unparseable fixture: {path}");
                    continue;
                }
                Debug.Log($"[Kerbcam-CI] rendering {fx.name} (intensity={fx.inputs.intensity:F2} fxState={fx.inputs.fxState:F2} radiusMul={fx.inputs.fxRadiusMul:F2})");
                RenderOne(fx, shader, Path.GetDirectoryName(path));
            }

            Debug.Log("[Kerbcam-CI] RenderFxPreviews: done");
        }

        private static void RenderOne(FxFixture fx, Shader shader, string fixtureDir)
        {
            // Build the scene fresh per fixture so leaked state can't bleed between renders.
            var sceneRoot = new GameObject("__fx_preview_root");
            try
            {
                // Proxy vessel: cylinder body + cone nose, axis along +Y so that
                // wind blowing along -Y in our fixtures runs from nose to tail
                // (matches a vertical-ascent KSP convention).
                var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                body.transform.SetParent(sceneRoot.transform, false);
                body.transform.localPosition = Vector3.zero;
                body.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
                var nose = MakeCone();
                nose.transform.SetParent(sceneRoot.transform, false);
                nose.transform.localPosition = new Vector3(0f, 1.5f, 0f);
                nose.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);

                // Directional light so the underlying proxy silhouette is
                // legibly lit — without one the body renders as a near-black
                // shape against the dark camera clear colour and we can't
                // tell where the FX overlay sits relative to the vessel.
                var lightGo = new GameObject("__fx_preview_light");
                lightGo.transform.SetParent(sceneRoot.transform, false);
                lightGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.color = new Color(1f, 0.96f, 0.9f, 1f);

                // Camera setup
                var camGo = new GameObject("__fx_preview_camera");
                camGo.transform.SetParent(sceneRoot.transform, false);
                ApplyCameraPose(camGo.transform, fx.camera);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 1f);
                cam.fieldOfView = fx.camera != null && fx.camera.fov > 0f ? fx.camera.fov : 60f;
                cam.nearClipPlane = fx.camera != null && fx.camera.near > 0f ? fx.camera.near : 0.3f;
                cam.farClipPlane = fx.camera != null && fx.camera.far > 0f ? fx.camera.far : 200f;
                cam.allowMSAA = false;
                cam.allowHDR = true;

                // Plasma material + per-fixture uniforms
                var mat = new Material(shader);
                ApplyMaterialInputs(mat, fx.inputs);

                // Globals + textures
                ApplyGlobals(fx.globals, fixtureDir, fx.textures);

                // CommandBuffer: draw both proxy renderers with our material at
                // CameraEvent.AfterForwardAlpha — same hook the runtime uses.
                var cb = new CommandBuffer { name = $"Kerbcam Preview FX [{fx.name}]" };
                foreach (var rend in sceneRoot.GetComponentsInChildren<Renderer>())
                {
                    if (rend == null || rend is ParticleSystemRenderer) continue;
                    int subMeshes = rend.sharedMaterials != null ? rend.sharedMaterials.Length : 1;
                    if (subMeshes < 1) subMeshes = 1;
                    for (int s = 0; s < subMeshes; s++) cb.DrawRenderer(rend, mat, s);
                }
                cam.AddCommandBuffer(CameraEvent.AfterForwardAlpha, cb);

                // Render to RT
                var rt = new RenderTexture(_outWidth, _outHeight, 24, RenderTextureFormat.ARGB32)
                {
                    name = $"FxPreviewRT_{fx.name}",
                    antiAliasing = 1
                };
                cam.targetTexture = rt;
                cam.Render();

                // Read back + encode
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(_outWidth, _outHeight, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, _outWidth, _outHeight), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                var outPath = Path.Combine(_outputDir, fx.name + ".png");
                File.WriteAllBytes(outPath, tex.EncodeToPNG());
                Debug.Log($"[Kerbcam-CI]   wrote {outPath} ({new FileInfo(outPath).Length} bytes)");

                cam.targetTexture = null;
                Object.DestroyImmediate(tex);
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(mat);
            }
            finally
            {
                Object.DestroyImmediate(sceneRoot);
            }
        }

        private static void ApplyCameraPose(Transform t, FxFixture.CameraPose pose)
        {
            if (pose == null) { t.localPosition = new Vector3(3.5f, 0f, 0f); t.LookAt(Vector3.zero, Vector3.up); return; }
            t.localPosition = ToVec3(pose.position, new Vector3(3.5f, 0f, 0f));
            if (pose.rotation != null && pose.rotation.Length >= 4)
                t.localRotation = new Quaternion(pose.rotation[0], pose.rotation[1], pose.rotation[2], pose.rotation[3]);
            else
                t.LookAt(Vector3.zero, Vector3.up);
        }

        private static void ApplyMaterialInputs(Material mat, FxFixture.Inputs inputs)
        {
            if (inputs == null) return;
            mat.SetFloat("_Intensity", inputs.intensity);
            mat.SetFloat("_FxState", inputs.fxState);
            mat.SetFloat("_FxRadiusMul", inputs.fxRadiusMul > 0f ? inputs.fxRadiusMul : 1.6f);
            mat.SetVector("_WindDirWorld", ToVec4(inputs.windDirWorld, new Vector4(0f, 1f, 0f, 0f)));
        }

        private static void ApplyGlobals(FxFixture.Globals g, string fixtureDir, FxFixture.Textures textures)
        {
            if (g != null)
            {
                Shader.SetGlobalVector("_LightDirection0", ToVec4(g.lightDirection0, new Vector4(0f, -1f, 0f, 0f)));
                Shader.SetGlobalVector("_FXColor", ToVec4(g.fxColor, new Vector4(1f, 0.5f, 0.2f, 1f)));
                Shader.SetGlobalFloat("_FxLength", g.fxLength);
                Shader.SetGlobalFloat("_FXWobble", g.fxWobble);
                Shader.SetGlobalFloat("_FXFalloff", g.fxFalloff);
                Shader.SetGlobalMatrix("_FXDepthCamMatrix", ToMat4(g.fxDepthCamMatrix, Matrix4x4.identity));
                Shader.SetGlobalMatrix("_FXDepthProjMatrix", ToMat4(g.fxDepthProjMatrix, Matrix4x4.identity));
                Shader.SetGlobalFloat("_FXProjectionNear", g.fxProjectionNear > 0f ? g.fxProjectionNear : 0.5f);
                Shader.SetGlobalFloat("_FXProjectionFar", g.fxProjectionFar > 0f ? g.fxProjectionFar : 80f);
            }

            // Textures: load from disk if a path is set; otherwise generate a
            // placeholder so the shader has something to sample.
            Shader.SetGlobalTexture("_FXMainTex",
                LoadOrPlaceholder(textures != null ? textures.fxMainTex : null, fixtureDir, MakeNoiseTexture));
            Shader.SetGlobalTexture("_FXDepthMap",
                LoadOrPlaceholder(textures != null ? textures.fxDepthMap : null, fixtureDir, MakeFlatDepthTexture));
        }

        private static Texture2D LoadOrPlaceholder(string relPath, string fixtureDir, System.Func<Texture2D> fallback)
        {
            if (!string.IsNullOrEmpty(relPath))
            {
                var abs = Path.Combine(fixtureDir, relPath);
                if (File.Exists(abs))
                {
                    var bytes = File.ReadAllBytes(abs);
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (t.LoadImage(bytes)) return t;
                    Debug.LogWarning($"[Kerbcam-CI] failed to load image: {abs}");
                }
                else Debug.LogWarning($"[Kerbcam-CI] missing texture file: {abs}");
            }
            return fallback();
        }

        private static Texture2D MakeNoiseTexture()
        {
            // 256×256 Perlin-noise placeholder for _FXMainTex.
            const int size = 256;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "PlaceholderFXMainTex" };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float n = Mathf.PerlinNoise(x * 0.04f, y * 0.04f);
                float streak = Mathf.PerlinNoise(x * 0.005f, y * 0.18f);
                float v = Mathf.Clamp01(0.4f * n + 0.7f * streak);
                px[y * size + x] = new Color(v, v, v, 1f);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D MakeFlatDepthTexture()
        {
            // 4×4 flat depth=1.0 placeholder for _FXDepthMap.
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "PlaceholderFXDepthMap" };
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = Color.white;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // Build a simple cone mesh (8 radial segments, apex at +Y) so the
        // proxy vessel has both flat sides (cylinder) and a tapered nose.
        // UVs included because KerbcamPlasma.shader's vert stage reads
        // v.uv — missing UVs would feed (0,0) and collapse trailUV.
        private static GameObject MakeCone()
        {
            var go = new GameObject("nose_cone");
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            var stdShader = Shader.Find("Standard");
            if (stdShader != null) mr.sharedMaterial = new Material(stdShader);
            const int seg = 8;
            const float r = 1f;
            const float h = 1.5f;
            var verts = new Vector3[seg + 2];
            var uvs = new Vector2[seg + 2];
            verts[0] = new Vector3(0f, h, 0f); // apex
            uvs[0] = new Vector2(0.5f, 1f);
            for (int i = 0; i < seg; i++)
            {
                float u = i / (float)seg;
                float a = u * Mathf.PI * 2f;
                verts[1 + i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                uvs[1 + i] = new Vector2(u, 0f);
            }
            verts[seg + 1] = Vector3.zero; // base centre
            uvs[seg + 1] = new Vector2(0.5f, 0f);
            var tris = new List<int>();
            for (int i = 0; i < seg; i++) { tris.Add(0); tris.Add(1 + i); tris.Add(1 + ((i + 1) % seg)); }
            for (int i = 0; i < seg; i++) { tris.Add(seg + 1); tris.Add(1 + ((i + 1) % seg)); tris.Add(1 + i); }
            var mesh = new Mesh { name = "nose_cone_mesh" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
            return go;
        }

        // --- helpers -------------------------------------------------------

        private static Vector3 ToVec3(float[] a, Vector3 fallback)
        {
            if (a == null || a.Length < 3) return fallback;
            return new Vector3(a[0], a[1], a[2]);
        }

        private static Vector4 ToVec4(float[] a, Vector4 fallback)
        {
            if (a == null || a.Length < 4) return fallback;
            return new Vector4(a[0], a[1], a[2], a[3]);
        }

        private static Matrix4x4 ToMat4(float[] a, Matrix4x4 fallback)
        {
            if (a == null || a.Length < 16) return fallback;
            var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i / 4, i % 4] = a[i];
            return m;
        }
    }
}
