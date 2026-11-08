Shader "NailongFNAF/MonitorSticker"
{
    // 监控画面里**插画层**的材质 —— 唯一作用是跟着底图一起抖。
    //
    // ## 为什么需要它
    // 底图 <c>MonitorCRT.shader</c> 的抖动是**逐行 uv 偏移**：
    //     uv.x += lineJitter * 0.0016 * s;
    // 那是「采样点」在动，不是节点在动。插画层是另一个 QuadGraphic，
    // 挂在底图的**兄弟节点**下，完全不知道底图被抖了 ——
    // 结果就是「底图在抖、插画纹丝不动」（用户 2026-11-07 报的）。
    //
    // 整层位移（anchoredPosition）模拟不了：逐行随机、每行偏移都不同，
    // 最大 ±1.5px，大跳变可达 ±11.5px。
    //
    // ## 做法
    // 插画用**同一个抖动公式**，但换算方式最简单可靠：
    //   1. 用屏幕 UV 的行号算出「这一行应该偏移多少屏幕 UV」
    //   2. 把它换算成「这张贴图自己的 UV 偏移」= 偏移 / 图占屏幕宽度的比例
    //   3. _ScreenWidthRatio 由 C# 每帧传入（这张图在画面上占多宽）
    //
    // ⚠️ 行号必须用**屏幕 UV**，不能用贴图 UV。贴图 UV 的 y 是插画自己的 0~1，
    // 行号会和底图对不上，抖出来是两层互不相干的花纹 —— 更穿帮。
    //
    // ⚠️ 只抖**位置**，不抖颜色。色偏 / 扫描线 / 暗角底图已经做过一次，
    // 插画再压一遍会比分底图暗，看着像贴了块脏玻璃。

    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Strength ("Jitter Strength", Range(0, 1)) = 1
        // 这张图在画面上占多宽（0~1）。由 C# 每帧写，用来把「屏幕 UV 偏移」
        // 换算成「贴图 UV 偏移」。
        _ScreenWidthRatio ("Screen Width Ratio", Float) = 0.3
        // 切台撕裂强度 —— 必须和 MonitorCRT / MonitorCharacter 同一份公式、
        // 同一个值，否则切台时插画和立绘不跟着底图撕，就是「一动一静」那个 bug 的翻版。
        _Burst ("Tear Burst", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
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
                float2 screenUV : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Strength;
            float _ScreenWidthRatio;
            float _Burst;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;

                float4 sp = ComputeScreenPos(OUT.vertex);
                OUT.screenUV = sp.xy / sp.w;

                return OUT;
            }

            // ⚠️ 和 MonitorCRT.shader 里那份**逐字一致**。
            // 哈希函数不同的话两边抖出来的行对不上，看着像两层错位的重影。
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float s = _Strength;

                // 和底图一样的 1 秒渐入
                float warmup = saturate(_Time.y / 1.0);
                s *= lerp(0.35, 1.0, warmup);

                if (s <= 0.001)
                {
                    return tex2D(_MainTex, IN.texcoord) * IN.color;
                }

                // 用**屏幕 UV** 的行号 —— 和底图同一个坐标系，抖动才对得上
                float sy = IN.screenUV.y;

                // ⑤ 整行轻微左右抖动（和 MonitorCRT 同参数）
                float lineJitter = (Hash(float2(floor(sy * 240.0), floor(_Time.y * 18.0))) - 0.5);
                float offsetScreen = lineJitter * 0.0016 * s;

                // 偶尔一次大幅漂移
                float bigJump = step(0.985, Hash(float2(floor(_Time.y * 3.0), 11.0)));
                offsetScreen += bigJump * (Hash(float2(floor(sy * 60.0), floor(_Time.y * 20.0))) - 0.5) * 0.012 * s;

                // ⑥ 切台撕裂 —— 行号、阈值、幅度、hash 参数全部和 MonitorCRT 逐字一致。
                // 这里用**屏幕 UV** 的行号（底图用的是它自己的贴图 uv.y，
                // 满屏时两者相等；非 16:9 有黑边时本来就有同样的偏差，
                // 和 lineJitter 保持一致，不另立标准）。
                float tearRow = floor(sy * 40.0);
                float tearTick = floor(_Time.y * 60.0);
                float tearSel = step(0.80, Hash(float2(tearRow, tearTick)));
                offsetScreen += tearSel * (Hash(float2(tearRow + 17.1, tearTick)) - 0.5) * 0.05 * _Burst;

                // 屏幕 UV 偏移 → 贴图 UV 偏移（除以图占屏幕宽度的比例）
                float2 uv = IN.texcoord;
                uv.x += offsetScreen / max(0.0001, _ScreenWidthRatio);

                return tex2D(_MainTex, uv) * IN.color;
            }
            ENDCG
        }
    }
}
