// Pyrite9 Wheelchair — 이름 상수 + 메뉴 라벨(한·일·영)
#if UNITY_EDITOR
using System.Collections.Generic;

namespace Pyrite9.Wheelchair
{
    public static class WcConst
    {
        public const string Version = "1.1.1";   // README 3종 맨 위·빌드 로그 머리줄과 같이 올린다
        public const string ModelName = "WheelChair";
        public const string HipRefName = "WC_HipRef";
        public const string FollowName = "WC_FollowHips";
        public const string SeatName = "Seat";
        public const string FbtOffsetName = "WC_FBTOffset";   // Seat 밑 — 휠체어 Parent Constraint 소스. 풀 트래킹 밀림 클립이 이 오브젝트의 localPosition.z를 움직인다
        public const string GestureNodeName = "Anim Action Gesture";
        // VR 머리 높이(9-26 D안): 루트 위 Proximity Receiver ↔ Head 뼈 Sender → 블렌드 트리(바닥 앉기 ↔ 올린 앉기). 파라미터는 MA에 선언하지 않는 로컬 이름
        public const string HeadHeightParam = "Pyrite9WC_HeadHeight";
        public const string HeadHeightTree = "Wheelchair Sit Height (Head)";
        public const string HeadHeightTag = "Pyrite9WC_Head";
        // 아래로 따라가기: 앉은 머리 R 아래에 둔 두 번째 Receiver(값 = 앉은 높이에서 내려간 만큼, 앉은 높이 이상이면 0) → 바깥 1D 트리 [위 트리 @0, 내린 앉기 @1m]
        public const string HeadDownParam = "Pyrite9WC_HeadDown";
        public const string HeadDownTree = "Wheelchair Sit Height (Head Down)";
        // VRChat 풀 바디 트래킹 일반 모드(9-41): Action 트래커 상태의 앉기를 골반 높이만큼 올림. 골반 = 트래커라 되먹임 없음
        public const string HipHeightParam = "Pyrite9WC_HipHeight";
        public const string HipHeightTree = "Wheelchair Sit Height (Hip)";
        public const string HipHeightTag = "Pyrite9WC_Hip";
        public const string HipDownParam = "Pyrite9WC_HipDown";
        public const string HipDownTree = "Wheelchair Sit Height (Hip Down)";
    }

    // 배포판 메뉴 라벨 — 키 = 프리팹 안 Menu Item 오브젝트 이름. 빌드 때 WheelchairRoot.menuLanguage로 고른다(기본 English)
    public static class WcMenuLabels
    {
        public static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            //                    English               日本語               한국어
            { "Menu",     new[] { "Wheelchair",         "車椅子",             "휠체어" } },
            { "OnOff",    new[] { "On/Off",             "オン/オフ",          "On/Off" } },
            { "FBT",      new[] { "Full Body Tracking", "フルトラッキング",   "풀 트래킹" } },
            { "RearSeat", new[] { "Rear Seat",          "後部座席",           "뒷좌석 탑승" } },
        };

        public static string Get(string goName, MenuLanguage lang)
        {
            if (!Table.TryGetValue(goName, out var row)) return null;
            int i = (int)lang;
            return i >= 0 && i < row.Length ? row[i] : row[0];
        }
    }
}
#endif
