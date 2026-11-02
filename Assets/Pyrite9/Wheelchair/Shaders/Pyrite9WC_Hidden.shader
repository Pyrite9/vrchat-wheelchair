// Pyrite9 Wheelchair — 아무것도 그리지 않는 셰이더(AFK로 사라지는 동안 디졸브를 못 하는 슬롯에 끼운다, 1.1.2)
//   1.1.1은 lilToon Cutout + _Color 알파 0으로 숨겼는데, Light Limit Changer처럼 렌더러 단위로 'material._Color'를
//   애니메이션하는 도구가 알파를 1로 덮어 흰 조각이 남았다 → 어떤 속성에도 기대지 않는다:
//   정점을 한 점으로 모아(면적 0) 픽셀이 안 생기고, 그래도 색·깊이를 쓰지 않으며, 그림자 패스도 없다.
//   셰이더가 막힌 사람에겐 VRChat 대체 셰이더가 'Hidden'(안 그림)
Shader "Hidden/Pyrite9/Wheelchair/Invisible"
{
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" "IgnoreProjector"="True" "VRCFallback"="Hidden" }
        Pass
        {
            ZWrite Off
            ColorMask 0
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return float4(0, 0, 0, 1);
            }

            fixed4 frag() : SV_Target
            {
                discard;
                return 0;
            }
            ENDCG
        }
    }
}
