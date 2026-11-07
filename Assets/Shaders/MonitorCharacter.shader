Shader "NailongFNAF/MonitorCharacter"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Occluder ("Occluder Mask", 2D) = "black" {}
        _UseOccluder ("Use Occluder", Float) = 0
        _OccluderRect ("Occluder Rect", Vector) = (0,0,1,1)
        _DebugMode ("Debug", Float) = 0
        // 可视区：**屏幕像素矩形**（xy = 左下，zw = 右上）。
        // 立绘超出监控画面框的部分直接丢弃 —— 敌人半个身子探出画面外时，框外那截不能画。
        // 关闭裁剪：设成 (-10000,-10000,10000,10000) 即可。
        _ScreenRect ("Screen Rect (px)", Vector) = (-10000,-10000,10000,10000)
        // 劣化强度 —— 必须和底图 MonitorCRT 的 _Strength 取**同一个值**，
        // 否则敌人和背景会有两套不同的脏法，看着还是贴纸（见 MonitorCrtStrength）。
        // 0 = 不劣化（编辑器调点位时用）。
        _CrtStrength ("CRT Degradation", Range(0, 1)) = 1
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
                float2 screenUV : TEXCOORD1;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _Occluder;
            fixed4 _Color;
            float _UseOccluder;
            float4 _OccluderRect;
            float _DebugMode;
            float _CrtStrength;
            float4 _ScreenRect;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                // ⚠️ 裁剪用**屏幕像素坐标**，不用任何父子节点坐标。
                //
                // 踩过的坑（2026-10-25，前后四版）：拿 anchoredPosition 当父坐标、
                // 拿世界坐标、拿 TransformPoint 反算 —— 都会因为
                // 父节点 pivot 在画面下沿 / canvas scaleFactor / 中间层偏移
                // 而对不上，表现是敌人被削掉半截。
                // 屏幕坐标只有一个参照系：画面框的像素矩形由 C# 算好传进来，
                // shader 只负责把当前片元换到同一把尺上 —— 零歧义。
                float4 sp = ComputeScreenPos(OUT.vertex);
                OUT.screenUV = sp.xy / sp.w;
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 和 MonitorCRT.shader 里那份**一模一样**的哈希，别各写各的 ——
            // 两边噪点分布对不上时，敌人和背景的颗粒会像两层不同的膜。
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.texcoord) * i.color;

                // 屏幕 UV（0..1，左下原点）→ 像素
                float2 px = i.screenUV * _ScreenParams.xy;

                // 可视区裁剪：超出画面框像素矩形的像素整个丢弃。
                // 敌人立在监控画面边缘时，探出画面框的那截就被切掉。
                //
                // _DebugMode==2：不裁，改把屏幕像素画成颜色
                // （红=每 50px 一道条码，绿=y 在画面框里的归一化位置），
                // 用来肉眼核对坐标系对不对。
                if (_DebugMode > 1.5)
                {
                    float bands = step(0.5, frac(px.x / 50.0));
                    float ny = (px.y - _ScreenRect.y) / max(1.0, _ScreenRect.w - _ScreenRect.y);
                    return fixed4(bands, frac(ny), 0, 1);
                }

                clip(px.x - _ScreenRect.x);
                clip(px.y - _ScreenRect.y);
                clip(_ScreenRect.z - px.x);
                clip(_ScreenRect.w - px.y);

                float2 suv = _OccluderRect.xy + i.texcoord * _OccluderRect.zw;

                if (_DebugMode > 0.5)
                {
                    fixed dbg = tex2D(_Occluder, suv).r;
                    return fixed4(dbg, dbg, dbg, 1);
                }

                if (_UseOccluder > 0.5)
                {
                    fixed occ = tex2D(_Occluder, suv).r;

                    // ⚠️ **硬边**：超过阈值就整个像素不画，不留半透明过渡。
                    // 不做这步的话，纹理过滤会让边缘出现 0~1 之间的中间值，
                    // 结果是被家具挡住的边缘呈半透明 —— 看起来像玻璃，穿帮。
                    // 用 step 硬切，边缘就是干净的「画 / 不画」。
                    occ = step(0.5, occ);

                    // 直接丢弃像素（比让 alpha=0 更彻底：alpha=0 在某些混合模式下
                    // 仍会参与写入；clip 是彻底不产生片元）
                    // occ = 1 → clip(-0.5) 丢弃；occ = 0 → clip(0.5) 保留
                    clip(0.5 - occ);
                }

                // ─────────────────────────────────────────────────────────
                // 监控劣化 —— 和底图 MonitorCRT.shader **同一套公式、同一个强度值**。
                //
                // 为什么不各调各的：玩法上敌人是「贴在监控画面里」的，
                // 底图已经压过一遍饱和 / 对比、加过扫描线和噪点，
                // 敌人如果保持原样，就会像一张高清贴纸浮在糊画面上 ——
                // 2026-11-07 用户反馈「画风有点草率」，根因就是这个。
                //
                // ⚠️ 这里的 uv 用 **screenUV**，不是 texcoord。
                // texcoord 是立绘自己的 0~1，扫描线会跟着立绘走 ——
                // 立绘动的时候横纹也跟着动，一眼假。
                // screenUV 是整屏的，横纹才会像「打在屏幕上」。
                // ─────────────────────────────────────────────────────────
                float crt = saturate(_CrtStrength);
                if (crt > 0.001)
                {
                    // 和底图一样有个 1 秒渐入，避免刚进 Play 时脏得太突然
                    float warmup = saturate(_Time.y / 1.0);
                    float cs = crt * lerp(0.35, 1.0, warmup);
                    float2 suv = i.screenUV;

                    // ③ 色偏（压红提绿蓝）
                    c.r *= lerp(1.0, 0.82, cs);
                    c.g *= lerp(1.0, 1.08, cs);
                    c.b *= lerp(1.0, 1.04, cs);

                    // ③b 降饱和
                    float lum = dot(c.rgb, float3(0.299, 0.587, 0.114));
                    c.rgb = lerp(c.rgb, lum.xxx, 0.34 * cs);

                    // ③c 降对比
                    c.rgb = (c.rgb - 0.5) * lerp(1.0, 0.78, cs) + 0.5;

                    // ① 扫描线（跟着屏幕走，不跟立绘走）
                    float scanPos = suv.y * 360.0 + _Time.y * 10.0;
                    float scan = 0.72 + 0.28 * sin(scanPos * 3.14159);
                    c.rgb *= lerp(1.0, scan, cs);

                    // ② 颗粒噪点（乘法，理由见 MonitorCRT：加法会让纯黑处飘白点）
                    float n = Hash(floor(suv * _ScreenParams.xy / 2.0)
                                   + floor(_Time.y * 30.0));
                    c.rgb *= 1.0 + (n - 0.5) * 0.30 * cs;

                    // ④ 暗角（按整屏算，和底图对齐）
                    float2 dv = suv - 0.5;
                    float vig = 1.0 - dot(dv, dv) * 1.35 * cs;
                    c.rgb *= max(0.35, vig);

                    // ⑥ 整体压暗
                    c.rgb *= lerp(1.0, 0.90, cs);
                }

                return c;
            }
            ENDCG
        }
    }
}
