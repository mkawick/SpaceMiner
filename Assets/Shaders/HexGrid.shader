// Endless hex grid on the play plane, drawn procedurally (no texture).
// Pointy-top hexes laid out exactly like HexGrid.cs, so every cell matches a minimap hex.
// Lines stay the same pixel width at any zoom and fade out with distance from the camera.
Shader "Spacey/HexGrid"
{
    Properties
    {
        _HexSize ("Hex size (centre to corner, world units)", Float) = 200
        _LineColor ("Line colour", Color) = (0.8, 0.91, 0.8, 0.3)
        _LineWidth ("Line width (pixels)", Float) = 1.5
        _GlowColor ("Glow colour (soft band along the lines)", Color) = (0.8, 0.91, 0.8, 0.05)
        _GlowWidth ("Glow width (world units)", Float) = 14
        _FadeStart ("Fade start (distance from camera)", Float) = 700
        _FadeEnd ("Fade end (distance from camera)", Float) = 4000
        _HomeFill ("Home hex fill opacity", Float) = 0.05
        _HomeLineScale ("Home hex line width multiplier", Float) = 2.5
        [HideInInspector] _HomeHex ("Home hex (q, r) - set by HexGridBackground", Vector) = (0, 0, 0, 0)
        [HideInInspector] _HomeColor ("Home colour - the player's colour", Color) = (1, 1, 1, 1)
        [HideInInspector] _HasHome ("Has home hex", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "HexGrid"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _HexSize;
                float4 _LineColor;
                float _LineWidth;
                float4 _GlowColor;
                float _GlowWidth;
                float _FadeStart;
                float _FadeEnd;
                float _HomeFill;
                float _HomeLineScale;
                float4 _HomeHex;
                float4 _HomeColor;
                float _HasHome;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // Distance from p (world x, z) to the nearest hex edge. Same maths as HexGrid.WorldToHex.
            float HexEdgeDistance(float2 p, float size, out float2 hex)
            {
                const float sqrt3 = 1.7320508;
                float q = (sqrt3 / 3.0 * p.x - p.y / 3.0) / size;
                float r = (2.0 / 3.0 * p.y) / size;
                float s = -q - r;
                float rq = round(q), rr = round(r), rs = round(s);
                float dq = abs(rq - q), dr = abs(rr - r), ds = abs(rs - s);
                if (dq > dr && dq > ds) rq = -rr - rs;
                else if (dr > ds) rr = -rq - rs;
                hex = float2(rq, rr);
                float2 centre = float2(size * sqrt3 * (rq + rr * 0.5), size * 1.5 * rr);
                float2 d = abs(p - centre);
                float apothem = size * sqrt3 * 0.5;
                return apothem - max(d.x, d.x * 0.5 + d.y * sqrt3 * 0.5);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 hex;
                float e = HexEdgeDistance(i.positionWS.xz, max(1.0, _HexSize), hex);
                float home = (_HasHome > 0.5 && all(abs(hex - _HomeHex.xy) < 0.5)) ? 1.0 : 0.0;

                float px = max(fwidth(e), 1e-4);
                float halfWidth = px * _LineWidth * 0.5 * lerp(1.0, _HomeLineScale, home);
                float lineMask = 1.0 - smoothstep(halfWidth, halfWidth + px, e);
                float glow = 1.0 - saturate(e / max(0.01, _GlowWidth));
                glow *= glow;
                float dist = distance(i.positionWS.xz, _WorldSpaceCameraPos.xz);
                float fade = 1.0 - saturate((dist - _FadeStart) / max(1.0, _FadeEnd - _FadeStart));

                float3 lineColour = lerp(_LineColor.rgb, _HomeColor.rgb, home);
                float3 glowColour = lerp(_GlowColor.rgb, _HomeColor.rgb, home);
                float lineAlpha = lerp(_LineColor.a, max(_LineColor.a, 0.6), home);
                float glowAlpha = lerp(_GlowColor.a, _GlowColor.a * 2.0, home);
                float fill = home * _HomeFill;

                float alpha = max(max(lineMask * lineAlpha, glow * glowAlpha), fill);
                float3 colour = lerp(glowColour, lineColour, lineMask);
                // The home hex stays visible however far the camera is; other hexes fade.
                alpha *= lerp(fade, max(fade, 0.6), home);
                return half4(colour, alpha);
            }
            ENDHLSL
        }
    }
}
