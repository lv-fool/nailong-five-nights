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
        // 可视区（本地坐标，xy=min, zw=max）。立绘超出监控画面框的
        // 部分直接丢弃像素 —— 敌人半个身子探出画面外时，框外那截不能画。
        // 关闭裁剪：设成一个巨大范围（如 -10000,10000）即可。
        _ViewRect ("View Rect (local min/max)", Vector) = (-10000,-10000,10000,10000)
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
                float2 parentPos : TEXCOORD1;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            sampler2D _Occluder;
            fixed4 _Color;
            float _UseOccluder;
            float4 _OccluderRect;
            float _DebugMode;
            float4 _ViewRect;
            float4 _SelfScale;   // xy = 本地缩放（立绘 rect.localScale）
            float4 _SelfPos;     // xy = 本地位置（rect.anchoredPosition）

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                // ⚠️ 裁剪不用世界坐标 —— Screen Space Overlay 的
                // unity_ObjectToWorld 尺度特殊（含 canvas scaleFactor），
                // 和 GetWorldCorners 对不上（踩过）。
                //
                // 改用 **shader 自算的"父节点坐标"**：
                //   parent = localPos * localScale + localPosition
                // UGUI 元素没有旋转，这个公式精确成立。
                // _ViewRect 由 C# 给出：Area 本地系（即设计区 1920×1080）。
                // 材质上另设 _SelfScale / _SelfPos 每帧同步。
                OUT.parentPos = v.vertex.xy * _SelfScale.xy + _SelfPos.xy;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.texcoord) * i.color;

                // 可视区裁剪：父节点坐标超出 _ViewRect 的像素整个丢弃。
                // 敌人立在监控画面边缘时，探出画面框的那截就被切掉。
                clip(i.parentPos.x - _ViewRect.x);
                clip(i.parentPos.y - _ViewRect.y);
                clip(_ViewRect.z - i.parentPos.x);
                clip(_ViewRect.w - i.parentPos.y);

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
