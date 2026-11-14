Shader "NailongFNAF/ScreenPost"
{
    // 方案 A：全屏氛围后处理。
    //
    // 三个 pass：
    //   0. BloomPrefilter —— 按亮度阈值把亮部单独抽出来（降半分辨率跑）
    //   1. BloomBlur      —— 可分离高斯模糊（H / V 各跑一次）
    //   2. Composite      —— 叠 Bloom + 暗角 + 颗粒 + 色差 + 对比度/饱和度
    //
    // ⚠️ 这只作用于**相机画面**；Screen Space Overlay 的 UGUI（HUD / 监控层）
    //    不会被 OnRenderImage 抓到，所以 HUD 保持清晰 —— 这正是我们要的。
    //
    // ⚠️ 不要在这里加「扫描线 / 雪花」：那是监控层 MonitorCRT / MonitorStatic 的活。
    //    两层混在一起会互相打架，调起来也更难。
    //
    // 参数默认值走「轻微电影感」：Bloom 0.35、暗角 0.28、颗粒 0.015、色差 0.0008。

    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}

        _BloomIntensity ("Bloom Intensity", Range(0, 2)) = 0.35
        _Threshold ("Bloom Threshold", Range(0, 2)) = 0.65

        _Vignette ("Vignette", Range(0, 1)) = 0.28
        _Grain ("Grain", Range(0, 0.2)) = 0.015
        _Chroma ("Chromatic Aberration", Range(0, 0.01)) = 0.0008
        _Contrast ("Contrast", Range(0.5, 1.5)) = 1.04
        _Saturation ("Saturation", Range(0, 2)) = 1.05

        _Intensity ("Intensity", Range(0, 1)) = 0.85
        _TimeSeed ("Time Seed", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _BloomTex;

    float _BloomIntensity;
    float _Threshold;
    float _Vignette;
    float _Grain;
    float _Chroma;
    float _Contrast;
    float _Saturation;
    float _Intensity;
    float _TimeSeed;

    float4 _BlurDir;

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv  : TEXCOORD0;
    };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        return o;
    }

    float Luma(float3 c)
    {
        return dot(c, float3(0.2126, 0.7152, 0.0722));
    }

    // ── Pass 0：按亮度阈值提取亮部 ──
    fixed4 fragPrefilter(v2f i) : SV_Target
    {
        fixed3 c = tex2D(_MainTex, i.uv).rgb;
        float l = Luma(c);
        float bright = max(l - _Threshold, 0.0) / max(l, 0.0001);
        return fixed4(c * bright, 1.0);
    }

    // ── Pass 1：可分离高斯（5 tap）──
    fixed4 fragBlur(v2f i) : SV_Target
    {
        float2 step = _BlurDir.xy;

        fixed3 sum = tex2D(_MainTex, i.uv).rgb * 0.227027;
        sum += tex2D(_MainTex, i.uv + step * 1.384615).rgb * 0.316216;
        sum += tex2D(_MainTex, i.uv - step * 1.384615).rgb * 0.316216;
        sum += tex2D(_MainTex, i.uv + step * 3.230769).rgb * 0.070270;
        sum += tex2D(_MainTex, i.uv - step * 3.230769).rgb * 0.070270;

        return fixed4(sum, 1.0);
    }

    // ── Pass 2：合成 ──
    fixed4 fragComposite(v2f i) : SV_Target
    {
        float2 uv = i.uv;
        float2 d = uv - 0.5;

        fixed3 baseCol = tex2D(_MainTex, uv).rgb;

        // 色差：R / B 沿径向错开一点，G 保持原位
        fixed3 ca;
        ca.r = tex2D(_MainTex, uv - d * _Chroma).r;
        ca.g = baseCol.g;
        ca.b = tex2D(_MainTex, uv + d * _Chroma).b;

        fixed3 bloom = tex2D(_BloomTex, uv).rgb * _BloomIntensity;
        fixed3 col = ca + bloom;

        // 对比度 / 饱和度（轻微即可，别做成滤镜）
        col = (col - 0.5) * _Contrast + 0.5;
        float gray = Luma(col);
        col = lerp(gray.xxx, col, _Saturation);

        // 暗角：四角压暗，中心不动
        float aspect = _ScreenParams.x / max(_ScreenParams.y, 1.0);
        float2 vd = (uv - 0.5) * float2(aspect, 1.0);
        float dist = length(vd);
        float vig = saturate(1.0 - _Vignette * smoothstep(0.25, 0.95, dist));
        col *= vig;

        // 颗粒：逐像素 hash，随时间滚动
        float n = frac(sin(dot(uv * _ScreenParams.xy + _TimeSeed,
            float2(12.9898, 78.233))) * 43758.5453);
        col += (n - 0.5) * _Grain;

        col = lerp(baseCol, col, _Intensity);
        return fixed4(col, 1.0);
    }
    ENDCG

    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragPrefilter
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragBlur
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragComposite
            ENDCG
        }
    }

    Fallback Off
}