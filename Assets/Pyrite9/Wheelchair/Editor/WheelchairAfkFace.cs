// Pyrite9 Wheelchair — AFK 모션 표정 키를 VRC Avatar Descriptor에서 자동으로(own-model 9-56)
//   눈 감기 = Eye Look의 Eyelids(Blendshapes) 첫 키(blink) · 입 = Lip Sync(Viseme Blend Shape)의 aa·oh·ou
//   웃음·눈물 = 얼굴 메시의 셰이프키 이름에서 흔한 것(にこり·笑い·なごみ·smile·joy / にやり·mouth_smile / 涙·tear)을 찾아서 — 없으면 그 묶음은 비움
//   묶음(값 0~100, 기본값 위에 더함): effort "읏차" · yawnEyes · yawnMouth 하품 · tears 눈물 · puff "후—" · content 만족 · walk 걸을 때
//   얼굴 메시 = 립싱크 메시(없으면 눈꺼풀 메시). 눈 감기 키가 다른 메시에 있으면 눈 감기는 뺀다(한 메시만 굽는다)
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pyrite9.Wheelchair
{
    public sealed class WcAfkFaceMap
    {
        public string Path = "";
        public readonly List<string> Blink = new List<string>();
        public readonly HashSet<string> EyeKeys = new HashSet<string>();   // 감는 동안 줄일 다른 눈 키(웃는 눈)
        public readonly Dictionary<string, Dictionary<string, float>> Groups = new Dictionary<string, Dictionary<string, float>>();
        public List<string> Keys => Groups.Values.SelectMany(d => d.Keys).Concat(Blink).Distinct().ToList();

        static readonly string[] SmileEyeNames = { "にこり", "笑い", "なごみ", "eye_nagomi", "eye_smile", "eyesmile", "smile_eye", "eye_joy", "eye_happy", "eye_laugh" };
        static readonly string[] SmileMouthNames = { "にやり", "にっこり", "mouth_smile", "mouth_joy", "mouth_happy", "mouthsmile", "smile_mouth", "∀" };
        static readonly string[] TearNames = { "涙", "なみだ", "tear" };
        static readonly string[] BlinkNames = { "まばたき", "瞬き", "目閉じ", "blink", "eye_close", "eyeclose", "eyes_closed", "eye_blink", "eyeblink", "close_eye" };

        public static WcAfkFaceMap From(VRCAvatarDescriptor d, Transform root, StringBuilder log)
        {
            var map = new WcAfkFaceMap();
            SkinnedMeshRenderer face = null;
            if (d.lipSync == VRC_AvatarDescriptor.LipSyncStyle.VisemeBlendShape && d.VisemeSkinnedMesh != null) face = d.VisemeSkinnedMesh;
            var lids = d.customEyeLookSettings.eyelidType == VRCAvatarDescriptor.EyelidType.Blendshapes ? d.customEyeLookSettings.eyelidsSkinnedMesh : null;
            if (face == null) face = lids;
            if (face == null || face.sharedMesh == null || !face.transform.IsChildOf(root))
            {
                log.AppendLine("afkmotion: 얼굴 — Descriptor에 립싱크·눈꺼풀 셰이프키가 없음 → 표정 없이");
                return map;
            }
            map.Path = AnimationUtility.CalculateTransformPath(face.transform, root);
            var mesh = face.sharedMesh;
            var names = Enumerable.Range(0, mesh.blendShapeCount).Select(i => mesh.GetBlendShapeName(i)).ToList();
            string Has(string n) => !string.IsNullOrEmpty(n) && names.Contains(n) ? n : null;

            // 눈 감기
            string blink = null;
            if (lids == face && d.customEyeLookSettings.eyelidsBlendshapes != null && d.customEyeLookSettings.eyelidsBlendshapes.Length > 0)
            {
                int bi = d.customEyeLookSettings.eyelidsBlendshapes[0];
                if (bi >= 0 && bi < names.Count) blink = names[bi];
            }
            // Descriptor에 눈꺼풀 셰이프키가 없으면 흔한 이름으로(왼·오른 따로인 'blink_L' 같은 건 안 씀 — 양쪽 한 번에 감는 키만)
            // 입(립싱크 키: 0 sil … 10 aa, 11 E, 12 ih, 13 oh, 14 ou)
            string V(int i) => d.VisemeBlendShapes != null && d.VisemeBlendShapes.Length > i ? Has(d.VisemeBlendShapes[i]) : null;
            string aa = V(10), oh = V(13), ou = V(14);
            if (face != d.VisemeSkinnedMesh) aa = oh = ou = null;
            // 이름으로 찾기(대소문자 무시, 같은 이름 먼저 → 포함)
            string Find(string[] cands, params string[] avoid)
            {
                foreach (var c in cands) { var e = names.FirstOrDefault(n => string.Equals(n, c, System.StringComparison.OrdinalIgnoreCase) && !avoid.Contains(n)); if (e != null) return e; }
                foreach (var c in cands) { var e = names.FirstOrDefault(n => n.IndexOf(c, System.StringComparison.OrdinalIgnoreCase) >= 0 && !n.StartsWith("vrc.") && !avoid.Contains(n)); if (e != null) return e; }
                return null;
            }
            if (blink == null)
            {
                blink = Find(BlinkNames);
                if (blink != null && (blink.EndsWith("_L") || blink.EndsWith("_R") || blink.EndsWith(".L") || blink.EndsWith(".R") || blink.Contains("左") || blink.Contains("右") || blink.IndexOf("left", System.StringComparison.OrdinalIgnoreCase) >= 0 || blink.IndexOf("right", System.StringComparison.OrdinalIgnoreCase) >= 0)) blink = null;
                if (blink != null) log.AppendLine($"afkmotion: 얼굴 — Descriptor에 눈꺼풀 셰이프키가 없어 이름으로 찾음: {blink}");
            }
            if (blink != null) map.Blink.Add(blink);
            string smileEye = Find(SmileEyeNames, blink);
            string smileMouth = Find(SmileMouthNames, smileEye, blink);
            string tear = Find(TearNames);
            // 눈물이 좌우 따로면(…left/…right, _L/_R) 짝도
            string tear2 = null;
            if (tear != null)
            {
                foreach (var (a, c) in new[] { ("left", "right"), ("right", "left"), ("_L", "_R"), ("_R", "_L"), ("Left", "Right"), ("Right", "Left") })
                    if (tear.Contains(a)) { var other = tear.Replace(a, c); if (names.Contains(other)) { tear2 = other; break; } }
            }
            if (smileEye != null) map.EyeKeys.Add(smileEye);

            Dictionary<string, float> G(params (string k, float v)[] kv)
            {
                var g = new Dictionary<string, float>();
                foreach (var (k, v) in kv) if (k != null) g[k] = v;
                return g;
            }
            map.Groups["effort"] = G((blink, 40f));
            map.Groups["yawnEyes"] = G((blink, 100f));
            map.Groups["yawnMouth"] = aa != null ? G((aa, 85f)) : G((oh, 90f));
            map.Groups["tears"] = G((tear, 100f), (tear2, 100f));
            map.Groups["puff"] = G((ou, 60f), (blink, 100f));
            map.Groups["content"] = smileEye != null || smileMouth != null ? G((smileEye, 85f), (smileMouth, 70f)) : G((blink, 35f));
            map.Groups["walk"] = G((smileEye, 50f), (smileMouth, 50f));
            log.AppendLine($"afkmotion: 얼굴 '{map.Path}' — 눈 감기 {blink ?? "-"}, 입 aa {aa ?? "-"} · oh {oh ?? "-"} · ou {ou ?? "-"}, 웃는 눈 {smileEye ?? "-"}, 웃는 입 {smileMouth ?? "-"}, 눈물 {tear ?? "-"}{(tear2 != null ? " + " + tear2 : "")}");
            return map;
        }
    }
}
#endif
