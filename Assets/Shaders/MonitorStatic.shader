Shader "NailongFNAF/MonitorStatic"
{
    // 监控画面的「雪花 / 干扰」效果 —— 程序化生成，不需要任何贴图。
    //
    // 三层叠加：
    //   1. 每帧随机灰点（雪花）—— hash(uv + time) 逐像素随机
    //   2. 横向滚动扫描线 —— 暗带从上往下滚，老式 CRT 的味道
    //   3. 整体闪烁 —— 亮度随时间小幅抖动
    //
    // _Strength 控制总强度：切机位的一瞬推到 1，平时 0，
    // 用一个参数就能做出「一切换就炸雪花然后平息」的经典过渡。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Strength ("Static Strength", Range(0, 1)) = 0
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
            float _Strength;
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
                // 底图（机位画面）本身
                fixed4 baseColor = tex2D(_MainTex, IN.texcoord) * IN.color;

                if (_Strength <= 0.001)
                {
                    return baseColor;
                }

                float t = _Time.y * _TimeScale;

                // ① 雪花：逐像素随机灰度
                float n = Hash(floor(IN.texcoord * float2(640, 360)) + floor(t));
                float3 snow = n.xxx;

                // ② 扫描线：几条暗带从上往下滚
                float scan = 0.75 + 0.25 * sin(IN.texcoord.y * 240.0 + _Time.y * 9.0);

                // ③ 随机横向撕裂：某几行水平错位
                float row = floor(IN.texcoord.y * 90.0);
                float tear = step(0.975, Hash(float2(row, floor(_Time.y * 11.0))));

                // 合成：按强度把雪花/撕裂混进底图
                fixed4 result = baseColor;

                // 撕裂行：把该行换成更亮的雪花
                result.rgb = lerp(result.rgb, snow * 1.15, tear * _Strength);

                // 雪花：强度高时占比大；切机位的一瞬基本全是雪花
                float snowMix = _Strength * (0.35 + 0.65 * _Strength);
                result.rgb = lerp(result.rgb, snow * scan, snowMix);

                // 扫描线压暗
                result.rgb *= lerp(1.0, scan, 0.5 * _Strength);

                // 闪烁
                result.rgb *= 1.0 + (Hash(float2(floor(_Time.y * 24.0), 7.0)) - 0.5) * 0.22 * _Strength;

                return result;
            }
            ENDCG
        }
    }
}
