Shader "NailongFNAF/UnlitTexture"
{
    // 内置 Unlit/Texture 的项目内副本 —— 构建时必然进包。
    // 背景：Shader.Find("Unlit/Texture") 在发行版里可能返回 null（内置 shader
    // 没被引用时会被剔除），导致 OfficeRoom 的 new Material(null) 抛异常、办公室崩。
    // 用项目自带的 shader 就不会有这个问题。
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}

        // 可选色调：默认白色（不改变任何现有材质的外观），
        // 奶包那部手机靠它做「转到它 -> 亮起来」的高亮。
        _Color ("Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            fixed4 _Color;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
}
