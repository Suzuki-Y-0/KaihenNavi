Shader "Hidden/KaihenNavi/InnerColorVisibility"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // 髪の裏面も遮る。片面の板で作られた髪でも、頭越しの票を数えない。
        Cull Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _KNViewTexture;
        float4 _MainTex_ST;
        float4x4 _KNView, _KNVP, _KNUVProjection, _KNHeadSpace;
        float _KNCutoff, _KNAlpha, _KNRear, _KNLowest, _KNEpsilon, _KNCameraSize;
        float4 _KNDir; // 頭からカメラへ向かう向き（ワールド）
        struct Input
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
            float2 sourceUV : TEXCOORD1;
            float3 normal : NORMAL;
        };
        struct Varying
        {
            float4 vertex : SV_POSITION;
            float2 uv : TEXCOORD0;
            float3 world : TEXCOORD1;
            float3 normal : TEXCOORD2;
        };
        Varying CameraVertex(Input v)
        {
            Varying o;
            o.world = v.vertex.xyz; // Bake 済みのワールド座標だけを受け取る。
            o.normal = v.normal;
            o.vertex = mul(_KNVP, v.vertex);
            o.uv = TRANSFORM_TEX(v.uv, _MainTex);
            return o;
        }
        Varying UVVertex(Input v)
        {
            Varying o;
            o.world = v.vertex.xyz;
            o.normal = v.normal;
            o.vertex = mul(_KNUVProjection, float4(v.uv, 0, 1));
            o.uv = v.sourceUV; // 透明部分は元の拡縮・移動を適用した座標で読む。
            return o;
        }
        void AlphaTest(float2 uv)
        {
            clip(tex2D(_MainTex, uv).a * _KNAlpha - _KNCutoff);
        }
        float4 CameraFragment(Varying i) : SV_Target
        {
            AlphaTest(i.uv);
            // RG=実際に見えた UV、B=線形深度、A=描画あり。深度テストで最前面を残す。
            return float4(frac(i.uv), -mul(_KNView, float4(i.world, 1)).z, 1);
        }
        float4 VoteFragment(Varying i) : SV_Target
        {
            float4 projected = mul(_KNVP, float4(i.world, 1));
            float2 screenUV = projected.xy / projected.w * 0.5 + 0.5;
            #if UNITY_UV_STARTS_AT_TOP
                screenUV.y = 1 - screenUV.y;
            #endif
            float depth = -mul(_KNView, float4(i.world, 1)).z;
            // 同じ斜面でも、画素中心と実際の位置では深度が違う。三角形の平面上で補正する。
            // 一律に深度の許容幅を増やすと、隣の房の外側にまで色が漏れるため。
            float2 du = ddx(screenUV), dv = ddy(screenUV);
            float dzdu = ddx(depth), dzdv = ddy(depth);
            float determinant = du.x * dv.y - du.y * dv.x;
            float2 slope = float2(dzdu * dv.y - dzdv * du.y, du.x * dzdv - dv.x * dzdu) /
                (abs(determinant) > 1e-12 ? determinant : 1);
            float2 sampleUV = (floor(screenUV * _KNCameraSize) + 0.5) / _KNCameraSize;
            depth += dot(slope, sampleUV - screenUV);
            float4 seen = tex2D(_KNViewTexture, screenUV);
            float inside = all(screenUV >= 0) && all(screenUV <= 1);
            // UV カメラ画像からの逆引き。カメラの画素に入らない細い毛も、UV の全テクセルで照合する。
            float visible = inside * seen.a * step(1e-12, abs(determinant)) * step(depth, seen.b + _KNEpsilon);
            AlphaTest(i.uv);
            // 面がこのカメラの方を向いているときだけ数える。向こう側を向いた面は、外側の面でも見えないのが当たり前なので、
            // 全方向で割ると外側の面まで「見えにくい＝内側」と判定されてしまう（まだらに色がつく原因）。
            float facing = step(0.05, dot(normalize(i.normal), _KNDir.xyz));
            return float4(visible * facing, visible * facing * _KNRear, facing, facing * _KNRear);
        }
        float4 ScopeFragment(Varying i) : SV_Target
        {
            AlphaTest(i.uv);
            float3 p = mul(_KNHeadSpace, float4(i.world, 1)).xyz;
            float back = saturate(-p.z / max(length(p), 0.000001) / 0.15);
            back = back * back * (3 - 2 * back);
            return float4(saturate(-p.y / max(-_KNLowest, 0.00001)), back, 1, 1);
        }
        float4 AddFragment(v2f_img i) : SV_Target { return tex2D(_MainTex, i.uv); }
        ENDCG

        // 0: アバター全体の深度と、外から見える UV。
        Pass
        {
            ZWrite On ZTest LEqual
            CGPROGRAM
            #pragma target 3.0
            #pragma editor_sync_compilation
            #pragma vertex CameraVertex
            #pragma fragment CameraFragment
            ENDCG
        }
        // 1: 1 方向につき 1 票。重複 UV は「どれかが外から見えた」側を優先する。
        Pass
        {
            ZWrite Off ZTest Always Blend One One BlendOp Max
            CGPROGRAM
            #pragma target 3.0
            #pragma editor_sync_compilation
            #pragma vertex UVVertex
            #pragma fragment VoteFragment
            ENDCG
        }
        // 2: 頭からの高さ・後ろ向きの条件。判定前の位置をテクセル単位で保存する。
        Pass
        {
            ZWrite Off ZTest Always
            CGPROGRAM
            #pragma target 3.0
            #pragma editor_sync_compilation
            #pragma vertex UVVertex
            #pragma fragment ScopeFragment
            ENDCG
        }
        // 3: 各方向の票を加算。読み戻しは最後の 1 回にまとめる。
        Pass
        {
            ZWrite Off ZTest Always Blend One One
            CGPROGRAM
            #pragma target 3.0
            #pragma editor_sync_compilation
            #pragma vertex vert_img
            #pragma fragment AddFragment
            ENDCG
        }
    }
    Fallback Off
}
