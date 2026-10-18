// Pyrite9 Wheelchair — 휠체어 일습 루트 표시 + 메뉴 언어 + 수동 보정 값
//   NDMF 플러그인(Editor/WheelchairPlugin.cs)이 이 컴포넌트로 휠체어를 찾는다 — 오브젝트 이름·위치는 자유.
//   빌드 때 자동 맞춤(fitA 배율·위치·센서, fitB 좌석 폭·발판·좌석 관통 시 몸 올림) 뒤에 아래 보정 값을 더한다. 값은 아바타 기준 미터.
//   IEditorOnly — 업로드되는 아바타에는 남지 않는다(플러그인이 맞춤 뒤 지운다). 인스펙터 문구는 배포용이라 영어.
using UnityEngine;

namespace Pyrite9.Wheelchair
{
    public enum MenuLanguage { English = 0, Japanese = 1, Korean = 2 }

    [DisallowMultipleComponent]
    [AddComponentMenu("Pyrite9/Wheelchair Root")]
    public class WheelchairRoot : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        [Tooltip("Language of the Expression Menu labels (applied at build time).")]
        public MenuLanguage menuLanguage = MenuLanguage.English;

        [Tooltip("Measure the avatar at build time and fit the wheelchair to it. Turn off to keep the prefab's own position and size.")]
        public bool autoFit = true;

        [Tooltip("Seat width adjustment per side (m). + wider, - narrower.")]
        public float seatWidthOffset;

        [Tooltip("Sitting height adjustment (m), added to the automatic correction. + raises the body (use it if the hips still go through the seat), - lowers it.")]
        public float seatHeightOffset;

        [Tooltip("Footrest height adjustment (m). + up.")]
        public float footHeightOffset;

        [Tooltip("Footrest front/back adjustment (m). + forward (away from the body).")]
        public float footForwardOffset;
    }
}
