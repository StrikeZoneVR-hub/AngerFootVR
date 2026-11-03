// Anger Foot VR's cartoon shading (the CARTOON SHADED style and the wardrobe's own bodies and gloves): each light gives
// a flat lit tone and a flat shade, ambient comes in flat, and a bold ink outline is drawn round the shape (the mesh
// pushed out along its normals, its back faces in ink: works on the GPU-skinned body as it moves).
Shader "AngerFootVR/Toon"
{
    Properties
    {
        _MainTex ("Colour", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Shade ("Shade (the dark side's brightness)", Range(0, 1)) = 0.6
        _Threshold ("Where light turns to shade", Range(-1, 1)) = 0.05
        _Soft ("Edge softness", Range(0.001, 0.5)) = 0.03
        _Gloss ("Gloss spot", Range(0, 1)) = 0.0
        _GlossSize ("Gloss spot size", Range(0.8, 0.999)) = 0.965
        _Flat ("Flat ambient", Range(0, 1)) = 0.6
        _OutlineColor ("Outline", Color) = (0.03, 0.02, 0.04, 1)
        _Outline ("Outline width (metres)", Range(0, 0.03)) = 0.006
        _VertexColour ("Use vertex colours", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        LOD 200

        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            fixed4 _OutlineColor;
            float _Outline;
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_FOG_COORDS(0) UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 wn = normalize(UnityObjectToWorldNormal(v.normal));
                o.pos = mul(UNITY_MATRIX_VP, float4(wp + wn * _Outline, 1.0));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = _OutlineColor;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }

        CGPROGRAM
        #pragma surface surf Toon fullforwardshadows addshadow
        #pragma target 3.0

        sampler2D _MainTex;
        fixed4 _Color;
        half _Shade, _Threshold, _Soft, _Gloss, _GlossSize, _Flat, _VertexColour;

        struct Input
        {
            float2 uv_MainTex;
            float4 color : COLOR;
        };

        half4 LightingToon(SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half ndl = dot(s.Normal, lightDir);
        #if defined(POINT) || defined(SPOT) || defined(POINT_COOKIE)
            half lit = smoothstep(_Threshold - _Soft, _Threshold + _Soft, ndl);
            half fall = smoothstep(0.05, 0.15, atten) * 0.4 + smoothstep(0.3, 0.6, atten) * 0.6;
            half tone = lerp(_Shade, 1.0, lit) * fall;
            lit *= fall;
        #else
            half lit = smoothstep(_Threshold - _Soft, _Threshold + _Soft, ndl) * smoothstep(0.15, 0.55, atten);
            half tone = lerp(_Shade, 1.0, lit);
        #endif
            half3 hv = lightDir + viewDir;
            half3 h = hv * rsqrt(max(dot(hv, hv), 1e-4));
            half spot = smoothstep(_GlossSize - 0.008, _GlossSize + 0.008, dot(s.Normal, h)) * _Gloss * lit;
            half4 c;
            c.rgb = (s.Albedo * tone + spot) * _LightColor0.rgb;
            c.a = s.Alpha;
            return c;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            c.rgb *= lerp(fixed3(1, 1, 1), IN.color.rgb, _VertexColour);
            o.Albedo = c.rgb;
            o.Alpha = c.a;
            o.Emission = c.rgb * ShadeSH9(half4(0, 1, 0, 1)) * _Flat * 0.35;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
