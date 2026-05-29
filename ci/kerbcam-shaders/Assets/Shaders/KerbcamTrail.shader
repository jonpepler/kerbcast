// kerbcam atmospheric-FX trail shader. Drawn additively over a procedural
// tapered tube (built in TrailEffect.cs) that streams behind the vessel along
// -velocity. Composites against the near render's depth so it occludes
// correctly against terrain/ships in front of it.
//
// Look: a plasma wake. Bright at the vessel end, fading to nothing by the
// tail. Scrolling streaks along the tube length read as "flowing backward".
// Colour stays wind-white through moderate intensities; plasma-orange shift
// only blends in at hard reentry, same convention as KerbcamPlasma.
//
// UVs (set by mesh): uv.y runs 0 at the vessel-end → 1 at the tail; uv.x runs
// 0→1 around the tube circumference (seam-duplicated). Cull Off so both the
// outer and inner sides of the tube render — looking from inside the wake
// should still show.
Shader "Kerbcam/Trail"
{
    Properties
    {
        _WindColor   ("Wind Colour (low intensity)", Color) = (0.85, 0.92, 1.0, 1)
        _PlasmaColor ("Plasma Colour (high intensity)", Color) = (1.0, 0.45, 0.15, 1)
        _Intensity   ("Intensity", Range(0,4)) = 0
        _WindDirWorld("Wind Dir (world)", Vector) = (0,1,0,0)
        _ScrollSpeed ("Scroll Speed", Float) = 3.0
        _StreakFreq  ("Streak Frequency (along)", Float) = 18.0
        _CrossFreq   ("Streak Frequency (around)", Float) = 6.0
        _FadePower   ("Tail Fade Power", Range(0.5,8)) = 2.2
        // Plasma colour only blends in above this intensity (reserved for
        // heavy reentry); below it stays wind-white.
        _PlasmaOnset ("Plasma Onset", Range(0,1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend One One   // additive overlay
            ZWrite Off
            ZTest LEqual    // occlude against the near render's depth buffer
            Cull Off        // show both inside and outside of the tube

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _WindColor;
            float4 _PlasmaColor;
            float  _Intensity;
            float4 _WindDirWorld;
            float  _ScrollSpeed;
            float  _StreakFreq;
            float  _CrossFreq;
            float  _FadePower;
            float  _PlasmaOnset;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.texcoord.xy;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                if (_Intensity <= 0.0001) return fixed4(0, 0, 0, 0);

                // Length fade — bright at vessel end (uv.y=0), gone by the
                // tail (uv.y=1). pow gives a soft front and a long thin tail.
                float lenFade = pow(saturate(1.0 - i.uv.y), _FadePower);

                // Scroll speed scales with intensity — faster wake at higher
                // mach, per spec. Streaks move toward uv.y=1 (the tail) as
                // time advances: sample uv.y - t.
                float scrollT = _Time.y * _ScrollSpeed * (0.5 + _Intensity);
                float along = i.uv.y * _StreakFreq - scrollT;

                // Layered sines across uv.x so the streaks break up around
                // the tube rather than read as concentric bands.
                float across = sin(i.uv.x * _CrossFreq * 6.28318)
                             + sin(i.uv.x * _CrossFreq * 9.7 + 1.3) * 0.6;
                float streakRaw = sin(along) * 0.6
                                + sin(along * 2.1 + across * 0.8) * 0.4;
                // Map to [0,1], pinch into wisps.
                float streaks = saturate(0.5 + 0.55 * streakRaw);
                streaks = pow(streaks, 1.6);

                // Soft radial pulse toward the front so the head reads as a
                // hot core, not a uniform tube.
                float head = pow(saturate(1.0 - i.uv.y * 1.4), 2.0);

                float glow = (streaks * 0.85 + head * 0.4) * lenFade * _Intensity;

                // Same wind-white → plasma-orange convention as KerbcamPlasma.
                float plasmaShift = smoothstep(_PlasmaOnset, 1.0, _Intensity);
                float3 col = lerp(_WindColor.rgb, _PlasmaColor.rgb, plasmaShift);

                return fixed4(col * glow, 1.0);
            }
            ENDCG
        }
    }
}
