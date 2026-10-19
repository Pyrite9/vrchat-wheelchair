// Pyrite9 Wheelchair — 앉은 모습 미리보기(인스펙터 토글)
//   아바타 사본에 실제 빌드(NDMF ProcessAvatar — 맞춤·몸 올림·보정 값 전부)를 돌리고, 휠체어를 켜고 데스크톱 앉은 자세로 굳혀
//   원본 자리에 보여 준다. 원본은 Scene 뷰에서만 숨김(SceneVisibilityManager — 씬 파일에 안 남음).
//   🔴 ProcessAvatar는 사본이 있는 씬을 '수정됨'으로 만든다 → 빈 임시 씬에서 빌드한 뒤 원래 씬으로 옮기고 임시 씬을 닫는다
//      (DontSave 오브젝트를 옮기는 것은 씬을 더럽히지 않음, 2026-10-19 확인)
//   🔴 빌드 직후엔 Animator가 아직 묶이지 않아 자세가 안 먹는다(인스펙터 버튼으로 누르면 delayCall 한 번으론 모자람 — 관리자 10-19
//      "버튼 눌러도 안 올라옴": 사본이 선 채·휠체어 꺼진 채로 남음) → Rebind 후 평가, 골반이 안 움직였으면 몇 프레임 뒤 다시(최대 MaxTries)
//   자세: FX(휠체어 켜기 등 오브젝트 켜짐)와 Action(몸)을 따로 평가 — FX를 휴머노이드 그래프에 섞으면 자세를 덮는다.
//   사본에선 VRC Avatar Descriptor·Pipeline Manager를 지워 SDK 업로드 목록에 안 뜨게, Animator를 꺼서 자세 유지.
//   스크립트 다시 읽기·Play·씬 닫기·Unity 종료 때 자동으로 끈다.
#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace Pyrite9.Wheelchair
{
    [InitializeOnLoad]
    public static class WcSitPreview
    {
        const string Suffix = " (Wheelchair Preview)";
        const float SettleSeconds = 2.5f;   // 상태 전이·블렌드가 끝날 만큼
        const int MaxTries = 10;

        static void Later(int ticks, System.Action a)
        {
            EditorApplication.CallbackFunction f = null;
            f = () => { if (--ticks > 0) { EditorApplication.QueuePlayerLoopUpdate(); return; } EditorApplication.update -= f; a(); };
            EditorApplication.update += f;
            EditorApplication.QueuePlayerLoopUpdate();
        }

        static GameObject clone, original;
        static string settingsHash;
        public static event System.Action Changed;

        public static bool Busy { get; private set; }
        public static bool IsActiveFor(GameObject avatar) => clone != null && original == avatar;
        public static bool Stale(WheelchairRoot r) => clone != null && settingsHash != Hash(r);
        public static string LastError { get; private set; }

        static WcSitPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) Stop(); };
            EditorSceneManager.sceneClosing += (s, removing) => { if (original != null && original.scene == s) Stop(); };
            EditorApplication.wantsToQuit += () => { Stop(); return true; };
            // 다시 읽기 전에 못 지운 사본(이름 끝 + DontSave)이 남아 있으면 정리
            EditorApplication.delayCall += () =>
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                    if (go != null && go.transform.parent == null && go.name.EndsWith(Suffix) && (go.hideFlags & HideFlags.DontSave) != 0 && go.scene.IsValid())
                        Object.DestroyImmediate(go);
            };
        }

        static string Hash(WheelchairRoot r) => r == null ? "" :
            $"{r.autoFit}|{r.seatWidthOffset}|{r.seatHeightOffset}|{r.footHeightOffset}|{r.footForwardOffset}|{r.menuLanguage}";

        public static void Start(WheelchairRoot root)
        {
            Stop();
            LastError = null;
            var desc = root != null ? root.GetComponentInParent<VRCAvatarDescriptor>(true) : null;
            if (desc == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var src = desc.gameObject;
            var home = src.scene;
            Busy = true;
            Changed?.Invoke();
            Scene tmp = default;
            GameObject c = null;
            try
            {
                EditorUtility.DisplayProgressBar("Pyrite9 Wheelchair", "Building the sitting preview…", 0.4f);
                var active = SceneManager.GetActiveScene();
                tmp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(active);
                c = Object.Instantiate(src, src.transform.position, src.transform.rotation);
                c.transform.localScale = src.transform.lossyScale;
                c.name = src.name;   // 빌드 기록(WcBuildRecord)이 원본 이름으로 남게
                SceneManager.MoveGameObjectToScene(c, tmp);
                nadena.dev.ndmf.AvatarProcessor.ProcessAvatar(c);
                foreach (var t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
                SceneManager.MoveGameObjectToScene(c, home);
                c.name = src.name + Suffix;
                clone = c; original = src; settingsHash = Hash(root);
                SceneVisibilityManager.instance.DisablePicking(c, true);
                SceneVisibilityManager.instance.Hide(src, true);
                var cc = c;
                Later(3, () => Pose(cc, 1));
            }
            catch (System.Exception e)
            {
                LastError = e.Message;
                Debug.LogException(e);
                if (c != null) Object.DestroyImmediate(c);
                clone = null; original = null; Busy = false;
            }
            finally
            {
                if (tmp.IsValid()) EditorSceneManager.CloseScene(tmp, true);
                EditorUtility.ClearProgressBar();
                if (clone == null) Changed?.Invoke();
            }
        }

        static void Pose(GameObject c, int attempt)
        {
            bool retry = false;
            try
            {
                if (c == null || c != clone) return;
                var d = c.GetComponent<VRCAvatarDescriptor>();
                var anim = c.GetComponent<Animator>();
                if (d == null || anim == null) return;
                anim.enabled = true;
                anim.Rebind();
                var hips = anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
                var hipsRest = hips != null ? hips.position : Vector3.zero;
                var all = c.GetComponentsInChildren<Transform>(true);
                var hipsPosed = hipsRest;
                bool[] act = null; Vector3[] lp = null; Quaternion[] lr = null; Vector3[] ls = null;
                foreach (var type in new[] { VRCAvatarDescriptor.AnimLayerType.FX, VRCAvatarDescriptor.AnimLayerType.Action })
                {
                    var ctrl = d.baseAnimationLayers.FirstOrDefault(l => l.type == type).animatorController;
                    if (ctrl == null) continue;
                    var graph = PlayableGraph.Create("Pyrite9 Wheelchair Preview");
                    try
                    {
                        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        var p = AnimatorControllerPlayable.Create(graph, ctrl);
                        var o = AnimationPlayableOutput.Create(graph, "out", anim);
                        o.SetSourcePlayable(p);
                        var ac = ctrl as UnityEditor.Animations.AnimatorController;
                        var prm = ac != null ? ac.parameters.FirstOrDefault(x => x.name == WheelchairAfk.OnParam) : null;
                        if (prm == null || prm.type == AnimatorControllerParameterType.Float) p.SetFloat(WheelchairAfk.OnParam, 1f);
                        else if (prm.type == AnimatorControllerParameterType.Bool) p.SetBool(prm.name, true);
                        else if (prm.type == AnimatorControllerParameterType.Int) p.SetInteger(prm.name, 1);
                        for (int i = 0; i < SettleSeconds * 60f; i++) graph.Evaluate(1f / 60f);
                        if (type == VRCAvatarDescriptor.AnimLayerType.FX) act = all.Select(t => t.gameObject.activeSelf).ToArray();
                        else { lp = all.Select(t => t.localPosition).ToArray(); lr = all.Select(t => t.localRotation).ToArray(); ls = all.Select(t => t.localScale).ToArray(); if (hips != null) hipsPosed = hips.position; }
                    }
                    finally { graph.Destroy(); }
                }
                // 골반이 그대로면 아직 안 묶인 것 — Transform을 건드리지 말고 몇 프레임 뒤 다시
                bool moved = lp != null && Vector3.Distance(hipsPosed, hipsRest) > 0.005f;
                if (!moved && hips != null)
                {
                    if (attempt < MaxTries) { retry = true; Later(5, () => Pose(c, attempt + 1)); return; }
                    LastError = "Could not pose the preview (the animator did not respond). Try Refresh.";
                }
                for (int i = 0; i < all.Length; i++)
                {
                    if (lp != null) { all[i].localPosition = lp[i]; all[i].localRotation = lr[i]; all[i].localScale = ls[i]; }
                    if (act != null) all[i].gameObject.SetActive(act[i]);
                }
                foreach (var a in c.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                // SDK 업로드 목록에 안 뜨게(자세를 굳힌 뒤엔 필요 없음)
                foreach (var pm in c.GetComponents<Component>().Where(x => x != null && x.GetType().Name == "PipelineManager").ToArray()) Object.DestroyImmediate(pm);
                Object.DestroyImmediate(d);
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
            }
            catch (System.Exception e) { LastError = e.Message; Debug.LogException(e); }
            finally { if (!retry) { Busy = false; Changed?.Invoke(); } }
        }

        public static void Stop()
        {
            bool had = clone != null || original != null;
            if (clone != null) Object.DestroyImmediate(clone);
            if (original != null) SceneVisibilityManager.instance.Show(original, true);
            clone = null; original = null; settingsHash = null; Busy = false;
            if (had) { SceneView.RepaintAll(); Changed?.Invoke(); }
        }
    }
}
#endif
