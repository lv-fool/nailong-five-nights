Shader "NailongFNAF/MonitorStatic"
{
    // 监控画面的「雪花 / 干扰」效果 —— 程序化生成，不需要任何贴图。
    //
    // 三层叠加：
    //   1. 每帧随机灰点（雪花）—— hash(uv + time) 逐像素随机
    //   2. 横向滚动扫描线 —— 暗带从上往下滚，老式 CRT 的味道
    //   3. 随机横向撕裂 —— 某几行更亮
    //
    // _Strength 控制总强度：切机位的一瞬推到 1，敌人挪窝时推 0.5，
    // 之后衰减回 0。**强度只走 alpha**，颜色始终是压暗的噪点 ——
    // 底下的机位画面要一直透得出来，不能糊成一片白。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TimeScale ("Time Scroll Speed", Float) = 37.0
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

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _TimeScale;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            // 伪随机 hash：输入两个数，输出 0..1
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                // 强度走 **顶点色 alpha**（C# 每帧写 RawImage.color.a）。
                //
                // ⚠️ 不要改回自定义 uniform（`_Strength`）：试过，设了
                // SetFloat 材质上读得到、渲染时却是 0，整层透明 ——
                // 顶点色是 UI 一定会传的通道，绕开这个坑。
                float strength = IN.color.a;

                float t = _Time.y * _TimeScale;

                // ① 雪花：逐像素随机灰度（0.14 ~ 0.76）——
                //    纯白噪点糊在深色监控画面上会整片发白，看着像过曝；
                //    但压得太狠又看不见颗粒，这个范围是两版试出来的中间值。
                float n = Hash(floor(IN.texcoord * float2(640, 360)) + floor(t));
                float snow = n * 0.62 + 0.14;

                // ② 扫描线：几条暗带从上往下滚
                float scan = 0.75 + 0.25 * sin(IN.texcoord.y * 240.0 + _Time.y * 9.0);

                // ③ 随机横向撕裂：某几行水平错位（这几行稍亮一点，才看得出「撕」）
                float row = floor(IN.texcoord.y * 90.0);
                float tear = step(0.975, Hash(float2(row, floor(_Time.y * 11.0))));

                float3 col = snow * scan * (1.0 + tear * 0.45);

                // ④ alpha 上限 0.44：满强度也只盖住四成多，
                //    机位画面一直隐约透出来，像隔着干扰看监控，
                //    而不是把画面糊成一片白。
                //
                //    ⚠️ 早先的写法是 lerp(纯白底图, 雪花, 强度)：
                //    这一层的底图其实是 whiteTexture，等于「白色盖在监控上」，
                //    强度一高整屏发白 —— 雪花「太亮」就是这么来的。
                float alpha = strength * 0.44;

                return fixed4(col, alpha);
            }
            ENDCG
        }
    }
}
