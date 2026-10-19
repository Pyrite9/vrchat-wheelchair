// Pyrite9 Wheelchair — 앉은 채 AFK 모션 레이어를 빌드된 Action·FX에 붙인다(own-model 9-56, 개발 모듈 WcAfkBuild에서 옮김)
//   Action 'Wheelchair AFK'       : Off → Leave(AFK & 휠체어 켜짐, 머리·손·손가락·눈·입·골반·발 = Animation, Action 무게 1) → 풀리면 Back(1초 크로스페이드, 앉기)
//                                   → Restore(머리·손·손가락·눈·입 = Tracking, 골반·발 = Animation — 휠체어 'On <모드>' 재진입이 모드에 맞게) → Off
//                                   휠체어 FBT 모드면 Restore → Restore FBT(전부 Tracking, Action 무게 0) → Off
//   Action 'Wheelchair AFK Track' : AFK 동안 0.25초마다 전신 = Animation 다시(다른 레이어가 Tracking을 걸 때의 보험)
//   FX 'Wheelchair AFK Freeze'    : 휠체어 루트 VRC Parent Constraint IsActive 0(몸이 걸어가도 휠체어는 제자리) → 풀리면 1.2초 더 → 1 → Off
//   FX 'Wheelchair AFK Face'      : AFK 얼굴 → 풀리면 Reset(표정 키를 빌드 기본값으로 0.1초) → Off
//   FX 'Wheelchair AFK Fade'      : 사라지기 — 디졸브(사본 머티리얼로 바꿔 끼우고 _AlphaMaskValue 1 → 0) 또는 즉시(눈이 다 감기는 순간)
//   🔴 WD off(FX) 레이어는 지나간 값을 붙잡는다 → Off·빈 상태는 커브 없는 빈 클립. 머티리얼·알파·표정·IsActive는 지나가는 상태에서만 쓰고,
//      끝에 원래 값을 한 번 써 둔다(아바타 자체 머티리얼 바꾸기·표정 레이어를 막지 않게). Action은 WD on(빈 상태가 휴머노이드를 덮지 않게)
//   lilToon(알파 마스크 있는 것)은 Cutout+Dither 사본으로 디졸브, 그 밖(Poiyomi·lilToon Lite/Multi·FakeShadow 등)은 페이드 중간에 한 번에 숨김
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Pyrite9.Wheelchair
{
    public static class WcAfkLayers
    {
        public const string ActionLayer = "Wheelchair AFK", TrackLayer = "Wheelchair AFK Track";
        public const string FreezeLayer = "Wheelchair AFK Freeze", FaceLayer = "Wheelchair AFK Face", FadeLayer = "Wheelchair AFK Fade";

        // 이미 AFK 모션이 있는 아바타(개발 모듈·Pyrite WheelchairPlus)는 건너뛴다
        public static bool HasOtherAfk(AnimatorController act) =>
            act.layers.Any(l => l.name == ActionLayer || l.name.StartsWith("WheelchairPlus AFK"));

        sealed class Ctx
        {
            public Action<UnityEngine.Object> Save;
            public AnimatorController Ac;
            public AnimationClip Empty;
        }

        static AnimatorControllerParameter Param(AnimatorController ac, string name, AnimatorControllerParameterType t)
        {
            var p = ac.parameters.FirstOrDefault(x => x.name == name);
            if (p != null) return p;
            ac.AddParameter(name, t);
            return ac.parameters.First(x => x.name == name);
        }

        static void Cond(AnimatorStateTransition t, AnimatorControllerParameter p, bool on)
        {
            if (p.type == AnimatorControllerParameterType.Bool) t.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, p.name);
            else t.AddCondition(on ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, 0.5f, p.name);
        }

        static AnimatorStateMachine Layer(Ctx c, string name)
        {
            var sm = new AnimatorStateMachine { name = name, hideFlags = HideFlags.HideInHierarchy };
            c.Save(sm);
            var ls = c.Ac.layers.ToList();
            ls.Add(new AnimatorControllerLayer { name = name, stateMachine = sm, defaultWeight = 1f });
            c.Ac.layers = ls.ToArray();
            return sm;
        }

        static AnimatorState S(Ctx c, AnimatorStateMachine sm, string n, Motion m, float x, float y, bool wd)
        {
            var s = sm.AddState(n, new Vector3(x, y, 0));
            if (!EditorUtility.IsPersistent(s)) c.Save(s);
            s.motion = m; s.writeDefaultValues = wd; s.iKOnFeet = false;
            return s;
        }

        static AnimatorStateTransition T(Ctx c, AnimatorState a, AnimatorState b, float dur = 0, float exit = -1)
        {
            var t = a.AddTransition(b);
            if (!EditorUtility.IsPersistent(t)) c.Save(t);
            t.hasExitTime = exit >= 0; t.exitTime = Mathf.Max(0, exit); t.duration = dur; t.hasFixedDuration = true;
            return t;
        }

        static VRCAnimatorTrackingControl Track(Ctx c, AnimatorState s, VRC_AnimatorTrackingControl.TrackingType body, VRC_AnimatorTrackingControl.TrackingType hipFeet, VRC_AnimatorTrackingControl.TrackingType mouth)
        {
            var b = s.AddStateMachineBehaviour<VRCAnimatorTrackingControl>();
            if (!EditorUtility.IsPersistent(b)) c.Save(b);
            b.trackingHead = b.trackingLeftHand = b.trackingRightHand = b.trackingLeftFingers = b.trackingRightFingers = b.trackingEyes = body;
            b.trackingHip = b.trackingLeftFoot = b.trackingRightFoot = hipFeet;
            b.trackingMouth = mouth;
            return b;
        }

        static void Weight(Ctx c, AnimatorState s, float goal, float blend)
        {
            var b = s.AddStateMachineBehaviour<VRCPlayableLayerControl>();
            if (!EditorUtility.IsPersistent(b)) c.Save(b);
            b.layer = VRC_PlayableLayerControl.BlendableLayer.Action; b.goalWeight = goal; b.blendDuration = blend;
        }

        static AnimationClip Clip(Ctx c, string name)
        {
            var clip = new AnimationClip { name = name };
            c.Save(clip);
            return clip;
        }

        static AnimationCurve Const(float v, float len) => new AnimationCurve(new Keyframe(0, v), new Keyframe(len, v));

        // ================================================================ Action
        public static void AddAction(AnimatorController act, WcAfkMotion.Result r, bool hasFace, Action<UnityEngine.Object> save, StringBuilder log)
        {
            var c = new Ctx { Ac = act, Save = save };
            var afk = Param(act, "AFK", AnimatorControllerParameterType.Bool);
            var on = Param(act, WheelchairAfk.OnParam, AnimatorControllerParameterType.Float);
            var fbt = Param(act, "Wheelchair/FBT", AnimatorControllerParameterType.Bool);
            var A = VRC_AnimatorTrackingControl.TrackingType.Animation;
            var Tr = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            var mouthIn = hasFace ? A : VRC_AnimatorTrackingControl.TrackingType.NoChange;
            var mouthOut = hasFace ? Tr : VRC_AnimatorTrackingControl.TrackingType.NoChange;

            var sm = Layer(c, ActionLayer);
            var off = S(c, sm, "Off", null, 300, 0, true); sm.defaultState = off;
            var leave = S(c, sm, "Leave", r.Leave, 300, 100, true);
            Track(c, leave, A, A, mouthIn);
            Weight(c, leave, 1f, 0.25f);   // 휠체어 FBT 모드(Sit Off = 무게 0)에서도 보이게(9-52-8)
            var back = S(c, sm, "Back", r.Sit, 300, 200, true);
            var restore = S(c, sm, "Restore", r.Sit, 300, 300, true);
            Track(c, restore, Tr, A, mouthOut);
            var restoreFbt = S(c, sm, "Restore FBT", r.Sit, 600, 300, true);
            Track(c, restoreFbt, Tr, Tr, mouthOut);
            Weight(c, restoreFbt, 0f, 0.5f);
            var t0 = T(c, off, leave); Cond(t0, afk, true); Cond(t0, on, true);
            Cond(T(c, leave, back, 1), afk, false);
            Cond(T(c, leave, back, 1), on, false);
            T(c, back, restore, 0, 1f);
            Cond(T(c, restore, off, 0, 0.6f), fbt, false);
            Cond(T(c, restore, restoreFbt, 0, 0.6f), fbt, true);
            T(c, restoreFbt, off, 0, 0.6f);

            var tm = Layer(c, TrackLayer);
            var idle = S(c, tm, "Idle", null, 300, 0, true); tm.defaultState = idle;
            var ha = S(c, tm, "Hold A", null, 300, 100, true); var hb = S(c, tm, "Hold B", null, 550, 100, true);
            Track(c, ha, A, A, mouthIn); Track(c, hb, A, A, mouthIn);
            foreach (var (x, y, ex) in new[] { (idle, ha, -1f), (ha, hb, 0.25f), (hb, ha, 0.25f) }) { var t = T(c, x, y, 0, ex); Cond(t, afk, true); Cond(t, on, true); }
            foreach (var h in new[] { ha, hb }) { Cond(T(c, h, idle), afk, false); Cond(T(c, h, idle), on, false); }
            EditorUtility.SetDirty(act);
            log.AppendLine($"afkmotion: Action 레이어 '{ActionLayer}'·'{TrackLayer}' 추가 (WD on, Leave Action 무게 1, FBT 모드 복귀 Restore FBT)");
        }

        // ================================================================ FX
        sealed class Slot { public string Path; public Type Type; public int Index; public Material Orig, Fade; public bool Pop; }

        public static void AddFx(AnimatorController fx, Transform root, Transform holder, WcAfkMotion.Result r, AfkVanish vanish, Action<UnityEngine.Object> save, StringBuilder log)
        {
            var c = new Ctx { Ac = fx, Save = save };
            c.Empty = Clip(c, "AFK_Empty");
            var afk = Param(fx, "AFK", AnimatorControllerParameterType.Bool);
            var on = Param(fx, WheelchairAfk.OnParam, AnimatorControllerParameterType.Float);
            void OnAfk(AnimatorStateTransition t) { Cond(t, afk, true); Cond(t, on, true); }
            void OffAfk(AnimatorState a, AnimatorState b, float dur = 0) { Cond(T(c, a, b, dur), afk, false); Cond(T(c, a, b, dur), on, false); }

            // ---- Freeze: 휠체어 루트 VRC Parent Constraint
            var chairPath = AnimationUtility.CalculateTransformPath(holder, root);
            var pc = holder.GetComponents<Component>().FirstOrDefault(x => x != null && x.GetType().Name == "VRCParentConstraint");
            if (pc != null)
            {
                var freeze = Clip(c, "AFK_Freeze"); AnimationUtility.SetEditorCurve(freeze, EditorCurveBinding.FloatCurve(chairPath, pc.GetType(), "IsActive"), Const(0, 1));
                var unfreeze = Clip(c, "AFK_Unfreeze"); AnimationUtility.SetEditorCurve(unfreeze, EditorCurveBinding.FloatCurve(chairPath, pc.GetType(), "IsActive"), Const(1, 0.1f));
                var fm = Layer(c, FreezeLayer);
                var fOff = S(c, fm, "Off", c.Empty, 300, 0, false); fm.defaultState = fOff;
                var frozen = S(c, fm, "Frozen", freeze, 300, 100, false);
                var hold = S(c, fm, "Unfreezing", freeze, 300, 200, false);
                var thaw = S(c, fm, "Unfreeze", unfreeze, 300, 300, false);
                OnAfk(T(c, fOff, frozen));
                OffAfk(frozen, hold);
                T(c, hold, thaw, 0, 1.2f);   // 몸이 앉을 때까지(Back 1초) 더 고정
                T(c, thaw, fOff, 0, 1f);
            }
            else log.AppendLine("afkmotion: WARN the wheelchair root has no VRC Parent Constraint — the chair may follow the avatar while AFK");

            // ---- Face
            if (r.Face != null && r.FaceBase != null && r.FaceBase.Count > 0)
            {
                var reset = Clip(c, "AFK_FaceReset");
                foreach (var kv in r.FaceBase)
                    AnimationUtility.SetEditorCurve(reset, EditorCurveBinding.FloatCurve(r.FacePath, typeof(SkinnedMeshRenderer), "blendShape." + kv.Key), Const(kv.Value, 0.1f));
                var em = Layer(c, FaceLayer);
                var eOff = S(c, em, "Off", c.Empty, 300, 0, false); em.defaultState = eOff;
                var eFace = S(c, em, "Face", r.Face, 300, 100, false);
                var eReset = S(c, em, "Reset", reset, 300, 200, false);
                OnAfk(T(c, eOff, eFace));
                OffAfk(eFace, eReset);
                T(c, eReset, eOff, 0, 1f);
            }

            AddFade(c, root, holder, r, vanish, OnAfk, afk, on, log);
            // 빈 상태 채우기(🔴 WD off 빈 상태 함정) — 우리 레이어만
            foreach (var l in fx.layers.Where(l => l.name.StartsWith("Wheelchair AFK")))
                foreach (var cs in l.stateMachine.states) if (cs.state.motion == null) cs.state.motion = c.Empty;
            EditorUtility.SetDirty(fx);
        }

        static float EaseS(float x) { x = Mathf.Clamp01(x); return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * x); }
        static AnimationCurve Linear(AnimationCurve cv)
        {
            for (int i = 0; i < cv.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cv, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cv, i, AnimationUtility.TangentMode.Linear); }
            return cv;
        }
        static AnimationCurve Step(params (float t, float v)[] keys)
        {
            var cv = new AnimationCurve(keys.Select(k => new Keyframe(k.t, k.v)).ToArray());
            for (int i = 0; i < cv.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cv, i, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(cv, i, AnimationUtility.TangentMode.Constant); }
            return cv;
        }

        static void AddFade(Ctx c, Transform root, Transform holder, WcAfkMotion.Result r, AfkVanish vanish, Action<AnimatorStateTransition> onAfk, AnimatorControllerParameter afk, AnimatorControllerParameter on, StringBuilder log)
        {
            // 시간(Leave 기준 초): 걷기가 감속하기 전(끝 0.9초 전)에 다 사라지게
            const float CloseT = 0.4f, AlphaT0 = 0.5f, FadeT = 1.5f;
            float fadeEnd = r.WalkEnd - 0.9f - 0.1f;
            float waitT = Mathf.Max(r.WalkStart + 0.5f, fadeEnd - AlphaT0 - FadeT);
            const float InHold = 1.0f, InT = 1.0f, InOpen0 = 1.95f, InOpen1 = 2.25f, InLen = 2.3f;
            bool instant = vanish == AfkVanish.Instant;

            var slots = new List<Slot>(); var copies = new Dictionary<Material, (Material m, bool pop)>();
            Material invisible = null;
            foreach (var rd in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(rd is SkinnedMeshRenderer) && !(rd is MeshRenderer)) continue;
                if (rd.transform.IsChildOf(holder) || rd.gameObject.CompareTag("EditorOnly")) continue;
                string path = AnimationUtility.CalculateTransformPath(rd.transform, root);
                var mats = rd.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    if (!copies.TryGetValue(m, out var f))
                    {
                        var cp = MakeCopy(m, log);
                        if (cp == null) { if (invisible == null) { invisible = MakeInvisible(); c.Save(invisible); } f = (invisible, true); }
                        else { c.Save(cp); f = (cp, false); }
                        copies[m] = f;
                    }
                    slots.Add(new Slot { Path = path, Type = rd.GetType(), Index = i, Orig = m, Fade = f.m, Pop = f.pop });
                }
            }
            var fadePaths = slots.Where(s => !s.Pop).Select(s => (s.Path, s.Type)).Distinct().ToList();
            log.AppendLine($"afkmotion: 사라지기 {(instant ? "즉시" : "디졸브")} — 렌더러 {slots.Select(s => s.Path).Distinct().Count()}개, 슬롯 {slots.Count}개, 디졸브 사본 {copies.Values.Count(v => !v.pop)}개, 한 번에 숨김 {slots.Count(s => s.Pop)}슬롯");
            if (slots.Count == 0) return;

            EditorCurveBinding Alpha((string Path, Type Type) p) => EditorCurveBinding.FloatCurve(p.Path, p.Type, "material._AlphaMaskValue");
            void Mats(AnimationClip clip, Func<Slot, (float t, bool fade)[]> keys)
            {
                foreach (var s in slots)
                {
                    var b = EditorCurveBinding.PPtrCurve(s.Path, s.Type, $"m_Materials.Array.data[{s.Index}]");
                    AnimationUtility.SetObjectReferenceCurve(clip, b, keys(s).Select(k => new ObjectReferenceKeyframe { time = k.t, value = k.fade ? s.Fade : s.Orig }).ToArray());
                }
            }

            // Wait: 걷기 중 사라질 때까지 기다림(알파 1만 — 길이용)
            var cWait = Clip(c, "AFK_Fade_Wait");
            foreach (var p in fadePaths) AnimationUtility.SetEditorCurve(cWait, Alpha(p), Const(1, waitT));
            if (fadePaths.Count == 0) AnimationUtility.SetEditorCurve(cWait, EditorCurveBinding.FloatCurve("Pyrite9WC_AfkTimer", typeof(GameObject), "m_IsActive"), Const(1, waitT));   // 길이용(없는 경로 — 아무것도 안 건드림)

            float outLen = AlphaT0 + FadeT + 0.3f, popOut = instant ? CloseT : AlphaT0 + FadeT * 0.5f, popIn = instant ? InHold : InHold + InT * 0.5f;
            var cOut = Clip(c, instant ? "AFK_Fade_Out_Instant" : "AFK_Fade_Out");
            float swapOut = instant ? CloseT : 0f;
            Mats(cOut, s => s.Pop ? new[] { (0f, false), (popOut, true) } : new[] { (0f, false), (swapOut, true) });
            foreach (var p in fadePaths)
            {
                AnimationCurve cv;
                if (instant) cv = Step((0f, 1f), (CloseT, 0f), (outLen, 0f));
                else
                {
                    cv = new AnimationCurve(); cv.AddKey(0f, 1f);
                    for (int k = 0; k <= 24; k++) { float u = k / 24f; cv.AddKey(AlphaT0 + FadeT * u, 1f - EaseS(u)); }
                    cv.AddKey(outLen, 0f); Linear(cv);
                }
                AnimationUtility.SetEditorCurve(cOut, Alpha(p), cv);
            }
            var cIn = Clip(c, instant ? "AFK_Fade_In_Instant" : "AFK_Fade_In");
            float swapBack = instant ? InHold : InLen;
            Mats(cIn, s => s.Pop ? new[] { (0f, true), (popIn, false), (InLen, false) } : new[] { (0f, true), (swapBack, false), (InLen, false) });
            foreach (var p in fadePaths)
            {
                AnimationCurve cv;
                if (instant) cv = Step((0f, 0f), (InHold, 1f), (InLen, 1f));
                else
                {
                    cv = new AnimationCurve(); cv.AddKey(0f, 0f); cv.AddKey(InHold, 0f);
                    for (int k = 1; k <= 16; k++) { float u = k / 16f; cv.AddKey(InHold + InT * u, EaseS(u)); }
                    cv.AddKey(InLen, 1f); Linear(cv);
                }
                AnimationUtility.SetEditorCurve(cIn, Alpha(p), cv);
            }
            // 눈 감기(디졸브·즉시 모두 사라지기 직전에 눈을 감는다) — Out 0~CloseT 감기, In 끝에 뜨기
            if (r.Face != null && r.FaceBase != null)
            {
                var blink = r.FaceBase.Keys.Where(k => r.BlinkKeys != null && r.BlinkKeys.Contains(k)).ToList();
                foreach (var k in blink)
                {
                    float b0 = r.FaceBase[k];
                    var bind = EditorCurveBinding.FloatCurve(r.FacePath, typeof(SkinnedMeshRenderer), "blendShape." + k);
                    var co = new AnimationCurve(); co.AddKey(0f, b0);
                    for (int i = 0; i <= 8; i++) { float u = i / 8f; co.AddKey(CloseT * u, Mathf.Lerp(b0, 100f, EaseS(u))); }
                    co.AddKey(outLen, 100f);
                    AnimationUtility.SetEditorCurve(cOut, bind, Linear(co));
                    float o0 = instant ? InHold + 0.3f : InOpen0, o1 = instant ? InHold + 0.6f : InOpen1;
                    var ci = new AnimationCurve(); ci.AddKey(0f, 100f);
                    for (int i = 0; i <= 8; i++) { float u = i / 8f; ci.AddKey(o0 + (o1 - o0) * u, Mathf.Lerp(100f, b0, EaseS(u))); }
                    ci.AddKey(InLen, b0);
                    AnimationUtility.SetEditorCurve(cIn, bind, Linear(ci));
                }
                // 웃는 눈 등 다른 눈 키는 감는 동안 기본값으로(감기 키와 겹치면 눈이 무너진다 — 9-52-6)
                foreach (var k in r.FaceBase.Keys.Where(k => r.EyeKeys != null && r.EyeKeys.Contains(k)))
                {
                    var bind = EditorCurveBinding.FloatCurve(r.FacePath, typeof(SkinnedMeshRenderer), "blendShape." + k);
                    AnimationUtility.SetEditorCurve(cOut, bind, Const(r.FaceBase[k], outLen));
                    AnimationUtility.SetEditorCurve(cIn, bind, Const(r.FaceBase[k], InLen));
                }
            }

            var sm = Layer(c, FadeLayer);
            var sOff = S(c, sm, "Off", c.Empty, 300, 0, false); sm.defaultState = sOff;
            var sWait = S(c, sm, "Wait", cWait, 300, 100, false);
            var sOut = S(c, sm, "FadeOut", cOut, 300, 200, false);
            var sIn = S(c, sm, "FadeIn", cIn, 600, 100, false);
            onAfk(T(c, sOff, sWait));
            Cond(T(c, sWait, sOff), afk, false); Cond(T(c, sWait, sOff), on, false);
            T(c, sWait, sOut, 0, 1f);
            Cond(T(c, sOut, sIn), afk, false); Cond(T(c, sOut, sIn), on, false);
            T(c, sIn, sOff, 0, 1f);
            log.AppendLine($"afkmotion: FX '{FadeLayer}' — Leave 기준 {waitT:F2}초 기다림 → " + (instant ? $"{CloseT}초에 한 번에 사라짐 / 돌아올 때 {InHold}초에 한 번에" : $"{AlphaT0}~{AlphaT0 + FadeT}초 디졸브 / 돌아올 때 {InHold}~{InHold + InT}초"));
        }

        // ---------------------------------------------------------------- 사본 머티리얼
        static Material MakeInvisible()
        {
            var f = new Material(Shader.Find("lilToon")) { name = "AFK_Hidden" };
            SetupCutout(f, false);
            f.SetColor("_Color", new Color(0, 0, 0, 0));
            f.SetFloat("_Cutoff", 0.5f);
            return f;
        }

        // lilToon(알파 마스크가 있는 일반 변형)만 디졸브 사본, 아니면 null(한 번에 숨김)
        static Material MakeCopy(Material m, StringBuilder log)
        {
            var sh = m.shader != null ? m.shader.name : "";
            bool lil = sh.Contains("lilToon") && !sh.Contains("Lite") && !sh.Contains("Multi") && !sh.Contains("FakeShadow") && !sh.Contains("Fur") && !sh.Contains("Gem") && !sh.Contains("Refraction") && m.HasProperty("_AlphaMaskValue");
            if (!lil) return null;
            bool transparent = sh.Contains("Transparent");
            bool outline = sh.Contains("Outline");
            var f = new Material(m) { name = m.name + "_AfkFade" };
            SetupCutout(f, outline);
            f.SetFloat("_AlphaMaskMode", transparent ? 2 : 1);   // 불투명 원본은 대체, Transparent는 곱하기(머리끝 알파 유지)
            f.SetTexture("_AlphaMask", null);
            f.SetFloat("_AlphaMaskScale", 0f);
            f.SetFloat("_AlphaMaskValue", 1f);
            f.SetFloat("_UseDither", 1f);
            f.SetTexture("_DitherTex", Bayer());
            f.SetFloat("_DitherMaxValue", 255f);
            f.SetFloat("_Cutoff", 0.5f);
            f.renderQueue = m.renderQueue;
            return f;
        }

        static Texture2D bayer;
        static Texture2D Bayer()
        {
            if (bayer != null) return bayer;
            bayer = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/jp.lilxyzw.liltoon/Texture/lil_bayer_16x16.png");
            if (bayer == null)
            {
                var g = AssetDatabase.FindAssets("lil_bayer_16x16 t:Texture2D").FirstOrDefault();
                if (g != null) bayer = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g));
            }
            return bayer;
        }

        static void SetupCutout(Material f, bool outline)
        {
            var utils = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("lilToon.lilMaterialUtils")).FirstOrDefault(x => x != null);
            var mi = utils?.GetMethod("SetupMaterialWithRenderingMode", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (mi == null) throw new Exception("lilToon SetupMaterialWithRenderingMode not found");
            var ps = mi.GetParameters();
            int q = f.renderQueue;
            mi.Invoke(null, new object[] { f, Enum.ToObject(ps[1].ParameterType, 1), Enum.ToObject(ps[2].ParameterType, 0), outline, false, false, false });   // Cutout, Normal
            f.SetFloat("_TransparentMode", 1f);
            f.renderQueue = q;
        }
    }
}
#endif
