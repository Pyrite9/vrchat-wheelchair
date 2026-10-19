// Pyrite9 Wheelchair — WheelchairRoot 인스펙터(UI Toolkit, 디자인 'Assay': 어두운 바탕 + 파이라이트 금색 포인트)
//   소개·기능(접기) · 앉은 모습 미리보기(WcSitPreview) · 01 기본(메뉴 언어·자동 맞춤) · 02 미세 조정(게이지 + 값, ±0.10 m 게이지는 끌어서도 바꿈, 두 번 누르면 0)
//   · AFK(모션 끔/자동/한쪽/V · 사라지기 디졸브/즉시) · 마지막 빌드(WcBuildRecord) · 변경 기록(접기 — 이 버전 한 줄, 글은 README 3종 '변경 기록' 절에서 읽음). 접힘 상태는 EditorPrefs(사용자별)
//   문구는 메뉴 언어를 따라간다. 칸 툴팁 앞머리에 README가 쓰는 영어 이름(Seat Height Offset 등)을 붙인다.
//   스타일 WheelchairInspector.uss(이 스크립트 옆) — 못 찾아도 동작은 같다(모양만 Unity 기본).
#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.Avatars.Components;

namespace Pyrite9.Wheelchair
{
    [CustomEditor(typeof(WheelchairRoot))]
    public class WheelchairRootEditor : Editor
    {
        static readonly Color Gold = new Color32(0xE2, 0xCE, 0x74, 0xFF);
        const float GaugeRange = 0.10f;   // m — 자동 올림 상한(10 cm)과 같은 폭

        VisualElement root;
        int lang = -1;
        long stamp = -1;
        bool stale;
        int secNo;
        string Num() => (++secNo).ToString("00");

        // ---------------------------------------------------------------- 문구 (English / Japanese / Korean = MenuLanguage 순서)
        static string T(int l, string en, string jp, string ko) => l == 1 ? jp : l == 2 ? ko : en;

        public override VisualElement CreateInspectorGUI()
        {
            root = new VisualElement();
            root.AddToClassList("wc-root");
            var script = MonoScript.FromScriptableObject(this);
            var dir = script != null ? Path.GetDirectoryName(AssetDatabase.GetAssetPath(script)) : null;
            var uss = dir != null ? AssetDatabase.LoadAssetAtPath<StyleSheet>(dir + "/WheelchairInspector.uss") : null;
            if (uss != null) root.styleSheets.Add(uss);
            Build();
            root.schedule.Execute(Poll).Every(300);
            System.Action onCheck = () => { if (root.panel != null) Build(); };
            WcUpdateCheck.Changed += onCheck;
            WcSitPreview.Changed += onCheck;
            root.RegisterCallback<DetachFromPanelEvent>(_ => { WcUpdateCheck.Changed -= onCheck; WcSitPreview.Changed -= onCheck; });
            WcUpdateCheck.CheckIfDue();
            return root;
        }

        void Poll()
        {
            if (target == null) return;
            serializedObject.UpdateIfRequiredOrScript();
            int l = serializedObject.FindProperty("menuLanguage").intValue;
            long s = WcBuildRecord.Stamp() ^ WcAfkBakeRecord.Stamp();
            bool st = WcSitPreview.Stale((WheelchairRoot)target);
            if (l != lang || s != stamp || st != stale) Build();
        }

        void Build()
        {
            serializedObject.Update();
            lang = serializedObject.FindProperty("menuLanguage").intValue;
            stamp = WcBuildRecord.Stamp() ^ WcAfkBakeRecord.Stamp();
            stale = WcSitPreview.Stale((WheelchairRoot)target);
            secNo = 0;
            root.Clear();
            root.Add(Header());
            var body = new VisualElement(); body.AddToClassList("wc-body");
            root.Add(body);
            body.Add(AboutSection());
            body.Add(PreviewBar());
            body.Add(Divider());
            body.Add(BasicSection());
            body.Add(Divider());
            body.Add(OffsetSection());
            body.Add(Divider());
            body.Add(AfkSection());
            body.Add(Divider());
            body.Add(BuildSection());
            body.Add(Divider());
            body.Add(ChangelogSection());
            root.Bind(serializedObject);
        }

        // ---------------------------------------------------------------- 머리
        VisualElement Header()
        {
            var h = new VisualElement(); h.AddToClassList("wc-header");
            var left = new VisualElement(); left.AddToClassList("wc-header__left");
            var glyph = new VisualElement(); glyph.AddToClassList("wc-glyph");
            glyph.generateVisualContent += DrawGlyph;
            left.Add(glyph);
            var titles = new VisualElement(); titles.AddToClassList("wc-header__titles");
            titles.Add(Label("PYRITE9", "wc-kicker"));
            titles.Add(Label("Wheelchair", "wc-title"));
            left.Add(titles);
            h.Add(left);
            h.Add(VersionBox());
            return h;
        }

        // 버전 + 새 버전 확인(WcUpdateCheck): 버전을 누르면 다시 확인, 새 버전 줄을 누르면 릴리스 페이지
        VisualElement VersionBox()
        {
            var box = new VisualElement(); box.AddToClassList("wc-versionbox");
            var ver = Label("v" + WcConst.Version, "wc-version");
            ver.tooltip = T(lang, "Click to check for updates", "クリックで更新を確認", "누르면 새 버전 확인");
            ver.RegisterCallback<ClickEvent>(_ => WcUpdateCheck.Check());
            box.Add(ver);
            Label st;
            switch (WcUpdateCheck.Current)
            {
                case WcUpdateCheck.State.Checking: st = Label(T(lang, "checking…", "確認中…", "확인 중…"), "wc-update"); break;
                case WcUpdateCheck.State.Latest: st = Label(T(lang, "up to date", "最新", "최신"), "wc-update"); break;
                case WcUpdateCheck.State.Failed: st = Label(T(lang, "couldn't check", "確認できません", "확인 못 함"), "wc-update"); break;
                case WcUpdateCheck.State.Newer:
                    st = Label(T(lang, WcUpdateCheck.LatestTag + " available", WcUpdateCheck.LatestTag + " があります", WcUpdateCheck.LatestTag + " 있음"), "wc-update");
                    st.AddToClassList("wc-update--new");
                    st.tooltip = WcUpdateCheck.LatestUrl;
                    st.RegisterCallback<ClickEvent>(_ => Application.OpenURL(WcUpdateCheck.LatestUrl));
                    break;
                default: st = null; break;
            }
            if (st != null) box.Add(st);
            return box;
        }

        // 휠체어 선 그림(34×34): 바퀴 두 겹 · 앞바퀴 · 등받이에서 발판까지 한 줄 · 손잡이
        static void DrawGlyph(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            p.strokeColor = Gold;
            p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
            void Circle(float x, float y, float r, float w)
            {
                p.lineWidth = w;
                p.BeginPath();
                p.Arc(new Vector2(x, y), r, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Stroke();
            }
            Circle(14f, 22f, 9f, 1.5f);
            Circle(14f, 22f, 6.5f, 0.8f);
            Circle(27f, 29f, 2.5f, 1.5f);
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(9f, 4f)); p.LineTo(new Vector2(11f, 14f)); p.LineTo(new Vector2(23f, 14f)); p.LineTo(new Vector2(27f, 26f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(new Vector2(7f, 4f)); p.LineTo(new Vector2(11f, 4f));
            p.Stroke();
        }

        // ---------------------------------------------------------------- 소개·기능
        // 인스펙터용 짧은 글(README '기능' 절은 길어서 따로). 기능이 바뀌면 README와 같이 고친다
        static readonly string[][] Features =
        {
            new[] { "Auto fit at build — measures the seated avatar for size, seat width, sitting height, footrests and sensors",
                    "ビルド時に自動フィット — 座った姿勢で測って、大きさ・座面の幅・座る高さ・足置き・センサーを合わせる",
                    "빌드 때 자동 맞춤 — 앉은 자세로 재서 크기·좌석 폭·앉는 높이·발판·센서를 맞춤" },
            new[] { "Desktop and VR sitting pose; fingers keep following your gestures",
                    "デスクトップ・VR の座り姿勢。指はジェスチャーのまま",
                    "데스크톱·VR 앉은 자세. 손가락은 제스처 그대로" },
            new[] { "Space Drag lifts you and the chair together, at any avatar size (up to about 2.5 m)",
                    "Space Drag で持ち上げると体と車椅子が一緒に（アバターの大きさに関係なく約 2.5 m まで）",
                    "Space Drag로 들어 올리면 몸과 휠체어가 같이(아바타 크기와 상관없이 약 2.5 m까지)" },
            new[] { "Full-body tracking — seated legs follow the hip tracker; in FBT mode the chair backs off when your back or shins touch it",
                    "フルトラ — 座った脚が腰トラッカーに付いてくる。フルトラモードでは背中・すねが当たると車椅子が下がる",
                    "풀 트래킹 — 앉은 다리가 골반 트래커를 따라옴. 풀 트래킹 모드에선 등·정강이가 닿으면 휠체어가 물러남" },
            new[] { "Rolling wheels and casters, rear seat for a passenger",
                    "後輪・キャスターが回る。後ろに人を乗せられる",
                    "뒷바퀴·캐스터가 굴러감. 뒷좌석에 다른 사람 태우기" },
            new[] { "AFK while seated — stand up, stretch, walk away and disappear; sit back down when you return (made for each avatar at build time)",
                    "座ったまま AFK — 立ち上がって伸びをし、歩いて消える。戻ると座り直す（ビルド時にアバターごとに作る）",
                    "앉은 채 AFK — 일어나 기지개 켜고 걸어가 사라짐, 돌아오면 다시 앉음(빌드 때 아바타마다 만듦)" },
        };

        VisualElement AboutSection()
        {
            var sec = new VisualElement(); sec.AddToClassList("wc-section");
            sec.Add(Label(T(lang, "Drop it into a humanoid avatar — it fits itself to that avatar when you build. Your scene is not changed.",
                "ヒューマノイドのアバターに入れるだけで、ビルド時にそのアバターに合わせます。シーンは変わりません。",
                "휴머노이드 아바타에 넣기만 하면 빌드할 때 그 아바타에 맞춰집니다. 씬은 바뀌지 않습니다."), "wc-intro"));
            var list = new VisualElement(); list.AddToClassList("wc-features");
            foreach (var f in Features)
            {
                var row = new VisualElement(); row.AddToClassList("wc-feature");
                var dot = new VisualElement(); dot.AddToClassList("wc-feature__dot"); row.Add(dot);
                row.Add(Label(f[Mathf.Clamp(lang, 0, 2)], "wc-feature__text"));
                list.Add(row);
            }
            sec.Add(Fold("Pyrite9WC.ShowFeatures", true, T(lang, "FEATURES", "機能", "기능"), list));
            return sec;
        }

        // 접는 칸: 머리(작은 제목 + 꺾쇠)를 누르면 펼침/접기, 상태는 EditorPrefs
        static VisualElement Fold(string key, bool defaultOpen, string title, VisualElement content)
        {
            var box = new VisualElement(); box.AddToClassList("wc-fold");
            var head = new VisualElement(); head.AddToClassList("wc-fold__head");
            head.Add(Label(title, "wc-fold__title"));
            var chev = new VisualElement(); chev.AddToClassList("wc-fold__chevron");
            bool open = EditorPrefs.GetBool(key, defaultOpen);
            chev.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.strokeColor = new Color32(0x7D, 0x83, 0x91, 0xFF); p.lineWidth = 1.5f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                p.BeginPath();
                if (open) { p.MoveTo(new Vector2(2f, 4f)); p.LineTo(new Vector2(5f, 7f)); p.LineTo(new Vector2(8f, 4f)); }
                else { p.MoveTo(new Vector2(4f, 2f)); p.LineTo(new Vector2(7f, 5f)); p.LineTo(new Vector2(4f, 8f)); }
                p.Stroke();
            };
            head.Add(chev);
            box.Add(head);
            box.Add(content);
            content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            head.RegisterCallback<ClickEvent>(_ =>
            {
                open = !open;
                EditorPrefs.SetBool(key, open);
                content.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                chev.MarkDirtyRepaint();
            });
            return box;
        }

        // ---------------------------------------------------------------- 앉은 모습 미리보기
        VisualElement PreviewBar()
        {
            var wr = (WheelchairRoot)target;
            var desc = wr.GetComponentInParent<VRCAvatarDescriptor>(true);
            bool on = desc != null && WcSitPreview.IsActiveFor(desc.gameObject);
            var bar = new VisualElement(); bar.AddToClassList("wc-preview");
            var row = new VisualElement(); row.AddToClassList("wc-preview__row");
            string label = WcSitPreview.Busy ? T(lang, "Building…", "作成中…", "만드는 중…")
                : on ? T(lang, "Sitting preview — on", "座った姿のプレビュー — オン", "앉은 모습 미리보기 — 켜짐")
                : T(lang, "Preview sitting", "座った姿をプレビュー", "앉은 모습 미리보기");
            var btn = new Button(() =>
            {
                if (on) WcSitPreview.Stop();
                else WcSitPreview.Start(wr);
            }) { text = label };
            btn.AddToClassList("wc-preview__toggle");
            if (on) btn.AddToClassList("wc-preview__toggle--on");
            btn.SetEnabled(desc != null && !WcSitPreview.Busy && !EditorApplication.isPlaying);
            row.Add(btn);
            if (on && stale)
            {
                var re = new Button(() => WcSitPreview.Start(wr)) { text = T(lang, "Refresh", "更新", "새로 고침") };
                re.AddToClassList("wc-button"); re.AddToClassList("wc-preview__refresh");
                row.Add(re);
            }
            bar.Add(row);
            string note = desc == null ? T(lang, "Available when the wheelchair is inside an avatar.", "アバターの中にあるときに使えます。", "아바타 안에 있을 때 쓸 수 있습니다.")
                : WcSitPreview.LastError != null ? WcSitPreview.LastError
                : on && stale ? T(lang, "Settings changed — refresh to see them.", "設定が変わりました。更新すると反映されます。", "설정이 바뀌었습니다. 새로 고침하면 반영됩니다.")
                : on ? T(lang, "A built copy, seated (desktop pose). Scene view only, never saved. The avatar is hidden in the Scene view until you turn this off.",
                         "ビルドしたコピーを座らせて表示（デスクトップの姿勢）。Scene ビューのみ・保存されません。オフにするまでアバターは Scene ビューで隠れます。",
                         "빌드한 사본을 앉혀서 보여 줍니다(데스크톱 자세). Scene 뷰에서만 보이고 저장되지 않습니다. 끌 때까지 아바타는 Scene 뷰에서 숨겨집니다.")
                : T(lang, "Builds a copy with your current settings and seats it in place (takes a few seconds).",
                      "今の設定でコピーをビルドして、その場に座らせます（数秒かかります）。",
                      "지금 설정으로 사본을 빌드해 그 자리에 앉혀 봅니다(몇 초 걸립니다).");
            var n = Label(note, "wc-note"); n.AddToClassList("wc-preview__note");
            if (on && stale || WcSitPreview.LastError != null) n.AddToClassList("wc-fact__value--gold");
            bar.Add(n);
            return bar;
        }

        // ---------------------------------------------------------------- 04 변경 기록
        VisualElement ChangelogSection()
        {
            var readme = ReadmePath();
            var entries = readme != null ? ReadChangelog(readme) : new System.Collections.Generic.List<(string ver, string date, string text)>();
            var cur = entries.FirstOrDefault(e => e.ver == WcConst.Version);
            if (cur.ver == null && entries.Count > 0) cur = entries[0];
            var sec = Section(Num(), T(lang, "CHANGELOG", "更新履歴", "변경 기록"), cur.ver != null ? "v" + cur.ver + " · " + cur.date : null);
            var content = new VisualElement();
            if (cur.ver == null) content.Add(Label(T(lang, "See the README.", "README を見てください。", "README를 보세요."), "wc-note"));
            else content.Add(Label(cur.text, "wc-changelog"));
            if (readme != null)
            {
                var all = Label(T(lang, "Full changelog — README", "すべての更新履歴 — README", "전체 변경 기록 — README"), "wc-link");
                all.RegisterCallback<ClickEvent>(_ => EditorUtility.OpenWithDefaultApp(readme));
                content.Add(all);
            }
            sec.Add(Fold("Pyrite9WC.ShowChangelog", true, T(lang, "THIS VERSION", "この版", "이번 버전"), content));
            return sec;
        }

        // README '## 변경 기록 / Changelog / 更新履歴' 절의 "- **1.0.1** (2026-10-18): 글" 줄들(들여 쓴 다음 줄은 이어 붙임)
        static System.Collections.Generic.List<(string ver, string date, string text)> ReadChangelog(string path)
        {
            var list = new System.Collections.Generic.List<(string ver, string date, string text)>();
            try
            {
                bool inSec = false;
                var rx = new System.Text.RegularExpressions.Regex(@"^-\s*\*\*(?<v>[0-9][0-9.]*)\*\*\s*[(（](?<d>[^)）]*)[)）]\s*[:：]\s*(?<t>.*)$");
                foreach (var line in File.ReadAllLines(path))
                {
                    if (line.StartsWith("## ")) { inSec = line.Contains("Changelog") || line.Contains("변경 기록") || line.Contains("更新履歴"); continue; }
                    if (!inSec) continue;
                    var m = rx.Match(line.Trim());
                    if (m.Success) list.Add((m.Groups["v"].Value, m.Groups["d"].Value.Trim(), Plain(m.Groups["t"].Value)));
                    else if (list.Count > 0 && line.StartsWith("  ") && line.Trim().Length > 0)
                    {
                        var last = list[list.Count - 1];
                        list[list.Count - 1] = (last.ver, last.date, last.text + " " + Plain(line.Trim()));
                    }
                }
            }
            catch { }
            return list;
        }

        static string Plain(string md) => md.Replace("**", "").Replace("`", "");

        // ---------------------------------------------------------------- 01 기본
        VisualElement BasicSection()
        {
            var sec = Section(Num(), T(lang, "BASICS", "基本", "기본"), null);
            var grid = new VisualElement(); grid.AddToClassList("wc-rows");
            sec.Add(grid);

            var seg = new VisualElement(); seg.AddToClassList("wc-seg");
            string[] names = { "EN", "日本語", "한국어" };
            for (int i = 0; i < names.Length; i++)
            {
                int v = i;
                var b = new Button(() =>
                {
                    var prop = serializedObject.FindProperty("menuLanguage");
                    prop.intValue = v;
                    serializedObject.ApplyModifiedProperties();
                    Build();
                }) { text = names[i] };
                b.AddToClassList("wc-seg__item");
                if (i == 0) b.AddToClassList("wc-seg__item--first");
                if (i == lang) b.AddToClassList("wc-seg__item--on");
                seg.Add(b);
            }
            grid.Add(Row(T(lang, "Menu language", "メニュー言語", "메뉴 언어"),
                "Menu Language — " + T(lang, "Expression Menu labels, applied at build.", "Expression Menu の表示。ビルド時に反映。", "Expression Menu 표시. 빌드 때 적용."), seg));

            grid.Add(Row(T(lang, "Auto fit", "自動フィット", "자동 맞춤"),
                "Auto Fit — " + T(lang, "Turn off to keep the prefab's own size and position.", "オフにするとプレハブのままの大きさ・位置。", "끄면 프리팹 그대로의 크기·위치."),
                Check("autoFit", T(lang, "Fit to the avatar at build", "ビルド時にアバターに合わせる", "빌드 때 아바타에 맞춤"))));
            return sec;
        }

        // 체크 + 옆 글. Unity 기본 체크 그림 대신 직접 그린다(기본 그림이 금색 바탕을 덮음)
        static VisualElement Check(string path, string text)
        {
            var box = new VisualElement(); box.AddToClassList("wc-check");
            var tg = new Toggle { bindingPath = path };
            tg.AddToClassList("wc-toggle");
            var mark = tg.Q(className: "unity-toggle__checkmark");
            if (mark != null)
            {
                mark.generateVisualContent += mgc =>
                {
                    if (!tg.value) return;
                    var p = mgc.painter2D;
                    p.strokeColor = new Color32(0x15, 0x17, 0x1C, 0xFF);
                    p.lineWidth = 2f; p.lineCap = LineCap.Round; p.lineJoin = LineJoin.Round;
                    p.BeginPath();
                    p.MoveTo(new Vector2(3.5f, 7.5f)); p.LineTo(new Vector2(6.5f, 10.5f)); p.LineTo(new Vector2(11.5f, 4.5f));
                    p.Stroke();
                };
                tg.RegisterValueChangedCallback(_ => mark.MarkDirtyRepaint());
            }
            box.Add(tg);
            box.Add(Label(text, "wc-check__text"));
            return box;
        }

        // ---------------------------------------------------------------- AFK(앉은 채 AFK 모션 — 빌드 때 굽는다, 9-56)
        string MotionName(int m) => m == (int)AfkMotion.BothArms ? T(lang, "Both arms (V)", "両腕バンザイ（V）", "V자 만세")
            : m == (int)AfkMotion.OneArm ? T(lang, "One-arm", "片腕の伸び", "한쪽 기지개")
            : m == (int)AfkMotion.Off ? T(lang, "Off", "オフ", "끔") : T(lang, "Auto", "自動", "자동");

        VisualElement AfkSection()
        {
            var sec = Section(Num(), "AFK", null);
            var grid = new VisualElement(); grid.AddToClassList("wc-rows");
            sec.Add(grid);

            // 모션: 끔 / 자동 / 한쪽 기지개 / V자 만세
            var prop = serializedObject.FindProperty("afkMotion");
            int cur = prop != null ? prop.intValue : 0;
            var seg = new VisualElement(); seg.AddToClassList("wc-seg");
            int[] order = { (int)AfkMotion.Off, (int)AfkMotion.Auto, (int)AfkMotion.OneArm, (int)AfkMotion.BothArms };
            for (int i = 0; i < order.Length; i++)
            {
                int v = order[i];
                var b = new Button(() =>
                {
                    var p = serializedObject.FindProperty("afkMotion");
                    p.intValue = v;
                    serializedObject.ApplyModifiedProperties();
                    Build();
                }) { text = MotionName(v) };
                b.AddToClassList("wc-seg__item");
                if (i == 0) b.AddToClassList("wc-seg__item--first");
                if (v == cur) b.AddToClassList("wc-seg__item--on");
                seg.Add(b);
            }
            grid.Add(Row(T(lang, "Motion", "モーション", "모션"),
                "AFK Motion — " + T(lang, "While seated and AFK: stand up, stretch, walk away and disappear; sit back down when you return. Made at build time.",
                    "座ったまま AFK になると、立ち上がって伸びをし、歩いて消えます。戻ると座り直します。ビルド時に作ります。",
                    "앉은 채 AFK가 되면 일어나 기지개를 켜고 걸어가 사라지고, 돌아오면 다시 앉습니다. 빌드 때 만듭니다."), seg));
            if (cur == (int)AfkMotion.Auto)
                sec.Add(Label(T(lang,
                    "Auto: the build first tries the one-arm stretch and measures the closest gap between the arms and the head. If an arm would go into the head (gap below 0), it uses both arms (V) instead. Avatars with a large head or short arms usually get V.",
                    "自動：ビルド時にまず片腕の伸びを試し、腕と頭のいちばん近い間隔を測ります。腕が頭にめり込む（間隔が0未満）なら両腕バンザイ（V）にします。頭が大きい・腕が短いアバターはたいていVになります。",
                    "자동: 빌드할 때 한쪽 기지개를 먼저 해 보고 팔과 머리 사이 가장 가까운 간격을 잽니다. 팔이 머리 속으로 들어가면(간격 0 미만) V자 만세로 바꿉니다. 머리가 크거나 팔이 짧은 아바타는 대개 V자 만세가 됩니다."), "wc-note wc-note--gap"));
            else if (cur == (int)AfkMotion.Off)
                sec.Add(Label(T(lang, "Off: while AFK you just stay seated.", "オフ：AFK の間は座ったままです。", "끔: AFK 동안 그대로 앉아 있습니다."), "wc-note wc-note--gap"));

            // 마지막 빌드에서 만든 것(WcAfkBakeRecord — 빌드의 afkmotion 패스가 쓴다)
            var desc = ((Component)target).GetComponentInParent<VRCAvatarDescriptor>(true);
            var rec = desc != null ? WcAfkBakeRecord.Get(desc.gameObject.name) : null;
            if (rec != null)
            {
                string baked = MotionName(rec.motion);
                if (rec.setting == (int)AfkMotion.Auto && rec.motion == (int)AfkMotion.BothArms && rec.oneArmGap < 100f)
                    baked += T(lang, $"  (auto · one-arm {rec.oneArmGap * 100f:+0.0;-0.0} cm)", $"（自動・片腕 {rec.oneArmGap * 100f:+0.0;-0.0} cm）", $"  (자동 · 한쪽 {rec.oneArmGap * 100f:+0.0;-0.0} cm)");
                else if (rec.setting == (int)AfkMotion.Auto) baked += T(lang, "  (auto)", "（自動）", "  (자동)");
                baked += " · " + ShortTime(rec.time);
                bool changed = rec.setting != cur && cur != (int)AfkMotion.Off;
                grid.Add(Fact(T(lang, "Last build", "前回のビルド", "마지막 빌드"), baked, changed ? "wc-fact__value--gold" : null));
                if (changed)
                    sec.Add(Label(T(lang, "Changed — applied at the next Play or upload.", "変更しました。次の Play・アップロードで反映されます。", "바뀌었습니다. 다음 Play·업로드 때 반영됩니다."), "wc-note wc-note--gap"));
            }

            // 사라지기: 디졸브 / 즉시
            var vp = serializedObject.FindProperty("afkVanish");
            int vcur = vp != null ? vp.intValue : 0;
            var vseg = new VisualElement(); vseg.AddToClassList("wc-seg");
            string[] vnames = { T(lang, "Dissolve", "ディゾルブ", "디졸브"), T(lang, "Instant", "即時", "즉시") };
            for (int i = 0; i < 2; i++)
            {
                int v = i;
                var b = new Button(() =>
                {
                    var p = serializedObject.FindProperty("afkVanish");
                    p.intValue = v;
                    serializedObject.ApplyModifiedProperties();
                    Build();
                }) { text = vnames[i] };
                b.AddToClassList("wc-seg__item");
                if (i == 0) b.AddToClassList("wc-seg__item--first");
                if (i == vcur) b.AddToClassList("wc-seg__item--on");
                vseg.Add(b);
            }
            grid.Add(Row(T(lang, "Vanish", "消え方", "사라지기"),
                "AFK Vanish — " + T(lang, "Applied at build. Dissolve by default.", "ビルド時に反映。既定はディゾルブ。", "빌드 때 적용. 기본은 디졸브."), vseg));
            sec.Add(Label(vcur == 1
                ? T(lang, "Instant: disappears at once the moment the eyes close, and reappears at once (eyes open 0.3 s later). For avatars whose clothes show their inside while dissolving.",
                    "即時：目を閉じた瞬間に一度に消え、戻るときも一度に現れます（0.3秒後に目を開ける）。ディゾルブ中に服の内側が透けて見えるアバター向け。",
                    "즉시: 눈을 감는 순간 한 번에 사라지고, 돌아올 때도 한 번에 나타납니다(0.3초 뒤 눈 뜸). 디졸브 동안 옷 안쪽이 비쳐 보이는 아바타용.")
                : T(lang, "Dissolve: fades out gradually while walking away. If the inside of the clothes shows through, use Instant.",
                    "ディゾルブ：歩き去りながら少しずつ消えます。服の内側が透けて見えるなら「即時」に。",
                    "디졸브: 걸어가면서 서서히 사라집니다. 옷 안쪽이 비쳐 보이면 '즉시'로."), "wc-note"));
            return sec;
        }

        // ---------------------------------------------------------------- 02 미세 조정
        VisualElement OffsetSection()
        {
            var sec = Section(Num(), T(lang, "FINE TUNING", "微調整", "미세 조정"), "m");
            var grid = new VisualElement(); grid.AddToClassList("wc-rows");
            sec.Add(grid);
            grid.Add(OffsetRow("seatWidthOffset", T(lang, "Seat width", "座面の幅", "좌석 폭"),
                "Seat Width Offset — " + T(lang, "per side. + wider", "片側。+ で広く", "한쪽. +는 넓게")));
            grid.Add(OffsetRow("seatHeightOffset", T(lang, "Seat height", "座る高さ", "앉은 높이"),
                "Seat Height Offset — " + T(lang, "added to the automatic sitting height. + raises the body, - lowers it", "自動で決まる座る高さに足す。+ で上へ、- で下へ", "자동으로 정한 앉는 높이에 더함. +는 위로, -는 아래로")));
            grid.Add(OffsetRow("footHeightOffset", T(lang, "Footrest height", "足置きの高さ", "발판 높이"),
                "Foot Height Offset — " + T(lang, "+ up", "+ で上へ", "+는 위로")));
            grid.Add(OffsetRow("footForwardOffset", T(lang, "Footrest forward", "足置きの前後", "발판 앞뒤"),
                "Foot Forward Offset — " + T(lang, "+ forward", "+ で前へ", "+는 앞으로")));
            return sec;
        }

        VisualElement OffsetRow(string path, string label, string tip)
        {
            var row = new VisualElement(); row.AddToClassList("wc-offset");
            row.tooltip = tip;
            var l = Label(label, "wc-label"); row.Add(l);

            var gauge = new VisualElement(); gauge.AddToClassList("wc-gauge");
            var track = new VisualElement(); track.AddToClassList("wc-gauge__track"); gauge.Add(track);
            var zero = new VisualElement(); zero.AddToClassList("wc-gauge__zero"); gauge.Add(zero);
            var fill = new VisualElement(); fill.AddToClassList("wc-gauge__fill"); gauge.Add(fill);
            var knob = new VisualElement(); knob.AddToClassList("wc-gauge__knob"); gauge.Add(knob);
            row.Add(gauge);

            var field = new FloatField { bindingPath = path, formatString = "+0.000;-0.000;0.000" };
            field.AddToClassList("wc-value");
            row.Add(field);

            void Show(float v)
            {
                bool on = Mathf.Abs(v) > 1e-4f;
                float t = Mathf.Clamp(v / GaugeRange, -1f, 1f) * 0.5f + 0.5f;
                fill.style.left = Length.Percent(Mathf.Min(t, 0.5f) * 100f);
                fill.style.width = Length.Percent(Mathf.Abs(t - 0.5f) * 100f);
                knob.style.left = Length.Percent(t * 100f);
                row.EnableInClassList("wc-offset--on", on);
            }
            field.RegisterValueChangedCallback(e => Show(e.newValue));
            Show(serializedObject.FindProperty(path).floatValue);

            // 게이지를 끌어서 값 바꾸기(1 mm 단위), 두 번 누르면 0
            bool drag = false;
            void SetFrom(Vector2 local)
            {
                float w = gauge.contentRect.width;
                if (w < 1f) return;
                float v = (Mathf.Clamp01(local.x / w) - 0.5f) * 2f * GaugeRange;
                v = Mathf.Round(v * 1000f) / 1000f;
                var prop = serializedObject.FindProperty(path);
                if (Mathf.Approximately(prop.floatValue, v)) return;
                prop.floatValue = v;
                serializedObject.ApplyModifiedProperties();
            }
            gauge.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (e.clickCount == 2)
                {
                    var prop = serializedObject.FindProperty(path);
                    prop.floatValue = 0f; serializedObject.ApplyModifiedProperties();
                    return;
                }
                drag = true; gauge.CapturePointer(e.pointerId); SetFrom(e.localPosition);
            });
            gauge.RegisterCallback<PointerMoveEvent>(e => { if (drag) SetFrom(e.localPosition); });
            gauge.RegisterCallback<PointerUpEvent>(e => { drag = false; gauge.ReleasePointer(e.pointerId); });
            return row;
        }

        // ---------------------------------------------------------------- 03 마지막 빌드
        VisualElement BuildSection()
        {
            var desc = ((Component)target).GetComponentInParent<VRCAvatarDescriptor>(true);
            var rec = desc != null ? WcBuildRecord.Get(desc.gameObject.name) : null;
            string meta = rec != null ? $"{rec.avatar} · {ShortTime(rec.time)}" : null;
            var sec = Section(Num(), T(lang, "LAST BUILD", "最後のビルド", "마지막 빌드"), meta);

            if (desc == null)
                sec.Add(Label(T(lang, "Put the wheelchair inside an avatar (under the object with the VRC Avatar Descriptor).",
                    "アバターの中（VRC Avatar Descriptor のオブジェクトの下）に入れてください。",
                    "아바타 안(VRC Avatar Descriptor가 있는 오브젝트 밑)에 넣어 주세요."), "wc-note"));
            else if (rec == null)
                sec.Add(Label(T(lang, "No build yet — Play or Build & Test fills this in.",
                    "まだビルドの記録がありません。Play または Build & Test で表示されます。",
                    "아직 빌드 기록이 없습니다. Play 또는 Build & Test 때 채워집니다."), "wc-note"));
            else
            {
                var grid = new VisualElement(); grid.AddToClassList("wc-facts");
                sec.Add(grid);
                if (rec.fitted)
                {
                    bool far = rec.scale < 0.75f || rec.scale > 1.35f;   // WheelchairFit.FitBase의 WARN 범위와 같음
                    grid.Add(Fact(T(lang, "Chair scale", "車椅子の倍率", "휠체어 배율"),
                        rec.scale.ToString("0.00") + (far ? T(lang, " — far from the reference", " — 基準から遠い", " — 기준과 멂") : ""), far ? "wc-fact__value--gold" : null));
                    grid.Add(Fact(T(lang, "Body raise", "体の持ち上げ", "몸 올림"),
                        Mathf.Abs(rec.raise) < 1e-4f ? "0 m" : rec.raise.ToString("+0.000;-0.000") + " m", null));
                }
                else
                    grid.Add(Fact(T(lang, "Auto fit", "自動フィット", "자동 맞춤"), T(lang, "off — prefab size", "オフ — プレハブのまま", "꺼짐 — 프리팹 그대로"), null));
                grid.Add(Fact("Space Drag", rec.heightScale
                    ? T(lang, "any avatar size · up to 2.55 m", "アバターの大きさに関係なく · 最大 2.55 m", "크기 무관 · 최대 2.55 m")
                    : T(lang, "fixed to the build size", "ビルド時の大きさのみ", "빌드 때 크기에서만"), rec.heightScale ? null : "wc-fact__value--gold"));
                grid.Add(Fact(T(lang, "Warnings", "警告", "경고"), rec.warnings.Count == 0 ? T(lang, "none", "なし", "없음") : rec.warnings.Count.ToString(),
                    rec.warnings.Count == 0 ? null : "wc-fact__value--gold"));
                if (rec.version != WcConst.Version)
                    grid.Add(Fact(T(lang, "Built with", "ビルド時の版", "빌드 버전"), "v" + rec.version + T(lang, " — rebuild", " — 再ビルドを", " — 다시 빌드 필요"), "wc-fact__value--gold"));
                foreach (var w in rec.warnings.Take(6))
                    sec.Add(Label(w.Replace("fitA: ", "").Replace("fitB: ", "").Replace("WARN ", ""), "wc-warn"));
                if (rec.warnings.Count > 6) sec.Add(Label("…", "wc-warn"));
            }

            var buttons = new VisualElement(); buttons.AddToClassList("wc-buttons");
            var logPath = Path.GetFullPath("Logs/Pyrite9_Wheelchair_build.txt");
            var logBtn = new Button(() => EditorUtility.OpenWithDefaultApp(logPath)) { text = T(lang, "Build log", "ビルドログ", "빌드 로그") };
            logBtn.AddToClassList("wc-button");
            logBtn.SetEnabled(File.Exists(logPath));
            buttons.Add(logBtn);
            var readme = ReadmePath();
            var rmBtn = new Button(() => EditorUtility.OpenWithDefaultApp(readme)) { text = "README" };
            rmBtn.AddToClassList("wc-button");
            rmBtn.SetEnabled(readme != null);
            buttons.Add(rmBtn);
            sec.Add(buttons);
            return sec;
        }

        string ReadmePath()
        {
            var script = MonoScript.FromScriptableObject(this);
            if (script == null) return null;
            var editorDir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(script));
            var pkg = Path.GetDirectoryName(editorDir);
            string name = lang == 1 ? "README_jp.md" : lang == 2 ? "README_ko.md" : "README_en.md";
            var p = Path.GetFullPath(Path.Combine(pkg, name));
            return File.Exists(p) ? p : null;
        }

        static string ShortTime(string t)
        {
            if (!System.DateTime.TryParse(t, out var d)) return t;
            return d.Date == System.DateTime.Today ? d.ToString("HH:mm") : d.ToString("MM-dd HH:mm");
        }

        // ---------------------------------------------------------------- 공용 조각
        static Label Label(string text, string cls)
        {
            var l = new Label(text);
            if (cls != null) foreach (var k in cls.Split(' ')) l.AddToClassList(k);
            return l;
        }

        static VisualElement Section(string num, string title, string meta)
        {
            var sec = new VisualElement(); sec.AddToClassList("wc-section");
            var head = new VisualElement(); head.AddToClassList("wc-section__head");
            head.Add(Label(num, "wc-section__num"));
            head.Add(Label(title, "wc-section__title"));
            if (meta != null) head.Add(Label(meta, "wc-section__meta"));
            sec.Add(head);
            return sec;
        }

        static VisualElement Divider() { var d = new VisualElement(); d.AddToClassList("wc-divider"); return d; }

        static VisualElement Row(string label, string tip, VisualElement control)
        {
            var row = new VisualElement(); row.AddToClassList("wc-row");
            row.tooltip = tip;
            row.Add(Label(label, "wc-label"));
            control.AddToClassList("wc-row__control");
            row.Add(control);
            return row;
        }

        static VisualElement Fact(string label, string value, string valueCls)
        {
            var row = new VisualElement(); row.AddToClassList("wc-fact");
            row.Add(Label(label, "wc-label"));
            var v = Label(value, "wc-fact__value");
            if (valueCls != null) v.AddToClassList(valueCls);
            row.Add(v);
            return row;
        }
    }
}
#endif
