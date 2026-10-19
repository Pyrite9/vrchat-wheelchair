// Pyrite9 Wheelchair — 아바타별 마지막 빌드 결과(인스펙터 '마지막 빌드' 칸)
//   빌드(Play·업로드)의 마지막 패스가 Library/Pyrite9Wheelchair/last_builds.json에 아바타 이름별로 쓴다.
//   Library는 프로젝트마다 따로·git 밖이라 받는 사람 프로젝트에 흔적이 남지 않는다. 읽기 실패는 '기록 없음'으로.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Pyrite9.Wheelchair
{
    public static class WcBuildRecord
    {
        public const string Dir = "Library/Pyrite9Wheelchair";
        public const string FilePath = Dir + "/last_builds.json";
        const int MaxEntries = 32;

        [System.Serializable]
        public class Entry
        {
            public string avatar;
            public string time;        // yyyy-MM-dd HH:mm:ss
            public string version;
            public bool fitted;        // 자동 맞춤이 돌았나(꺼짐·휴머노이드 아님이면 false)
            public float scale;        // 휠체어 배율
            public float raise;        // 몸 올림(m, 자동 + 수동)
            public bool heightScale;   // 크기 무관 높이 따라가기(scale 패스)가 붙었나
            public List<string> warnings = new List<string>();
        }

        [System.Serializable]
        class Store { public List<Entry> entries = new List<Entry>(); }

        public static string CleanName(string n) => n == null ? "" : n.Replace("(Clone)", "").Trim();

        static Store Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonUtility.FromJson<Store>(File.ReadAllText(FilePath)) ?? new Store();
            }
            catch { }
            return new Store();
        }

        public static Entry Get(string avatar)
        {
            var n = CleanName(avatar);
            return Load().entries.FirstOrDefault(e => e.avatar == n);
        }

        public static long Stamp() { try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath).Ticks : 0; } catch { return 0; } }

        public static void Put(Entry e)
        {
            try
            {
                e.avatar = CleanName(e.avatar);
                var st = Load();
                st.entries.RemoveAll(x => x.avatar == e.avatar);
                st.entries.Insert(0, e);
                if (st.entries.Count > MaxEntries) st.entries.RemoveRange(MaxEntries, st.entries.Count - MaxEntries);
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonUtility.ToJson(st, true));
            }
            catch (System.Exception ex) { Debug.LogWarning("[Pyrite9 Wheelchair] build record not saved: " + ex.Message); }
        }
    }

    // AFK 모션 굽기 결과(AFK 모션 모듈이 쓴다) — 인스펙터 AFK 칸이 '마지막 굽기'와 설정이 바뀌었는지를 본다
    public static class WcAfkBakeRecord
    {
        public const string FilePath = WcBuildRecord.Dir + "/afk_bakes.json";

        [System.Serializable]
        public class Entry
        {
            public string avatar;
            public string time;
            public int setting;        // 구울 때 인스펙터 값(AfkMotion)
            public int motion;         // 실제로 구운 것(AfkMotion.OneArm·BothArms)
            public float oneArmGap;    // 한쪽 기지개를 구워 본 팔-머리 최소 간격(m, 안 구웠으면 NaN 대신 999)
            public float gap;          // 실제로 구운 것의 간격
        }

        [System.Serializable]
        class Store { public List<Entry> entries = new List<Entry>(); }

        static Store Load()
        {
            try { if (File.Exists(FilePath)) return JsonUtility.FromJson<Store>(File.ReadAllText(FilePath)) ?? new Store(); }
            catch { }
            return new Store();
        }

        public static Entry Get(string avatar)
        {
            var n = WcBuildRecord.CleanName(avatar);
            return Load().entries.FirstOrDefault(e => e.avatar == n);
        }

        public static long Stamp() { try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath).Ticks : 0; } catch { return 0; } }

        public static void Put(Entry e)
        {
            try
            {
                e.avatar = WcBuildRecord.CleanName(e.avatar);
                var st = Load();
                st.entries.RemoveAll(x => x.avatar == e.avatar);
                st.entries.Insert(0, e);
                Directory.CreateDirectory(WcBuildRecord.Dir);
                File.WriteAllText(FilePath, JsonUtility.ToJson(st, true));
            }
            catch (System.Exception ex) { Debug.LogWarning("[Pyrite9 Wheelchair] AFK bake record not saved: " + ex.Message); }
        }
    }
}
#endif
