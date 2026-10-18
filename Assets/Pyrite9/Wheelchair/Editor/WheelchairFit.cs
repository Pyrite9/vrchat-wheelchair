// Pyrite9 Wheelchair — 빌드 때 맞춤
//   아바타마다 다른 값은 프리팹에 두지 않고 빌드 사본을 재서 정한다. NDMF(WheelchairPlugin.cs)가 부른다.
//   A. FitBase  (Resolving, MA 전) : 앉은 클립의 골반 위치 → 모델 배율·XZ, WC_HipRef(Bone Proxy 전), Seat.
//   B. FitParts (Transforming, MA 후·AAO 전) : 의상이 합쳐진 뒤 앉은 자세로 메시를 구워
//                                     골반·허벅지 반폭 → WC_Side_L/R(+바퀴·캐스터), 발바닥·뒤꿈치 → WC_Foot_L/R.
//                                     AAO가 애니메이션 없는 조절 본을 WC_Root에 합치기 전에 옮겨야 그 위치가 메시에 구워진다.
//   자세를 샘플한 뒤에는 아바타의 모든 트랜스폼을 저장해 둔 값으로 되돌린다.
//   WheelchairRoot 마커가 있으면: autoFit 끄면 건너뜀, fitB 결과에 수동 보정(아바타 기준 m)을 더한다(D8).
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Pyrite9.Wheelchair
{
    public static class WheelchairFit
    {
        // 설계 기준값 — 휠체어 v7(Lapwing 앉은 자세 실측). 모델 설계 좌표(배율 1, m).
        public const float RailX = 0.165f;          // 좌석 레일·측면 프레임 x
        public const float GuardInnerX = 0.170f;    // 옷 보호판 안쪽 면 x(팔걸이 0.18 − 0.008 − 판 두께 반) — 좌석 폭 기준
        public const float ClothClear = 0.005f;     // 몸·옷과 보호판 사이 최소 간격. 모자라면 그만큼만 넓힌다(좁히지는 않음)
        // 발판 기준(리그 v8·앉기 자세 v8 — 무릎 Stretch 0·발목 Up-Down 0.5, Base 씬 Lapwing 맨발 실측 2026-10-17). 판 = 뒤꿈치 −2cm ~ 발끝 +4mm
        //   z는 모델 원점(= 앉은 골반 xz) 기준. 🔴 v8 자세는 무게중심 때문에 골반이 루트 z +0.0075로 옮겨 가 루트 기준 실측(뒤꿈치 0.413·발끝 0.606)에서 뺐다
        public const float RefHipHeightV8 = 0.5403f; // v8 자세 Lapwing 앉은 골반 높이(모델 배율 1 기준)
        public const float RefSoleMinY = 0.138f;    // Lapwing 맨발 발바닥 최저점
        public const float RefHeelZ = 0.4055f;      // Lapwing 맨발 뒤꿈치(가장 뒤 z)
        public const float RefToeZ = 0.5985f;       // Lapwing 맨발 발끝(가장 앞 z) — 로그용. v8.1부터 판 앞끝을 신발에 맞춰 늘리지 않는다(StretchPlateToToe)
        // v8.1: 판 깊이 16cm 고정(신발 길이에 맞춰 늘리지 않음), 앞끝 본은 뒤끝과 같이 움직인다
        public const bool StretchPlateToToe = false;
        public const float PlateBackTopY = 0.151f;  // 판 뒤끝 윗면(설계) — 바닥 뚫림 검사용
        public const float MinPlateLength = 0.10f;  // 판 앞끝이 뒤끝보다 최소 이만큼 앞
        public const float MinPlateTopY = 0.03f;    // 발판이 바닥을 뚫지 않게(루트 기준 m)
        // 좌석 천 윗면(모델 설계 좌표 — bpy SEAT_Y·TUBE_R·SEAT_CENTER_Y·SEAT_Z): 가장자리 = 레일 위, 가운데로 포물선 처짐. 좌석을 넓히면 x로 늘어난다
        public const float SeatRailY = 0.48f, SeatTubeR = 0.011f, SeatCenterY = 0.466f, SeatZ0 = -0.10f, SeatZ1 = 0.29f;
        // 엉덩이가 좌석을 뚫으면 몸(앉기 클립)을 올린다: 올림 = 가장 깊이 뚫은 정점 + 간격. Lapwing 엉덩이 최저 0.470(가운데 0.466 위 4mm) → 0
        //   SeatProbeDepth보다 깊은 정점은 좌석을 뚫고 내려온 다른 것(늘어진 치마 등)으로 보고 뺀다. 자동 올림 상한 SeatMaxRaise(넘으면 경고)
        public const float SeatClear = 0.003f, SeatProbeDepth = 0.15f, SeatMaxRaise = 0.10f;
        //   깊이 = 관통 정점 깊이의 SeatPercentile 분위(낱개로 튀어나온 옷자락은 무시, 엉덩이 면처럼 정점이 몰린 깊이를 씀). Milfy 몸 자체가 8~9cm(2026-10-17)
        public const float SeatPercentile = 0.995f;
        //   재는 범위 = 좌석 폭 가운데 SeatCoreFrac(엉덩이가 실제로 앉는 곳). 가장자리는 천이 레일 높이로 올라가 넓은 골반·허벅지·옷자락이 늘 걸린다(Milfy 6cm, 2026-10-17)
        public const float SeatCoreFrac = 0.75f;
        const string ArmPath = "WheelChairArmature/WC_Root";

        // 풀 트래킹 센서 — 자작 모델 v7 설계 좌표(배율 1, 모델 원점 = 앉은 골반 밑 바닥)
        //   Receiver Box(의자 쪽): 앞면 = 등받이 천 앞면·좌석 앞 가장자리 + 1cm, 크기 0.5×0.5×깊이 0.3. 골반 틀(WC_FollowHips)에 둔다 — 휠체어에 붙이면 되먹임
        public const float BackBoxFrontZ = -0.142f;   // 등받이 천 가운데 앞면 z(Sender 높이 0.897에서 −0.152) + 0.01
        public const float BackBoxCenterY = 0.837f;   // 골반(0.537) 위 0.30
        public const float ShinBoxFrontZ = 0.30f;     // 좌석 천 앞 0.29 + 0.01
        public const float ShinBoxCenterY = 0.337f;   // 골반 아래 0.20
        static readonly Vector3 SensorBoxSize = new Vector3(0.5f, 0.5f, 0.3f);
        //   Sender(몸 쪽): 등 = 골반 기준 위 0.36·뒤 0.07(Lapwing, 배율 s), 정강이 = 무릎→발목의 52%(Lapwing 0.20 / 0.3835)
        public const float BackSenderUp = 0.36f, BackSenderBack = 0.07f, BackSenderR = 0.03f;
        public const float ShinSenderRatio = 0.5215f, ShinSenderR = 0.045f;

        // 바퀴 회전 — 모델 설계값 WHEEL_R·CASTER_R = WHEEL_R / 4
        public const float WheelR = 0.301f;          // 뒷바퀴 반지름(설계, 배율 1)
        public const int CasterTurns = 4;            // 뒷바퀴 1바퀴에 캐스터 바퀴 수(정수 — 클립 루프 이음매)

        // 앉은 클립 = 휠체어 안 Action Merge Animator의 'On VR' 상태 모션의 첫 클립(블렌드 트리는 첫 자식을 따라 내려감 = 바닥 앉기)
        public static AnimationClip SitClip(Transform holder)
        {
            if (holder != null)
                foreach (var mb in holder.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb.GetType().Name != "ModularAvatarMergeAnimator") continue;
                    var ac = new SerializedObject(mb).FindProperty("animator").objectReferenceValue as AnimatorController;
                    var m = ac != null ? FindState(ac, "On VR")?.motion : null;
                    var clip = FirstClip(m);
                    if (clip != null) return clip;
                }
            return null;
        }

        // 🔴 fitB(MA 후)에선 MA가 Merge Animator를 이미 처리해 일습 안에서 클립을 못 찾는다 → fitA(MA 전)에서 찾은 걸 아바타별로 기억
        static readonly Dictionary<int, AnimationClip> SitByAvatar = new Dictionary<int, AnimationClip>();
        // fitB가 정한 몸 올림(루트 기준 m, 자동 + 수동) → seat 패스·height 패스가 쓴다
        static readonly Dictionary<int, float> RaiseByAvatar = new Dictionary<int, float>();

        static AnimationClip FirstClip(Motion m)
        {
            for (int depth = 0; depth < 8 && m != null; depth++)
            {
                if (m is AnimationClip c) return c;
                m = m is BlendTree bt && bt.children.Length > 0 ? bt.children[0].motion : null;
            }
            return null;
        }

        // 상태 모션 안의 블렌드 트리 전부(중첩 포함)
        static IEnumerable<BlendTree> Trees(Motion m)
        {
            if (!(m is BlendTree bt)) yield break;
            yield return bt;
            foreach (var ch in bt.children) foreach (var x in Trees(ch.motion)) yield return x;
        }

        static AnimatorState FindState(AnimatorController ac, string name) =>
            ac.layers.SelectMany(l => l.stateMachine.states).Select(s => s.state).FirstOrDefault(s => s.name == name);

        static WheelchairRoot Settings(Transform holder) => holder != null ? holder.GetComponent<WheelchairRoot>() : null;
        static bool HasRig(Transform holder) => holder != null && holder.Find(WcConst.ModelName + "/" + ArmPath + "/WC_Side_L") != null;

        // ---------------------------------------------------------------- 자세 샘플 + 복원
        struct Pose { public Vector3 p; public Quaternion r; public Vector3 s; }

        static T WithSitPose<T>(Transform root, AnimationClip sit, System.Func<T> body)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            var saved = all.Select(t => new Pose { p = t.localPosition, r = t.localRotation, s = t.localScale }).ToArray();
            try
            {
                sit.SampleAnimation(root.gameObject, 0f);
                return body();
            }
            finally
            {
                for (int i = 0; i < all.Length; i++) { all[i].localPosition = saved[i].p; all[i].localRotation = saved[i].r; all[i].localScale = saved[i].s; }
            }
        }

        // ---------------------------------------------------------------- A
        public static bool FitBase(Transform root, Transform holder, StringBuilder log)
        {
            var anim = root.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) { log.AppendLine("fitA: WARN the avatar is not humanoid — automatic fitting skipped"); return false; }
            var set = Settings(holder);
            if (set != null && !set.autoFit) { log.AppendLine("fitA: autoFit 꺼짐 — 프리팹 값 그대로"); return false; }
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var sit = SitClip(holder);
            if (sit != null) SitByAvatar[root.GetInstanceID()] = sit;
            if (sit == null) { log.AppendLine("fitA: WARN sitting animation not found in the prefab — automatic fitting skipped"); return false; }
            var restHipPos = hips.position; var restHipRot = hips.rotation;
            Vector3 hp = Vector3.zero; Quaternion refLocal = Quaternion.identity;
            WithSitPose(root, sit, () => { hp = root.InverseTransformPoint(hips.position); refLocal = Quaternion.Inverse(hips.rotation) * root.rotation; return 0; });
            if (!HasRig(holder)) { log.AppendLine("fitA: WARN wheelchair bone WC_Side_L not found — automatic fitting skipped"); return false; }
            float s = hp.y / RefHipHeightV8;
            if (Mathf.Abs(s - 1f) < 0.01f) s = 1f;

            var model = holder.Find(WcConst.ModelName);
            if (model == null) { log.AppendLine("fitA: WARN wheelchair model object not found — automatic fitting skipped"); return false; }
            // 받는 쪽이 일습을 어느 오브젝트 밑에 어떤 위치로 두든, 아래 계산은 '아바타 루트 = 휠체어 루트' 기준.
            // 🔴 위치만 맞추면 안 된다: 휠체어 루트의 VRC Parent Constraint는 weight 0(풀 트래킹 끔)일 때 rest(local 0)로 돌아가
            //    부모 기준으로 엇갈린다(2026-10-17 Play 실측) → 아바타 루트 직속, local 0으로 옮긴다.
            if (holder.parent != root || holder.localPosition != Vector3.zero || holder.localRotation != Quaternion.identity || holder.localScale != Vector3.one)
            {
                log.AppendLine($"fitA: 휠체어 루트 '{AnimationUtility.CalculateTransformPath(holder, root)}' (world {holder.position.ToString("F3")}) → 아바타 루트 직속 local 0");
                holder.SetParent(root, false);
                holder.localPosition = Vector3.zero; holder.localRotation = Quaternion.identity; holder.localScale = Vector3.one;
            }
            string before = $"pos {model.localPosition.ToString("F4")} scale {model.localScale.x:F4}";
            model.localPosition = new Vector3(hp.x, 0, hp.z);
            model.localScale = Vector3.one * s;

            var hipRef = holder.Find(WcConst.HipRefName);
            var seat = hipRef != null ? hipRef.Find(WcConst.FollowName + "/" + WcConst.SeatName) : null;
            if (hipRef != null && seat != null)
            {
                hipRef.SetPositionAndRotation(restHipPos, restHipRot * refLocal);
                hipRef.localScale = Vector3.one;
                seat.localPosition = -hp; seat.localRotation = Quaternion.identity;
            }
            else log.AppendLine("fitA: WARN WC_HipRef/Seat not found — the chair may not follow the hips");
            log.AppendLine($"fitA: 골반 {hp.ToString("F4")} → 배율 {s:F4}, 모델 {before} → pos {model.localPosition.ToString("F4")} scale {s:F4}");
            if (s < 0.75f || s > 1.35f) log.AppendLine($"fitA: WARN chair scale {s:F2} is far from the reference — check the fit and use the offsets if needed");
            FitSensors(root, holder, anim, sit, hp, s, log);
            return true;
        }

        // 풀 트래킹 센서: Receiver는 골반 틀 기준 위치·크기, Sender는 붙을 뼈 기준 위치·반지름(Bone Proxy가 뼈 밑 local 0에 붙인 뒤에도 맞게)
        static void FitSensors(Transform root, Transform holder, Animator anim, AnimationClip sit, Vector3 hp, float s, StringBuilder log)
        {
            void Box(string path, float frontZ, float centerY)
            {
                var t = holder.Find(path); var rc = t != null ? t.GetComponent<VRC.Dynamics.ContactBase>() : null;
                if (rc == null) { log.AppendLine("fitA: WARN full-body sensor not found: " + path); return; }
                var before = rc.position;
                rc.position = new Vector3(0, s * centerY - hp.y, s * (frontZ - SensorBoxSize.z / 2));   // 골반 틀 = 루트 축, 원점 = 앉은 골반
                rc.size = SensorBoxSize * s;
                log.AppendLine($"fitA: 센서 {t.name} 중심 {before.ToString("F3")} → {rc.position.ToString("F3")} 크기 {rc.size.ToString("F3")}");
            }
            Box(WcConst.HipRefName + "/" + WcConst.FollowName + "/WC_BackSensor", BackBoxFrontZ, BackBoxCenterY);
            Box(WcConst.HipRefName + "/" + WcConst.FollowName + "/WC_ShinSensor", ShinBoxFrontZ, ShinBoxCenterY);

            // Sender: 앉은 자세에서 원하는 점 → 붙을 뼈 기준 local. 뼈 기준 값이라 자세가 바뀌어도 뼈를 따라간다
            Transform Bone(string refName)
            {
                var r = holder.Find(refName);
                var bp = r != null ? r.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "ModularAvatarBoneProxy") : null;
                return bp != null ? anim.GetBoneTransform((HumanBodyBones)new SerializedObject(bp).FindProperty("boneReference").intValue) : null;
            }
            var jobs = new List<(string sender, Transform bone, System.Func<Vector3> world, float r)>();
            var chest = Bone("WC_ChestRef");
            jobs.Add(("WC_ChestRef/WC_BackSender", chest, () => root.TransformPoint(hp + new Vector3(0, BackSenderUp * s, -BackSenderBack * s)), BackSenderR));
            foreach (var side in new[] { ("L", HumanBodyBones.LeftFoot), ("R", HumanBodyBones.RightFoot) })
            {
                var knee = Bone("WC_KneeRef" + side.Item1); var foot = anim.GetBoneTransform(side.Item2);
                if (knee == null || foot == null) { log.AppendLine("fitA: WARN knee or foot bone not found: " + side.Item1); continue; }
                jobs.Add(("WC_KneeRef" + side.Item1 + "/WC_ShinSender" + side.Item1, knee, () => Vector3.Lerp(knee.position, foot.position, ShinSenderRatio), ShinSenderR));
            }
            var local = new Vector3[jobs.Count];
            WithSitPose(root, sit, () => { for (int i = 0; i < jobs.Count; i++) if (jobs[i].bone != null) local[i] = jobs[i].bone.InverseTransformPoint(jobs[i].world()); return 0; });
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i]; var t = holder.Find(j.sender); var sd = t != null ? t.GetComponent<VRC.Dynamics.ContactBase>() : null;
                if (sd == null || j.bone == null) { log.AppendLine("fitA: WARN contact sender or its bone not found: " + j.sender); continue; }
                var before = t.localPosition;
                t.localPosition = local[i]; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
                sd.position = Vector3.zero;
                sd.radius = j.r * s / Mathf.Max(1e-6f, j.bone.lossyScale.x);   // Bone Proxy 뒤 부모 = 뼈(local 0, 배율 1)
                log.AppendLine($"fitA: 센서 {t.name} 뼈 {j.bone.name} 기준 {before.ToString("F4")} → {t.localPosition.ToString("F4")} 반지름 {sd.radius:F4}");
            }
        }

        // 풀 트래킹 '뒤로 빼기' 양: 클립이 Seat 밑 WC_FBTOffset의 localPosition.z(휠체어 Parent Constraint 소스)를 움직인다.
        // 🔴 VRC Parent Constraint 오프셋(m)은 배율을 안 따라서(2026-10-17 실측), 1.0.0처럼 오프셋을 직접 쓰면 VRChat 아바타 크기 조절(실행 중)에
        // 밀리는 거리가 그대로였다 → 1.0.1부터 로컬 위치로 바꿔 실행 중 배율은 계층이 따라가게 하고, 빌드 때는 '휠체어 모델 배율 ÷ Seat 배율'만 곱한다
        public static int ScaleFbtOffsets(AnimatorController fx, Transform root, Transform holder, System.Func<AnimationClip, AnimationClip> ensureTemp, StringBuilder log)
        {
            var model = holder.Find(WcConst.ModelName);
            var off = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == WcConst.FbtOffsetName && t.parent != null && t.parent.name == WcConst.SeatName);
            if (model == null || off == null || !HasRig(holder)) { log.AppendLine($"fbt: WARN {WcConst.FbtOffsetName} not found under {WcConst.SeatName} — full-body push-back is not scaled"); return 0; }
            // 클립 값(m)은 기준 휠체어(모델 배율 1, Seat 배율 1) 기준. 월드 거리 = 값 × Seat 배율이니, 휠체어 크기에 맞추려면 × 모델 배율 ÷ Seat 배율
            float s = model.lossyScale.z / Mathf.Max(1e-6f, off.parent.lossyScale.z);
            if (Mathf.Abs(s - 1f) < 1e-4f) { log.AppendLine($"fbt: 배율 {s:F4} — 그대로"); return 0; }
            string path = AnimationUtility.CalculateTransformPath(off, root);
            var done = new Dictionary<AnimationClip, AnimationClip>();
            int curves = 0;
            Motion Fix(Motion m)
            {
                if (m is BlendTree bt) { var ch = bt.children; for (int i = 0; i < ch.Length; i++) ch[i].motion = Fix(ch[i].motion); bt.children = ch; return bt; }
                var clip = m as AnimationClip;
                if (clip == null) return m;
                if (done.TryGetValue(clip, out var d)) return d;
                var bs = AnimationUtility.GetCurveBindings(clip).Where(b => b.path == path && b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalPosition")).ToArray();
                if (bs.Length == 0) { done[clip] = clip; return clip; }
                var c = ensureTemp(clip);
                foreach (var b in bs)
                {
                    var cv = AnimationUtility.GetEditorCurve(c, b);
                    var ks = cv.keys; for (int k = 0; k < ks.Length; k++) { ks[k].value *= s; ks[k].inTangent *= s; ks[k].outTangent *= s; }
                    cv.keys = ks; AnimationUtility.SetEditorCurve(c, b, cv); curves++;
                }
                log.AppendLine($"fbt: '{clip.name}' {path} 로컬 z × {s:F4}(모델 {model.lossyScale.z:F4} ÷ Seat {off.parent.lossyScale.z:F4})");
                done[clip] = c; return c;
            }
            foreach (var l in fx.layers) foreach (var st in AllStates(l.stateMachine)) st.motion = Fix(st.motion);
            if (curves == 0) log.AppendLine($"fbt: WARN no push-back curve found on '{path}' — check the FBT animator");
            return curves;
        }

        // 오브젝트를 옮긴 뒤 애니메이션 경로 따라가기: from(또는 from/…)을 가리키는 커브를 to로 바꾼다. 원본 클립은 ensureTemp로 복제해서 고친다
        public static int RemapPaths(AnimatorController ac, string from, string to, System.Func<AnimationClip, AnimationClip> ensureTemp)
        {
            var done = new Dictionary<AnimationClip, AnimationClip>();
            int curves = 0;
            bool Hit(string p) => p == from || p.StartsWith(from + "/");
            Motion Fix(Motion m)
            {
                if (m is BlendTree bt) { var ch = bt.children; for (int i = 0; i < ch.Length; i++) ch[i].motion = Fix(ch[i].motion); bt.children = ch; return bt; }
                var clip = m as AnimationClip;
                if (clip == null) return m;
                if (done.TryGetValue(clip, out var d)) return d;
                var fb = AnimationUtility.GetCurveBindings(clip).Where(b => Hit(b.path)).ToArray();
                var ob = AnimationUtility.GetObjectReferenceCurveBindings(clip).Where(b => Hit(b.path)).ToArray();
                if (fb.Length + ob.Length == 0) { done[clip] = clip; return clip; }
                var c = ensureTemp(clip);
                foreach (var b in fb)
                {
                    var cv = AnimationUtility.GetEditorCurve(c, b); var nb = b; nb.path = to + b.path.Substring(from.Length);
                    AnimationUtility.SetEditorCurve(c, b, null); AnimationUtility.SetEditorCurve(c, nb, cv); curves++;
                }
                foreach (var b in ob)
                {
                    var kf = AnimationUtility.GetObjectReferenceCurve(c, b); var nb = b; nb.path = to + b.path.Substring(from.Length);
                    AnimationUtility.SetObjectReferenceCurve(c, b, null); AnimationUtility.SetObjectReferenceCurve(c, nb, kf); curves++;
                }
                done[c] = c; done[clip] = c; return c;
            }
            foreach (var l in ac.layers) foreach (var st in AllStates(l.stateMachine)) st.motion = Fix(st.motion);
            return curves;
        }

        // 바퀴 회전 속도: 상태 속도 = VelocityZ × 클립 길이 / (2π·R·s). 클립 1초 = 뒷바퀴 1바퀴. 배율마다 달라 빌드 사본 FX에 쓴다
        public static int SetSpinSpeed(AnimatorController fx, Transform root, Transform holder, StringBuilder log)
        {
            var model = holder.Find(WcConst.ModelName);
            if (model == null || !HasRig(holder)) { log.AppendLine("spin: 조절 본 없음 — 그대로"); return 0; }
            float s = model.localScale.x;
            string wheel = AnimationUtility.CalculateTransformPath(model, root) + "/" + ArmPath + "/WC_Wheel_L";
            int n = 0;
            foreach (var l in fx.layers)
                foreach (var st in AllStates(l.stateMachine))
                {
                    var clip = st.motion as AnimationClip;
                    if (clip == null || !st.speedParameterActive || st.speedParameter != "VelocityZ") continue;
                    if (!AnimationUtility.GetCurveBindings(clip).Any(b => b.path == wheel)) continue;
                    float before = st.speed;
                    st.speed = clip.length / (2f * Mathf.PI * WheelR * s);
                    log.AppendLine($"spin: [{l.name}/{st.name}] 속도 {before:F4} → {st.speed:F4} (R {WheelR} × 배율 {s:F4})");
                    n++;
                }
            if (n == 0) log.AppendLine("spin: WARN wheel spin state not found — wheels keep the default speed: " + wheel);
            return n;
        }

        // VR 머리 높이: Space Drag로 HMD를 올리면 VRChat은 몸을 머리에 매달아 올리지만 앉기 클립의 발은 바닥에 남아 다리가 펴진다
        //   → 앉은 자세 자체를 머리 높이만큼 올린다. 높이는 Contact로 잰다(OSC 안 씀):
        //   Receiver(Proximity, 구) = 아바타 루트 밑, 중심 = 앉은 머리 바로 위 0.9R, R = 3m(🔴 Contact 반경은 3m에서 잘림)
        //   Sender = Head 뼈. 값 v = 1 − (거리 − Sender 반지름) / R → 머리가 높을수록 큼. 바닥 앉기 @ v0(거리 0.9R), 올린 앉기 @ v1(거리 0.05R, 올림 0.85R).
        //   v0 아래(실제로 더 숙임·값이 안 옴 = 0)는 바닥 앉기에서 멈춘다. 빌드 사본(Action·올린 클립)에만 쓴다
        public const float HeadRecvMaxR = 3f, HeadRestFrac = 0.9f, HeadTopFrac = 0.05f, HeadSenderR = 0.005f;
        public const float HeadDownMax = 1.0f;   // 아래로 따라가기 최대(루트 기준 m) — 앉은 머리에서 바닥 근처까지. 휠체어도 골반을 따라 바닥에 파묻힘(관리자 결정)

        public static int SetHeadHeight(AnimatorController action, Transform root, Transform holder, System.Func<AnimationClip, AnimationClip> newCopy, StringBuilder log) =>
            SetBoneHeight(action, root, holder, newCopy, log, HumanBodyBones.Head, "Head", "height",
                WcConst.HeadHeightTag, WcConst.HeadHeightParam, WcConst.HeadHeightTree, WcConst.HeadDownParam, WcConst.HeadDownTree, true);

        // VRChat 풀 바디 트래킹 일반 모드(9-40·9-41): Action 트래커 상태의 앉기(발 위치는 루트 기준이라 자세째 올려야 골반 밑에 옴)를
        //   골반 높이만큼. 골반 뼈는 골반 트래커를 따르므로 재는 값이 올린 자세에 되먹지 않는다. seat 패스가 이 클립도 올리므로 몸 올림 반영
        public static int SetHipHeight(AnimatorController action, Transform root, Transform holder, System.Func<AnimationClip, AnimationClip> newCopy, StringBuilder log) =>
            SetBoneHeight(action, root, holder, newCopy, log, HumanBodyBones.Hips, "Hip", "hip",
                WcConst.HipHeightTag, WcConst.HipHeightParam, WcConst.HipHeightTree, WcConst.HipDownParam, WcConst.HipDownTree, true);

        //   트리 자식은 프리팹에선 전부 앉기 클립 하나(올림 0 — 패스가 안 돌면 안 움직임). 올린/내린 클립은 여기서 새 사본으로 만든다(newCopy = 항상 새 빌드 사본)
        static int SetBoneHeight(AnimatorController ctrl, Transform root, Transform holder, System.Func<AnimationClip, AnimationClip> newCopy, StringBuilder log,
            HumanBodyBones boneId, string label, string tag, string contactTag, string upParam, string upTree, string downParam, string downTree, bool addSeatRaise)
        {
            if (!HasRig(holder)) { log.AppendLine($"{tag}: 조절 본 없음 — 건너뜀"); return 0; }
            var anim = root.GetComponent<Animator>();
            if (!SitByAvatar.TryGetValue(root.GetInstanceID(), out var sit) || sit == null || anim == null || !anim.isHuman) { log.AppendLine($"{tag}: 앉은 클립·휴머노이드 없음 — 건너뜀"); return 0; }
            var bone = anim.GetBoneTransform(boneId);
            var trees = ctrl.layers.SelectMany(l => AllStates(l.stateMachine)).SelectMany(st => Trees(st.motion))
                .Where(b => b.name == upTree).Distinct().ToList();
            if (bone == null || trees.Count == 0) { log.AppendLine($"{tag}: WARN {boneId} bone or '{upTree}' blend tree not found — VR height follow skipped"); return 0; }

            // 루트 기준 단위(루트 배율 sr). 반경은 local·world 어느 쪽에서 잘려도 3 이하가 되게
            float sr = Mathf.Max(1e-4f, root.lossyScale.x);
            float R = Mathf.Min(HeadRecvMaxR, HeadRecvMaxR / sr);
            float rs = HeadSenderR / sr;
            var boneSit = WithSitPose(root, sit, () => root.InverseTransformPoint(bone.position));
            if (addSeatRaise && RaiseByAvatar.TryGetValue(root.GetInstanceID(), out var raised)) boneSit.y += raised;   // seat 패스가 앉기 클립을 올렸다
            var center = new Vector3(boneSit.x, boneSit.y + HeadRestFrac * R, boneSit.z);
            float D = (HeadRestFrac - HeadTopFrac) * R;
            float V(float d) => 1f - (d - rs) / R;
            float v0 = V(HeadRestFrac * R), v1 = V(HeadTopFrac * R);

            var rgo = new GameObject($"WC_{label}HeightReceiver");
            rgo.transform.SetParent(root, false);
            var rc = rgo.AddComponent<VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver>();
            rc.shapeType = VRC.Dynamics.ContactBase.ShapeType.Sphere;
            rc.radius = R; rc.position = center;
            rc.receiverType = VRC.Dynamics.ContactReceiver.ReceiverType.Proximity;
            rc.parameter = upParam;
            rc.allowSelf = true; rc.allowOthers = false; rc.localOnly = false;
            rc.collisionTags = new List<string> { contactTag };
            var sgo = new GameObject($"WC_{label}HeightSender");
            sgo.transform.SetParent(bone, false);
            var sd = sgo.AddComponent<VRC.SDK3.Dynamics.Contact.Components.VRCContactSender>();
            sd.shapeType = VRC.Dynamics.ContactBase.ShapeType.Sphere;
            sd.radius = HeadSenderR / Mathf.Max(1e-4f, bone.lossyScale.x);
            sd.collisionTags = new List<string> { contactTag };

            foreach (var bt in trees)
            {
                var ch = bt.children;
                if (ch.Length < 2 || !(ch[0].motion is AnimationClip low) || !(ch[1].motion is AnimationClip)) { log.AppendLine($"{tag}: WARN unexpected '{upTree}' blend tree — VR height follow skipped for it"); continue; }
                var up = newCopy(low); up.name = low.name + "_Up";
                var b = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
                var cv = AnimationUtility.GetEditorCurve(low, b);
                if (cv == null) { log.AppendLine($"{tag}: WARN sitting animation has no RootT.y curve — VR height follow skipped"); return 0; }
                var ks = cv.keys; float add = D / anim.humanScale;
                for (int k = 0; k < ks.Length; k++) ks[k].value += add;
                cv.keys = ks;
                AnimationUtility.SetEditorCurve(up, b, cv);
                ch[0].threshold = v0; ch[1].threshold = v1; ch[1].motion = up;
                bt.useAutomaticThresholds = false;
                bt.children = ch;
                log.AppendLine($"{tag}: 앉은 {label} {boneSit.ToString("F3")}, Receiver 중심 {center.ToString("F3")} R {R:F3}(루트 배율 {sr:F3}) → v0 {v0:F4} · v1 {v1:F4}, 최대 올림 {D:F3}m (RootT.y +{add:F4}, humanScale {anim.humanScale:F3})");
            }

            // 아래로 따라가기: 두 번째 Receiver 중심 = 앉은 높이 R 아래 → 앉은 높이 이상이면 범위 밖(0), 내려간 L만큼 값 ≈ L / R.
            //   바깥 트리 [위 트리 @vd0, 내린 앉기 @vd1(내림 Dd)]. 값이 0이면(측정 안 됨 포함) 위 트리 = 바닥 앉기라 안전
            var downTrees = ctrl.layers.SelectMany(l => AllStates(l.stateMachine)).SelectMany(st => Trees(st.motion))
                .Where(b => b.name == downTree).Distinct().ToList();
            if (downTrees.Count > 0)
            {
                float Dd = Mathf.Min(HeadDownMax, 0.9f * R);
                var dcenter = new Vector3(boneSit.x, boneSit.y - R, boneSit.z);
                var dgo = new GameObject($"WC_{label}DownReceiver");
                dgo.transform.SetParent(root, false);
                var drc = dgo.AddComponent<VRC.SDK3.Dynamics.Contact.Components.VRCContactReceiver>();
                drc.shapeType = VRC.Dynamics.ContactBase.ShapeType.Sphere;
                drc.radius = R; drc.position = dcenter;
                drc.receiverType = VRC.Dynamics.ContactReceiver.ReceiverType.Proximity;
                drc.parameter = downParam;
                drc.allowSelf = true; drc.allowOthers = false; drc.localOnly = false;
                drc.collisionTags = new List<string> { contactTag };
                float vd0 = V(R), vd1 = V(R - Dd);
                var lowClip = trees[0].children[0].motion as AnimationClip;
                var bR = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
                var lowCv = lowClip != null ? AnimationUtility.GetEditorCurve(lowClip, bR) : null;
                foreach (var bt in downTrees)
                {
                    var ch = bt.children;
                    if (lowCv == null || ch.Length < 2 || !(ch[1].motion is AnimationClip)) { log.AppendLine($"{tag}: WARN unexpected '{downTree}' blend tree — VR follow-down skipped"); continue; }
                    var dn = newCopy(lowClip); dn.name = lowClip.name + "_Down";
                    var cv = new AnimationCurve(lowCv.keys);
                    var ks = cv.keys; float sub = Dd / anim.humanScale;
                    for (int k = 0; k < ks.Length; k++) ks[k].value -= sub;
                    cv.keys = ks;
                    AnimationUtility.SetEditorCurve(dn, bR, cv);
                    ch[0].threshold = vd0; ch[1].threshold = vd1; ch[1].motion = dn;
                    bt.useAutomaticThresholds = false;
                    bt.children = ch;
                    log.AppendLine($"{tag}: 아래로 — Receiver 중심 {dcenter.ToString("F3")} R {R:F3} → vd0 {vd0:F4} · vd1 {vd1:F4}, 최대 내림 {Dd:F3}m (RootT.y −{sub:F4})");
                }
            }
            return trees.Count;
        }

        // 몸 올림 적용(fitB 다음, height 전): 휠체어 레이어의 앉기 클립 RootT.y, 골반 추종 오프셋(Seat), 풀 트래킹 센서 상자.
        //   휠체어는 바닥에 그대로, 몸만 올라간다. 빌드 사본에만 쓴다
        public static void ApplySeatRaise(AnimatorController action, Transform root, Transform holder, System.Func<AnimationClip, AnimationClip> ensureTemp, StringBuilder log)
        {
            if (!RaiseByAvatar.TryGetValue(root.GetInstanceID(), out var dh) || Mathf.Abs(dh) < 1e-4f) { log.AppendLine("seat: 몸 올림 없음"); return; }
            var anim = root.GetComponent<Animator>();
            float add = dh / Mathf.Max(1e-4f, anim.humanScale);
            var bRoot = EditorCurveBinding.FloatCurve("", typeof(Animator), "RootT.y");
            var done = new Dictionary<AnimationClip, AnimationClip>();
            int raisedClips = 0;
            Motion Fix(Motion mo)
            {
                if (mo is BlendTree bt) { var ch = bt.children; for (int i = 0; i < ch.Length; i++) ch[i].motion = Fix(ch[i].motion); bt.children = ch; return bt; }
                var clip = mo as AnimationClip;
                if (clip == null) return mo;
                if (done.TryGetValue(clip, out var dn)) return dn;
                var cv = AnimationUtility.GetEditorCurve(clip, bRoot);
                if (cv == null) { done[clip] = clip; return clip; }
                var c = ensureTemp(clip);
                var ks = cv.keys; for (int k = 0; k < ks.Length; k++) ks[k].value += add;
                cv.keys = ks; AnimationUtility.SetEditorCurve(c, bRoot, cv);
                raisedClips++;
                done[clip] = c; return c;
            }
            foreach (var l in action.layers)
                if (l.name.StartsWith("Wheelchair Sit") || l.name.StartsWith("Wheelchair Arms"))
                    foreach (var st in AllStates(l.stateMachine)) st.motion = Fix(st.motion);
            log.AppendLine($"seat: 앉기 클립 {raisedClips}개 RootT.y +{add:F4}(몸 +{dh:F4} m, humanScale {anim.humanScale:F3})");

            var follow = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == WcConst.FollowName);
            var seat = follow != null ? follow.Find(WcConst.SeatName) : null;
            if (seat != null) { seat.localPosition += new Vector3(0, -dh, 0); log.AppendLine($"seat: 골반 추종 오프셋 Seat → {seat.localPosition.ToString("F4")}"); }
            else log.AppendLine("seat: WARN WC_HipRef/Seat not found — the chair may not follow the hips");
            if (follow != null)
                foreach (var n in new[] { "WC_BackSensor", "WC_ShinSensor" })
                {
                    var t = follow.Find(n); var rc = t != null ? t.GetComponent<VRC.Dynamics.ContactBase>() : null;
                    if (rc == null) continue;
                    rc.position += new Vector3(0, -dh, 0);
                    log.AppendLine($"seat: 센서 {n} 중심 → {rc.position.ToString("F3")}");
                }
        }

        static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine sm)
        {
            foreach (var cs in sm.states) yield return cs.state;
            foreach (var c in sm.stateMachines) foreach (var x in AllStates(c.stateMachine)) yield return x;
        }

        // ---------------------------------------------------------------- B
        public struct Measure
        {
            public Vector3 hip;            // 루트 기준
            public float halfWidth;        // 골반·허벅지 정점의 |x − 골반 x| 최댓값(좌석 높이 부근)
            public float[] soleMinY;       // [0]=왼발 [1]=오른발, 루트 기준
            public float[] heelZ;
            public float[] toeZ;
            public int samples;
            public List<Vector3> seatPts;  // 골반·허벅지 정점(좌석 높이 넓은 띠) — 좌석 관통 검사용, 루트 기준
        }

        static readonly HumanBodyBones[] SeatBones = { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg };
        static readonly HumanBodyBones[] FootL = { HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes };
        static readonly HumanBodyBones[] FootR = { HumanBodyBones.RightFoot, HumanBodyBones.RightToes };

        static Measure MeasureBody(Transform root, Transform holder, AnimationClip sit, StringBuilder log)
        {
            var anim = root.GetComponent<Animator>();
            // 정점의 부위 = 정점 본에서 위로 올라가 처음 만나는 휴머노이드 본(의상 본도 MA 병합 후엔 휴머노이드 본 밑에 있다).
            // 🔴 '조상 중 Hips가 있으면 좌석'으로 하면 팔·몸통도 전부 Hips 자손이라 걸린다(2026-10-17 첫 시험에서 반폭 0.37m)
            var human = new Dictionary<Transform, HumanBodyBones>();
            foreach (HumanBodyBones hb in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (hb == HumanBodyBones.LastBone) continue;
                var t = anim.GetBoneTransform(hb); if (t != null && !human.ContainsKey(t)) human[t] = hb;
            }
            var seat = new HashSet<HumanBodyBones>(SeatBones);
            var fl = new HashSet<HumanBodyBones>(FootL);
            var fr = new HashSet<HumanBodyBones>(FootR);
            var knee = anim.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            return WithSitPose(root, sit, () =>
            {
                var m = new Measure { soleMinY = new[] { 9f, 9f }, heelZ = new[] { 9f, 9f }, toeZ = new[] { -9f, -9f }, seatPts = new List<Vector3>() };
                m.hip = root.InverseTransformPoint(anim.GetBoneTransform(HumanBodyBones.Hips).position);
                float kneeZ = root.InverseTransformPoint(knee.position).z;
                var mesh = new Mesh();
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (!smr.enabled || !smr.gameObject.activeInHierarchy || smr.sharedMesh == null) continue;
                    if (smr.transform.IsChildOf(holder) || smr.CompareTag("EditorOnly")) continue;
                    var bones = smr.bones; var bw = smr.sharedMesh.boneWeights;
                    if (bw.Length == 0) continue;
                    smr.BakeMesh(mesh, true);   // 🔴 useScale — 렌더러 오브젝트에 배율이 있으면(Noem 신발 1.118) 기본값은 배율을 빼고 구워 발끝이 7.5cm 더 나온다(2026-10-17)
                    var vs = mesh.vertices;
                    for (int i = 0; i < vs.Length && i < bw.Length; i++)
                    {
                        // 가장 큰 가중치의 본(boneWeights는 가중치 내림차순)과 그 조상으로 부위 판별
                        var b = bw[i].boneIndex0 < bones.Length ? bones[bw[i].boneIndex0] : null;
                        if (b == null) continue;
                        int part = 0;   // 1 좌석 2 왼발 3 오른발
                        for (var t = b; t != null && t != root; t = t.parent)
                        {
                            HumanBodyBones hb;
                            if (!human.TryGetValue(t, out hb)) continue;
                            part = seat.Contains(hb) ? 1 : fl.Contains(hb) ? 2 : fr.Contains(hb) ? 3 : 0;
                            break;
                        }
                        if (part == 0) continue;
                        var p = root.InverseTransformPoint(smr.transform.TransformPoint(vs[i]));
                        if (part == 1 && p.y > m.hip.y - 0.30f && p.y < m.hip.y + 0.05f && p.z > m.hip.z - 0.30f && p.z < kneeZ + 0.05f) m.seatPts.Add(p);
                        if (part == 1)
                        {   // 좌석 높이 부근(골반 −0.15 ~ +0.05), 골반 뒤 0.15 ~ 무릎 사이만
                            if (p.y < m.hip.y - 0.15f || p.y > m.hip.y + 0.05f || p.z < m.hip.z - 0.15f || p.z > kneeZ) continue;
                            m.halfWidth = Mathf.Max(m.halfWidth, Mathf.Abs(p.x - m.hip.x)); m.samples++;
                        }
                        else
                        {
                            int k = part - 2;
                            m.soleMinY[k] = Mathf.Min(m.soleMinY[k], p.y);
                            m.heelZ[k] = Mathf.Min(m.heelZ[k], p.z);
                            m.toeZ[k] = Mathf.Max(m.toeZ[k], p.z);
                        }
                    }
                }
                Object.DestroyImmediate(mesh);
                return m;
            });
        }

        public static void FitParts(Transform root, Transform holder, StringBuilder log)
        {
            var set = Settings(holder);
            if (set != null && !set.autoFit) { log.AppendLine("fitB: autoFit 꺼짐 — 건너뜀"); return; }
            var sit = SitClip(holder) ?? (SitByAvatar.TryGetValue(root.GetInstanceID(), out var cached) ? cached : null);
            var model = holder.Find(WcConst.ModelName);
            var arm = model != null ? model.Find(ArmPath) : null;
            if (sit == null || arm == null) { log.AppendLine("fitB: 앉은 클립 또는 " + ArmPath + " 없음 — 건너뜀"); return; }
            Transform B(string n) => arm.Find(n);
            if (B("WC_Side_L") == null) { log.AppendLine("fitB: 조절 본 없음 — 건너뜀"); return; }

            var m = MeasureBody(root, holder, sit, log);
            float s = model.localScale.x;
            Vector3 off = model.localPosition;
            // 루트 기준 → 모델 설계 좌표(회전 없음, 배율 s, 원점 = 모델 위치)
            float halfModel = m.halfWidth / s;
            float sideDelta = Mathf.Max(0f, halfModel + ClothClear / s - GuardInnerX);
            log.AppendLine($"fitB: 좌석 정점 {m.samples}, 반폭 {m.halfWidth:F4}(모델 {halfModel:F4}) + 간격 {ClothClear} vs 보호판 안쪽 {GuardInnerX} → 넓힘 {sideDelta:F4}(한쪽)");
            Vector3 footAdd = Vector3.zero;
            if (set != null)
            {
                sideDelta += set.seatWidthOffset / s;
                footAdd = new Vector3(0, set.footHeightOffset, set.footForwardOffset) / s;
                if (set.seatWidthOffset != 0f || footAdd != Vector3.zero)
                    log.AppendLine($"fitB: 수동 보정 폭 {set.seatWidthOffset:F4} 발판 높이 {set.footHeightOffset:F4} 앞뒤 {set.footForwardOffset:F4}(m) → 폭 합계 {sideDelta:F4}(모델 단위)");
            }

            // 좌석 관통: 좌석 천 윗면(넓힌 폭으로 늘어난 포물선) 아래로 들어간 골반·허벅지 정점 중 가장 깊은 것
            float wSeat = RailX + sideDelta;
            float edgeY = SeatRailY + SeatTubeR + 0.0025f, sag = edgeY - SeatCenterY;
            float maxPen = float.NegativeInfinity; int inSeat = 0, tooDeep = 0; Vector2 deepAt = Vector2.zero;
            var pens = new List<float>();
            foreach (var p in m.seatPts)
            {
                float xm = (p.x - off.x) / s, zm = (p.z - off.z) / s;
                if (Mathf.Abs(xm) > wSeat || zm < SeatZ0 || zm > SeatZ1) continue;
                float xo = xm * RailX / wSeat;
                if (Mathf.Abs(xo) > SeatCoreFrac * RailX) continue;   // 가장자리(천이 레일로 올라가는 곳)는 좌석 폭 맞춤 몫
                float pen = (edgeY - sag * (1f - (xo / RailX) * (xo / RailX))) * s - p.y;
                inSeat++;
                if (pen > SeatProbeDepth) { tooDeep++; continue; }
                if (pen > maxPen) { maxPen = pen; deepAt = new Vector2(xm, zm); }
                pens.Add(pen);
            }
            pens.Sort();
            float depth = pens.Count > 0 ? pens[Mathf.Clamp(Mathf.RoundToInt(SeatPercentile * (pens.Count - 1)), 0, pens.Count - 1)] : float.NegativeInfinity;
            float autoRaise = pens.Count > 0 ? Mathf.Max(0f, depth + SeatClear) : 0f;
            if (autoRaise > SeatMaxRaise) { log.AppendLine($"fitB: WARN the hips go {depth:F3} m into the seat — raised only {SeatMaxRaise:F2} m; use Seat Height Offset if needed"); autoRaise = SeatMaxRaise; }
            float raise = autoRaise + (set != null ? set.seatHeightOffset : 0f);
            RaiseByAvatar[root.GetInstanceID()] = raise;
            log.AppendLine($"fitB: 좌석 관통 — 좌석 위 정점 {inSeat}(깊어서 뺌 {tooDeep}), 깊이 {SeatPercentile:P1} 분위 {(pens.Count > 0 ? depth.ToString("F4") : "-")}, 최대 {(pens.Count > 0 ? maxPen.ToString("F4") : "-")}(모델 x {deepAt.x:F3} z {deepAt.y:F3}) → 자동 올림 {autoRaise:F4}" + (set != null && set.seatHeightOffset != 0f ? $" + 수동 {set.seatHeightOffset:F4}" : "") + $" = 몸 올림 {raise:F4} m");
            footAdd.y += raise / s;   // 발도 몸과 같이 올라간다

            void Move(string bone, Vector3 d) { var t = B(bone); if (t != null) t.localPosition += d; }
            if (sideDelta != 0f)
            {
                foreach (var n in new[] { "WC_Side_L", "WC_Wheel_L", "WC_Caster_L" }) Move(n, new Vector3(-sideDelta, 0, 0));
                foreach (var n in new[] { "WC_Side_R", "WC_Wheel_R", "WC_Caster_R" }) Move(n, new Vector3(sideDelta, 0, 0));
            }

            string[] footBones = { "WC_Foot_L", "WC_Foot_R" };
            string[] tipBones = { "WC_FootTip_L", "WC_FootTip_R" };   // 리그 v8 — 없으면(v7) 판 길이 고정
            for (int k = 0; k < 2; k++)
            {
                var tip = B(tipBones[k]);
                if (m.soleMinY[k] > 8f) { Move(footBones[k], footAdd); Move(tipBones[k], footAdd); log.AppendLine($"fitB: {footBones[k]} — 발 정점 없음, 수동 보정만 {footAdd.ToString("F4")}"); continue; }
                float soleModel = m.soleMinY[k] / s;          // y는 모델 원점이 바닥(0)
                float heelModel = (m.heelZ[k] - off.z) / s;
                float toeModel = (m.toeZ[k] - off.z) / s;
                float dy = soleModel - RefSoleMinY;
                float dz = heelModel - RefHeelZ;
                float dzTip = StretchPlateToToe ? toeModel - RefToeZ : dz;   // 기본: 판 길이 고정(뒤끝과 같이)
                dy += footAdd.y; dz += footAdd.z; dzTip = StretchPlateToToe ? dzTip + footAdd.z : dz;
                // 바닥 뚫림 방지: 판 뒤끝 윗면 + dy가 MinPlateTopY 아래로 가지 않게
                float plateTop = (PlateBackTopY + dy) * s;
                if (plateTop < MinPlateTopY) dy += (MinPlateTopY - plateTop) / s;
                var foot = B(footBones[k]);
                Move(footBones[k], new Vector3(0, dy, dz));
                string tipLog = "";
                if (tip != null)
                {
                    float minTipZ = foot.localPosition.z + MinPlateLength - tip.localPosition.z;   // 판 최소 길이
                    if (dzTip < minTipZ) dzTip = minTipZ;
                    Move(tipBones[k], new Vector3(0, dy, dzTip));
                    tipLog = $", 발끝 z {m.toeZ[k]:F4} → 앞끝 이동 z {dzTip:F4} (판 길이 {tip.localPosition.z - foot.localPosition.z:F3})";
                }
                log.AppendLine($"fitB: {footBones[k]} 발바닥 {m.soleMinY[k]:F4} 뒤꿈치 z {m.heelZ[k]:F4} → 이동 (0, {dy:F4}, {dz:F4}) (모델 단위){tipLog}");
            }
            log.AppendLine($"fitB: 측면 본 x L {B("WC_Side_L").localPosition.x:F4} R {B("WC_Side_R").localPosition.x:F4}, 바퀴 x L {B("WC_Wheel_L").localPosition.x:F4} R {B("WC_Wheel_R").localPosition.x:F4}");
        }
    }
}
#endif
