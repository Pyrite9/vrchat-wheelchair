// Pyrite9 Wheelchair — 새 버전 확인(인스펙터 머리)
//   공개 저장소 GitHub Releases 'latest'의 태그(vX.Y.Z)와 WcConst.Version 비교. 인증 없음·보내는 정보 없음(GET 한 번).
//   자동은 하루 한 번(인스펙터를 열 때), 결과는 EditorPrefs에 저장. 실패하면 조용히 '확인 못 함'.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Pyrite9.Wheelchair
{
    public static class WcUpdateCheck
    {
        public const string Api = "https://api.github.com/repos/Pyrite9/vrchat-wheelchair/releases/latest";
        public const string ReleasesPage = "https://github.com/Pyrite9/vrchat-wheelchair/releases";
        const string KeyTag = "Pyrite9WC.LatestTag", KeyUrl = "Pyrite9WC.LatestUrl", KeyAt = "Pyrite9WC.CheckedAt", KeyFail = "Pyrite9WC.CheckFailed";
        static readonly System.TimeSpan Interval = System.TimeSpan.FromHours(24);

        public enum State { Unknown, Checking, Latest, Newer, Failed }

        [System.Serializable] class Release { public string tag_name; public string html_url; }

        static UnityWebRequest running;
        public static event System.Action Changed;

        public static string LatestTag => EditorPrefs.GetString(KeyTag, "");
        public static string LatestUrl { get { var u = EditorPrefs.GetString(KeyUrl, ""); return string.IsNullOrEmpty(u) ? ReleasesPage : u; } }

        public static State Current
        {
            get
            {
                if (running != null) return State.Checking;
                if (EditorPrefs.GetBool(KeyFail, false)) return State.Failed;
                var tag = LatestTag;
                if (string.IsNullOrEmpty(tag)) return State.Unknown;
                return Compare(tag, WcConst.Version) > 0 ? State.Newer : State.Latest;
            }
        }

        // 하루가 지났으면 확인
        public static void CheckIfDue()
        {
            long at = System.Convert.ToInt64(EditorPrefs.GetString(KeyAt, "0"));
            if (System.DateTime.UtcNow - new System.DateTime(at, System.DateTimeKind.Utc) >= Interval) Check();
        }

        public static void Check()
        {
            if (running != null) return;
            var req = UnityWebRequest.Get(Api);
            req.timeout = 10;
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            running = req;
            Changed?.Invoke();
            req.SendWebRequest().completed += _ =>
            {
                bool ok = req.result == UnityWebRequest.Result.Success;
                Release rel = null;
                if (ok) { try { rel = JsonUtility.FromJson<Release>(req.downloadHandler.text); } catch { } }
                ok = ok && rel != null && !string.IsNullOrEmpty(rel.tag_name);
                if (ok) { EditorPrefs.SetString(KeyTag, rel.tag_name.Trim()); EditorPrefs.SetString(KeyUrl, rel.html_url ?? ""); }
                EditorPrefs.SetBool(KeyFail, !ok);
                EditorPrefs.SetString(KeyAt, System.DateTime.UtcNow.Ticks.ToString());
                req.Dispose();
                running = null;
                Changed?.Invoke();
            };
        }

        // "v1.0.2" vs "1.0.1" → 양수면 a가 새것. 못 읽으면 0(같다고 봄)
        public static int Compare(string a, string b)
        {
            return System.Version.TryParse(a.TrimStart('v', 'V'), out var va) && System.Version.TryParse(b.TrimStart('v', 'V'), out var vb) ? va.CompareTo(vb) : 0;
        }
    }
}
#endif
