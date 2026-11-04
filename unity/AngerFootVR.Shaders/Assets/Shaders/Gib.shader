// The flesh chunks and bone bits thrown out of a big wound (Blender-made models, no UVs): their texture mapped onto
// them from all three sides, darkened in their creases (the occlusion baked into their vertex colours), wet and glossy.
Shader "AngerFootVR/Gib"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _Gloss ("Gloss", Range(0, 1)) = 0.75
        _Scale ("Texture scale (per metre)", Float) = 14
        _SpecColor ("Specular", Color) = (0.6, 0.55, 0.55, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        CGPROGRAM
        #pragma surface surf BlinnPhong vertex:vert fullforwardshadows addshadow nolightmap nodynlightmap nodirlightmap nometa
        #pragma target 3.0
        sampler2D _MainTex;
        fixed4 _Color;
        half _Gloss;
        float _Scale;
        struct Input
        {
            float3 objPos;
            float3 objN;
            float4 color : COLOR;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.objPos = v.vertex.xyz;
            o.objN = v.normal;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            float3 w = pow(abs(IN.objN), 4.0);
            w /= max(dot(w, 1.0), 1e-4);
            float3 p = IN.objPos * _Scale;
            float3 c = tex2D(_MainTex, p.yz).rgb * w.x + tex2D(_MainTex, p.xz).rgb * w.y + tex2D(_MainTex, p.xy).rgb * w.z;
            o.Albedo = c * _Color.rgb * IN.color.rgb;
            o.Specular = 0.35;
            o.Gloss = _Gloss;
        }
        ENDCG
    }
}
