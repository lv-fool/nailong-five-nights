Shader "NailongFNAF/MonitorCRT"
{
    // 监控画面的「劣质 CRT」效果。
    //
    // 一层 shader 里叠五种劣化，全部程序化、无贴图依赖：
    //   1. 扫描线      —— 隔行压暗，老监视器的横纹
    //   2. 颗粒噪点    —— 逐像素随机亮度扰动，随时间滚动
    //   3. 色偏（青绿）—— 老 CCD 摄像头偏冷偏绿
    //   4. 暗角        —— 四角压暗，镜头边缘失光
    //   5. 抖动/漂移   —— 整行轻微左右抖动，模拟信号不稳
    //
    // _Strength 是总开关：0 = 原图原样（编辑器调点位时关掉最清楚），
    // 1 = 正常游玩强度。

    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Strength ("Degradation", Range(0, 1)) = 1
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

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                // 开局 1 秒内让劣化强度渐入 —— 刚进 Play 时 _Time.y 很小，
                // 所有基于它的 hash 都还不稳定，直接满强度会看着"脏得突然"。
                float warmup = saturate(_Time.y / 1.0);
                float s = _Strength * lerp(0.35, 1.0, warmup);

                if (s <= 0.001)
                {
                    return tex2D(_MainTex, uv) * IN.color;
                }

                // ⑤ 整行轻微左右抖动（信号不稳）
                float lineJitter = (Hash(float2(floor(uv.y * 240.0), floor(_Time.y * 18.0))) - 0.5);
                uv.x += lineJitter * 0.0016 * s;

                // 偶尔一次较大幅度的整行漂移（概率低，模拟信号跳变）
                float bigJump = step(0.985, Hash(float2(floor(_Time.y * 3.0), 11.0)));
                uv.x += bigJump * (Hash(float2(floor(uv.y * 60.0), floor(_Time.y * 20.0))) - 0.5) * 0.012 * s;

                fixed4 c = tex2D(_MainTex, uv) * IN.color;

                // ③ 色偏：明显压红、提绿蓝 → 老 CCD 的青绿味
                c.r *= lerp(1.0, 0.82, s);
                c.g *= lerp(1.0, 1.08, s);
                c.b *= lerp(1.0, 1.04, s);

                // ③b 大幅降饱和（劣质摄像头几乎没有颜色）
                float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
                c.rgb = lerp(c.rgb, lum.xxx, 0.34 * s);

                // ③c 降对比 + 抬黑位 → 灰蒙蒙的雾感
                c.rgb = (c.rgb - 0.5) * lerp(1.0, 0.78, s) + 0.5;
                c.rgb += 0.045 * s;

                // ① 扫描线：更深的横纹，缓慢上滚
                float scanPos = uv.y * 360.0 + _Time.y * 10.0;
                float scan = 0.72 + 0.28 * sin(scanPos * 3.14159);
                c.rgb *= lerp(1.0, scan, s);

                // ①b 低分辨率块状感：把画面量化到稍低的网格，模拟老摄像头像素粗
                float2 grid = float2(480.0, 270.0);
                float2 q = floor(uv * grid) / grid;
                fixed3 coarse = tex2D(_MainTex, q).rgb * IN.color.rgb;
                c.rgb = lerp(c.rgb, coarse, 0.22 * s);

                // ② 颗粒噪点：逐像素 + 逐帧随机（比之前明显）
                float n = Hash(floor(uv * float2(960.0, 540.0)) + floor(_Time.y * 30.0));
                c.rgb += (n - 0.5) * 0.16 * s;

                // ②b 偶发的亮噪点（信号毛刺）
                //
                // ⚠️ 三个约束都必要：
                //   · 概率压到 0.2%（step 阈值 0.998 → 0.998）
                //   · 亮度减半（0.55 → 0.28）—— 一整片 0.55 的白点很扎眼
                //   · **开局前 2 秒不闪**：刚编译完 _Time.y 从 0 起算，
                //     头几帧 hash 分布还没"散开"，看着就是一片白粒子飘过。
                float sparkAge = step(2.0, _Time.y);
                float spark = step(0.998, Hash(floor(uv * float2(360.0, 200.0)) + floor(_Time.y * 24.0)));
                c.rgb += spark * 0.28 * s * sparkAge;

                // ④ 暗角：到中心的距离越大越暗（更强）
                float2 d = uv - 0.5;
                float vig = 1.0 - dot(d, d) * 1.35 * s;
                c.rgb *= max(0.35, vig);

                // ⑥ 整体压暗一点点 —— 老监控屏不会那么"透亮"
                c.rgb *= lerp(1.0, 0.90, s);

                // ⑦ 轻微重影：往右侧偏移一点点再叠一层，模拟信号反射
                fixed3 ghost = tex2D(_MainTex, uv + float2(0.0035, 0.0)).rgb * IN.color.rgb;
                c.rgb += ghost * 0.13 * s;

                return c;
            }
            ENDCG
        }
    }
}
