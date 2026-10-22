// Pyrite9 Wheelchair — 빌드 때 아바타마다 다른 부분을 채운다 (원본 에셋은 건드리지 않는다)
//   휠체어 찾기: WheelchairRoot 마커(이름·위치 자유). 마커가 없는 아바타는 아무것도 안 한다.
//   Resolving, MA 전
//     gesture : 'Anim Action Gesture'(MA Merge Animator, Action)에 그 아바타의 Gesture 컨트롤러(기본값이면 VRChat 기본 핸드 레이어)
//               — Action이 켜져도 손가락이 제스처를 따르게
//     labels  : 메뉴 라벨을 WheelchairRoot.menuLanguage로(WcMenuLabels)
//     fitA    : 배율·위치·WC_HipRef·Seat·풀 트래킹 센서(WheelchairFit.FitBase)
//     paths   : 상대 경로 Merge Animator의 기준(relativePathRoot)을 휠체어 루트로
//   Transforming, MA 후·AAO 전
//     fbt·spin : 배율에 맞춘 풀 트래킹 뒤로 빼기 양·바퀴 회전 속도
//     follow  : WC_FollowHips를 골반 밑(Bone Proxy 결과)에서 아바타 루트 밑으로 — 휠체어가 수평을 유지하고 Y 회전만 하게
//     fitB    : 의상이 합쳐진 뒤 좌석 폭·발판 조절 본, 좌석 관통 측정(WheelchairFit.FitParts)
//     seat    : 엉덩이가 좌석을 뚫으면 몸 올림 — 앉기 클립 RootT.y·골반 추종 오프셋·센서(WheelchairFit.ApplySeatRaise)
//     height  : VR 앉은 자세를 머리 높이만큼 올리는 Contact 3개 + Action 블렌드 트리 값(WheelchairFit.SetHeadHeight, 몸 올림 반영)
//     hip     : VRChat 풀 바디 트래킹 일반 모드 — Action 트래커 상태의 앉기를 골반 높이만큼 올리는 Contact 3개 + 블렌드 트리 값(WheelchairFit.SetHipHeight, 몸 올림 반영)
//     scale   : 아바타 크기와 상관없이 — 높이 Receiver Scaler 크기를 ScaleFactorInverse에 맞추는 FX 레이어(높이 트리는 height·hip이 ScaleFactorInverse 트리로 감쌈)
//     afk     : 앉아 있는 동안 AFK — 'Wheelchair Sit'에 모드별 'AFK Wait'(Hold 루프의 트래킹 재적용 멈춤)·'AFK Return'(풀리면 2.3초 뒤 'On <모드>' 재진입),
//               아바타 자체 AFK(Action·FX 레이어의 AFK 진입 전이)는 휠체어가 켜져 있으면 안 들어가게,
//               휠체어 Action 레이어의 빈 상태는 WD on(휠체어를 끄면 아바타 자체 AFK·이모트를 덮지 않게),
//               AFK 모션 모듈이 있으면 인스펙터 'AFK ▸ 사라지기(디졸브/즉시)'를 FX 파라미터 기본값으로(WheelchairAfk)
//     afkmotion : 앉은 채 AFK 모션(인스펙터 'AFK ▸ 모션', 기본 자동) — 이 아바타를 재서 일어나 기지개 켜고 걸어가 사라지는 클립을 굽고(WcAfkMotion,
//               표정 키는 Descriptor에서 WcAfkFaceMap) Action·FX에 레이어를 붙인다(WcAfkLayers). 끔이거나 이미 AFK 모듈이 있으면 건너뜀
//     marker  : 마커 제거
//   로그 Logs/Pyrite9_Wheelchair_build.txt — 프로젝트에 Logs/가 있을 때만. WARN 줄은 받는 사람 콘솔에도 경고로 뜨므로 영어로 쓴다(EXCEPTION은 LogException)
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using System.Text;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(Pyrite9.Wheelchair.WheelchairPlugin))]

namespace Pyrite9.Wheelchair
{
    public class WheelchairPlugin : Plugin<WheelchairPlugin>
    {
        public override string QualifiedName => "pyrite9.wheelchair";
        public override string DisplayName => "Pyrite9 Wheelchair";

        const string LogPath = "Logs/Pyrite9_Wheelchair_build.txt";
        static readonly StringBuilder Log = new StringBuilder();
        static bool heightScaled;   // scale 패스가 붙었나 → 빌드 기록

        protected override void Configure()
        {
            InPhase(BuildPhase.Resolving)
                .BeforePlugin("nadena.dev.modular-avatar")
                .Run("Merge avatar Gesture into Action", ctx => Guard(ctx, "gesture", FillGesture))
                .Then.Run("Menu labels", ctx => Guard(ctx, "labels", SetMenuLabels))
                .Then.Run("Fit size, position and sensors", ctx => Guard(ctx, "fitA",
                    c => WheelchairFit.FitBase(c.AvatarRootTransform, FindHolder(c.AvatarRootTransform), Log)))
                .Then.Run("Relative path roots", ctx => Guard(ctx, "paths", FillRelativeRoots));   // fitA가 루트를 옮긴 뒤
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                // Light Limit Changer가 색을 텍스처에 굽고(NormalizeMaterials) 밝기 값을 쓴 뒤에 — AFK 페이드 사본이 그 머티리얼을 복사해야 색이 안 바뀐다(9-61)
                .AfterPlugin("io.github.azukimochi.light-limit-changer")
                .BeforePlugin("com.anatawa12.avatar-optimizer")
                .Run("Scale full-body push-back", ctx => Guard(ctx, "fbt", ScaleFbt))
                .Then.Run("Wheel spin speed", ctx => Guard(ctx, "spin", SpinSpeed))
                .Then.Run("Level hip follower", ctx => Guard(ctx, "follow", LevelFollowHips))
                .Then.Run("Fit seat width and footrests", ctx => Guard(ctx, "fitB",
                    c => WheelchairFit.FitParts(c.AvatarRootTransform, FindHolder(c.AvatarRootTransform), Log)))
                .Then.Run("Raise the body above the seat", ctx => Guard(ctx, "seat", SeatRaise))
                .Then.Run("VR sit height follows head", ctx => Guard(ctx, "height", HeadHeight))
                .Then.Run("Full-body sit height follows hips", ctx => Guard(ctx, "hip", HipHeight))
                .Then.Run("Height follow works at any avatar scale", ctx => Guard(ctx, "scale", HeightScale))
                .Then.Run("AFK while seated", ctx => Guard(ctx, "afk", Afk))
                .Then.Run("AFK motion", ctx => Guard(ctx, "afkmotion", AfkMotionPass))
                .Then.Run("Remove marker", ctx => Guard(ctx, "marker", RemoveMarkers));
        }

        public static Transform FindHolder(Transform root)
        {
            var marks = root.GetComponentsInChildren<WheelchairRoot>(true);
            if (marks.Length > 1) Log.AppendLine($"WARN {marks.Length} WheelchairRoot components found — only the first one is used: " + AnimationUtility.CalculateTransformPath(marks[0].transform, root));
            return marks.Length > 0 ? marks[0].transform : null;
        }

        static void Guard(BuildContext ctx, string step, System.Action<BuildContext> body)
        {
            if (step == "gesture")
            {
                Log.Clear(); Log.AppendLine($"=== Pyrite9 Wheelchair {WcConst.Version} NDMF {System.DateTime.Now:HH:mm:ss} — {ctx.AvatarRootObject.name} ===");
                heightScaled = false; WheelchairFit.ForgetBuild(ctx.AvatarRootTransform);
            }
            bool found = false;
            try
            {
                if (FindHolder(ctx.AvatarRootTransform) == null) { Log.AppendLine(step + ": 휠체어 없음 — 건너뜀"); return; }
                found = true;
                body(ctx);
            }
            catch (System.Exception e) { Log.AppendLine(step + " EXCEPTION: " + e); Debug.LogException(e); }
            finally
            {
                if (step == "marker" && found)   // 마지막 패스: 경고를 콘솔로(받는 쪽엔 로그 파일이 없을 수 있다) + 인스펙터 '마지막 빌드' 기록
                {
                    var warns = Log.ToString().Split('\n').Where(l => l.Contains("WARN")).Select(l => l.Trim()).ToList();
                    foreach (var line in warns) Debug.LogWarning("[Pyrite9 Wheelchair] " + ctx.AvatarRootObject.name + ": " + line);
                    WriteRecord(ctx, warns);
                }
                if (Directory.Exists("Logs")) { try { File.WriteAllText(LogPath, Log.ToString(), new UTF8Encoding(false)); } catch { } }
            }
        }

        static void WriteRecord(BuildContext ctx, System.Collections.Generic.List<string> warns)
        {
            var root = ctx.AvatarRootTransform;
            bool fitted = WheelchairFit.TryGetScale(root, out var s);
            WheelchairFit.TryGetRaise(root, out var r);
            WcBuildRecord.Put(new WcBuildRecord.Entry
            {
                avatar = ctx.AvatarRootObject.name, time = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), version = WcConst.Version,
                fitted = fitted, scale = s, raise = r, heightScale = heightScaled, warnings = warns,
            });
        }

        // 메뉴 라벨: 프리팹 안 Menu Item 오브젝트 이름 → WcMenuLabels 표에서 마커 언어로
        static void SetMenuLabels(BuildContext ctx)
        {
            var holder = FindHolder(ctx.AvatarRootTransform);
            var mark = holder.GetComponent<WheelchairRoot>();
            if (mark == null) { Log.AppendLine("labels: 마커 없음 — 그대로"); return; }
            var tItem = MA("ModularAvatarMenuItem");
            int n = 0;
            foreach (var item in holder.GetComponentsInChildren(tItem, true))
            {
                var label = WcMenuLabels.Get(item.gameObject.name, mark.menuLanguage);
                if (label == null) continue;
                var so = new SerializedObject(item);
                so.FindProperty("label").stringValue = label;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            Log.AppendLine($"labels: {mark.menuLanguage} — {n}개");
        }

        static System.Type MA(string n) => System.AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("nadena.dev.modular_avatar.core." + n)).FirstOrDefault(x => x != null);

        // ---------------------------------------------------------------- gesture
        static void FillGesture(BuildContext ctx)
        {
            var node = FindHolder(ctx.AvatarRootTransform).Find(WcConst.GestureNodeName);
            var tMerge = MA("ModularAvatarMergeAnimator");
            var comp = node != null && tMerge != null ? node.GetComponent(tMerge) : null;
            if (comp == null) { Log.AppendLine("gesture: 'Anim Action Gesture' 없음 — 건너뜀"); return; }

            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var g = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Gesture);
            RuntimeAnimatorController ctrl = (!g.isDefault && g.animatorController != null) ? g.animatorController : null;
            string from = ctrl != null ? "아바타 Gesture" : "VRChat 기본 핸드 레이어";
            if (ctrl == null)
            {
                var guid = AssetDatabase.FindAssets("vrc_AvatarV3HandsLayer t:AnimatorController").FirstOrDefault();
                if (guid != null) ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            }
            var so = new SerializedObject(comp);
            var cur = so.FindProperty("animator").objectReferenceValue;
            if (ctrl == null)
            {
                Log.AppendLine("gesture: WARN Gesture controller not found — fingers may stop following gestures while seated");
                Object.DestroyImmediate(comp);
                return;
            }
            var masked = MaskLikeGesturePlayable(ctx, ctrl, g);
            so.FindProperty("animator").objectReferenceValue = masked;
            so.ApplyModifiedPropertiesWithoutUndo();
            Log.AppendLine($"gesture: {(cur ? cur.name : "(비어 있음)")} → {ctrl.name} ({from})");
        }

        // Gesture 레이어에선 VRChat이 레이어 전체에 마스크(설명자 Gesture 마스크 = 보통 첫 레이어의 손 마스크)를 씌우지만,
        //   Action으로 합치면 그 보호가 없어져 마스크 없는 레이어(예: 웅크림·엎드림 손 레이어)가 앉은 다리까지 덮는다(9-42 발견).
        //   → 빌드용 사본을 만들어 레이어마다 '레이어 마스크 ∩ 전체 마스크'(마스크 없으면 전체 마스크)를 씌운다. 원본은 안 건드림
        static RuntimeAnimatorController MaskLikeGesturePlayable(BuildContext ctx, RuntimeAnimatorController ctrl, VRCAvatarDescriptor.CustomAnimLayer g)
        {
            if (!(ctrl is AnimatorController src)) { Log.AppendLine("gesture: Override 컨트롤러 — 마스크 보정 건너뜀"); return ctrl; }
            var srcLayers = src.layers;
            AvatarMask play = !g.isDefault && g.mask != null ? g.mask : (srcLayers.Length > 0 ? srcLayers[0].avatarMask : null);
            if (play == null)
            {
                var id = AssetDatabase.FindAssets("vrc_HandsOnly t:AvatarMask").FirstOrDefault();
                if (id != null) play = AssetDatabase.LoadAssetAtPath<AvatarMask>(AssetDatabase.GUIDToAssetPath(id));
            }
            if (play == null) { Log.AppendLine("gesture: WARN no hand mask found for the gesture layers — a gesture layer may override the sitting pose"); return ctrl; }

            var copy = Object.Instantiate(src);   // 레이어 목록만 사본(상태 기계는 원본 공유 — 건드리지 않음)
            copy.name = src.name;
            var ls = copy.layers;
            int setN = 0, cutN = 0;
            for (int i = 0; i < ls.Length; i++)
            {
                var m = ls[i].avatarMask;
                if (m == null) { ls[i].avatarMask = play; setN++; continue; }
                bool extra = false;
                for (int p = 0; p < (int)AvatarMaskBodyPart.LastBodyPart; p++)
                    if (m.GetHumanoidBodyPartActive((AvatarMaskBodyPart)p) && !play.GetHumanoidBodyPartActive((AvatarMaskBodyPart)p)) extra = true;
                if (!extra) continue;
                var cut = new AvatarMask { name = m.name + " & " + play.name };
                for (int p = 0; p < (int)AvatarMaskBodyPart.LastBodyPart; p++)
                    cut.SetHumanoidBodyPartActive((AvatarMaskBodyPart)p, m.GetHumanoidBodyPartActive((AvatarMaskBodyPart)p) && play.GetHumanoidBodyPartActive((AvatarMaskBodyPart)p));
                cut.transformCount = m.transformCount;
                for (int t = 0; t < m.transformCount; t++) { cut.SetTransformPath(t, m.GetTransformPath(t)); cut.SetTransformActive(t, m.GetTransformActive(t)); }
                ctx.AssetSaver.SaveAsset(cut);
                ls[i].avatarMask = cut; cutN++;
            }
            if (setN == 0 && cutN == 0) { Object.DestroyImmediate(copy); Log.AppendLine($"gesture: 레이어 마스크 그대로({play.name} 안)"); return ctrl; }
            copy.layers = ls;
            ctx.AssetSaver.SaveAsset(copy);
            Log.AppendLine($"gesture: 레이어 마스크 보정 — 없음→{play.name} {setN}개, 겹치는 부위만 {cutN}개 (빌드 사본)");
            return copy;
        }

        // ---------------------------------------------------------------- 상대 경로 기준
        // MA AvatarObjectReference는 referencePath가 비면 targetObject도 안 본다 → 받는 쪽에서 어디에 두든 실제 경로로 채운다.
        static void FillRelativeRoots(BuildContext ctx)
        {
            var root = ctx.AvatarRootTransform;
            var holder = FindHolder(root);
            var tMerge = MA("ModularAvatarMergeAnimator");
            if (tMerge == null) return;
            string path = AnimationUtility.CalculateTransformPath(holder, root);
            int n = 0;
            foreach (var comp in holder.GetComponentsInChildren(tMerge, true))
            {
                var so = new SerializedObject(comp);
                if (so.FindProperty("pathMode").intValue != 0) continue;   // 0 = Relative
                so.FindProperty("relativePathRoot.referencePath").stringValue = path;
                so.FindProperty("relativePathRoot.targetObject").objectReferenceValue = holder.gameObject;
                so.ApplyModifiedPropertiesWithoutUndo();
                n++;
            }
            Log.AppendLine($"paths: 상대 경로 Merge Animator {n}개 기준 → '{path}'");
        }

        static void RemoveMarkers(BuildContext ctx)
        {
            var marks = ctx.AvatarRootTransform.GetComponentsInChildren<WheelchairRoot>(true);
            foreach (var m in marks) Object.DestroyImmediate(m);
            Log.AppendLine($"marker: WheelchairRoot {marks.Length}개 제거");
        }

        // ---------------------------------------------------------------- 풀 트래킹 오프셋 배율 (WheelchairFit.ScaleFbtOffsets)
        static void ScaleFbt(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var fx = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            if (fx == null || !ctx.IsTemporaryAsset(fx)) { Log.AppendLine("fbt: FX 없음 또는 빌드용 복제본 아님 — 건너뜀"); return; }
            int n = WheelchairFit.ScaleFbtOffsets(fx, ctx.AvatarRootTransform, FindHolder(ctx.AvatarRootTransform), clip =>
            {
                if (ctx.IsTemporaryAsset(clip)) return clip;
                var copy = Object.Instantiate(clip); copy.name = clip.name;
                ctx.AssetSaver.SaveAsset(copy);
                return copy;
            }, Log);
            Log.AppendLine($"fbt: 커브 {n}개");
        }

        static void SpinSpeed(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var fx = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            if (fx == null || !ctx.IsTemporaryAsset(fx)) { Log.AppendLine("spin: FX 없음 또는 빌드용 복제본 아님 — 건너뜀"); return; }
            WheelchairFit.SetSpinSpeed(fx, ctx.AvatarRootTransform, FindHolder(ctx.AvatarRootTransform), Log);
        }

        // 좌석 관통 시 몸 올림(fitB가 잰 값): 빌드 사본 Action의 앉기 클립·골반 추종 오프셋·센서 (WheelchairFit.ApplySeatRaise)
        static void SeatRaise(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var act = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action).animatorController as AnimatorController;
            if (act == null || !ctx.IsTemporaryAsset(act)) { Log.AppendLine("seat: Action 없음 또는 빌드용 복제본 아님 — 건너뜀"); return; }
            WheelchairFit.ApplySeatRaise(act, ctx.AvatarRootTransform, FindHolder(ctx.AvatarRootTransform), clip =>
            {
                if (ctx.IsTemporaryAsset(clip)) return clip;
                var copy = Object.Instantiate(clip); copy.name = clip.name;
                ctx.AssetSaver.SaveAsset(copy);
                return copy;
            }, Log);
        }

        // VR 앉은 자세를 머리 높이만큼(9-26): Contact 2개 추가 + 빌드 사본 Action의 블렌드 트리 문턱값·올린 클립 (WheelchairFit.SetHeadHeight)
        static void HeadHeight(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var act = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action).animatorController as AnimatorController;
            if (act == null || !ctx.IsTemporaryAsset(act)) { Log.AppendLine("height: Action 없음 또는 빌드용 복제본 아님 — 건너뜀"); return; }
            WheelchairFit.SetHeadHeight(act, ctx.AvatarRootTransform, FindHolder(ctx.AvatarRootTransform), clip => NewClipCopy(ctx, clip), o => SaveNew(ctx, o), Log);
        }

        // VRChat 풀 바디 트래킹 일반 모드(9-41): Contact 3개 추가 + 빌드 사본 Action 트래커 상태의 블렌드 트리 문턱값·올린/내린 클립 (WheelchairFit.SetHipHeight)
        static void HipHeight(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var act = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action).animatorController as AnimatorController;
            if (act == null || !ctx.IsTemporaryAsset(act)) { Log.AppendLine("hip: Action 없음 또는 빌드용 복제본 아님 — 건너뜀"); return; }
            WheelchairFit.SetHipHeight(act, ctx.AvatarRootTransform, FindHolder(ctx.AvatarRootTransform), clip => NewClipCopy(ctx, clip), o => SaveNew(ctx, o), Log);
        }

        // 앉아 있는 동안 AFK(WheelchairAfk): 빌드 사본 Action의 Hold 루프 멈춤·복귀, Action·FX의 아바타 자체 AFK 막기
        static void Afk(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var act = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action).animatorController as AnimatorController;
            var fx = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            if (act == null || !ctx.IsTemporaryAsset(act)) { Log.AppendLine("afk: Action 없음 또는 빌드용 복제본 아님 — 건너뜀"); return; }
            // 원본 에셋 보호: 빌드 컨테이너 밖의 저장된 오브젝트(전이)는 고치지 않는다
            System.Func<Object, bool> writable = o => !EditorUtility.IsPersistent(o) || ctx.IsTemporaryAsset(o);
            WheelchairAfk.PauseHoldLoop(act, writable, Log);
            WheelchairAfk.PassThroughEmptyStates(act, writable, Log);
            WheelchairAfk.BlockAvatarAfk(act, "Action", writable, Log);
            if (fx != null && ctx.IsTemporaryAsset(fx))
            {
                WheelchairAfk.BlockAvatarAfk(fx, "FX", writable, Log);
                var mark = FindHolder(ctx.AvatarRootTransform).GetComponent<WheelchairRoot>();
                WheelchairAfk.SetVanish(fx, mark != null && mark.afkVanish == AfkVanish.Instant, Log);
            }
            else Log.AppendLine("afk: FX 없음 또는 빌드용 복제본 아님 — FX 쪽 AFK 막기 건너뜀");
        }

        // 앉은 채 AFK 모션(9-56): 굽기 → Action·FX 레이어. 실패해도 빌드는 계속(WARN)
        static void AfkMotionPass(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var root = ctx.AvatarRootTransform;
            var holder = FindHolder(root);
            var mark = holder.GetComponent<WheelchairRoot>();
            var setting = mark != null ? mark.afkMotion : AfkMotion.Auto;
            if (setting == AfkMotion.Off) { Log.AppendLine("afkmotion: 끔(인스펙터) — 건너뜀"); return; }
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var act = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action).animatorController as AnimatorController;
            var fx = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            if (act == null || !ctx.IsTemporaryAsset(act) || fx == null || !ctx.IsTemporaryAsset(fx)) { Log.AppendLine("afkmotion: WARN Action/FX controller not found — AFK motion skipped"); return; }
            if (WcAfkLayers.HasOtherAfk(act)) { Log.AppendLine("afkmotion: 아바타에 AFK 모션 모듈이 이미 있음(Wheelchair AFK·WheelchairPlus AFK 레이어) — 건너뜀"); return; }
            var anim = root.GetComponent<Animator>();
            if (anim == null || !anim.isHuman) { Log.AppendLine("afkmotion: WARN the avatar is not humanoid — AFK motion skipped"); return; }
            var t0 = System.DateTime.Now;
            try
            {
                var faces = WcAfkFaceMap.From(desc, root, Log);
                var res = WcAfkMotion.Bake(root, setting, faces, o => SaveNew(ctx, o), Log);
                WcAfkLayers.AddAction(act, res, res.Face != null, o => SaveNew(ctx, o), Log);
                WcAfkLayers.AddFx(fx, root, holder, res, mark != null ? mark.afkVanish : AfkVanish.Dissolve, o => SaveNew(ctx, o), Log);
                WcAfkBakeRecord.Put(new WcAfkBakeRecord.Entry
                {
                    avatar = ctx.AvatarRootObject.name, time = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    setting = (int)setting, motion = (int)res.Motion, oneArmGap = res.OneArmGap, gap = res.HeadGap,
                });
                Log.AppendLine($"afkmotion: 끝 — {(System.DateTime.Now - t0).TotalSeconds:F1}초");
            }
            catch (System.Exception e)
            {
                Log.AppendLine("afkmotion: WARN AFK motion could not be made — the avatar just stays seated while AFK (" + e.Message + ")");
                Log.AppendLine("afkmotion: " + e);
            }
        }

        // 크기 무관(9-53): FX 레이어가 높이 Receiver의 Scaler 크기를 ScaleFactorInverse에 맞춘다 (WheelchairFit.AddHeightScaleLayer)
        static void HeightScale(BuildContext ctx)
        {
            ctx.DeactivateExtensionContext<AnimatorServicesContext>();
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            var fx = desc.baseAnimationLayers.FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            if (fx == null || !ctx.IsTemporaryAsset(fx)) { Log.AppendLine("scale: WARN FX controller not found — height follow may break when the avatar is resized"); return; }
            heightScaled = WheelchairFit.AddHeightScaleLayer(fx,ctx.AvatarRootTransform, o => SaveNew(ctx, o), Log);
        }

        // 빌드 때 새로 만든 오브젝트(블렌드 트리·클립·상태 기계)를 빌드 컨테이너에 저장 — 이미 저장된 것은 그대로
        static void SaveNew(BuildContext ctx, Object o)
        {
            if (o != null && !EditorUtility.IsPersistent(o)) ctx.AssetSaver.SaveAsset(o);
        }

        // 올린/내린 앉기 클립용: 원본이 임시든 아니든 항상 새 사본을 만들어 빌드 컨테이너에 저장
        static AnimationClip NewClipCopy(BuildContext ctx, AnimationClip clip)
        {
            var copy = Object.Instantiate(clip); copy.name = clip.name;
            ctx.AssetSaver.SaveAsset(copy);
            return copy;
        }

        // ---------------------------------------------------------------- follow
        // VRC constraint의 끈 축(회전 X·Z)은 부모 기준 local 값으로 채워진다. Bone Proxy가 WC_HipRef를 골반 밑으로 옮기면
        // 그 자식 WC_FollowHips가 골반 기울기를 물려받아 '회전은 Y만'이 안 먹는다(2026-10-17 Play 실측) → 수평인 아바타 루트 밑으로.
        static void LevelFollowHips(BuildContext ctx)
        {
            var root = ctx.AvatarRootTransform;
            var follow = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == WcConst.FollowName);
            if (follow == null) { Log.AppendLine("follow: WC_FollowHips 없음 — 건너뜀"); return; }
            string before = AnimationUtility.CalculateTransformPath(follow, root);
            if (follow.parent == root) { Log.AppendLine("follow: 이미 루트 밑 — " + before); return; }
            follow.SetParent(root, true);
            string after = AnimationUtility.CalculateTransformPath(follow, root);
            // 밑에 있는 WC_FBTOffset(풀 트래킹 밀림, 1.0.1~)을 움직이는 커브도 따라 옮긴다 — 안 하면 경로가 끊겨 밀림이 안 된다
            int n = 0;
            var desc = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            foreach (var l in desc.baseAnimationLayers.Concat(desc.specialAnimationLayers))
            {
                var ac = l.animatorController as AnimatorController;
                if (ac == null || !ctx.IsTemporaryAsset(ac)) continue;
                n += WheelchairFit.RemapPaths(ac, before, after, clip =>
                {
                    if (ctx.IsTemporaryAsset(clip)) return clip;
                    var copy = Object.Instantiate(clip); copy.name = clip.name;
                    ctx.AssetSaver.SaveAsset(copy);
                    return copy;
                });
            }
            Log.AppendLine($"follow: {before} → {after} (local rot {follow.localEulerAngles}), 애니메이션 경로 {n}개 따라 옮김");
        }
    }
}
#endif
