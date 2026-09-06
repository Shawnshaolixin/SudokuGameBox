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
        _MaskST ("液块UV平移(xy)缩放(zw)", Vector) = (0, 0, 1, 1)
        _MaskRot ("旋转补偿(xy=cos,sin;zw=液块底部中心UV)", Vector) = (1, 0, 0, 0)
        _MaskAspect ("管屏幕宽高比(w/h,旋转换算到等比空间)", Float) = 4.6
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
            // 倒水倾斜的「水面水平」补偿:液块矩形在试管内反向旋转保持屏幕水平后,
            // 其 UV 采样框相对试管转了 -θ;片元把 UV 绕液块中心反旋转回试管空间再采样,
            // 裁剪边界即贴合倾斜后的内腔剪影(推导见 WaterSortTubeRack.AnimatePourRotate)。
            // xy=(cosθ, sinθ)(θ=试管根节点旋转角),zw=液块中心(遮罩UV空间);默认恒等。
            float4 _MaskRot;
            float _MaskAspect;
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
                // 旋转补偿(_MaskRot 恒等时零开销等价):把片元 UV 偏移绕液块底部中心旋 -θ
                // 回试管空间。⚠ 旋转必须在等比空间做 —— 试管 96×400(1:4.2),归一化 UV 空间里
                // 的 32° 不是屏幕上的 32°,直接转会把采样区域扭得完全错位(液体露成方块/全消失)。
                // 故 x 先乘管屏幕宽高比换算到「管高为单位」的等比空间,旋转后再换算回来。
                // 采样 UV 夹回贴图范围:倾斜动画期液块加宽越出 [0,1],遮罩贴图是 Repeat 包装,
                // 越界会回卷出错误 alpha;遮罩四边空白(alpha=0),夹回即等效全裁。
                float2 o = IN.maskUV - _MaskRot.zw;
                o = float2(o.x * _MaskAspect, o.y);                   // → 等比空间(管高单位)
                o = float2(o.x * _MaskRot.x + o.y * _MaskRot.y,
                           o.y * _MaskRot.x - o.x * _MaskRot.y);      // Rot(-θ)
                o = float2(o.x / _MaskAspect, o.y);                   // → UV 空间
                half m = tex2D(_MaskTex, clamp(_MaskRot.zw + o, 0.002, 0.998)).a;
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
