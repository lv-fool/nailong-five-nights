Shader "NailongFNAF/RedSpread"
{
    // 跳杀结束后的红屏：**红色从画面边缘向中心蔓延**，最终铺满全屏。
    //
    // 和边缘红脉冲用的是同一种形状语言（四周先红、中间最后），
    // 所以玩家看到它时会觉得是「刚才那个提示的加强版」。
    //
    // 做法：算每个像素到最近边的归一化距离 d（0=贴边，1=正中），
    // 用 _Spread 当阈值往前推 —— spread 涨到 1.2 时中心也被吞掉。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Spread ("Spread (0=全黑, 1.2=全红)", Range(0, 1.5)) = 0
        _Softness ("Softness", Range(0.005, 0.6)) = 0.16
        _Red ("Red Color", Color) = (0.72, 0.03, 0.06, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            fixed4 _Color;
            fixed4 _Red;
            float _Spread;
            float _Softness;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 到最近一条边的距离，映射到 0..1：贴边=0，正中=1
                float2 uv = i.texcoord;
                float d = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)) * 2.0;

                // spread 越过 d 的地方就是红的；_Softness 给一点过渡边
                float a = saturate((_Spread - d) / max(_Softness, 0.001));

                // 黑 → 红：底色始终不透明，所以开局是纯黑屏
                float3 col = lerp(float3(0.0, 0.0, 0.0), _Red.rgb, a);

                return fixed4(col, i.color.a);
            }
            ENDCG
        }
    }
}
