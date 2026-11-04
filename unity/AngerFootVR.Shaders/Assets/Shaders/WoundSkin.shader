// What a wound does to the skin round it (drawn over the body, after it): the torn raw rim, a ring of grazed skin, the
// bruise spreading out, blood soaking out from the wound (wider as it bleeds), and the drips that run down from it under
// gravity and stay where they ran, wet and dark (darker and duller as they dry). A kick's bruise: a dark blotch.
Shader "AngerFootVR/WoundSkin"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "grey" {}
        _SkinNoise ("Skin noise", 2D) = "grey" {}
        _BloodColor ("Blood", Color) = (0.42, 0.02, 0.03, 1)
        _Pull ("Pull towards the eye (m)", Float) = 0.0008
        _Gloss ("Wet gloss", Float) = 0.7
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
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

            float _Pull, _Gloss;

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
                float3 toEye = normalize(_WorldSpaceCameraPos - wp);
                o.pos = mul(UNITY_MATRIX_VP, float4(wp + toEye * _Pull, 1.0));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // a layer over what's there (colour, coverage, wetness)
            void Over(inout float3 col, inout float a, inout float wet, float3 c, float ca, float w)
            {
                col = lerp(col, c, ca * (1.0 - a * 0.0) );
                a = a + ca * (1.0 - a);
                wet = max(wet, w * ca);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 p = i.wp;
                float3 n = normalize(i.wn);
                float3 v = normalize(p - _WorldSpaceCameraPos);
                float3 col = 0;
                float a = 0;
                float wet = 0;
                float3 blood = _BloodColor.rgb;
                float3 raw = blood * float3(0.55, 0.35, 0.35);

                int count = (int)_WCount;
                [loop]
                for (int k = 0; k < count; k++)
                {
                    float4 A = _WA[k], B = _WB[k], C = _WC[k], D = _WD[k];
                    float kindf = C.z;
                    float kind = kindf >= 3.5 ? kindf - 4.0 : kindf;
                    float seed = D.w;
                    float age = C.w;
                    float soakGrow = saturate(age / 18.0);
                    if (kind > 1.5)
                    {
                        // a kick's bruise: a soft dark blotch, mottled
                        float dist = length(p - A.xyz);
                        float r = A.w;
                        if (dist > r) continue;
                        float mott = tex2Dlod(_SkinNoise, float4(p.xy * 9.0 + p.z * 5.0 + seed, 0, 0)).g;
                        float k01 = pow(saturate(1.0 - dist / r), 1.4) * saturate(age * 3.0);
                        Over(col, a, wet, float3(0.2, 0.06, 0.14), k01 * (0.35 + 0.3 * mott), 0.0);
                        continue;
                    }

                    if (kind > 0.5)
                    {
                        // round a bite: raw torn flesh, bruising, blood soaking out
                        float dist = length(p - A.xyz) - A.w;
                        float mott = tex2Dlod(_SkinNoise, float4(p.xz * 7.0 + p.y * 3.0 + seed, 0, 0)).g;
                        if (dist < 0.0 || dist > A.w * 1.6) continue;
                        float t = dist / (A.w * 1.6);
                        Over(col, a, wet, float3(0.18, 0.05, 0.11), (1.0 - t) * 0.5, 0.0);
                        Over(col, a, wet, blood * (0.6 + 0.4 * mott), smoothstep(0.6, 0.1, t) * 0.85, 1.0);
                        Over(col, a, wet, raw, smoothstep(0.12, 0.0, t), 0.6);
                        continue;
                    }

                    // a shot: both its mouths (the way in, and the way out if it went through)
                    float3 e = A.xyz;
                    float3 u = B.xyz;
                    float L = B.w;
                    float d, along, ang;
                    float3 perp;
                    float2 flat;
                    AroundAxis(p, e, u, D.xyz, d, along, ang, perp, flat);
                    for (int m = 0; m < 2; m++)
                    {
                        if (m == 1 && L <= 0.0) break;
                        float at = m == 0 ? 0.0 : L;
                        float r = m == 0 ? A.w : C.x;
                        // (only the skin at this mouth, facing out of it: not the far side of the body)
                        float facing = dot(n, m == 0 ? -u : u);
                        if (abs(along - at) > r * 3.5 + 0.03 || facing < -0.15) continue;
                        float torn = Torn(ang, seed + m * 0.5, m == 0 ? 0.16 : 0.38);
                        float rr = r * torn;
                        float far = r * (m == 0 ? 3.6 : 4.2);
                        if (d > far + r * 2.5 * soakGrow) continue;
                        float2 suv = flat * 14.0 + seed;
                        float4 sn = tex2Dlod(_SkinNoise, float4(suv, 0, 0));
                        // the bruise round it (darker close in, mottled)
                        float bt = saturate((d - r) / max(far - r, 1e-4));
                        Over(col, a, wet, float3(0.16, 0.05, 0.12), pow(1.0 - bt, 2.2) * (0.18 + 0.3 * sn.g) * saturate(age * 1.5 + 0.2), 0.0);
                        // blood soaking out from it, wider as it bleeds (patchy at its edge)
                        // (ragged at its edge, the cloth or skin taking it unevenly)
                        float edgeN = tex2Dlod(_NoiseTex, float4(flat * 9.0 + seed * 1.7, 0, 0)).g;
                        float soak = r * (1.3 + 2.2 * soakGrow) * (0.8 + 0.4 * edgeN);
                        float st = saturate((d - rr) / max(soak - rr, 1e-4));
                        float patchy = smoothstep(0.3, 0.7, sn.a * 0.7 + edgeN * 0.5 + (1.0 - st) * 0.45);
                        Over(col, a, wet, blood * (0.42 + 0.2 * sn.r), smoothstep(1.0, 0.7, st) * patchy * 0.9, 1.0 - soakGrow * 0.4);
                        // grazed, burnt skin just outside the tear (the entry's), the raw torn rim
                        if (m == 0) Over(col, a, wet, float3(0.2, 0.07, 0.05), smoothstep(rr * 1.45, rr * 1.12, d) * (0.45 + 0.3 * sn.b), 0.15);
                        Over(col, a, wet, raw * 0.8, smoothstep(rr * 1.16, rr * 1.0, d), 0.8);
                    }
                }

                // the drips: down from where they started, wavering a little, thinner as they go, a bead at the end
                int dc = (int)_DCount;
                [loop]
                for (int j = 0; j < dc; j++)
                {
                    float4 DA = _DA[j], DB = _DB[j], DC = _DC[j], DD = _DD[j];
                    float3 rel = p - DA.xyz;
                    float down = dot(rel, DB.xyz);
                    float len = DB.w;
                    float w0 = DA.w;
                    if (down < -w0 || down > len + w0 * 2.0) continue;
                    // (on this side of the body, near the skin it ran on)
                    if (abs(dot(rel, DD.xyz)) > 0.05 || dot(n, DD.xyz) < -0.1) continue;
                    float seed = DC.w;
                    float wav = (tex2Dlod(_SkinNoise, float4(down * 6.0 + seed, seed * 0.3, 0, 0)).a - 0.5) * 0.012 + (tex2Dlod(_NoiseTex, float4(down * 21.0 + seed, seed, 0, 0)).r - 0.5) * 0.003;
                    float lat = dot(rel, DC.xyz) - wav;
                    float taper = lerp(1.0, 0.55, saturate(down / max(len, 1e-4)));
                    float w = w0 * taper;
                    float body = smoothstep(w, w * 0.6, abs(lat)) * step(0.0, down) * step(down, len);
                    // the bead gathering at its end
                    float2 bead = float2(lat, (down - len) / 1.25);
                    float beadA = smoothstep(w0 * 0.95, w0 * 0.6, length(bead));
                    // (the top fades in from the wound's soak)
                    float cover = max(body, beadA) * smoothstep(-w0, w0 * 0.5, down);
                    float age = DD.w;
                    float dry = saturate((age - 20.0) / 40.0);
                    // (darker where it runs thick, down its middle)
                    float core = smoothstep(w, 0.0, abs(lat));
                    float3 dc3 = lerp(blood * lerp(0.72, 0.5, core), blood * float3(0.42, 0.28, 0.28), dry);
                    Over(col, a, wet, dc3, cover * 0.94, 1.0 - dry * 0.7);
                }

                if (a < 0.004) clip(-1);
                // lit as the skin round it is (the room's light, the main light), wet blood glinting
                float3 L = _WorldSpaceLightPos0.w > 0.5 ? normalize(_WorldSpaceLightPos0.xyz - p) : _WorldSpaceLightPos0.xyz;
                float3 amb = ShadeSH9(float4(n, 1.0));
                float diff = saturate(dot(n, L));
                float3 lit = col * (amb * 0.85 + _LightColor0.rgb * diff * 0.55 + 0.2);
                float3 h = normalize(L - v);
                float spec = pow(saturate(dot(n, h)), 70.0) * _Gloss + pow(1.0 - saturate(dot(n, -v)), 4.0) * 0.12;
                lit += spec * wet * (_LightColor0.rgb + amb * 0.5);
                fixed4 o = fixed4(lit, saturate(a));
                UNITY_APPLY_FOG(i.fogCoord, o.rgb);
                return o;
            }
            ENDCG
        }
    }
}
