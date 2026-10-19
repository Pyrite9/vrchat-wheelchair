// Pyrite9 Wheelchair — 앉은 채 AFK 모션 굽기(빌드 때, 아바타마다)
//   개발 모듈(Assets/_WheelchairAfk, Pyrite WheelchairPlus의 Lapwing 전용 굽기에서 옴)을 배포판 NDMF 패스로 옮긴 것(own-model 9-56).
//   프로젝트에 걷기 같은 동작 클립이 없다(SDK proxy_*는 1프레임 자리표시자) → 직접 만든다.
//   방식: 아바타 FBX를 숨긴 사본으로 꺼내 프레임마다
//     ① 바탕 muscle(앉기 ↔ 서기 proxy_idle 섞기 + 숙임·젖힘·옆구리·어깨·걸음 상체 오프셋)을 HumanPoseHandler로 세우고
//     ② 골반 위치·회전을 옮긴 뒤 ③ 발 목표를 2본 IK로 풀고 ④ 팔은 IK·FK를 무게로 섞어 ⑤ GetHumanPose로 muscle·RootT·RootQ를 키로 굽는다.
//   얼굴은 같은 시간표로 셰이프키 클립(FX) — 키는 VRC Avatar Descriptor에서 자동(WcAfkFaceMap: 눈 깜빡임·립싱크·흔한 이름의 웃음/눈물).
//   시퀀스: 발판 앞 바닥에 발 딛기 → 상체를 숙이며 일어서기 → 기지개(한쪽 또는 V자 만세, 하품·눈물·"후—") → 약 5초 유유히 걷기
//   아바타마다 재는 것(빌드 아바타 기준): 모델 FBX · 앉기 클립(좌석 올림 반영) · 휠체어 본(배율·좌석 폭·발판 맞춤 반영) ·
//     몸 배율 RH(서기 골반 / 0.824) · 팔 배율 RA(팔 길이 / 0.404) · 머리(머리 뼈 밑 정점) · HipComp(굽고 → 숨긴 사본에 입혀 재고 → 다시)
//   🔴 업로드 빌드는 편집 모드라 빌드 아바타 Animator가 그 자리에서 안 묶인다 → 재생 확인은 숨긴 FBX 사본에 SampleAnimation
//   결과 클립은 파일로 만들지 않고 save 콜백(빌드 임시 에셋)으로
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRC.SDK3.Avatars.Components;

namespace Pyrite9.Wheelchair
{
    public static class WcAfkMotion
    {
        const string StandClipPath = "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim/proxy_idle.anim";
        const float Fps = 30f;

        enum StretchMode { Arc, Clasp, OneArm }
        static string ModelPath, FacePath;
        static WcAfkFaceMap Faces;
        static System.Action<UnityEngine.Object> Save;
        static readonly Dictionary<string, AnimationClip> Out = new Dictionary<string, AnimationClip>();
        static StretchMode Mode = StretchMode.OneArm;   // 이번 굽기의 기지개(인스펙터 'AFK ▸ 모션')
        static float TopPhi = -6f;                     // Arc 다 올린 팔의 바깥 벌림(도)
        public const float VTopPhi = 50f;              // V자 만세 — Kipfel에서 40°는 −0.8cm, 50°는 +1.9cm(9-52-2)
        public const float AutoMinGap = 0f;            // 자동: 한쪽 기지개 팔이 머리를 뚫으면(간격 < 0) V자 만세로 — Milfy는 +0.1cm로 닿을락말락이지만 인게임 정상(관리자 10-19)이라 여유를 두지 않는다

        // ---- 휠체어 치수(아바타 루트 기준, Play 빌드에서 잼) ----
        static float PlateZ1;              // 발판 앞끝
        static Vector3 PadL, PadR;         // 팔걸이 패드 위 손목 자리

        // ---- 몸(Play 빌드에서 잼 — 기본값은 Lapwing) ----
        static float RH = 1, RA = 1, RHead = 1;   // 몸·팔·머리 배율
        const float RefStandHip = 0.824f, RefArm = 0.404f, RefHeadR = 0.10f;
        static readonly Vector3 RefHeadFromShoulderL = new Vector3(0.10f, 0.175f, 0.005f);   // Lapwing 머리 가운데 − 왼 어깨(서기)
        static Vector3 HeadFromShoulderL = RefHeadFromShoulderL;   // 이 아바타
        static float HipComp = 0.018f;     // 리그 골반 높이 → 실제 재생 골반 차 보정(Pyrite 8-1절) — 재서 고친다
        static float AnkleBehind = 0.11f;  // 서기 자세에서 발목이 골반보다 뒤
        static float FootX = 0.075f;       // 발 간격(중심선에서)
        static float BallLen = 0.10f;      // 발목 → 발볼(뒤꿈치 들 때 회전축)

        // ---- 걸음(유유히, 약 5초) — Lapwing 값을 배율로 ----
        static int Steps = 8;
        static float StepT = 0.6f;         // 발뒤꿈치 닿는 간격
        static float SwingT = 0.46f;       // 한 발이 공중에 있는 시간 → 양발 지지 0.14초
        static float StanceT => 2 * StepT - SwingT;
        static float VMax = 0.55f;         // m/s → 걸음 0.33m
        static float HeelRise = 0.26f, FlatT = 0.1f;
        const float ToeOff = 30f, HeelStrike = -14f, ToeOut = 7f;
        static float Bob = 0.011f, Sway = 0.017f;
        const float PelvisYaw = 5f, PelvisRoll = 2.5f, PelvisTilt = 3f;
        const float ArmSwing = 0.3f, ArmLag = 0.08f;

        static StringBuilder Log = new StringBuilder();

        public sealed class Result
        {
            public float Length, WalkStart, WalkEnd, HipComp, OneArmGap = 999f, HeadGap;
            public AfkMotion Motion;
            public AnimationClip Leave, Face, Sit;
            public string FacePath;
            public Dictionary<string, float> FaceBase;   // 표정 키 → 빌드 아바타 기본값(Reset 클립용)
            public List<string> BlinkKeys; public HashSet<string> EyeKeys;
        }

        static void ScaleGait(float rh)
        {
            float s = Mathf.Sqrt(rh);
            StepT = 0.6f * s; SwingT = 0.46f * s; VMax = 0.55f * s; HeelRise = 0.26f * s; FlatT = 0.1f * s;
            Bob = 0.011f * rh; Sway = 0.017f * rh;
            Steps = Mathf.Max(6, Mathf.RoundToInt(8f / s));
        }

        // 머리 둘레 경로: Lapwing 왼 어깨 기준 점 → 머리 가운데 기준으로 옮겨 머리 크기만큼 늘린 뒤 이 아바타 왼 어깨 기준으로
        static Vector3 HeadRel(Vector3 lapOffL) => HeadFromShoulderL + (lapOffL - RefHeadFromShoulderL) * RHead;
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z) * RH;   // 몸 배율
        static Vector3 VA(float x, float y, float z) => new Vector3(x, y, z) * RA;  // 팔 배율

        // 빌드 아바타에서 재고 굽는다. 굽고 → 숨긴 사본에 입혀 골반 높이 차를 재고 → 한 번 더(HipComp)
        //   setting: Auto(한쪽 기지개 → 팔이 머리를 뚫으면 V자 만세) · OneArm · BothArms. save = 새로 만든 클립을 빌드 임시 에셋으로
        public static Result Bake(Transform built, AfkMotion setting, WcAfkFaceMap faces, System.Action<UnityEngine.Object> save, StringBuilder log)
        {
            Log = log; Save = save; Faces = faces; Out.Clear();
            Log.AppendLine($"afkmotion: 굽기 — {built.name}");
            FacePath = faces.Path; Built = built;
            var anim = built.GetComponent<Animator>();
            ModelPath = AssetDatabase.GetAssetPath(anim.avatar);
            if (string.IsNullOrEmpty(ModelPath) || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) throw new Exception("avatar model asset not found (" + (anim.avatar ? anim.avatar.name : "no avatar") + ")");
            Log.AppendLine("afkmotion: 모델 " + ModelPath + ", 루트 배율 " + built.lossyScale.x.ToString("F4"));
            var sitClip = BuiltSitClip(built);
            if (sitClip == null) throw new Exception("sitting clip not found in the built Action layer");
            MeasureChair(built);
            Result Passes()
            {
                var rr = new Result();
                float comp = float.NaN;
                for (int pass = 0; pass < 3; pass++)
                {
                    Log.AppendLine($"afkmotion: --- 굽기 {pass + 1}차 ({Mode}{(Mode == StretchMode.Arc ? $" {TopPhi:F0}°" : "")})");
                    rr = BakeOnce(sitClip, comp);
                    // 리그 목표 = 서기 골반 + HipComp, 재생 = 목표 − δ → 재생이 서기 골반이 되려면 HipComp = δ(= 계획 − 실제)
                    float delta = VerifyHips(rr);
                    bool done = Mathf.Abs(delta - HipComp) < 0.003f;
                    comp = delta;
                    if (done) break;
                }
                return rr;
            }
            float oneArmGap = 999f;
            if (setting == AfkMotion.BothArms) { Mode = StretchMode.Arc; TopPhi = VTopPhi; Log.AppendLine("afkmotion: 기지개 = V자 만세(인스펙터)"); }
            else { Mode = StretchMode.OneArm; Log.AppendLine(setting == AfkMotion.OneArm ? "afkmotion: 기지개 = 한쪽(인스펙터)" : "afkmotion: 기지개 = 자동 — 한쪽부터"); }
            var r = Passes();
            if (Mode == StretchMode.OneArm) oneArmGap = MinHeadGap;
            if (setting == AfkMotion.Auto && MinHeadGap < AutoMinGap)
            {
                Log.AppendLine($"afkmotion: 자동 — 한쪽 기지개 팔-머리 간격 {MinHeadGap * 100:F1}cm(뚫음) → V자 만세로 다시");
                Mode = StretchMode.Arc; TopPhi = VTopPhi;
                r = Passes();
            }
            else if (setting == AfkMotion.Auto) Log.AppendLine($"afkmotion: 자동 — 한쪽 기지개 간격 {MinHeadGap * 100:F1}cm, 그대로");
            if (MinHeadGap < 0) Log.AppendLine($"afkmotion: WARN the AFK stretch arm goes {-MinHeadGap * 100:F1} cm into the head — try the other AFK motion in the inspector");
            // 1초짜리 앉기(Back 크로스페이드·Restore) — 빌드 앉기 클립 값 그대로
            var sit = NewClip("AFK_Sit");
            foreach (var bnd in AnimationUtility.GetCurveBindings(sitClip))
            {
                float v = AnimationUtility.GetEditorCurve(sitClip, bnd).Evaluate(0);
                AnimationUtility.SetEditorCurve(sit, bnd, new AnimationCurve(new Keyframe(0, v), new Keyframe(1, v)));
            }
            var ss = AnimationUtility.GetAnimationClipSettings(sit);
            ss.loopTime = false; ss.loopBlendOrientation = ss.keepOriginalOrientation = true; ss.loopBlendPositionY = ss.keepOriginalPositionY = true; ss.loopBlendPositionXZ = ss.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(sit, ss);
            r.Motion = Mode == StretchMode.OneArm ? AfkMotion.OneArm : AfkMotion.BothArms;
            r.OneArmGap = oneArmGap; r.HeadGap = MinHeadGap;
            r.Leave = Out["AFK_Leave"]; r.Face = Out.TryGetValue("AFK_Face", out var fcl) ? fcl : null; r.Sit = sit;
            r.FacePath = FacePath; r.FaceBase = LastFaceBase; r.BlinkKeys = faces.Blink; r.EyeKeys = faces.EyeKeys;
            foreach (var c in new[] { r.Leave, r.Face, r.Sit }) if (c != null) Save(c);
            Log.AppendLine($"afkmotion: 굽기 끝 — {r.Motion}, 길이 {r.Length:F2}초, 걷기 {r.WalkStart:F2}~{r.WalkEnd:F2}, 팔-머리 {MinHeadGap * 100:F1}cm, RH {RH:F3} RA {RA:F3} RHead {RHead:F3}");
            return r;
        }

        static Dictionary<string, float> LastFaceBase;

        // 새 클립(같은 이름은 이번 굽기 안에서 바꿔 끼움 — 마지막 것만 save)
        static AnimationClip NewClip(string name)
        {
            var c = new AnimationClip { name = name };
            Out[name] = c;
            return c;
        }

        static AnimationClip BuiltSitClip(Transform built)
        {
            var desc = built.GetComponent<VRCAvatarDescriptor>();
            var act = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action).animatorController as AnimatorController;
            var layer = act?.layers.FirstOrDefault(l => l.name == "Wheelchair Sit");
            var st = layer?.stateMachine.states.Select(c => c.state).FirstOrDefault(s => s.name == "On VR");
            Motion m = st?.motion;
            for (int d = 0; d < 8 && m != null; d++)
            {
                if (m is AnimationClip c) return c;
                m = m is BlendTree bt && bt.children.Length > 0 ? bt.children[0].motion : null;
            }
            return null;
        }

        // 휠체어 본을 모델 설계 좌표로 읽고(배율 무관), 모델 local 변환으로 아바타 루트 기준 좌표로 — 휠체어가 Play 중 어디에 있든 같은 값
        static void MeasureChair(Transform built)
        {
            var all = built.GetComponentsInChildren<Transform>(true);
            Transform B(string n) => all.First(t => t.name == n);
            var wroot = B("WC_Root");
            var model = wroot.parent.parent;   // WheelChair(모델) ← WheelChairArmature ← WC_Root
            var holder = model.parent;         // Wheelchair(휠체어 루트 — fitA가 아바타 루트 직속 local 0으로)
            Vector3 ToRoot(Vector3 design) => built.InverseTransformPoint(holder.TransformPoint(model.localPosition + model.localRotation * Vector3.Scale(model.localScale, design)));
            Vector3 D(Transform t) => model.InverseTransformPoint(t.position);
            PlateZ1 = (ToRoot(D(B("WC_FootTip_L"))).z + ToRoot(D(B("WC_FootTip_R"))).z) * 0.5f;
            PadL = ToRoot(D(B("WC_Side_L")) + new Vector3(-0.025f, 0.185f, 0.06f));   // Lapwing(배율 1): 옆 본 (−0.165, 0.48, 0) → 패드 (−0.19, 0.665, 0.06)
            PadR = ToRoot(D(B("WC_Side_R")) + new Vector3(0.025f, 0.185f, 0.06f));
            Log.AppendLine($"afkmotion: 휠체어: 모델 배율 {model.localScale.x:F4} pos {model.localPosition:F3}, 발판 앞끝 z {PlateZ1:F3}, 팔걸이 패드 L {PadL:F3} R {PadR:F3} (Lapwing 0.55 / ±0.19, 0.665, 0.06)");
        }

        // 굽은 클립을 숨긴 FBX 사본에 입혀(SampleAnimation — 편집 모드 업로드 빌드에서도 됨) 서 있는·걷는 구간 골반 높이를 계획과 비교 → 평균 차(+면 실제가 낮음)
        static float VerifyHips(Result r)
        {
            var clip = Out["AFK_Leave"];
            float sum = 0; int n = 0; var sb = new StringBuilder();
            using (var rig = new Rig())
            {
                var hips = rig["Hips"];
                foreach (float t in new[] { r.WalkStart - 0.3f, r.WalkStart + 1.5f, r.WalkStart + 2.5f, r.WalkStart + 3.5f, r.WalkEnd + 0.4f })
                {
                    clip.SampleAnimation(rig.Go, t);
                    var y = rig.Go.transform.InverseTransformPoint(hips.position).y;
                    float want = LastPlan.HipsAt(t).y;
                    sum += want - y; n++;
                    sb.Append($" {t:F1}s 계획 {want:F3}/실제 {y:F3}");
                }
            }
            float dy = sum / n;
            Log.AppendLine($"afkmotion: 골반 재생 확인:{sb} → 평균 차 {dy * 100:F2}cm (HipComp {HipComp:F4})");
            return dy;
        }

        static Plan LastPlan;

        // ================================================================ 리그
        sealed class Rig : IDisposable
        {
            public readonly GameObject Go;
            public readonly HumanPoseHandler Hph;
            public readonly Dictionary<string, Transform> B = new Dictionary<string, Transform>();
            public Transform this[string n] => B[n];
            public Vector3 ElbowAxisL, ElbowAxisR;   // 팔꿈치 경첩 축(위팔 지역) — 굽힌 자세에서 잰다
            public Quaternion HeadRot0; public Vector3 HeadCenter;   // 점검용(서기 자세 머리 방향, 머리 메시 가운데)
            public Quaternion ChestRot0;                             // 서기 자세 가슴 방향(한쪽 기지개 팔 목표 기준)
            public Transform ChestTop => B.TryGetValue("UpperChest", out var uc) ? uc : B["Chest"];   // Lapwing은 UpperChest 뼈가 없다

            public Rig()
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                Go = UnityEngine.Object.Instantiate(model);
                Go.hideFlags = HideFlags.HideAndDontSave;
                Go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var avatar = Go.GetComponent<Animator>().avatar;
                var all = Go.GetComponentsInChildren<Transform>(true);
                foreach (var hb in avatar.humanDescription.human)
                {
                    var t = all.FirstOrDefault(x => x.name == hb.boneName);
                    if (t != null) B[hb.humanName] = t;
                }
                Hph = new HumanPoseHandler(avatar, Go.transform);
            }

            public void Dispose() { Hph.Dispose(); UnityEngine.Object.DestroyImmediate(Go); }

            // 발끝 점(발 지역 좌표) — 서기 자세에서 부른다. 발가락 뼈가 없으면(Kipfel) 발목 앞 0.10·RH, 발목 높이의 70% 아래
            readonly Dictionary<string, Vector3> toeLocal = new Dictionary<string, Vector3>();
            public void SetToes(float rh)
            {
                foreach (var sd in new[] { "Left", "Right" })
                {
                    var ft = B[sd + "Foot"];
                    var p = B.TryGetValue(sd + "Toes", out var toes) ? toes.position : ft.position + new Vector3(0, -0.7f * ft.position.y, 0.10f * rh);
                    toeLocal[sd] = ft.InverseTransformPoint(p);
                }
            }
            public Vector3 ToePos(string sd) => B[sd + "Foot"].TransformPoint(toeLocal[sd]);
        }

        // ================================================================ muscle 이름
        static readonly string[] MName = HumanTrait.MuscleName;
        static readonly Dictionary<string, int> MIdx = MName.Select((n, i) => (n, i)).ToDictionary(x => x.n, x => x.i);

        static string ClipName(string muscle)   // "Left Index 1 Stretched" → "LeftHand.Index.1 Stretched", "Left Index Spread" → "LeftHand.Index.Spread"
        {
            var p = muscle.Split(' ');
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            if (p.Length >= 3 && fingers.Contains(p[1]))
                return char.IsDigit(p[2][0]) ? $"{p[0]}Hand.{p[1]}.{p[2]} {string.Join(" ", p.Skip(3))}" : $"{p[0]}Hand.{p[1]}.{p[2]}";
            return muscle;
        }

        struct Pose { public float[] M; public Vector3 T; public Quaternion Q; }

        static Pose FromClip(AnimationClip clip)
        {
            float V(string attr, float def)
            {
                var c = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), attr));
                return c != null && c.length > 0 ? c.Evaluate(0) : def;
            }
            var p = new Pose { M = new float[MName.Length] };
            for (int i = 0; i < MName.Length; i++) p.M[i] = V(ClipName(MName[i]), 0);
            p.T = new Vector3(V("RootT.x", 0), V("RootT.y", 1), V("RootT.z", 0));
            p.Q = new Quaternion(V("RootQ.x", 0), V("RootQ.y", 0), V("RootQ.z", 0), V("RootQ.w", 1)).normalized;
            return p;
        }

        static float[] Lerp(float[] a, float[] b, float t) { var r = new float[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = Mathf.Lerp(a[i], b[i], t); return r; }
        static void Add(float[] m, string name, float v) { m[MIdx[name]] += v; }
        static void SetM(float[] m, string name, float v, float w) { int i = MIdx[name]; m[i] = Mathf.Lerp(m[i], v, w); }
        static float S(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }                 // smoothstep
        static float Seg(float t, float a, float b) => S((t - a) / (b - a));                          // a→b 구간 0→1
        static float Win(float t, float a, float b, float rampIn, float rampOut) => Seg(t, a, a + rampIn) * (1 - Seg(t, b - rampOut, b));
        static float EaseS(float x) { x = Mathf.Clamp01(x); return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * x); }   // 사인 가감속
        // 어깨 기준 팔 방향: theta = 아래(0°)에서 앞으로 올려 위(180°)까지, phi = 바깥(+)/안(−) 벌림, s = −1 왼팔 / +1 오른팔
        static Vector3 ArmDir(float s, float thetaDeg, float phiDeg)
        {
            float th = thetaDeg * Mathf.Deg2Rad, ph = phiDeg * Mathf.Deg2Rad;
            return new Vector3(s * Mathf.Sin(ph), -Mathf.Cos(th) * Mathf.Cos(ph), Mathf.Sin(th) * Mathf.Cos(ph)).normalized;
        }

        // ================================================================ 경로(키 위치 — 구간마다 부드럽게/등속, 들어올림)
        sealed class Path
        {
            readonly List<(float t, Vector3 p, float lift, bool linear)> k = new List<(float, Vector3, float, bool)>();
            public Path Key(float t, Vector3 p, float lift = 0, bool linear = false) { k.Add((t, p, lift, linear)); return this; }
            public Path Key(float t, float v) => Key(t, new Vector3(v, 0, 0));
            public float F(float t) => At(t).x;
            public Vector3 At(float t)
            {
                if (k.Count == 0) return Vector3.zero;
                if (t <= k[0].t) return k[0].p;
                for (int i = 1; i < k.Count; i++)
                    if (t <= k[i].t)
                    {
                        float u = (t - k[i - 1].t) / Mathf.Max(1e-4f, k[i].t - k[i - 1].t);
                        var p = Vector3.Lerp(k[i - 1].p, k[i].p, k[i].linear ? u : S(u));
                        p.y += k[i].lift * Mathf.Sin(Mathf.PI * u);
                        return p;
                    }
                return k[k.Count - 1].p;
            }
        }

        // Hermite 스플라인 — 경유점에서 멈추지 않는다(내부 접선 = 앞뒤 키 기울기), 양 끝만 속도 0
        sealed class Spline
        {
            readonly List<(float t, Vector3 p)> k = new List<(float, Vector3)>();
            public Spline Key(float t, Vector3 p) { k.Add((t, p)); return this; }
            Vector3 M(int i) => i == 0 || i == k.Count - 1 ? Vector3.zero : (k[i + 1].p - k[i - 1].p) / (k[i + 1].t - k[i - 1].t);
            public Vector3 At(float t)
            {
                if (t <= k[0].t) return k[0].p;
                for (int i = 1; i < k.Count; i++)
                    if (t <= k[i].t)
                    {
                        float h = k[i].t - k[i - 1].t, u = (t - k[i - 1].t) / h, u2 = u * u, u3 = u2 * u;
                        return (2 * u3 - 3 * u2 + 1) * k[i - 1].p + (u3 - 2 * u2 + u) * h * M(i - 1) + (-2 * u3 + 3 * u2) * k[i].p + (u3 - u2) * h * M(i);
                    }
                return k[k.Count - 1].p;
            }
        }

        // 호 — 전체는 부드럽게 출발·정지, z는 앞서고(zPow < 1) y는 뒤따른다(yPow > 1). 촘촘한 등속 키로 이어 붙임
        static void Arc(Path p, float t0, float t1, Vector3 a, Vector3 b, int n, float zPow, float yPow)
        {
            for (int i = 1; i <= n; i++)
            {
                float s = (float)i / n, e = S(s);
                p.Key(Mathf.Lerp(t0, t1, s), new Vector3(Mathf.Lerp(a.x, b.x, e), Mathf.Lerp(a.y, b.y, Mathf.Pow(e, yPow)), Mathf.Lerp(a.z, b.z, Mathf.Pow(e, zPow))), 0, true);
            }
        }

        // ================================================================ 발 시간표 — 디딘 자리 + 옮김(걸음이면 발 굴림)
        sealed class Foot
        {
            public readonly List<(float t0, float t1, Vector3 to, float lift, bool walk)> Ev = new List<(float, float, Vector3, float, bool)>();
            public Vector3 Start;
            public Vector3 Last => Ev.Count > 0 ? Ev[Ev.Count - 1].to : Start;
            public void Move(float t0, float t1, Vector3 to, float lift, bool walk) => Ev.Add((t0, t1, to, lift, walk));

            // 발목 위치와 굴림 각도(+ = 뒤꿈치 듦)
            public Vector3 At(float t, out float pitch)
            {
                var cur = Start; float prevEnd = -99; bool prevWalk = false;
                pitch = 0;
                foreach (var e in Ev)
                {
                    if (t < e.t0)
                    {
                        // 디딘 채: 다음이 걸음이면 떼기 전 뒤꿈치 듦, 직전이 걸음이면 착지 직후 발바닥 내림
                        if (e.walk) pitch = Mathf.Max(pitch, ToeOff * S(1 - (e.t0 - t) / HeelRise));
                        if (prevWalk && t - prevEnd < FlatT) pitch = Mathf.Min(pitch, HeelStrike * (1 - S((t - prevEnd) / FlatT)));
                        return Lift(cur, pitch);
                    }
                    if (t <= e.t1)
                    {
                        float u = (t - e.t0) / (e.t1 - e.t0);
                        var p = Vector3.Lerp(cur, e.to, S(u));
                        if (e.walk)
                        {
                            p.y += e.lift * Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.85f));   // 앞쪽에서 빨리 들고 길게 내려놓기
                            pitch = u < 0.45f ? ToeOff * (1 - S(u / 0.45f)) : HeelStrike * S((u - 0.45f) / 0.55f);
                        }
                        else p.y += e.lift * Mathf.Sin(Mathf.PI * u);
                        return Lift(p, pitch);
                    }
                    cur = e.to; prevEnd = e.t1; prevWalk = e.walk;
                }
                if (prevWalk && t - prevEnd < FlatT) pitch = HeelStrike * (1 - S((t - prevEnd) / FlatT));
                return Lift(cur, pitch);
            }

            static Vector3 Lift(Vector3 p, float pitch) { if (pitch > 0) p.y += BallLen * Mathf.Sin(pitch * Mathf.Deg2Rad); return p; }
        }

        // ================================================================ 시간표
        sealed class Plan
        {
            public Foot FootL = new Foot(), FootR = new Foot();
            public Func<float, Vector3> HipsAt;
            public Func<float, Vector3> PelvisRot;              // (기울기 x, 회전 y, 옆 기울기 z) 도
            public Func<float, float> HandIk, HandHeadW;          // 팔 IK 무게, 손 목표를 머리 기준으로 붙이는 무게(지금은 0 — 어깨 기준으로 바꿈)
            public Func<float, float> ArmW, Palm;                 // 어깨 기준 팔 목표 무게, 손바닥 위로(손목 젖힘)
            public Func<float, Vector3> ArmOffL, ArmOffR;         // 어깨(UpperArm) → 손목 목표(기지개 올리기·유지·내리기)
            public Func<float, Vector3> ClaspAt;                  // 깍지 점(두 어깨 가운데 기준)
            public Func<float, float> ClaspW, ClaspHalfW, Interlace, PalmsUp;   // 깍지 목표 무게, 두 손목 반 간격, 손가락 깍지·손 방향 무게, 손바닥 바깥으로(앞 → 머리 위에선 하늘)
            public Quaternion HandLocalL, HandLocalR;             // 손 본의 (손가락 방향, 엄지 쪽) 지역 틀(지금은 안 씀 — 9-10절)
            public Func<float, float> FingerOpen;                 // 깍지 직전 손가락 펼쳐 벌리기(풀 때도)
            public Func<float, int, float> FingerCurl;            // 손가락별 깍지 굽힘(0 검지 … 3 새끼, 4 엄지) — 차례로
            public Func<float, Vector3> HandLAt, HandRAt;         // 팔 IK 목표(손목, 월드)
            public Vector3 OverL, OverR;                          // 머리 기준 기지개 손 자리
            public Func<float, float> Stand, Lean, Arch, Reach, Side, Exhale, FootFlat, WalkW, Look;
            public Func<float, float> ArmSig;                     // 팔 흔들기 신호(+ = 오른다리가 앞)
            public Func<float, Dictionary<string, float>> Face;
            public float Length, WalkStart, WalkEnd, StretchStart;
            // 한쪽 기지개(9-11절) — 없으면(null) 깍지 기지개 그대로
            public Func<float, float> Tip;                        // 까치발 무게(없으면 Arch)
            public float TipDeg = 14f, ReachKL = 0.35f, ReachKR = 0.35f;
            // Arch 때 등(Spine·Chest·UpperChest Front-Back)·고개(Neck·Head Nod) — Front-Back·Nod는 +가 젖힘·위(골반 고정 실측, 9-11절).
            //   깍지 기지개의 음수 값은 실제로는 숙임이었다(몸 방향 보존으로 골반이 뒤로 기울어 가려져 있었음) — 그 모드는 그대로 둔다
            public Vector3 ArchTrunk = new Vector3(-0.28f, -0.25f, -0.2f);
            public Vector2 ArchNod = new Vector2(-0.25f, -0.35f);
            public Func<float, float> SideL;                      // 왼쪽(잡는 팔 쪽)으로 옆구리 늘리기
            public Func<float, float> GripW, GripOpen, GripCurl, Fist;   // 왼손 목표 → 오른 팔꿈치 밑, 왼손 펴기·감아쥐기, 오른손 주먹
            public float GripAlong;                               // 잡는 자리(오른 어깨 0 → 팔꿈치 1)
            public Vector2 GripOff;                               // 잡는 자리 → 왼 손목(팔 기준 뒤, 안쪽)
            public Vector3 GripWrist;                             // 잡을 때 왼 손목 muscle(Hand Down-Up, Hand In-Out, Forearm Twist)
            public Func<float, Vector3> PoleL, PoleR;             // 팔꿈치 방향(없으면 기존)
            public bool RollArms;                                 // 어깨 기준 팔 목표를 가슴(UpperChest, 없으면 Chest) 회전만큼 같이 돌린다
        }

        enum Stretch { Arc, Clasp, OneArm }

        // 한쪽 기지개 치수 — 올린 오른팔 방향(θ 위 180°, 넘으면 뒤), 바깥 벌림
        const float OneTopTh = 194f, OneTopPh = 8f, SideSign = -1f;   // Left-Right muscle +는 몸 오른쪽(+x)으로 굽힘(실측) → 왼쪽은 −

        static Plan MakePlan(Rig rig, Pose sit, Pose stand, Stretch mode)
        {
            bool interlace = mode == Stretch.Clasp, one = mode == Stretch.OneArm;
            Apply(rig, sit);
            var sitFootL = rig["LeftFoot"].position; var sitFootR = rig["RightFoot"].position;
            var sitHandL = rig["LeftHand"].position; var sitHandR = rig["RightHand"].position;
            var sitHips = rig["Hips"].position;
            Apply(rig, stand);
            var stHips = rig["Hips"].position;
            var stHandL = rig["LeftHand"].position - stHips; var stHandR = rig["RightHand"].position - stHips;
            var headUp = rig["Head"].position - stHips;
            float armLen = (rig["LeftLowerArm"].position - rig["LeftUpperArm"].position).magnitude + (rig["LeftHand"].position - rig["LeftLowerArm"].position).magnitude;   // 어깨 → 손목(0.404)
            float ankleY = (rig["LeftFoot"].position.y + rig["RightFoot"].position.y) * 0.5f;
            float standHipY = stHips.y + HipComp - 0.004f * RH;
            Quaternion LocalFrame(string sd)
            {
                var h = rig[sd + "Hand"]; var inv = Quaternion.Inverse(h.rotation);
                return Quaternion.LookRotation(inv * (rig[sd + " Middle Proximal"].position - h.position), inv * (rig[sd + " Index Proximal"].position - rig[sd + " Little Proximal"].position));
            }
            var frameL = LocalFrame("Left"); var frameR = LocalFrame("Right");
            var p = new Plan { HandLocalL = frameL, HandLocalR = frameR };
            p.FootL.Start = sitFootL; p.FootR.Start = sitFootR;

            // ---------------- ① 발 딛기 ② 숙이며 팔걸이 짚기 ③ 엉덩이 뜨기·발 위로 ④ 상체 펴며 서기 (sit-to-stand)
            var flFloor = new Vector3(-FootX, ankleY, PlateZ1 + 0.07f * RH);   // 발판 바로 앞 바닥 — 뒤꿈치가 발판 앞끝보다 앞
            var frFloor = new Vector3(FootX, ankleY, PlateZ1 + 0.07f * RH);
            p.FootL.Move(0.10f, 0.55f, flFloor, 0.03f * RH, false);
            p.FootR.Move(0.18f, 0.63f, frFloor, 0.03f * RH, false);
            var standAt = new Vector3(0, standHipY, flFloor.z + AnkleBehind);
            var edge = sitHips + V(0, 0.005f, 0.12f);
            float seatOff = 1.15f, upright = 2.15f, settle = 2.35f;
            var hips = new Path();
            hips.Key(0, sitHips).Key(0.25f, sitHips).Key(0.8f, edge).Key(seatOff, edge + V(0, 0, 0.03f));
            Arc(hips, seatOff, upright, edge + V(0, 0, 0.03f), standAt + V(0, -0.01f, -0.02f), 12, 0.65f, 1.5f);
            hips.Key(settle, standAt);
            Func<float, float> trunk = tt => 38f * Seg(tt, 0.45f, 1.2f) * (1 - Seg(tt, 1.45f, 2.25f));   // 골반째 숙임(도)
            p.Lean = tt => Seg(tt, 0.45f, 1.15f) * (1 - Seg(tt, 1.5f, 2.3f));
            p.Stand = tt => Seg(tt, 1.0f, 2.2f);
            p.FootFlat = tt => Seg(tt, 0.2f, 0.65f);
            var handL = new Path(); var handR = new Path();   // 팔걸이 짚기까지(일부러 멈추는 동작이라 smoothstep)
            handL.Key(0, sitHandL).Key(0.4f, sitHandL).Key(0.95f, PadL).Key(1.45f, PadL);
            handR.Key(0, sitHandR).Key(0.45f, sitHandR).Key(1.0f, PadR).Key(1.5f, PadR);
            // 팔걸이에서 떼어 옆으로(월드 목표) — 2.0초부터는 어깨 기준 목표가 이어받는다
            //   옆으로 돌지 않고 바로 허벅지 앞으로(옆을 거치면 손 1.68m/s로 빨랐다 — 9-10절)
            var offL = new Spline().Key(1.45f, PadL).Key(2.25f, standAt + new Vector3(-0.13f * RH, stHandL.y + 0.04f * RH, 0.13f * RH));
            var offR = new Spline().Key(1.5f, PadR).Key(2.3f, standAt + new Vector3(0.13f * RH, stHandR.y + 0.04f * RH, 0.13f * RH));
            Func<float, Vector3> handAtL = tt => tt < 1.45f ? handL.At(tt) : offL.At(tt);
            Func<float, Vector3> handAtR = tt => tt < 1.5f ? handR.At(tt) : offR.At(tt);

            // ---------------- 기지개 — 일어서며 두 손을 배 앞에 모아 깍지(2.55초) → 깍지 낀 채 두 어깨 가운데를 축으로 앞→위로(2.75~4.05초)
            //   → 머리 위에서 손바닥을 하늘로 뒤집고 젖히며 까치발, 좌우 옆구리 → 깍지 풀고 옆으로 크게 내리며 "후—"(9-8절, 관리자 10-19)
            //   깍지 점 반지름이 배 앞 0.36 → 머리 위 0.395로 고르게 커져 팔꿈치가 130° → 172°로 서서히 펴진다(마지막에 한꺼번에 펴지지 않게).
            //   interlace = false면 9-7절의 어깨 중심 큰 원(팔 거의 편 채) — 지금은 굽지 않는다
            float sA = settle;                     // 골반·숙임 경로 전환 시점(일어서기 끝)
            p.StretchStart = sA;
            float c1 = 2.55f;                      // 깍지 완성
            float r0 = interlace ? 2.85f : 2.05f, up1 = interlace ? 4.05f : one ? 3.85f : 3.45f, hold1 = up1 + (one ? 1.1f : 1.0f), down1 = hold1 + (one ? 1.35f : 1.25f), end = down1 + 0.25f;
            Vector3 Top(float s) => ArmDir(s, 172f, TopPhi);
            Vector3 Low(float s) => ArmDir(s, 8f, 10f) * (0.99f * armLen);
            var clBelly = VA(0, -0.30f, 0.20f);
            float thB = Mathf.Atan2(clBelly.z, -clBelly.y) * Mathf.Rad2Deg, rB = clBelly.magnitude;
            Vector3 Sag(float thDeg, float r) { float th = thDeg * Mathf.Deg2Rad; return new Vector3(0, -Mathf.Cos(th), Mathf.Sin(th)) * r; }
            Vector3 ClaspAt(float tt)
            {
                if (tt < c1) return Vector3.Lerp(VA(0, -0.36f, 0.12f), clBelly, Seg(tt, 1.9f, c1));   // 허벅지 앞 → 배 앞
                if (tt < r0) return clBelly;
                if (tt <= up1) { float E = EaseS((tt - r0) / (up1 - r0)); return Sag(Mathf.Lerp(thB, 172f, E), Mathf.Lerp(rB, 0.970f * armLen, S(E / 0.45f)) + 0.005f * armLen * E); }   // 손바닥 뒤집으며 앞으로 밀 때 이미 팔을 폄
                return Sag(172f, Mathf.Lerp(0.975f, 0.980f, Seg(tt, up1, up1 + 0.4f)) * armLen);   // 위로 더 뻗기 — 팔 최대 길이(0.399)에 붙이면 옆구리 때 팔꿈치가 폈다 굽혔다 출렁여 여유를 둠
            }
            p.ClaspAt = ClaspAt;
            p.ClaspW = tt => interlace ? Seg(tt, 1.85f, 2.35f) * (1 - Seg(tt, hold1, hold1 + 0.45f)) : 0;
            // 손바닥 바깥 깍지(관리자 10-19): 배 앞에서 깍지 낀 뒤 올리기 첫머리(2.6~3.1초)에 손바닥을 앞으로 뒤집고, 머리 위에선 하늘을 본다.
            //   팔꿈치를 옆으로 벌리고 아래팔이 서로를 향하게 해 손목은 덜 꺾고 아래팔 비틀기로 손바닥을 돌린다(9-9절)
            // 깍지 세부(9-10절): 2.0~2.45 손이 모이며 손바닥이 서로를 향함 → 2.05~2.6 손가락 펼쳐 벌림 → 2.4~2.76 검지부터 새끼까지 차례로 접어 깍지
            //   → 2.6~2.9 살짝 꽉 쥠 → 2.75~3.35 아래팔을 비틀어 손바닥 바깥(사인 가감속). 손목은 muscle로 보간(월드 방향 Slerp는 비틀기 해가 엇갈려 튀었다)
            Func<float, float> palmsOut = tt => interlace ? EaseS((tt - 2.75f) / 0.6f) * (1 - Seg(tt, hold1 - 0.1f, hold1 + 0.45f)) : 0;   // 풀 때도 천천히(0.4초는 457°/s)
            p.ClaspHalfW = tt => RA * (Mathf.Lerp(Mathf.Lerp(0.13f, 0.028f, Seg(tt, 1.95f, c1)), 0.065f, palmsOut(tt)) - 0.004f * Win(tt, 2.6f, 2.9f, 0.1f, 0.15f));   // 손바닥 바깥이면 손가락이 사이에 끼어 손목이 벌어진다
            p.Interlace = tt => interlace ? Seg(tt, 2.0f, 2.45f) * (1 - Seg(tt, hold1 - 0.05f, hold1 + 0.55f)) : 0;   // 손목 muscle 무게
            p.FingerOpen = tt => interlace ? Win(tt, 2.05f, 2.6f, 0.3f, 0.15f) + Win(tt, hold1 - 0.1f, hold1 + 0.5f, 0.15f, 0.3f) : 0;
            p.FingerCurl = (tt, i) => interlace ? Seg(tt, 2.4f + 0.035f * i, 2.62f + 0.035f * i) * (1 - Seg(tt, hold1 - 0.05f + 0.03f * i, hold1 + 0.2f + 0.03f * i)) : 0;
            p.PalmsUp = palmsOut;
            Vector3 ArmOff(float s, float tt)
            {
                float lag = s > 0 ? 0.05f : 0;      // 오른팔이 조금 늦게
                float ts = tt - lag;
                if (ts <= up1)
                {
                    if (ts < r0) return Low(s);
                    float E = EaseS((ts - r0) / (up1 - r0));
                    float k = Mathf.Lerp(0.99f, 0.995f, E) - 0.045f * Mathf.Sin(Mathf.PI * E);   // 중간에만 살짝 굽힘
                    return ArmDir(s, Mathf.Lerp(8f, 172f, E), 10f + 4f * Mathf.Sin(Mathf.PI * E) + (TopPhi - 10f) * E * E) * (k * armLen);
                }
                if (ts <= hold1) return Top(s) * ((0.995f + 0.004f * Seg(ts, up1, up1 + 0.4f)) * armLen);
                float v = (ts - hold1) / (down1 - hold1), Ev = EaseS(v);
                var top0 = Top(s); float psi0 = Mathf.Max(0f, Mathf.Atan2(s * top0.x, top0.y) * Mathf.Rad2Deg);   // 올린 팔의 정면 평면 각에서 시작(V자 만세가 머리 위로 모였다 내려가지 않게)
                float psi = Mathf.Lerp(psi0, 172f, Ev) * Mathf.Deg2Rad;     // 옆으로 큰 원(정면 평면)
                var side = new Vector3(s * Mathf.Sin(psi), Mathf.Cos(psi), 0.12f * Mathf.Sin(psi)).normalized;
                var dir = Vector3.Slerp(Top(s), side, Seg(v, 0f, 0.25f));
                float kd = Mathf.Lerp(0.999f, 0.99f, Seg(v, 0f, 0.2f)) - 0.02f * Mathf.Sin(Mathf.PI * Ev);
                return dir * (kd * armLen);
            }
            p.ArmOffL = tt => ArmOff(-1, tt); p.ArmOffR = tt => ArmOff(1, tt);
            p.ArmW = tt => Seg(tt, 1.95f, 2.35f);
            p.HandHeadW = tt => 0;
            p.Palm = tt => interlace || one ? 0 : Win(tt, up1 - 0.25f, hold1 + 0.05f, 0.35f, 0.3f);
            p.Reach = tt => Win(tt, r0 + 0.6f, hold1 + 0.2f, 0.6f, 0.4f);   // 어깨 으쓱은 팔이 어깨 높이를 넘을 때부터
            p.Arch = tt => Win(tt, up1 - 0.15f, hold1 + 0.25f, 0.45f, 0.45f);
            float sd0 = up1 + 0.2f, sd1 = hold1 - 0.1f;
            p.Side = tt => tt < sd0 || tt > sd1 ? 0 : Mathf.Sin(2 * Mathf.PI * (tt - sd0) / (sd1 - sd0)) * Win(tt, sd0, sd1, 0.15f, 0.15f);
            p.Exhale = tt => Win(tt, hold1 + 0.2f, end + 0.4f, 0.35f, 0.5f);
            var hipsStretch = new Path();
            hipsStretch.Key(sA, standAt).Key(up1, standAt + V(0, 0.006f, 0)).Key(up1 + 0.4f, standAt + V(0, 0.024f, 0.012f))
                       .Key(hold1, standAt + V(0, 0.024f, 0.012f)).Key(hold1 + 0.4f, standAt).Key(end, standAt + V(0, -0.008f, 0));   // 걷기 기준 높이로 이어지게

            if (mode == Stretch.Arc) { p.ArchTrunk = new Vector3(0.05f, 0.05f, 0f); p.ArchNod = new Vector2(0f, 0.08f); }   // 기존 음수 값은 실제로는 숙임(Pyrite 9-11절) — 가슴 살짝 젖히고 고개 살짝 위
            // ---------------- 한쪽 기지개(관리자 10-19, 참고 그림) — 오른팔을 주먹 쥐고 머리 위로 쭉 뻗고, 왼팔은 팔꿈치를 90°쯤 꺾어
            //   머리 옆 → 뒤로 넘겨 오른 팔꿈치 밑을 잡고 왼쪽으로 당기며 옆구리 늘리기 + 까치발 → 놓고 두 팔을 옆으로 내리며 "후—"(9-11절)
            //   Lapwing은 머리가 커서(정수리 1.47m · 어깨 관절 1.19m · 위팔 0.208m) 팔꿈치 근처를 잡으려면 왼 아래팔이 머리 뒤로 지나가야 한다
            //   → 올린 팔을 수직보다 조금 뒤로(θ 188°), 잡는 팔꿈치는 위·바깥·뒤로
            if (one)
            {
                float r0R = 2.25f, upR = 3.45f, r0L = 2.35f, dR0 = hold1 + 0.15f;
                Vector3 TopR() => ArmDir(1, OneTopTh, OneTopPh);
                p.ArmOffR = tt =>
                {
                    if (tt < r0R) return Low(1);
                    if (tt <= upR)
                    {
                        float E = EaseS((tt - r0R) / (upR - r0R));
                        float k = Mathf.Lerp(0.99f, 0.995f, E) - 0.05f * Mathf.Sin(Mathf.PI * E);   // 올리는 중간에만 살짝 굽힘
                        return ArmDir(1, Mathf.Lerp(8f, OneTopTh, E), Mathf.Lerp(10f, OneTopPh, E) + 4f * Mathf.Sin(Mathf.PI * E)) * (k * armLen);
                    }
                    if (tt <= dR0) return TopR() * ((0.995f + 0.004f * Seg(tt, upR, upR + 0.5f)) * armLen);
                    float v = (tt - dR0) / (down1 - dR0), Ev = EaseS(v);
                    float psi = Mathf.Lerp(0, 172f, Ev) * Mathf.Deg2Rad;     // 옆으로 큰 원(정면 평면)
                    var sideDir = new Vector3(Mathf.Sin(psi), Mathf.Cos(psi), 0.12f * Mathf.Sin(psi)).normalized;
                    var dir = Vector3.Slerp(TopR(), sideDir, Seg(v, 0f, 0.25f));
                    return dir * ((Mathf.Lerp(0.999f, 0.99f, Seg(v, 0f, 0.2f)) - 0.02f * Mathf.Sin(Mathf.PI * Ev)) * armLen);
                };
                // 왼팔: 팔꿈치를 앞세워 바깥으로 들어 올림 → 왼 어깨 위(머리 옆) → 머리 뒤(어깨 기준, 월드 축) — 그다음은 잡는 자리(GripW)가 이어받는다
                //   가슴 앞 → 관자놀이 경로는 손이 얼굴 앞을 지나갔다(3.0초)
                var swingL = ArmDir(-1, 25f, 55f) * (0.85f * armLen);   // 옆으로 크게 돌려 올린다 — 허벅지 → 옆·위를 곧장 이으면 손이 어깨에 붙어 팔꿈치가 69° → 53°로 접혔다 펴졌다(2.7초 569°/s)
                var outL = VA(-0.25f, 0.08f, 0.06f);   // 어깨 높이 옆, 팔꿈치 95°쯤(접는 걸 한꺼번에 하면 645°/s로 휙 올라갔다)
                var overL = HeadRel(new Vector3(-0.09f, 0.22f, -0.03f));
                var behindL = HeadRel(new Vector3(0.07f, 0.21f, -0.16f));
                var backL = HeadRel(new Vector3(-0.04f, 0.23f, -0.15f));  // 머리 왼쪽 뒤 — 옆 → 뒤를 바로 이으면 스플라인이 머리카락을 스쳤다
                var liftL = HeadRel(new Vector3(0.05f, 0.25f, -0.19f));   // 놓을 때 머리에서 떼며 뒤·위로
                var lUp = new Spline().Key(r0L, Low(-1)).Key(2.6f, swingL).Key(2.85f, outL).Key(3.3f, overL).Key(3.55f, backL).Key(3.75f, behindL);
                //   놓을 땐 머리 왼쪽 뒤 → 바로 옆으로 펴며 내린다(머리 위를 거치면 정수리를 쓰다듬는 것처럼 보였다)
                //   옆 → 아래 구간을 0.22초에 0.42m 지나가게 했더니 손이 1.9m/s로 휙 내려갔다(6.40초 1,354°/s) — 구간마다 0.25~0.3초, 0.2m 안쪽으로
                var sideL = VA(-0.2f, 0.06f, -0.06f);
                var lDown = new Spline().Key(hold1, behindL).Key(hold1 + 0.3f, liftL).Key(hold1 + 0.55f, backL).Key(hold1 + 0.85f, sideL)
                                        .Key(hold1 + 1.12f, ArmDir(-1, 28f, 35f) * (0.95f * armLen)).Key(down1, Low(-1));
                p.ArmOffL = tt => tt < hold1 ? lUp.At(tt) : lDown.At(tt);
                p.GripW = tt => Seg(tt, 3.6f, 4.0f) * (1 - Seg(tt, hold1 + 0.05f, hold1 + 0.4f));
                p.GripAlong = 0.88f; p.GripOff = new Vector2(0.055f, 0.04f) * RA; p.GripWrist = Vector3.zero;
                p.GripOpen = tt => Win(tt, 3.4f, 3.85f, 0.2f, 0.1f) + Win(tt, hold1 - 0.1f, hold1 + 0.45f, 0.15f, 0.25f);
                p.GripCurl = tt => Seg(tt, 3.8f, 4.05f) * (1 - Seg(tt, hold1 - 0.1f, hold1 + 0.1f));
                p.Fist = tt => Seg(tt, 2.55f, 3.1f) * (1 - Seg(tt, hold1 + 0.25f, hold1 + 0.7f));
                // 왼 팔꿈치 방향 = 손 경로와 같은 시각에 둔 "팔꿈치 자리"(어깨 기준) — 월드 방향 pole은 팔이 그쪽을 지나갈 때(어깨→손과 19°)
                //   팔꿈치 방향이 정해지지 않아 위팔 비틀기가 세 프레임에 1.2 풀렸다(5.87초 1,348°/s, 2.77초 671°/s). 굽은 팔의 팔꿈치 자리는 어깨-손 선에서 늘 떨어져 있다
                var eLow = VA(-0.12f, -0.15f, -0.13f);     // 늘어뜨린 팔 — 바깥·뒤(기존 pole과 같은 쪽)
                var eOut = VA(-0.19f, -0.07f, -0.04f);     // 손이 옆·위 → 팔꿈치는 옆·아래
                var eOver = HeadRel(new Vector3(-0.19f, 0.06f, -0.03f));     // 손이 머리 옆 → 팔꿈치는 바깥
                var eBack = HeadRel(new Vector3(-0.13f, 0.15f, -0.06f));     // 손이 머리 왼쪽 뒤 → 팔꿈치는 바깥·위
                var eGrip = HeadRel(new Vector3(-0.02f, 0.13f, -0.16f));     // 잡기(굽기 실측 4.40초 팔꿈치 자리) — 위·뒤
                var eLift = HeadRel(new Vector3(-0.05f, 0.12f, -0.17f));
                var eSide = VA(-0.15f, -0.12f, -0.07f);    // 손이 옆 → 팔꿈치는 아래·바깥
                var eLowSide = VA(-0.11f, -0.16f, -0.06f);
                var poleOld = new Path().Key(1.5f, new Vector3(-0.6f, -0.3f, -1f)).Key(2.35f, new Vector3(-0.69f, -0.25f, -0.75f));
                var eUp = new Spline().Key(r0L, eLow).Key(2.6f, VA(-0.15f, -0.1f, -0.1f)).Key(2.85f, eOut).Key(3.3f, eOver).Key(3.55f, eBack).Key(3.75f, eGrip);
                var eDown = new Spline().Key(hold1, eGrip).Key(hold1 + 0.3f, eLift).Key(hold1 + 0.55f, eBack).Key(hold1 + 0.85f, eSide).Key(hold1 + 1.12f, eLowSide).Key(down1, eLow);
                p.PoleL = tt => tt < r0L ? poleOld.At(tt) : tt < hold1 ? eUp.At(tt) : eDown.At(tt);
                var poleRp = new Path().Key(1.5f, new Vector3(0.6f, -0.3f, -1f)).Key(2.6f, new Vector3(0.7f, -0.25f, -0.7f)).Key(3.3f, new Vector3(1f, 0f, -0.4f))
                                       .Key(dR0, new Vector3(1f, 0f, -0.4f)).Key(down1, new Vector3(0.7f, -0.25f, -0.7f));
                p.PoleR = tt => poleRp.At(tt);
                p.RollArms = true;
                p.Reach = tt => Win(tt, r0R + 0.5f, hold1 + 0.3f, 0.6f, 0.45f);
                p.ReachKL = 0.25f; p.ReachKR = 0.45f;
                p.Arch = tt => Win(tt, up1 - 0.3f, hold1 + 0.25f, 0.45f, 0.45f);
                p.ArchTrunk = new Vector3(0.05f, 0.05f, 0f);            // 가슴만 살짝 — 더 젖히면 머리가 어깨보다 뒤로 빠져 머리 뒤 팔과 겹친다(0.12면 4cm)
                p.ArchNod = Vector2.zero;
                p.Tip = tt => Win(tt, up1 - 0.25f, hold1 + 0.35f, 0.45f, 0.45f);
                p.TipDeg = 22f;
                p.Side = tt => 0;
                p.SideL = tt => Win(tt, up1 - 0.3f, hold1 + 0.3f, 0.6f, 0.55f) * (0.85f + 0.15f * Seg(tt, up1, hold1 - 0.2f));
                float tipUp = BallLen * Mathf.Sin(p.TipDeg * Mathf.Deg2Rad);
                hipsStretch = new Path();
                hipsStretch.Key(sA, standAt).Key(up1 - 0.25f, standAt + V(0, 0.004f, 0)).Key(up1 + 0.2f, standAt + new Vector3(0, tipUp + 0.004f * RH, 0.012f * RH))
                           .Key(hold1, standAt + new Vector3(0, tipUp + 0.004f * RH, 0.012f * RH)).Key(hold1 + 0.45f, standAt).Key(end, standAt + V(0, -0.008f, 0));
            }

            // ---------------- 약 5초 걷기 — 속도 곡선에서 디딜 자리·골반 출렁임·쏠림·회전·기울기를 만든다
            float tw = end + 0.25f;
            p.WalkStart = tw;
            var g = BuildGait(p, tw, standAt, standHipY - 0.008f * RH);
            p.WalkEnd = g.TEnd;
            p.Length = g.TEnd + 0.7f;

            p.HipsAt = tt =>
            {
                if (tt < sA) return hips.At(tt);
                if (tt < tw) return hipsStretch.At(tt) + (p.SideL != null ? new Vector3(-SideSign * 0.012f * RH * p.SideL(tt), 0, 0) : Vector3.zero);   // 상체를 기울인 반대로 골반을 살짝(균형)
                return g.Hips(tt);
            };
            p.PelvisRot = tt => tt < sA ? new Vector3(trunk(tt), 0, 0) : tt < tw ? Vector3.zero : g.Rot(tt);
            p.WalkW = tt => Win(tt, tw, g.TEnd + 0.2f, 0.4f, 0.6f);
            p.ArmSig = tt => g.Yaw.F(tt - ArmLag) / -PelvisYaw;    // 오른발 닿을 때 골반 회전이 −라 부호를 뒤집는다
            p.Look = tt => Win(tt, tw + 1.5f, tw + 3.9f, 0.8f, 0.7f);
            p.HandIk = tt => tt < down1 + 0.05f ? 1 : 1 - Seg(tt, down1 + 0.05f, down1 + 0.4f);
            p.HandLAt = handAtL; p.HandRAt = handAtR;
            p.Face = MakeFace(sA, up1, hold1, down1, end, tw, g.TEnd);
            Log.AppendLine($"afkmotion: 시간표: 일어서기 0~{settle:F2}, 기지개 {sA:F2}~{end:F2}, 걷기 {tw:F2}~{g.TEnd:F2}({g.TEnd - tw:F2}초, {Steps}걸음), 길이 {p.Length:F2}");
            return p;
        }

        // ================================================================ 걸음
        sealed class Gait
        {
            public float T0, TEnd, Z0, X0, Y0, Corr, CorrFrom;
            public float[] Tab; public const float Dt = 0.002f;
            public Path Sway = new Path(), BobP = new Path(), Yaw = new Path(), Roll = new Path();
            public float Z(float t)
            {
                if (t <= T0) return Z0;
                float x = (t - T0) / Dt; int i = (int)x;
                if (i >= Tab.Length - 1) return Tab[Tab.Length - 1];
                return Mathf.Lerp(Tab[i], Tab[i + 1], x - i);
            }
            public float V(float t) => (Z(t + 0.01f) - Z(t - 0.01f)) / 0.02f;
            public Vector3 Hips(float t)
            {
                float z = Z(t) + Corr * Seg(t, CorrFrom, TEnd);
                return new Vector3(X0 + Sway.F(t), Y0 + BobP.F(t), z);
            }
            public Vector3 Rot(float t)
            {
                float w = Win(t, T0, TEnd + 0.2f, 0.4f, 0.6f);
                return new Vector3(PelvisTilt * w, Yaw.F(t), Roll.F(t));
            }
        }

        static Gait BuildGait(Plan p, float tw, Vector3 hip0, float hipY)
        {
            var g = new Gait { T0 = tw, Z0 = hip0.z, X0 = hip0.x, Y0 = hipY };
            float lead = 0.3f;                       // 첫 발 떼기 전 디딘 발로 무게 옮기기
            float lift0 = tw + lead;
            var hs = new float[Steps];
            for (int k = 0; k < Steps; k++) hs[k] = lift0 + SwingT + k * StepT;
            g.TEnd = hs[Steps - 1] + 0.25f;
            float vs = tw + lead * 0.5f, ta = 1.0f, td = 0.9f;
            int n = Mathf.CeilToInt((g.TEnd + 1f - tw) / Gait.Dt) + 2;
            g.Tab = new float[n];
            float z = hip0.z;
            for (int i = 0; i < n; i++)
            {
                float t = tw + i * Gait.Dt;
                float v = VMax * (t < vs ? 0 : S((t - vs) / ta)) * (1 - S((t - (g.TEnd - td)) / td));
                z += v * Gait.Dt;
                g.Tab[i] = z;
            }
            // 디딜 자리: 디딘 발은 디딘 시간의 절반쯤 골반 바로 밑(발목은 골반보다 AnkleBehind 뒤)을 지나간다
            g.BobP.Key(tw, 0); g.Sway.Key(tw, 0); g.Yaw.Key(tw, 0); g.Roll.Key(tw, 0);
            g.Sway.Key(lift0, -Sway * 0.6f);                         // 먼저 왼발 쪽으로 무게(오른발부터 뗀다)
            for (int k = 0; k < Steps; k++)
            {
                bool right = k % 2 == 0;
                var mover = right ? p.FootR : p.FootL;
                var other = right ? p.FootL : p.FootR;
                bool last = k == Steps - 1;
                float speed = g.V(hs[k]) / VMax;
                float zk = last ? other.Last.z : Mathf.Max(other.Last.z + 0.05f * RH, g.Z(hs[k]) - AnkleBehind + 0.5f * g.V(hs[k]) * StanceT);
                var dst = new Vector3(right ? FootX : -FootX, mover.Last.y, zk);
                mover.Move(hs[k] - SwingT, hs[k], dst, RH * (last ? 0.035f : 0.045f + 0.012f * speed), true);
                float side = right ? 1 : -1;
                float amp = Mathf.Clamp01(0.35f + speed);
                g.Yaw.Key(hs[k], -side * PelvisYaw * speed);
                g.Roll.Key(hs[k] - SwingT * 0.5f, -side * PelvisRoll * amp);
                g.Roll.Key(hs[k] + 0.1f, 0);
                if (!last)
                {
                    g.BobP.Key(hs[k] - SwingT * 0.5f, Bob * amp);   // 반대 발 한가운데 디딤 = 가장 높음
                    g.BobP.Key(hs[k] + 0.06f, -Bob * amp);          // 닿고 무릎으로 받는 순간 = 가장 낮음
                    g.Sway.Key(hs[k] + StanceT * 0.5f, side * Sway * amp);
                }
            }
            g.BobP.Key(g.TEnd, 0.008f * RH); g.Sway.Key(g.TEnd, 0); g.Yaw.Key(g.TEnd, 0); g.Roll.Key(g.TEnd, 0);
            g.CorrFrom = hs[Steps - 2];
            g.Corr = (p.FootL.Last.z + p.FootR.Last.z) * 0.5f + AnkleBehind - g.Z(g.TEnd);
            Log.AppendLine($"afkmotion: 걷기: 첫 걸음 {p.FootR.Ev[1].to.z - (PlateZ1 + 0.07f * RH):F2}m, 걸음 {VMax * StepT:F2}m, 끝 보정 {g.Corr * 100:F1}cm, 끝 발 z {p.FootL.Last.z:F2}/{p.FootR.Last.z:F2}");
            return g;
        }

        // ================================================================ 얼굴
        static Func<float, Dictionary<string, float>> MakeFace(float sA, float up1, float hold1, float down1, float end, float tw, float tEnd)
        {
            // Lapwing ARKit 묶음 대신 Descriptor에서 찾은 묶음(WcAfkFaceMap) — 시간 창은 Lapwing 그대로
            Dictionary<string, float> G(string k) => Faces.Groups.TryGetValue(k, out var d) ? d : new Dictionary<string, float>();
            var effort = G("effort"); var yawnEyes = G("yawnEyes"); var yawnMouth = G("yawnMouth"); var tears = G("tears");
            var puff = G("puff"); var content = G("content"); var walkFace = G("walk");
            var blinkSet = Faces.Blink.ToDictionary(k => k, k => 100f);
            float[] blinks = new[] { 0.35f, sA - 0.15f, end + 0.05f, tw + 1.0f, tw + 2.7f, tw + 4.4f }.Where(b => b < tEnd + 0.3f).ToArray();
            return t =>
            {
                var f = new Dictionary<string, float>();
                void Mix(Dictionary<string, float> src, float w) { if (w <= 0) return; foreach (var kv in src) f[kv.Key] = (f.TryGetValue(kv.Key, out var o) ? o : 0) + kv.Value * w; }
                Mix(effort, Win(t, 0.75f, 2.2f, 0.35f, 0.4f));
                Mix(yawnEyes, Win(t, up1 - 0.75f, hold1 + 0.15f, 0.5f, 0.35f));
                Mix(yawnMouth, Win(t, up1 - 0.55f, hold1, 0.6f, 0.4f));
                Mix(tears, Win(t, hold1 - 0.4f, end + 0.4f, 0.3f, 0.6f));
                Mix(puff, Win(t, hold1 - 0.05f, down1 + 0.1f, 0.25f, 0.35f));
                Mix(content, Win(t, down1 - 0.1f, tw + 0.8f, 0.4f, 0.8f));
                Mix(walkFace, Seg(t, tw + 0.2f, tw + 1.0f));
                float blink = 0;
                foreach (var b in blinks) { float u = (t - b) / 0.17f; if (u > 0 && u < 1) blink = Mathf.Max(blink, u < 0.35f ? u / 0.35f : 1 - (u - 0.35f) / 0.65f); }
                if (blink > 0) Mix(blinkSet, blink);
                // 감기 키와 다른 눈 키(eye_nagomi 등)가 겹치면 눈이 무너진다(Kipfel eye_nagomi 60 + eye_close 100 — 작은 꼬불선, 관리자 10-19)
                //   → 감은 만큼 다른 눈 키(양수)를 줄인다
                float closed = Faces.Blink.Count == 0 ? 0 : Faces.Blink.Max(k => f.TryGetValue(k, out var bv) ? Mathf.Clamp01(bv / 100f) : 0);
                if (closed > 0)
                    foreach (var key in f.Keys.ToList())
                        if (!Faces.Blink.Contains(key) && Faces.EyeKeys.Contains(key) && f[key] > 0) f[key] *= 1 - closed;
                foreach (var key in f.Keys.ToList()) f[key] = Mathf.Clamp(f[key], -100, 100);   // 음수 = 씬 기본값에서 빼기(최종 0~100은 굽기에서)
                return f;
            };
        }

        // ================================================================ 한 프레임
        static void Apply(Rig rig, Pose p)
        {
            var hp = new HumanPose { bodyPosition = p.T, bodyRotation = p.Q, muscles = (float[])p.M.Clone() };
            rig.Hph.SetHumanPose(ref hp);
        }

        static HumanPose Frame(Rig rig, Plan plan, Pose sit, Pose stand, Vector3 idleToeL, Vector3 idleToeR, float t, out Vector3 footL, out Vector3 footR)
        {
            footL = plan.FootL.At(t, out float pitchL);
            footR = plan.FootR.At(t, out float pitchR);
            var hipsPos = plan.HipsAt(t);
            var rot = plan.PelvisRot(t);
            float ww = plan.WalkW(t);

            // ① 바탕 muscle
            float st = plan.Stand(t);
            var m = Lerp(sit.M, stand.M, st);
            float lean = plan.Lean(t);   // 골반째 숙인 위에 등도 둥글게, 시선은 앞쪽 아래로 남긴다
            Add(m, "Spine Front-Back", 0.3f * lean); Add(m, "Chest Front-Back", 0.2f * lean);
            Add(m, "Neck Nod Down-Up", -0.35f * lean); Add(m, "Head Nod Down-Up", -0.25f * lean);
            float arch = plan.Arch(t);
            float sl = plan.SideL != null ? plan.SideL(t) : 0;   // 잡은 쪽(왼쪽)으로 옆구리 늘리기 — 머리는 덜 따라간다
            void TrunkBend(float[] arr, float k)                  // 등 젖힘 + 옆구리(골반 보정 때 빼고 다시 세우려고 한데 모음)
            {
                Add(arr, "Spine Front-Back", plan.ArchTrunk.x * arch * k); Add(arr, "Chest Front-Back", plan.ArchTrunk.y * arch * k); Add(arr, "UpperChest Front-Back", plan.ArchTrunk.z * arch * k);
                if (sl > 0) { Add(arr, "Spine Left-Right", SideSign * 0.2f * sl * k); Add(arr, "Chest Left-Right", SideSign * 0.16f * sl * k); Add(arr, "UpperChest Left-Right", SideSign * 0.12f * sl * k); }
            }
            TrunkBend(m, 1);
            Add(m, "Neck Nod Down-Up", plan.ArchNod.x * arch); Add(m, "Head Nod Down-Up", plan.ArchNod.y * arch);
            float reach = plan.Reach(t);
            Add(m, "Left Shoulder Down-Up", plan.ReachKL * reach); Add(m, "Right Shoulder Down-Up", plan.ReachKR * reach);
            if (sl > 0) Add(m, "Head Tilt Left-Right", SideSign * 0.25f * sl);   // 올린 팔에서 고개를 비켜 잡은 쪽으로
            float palm = plan.Palm(t);   // 손바닥 위로는 팔이 위에 다 왔을 때만(처음부터 45° 젖혀 "꺾인 채" 보였다)
            Add(m, "Left Hand Down-Up", -0.25f * palm); Add(m, "Right Hand Down-Up", -0.25f * palm);
            float side = plan.Side(t);
            Add(m, "Spine Left-Right", 0.14f * side); Add(m, "Chest Left-Right", 0.12f * side); Add(m, "UpperChest Left-Right", 0.08f * side);
            float ex = plan.Exhale(t);
            Add(m, "Left Shoulder Down-Up", -0.15f * ex); Add(m, "Right Shoulder Down-Up", -0.15f * ex);
            Add(m, "Chest Front-Back", 0.06f * ex); Add(m, "Head Nod Down-Up", 0.08f * ex);
            // 걷는 상체: 골반과 반대로 가슴, 머리는 정면, 다리와 반대로 팔(살짝 늦게), 둘러보기
            float yaw = rot.y;
            Add(m, "Spine Twist Left-Right", -0.4f * yaw / 40f); Add(m, "Chest Twist Left-Right", -0.9f * yaw / 40f); Add(m, "UpperChest Twist Left-Right", -0.5f * yaw / 40f);
            Add(m, "Neck Turn Left-Right", 0.5f * yaw / 40f);
            Add(m, "Spine Left-Right", -0.6f * rot.z / 40f);
            Add(m, "Spine Front-Back", 0.04f * ww); Add(m, "Head Nod Down-Up", -0.04f * ww);
            float look = plan.Look(t);
            Add(m, "Neck Turn Left-Right", -0.22f * look); Add(m, "Head Turn Left-Right", -0.3f * look); Add(m, "Head Tilt Left-Right", -0.06f * look);
            float sig = plan.ArmSig(t) * ww;
            float armR = ArmSwing * sig, armL = -ArmSwing * sig;          // Arm Front-Back은 − 쪽이 앞(Play 실측) — 오른다리 앞(sig +)이면 오른팔 뒤(+)
            Add(m, "Right Arm Front-Back", armR - 0.04f * ww); Add(m, "Left Arm Front-Back", armL - 0.04f * ww);
            Add(m, "Right Arm Down-Up", (0.05f + 0.07f * Mathf.Max(0, -armR / ArmSwing)) * ww); Add(m, "Left Arm Down-Up", (0.05f + 0.07f * Mathf.Max(0, -armL / ArmSwing)) * ww);   // 앞으로 갈 때 살짝 벌려 손이 배 앞으로 모이지 않게
            Add(m, "Right Forearm Stretch", (-0.1f - 0.25f * Mathf.Max(0, -armR / ArmSwing)) * ww);
            Add(m, "Left Forearm Stretch", (-0.1f - 0.25f * Mathf.Max(0, -armL / ArmSwing)) * ww);
            float il = plan.Interlace(t);
            float po = plan.PalmsUp(t);
            float fo = plan.FingerOpen(t);
            string[] fingers = { "Index", "Middle", "Ring", "Little" };
            foreach (var sd in new[] { "Left", "Right" })
            {
                for (int fi = 0; fi < 4; fi++)
                {
                    string fn = fingers[fi];
                    if (fo > 0)   // 펼쳐 벌림 — 서로 사이로 들어갈 준비
                    {
                        SetM(m, $"{sd} {fn} 1 Stretched", 0.35f, fo); SetM(m, $"{sd} {fn} 2 Stretched", 0.3f, fo);
                        SetM(m, $"{sd} {fn} 3 Stretched", 0.25f, fo); SetM(m, $"{sd} {fn} Spread", 0.9f, fo);
                    }
                    float fc = plan.FingerCurl(t, fi);
                    if (fc > 0)   // 깍지: 손바닥 마주면 상대 손등을 감싸게, 바깥이면 덜 굽혀 사이에 낀 채
                    {
                        //   바깥 깍지도 단단히(−0.15/−0.35/−0.25는 뒤집는 중 손가락이 펴져 깍지가 풀린 듯 보였다)
                        SetM(m, $"{sd} {fn} 1 Stretched", Mathf.Lerp(-0.35f, -0.4f, po), fc); SetM(m, $"{sd} {fn} 2 Stretched", Mathf.Lerp(-0.7f, -0.5f, po), fc);
                        SetM(m, $"{sd} {fn} 3 Stretched", Mathf.Lerp(-0.6f, -0.4f, po), fc); SetM(m, $"{sd} {fn} Spread", Mathf.Lerp(0.5f, 0.6f, po), fc);
                    }
                }
                float tc = plan.FingerCurl(t, 4);
                if (tc > 0) { SetM(m, $"{sd} Thumb 1 Stretched", -0.2f, tc); SetM(m, $"{sd} Thumb 2 Stretched", -0.45f, tc); SetM(m, $"{sd} Thumb 3 Stretched", -0.35f, tc); }
                // 한쪽 기지개: 오른손 주먹, 왼손은 펴서 다가가 오른 팔꿈치 밑을 감아쥔다
                float fist = sd == "Right" && plan.Fist != null ? plan.Fist(t) : 0;
                float gOpen = sd == "Left" && plan.GripOpen != null ? plan.GripOpen(t) : 0;
                float gCurl = sd == "Left" && plan.GripCurl != null ? plan.GripCurl(t) : 0;
                for (int fi = 0; fi < 4; fi++)
                {
                    string fn = fingers[fi];
                    if (fist > 0)
                    {
                        float w1 = Seg(fist, 0.04f * fi, 0.8f + 0.05f * fi);   // 새끼 쪽이 조금 늦게
                        //   주먹 후보 렌더에서 고름 — −0.75/−0.95/−0.85는 손끝이 손바닥에 안 닿아 갈고리처럼 보였다(muscle은 ±1을 넘어도 계속 굽는다)
                        SetM(m, $"{sd} {fn} 1 Stretched", -1.1f, w1); SetM(m, $"{sd} {fn} 2 Stretched", -1.1f, w1);
                        SetM(m, $"{sd} {fn} 3 Stretched", -1.0f, w1); SetM(m, $"{sd} {fn} Spread", -0.2f, w1);
                    }
                    if (gOpen > 0)
                    {
                        SetM(m, $"{sd} {fn} 1 Stretched", 0.25f, gOpen); SetM(m, $"{sd} {fn} 2 Stretched", 0.2f, gOpen);
                        SetM(m, $"{sd} {fn} 3 Stretched", 0.15f, gOpen); SetM(m, $"{sd} {fn} Spread", 0.35f, gOpen);
                    }
                    if (gCurl > 0)
                    {
                        float w1 = Seg(gCurl, 0.05f * fi, 0.8f + 0.05f * fi);
                        SetM(m, $"{sd} {fn} 1 Stretched", -0.45f, w1); SetM(m, $"{sd} {fn} 2 Stretched", -0.75f, w1);
                        SetM(m, $"{sd} {fn} 3 Stretched", -0.6f, w1); SetM(m, $"{sd} {fn} Spread", 0f, w1);
                    }
                }
                if (fist > 0) { float wt = Seg(fist, 0.3f, 1f); SetM(m, $"{sd} Thumb 1 Stretched", -0.6f, wt); SetM(m, $"{sd} Thumb 2 Stretched", -0.8f, wt); SetM(m, $"{sd} Thumb 3 Stretched", -0.8f, wt); SetM(m, $"{sd} Thumb Spread", -0.7f, wt); }   // 엄지는 손가락이 다 접힌 뒤 덮는다
                if (gOpen > 0) { SetM(m, $"{sd} Thumb 1 Stretched", 0.2f, gOpen); SetM(m, $"{sd} Thumb Spread", 0.3f, gOpen); }
                if (gCurl > 0) { SetM(m, $"{sd} Thumb 1 Stretched", 0.1f, gCurl); SetM(m, $"{sd} Thumb 2 Stretched", -0.35f, gCurl); SetM(m, $"{sd} Thumb 3 Stretched", -0.45f, gCurl); }
                float gw0 = sd == "Left" && plan.GripW != null ? plan.GripW(t) : 0;
                if (gw0 > 0)
                {
                    SetM(m, $"{sd} Hand Down-Up", plan.GripWrist.x, gw0); SetM(m, $"{sd} Hand In-Out", plan.GripWrist.y, gw0);
                    SetM(m, $"{sd} Forearm Twist In-Out", plan.GripWrist.z, gw0);
                }
                if (il > 0)   // 손목: 손바닥 마주 → 바깥을 muscle로 보간. 한계(±1) 안쪽의 편한 값 — 끝 자세 실측(−1.07/1.05)은 손목이 끝까지 꺾여 보였다
                {
                    SetM(m, $"{sd} Forearm Twist In-Out", Mathf.Lerp(0.3f, -0.85f, po), il);
                    SetM(m, $"{sd} Hand Down-Up", Mathf.Lerp(0.15f, 0.7f, po), il);
                    SetM(m, $"{sd} Hand In-Out", Mathf.Lerp(0.45f, 0.3f, po), il);
                }
            }
            Vector3 bodyT = Vector3.Lerp(sit.T, stand.T, st);
            Quaternion bodyQ = Quaternion.Slerp(sit.Q, stand.Q, st);
            var hips = rig["Hips"];
            if (plan.SideL != null && (arch > 0 || sl > 0))
            {
                // 휴머노이드는 몸 방향(bodyRotation)을 지키려고 등을 굽힌 반대로 골반을 기울인다 → 옆구리를 늘리면 왼 골반이 올라가
                //   왼발이 4.6cm 떴다(9-11절). 등 굽힘을 뺀 자세의 골반 방향으로 되돌려 골반은 수평, 굽힘은 전부 등에서
                var m0 = (float[])m.Clone(); TrunkBend(m0, -1);
                Apply(rig, new Pose { M = m0, T = bodyT, Q = bodyQ });
                var hq = hips.rotation;
                Apply(rig, new Pose { M = m, T = bodyT, Q = bodyQ });
                hips.rotation = hq;
            }
            else Apply(rig, new Pose { M = m, T = bodyT, Q = bodyQ });

            // ② 골반 위치·회전(숙임 x, 걸음 회전 y, 옆 기울기 z)
            hips.position = hipsPos;
            hips.rotation = Quaternion.AngleAxis(rot.y, Vector3.up) * Quaternion.AngleAxis(rot.z, Vector3.forward) * Quaternion.AngleAxis(rot.x, Vector3.right) * hips.rotation;

            // ③ 다리 IK + 발 굴림(+ 기지개 까치발) + 발끝 바깥(걸을 때)
            float flat = plan.FootFlat(t);
            float tip = plan.TipDeg * (plan.Tip != null ? plan.Tip(t) : arch);
            float tipLift = BallLen * Mathf.Sin(tip * Mathf.Deg2Rad);
            LegIk(rig, "Left", footL + new Vector3(0, tipLift, 0), idleToeL, flat, pitchL + tip, -ToeOut * ww);
            LegIk(rig, "Right", footR + new Vector3(0, tipLift, 0), idleToeR, flat, pitchR + tip, ToeOut * ww);

            // ④ 팔 — FK 결과와 IK 결과를 무게로 섞는다(기지개 땐 손 목표를 머리에 붙인다)
            var fk = new HumanPose(); rig.Hph.GetHumanPose(ref fk);
            float w = plan.HandIk(t);
            if (w <= 0) return fk;
            var hl = plan.HandLAt(t); var hr = plan.HandRAt(t);
            float aw = plan.ArmW(t);
            var roll = Quaternion.identity;   // 옆구리·젖힘만큼 올린 팔·잡는 팔(과 팔꿈치 방향)도 가슴과 같이 돈다(머리와 팔 사이가 등 굽힘에 따라 변하지 않게)
            if (plan.RollArms) roll = rig.ChestTop.rotation * Quaternion.Inverse(rig.ChestRot0);
            if (aw > 0)
            {
                hl = Vector3.Lerp(hl, rig["LeftUpperArm"].position + roll * plan.ArmOffL(t), aw);
                hr = Vector3.Lerp(hr, rig["RightUpperArm"].position + roll * plan.ArmOffR(t), aw);
            }
            float cw = plan.ClaspW(t);
            Vector3 mid = (rig["LeftUpperArm"].position + rig["RightUpperArm"].position) * 0.5f;   // 두 어깨 가운데
            if (cw > 0)
            {
                var c = mid + plan.ClaspAt(t); float hw2 = plan.ClaspHalfW(t);
                hl = Vector3.Lerp(hl, c + new Vector3(-hw2, 0, 0), cw); hr = Vector3.Lerp(hr, c + new Vector3(hw2, 0, 0), cw);
            }
            float hw = plan.HandHeadW(t);
            if (hw > 0)
            {
                var head = rig["Head"].position;
                hl = Vector3.Lerp(hl, head + plan.OverL, hw); hr = Vector3.Lerp(hr, head + plan.OverR, hw);
            }
            // 팔꿈치 방향: 팔걸이 짚기(뒤) → 기지개(바깥)를 1.5~2.6초에 걸쳐 서서히(한 프레임에 바꾸면 팔꿈치가 튄다 — 9-6절)
            //   기지개 쪽 기준은 바깥·뒤 — 바깥만 쓰면 팔을 옆으로 쭉 펴 내릴 때 팔 방향과 겹쳐 팔꿈치가 뒤집힌다(4.7초, 9-6절)
            var poleL = Vector3.Lerp(new Vector3(-0.6f, -0.3f, -1f), new Vector3(-0.7f, -0.25f, -0.7f), Seg(t, 1.5f, 2.6f));
            poleL = Vector3.Lerp(poleL, new Vector3(-1f, -0.15f, 0.1f), plan.PalmsUp(t));   // 손바닥 바깥 깍지 = 팔꿈치 옆으로
            var poleR = new Vector3(-poleL.x, poleL.y, poleL.z);
            if (plan.PoleL != null) { poleL = roll * plan.PoleL(t); poleR = roll * plan.PoleR(t); }
            // 오른팔 먼저 — 한쪽 기지개에서 왼손은 오른 팔꿈치 밑(움직이는 팔 위의 점)을 잡는다
            ArmIk(rig["RightUpperArm"], rig["RightLowerArm"], rig["RightHand"], hr, poleR, rig.ElbowAxisR);
            float gw = plan.GripW != null ? plan.GripW(t) : 0;
            if (gw > 0)
            {
                var ru = rig["RightUpperArm"].position; var rl = rig["RightLowerArm"].position; var ax = (rl - ru).normalized;
                var back = Vector3.ProjectOnPlane(Vector3.back, ax).normalized; var inward = Vector3.ProjectOnPlane(Vector3.left, ax).normalized;
                hl = Vector3.Lerp(hl, Vector3.Lerp(ru, rl, plan.GripAlong) + back * plan.GripOff.x + inward * plan.GripOff.y, gw);
            }
            ArmIk(rig["LeftUpperArm"], rig["LeftLowerArm"], rig["LeftHand"], hl, poleL, rig.ElbowAxisL);
            var ik = new HumanPose(); rig.Hph.GetHumanPose(ref ik);
            if (w >= 1) return ik;
            ik.muscles = Lerp(fk.muscles, ik.muscles, w);
            return ik;
        }

        static void LegIk(Rig rig, string side, Vector3 target, Vector3 idleToe, float flat, float pitch, float toeOut)
        {
            var up = rig[side + "UpperLeg"]; var lo = rig[side + "LowerLeg"]; var ft = rig[side + "Foot"];
            float sx = side == "Left" ? -1 : 1;
            var before = ft.rotation;
            TwoBone(up, lo, ft, target, Quaternion.AngleAxis(toeOut, Vector3.up) * new Vector3(0.12f * sx, 0, 1));
            ft.rotation = before;   // IK가 발 방향을 비틀지 않게 — 먼저 원래(바탕) 방향으로
            if (flat > 0)
            {
                // 발가락 뼈가 없는 아바타(Kipfel)는 서기 자세에서 잡은 발 앞 가상점(발 지역 좌표)으로
                bool hasToes = rig.B.TryGetValue(side + "Toes", out var toes);
                var cur = rig.ToePos(side) - ft.position;
                var want = Quaternion.AngleAxis(toeOut, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.right) * idleToe;   // pitch + = 발끝 아래(뒤꿈치 듦)
                var rot = Quaternion.FromToRotation(cur, want) * ft.rotation;
                ft.rotation = Quaternion.Slerp(ft.rotation, rot, flat);
                // 발가락: 뒤꿈치를 들면 바닥에 붙은 채 위로 꺾인다(굴림 각의 80%를 되돌림)
                if (hasToes && pitch > 0) toes.rotation = Quaternion.AngleAxis(-0.8f * pitch * flat, Quaternion.AngleAxis(toeOut, Vector3.up) * Vector3.right) * toes.rotation;
            }
        }

        // 팔 IK — 팔꿈치는 경첩으로만 굽힌다(9-10절). 최소 회전 TwoBone은 아래팔 비틀림을 조금씩 누적시켜,
        //   팔을 머리 위까지 올리는 동안 Forearm Twist가 ±180°를 넘어 반대편 해로 튀었다(4.10초, −1.92 → +1.98).
        //   ① 팔꿈치 각만 경첩 축(위팔 지역 고정)으로 ② 위팔째 돌려 손목을 목표로 ③ 어깨→목표 축으로 굴려 팔꿈치를 pole 쪽으로
        //   → 아래팔·손목의 비틀기는 muscle로 정한 값 그대로 남는다
        static void ArmIk(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, Vector3 axisLocal)
        {
            Vector3 pa = a.position;
            float la = (b.position - pa).magnitude, lb = (c.position - b.position).magnitude;
            // 소프트 IK — 팔 길이의 96.5%를 넘는 거리는 지수 곡선으로 눌러 팔꿈치가 일직선이 되지 않게(일직선이면 비틀림 배분이 정해지지 않아
            //   1.27초에 위팔·아래팔 비틀기가 한 프레임에 바뀌었고, 일직선 근처는 1.4cm에 30°가 변해 2.00초에 튀었다)
            float L = la + lb, d0 = 0.965f * L, dMax = 0.995f * L, raw = (target - pa).magnitude;
            float dist = raw <= d0 ? Mathf.Max(raw, Mathf.Abs(la - lb) + 1e-4f) : d0 + (dMax - d0) * (1 - Mathf.Exp(-(raw - d0) / (dMax - d0)));
            var n = a.TransformDirection(axisLocal).normalized;
            float cur = Vector3.SignedAngle(a.position - b.position, c.position - b.position, n);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - dist * dist) / (2 * la * lb), -1, 1)) * Mathf.Rad2Deg;
            b.rotation = Quaternion.AngleAxis(want - cur, n) * b.rotation;
            a.rotation = Quaternion.FromToRotation(c.position - pa, target - pa) * a.rotation;
            var dn = (target - pa).normalized;
            // 팔꿈치가 나가는 방향 = cross(경첩 축, 어깨→손목) — 팔이 쭉 펴져 있어도 정해진다(팔꿈치 위치로 재면 펴진 팔에서 정해지지 않다가
            //   굽는 순간 한 프레임에 굴러 1,230°/s로 튀었다, 2.00초)
            var e = Vector3.Cross(a.TransformDirection(axisLocal), dn); var pp = Vector3.ProjectOnPlane(pole, dn);
            if (e.sqrMagnitude > 1e-8f && pp.sqrMagnitude > 1e-8f) a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(e, pp, dn), dn) * a.rotation;
        }

        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            Vector3 pa = a.position;
            float la = (b.position - pa).magnitude, lb = (c.position - b.position).magnitude;
            Vector3 d = target - pa;
            float dist = Mathf.Clamp(d.magnitude, Mathf.Abs(la - lb) + 1e-4f, (la + lb) * 0.9995f);
            Vector3 dn = d.normalized;
            float cosA = Mathf.Clamp((la * la + dist * dist - lb * lb) / (2 * la * dist), -1, 1);
            float sinA = Mathf.Sqrt(1 - cosA * cosA);
            Vector3 perp = Vector3.ProjectOnPlane(pole, dn);
            if (perp.sqrMagnitude < 1e-8f) perp = Vector3.ProjectOnPlane(Vector3.forward, dn);
            perp.Normalize();
            Vector3 knee = pa + (dn * cosA + perp * sinA) * la;
            a.rotation = Quaternion.FromToRotation(b.position - pa, knee - pa) * a.rotation;
            Vector3 tgt = pa + dn * dist;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, tgt - b.position) * b.rotation;
        }

        // 남긴 키 사이를 직선으로 이었을 때 빠진 키와의 차가 eps 이하인 구간은 양 끝만 남긴다
        static AnimationCurve Reduce(AnimationCurve c, float eps)
        {
            var k = c.keys;
            var keep = new List<Keyframe> { k[0] };
            int a = 0;
            for (int b = 2; b < k.Length; b++)
            {
                bool ok = true;
                for (int j = a + 1; j < b && ok; j++)
                {
                    float u = (k[j].time - k[a].time) / (k[b].time - k[a].time);
                    ok = Mathf.Abs(Mathf.Lerp(k[a].value, k[b].value, u) - k[j].value) <= eps;
                }
                if (!ok) { keep.Add(k[b - 1]); a = b - 1; }
            }
            if (k.Length > 1) keep.Add(k[k.Length - 1]);
            var r = new AnimationCurve(keep.Select(x => new Keyframe(x.time, x.value)).ToArray());
            for (int i = 0; i < r.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(r, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(r, i, AnimationUtility.TangentMode.Linear);
            }
            return r;
        }

        static void BakeBody(Rig rig, Plan plan, Pose sit, Pose stand, Vector3 idleToeL, Vector3 idleToeR, string path)
        {
            int n = Mathf.CeilToInt(plan.Length * Fps) + 1;
            MinHeadGap = 1;

            // 몸
            var curves = new AnimationCurve[MName.Length + 7];
            for (int i = 0; i < curves.Length; i++) curves[i] = new AnimationCurve();
            Quaternion prevQ = Quaternion.identity; bool first = true;
            float maxFootErr = 0, errT = 0;
            // 팔 뼈 회전 속도(1/30초 차분) — 튐 점검(9-10·9-11절). 리그엔 팔 비틀림 보조 뼈(RotationConstraint)가 없으니 주 뼈만
            string[] spd = { "LeftUpperArm", "LeftLowerArm", "LeftHand", "RightUpperArm", "RightLowerArm", "RightHand" };
            var prevR = new Quaternion[spd.Length]; var top = new List<(float v, string s)>();
            for (int f = 0; f < n; f++)
            {
                float t = Mathf.Min(f / Fps, plan.Length);
                var hp = Frame(rig, plan, sit, stand, idleToeL, idleToeR, t, out var fl, out var fr);
                float e = Mathf.Max((rig["LeftFoot"].position - fl).magnitude, (rig["RightFoot"].position - fr).magnitude);
                if (e > maxFootErr) { maxFootErr = e; errT = t; }
                if (f % 6 == 0 && t > 2.4f && t < 6.4f) Diag(rig, plan, t, fl, fr);
                rig.Hph.SetHumanPose(ref hp);   // 클립에 들어가는 자세(IK·FK 섞인 값)로 다시 세워서 잰다 — 리그는 IK 결과 그대로라 섞는 구간이 튀어 보였다
                for (int k = 0; k < spd.Length; k++)
                {
                    var bq = rig[spd[k]].rotation;
                    if (f > 0) { float v = Quaternion.Angle(prevR[k], bq) * Fps; if (v > 300) top.Add((v, $"{spd[k]} {v:F0}°/s @{t:F2}")); }
                    prevR[k] = bq;
                }
                for (int i = 0; i < MName.Length; i++) curves[i].AddKey(t, hp.muscles[i]);
                var q = hp.bodyRotation;
                if (!first && Quaternion.Dot(prevQ, q) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                prevQ = q; first = false;
                var T = hp.bodyPosition;
                curves[MName.Length + 0].AddKey(t, T.x); curves[MName.Length + 1].AddKey(t, T.y); curves[MName.Length + 2].AddKey(t, T.z);
                curves[MName.Length + 3].AddKey(t, q.x); curves[MName.Length + 4].AddKey(t, q.y); curves[MName.Length + 5].AddKey(t, q.z); curves[MName.Length + 6].AddKey(t, q.w);
            }
            int before = 0, after = 0;
            for (int i = 0; i < curves.Length; i++)
            {
                before += curves[i].length;
                curves[i] = Reduce(curves[i], i < MName.Length ? 0.002f : 0.0004f);
                after += curves[i].length;
            }
            var clip = NewClip(path);
            for (int i = 0; i < MName.Length; i++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), ClipName(MName[i])), curves[i]);
            string[] rootNames = { "RootT.x", "RootT.y", "RootT.z", "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int i = 0; i < 7; i++)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), rootNames[i]), curves[MName.Length + i]);
            var s = AnimationUtility.GetAnimationClipSettings(clip);
            s.loopTime = false;
            s.loopBlendOrientation = s.keepOriginalOrientation = true;
            s.loopBlendPositionY = s.keepOriginalPositionY = true;
            s.loopBlendPositionXZ = s.keepOriginalPositionXZ = true;
            AnimationUtility.SetAnimationClipSettings(clip, s);
            EditorUtility.SetDirty(clip);
            Log.AppendLine($"afkmotion: 기지개 중(2.4~6.4초) 팔과 머리 최소 간격 {MinHeadGap * 100:F1}cm" + (MinHeadGap < 0 ? " 🔴 팔이 머리를 뚫음" : ""));
            Log.AppendLine($"afkmotion: 팔 뼈 회전 > 300°/s: {top.Count}프레임" + (top.Count > 0 ? " — 큰 순: " + string.Join(", ", top.OrderByDescending(x => x.v).Take(8).Select(x => x.s)) : ""));
            var end = plan.HipsAt(plan.Length);
            Log.AppendLine($"afkmotion: 몸 {path} — {plan.Length:F2}초, 끝 골반 z {end.z:F2}m, 키 {before} → {after}, 발 IK 최대 오차 {maxFootErr * 100:F1}cm({errT:F2}초)");

        }

        // 한쪽 기지개 점검 — 팔꿈치 각(180 = 쭉 폄), 왼 아래팔과 머리 사이(머리 중심 → 아래팔 선분 거리 − 머리 반지름 0.10), 잡는 손 오차, 발 오차
        static float MinHeadGap = 1;
        static void Diag(Rig rig, Plan plan, float t, Vector3 fl, Vector3 fr)
        {
            float Elbow(string sd) => Vector3.Angle(rig[sd + "UpperArm"].position - rig[sd + "LowerArm"].position, rig[sd + "Hand"].position - rig[sd + "LowerArm"].position);
            var head = rig["Head"];
            var c = head.position + head.rotation * Quaternion.Inverse(rig.HeadRot0) * rig.HeadCenter;
            Vector3 a = rig["LeftLowerArm"].position, b = rig["LeftHand"].position;
            float u = Mathf.Clamp01(Vector3.Dot(c - a, b - a) / (b - a).sqrMagnitude);
            float gap = (a + (b - a) * u - c).magnitude - HeadR;
            float gapU = Vector3.Distance(c, ClosestOnSeg(rig["LeftUpperArm"].position, a, c)) - HeadR;
            float gapR = Vector3.Distance(c, ClosestOnSeg(rig["RightUpperArm"].position, rig["RightLowerArm"].position, c)) - HeadR;
            var ru = rig["RightUpperArm"].position; var rl = rig["RightLowerArm"].position;
            float gripD = plan.GripW != null ? (b - Vector3.Lerp(ru, rl, plan.GripAlong)).magnitude : -1;
            float gapRL = Vector3.Distance(c, ClosestOnSeg(rl, rig["RightHand"].position, c)) - HeadR;
            float lean = Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(rig["Neck"].position - rig["Hips"].position, Vector3.forward), Vector3.forward);
            MinHeadGap = Mathf.Min(MinHeadGap, gap, gapU, gapR, gapRL);
            if (Mathf.Abs(t - 4.4f) < 0.01f)
                Log.AppendLine($"afkmotion:   [4.40 뼈] 머리중심 {c:F3} 왼어깨 {rig["LeftUpperArm"].position:F3} 왼팔꿈치 {a:F3} 왼손목 {b:F3} 오른어깨 {ru:F3} 오른팔꿈치 {rl:F3} 오른손목 {rig["RightHand"].position:F3}");
            Log.AppendLine($"afkmotion:   {t:F2}s 상체 옆 {lean:F0}° | 팔꿈치 L {Elbow("Left"):F0}° R {Elbow("Right"):F0}° | 머리와 간격(반지름 {HeadR * 100:F1}) 왼아래팔 {gap * 100:F1} 왼위팔 {gapU * 100:F1} 오른위팔 {gapR * 100:F1} 오른아래팔 {gapRL * 100:F1}cm" + (gripD >= 0 ? $" | 왼손목→잡는 점 {gripD * 100:F1}cm (무게 {plan.GripW(t):F2})" : "") + $" | 손 높이 L {b.y:F2} R {rig["RightHand"].position.y:F2} | 발 y {rig["LeftFoot"].position.y - fl.y:F3}/{rig["RightFoot"].position.y - fr.y:F3}");
        }

        static Vector3 ClosestOnSeg(Vector3 a, Vector3 b, Vector3 p)
        {
            float u = Mathf.Clamp01(Vector3.Dot(p - a, b - a) / Mathf.Max(1e-8f, (b - a).sqrMagnitude));
            return a + (b - a) * u;
        }

        static Transform Built;   // 굽는 동안 빌드 아바타(얼굴 키·렌더러 목록)

        static Result BakeOnce(AnimationClip sitClip, float comp)
        {
            var sit = FromClip(sitClip);
            var stand = FromClip(AssetDatabase.LoadAssetAtPath<AnimationClip>(StandClipPath));
            stand.Q = Quaternion.identity;   // proxy_idle의 몸 방향은 정면으로
            using (var rig = new Rig())
            {
                Apply(rig, sit);
                var chk = new HumanPose(); rig.Hph.GetHumanPose(ref chk);
                float maxD = 0; for (int i = 0; i < 55; i++) maxD = Mathf.Max(maxD, Mathf.Abs(chk.muscles[i] - sit.M[i]));
                Log.AppendLine($"afkmotion: 왕복 확인: muscle 최대 차 {maxD:F4}, 앉은 골반 {rig["Hips"].position:F3}");

                Apply(rig, stand);
                // 팔꿈치 경첩 축: 서기 자세에서 아래팔만 굽혀(Forearm Stretch −0.5) 위팔·아래팔 평면의 법선을 위팔 지역으로 저장
                var bent = stand; bent.M = (float[])stand.M.Clone();
                bent.M[MIdx["Left Forearm Stretch"]] = -0.5f; bent.M[MIdx["Right Forearm Stretch"]] = -0.5f;
                Apply(rig, bent);
                Vector3 Axis(string sd) { var a = rig[sd + "UpperArm"]; var b = rig[sd + "LowerArm"]; var c = rig[sd + "Hand"]; return a.InverseTransformDirection(Vector3.Cross(a.position - b.position, c.position - b.position).normalized); }
                rig.ElbowAxisL = Axis("Left"); rig.ElbowAxisR = Axis("Right");
                Apply(rig, stand);
                rig.HeadRot0 = rig["Head"].rotation; rig.ChestRot0 = rig.ChestTop.rotation;
                MeasureBody(rig);
                HipComp = float.IsNaN(comp) ? 0.018f * RH : comp;
                rig.HeadCenter = Quaternion.Inverse(rig["Head"].rotation) * (HeadC - rig["Head"].position);
                rig.SetToes(RH);
                var idleToeL = rig.ToePos("Left") - rig["LeftFoot"].position;
                var idleToeR = rig.ToePos("Right") - rig["RightFoot"].position;
                Log.AppendLine($"afkmotion: HipComp {HipComp:F4}");

                var mode = Mode == StretchMode.Clasp ? Stretch.Clasp : Mode == StretchMode.Arc ? Stretch.Arc : Stretch.OneArm;
                var plan = MakePlan(rig, sit, stand, mode);
                LastPlan = plan;
                BakeBody(rig, plan, sit, stand, idleToeL, idleToeR, "AFK_Leave");
                int n = Mathf.CeilToInt(plan.Length * Fps) + 1;

                // 얼굴 — 표정 묶음이 쓰는 키(WcAfkFaceMap)를 같은 시간표로(바탕 = 빌드 아바타 기본값, 그 위에 표정)
                if (Faces.Keys.Count > 0)
                {
                    var faceSmr = Built.Find(FacePath) ? Built.Find(FacePath).GetComponent<SkinnedMeshRenderer>() : null;
                    var keys = new SortedSet<string>(Faces.Keys, StringComparer.Ordinal);
                    var baseW = new Dictionary<string, float>();
                    foreach (var k in keys) { int bi = faceSmr != null && faceSmr.sharedMesh != null ? faceSmr.sharedMesh.GetBlendShapeIndex(k) : -1; baseW[k] = bi >= 0 ? faceSmr.GetBlendShapeWeight(bi) : 0; }
                    LastFaceBase = baseW;
                    var fc = keys.ToDictionary(x => x, x => new AnimationCurve());
                    for (int f = 0; f < n; f++)
                    {
                        float t = Mathf.Min(f / Fps, plan.Length);
                        var face = plan.Face(t);
                        foreach (var k in keys) fc[k].AddKey(t, Mathf.Clamp(baseW[k] + (face.TryGetValue(k, out var v) ? v : 0), 0, 100));
                    }
                    var faceClip = NewClip("AFK_Face");
                    int fk0 = 0, fk1 = 0;
                    foreach (var kv in fc)
                    {
                        fk0 += kv.Value.length;
                        var rc = Reduce(kv.Value, 0.5f); fk1 += rc.length;
                        AnimationUtility.SetEditorCurve(faceClip, EditorCurveBinding.FloatCurve(FacePath, typeof(SkinnedMeshRenderer), "blendShape." + kv.Key), rc);
                    }
                    var fs = AnimationUtility.GetAnimationClipSettings(faceClip); fs.loopTime = false; AnimationUtility.SetAnimationClipSettings(faceClip, fs);
                    Log.AppendLine($"afkmotion: 얼굴 '{FacePath}' 키 {keys.Count}개({string.Join(", ", keys)}), 키프레임 {fk0} → {fk1}");
                }
                else { LastFaceBase = new Dictionary<string, float>(); Log.AppendLine("afkmotion: 얼굴 키 없음 — 표정 없이"); }
                return new Result { Length = plan.Length, WalkStart = plan.WalkStart, WalkEnd = plan.WalkEnd, HipComp = HipComp };
            }
        }

        static Vector3 HeadC; static float HeadR = RefHeadR;

        // 서기 자세 리그에서: 몸·팔 배율, 발 치수, 머리(머리 뼈와 그 아래 뼈가 주 가중치인 정점 — 빌드 아바타에서 켜진 렌더러만)
        static void MeasureBody(Rig rig)
        {
            var hips = rig["Hips"].position;
            float armLen = (rig["LeftLowerArm"].position - rig["LeftUpperArm"].position).magnitude + (rig["LeftHand"].position - rig["LeftLowerArm"].position).magnitude;
            RH = hips.y / RefStandHip; RA = armLen / RefArm;
            ScaleGait(RH);
            var al = rig["LeftFoot"].position; var ar = rig["RightFoot"].position;
            AnkleBehind = hips.z - (al.z + ar.z) * 0.5f;
            FootX = 0.075f * RH;
            BallLen = rig.B.TryGetValue("LeftToes", out var lt) ? Vector3.ProjectOnPlane(lt.position - al, Vector3.up).magnitude : 0.10f * RH;
            // 머리
            var head = rig["Head"];
            var under = new HashSet<Transform>(head.GetComponentsInChildren<Transform>(true));
            var on = new HashSet<string>(Built.GetComponentsInChildren<SkinnedMeshRenderer>(false).Where(r => r.enabled).Select(r => r.name));
            var pts = new List<Vector3>();
            foreach (var smr in rig.Go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!on.Contains(smr.name) || smr.sharedMesh == null) continue;
                var bones = smr.bones; var bw = smr.sharedMesh.boneWeights;
                if (bw.Length == 0) continue;
                var m = new Mesh(); smr.BakeMesh(m, true);
                var vs = m.vertices;
                var tr = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                for (int i = 0; i < vs.Length && i < bw.Length; i++)
                {
                    var bi = bw[i].boneIndex0;
                    if (bi < 0 || bi >= bones.Length || bones[bi] == null || !under.Contains(bones[bi])) continue;
                    var p = tr.MultiplyPoint3x4(vs[i]);
                    if (p.y >= head.position.y) pts.Add(p);
                }
                UnityEngine.Object.DestroyImmediate(m);
            }
            if (pts.Count > 50)
            {
                var ys = pts.Select(p => p.y).OrderBy(y => y).ToList();
                float top = ys[(int)(ys.Count * 0.99f)];
                var zs = pts.Select(p => p.z).OrderBy(z => z).ToList();
                float zMid = (zs[(int)(zs.Count * 0.02f)] + zs[(int)(zs.Count * 0.98f)]) * 0.5f;
                var rs = pts.Select(p => new Vector2(p.x - head.position.x, p.z - zMid).magnitude).OrderBy(r => r).ToList();
                HeadR = rs[(int)(rs.Count * 0.9f)];
                HeadC = new Vector3(head.position.x, (head.position.y + top) * 0.5f, zMid);
                Log.AppendLine($"afkmotion: 머리: 정점 {pts.Count}, 머리 뼈 y {head.position.y:F3} · 정수리 {top:F3} · 반지름(90%) {HeadR:F3} · 가운데 {HeadC:F3}");
            }
            else
            {
                HeadR = RefHeadR * RA; HeadC = head.position + new Vector3(0, 0.076f, 0.014f) * RA;
                Log.AppendLine($"afkmotion: 머리: 정점 부족({pts.Count}) — 팔 배율로 어림");
            }
            RHead = HeadR / RefHeadR;
            HeadFromShoulderL = HeadC - rig["LeftUpperArm"].position;
            Log.AppendLine($"afkmotion: 몸: 서기 골반 {hips.y:F3} → RH {RH:F3}, 팔 {armLen:F3} → RA {RA:F3}, 머리 RHead {RHead:F3}, 머리−왼어깨 {HeadFromShoulderL:F3}(Lapwing {RefHeadFromShoulderL:F3}), 어깨 y {rig["LeftUpperArm"].position.y:F3}");
            Log.AppendLine($"afkmotion: 발: 발목 뒤 {AnkleBehind:F3}(Lapwing 0.11), 발 간격 {FootX:F3}, 발볼 {BallLen:F3}(Lapwing 0.10) / 걸음: {Steps}걸음 × {StepT:F2}초, 최고 {VMax:F2}m/s(보폭 {VMax * StepT:F2}m)");
        }
    }
}
#endif
