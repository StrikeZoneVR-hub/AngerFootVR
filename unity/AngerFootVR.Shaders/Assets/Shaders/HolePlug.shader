// A cartoon hole's plug: invisible, it only writes depth, after the level and before the goon it's in, so the goon's
// own drawing is held back there and what's behind it shows through (the goon's mesh is never touched).
Shader "AngerFootVR/HolePlug"
{
    SubShader
    {
        Tags { "Queue" = "Geometry+1" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Pass
        {
            ColorMask 0
            ZWrite On
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target { return 0; }
            ENDCG
        }
    }
}
