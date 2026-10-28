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

                // ③c 降对比 → 灰蒙蒙的雾感。
                //
                // ⚠️ 这里**不再额外抬黑位**（原来有 c.rgb += 0.045）。
                // 抬黑位等于给每个像素加一个常数 —— 纯黑区域被抬成灰，
                // 再叠上噪点就变成「飘着的白点」。降对比本身已经让画面发灰了。
                c.rgb = (c.rgb - 0.5) * lerp(1.0, 0.78, s) + 0.5;

                // ① 扫描线：更深的横纹，缓慢上滚
                float scanPos = uv.y * 360.0 + _Time.y * 10.0;
                float scan = 0.72 + 0.28 * sin(scanPos * 3.14159);
                c.rgb *= lerp(1.0, scan, s);

                // ①b 低分辨率块状感：把画面量化到稍低的网格，模拟老摄像头像素粗
                float2 grid = float2(480.0, 270.0);
                float2 q = floor(uv * grid) / grid;
                fixed3 coarse = tex2D(_MainTex, q).rgb * IN.color.rgb;
                c.rgb = lerp(c.rgb, coarse, 0.22 * s);

                // ② 颗粒噪点：逐像素 + 逐帧随机
                //
                // ⚠️ **必须用乘法，不能用加法**（踩过，表现为「暗部飘白粒子」）：
                // 加法是「往像素上加固定亮度」，画面几乎全黑的地方（天花板、黑箱）
                // 加 0.08 就成了孤立的白点，看起来像一片白粒子在飘。
                // 乘法是按比例扰动，黑色 × 任何值仍是黑色，只有本来有亮度的地方才见颗粒
                // —— 这才是真实传感器的噪声行为。
                float n = Hash(floor(uv * float2(960.0, 540.0)) + floor(_Time.y * 30.0));
                c.rgb *= 1.0 + (n - 0.5) * 0.30 * s;

                // ④ 暗角：到中心的距离越大越暗（更强）
                float2 d = uv - 0.5;
                float vig = 1.0 - dot(d, d) * 1.35 * s;
                c.rgb *= max(0.35, vig);

                // ⑥ 整体压暗一点点 —— 老监控屏不会那么"透亮"
                c.rgb *= lerp(1.0, 0.90, s);

                // ⑦ 轻微重影：往右侧偏移一点点再叠一层，模拟信号反射。
                // 同样是**乘法**叠加 —— 加法会让纯黑区域浮出一层灰白。
                fixed3 ghost = tex2D(_MainTex, uv + float2(0.0035, 0.0)).rgb * IN.color.rgb;
                c.rgb *= 1.0 + ghost * 0.10 * s;

                return c;
            }
            ENDCG
        }
    }
}
