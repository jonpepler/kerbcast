// kerbcam atmospheric-FX core sheath shader. Drawn additively over the vessel's
// part renderers (CommandBuffer at AfterForwardAlpha) so it composites against
// the near render's depth — correct occlusion, no second camera.
//
// Look: streaks of air flowing past the hull (not a part-surface glow). The
// shell is inflated off the skin by a per-vertex noise so it variably comes
// near/far (lumpy, not a perfect shell). Streaks are anisotropic — narrow
// across, long along the wind axis — and scroll backward (toward -wind) over
// time so they read as flowing past the ship. Colour stays wind-white through
// moderate intensities; the plasma-orange shift is reserved for hard reentry.
Shader "Kerbcam/Plasma"
{
    Properties
    {
        _WindColor   ("Wind Colour (low intensity)", Color) = (0.85, 0.92, 1.0, 1)
        _PlasmaColor ("Plasma Colour (high intensity)", Color) = (1.0, 0.45, 0.15, 1)
        _Intensity   ("Intensity", Range(0,4)) = 0
        _WindDirWorld("Wind Dir (world)", Vector) = (0,1,0,0)
        _RimPower    ("Rim Power", Range(0.5,8)) = 3
        _NoiseSpeed  ("Streak Speed", Float) = 4
        _PuffDistance("Puff Distance (m)", Range(0,1)) = 0.18
        // Plasma colour only blends in above this intensity (reserved for
        // heavy reentry); below it stays wind-white.
        _PlasmaOnset ("Plasma Onset", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Transparent" }
        Pass
        {
            Blend One One   // additive overlay
            ZWrite Off
            ZTest LEqual    // occlude against the near render's depth buffer
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _WindColor;
            float4 _PlasmaColor;
            float  _Intensity;
            float4 _WindDirWorld;
            float  _RimPower;
            float  _NoiseSpeed;
            float  _PuffDistance;
            float  _PlasmaOnset;

            struct v2f
            {
                float4 pos        : SV_POSITION;
                float3 worldNormal: TEXCOORD0;
                float3 worldPos   : TEXCOORD1;
            };

            // Per-vertex puff modulator in [0.25 .. 1.0] — drives a lumpy,
            // non-uniform inflation that varies *across* the hull (so the shell
            // comes near and far from the parts, never further out than the
            // uniform _PuffDistance). High spatial frequencies (a few cycles
            // per metre) so peaks and valleys are visible at part scale; the
            // time term is position-phase-shifted so different points breathe
            // out of phase instead of the whole thing throbbing together.
            float puffScale(float3 p)
            {
                float n = sin(p.x * 2.3 + p.y * 1.7) * 0.5
                        + sin(p.y * 3.1 - p.z * 2.5 + 1.3) * 0.35
                        + sin(p.x * 4.7 + p.z * 3.9 + 0.8) * 0.25
                        + sin(p.z * 5.5 - p.y * 2.1 + 2.1) * 0.2;
                // Position-shifted breathing: phase varies with worldPos so
                // adjacent points are at different phases of the cycle.
                n += sin(_Time.y * 0.7 + p.x * 0.9 + p.z * 0.6) * 0.18;
                return 0.25 + 0.75 * saturate(n * 0.4 + 0.5);
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                worldPos += worldNormal * _PuffDistance * puffScale(worldPos);
                o.pos = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                o.worldNormal = worldNormal;
                o.worldPos = worldPos;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Intensity <= 0.0001) return fixed4(0, 0, 0, 0);

                float3 n = normalize(i.worldNormal);
                float3 wind = normalize(_WindDirWorld.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);

                // Wind-aligned coordinate frame (avoid the degenerate case
                // where wind is parallel to world up by switching the helper
                // axis). 'along' runs in the wind direction, lat/bin are the
                // two perpendicular axes.
                float3 helper = abs(wind.y) < 0.99 ? float3(0,1,0) : float3(1,0,0);
                float3 latAxis = normalize(cross(wind, helper));
                float3 binAxis = cross(wind, latAxis);
                float along = dot(i.worldPos, wind);
                float lat = dot(i.worldPos, latAxis);
                float bin = dot(i.worldPos, binAxis);

                float t = _Time.y * _NoiseSpeed;

                // Anisotropic streak pattern. High lateral frequency = narrow
                // parallel ridges across the flow. Low along-wind frequency,
                // scrolling with +t — because the sine `sin(k*along + w*t)`
                // moves the wave in the -along direction (i.e. -wind, backward
                // relative to motion) as t advances.
                float lateral = sin(lat * 4.0)
                              + sin(bin * 5.3 + 1.1) * 0.7
                              + sin(lat * 8.7 - bin * 2.3) * 0.5;
                float scroll = sin(along * 0.4 + t) * 0.5 + 0.5;
                float streaks = saturate(0.5 + 0.45 * lateral) * scroll;
                streaks = pow(streaks, 1.5); // pinch into wisps

                float windward = saturate(dot(n, wind));
                windward = windward * windward;
                float rim = pow(1.0 - saturate(dot(n, viewDir)), _RimPower);

                // Streaks dominate; the underlying glow is intentionally faint
                // so it reads as "air flowing past the hull" rather than the
                // hull itself glowing.
                float baseGlow = windward * 0.35 + rim * 0.25;
                float glow = (baseGlow + streaks * 0.9) * _Intensity;

                // Stay wind-white through moderate intensities. Plasma-orange
                // only blends in above _PlasmaOnset (reserved for hard reentry).
                float plasmaShift = smoothstep(_PlasmaOnset, 1.0, _Intensity);
                float3 col = lerp(_WindColor.rgb, _PlasmaColor.rgb, plasmaShift);
                return fixed4(col * glow, 1.0);
            }
            ENDCG
        }
    }
}
