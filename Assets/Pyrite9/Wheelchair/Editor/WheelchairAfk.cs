// Pyrite9 Wheelchair — 앉아 있는 동안 AFK (빌드 사본만 고친다, 배포판·아바타 원본 컨트롤러는 그대로)
//   PauseHoldLoop  : 'Wheelchair Sit'의 Hold A ↔ Hold B는 약 0.5초마다(앉기 클립 0.04초 × exit time 12) 들어가며 머리·손 = Tracking을 다시 건다
//                    → AFK 동안 다른 레이어(AFK 모션)의 머리 = Animation이 덮여, VR에선 머리가 HMD에 묶이고 몸만 움직였다(Pyrite 인게임 2026-10-19).
//                    전이에 !AFK를 붙이면 안 된다 — exit time > 1 전이는 그 시점에 한 번만 검사돼, AFK가 끝나도 루프가 다시 안 돈다(Play 실측).
//                    → 모드(Desktop·VR·VR Trackers)마다 'AFK Wait <모드>'(Hold A와 같은 모션, 트래킹 행동 없음)
//                      On/Hold A/Hold B → Wait (AFK, 전이 목록 맨 앞) · Wait → 'AFK Return <모드>' (!AFK) → 2.3초 뒤 'On <모드>'
//                      (모드별 트래킹·Pose Space·Action 가중치를 처음 앉을 때처럼 다시 건다 — 다른 레이어가 AFK를 풀며 건 트래킹보다 늦게)
//                      · Hold A의 조건 전이(휠체어 끔·FBT·VRMode·TrackingType → Off)를 Wait·Return에도 복사, Return 중 다시 AFK면 Wait로
//   BlockAvatarAfk : 아바타 자체 AFK(GoGo Loco 'Action&AFK', VRChat 기본식 'Prepare AFK', FX의 AFK 레이어 등)는 풀릴 때
//                    'Restore Tracking'에서 골반·발 = Tracking, Pose Space 해제를 1~2초 늦게 걸어 휠체어 앉기를 덮어썼다(Pyrite 10-19).
//                    → Action·FX의 'Wheelchair…' 아닌 레이어에서 AFK가 켜질 때의 전이에 Wheelchair/On < 0.5를 붙인다.
//                    휠체어가 꺼져 있으면 아바타 AFK 그대로
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Pyrite9.Wheelchair
{
    public static class WheelchairAfk
    {
        public const string SitLayer = "Wheelchair Sit";
        public const string OnParam = "Wheelchair/On";
        public const string AfkParam = "AFK";
        public const string InstantParam = "WheelchairAFK/Instant";   // AFK 모션 모듈(배포판 밖)의 사라지기 분기(디졸브/즉시) — 동기화 안 함, 기본값만
        public const float ReturnDelay = 2.3f;   // 풀린 뒤 'On <모드>' 재진입까지 — AFK 모션 레이어의 복귀(풀린 뒤 2.03초) 직후(Pyrite Play 시뮬레이션 10-19)

        static AnimatorCondition Cond(AnimatorControllerParameter p, bool on)
        {
            if (p.type == AnimatorControllerParameterType.Bool)
                return new AnimatorCondition { parameter = p.name, mode = on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot };
            return new AnimatorCondition { parameter = p.name, mode = on ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less, threshold = 0.5f };
        }

        static AnimatorControllerParameter Param(AnimatorController ac, string name, AnimatorControllerParameterType addAs)
        {
            var p = ac.parameters.FirstOrDefault(x => x.name == name);
            if (p != null) return p;
            ac.AddParameter(name, addAs);
            return ac.parameters.First(x => x.name == name);
        }

        public static void PauseHoldLoop(AnimatorController action, System.Func<Object, bool> writable, StringBuilder log)
        {
            var layer = action.layers.FirstOrDefault(l => l.name == SitLayer);
            if (layer == null) { log.AppendLine($"afk: '{SitLayer}' 레이어 없음 — 건너뜀"); return; }
            var p = Param(action, AfkParam, AnimatorControllerParameterType.Bool);
            var afkOn = Cond(p, true); var afkOff = Cond(p, false);
            var sm = layer.stateMachine;
            var states = sm.states.Select(c => c.state).ToList();
            int modes = 0;
            foreach (var holdA in states.Where(s => s.name.StartsWith("Hold A ")).ToList())
            {
                string mode = holdA.name.Substring("Hold A ".Length);
                if (states.Any(s => s.name == "AFK Wait " + mode)) { log.AppendLine($"afk: 'AFK Wait {mode}' 이미 있음 — 건너뜀"); continue; }
                if (!writable(holdA)) { log.AppendLine($"afk: '{holdA.name}'이 빌드 사본이 아님 — 원본 보호를 위해 건너뜀"); continue; }
                var pos = sm.states.First(c => c.state == holdA).position;
                var offTransitions = holdA.transitions.Where(x => !x.hasExitTime && x.destinationState != null).ToList();
                AnimatorState Clone(string name, Vector3 at)
                {
                    var s = sm.AddState(name, at);
                    s.motion = holdA.motion; s.writeDefaultValues = holdA.writeDefaultValues; s.iKOnFeet = holdA.iKOnFeet;
                    foreach (var src in offTransitions)   // Off로 가는 조건 전이(휠체어 끔·FBT·VRMode·TrackingType)
                    {
                        var c = s.AddTransition(src.destinationState);
                        c.hasExitTime = false; c.duration = src.duration; c.hasFixedDuration = src.hasFixedDuration; c.conditions = src.conditions;
                    }
                    return s;
                }
                var wait = Clone("AFK Wait " + mode, pos + new Vector3(0, 120, 0));
                var ret = Clone("AFK Return " + mode, pos + new Vector3(0, 180, 0));
                var on = states.FirstOrDefault(s => s.name == "On " + mode);
                var back = wait.AddTransition(ret); back.hasExitTime = false; back.duration = 0; back.conditions = new[] { afkOff };
                float clipLen = holdA.motion is AnimationClip hc && hc.length > 0.001f ? hc.length : 0.04f;
                var toOn = ret.AddTransition(on ?? holdA); toOn.hasExitTime = true; toOn.exitTime = ReturnDelay / clipLen; toOn.duration = 0; toOn.conditions = new AnimatorCondition[0];
                var reAfk = ret.AddTransition(wait); reAfk.hasExitTime = false; reAfk.duration = 0; reAfk.conditions = new[] { afkOn };
                ret.transitions = new[] { reAfk }.Concat(ret.transitions.Where(x => x != reAfk)).ToArray();
                int entries = 0;
                foreach (var s in states.Where(x => x.name == "On " + mode || x.name == "Hold A " + mode || x.name == "Hold B " + mode))
                {
                    var tIn = s.AddTransition(wait); tIn.hasExitTime = false; tIn.duration = 0; tIn.conditions = new[] { afkOn };
                    s.transitions = new[] { tIn }.Concat(s.transitions.Where(x => x != tIn)).ToArray();
                    EditorUtility.SetDirty(s); entries++;
                }
                log.AppendLine($"afk: 'AFK Wait {mode}' — 들어오는 전이 {entries}개, Off 전이 복사 {offTransitions.Count}개, 풀리면 'AFK Return' {ReturnDelay}초 → {(on ?? holdA).name}(exitTime {toOn.exitTime:F1})");
                modes++;
            }
            EditorUtility.SetDirty(action);
            log.AppendLine($"afk: 모드 {modes}개 처리 (AFK 파라미터 {p.type})");
        }

        // 🔴 휠체어를 끄면 'Wheelchair Sit'·'Wheelchair Arms Desktop'은 모션 없는 Off에 머문다. 아바타 Action이 WD off(MA가 맞춤)면
        //   이 빈 상태가 아래 레이어(아바타 자체 AFK·이모트)의 휴머노이드를 덮는다 — Kipfel 휠체어 끔 + AFK에서 오른발이 바닥 아래 0.18m,
        //   머리·손이 어긋남(관리자 인게임 "휠체어를 껐을 때 캐릭터 기본 AFK가 안 나감", Play 재현 2026-10-19).
        //   → 휠체어 Action 레이어의 모션 없는 상태만 WD on(빈 상태 = 통과). 휠체어 켠 동작·Space Drag는 그대로(Play 확인)
        // AFK 모션 모듈이 있으면(FX에 InstantParam) 인스펙터 'AFK ▸ 사라지기'를 그 파라미터 기본값으로 — 동기화 파라미터가 아니라 모두 같은 기본값을 본다
        public static void SetVanish(AnimatorController fx, bool on, StringBuilder log)
        {
            var ps = fx.parameters;
            int i = System.Array.FindIndex(ps, x => x.name == InstantParam);
            if (i < 0) return;
            if (ps[i].type == AnimatorControllerParameterType.Bool) ps[i].defaultBool = on;
            else if (ps[i].type == AnimatorControllerParameterType.Float) ps[i].defaultFloat = on ? 1f : 0f;
            else if (ps[i].type == AnimatorControllerParameterType.Int) ps[i].defaultInt = on ? 1 : 0;
            fx.parameters = ps;
            log.AppendLine($"afk: AFK 사라지기 = {(on ? "즉시" : "디졸브")} ({InstantParam} 기본값)");
        }

        public static void PassThroughEmptyStates(AnimatorController action, System.Func<Object, bool> writable, StringBuilder log)
        {
            int n = 0;
            var names = new List<string>();
            foreach (var layer in action.layers.Where(l => l.name.StartsWith("Wheelchair")))
                foreach (var cs in layer.stateMachine.states)
                {
                    var s = cs.state;
                    if (s.motion != null || s.writeDefaultValues) continue;
                    if (!writable(s)) continue;
                    s.writeDefaultValues = true; EditorUtility.SetDirty(s); n++;
                    names.Add(layer.name + "/" + s.name);
                }
            log.AppendLine($"afk: 휠체어 Action 레이어 빈 상태 WD on {n}개" + (n > 0 ? " — " + string.Join(", ", names) : ""));
        }

        public static void BlockAvatarAfk(AnimatorController ac, string label, System.Func<Object, bool> writable, StringBuilder log)
        {
            var hasAfk = ac.parameters.Any(x => x.name == AfkParam);
            if (!hasAfk) { log.AppendLine($"afk: {label}에 AFK 파라미터 없음 — 막을 게 없음"); return; }
            AnimatorControllerParameter wp = null;
            int n = 0, skipped = 0;
            var layers = new List<string>();
            foreach (var layer in ac.layers)
            {
                if (layer.name.StartsWith("Wheelchair")) continue;   // 배포판·AFK 모션 레이어
                int before = n;
                foreach (var t in AllTransitions(layer.stateMachine))
                {
                    if (!t.conditions.Any(c => c.parameter == AfkParam && (c.mode == AnimatorConditionMode.If || c.mode == AnimatorConditionMode.Greater && c.threshold < 1f || c.mode == AnimatorConditionMode.Equals && c.threshold >= 1f))) continue;
                    if (t.conditions.Any(c => c.parameter == OnParam)) continue;
                    if (!writable(t)) { skipped++; continue; }
                    if (wp == null) wp = Param(ac, OnParam, AnimatorControllerParameterType.Float);
                    t.conditions = t.conditions.Concat(new[] { Cond(wp, false) }).ToArray();
                    EditorUtility.SetDirty(t); n++;
                }
                if (n > before) layers.Add($"{layer.name} {n - before}");
            }
            if (n > 0) EditorUtility.SetDirty(ac);
            log.AppendLine($"afk: {label} 아바타 AFK 진입 전이 {n}개에 {OnParam} < 0.5" + (layers.Count > 0 ? " — " + string.Join(", ", layers) : "") + (skipped > 0 ? $" (원본이라 건너뜀 {skipped}개)" : ""));
        }

        static IEnumerable<AnimatorTransitionBase> AllTransitions(AnimatorStateMachine sm)
        {
            foreach (var t in sm.anyStateTransitions) yield return t;
            foreach (var t in sm.entryTransitions) yield return t;
            foreach (var cs in sm.states) foreach (var t in cs.state.transitions) yield return t;
            foreach (var c in sm.stateMachines)
            {
                foreach (var t in sm.GetStateMachineTransitions(c.stateMachine)) yield return t;
                foreach (var t in AllTransitions(c.stateMachine)) yield return t;
            }
        }
    }
}
#endif
