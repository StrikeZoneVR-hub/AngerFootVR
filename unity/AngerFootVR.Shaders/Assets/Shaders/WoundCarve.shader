// A wound's hole (drawn just before the body it's in: the body's own drawing is held back there). Inside a wound's
// mouth the hole is traced as a real tunnel along the shot: the view goes in, meets the wall (skin, the yellow fat under
// it, wet muscle, a rib across a chest wound; darker the deeper), or the bottom of a wound the bullet stopped in, or, in a
// wound big enough to go right through, comes out the far side, and what's behind the body shows. A bite (an
// explosion's) is a ragged bowl of the same. Outside the wounds nothing is drawn.
Shader "AngerFootVR/WoundCarve"
{
    Properties
    {
        _FleshTex ("Flesh", 2D) = "white" {}
        _BoneTex ("Bone", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "grey" {}
        _SkinNoise ("Skin noise", 2D) = "grey" {}
        _BloodColor ("Blood", Color) = (0.42, 0.02, 0.03, 1)
        _FleshTint ("Flesh tint", Color) = (1, 1, 1, 1)
        _Pull ("Pull towards the eye (m)", Float) = 0.004
        _ThroughSize ("Radius that goes right through (m)", Float) = 0.019
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite On
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"
            #include "WoundCommon.cginc"

            sampler2D _FleshTex;
            sampler2D _BoneTex;
            float4 _FleshTint;
            float _Pull, _ThroughSize;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wp : TEXCOORD0;
                float3 wn : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.wp = wp;
                o.wn = UnityObjectToWorldNormal(v.normal);
                // (a few millimetres nearer the eye than the skin: the body's own drawing then gives way here)
                float3 toEye = normalize(_WorldSpaceCameraPos - wp);
                o.pos = mul(UNITY_MATRIX_VP, float4(wp + toEye * _Pull, 1.0));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // the wall of a wound at a point inside the body (depth: how far in from the skin)
            float3 Wall(float3 q, float3 wallN, float depth, float angle01, float axial, float seed, bool torso, float3 across, float3 e, float3 v, float age, float lod, float size, float2 flat)
            {
                // (the walls mapped round the tunnel; a bottom flat across it)
                float2 uv = flat.x > -50.0 ? flat * 22.0 + seed : float2(angle01 * 2.0 + seed, axial * 9.0 + seed * 0.61);
                float3 flesh = tex2Dlod(_FleshTex, float4(uv, 0, lod)).rgb * _FleshTint.rgb;
                // the layers: the skin's cut edge (dark), the yellow fat under it, then muscle
                float n = tex2Dlod(_NoiseTex, float4(uv * 1.7, 0, lod)).g;
                float skinEdge = 0.0018 + n * 0.0012;
                float fatEnd = 0.0055 + n * 0.004;
                float3 c = flesh;
                // (fat shows in a big wound; a bullet hole's edge is all blood)
                float3 fat = lerp(float3(0.62, 0.45, 0.2), float3(0.74, 0.56, 0.28), tex2Dlod(_NoiseTex, float4(uv * 3.1, 0, lod)).r);
                float fatShow = smoothstep(0.014, 0.03, size) * (0.45 + 0.4 * n);
                c = lerp(c, fat, smoothstep(fatEnd, fatEnd - 0.002, depth) * fatShow);
                c = lerp(c, float3(0.25, 0.05, 0.05), smoothstep(skinEdge + 0.0008, skinEdge, depth));
                // ribs across a chest wound: pale bands, a little way in
                if (torso)
                {
                    float h = dot(q - e, across) / 0.024 + seed * 3.0;
                    float band = smoothstep(0.62, 0.52, abs(frac(h) - 0.5) * 2.0);
                    float inBand = smoothstep(0.009, 0.013, depth) * smoothstep(0.038, 0.03, depth);
                    float3 bone = tex2Dlod(_BoneTex, float4(uv * 1.3, 0, lod)).rgb;
                    c = lerp(c, bone * 0.95, band * inBand);
                }

                // fresh blood pooling in the deep parts and running down the walls
                float wet = tex2Dlod(_NoiseTex, float4(uv * 0.8 + age * 0.002, 0, lod)).a;
                c = lerp(c, _BloodColor.rgb * 0.55, saturate(smoothstep(0.3, 0.8, wet) * 0.55 + (1.0 - smoothstep(0.012, 0.03, size)) * 0.5));
                // light: what gets in from the mouth (darker the deeper), the room's own light, a wet glint
                float shade = exp(-depth / 0.022) * 0.85 + 0.12;
                float3 amb = ShadeSH9(float4(normalize(wallN), 1.0));
                float3 L = _WorldSpaceLightPos0.w > 0.5 ? normalize(_WorldSpaceLightPos0.xyz - q) : _WorldSpaceLightPos0.xyz;
                float diff = saturate(dot(wallN, L)) * 0.35;
                float3 lit = c * (amb * 0.75 + _LightColor0.rgb * diff + 0.18) * shade;
                float3 r = reflect(v, wallN);
                float spec = pow(saturate(dot(r, L)), 48.0) * 0.6 + pow(saturate(dot(r, -v)), 24.0) * 0.08;
                return lit + spec * shade * (0.5 + 0.5 * wet) * (_LightColor0.rgb + 0.4);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 p = i.wp;
                float3 v = normalize(p - _WorldSpaceCameraPos);
                float lod = clamp(log2(max(length(p - _WorldSpaceCameraPos), 0.05) * 3.0), 0.0, 5.0);
                int count = (int)_WCount;
                [loop]
                for (int k = 0; k < count; k++)
                {
                    float4 A = _WA[k], B = _WB[k], C = _WC[k], D = _WD[k];
                    float kindf = C.z;
                    bool torso = kindf >= 3.5;
                    float kind = torso ? kindf - 4.0 : kindf;
                    if (kind > 1.5) continue; // (a bruise: nothing cut)
                    float seed = D.w;
                    if (kind > 0.5)
                    {
                        // a bite: a ragged bowl round its centre
                        float3 w = p - A.xyz;
                        float dist = length(w);
                        float3 dirw = w / max(dist, 1e-5);
                        float ang = atan2(dirw.z, dirw.x) * 0.15915494 + 0.5;
                        float R = A.w * Torn(ang + dirw.y * 0.3, seed, 0.35);
                        if (dist > R) continue;
                        // the view goes on into the bowl and meets its far wall
                        float b = dot(w, v);
                        float c2 = dot(w, w) - R * R;
                        float t = -b + sqrt(max(b * b - c2, 0.0));
                        float3 q = p + v * t;
                        float3 wallN = normalize(A.xyz - q);
                        float depth = length(q - p) * 0.6 + (R - dist) * 0.3;
                        float3 col = Wall(q, wallN, depth, ang, dot(q - A.xyz, B.xyz), seed, torso, D.xyz, A.xyz, v, C.w, lod, R, float2(-99, 0));
                        // the torn skin round the edge: a dark lip
                        col = lerp(col, float3(0.18, 0.03, 0.03), smoothstep(R * 0.86, R, dist) * 0.8);
                        UNITY_APPLY_FOG(i.fogCoord, col);
                        return fixed4(col, 1);
                    }

                    // a shot: is this point in one of its mouths?
                    float3 e = A.xyz;
                    float3 u = B.xyz;
                    float L = B.w;
                    float d, along, ang;
                    float3 perp;
                    float2 flat;
                    AroundAxis(p, e, u, D.xyz, d, along, ang, perp, flat);
                    float through = L > 0.0 ? 1.0 : 0.0;
                    float rIn = A.w, rOut = C.x;
                    float end = through > 0.5 ? L : C.y;
                    if (along < -rOut * 2.0 || along > end + rOut * 2.0) continue;
                    float atExit = through > 0.5 ? saturate((along / max(L, 1e-4) - 0.35) * 3.0) : 0.0;
                    float rMouth = lerp(rIn, rOut, atExit) * Torn(ang, seed + atExit * 0.5, atExit > 0.5 ? 0.42 : 0.22);
                    if (d > rMouth) continue;

                    // the tunnel (a little narrower than the torn mouth: the skin hangs over its edge)
                    // (a big round opens a wide channel inside: you see into it, and through)
                    float bigW = smoothstep(_ThroughSize * 0.85, _ThroughSize * 1.15, rIn) * through;
                    float rT = lerp(lerp(rIn, rOut, atExit) * 0.78, max(rIn, rOut) * 0.9, bigW);
                    if (d > rT)
                    {
                        // the lip: torn skin and the fat under it, sloping in
                        float t01 = saturate((d - rT) / max(rMouth - rT, 1e-5));
                        float3 lipN = normalize(-perp / max(d, 1e-5) + (atExit > 0.5 ? u : -u) * 0.8);
                        float depth = (1.0 - t01) * 0.006;
                        float3 col = Wall(p, lipN, depth, ang, along, seed, torso, D.xyz, e, v, C.w, lod, rIn, float2(-99, 0));
                        col = lerp(col, _BloodColor.rgb * 0.35, smoothstep(0.35, 1.0, t01) * 0.9);
                        UNITY_APPLY_FOG(i.fogCoord, col);
                        return fixed4(col, 1);
                    }

                    float3 vp = v - dot(v, u) * u;
                    float a2 = dot(vp, vp);
                    float b2 = dot(perp, vp);
                    float c2 = d * d - rT * rT;
                    float t = a2 > 1e-7 ? (-b2 + sqrt(max(b2 * b2 - a2 * c2, 0.0))) / a2 : 1e3;
                    float vu = dot(v, u);
                    // a wound the bullet stopped in: its bottom
                    if (through < 0.5)
                    {
                        float tb = vu > 1e-4 ? (end - along) / vu : 1e3;
                        if (tb < t)
                        {
                            float3 q = p + v * tb;
                            float3 bx = normalize(D.xyz - dot(D.xyz, u) * u + 1e-5);
                            float3 col = Wall(q, -u, end, ang, end, seed, torso, D.xyz, e, v, C.w, lod, rIn, float2(dot(q - e, bx), dot(q - e, cross(u, bx)))) * 0.5;
                            col = lerp(col, _BloodColor.rgb * 0.25, 0.5);
                            UNITY_APPLY_FOG(i.fogCoord, col);
                            return fixed4(col, 1);
                        }
                    }

                    float3 q = p + v * t;
                    float aq = dot(q - e, u);
                    float big = smoothstep(_ThroughSize * 0.85, _ThroughSize * 1.15, min(rIn, rOut));
                    if (through > 0.5 && (aq > L || aq < 0.0) && big > 0.0)
                    {
                        // right through: what's behind the body shows (the far mouth's edge a little dark)
                        float edge = saturate(1.0 - abs(aq - (aq > L ? L : 0.0)) / max(rT, 1e-4));
                        return fixed4(float3(0.15, 0.02, 0.02), saturate((1.0 - big) + edge * 0.35));
                    }

                    // the wall, so deep in (measured from the nearer mouth)
                    float aqc = clamp(aq, 0.0, end);
                    float depthIn = through > 0.5 ? min(aqc, L - aqc) : aqc;
                    float3 axisPt = e + u * aqc;
                    float3 wallN = normalize(axisPt - q + 1e-5);
                    float3 col = Wall(q, wallN, depthIn, ang, aqc, seed, torso, D.xyz, e, v, C.w, lod, min(rIn, rOut), float2(-99, 0));
                    UNITY_APPLY_FOG(i.fogCoord, col);
                    return fixed4(col, 1);
                }

                clip(-1);
                return 0;
            }
            ENDCG
        }
    }
}
