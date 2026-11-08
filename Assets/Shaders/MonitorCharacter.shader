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
        // 切台撕裂强度 —— 和 MonitorCRT / MonitorSticker 同一份公式同一个值。
        // 立绘不跟着撕的话，切台瞬间就是「底图撕、人不动」的翻版。
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
            #pragma target 3.0     // ddx 算导数需要 SM3（见下面撕裂的换算）
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
            float4 _ScreenRect;
            float _Burst;

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

            // ⚠️ 和 MonitorCRT / MonitorSticker 里那份**逐字一致**。
            // 哈希不同 → 三层撕出来的行对不上，切台瞬间画面像碎成三截。
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // ⑥ 切台撕裂 —— 行号 / 阈值 / 幅度 / hash 参数和 MonitorCRT 逐字一致。
                //
                // 「屏幕 UV 偏移 → 贴图 UV 偏移」这里用**片元导数**换算：
                // ddx(texcoord.x) = 每个屏幕像素走多少 u，立绘占多宽不用 C# 传参，
                // 任何大小/位置的立绘都自动算对（插画层用的是另一套 _ScreenWidthRatio，
                // 那边是 QuadGraphic 四角自己算的，两边殊途同归）。
                // 每个屏幕像素走多少 u —— 线性插值，整个三角形是常数，
                // 所以先取导数再改 uv，不存在「对自己求导」的歧义。
                float duPerScreenPx = ddx(i.texcoord.x);

                float tearRow = floor(i.screenUV.y * 40.0);
                float tearTick = floor(_Time.y * 60.0);
                float tearSel = step(0.80, Hash(float2(tearRow, tearTick)));
                float tearScreen = tearSel * (Hash(float2(tearRow + 17.1, tearTick)) - 0.5) * 0.05 * _Burst;

                // ⚠️ 换算用**监控区宽**（_ScreenRect），不是整屏宽：
                // 底图/插画的撕裂位移都是「0.05 × 监控区在屏幕上的宽度」。
                // 非 16:9 时监控区两侧有黑边（f < 1），立绘按整屏算会多甩 1/f 倍 ——
                // 1280×960 窗口下差出 12px，人会和背景撕开（同「一动一静」那一类）。
                float rectW = _ScreenRect.z - _ScreenRect.x;
                float2 uv = i.texcoord;
                uv.x += tearScreen * rectW * duPerScreenPx;

                fixed4 c = tex2D(_MainTex, uv) * i.color;

                // 被撕裂甩开的行顺带提亮（和底图同参数）
                c.rgb *= 1.0 + tearSel * 0.18 * _Burst;

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

                return c;
            }
            ENDCG
        }
    }
}
