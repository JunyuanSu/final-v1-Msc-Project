Shader "PortOfDover/StereoObservationFade"
{
 Properties { _Alpha("Opacity",Range(0,1))=0 }
 SubShader {
 Tags {"Queue"="Overlay+1000" "RenderType"="Transparent"}
 Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
 Pass {
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "UnityCG.cginc"
 struct appdata {float4 vertex:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID};
 struct v2f {float4 vertex:SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO};
 float _Alpha;
 v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_OUTPUT(v2f,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.vertex=float4(v.vertex.xy,0,1);return o;}
 fixed4 frag(v2f i):SV_Target {UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);return fixed4(0,0,0,_Alpha);}
 ENDCG
 }
 }
}
