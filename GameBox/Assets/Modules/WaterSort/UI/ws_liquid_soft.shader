// 水排序液体软裁剪 shader(替代 UGUI Mask 模板硬裁剪)。
// 背景:UGUI Mask 走 stencil,裁剪边是几何硬边且无抗锯齿,底弧强曲线处呈
// 像素阶梯;本 shader 改为逐像素采样内腔剪影遮罩(ws_tube_mask)的 alpha,
// 经 smoothstep 软阈值过渡 —— 剪影边缘自带 1~2px 羽化,弧线裁剪边天然平滑。
//
// UV 映射:液块是管矩形内的子矩形(纯色 quad,texcoord 0..1 为液块自身),
// 由 WaterSortTubeRack 按液块在管内的归一化位置写入材质向量 _MaskST
// (x,y=左下角平移,z,w=宽高缩放),把液块 UV 映射到遮罩贴图空间。
// 结构照抄内置 UI-Default(含 Stencil/_ClipRect 分支),保证与 UGUI
// RectMask2D/父级 Mask 及画布裁剪的兼容性不回退。
Shader "Box/UI/WaterSortLiquid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _MaskTex ("内腔剪影遮罩(仅用 alpha)", 2D) = "white" {}
        _MaskST ("遮罩UV平移(xy)缩放(zw)", Vector) = (0, 0, 1, 1)
        _EdgeLo ("软边下限(遮罩alpha)", Float) = 0.05
        _EdgeHi ("软边上限(遮罩alpha)", Float) = 0.5
        _Color ("Tint", Color) = (1, 1, 1, 1)

        // 保留 UGUI 标准模板参数(父级 Mask/RectMask2D 会按需改写)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_CLIP_RECT)] _UseUIClipRect ("Use UI Clip Rect", Float) = 1
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use UI Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

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
                float2 maskUV   : TEXCOORD1;   // 遮罩空间 UV(经 _MaskST 映射)
                float4 worldPosition : TEXCOORD2;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;

            sampler2D _MaskTex;
            float4 _MaskST;
            float _EdgeLo;
            float _EdgeHi;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.maskUV = _MaskST.xy + v.texcoord * _MaskST.zw;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;

                // 内腔软裁剪:遮罩 alpha 在 [EdgeLo, EdgeHi] 区间平滑重映射。
                // 剪影贴图边缘有 1~2px 羽化,此处过渡带完全落在羽化内 → 抗锯齿;
                // EdgeHi 以下全裁、以上全留,管内主体 alpha 不受影响。
                half m = tex2D(_MaskTex, IN.maskUV).a;
                color.a *= smoothstep(_EdgeLo, _EdgeHi, m);

                #ifdef UNITY_UI_CLIP_RECT
                float mask = UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                color.a *= mask;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
