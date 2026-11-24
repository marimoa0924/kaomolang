// 프로토타입 화면: 노트북 바탕화면(레트로 데스크톱) 하나 안에서 창을 여닫는다.
// shortcut: IMGUI와 코드로 만든 단색 텍스처뿐. 픽셀 아트를 입힐 때 uGUI/UI Toolkit으로 교체하고 GameState는 그대로 쓴다.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Kaomoji.GameData;

namespace Kaomoji
{
    public class GameView : MonoBehaviour
    {
        // 씬 설정 없이 Play만 누르면 시작되게 한다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<GameView>() == null) new GameObject("KaomojiGame").AddComponent<GameView>();
        }

        enum Win { Places, Encounter, Sns, Notebook, Traits, Ending }

        class Popup
        {
            public string Title, Body, Ok = "확인", Cancel;
            public bool Error;
            public Action OnOk;
        }

        const float VW = 1280, VH = 720, BarH = 38, PopupW = 460;
        const int PopupId = 99;
        static readonly Dictionary<Win, float> WinWidth = new()
        {
            [Win.Places] = 560, [Win.Encounter] = 820, [Win.Sns] = 960, [Win.Notebook] = 600, [Win.Traits] = 540, [Win.Ending] = 560,
        };
        static readonly Emo[] PartOrder = { Emo.Neutral, Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };

        GameState S = new();
        readonly List<Win> open = new();
        readonly Dictionary<Win, Rect> rects = new();
        readonly Queue<Popup> popups = new();
        readonly HashSet<string> tips = new();
        Rect popupRect = new((VW - PopupW) / 2, 150, PopupW, 0);
        Action pending;       // 버튼 동작은 다음 Update에서 실행해 IMGUI 레이아웃 불일치를 피한다
        int? focusReq;
        string place;
        bool traitsEndNight, blocked;
        Emo selEye = Emo.Neutral, selMouth = Emo.Neutral;

        Font font;
        GUIStyle label, small, big, faceStyle, postStyle, symStyle, winStyle, titleStyle, errTitleStyle,
                 btn, selBtn, closeBtn, card, paper, bubble, iconStyle, taskbar, panel;
        readonly Dictionary<string, Texture2D> texCache = new();
        readonly Dictionary<Emo, GUIStyle> chips = new(), tintCards = new();
        Texture2D bgDay, bgNight;

        // ---------- 수명 주기 ----------

        void Awake()
        {
            // 한글과 카오모지 글리프가 있는 OS 폰트
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial Unicode MS" }, 16);
            Tip("intro", Manual.Intro);
        }

        void Update()
        {
            if (pending != null) { var a = pending; pending = null; a(); }
            if (S.Ending != Ending.None && !open.Contains(Win.Ending))
            {
                open.Clear(); popups.Clear();
                Open(Win.Ending);
            }
        }

        void OnGUI()
        {
            InitStyles();
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), S.IsNight ? bgNight : bgDay);
            float k = Mathf.Min(Screen.width / VW, Screen.height / VH);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - VW * k) / 2, (Screen.height - VH * k) / 2, 0), Quaternion.identity, new Vector3(k, k, 1));

            blocked = popups.Count > 0;
            DrawIcons();
            DrawWidgets();
            DrawTaskbar();

            foreach (var w in open.ToList())
                rects[w] = GUILayout.Window((int)w + 1, rects[w], _ => DrawWin(w), GUIContent.none, winStyle, GUILayout.Width(WinWidth[w]));

            if (popups.Count > 0)
            {
                popupRect = GUILayout.Window(PopupId, popupRect, _ => DrawPopup(), GUIContent.none, winStyle, GUILayout.Width(PopupW));
                GUI.BringWindowToFront(PopupId);
                GUI.FocusWindow(PopupId);
            }
            else if (focusReq.HasValue) { GUI.BringWindowToFront(focusReq.Value); GUI.FocusWindow(focusReq.Value); focusReq = null; }
        }

        // ---------- 창 관리 ----------

        void Open(Win w)
        {
            if (!open.Contains(w))
            {
                open.Add(w);
                float wid = WinWidth[w];
                float x = wid > 820 ? 130 : 140 + (790 - wid) / 2 + open.Count * 14;
                rects[w] = new Rect(x, 24 + open.Count * 14, wid, 0);
            }
            focusReq = (int)w + 1;
        }

        void Close(Win w)
        {
            open.Remove(w);
            if (w == Win.Traits && traitsEndNight)
            {
                traitsEndNight = false;
                S.EndNight();
                if (S.Ending == Ending.None)
                    Say($"{S.Day}일째 아침", $"새 아침이 밝았다. 남은 날: {Balance.Days - S.Day + 1}일\n\n오늘은 외출할까, 쉴까?");
            }
        }

        void Say(string title, string body, Action ok = null) => popups.Enqueue(new Popup { Title = title, Body = body, OnOk = ok });

        void Tip(string key, string body)
        {
            if (tips.Add(key)) popups.Enqueue(new Popup { Title = "모행성 매뉴얼.txt", Body = body, Ok = "알겠어" });
        }

        void Restart()
        {
            S = new GameState();
            open.Clear(); popups.Clear();
            traitsEndNight = false;
            selEye = selMouth = Emo.Neutral;
            Say("1일째 아침", "다시 지구에 내려왔다. 이번엔 잘해 보자.");
        }

        // ---------- 바탕화면 ----------

        void DrawIcons()
        {
            var icons = new (string glyph, string name, Action act, bool on)[]
            {
                ("☞", "외출", ClickOuting, !S.IsNight),
                ("♨", "휴식", ClickRest, !S.IsNight),
                ("♥", "SNS", ClickSns, S.IsNight),
                ("▤", "해독 수첩", () => { Open(Win.Notebook); Tip("notebook", Manual.Notebook); }, true),
                ("★", S.Points > 0 ? $"특성 ({S.Points})" : "특성", () => Open(Win.Traits), true),
            };
            iconStyle.normal.textColor = iconStyle.hover.textColor = S.IsNight ? Color.white : C("#4a3a5a");
            for (int i = 0; i < icons.Length; i++)
            {
                var (glyph, name, act, on) = icons[i];
                GUI.color = new Color(1, 1, 1, on ? 1 : 0.45f); // 못 쓰는 아이콘도 눌러서 이유를 알 수 있게 흐리게만
                if (GUI.Button(new Rect(24, 24 + i * 104, 92, 96), $"<size=38>{glyph}</size>\n{name}", iconStyle) && !blocked) pending = act;
                GUI.color = Color.white;
            }
        }

        void ClickOuting()
        {
            if (S.IsNight) { Say("지금은 밤", "외출은 낮에만 할 수 있다.\n밤에는 [SNS]를 열자."); return; }
            Open(Win.Places);
            Tip("outing", Manual.Outing);
        }

        void ClickRest()
        {
            if (S.IsNight) { Say("지금은 밤", "휴식은 낮에 고르는 행동이다.\n밤에는 [SNS]를 열자."); return; }
            popups.Enqueue(new Popup
            {
                Title = "휴식", Ok = "쉰다", Cancel = "취소",
                Body = $"오늘은 방에서 쉴까?\n\n· 의태 피로 {Balance.RestFat}\n· 의심도 {Balance.RestSus}\n· 새 파츠는 얻지 못한다\n· 확인하면 바로 밤이 된다",
                OnOk = () =>
                {
                    var before = Snap();
                    S.DoRest();
                    Say("밤이 되었다", $"방에서 푹 쉬었다.\n{Delta(before)}\n\n바탕화면의 [SNS]를 열자.");
                },
            });
        }

        void ClickSns()
        {
            if (!S.IsNight) { Say("아직 낮", "SNS는 밤에 열린다.\n먼저 [외출]하거나 [휴식]해서 낮을 보내자."); return; }
            S.MarkSeen();
            Open(Win.Sns);
            Tip("sns", Manual.Sns);
        }

        void DrawWidgets()
        {
            GUILayout.BeginArea(new Rect(VW - 344, 20, 324, 520));
            GUILayout.BeginVertical(panel);
            GUILayout.Label("상태", big);
            Bar("팔로워", S.Followers, Balance.FollowerGoal, "#a68bff", $"{S.Followers}/{Balance.FollowerGoal}");
            Bar("의심도", S.Sus, 100, "#ff6b8a", $"{Mathf.RoundToInt(S.Sus)}");
            Bar("의태 피로", S.Fat, 100, S.Fat >= Balance.FatShaky ? "#ff3b3b" : "#f4a261", $"{Mathf.RoundToInt(S.Fat)}");
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical(paper);
            GUILayout.Label("오늘 할 일", big);
            if (!S.IsNight) Todo(false, "[외출] 또는 [휴식] 고르기");
            else
            {
                Todo(true, "낮 행동");
                Todo(S.Uploaded, "[SNS] 사진 올리기");
                int n = S.Replied.Count(r => r.HasValue);
                Todo(n == 3, $"[SNS] 팬 글 답장 {n}/3");
                Todo(false, "특성 포인트 쓰고 잠들기");
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        void Todo(bool done, string text) =>
            GUILayout.Label(done ? $"<color=#9a8aa8>■ {text}</color>" : $"□ {text}", label);

        void Bar(string name, float v, float max, string color, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, small, GUILayout.Width(70));
            var r = GUILayoutUtility.GetRect(150, 22, GUILayout.ExpandWidth(true));
            r.y += 6; r.height = 12;
            GUI.DrawTexture(r, Tex("#f3e3ec"));
            var f = r; f.width *= Mathf.Clamp01(v / max);
            GUI.DrawTexture(f, Tex(color));
            GUILayout.Label(value, small, GUILayout.Width(70));
            GUILayout.EndHorizontal();
        }

        void DrawTaskbar()
        {
            GUI.Box(new Rect(0, VH - BarH, VW, BarH), GUIContent.none, taskbar);
            if (GUI.Button(new Rect(4, VH - BarH + 4, 96, BarH - 8), "<b>♥ 시작</b>", btn) && !blocked)
                pending = () => popups.Enqueue(new Popup { Title = "게임 방법", Body = Manual.Help });
            float x = 108;
            foreach (var w in open)
            {
                if (GUI.Button(new Rect(x, VH - BarH + 4, 150, BarH - 8), Title(w), btn)) focusReq = (int)w + 1;
                x += 154;
            }
            var day = S.IsNight ? "<color=#5b4b8a>● 밤</color>" : "<color=#e07a00>○ 낮</color>";
            GUI.Label(new Rect(VW - 190, VH - BarH + 4, 180, BarH - 8), $"<b>{S.Day}일째 / {Balance.Days}</b>   {day}",
                new GUIStyle(label) { alignment = TextAnchor.MiddleRight, wordWrap = false });
        }

        string Title(Win w) => w switch
        {
            Win.Places => "외출", Win.Encounter => place != null ? $"{PlaceById(place).Name} — {PlaceById(place).Npc}" : "만남",
            Win.Sns => $"SNS — {S.Day}일째 밤", Win.Notebook => "해독 수첩", Win.Traits => "특성", _ => "엔딩",
        };

        // ---------- 창 공통 ----------

        void TitleBar(string title, float width, Action onClose, bool error = false)
        {
            GUI.Label(new Rect(4, 4, width - 8, 26), title, error ? errTitleStyle : titleStyle);
            if (onClose != null && GUI.Button(new Rect(width - 30, 7, 22, 20), "×", closeBtn) && !blocked) pending = onClose;
        }

        bool Btn(string t, Action act, bool on = true, GUIStyle st = null, params GUILayoutOption[] opts)
        {
            GUI.enabled = on && !blocked;
            bool r = GUILayout.Button(t, st ?? btn, opts);
            GUI.enabled = true;
            if (r) pending = act;
            return r;
        }

        void DrawWin(Win w)
        {
            blocked = popups.Count > 0;
            float width = rects[w].width;
            TitleBar(Title(w), width, w == Win.Ending ? null : () => Close(w));
            switch (w)
            {
                case Win.Places: DrawPlaces(); break;
                case Win.Encounter: DrawEncounter(); break;
                case Win.Sns: DrawSns(); break;
                case Win.Notebook: DrawNotebook(); break;
                case Win.Traits: DrawTraits(); break;
                case Win.Ending: DrawEnding(); break;
            }
            GUI.DragWindow(new Rect(0, 0, width, 32));
        }

        void DrawPopup()
        {
            blocked = false;
            var p = popups.Peek();
            TitleBar(p.Title, popupRect.width, null, p.Error);
            GUILayout.Label(p.Body, label);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (p.Cancel != null) Btn(p.Cancel, () => popups.Dequeue(), true, null, GUILayout.Width(100));
            Btn(p.Ok, () => { popups.Dequeue(); p.OnOk?.Invoke(); }, true, null, GUILayout.Width(100));
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, popupRect.width, 32));
        }

        // ---------- 외출 ----------

        void DrawPlaces()
        {
            GUILayout.Label("장소마다 일하는 사람이 다르고, 배울 수 있는 얼굴도 다르다.", small);
            foreach (var row in GameData.Places.Select((p, i) => (p, i)).GroupBy(x => x.i / 2))
            {
                GUILayout.BeginHorizontal();
                foreach (var (p, _) in row)
                {
                    int aff = S.Aff.GetValueOrDefault(p.Id), n = S.Visits.GetValueOrDefault(p.Id);
                    Btn($"<size=18><b>{p.Name}</b></size>\n{p.Npc}\n<size=12>가본 횟수 {n} · 호감 {(aff > 0 ? new string('♥', aff) : "-")}</size>",
                        () =>
                        {
                            place = p.Id; selEye = selMouth = Emo.Neutral;
                            Close(Win.Places); Open(Win.Encounter);
                            Tip("encounter", Manual.Encounter);
                        }, true, null, GUILayout.Height(96));
                }
                GUILayout.EndHorizontal();
            }
        }

        void DrawEncounter()
        {
            var ev = S.NextEvent(place);
            bool second = S.Visits.GetValueOrDefault(place) > 0;
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(400));
            GUILayout.Label(second ? "다시 만났다. 조금 친해진 것 같다." : "처음 보는 사람이다.", small);
            GUILayout.BeginVertical(paper);
            GUILayout.Label("<b>상황</b>", label);
            GUILayout.Label(ev.Situation, label);
            GUILayout.EndVertical();
            GUILayout.Space(6);
            GUILayout.Label("상대가 말을 건다:", small);
            GUILayout.Label($"“{ev.Say}”", bubble);
            GUILayout.Label("말은 못 알아듣지만, 표정과 상황을 보고 반응하자.", small);
            if (S.Fat >= Balance.FatShaky) GUILayout.Label("<color=#e0344f><b>의태 피로가 높다. 얼굴이 멋대로 바뀔지도 모른다.</b></color>", label);
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical();
            Builder();
            Btn("<b>이 표정으로 반응하기</b>", React, true, null, GUILayout.Height(40));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        void React()
        {
            var before = Snap();
            var p = PlaceById(place);
            var (r, ans, msgs) = S.DoEncounter(place, selEye, selMouth);
            Close(Win.Encounter);
            Say($"{p.Npc}의 반응 — {ResultName(r)}",
                $"{Why(r, selEye, selMouth, ans, true)}\n\n{string.Join("\n", msgs)}\n{Delta(before)}\n\n밤이 되었다. 바탕화면의 [SNS]를 열자.");
            if (S.Fat >= Balance.FatShaky) Tip("shaky", Manual.Shaky);
            MaybeError();
        }

        // ---------- SNS ----------

        void DrawSns()
        {
            int n = S.Replied.Count(r => r.HasValue);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(560));
            GUILayout.Label($"업로드 {(S.Uploaded ? 1 : 0)}/1 · 답장 {n}/3   — 오른쪽에서 얼굴을 만들고, 카드의 버튼을 누른다.", small);

            GUILayout.BeginVertical(card);
            string photo = S.Went is null or "room" ? "방 사진" : $"{PlaceById(S.Went).Name} 사진";
            GUILayout.Label($"<b>오늘의 업로드</b> · {photo}", label);
            if (S.Uploaded) GUILayout.Label("올림 완료", label);
            else
            {
                GUILayout.Label(S.Went is null or "room" ? "방 사진은 얼굴과 상관없이 조금만 오른다." : "사진의 분위기에 어울리는 얼굴을 달면 팔로워가 늘어난다.", small);
                Btn($"지금 얼굴 {Face(selEye, selMouth)} 로 올리기", Upload);
            }
            GUILayout.EndVertical();

            for (int i = 0; i < 3; i++) PostCard(i);
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical();
            Builder();
            Btn("해독 수첩 열기", () => { Open(Win.Notebook); Tip("notebook", Manual.Notebook); });
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        void PostCard(int i)
        {
            var post = S.TodayPosts[i];
            var tokens = post.Text.Split(' ');
            GUILayout.BeginVertical(S.Traits[Trait.Empathy] >= 1 ? tintCards[post.Emotion] : card);

            GUILayout.BeginHorizontal();
            foreach (var t in tokens)
            {
                if (!Sym.TryGetValue(t, out var e)) GUILayout.Label(t, postStyle, GUILayout.ExpandWidth(false));
                else if (S.Solved(t)) GUILayout.Label($"{t} {E[e].Name}", chips[e], GUILayout.ExpandWidth(false));
                else GUILayout.Label(t, symStyle, GUILayout.ExpandWidth(false));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label($"단서: {string.Join(" · ", post.Clues.Take(S.Traits[Trait.Decode] >= 1 ? 2 : 1))}", small);

            // 수첩에서 알아낸 것을 글 아래에 바로 보여준다
            var notes = tokens.Where(Sym.ContainsKey).Select(t =>
                S.Solved(t) ? $"<b>{t} = {E[Sym[t]].Name}</b>"
                : S.Stickers.TryGetValue(t, out var c) ? $"{t}에 {E[Sym[t]].Name} 스티커 {c}장"
                : $"{t} = ?").ToList();
            GUILayout.Label($"수첩 메모: {string.Join(", ", notes)}", small);

            var rep = S.Replied[i];
            if (rep.HasValue) GUILayout.Label($"↳ 답장 {rep.Value.face} — <b>{ResultName(rep.Value.r)}</b>", label);
            else Btn($"이 글에 {Face(selEye, selMouth)} 로 답장", () => Reply(i));
            GUILayout.EndVertical();
        }

        void Upload()
        {
            var before = Snap();
            var (r, _) = S.DoUpload(selEye, selMouth);
            Say("업로드 완료", (r.HasValue ? $"{Face(selEye, selMouth)} 를 단 사진이 올라갔다. — {ResultName(r.Value)}" : "방 사진을 올렸다.") + $"\n{Delta(before)}");
            AfterNightAction();
        }

        void Reply(int i)
        {
            var before = Snap();
            var post = S.TodayPosts[i];
            var (r, _, newly) = S.DoReply(i, selEye, selMouth);
            var body = $"{Fan(r)}\n\n{Why(r, selEye, selMouth, post.Emotion, false)}\n{Delta(before)}";
            if (r is Result.Perfect or Result.Success)
                body += $"\n\n이 글의 기호 {string.Join(" ", post.Text.Split(' ').Where(Sym.ContainsKey))} 에 {E[post.Emotion].Name} 스티커가 붙었다.";
            foreach (var s in newly)
                body += $"\n\n<b>해독 완료! {s} = {E[Sym[s]].Name}</b>\n이제 모든 팬 글에서 {s} 는 '{s} {E[Sym[s]].Name}' 스티커로 보인다.";
            Say($"팬의 반응 — {ResultName(r)}", body);
            if (newly.Count > 0) Tip("solve", Manual.Solve);
            MaybeError();
            AfterNightAction();
        }

        void AfterNightAction()
        {
            if (!S.NightDone || S.Ending != Ending.None) return;
            S.GrantNightPoint();
            traitsEndNight = true;
            Close(Win.Sns);
            Open(Win.Traits);
            Say("오늘 밤 끝", "업로드와 답장을 모두 마쳤다. 특성 포인트 +1\n\n특성 창에서 포인트를 쓰고 [잠들기]를 누르자.");
            Tip("traits", Manual.Traits);
        }

        // ---------- 카오모지 조립 바 (만남과 SNS 공용) ----------

        void Builder()
        {
            GUILayout.BeginVertical(card, GUILayout.Width(360));
            GUILayout.Label("<b>얼굴 조립</b>", label);
            PartRow("눈 — 감정의 본체", S.Eyes, true);
            PartRow("입 — 뉘앙스 (모르겠으면 중립)", S.Mouths, false);
            GUILayout.Label(Face(selEye, selMouth), faceStyle, GUILayout.Height(70));
            GUILayout.Label($"{E[selEye].Name} 눈 + {E[selMouth].Name} 입", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();
        }

        void PartRow(string title, List<Emo> owned, bool eye)
        {
            GUILayout.Label(title, small);
            GUILayout.BeginHorizontal();
            foreach (var e in PartOrder)
            {
                var opts = new[] { GUILayout.Width(52), GUILayout.Height(50) };
                if (!owned.Contains(e)) { GUI.enabled = false; GUILayout.Button("?\n<size=10>미획득</size>", btn, opts); GUI.enabled = true; continue; }
                bool sel = eye ? selEye == e : selMouth == e;
                var txt = $"{(eye ? E[e].EyeL + " " + E[e].EyeR : E[e].Mouth)}\n<size=10>{E[e].Name}</size>";
                if (GUILayout.Button(txt, sel ? selBtn : btn, opts) && !blocked) { if (eye) selEye = e; else selMouth = e; }
            }
            GUILayout.EndHorizontal();
        }

        // ---------- 수첩, 특성, 엔딩 ----------

        void DrawNotebook()
        {
            int need = S.Traits[Trait.Decode] >= 2 ? 1 : 2;
            GUILayout.Label($"규칙: 기호 하나는 감정 하나. 맞힌 답장마다 기호에 감정 스티커가 붙고, {need}장이면 해독된다.", small);
            GUILayout.Label($"해독한 기호 {S.SolvedCount}/{Sym.Count}", big);
            var seen = S.Seen.ToList();
            if (seen.Count == 0) GUILayout.Label("아직 본 기호가 없다. 밤에 SNS를 열면 기호가 모인다.", label);
            for (int i = 0; i < seen.Count; i += 5)
            {
                GUILayout.BeginHorizontal();
                foreach (var s in seen.Skip(i).Take(5))
                {
                    GUILayout.BeginVertical(paper, GUILayout.Width(104));
                    GUILayout.Label(s, new GUIStyle(symStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 30 });
                    if (S.Solved(s)) GUILayout.Label($"해독 {E[Sym[s]].Name}", chips[Sym[s]]);
                    else if (S.Stickers.TryGetValue(s, out var c)) GUILayout.Label($"{E[Sym[s]].Name} {new string('●', c)}{new string('○', need - c)}", chips[Sym[s]]);
                    else GUILayout.Label("? 스티커 없음", small);
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        void DrawTraits()
        {
            GUILayout.Label($"남은 포인트 <b>{S.Points}</b> · 특성마다 2레벨까지", label);
            TraitRow(Trait.Decode, "해독", "팬 글마다 단서가 하나 더 보임", "기호가 스티커 한 장으로 풀림");
            TraitRow(Trait.Mimic, "의태", "낮에 성공하면 파츠를 두 개 얻음", "의태 피로가 절반만 오름");
            TraitRow(Trait.Empathy, "공감", "팬 글 카드가 감정 색으로 물듦", "섬뜩이어도 의심도가 오르지 않음");
            if (traitsEndNight)
                Btn(S.Day >= Balance.Days ? "<b>잠들기 → 결말</b>" : $"<b>잠들기 → {S.Day + 1}일째 아침</b>", () => Close(Win.Traits), true, null, GUILayout.Height(40));
        }

        void TraitRow(Trait t, string name, string l1, string l2)
        {
            int lv = S.Traits[t];
            GUILayout.BeginVertical(card);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{name}</b>  {new string('●', lv)}{new string('○', 2 - lv)}", big);
            GUILayout.FlexibleSpace();
            Btn("올리기", () => S.Raise(t), S.CanRaise(t), null, GUILayout.Width(90));
            GUILayout.EndHorizontal();
            GUILayout.Label($"{(lv >= 1 ? "■" : "□")} 1레벨: {l1}\n{(lv >= 2 ? "■" : "□")} 2레벨: {l2}", small);
            GUILayout.EndVertical();
        }

        void DrawEnding()
        {
            var (title, text) = Endings[S.Ending];
            GUILayout.Space(20);
            GUILayout.Label(title, new GUIStyle(big) { fontSize = 30, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);
            GUILayout.Label(text, new GUIStyle(label) { fontSize = 17, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);
            GUILayout.Label($"팔로워 {S.Followers} · 의심도 {Mathf.RoundToInt(S.Sus)} · 해독 {S.SolvedCount}/{Sym.Count}", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(16);
            Btn("<b>처음부터</b>", Restart, true, null, GUILayout.Height(40));
        }

        // ---------- 결과 문구 ----------

        (int f, float s, float fat) Snap() => (S.Followers, S.Sus, S.Fat);

        string Delta((int f, float s, float fat) b)
        {
            var parts = new List<string>();
            void Add(string n, float d) { if (Mathf.RoundToInt(d) != 0) parts.Add($"{n} {(d > 0 ? "+" : "")}{Mathf.RoundToInt(d)}"); }
            Add("팔로워", S.Followers - b.f);
            Add("의심도", S.Sus - b.s);
            Add("의태 피로", S.Fat - b.fat);
            return parts.Count > 0 ? $"<color=#7a6a8a>({string.Join(", ", parts)})</color>" : "";
        }

        static string Why(Result r, Emo eye, Emo mouth, Emo ans, bool reveal) => r switch
        {
            Result.Perfect => $"눈과 입이 모두 {E[ans].Name}. 완벽한 의태!",
            Result.Success => $"눈이 {E[ans].Name}, 입은 중립. 무난하게 통했다.",
            Result.Creepy => $"눈은 {E[ans].Name}인데 입이 {E[mouth].Name}. 사람 얼굴로는 어딘가 이상하다.",
            Result.Meh => "눈이 중립이라 감정이 전해지지 않았다.",
            _ => reveal ? $"눈이 {E[eye].Name}였지만, 상대가 기대한 건 {E[ans].Name}였다." : $"눈이 {E[eye].Name}였지만, 팬이 기대한 감정이 아니었다.",
        };

        static string Fan(Result r) => r switch
        {
            Result.Perfect => "팬: (꺄아아!!)", Result.Success => "팬: (좋아해 줬다)", Result.Creepy => "팬: (…무서운데 계속 보게 돼)",
            Result.Meh => "팬: (읽씹당한 기분)", _ => "팬: (??? 뭐임)",
        };

        // ---------- 인외 연출: 에러 팝업 ----------

        static readonly string[] ErrSus = { "알 수 없는 시선이 감지되었습니다.", "경고: 표정 모듈이 응답하지 않습니다.", "피부.exe 가 예기치 않게 종료되었습니다.", "촉수를 숨기시겠습니까?" };
        static readonly string[] ErrFat = { "의태 드라이버가 과열되었습니다.", "눈 파츠를 찾을 수 없습니다. 대체 눈을 사용합니다.", "얼굴이 흐르고 있습니다. (^▽T)", "ㅇ ㅏ ㄴ ㄴ ㅕ ㅇ 하 세 요" };

        void MaybeError()
        {
            float sus = S.Sus / 100f, fat = S.Fat / 100f, heat = Mathf.Max(sus, fat);
            if (heat < 0.5f || UnityEngine.Random.value > heat) return;
            var pool = sus >= fat ? ErrSus : ErrFat;
            int i = Mathf.Min(pool.Length - 1, (int)((heat - 0.5f) * 2 * pool.Length));
            popups.Enqueue(new Popup { Title = "시스템 오류", Body = Glitch(pool[i], (heat - 0.5f) * 0.6f), Error = true, Ok = heat > 0.8f ? "예" : "확인" });
            Tip("error", Manual.Error);
        }

        static string Glitch(string s, float p) =>
            new string(s.Select(c => c != ' ' && UnityEngine.Random.value < p ? "■▒▓#"[UnityEngine.Random.Range(0, 4)] : c).ToArray());

        // ---------- 스타일 ----------

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        Texture2D Tex(string hex)
        {
            if (texCache.TryGetValue(hex, out var t)) return t;
            t = new Texture2D(1, 1);
            t.SetPixel(0, 0, C(hex));
            t.Apply();
            return texCache[hex] = t;
        }

        // 윈도우 98식 볼록 테두리: 위·왼쪽 밝게, 아래·오른쪽 어둡게
        static Texture2D Bevel(string face, string light, string dark)
        {
            var t = new Texture2D(4, 4) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    t.SetPixel(x, y, C(x == 0 || y == 3 ? light : x == 3 || y == 0 ? dark : face));
            t.Apply();
            return t;
        }

        static Texture2D Gradient(string a, string b, bool vertical)
        {
            var t = new Texture2D(vertical ? 1 : 2, vertical ? 2 : 1) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            t.SetPixel(0, 0, C(vertical ? b : a));
            t.SetPixel(vertical ? 0 : 1, vertical ? 1 : 0, C(vertical ? a : b));
            t.Apply();
            return t;
        }

        static GUIStyle Boxed(Texture2D bg, int pad = 10)
        {
            var s = new GUIStyle { border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(pad, pad, pad, pad), margin = new RectOffset(4, 4, 4, 4) };
            s.normal.background = s.onNormal.background = bg;
            return s;
        }

        void InitStyles()
        {
            if (btn != null) return;
            GUI.skin.font = font;
            var ink = C("#4a3a5a");
            GUIStyle Text(int size, Color col, FontStyle fs = FontStyle.Normal)
            {
                var s = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true, wordWrap = true, fontStyle = fs };
                s.normal.textColor = col;
                return s;
            }

            label = Text(15, ink);
            small = Text(13, C("#7a6a8a"));
            big = Text(19, ink, FontStyle.Bold);
            faceStyle = Text(46, ink); faceStyle.alignment = TextAnchor.MiddleCenter;
            postStyle = Text(22, ink); postStyle.wordWrap = false;
            symStyle = Text(24, C("#7b4fe0"), FontStyle.Bold); symStyle.wordWrap = false;

            bgDay = Gradient("#bde0fe", "#ffc8dd", true);
            bgNight = Gradient("#2b2540", "#6a4c93", true);

            winStyle = Boxed(Bevel("#fff4f8", "#ffffff", "#b48aa0"));
            winStyle.padding = new RectOffset(12, 12, 38, 12);

            titleStyle = new GUIStyle { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 30, 0, 0) };
            titleStyle.normal.background = Gradient("#ff8fb3", "#8ecdf0", false);
            titleStyle.normal.textColor = Color.white;
            errTitleStyle = new GUIStyle(titleStyle);
            errTitleStyle.normal.background = Gradient("#e0344f", "#ffb3c1", false);

            btn = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true, wordWrap = true, border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(8, 8, 6, 6) };
            btn.normal.background = Bevel("#ffe3ee", "#ffffff", "#c08aa5");
            btn.hover.background = Bevel("#fff0f6", "#ffffff", "#c08aa5");
            btn.active.background = Bevel("#ffd3e4", "#c08aa5", "#ffffff");
            btn.focused.background = btn.normal.background;
            btn.normal.textColor = btn.hover.textColor = btn.active.textColor = btn.focused.textColor = ink;
            selBtn = new GUIStyle(btn) { fontStyle = FontStyle.Bold };
            selBtn.normal.background = selBtn.hover.background = Bevel("#8ecdf0", "#d6efff", "#4a8db5");
            selBtn.normal.textColor = selBtn.hover.textColor = Color.white;
            closeBtn = new GUIStyle(btn) { padding = new RectOffset(0, 0, 0, 2), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            card = Boxed(Bevel("#ffffff", "#ffffff", "#d9c2d0"));
            paper = Boxed(Bevel("#fffbe8", "#ffffff", "#d8c79a"));
            panel = Boxed(Bevel("#fff4f8", "#ffffff", "#b48aa0"), 12);
            bubble = Boxed(Bevel("#eef7ff", "#ffffff", "#9cc3e0"));
            bubble.fontSize = 22; bubble.normal.textColor = ink; bubble.alignment = TextAnchor.MiddleCenter;
            taskbar = Boxed(Bevel("#ffc2d9", "#ffe3ee", "#d98fb0"), 0);

            iconStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = true };
            iconStyle.hover.background = Tex("#ffffff55");

            foreach (var e in PartOrder)
            {
                var chip = new GUIStyle(label) { fontSize = 16, fontStyle = FontStyle.Bold, wordWrap = false, padding = new RectOffset(6, 6, 3, 3), margin = new RectOffset(2, 2, 4, 4), alignment = TextAnchor.MiddleCenter };
                chip.normal.background = Tex(E[e].Color);
                chips[e] = chip;
                tintCards[e] = Boxed(Bevel(ColorUtility.ToHtmlStringRGB(Color.Lerp(Color.white, C(E[e].Color), 0.3f)).Insert(0, "#"), "#ffffff", "#d9c2d0"));
            }
        }

        // ---------- 튜토리얼 문구 ----------

        static class Manual
        {
            public static readonly string Intro =
                "안녕, 요원. 너는 지구에서 인플루언서로 위장한 외계인이다.\n" +
                $"7일 동안 들키지 않고 팔로워를 {Balance.FollowerGoal}명 모아라.\n\n" +
                "· 낮: [외출]해서 사람들의 표정을 배우거나, [휴식]한다.\n" +
                "· 밤: [SNS]에 사진을 올리고, 읽을 수 없는 팬 글 3개에 카오모지로 답한다.\n\n" +
                "오른쪽 위 수치를 잘 보자.\n" +
                "· 의심도가 100이 되면 정체가 들킨다.\n" +
                "· 의태 피로가 80을 넘으면 얼굴이 멋대로 바뀐다.\n\n" +
                "먼저 왼쪽의 [외출] 아이콘을 눌러 보자.";

            public static readonly string Outing =
                "장소를 하나 고르자.\n장소마다 일하는 사람이 있고, 그 사람의 표정을 따라 하면 새 얼굴 파츠를 배운다.\n\n어디서 어떤 감정을 배우는지는 직접 알아내야 한다.";

            public static readonly string Encounter =
                "카오모지는 [눈]과 [입] 두 파츠로 만든다.\n· 눈 = 감정의 본체\n· 입 = 뉘앙스. 모르겠으면 중립 _ 이 안전하다.\n\n" +
                "판정\n· 완벽: 눈과 입 모두 맞음\n· 성공: 눈이 맞고 입은 중립\n· 섬뜩: 눈은 맞는데 입이 다른 감정 (인외의 얼굴!)\n· 시큰둥: 눈이 중립\n· 오답: 눈이 틀림\n\n" +
                "처음 보는 사람에겐 웃는 얼굴로 인사하는 게 지구의 예의라고 한다.";

            public static readonly string Sns =
                "밤의 SNS.\n\n" +
                "1) 업로드: 오늘 간 장소 사진에 어울리는 얼굴을 달아 올린다.\n" +
                "2) 팬 글 답장: 팬 글은 대부분 ■(못 읽는 글자)다. <color=#7b4fe0><b>보라색 기호</b></color>가 감정을 담은 '감정 기호'.\n" +
                "    사진, 반응, 시각 같은 단서로 감정을 추측해 얼굴로 답하자.\n\n" +
                "오른쪽에서 얼굴을 조립하고, 카드의 [답장] 버튼을 누르면 된다.\n" +
                "맞히면(완벽·성공) 그 글의 기호에 감정 스티커가 붙고, 2장 모이면 기호가 해독된다.";

            public static readonly string Solve =
                "기호가 해독됐다!\n\n이제 팬 글에서 그 기호는 색깔 스티커로 '☆ 기쁨'처럼 뜻과 함께 보인다.\n" +
                "해독된 기호가 든 글은 그 감정으로 답하면 맞을 가능성이 높다.\n\n규칙은 하나뿐: 기호 하나는 감정 하나.";

            public static readonly string Notebook =
                "해독 수첩에는 지금까지 본 감정 기호가 모인다.\n맞힌 답장마다 기호에 감정 스티커가 붙고, 스티커가 다 차면 해독 완료.\n\n밤에 답장하다 막히면 수첩을 열어 보자.";

            public static readonly string Traits =
                "하루가 끝나면 특성 포인트 1을 받는다.\n" +
                "· 해독: 단서가 늘고, 기호가 더 빨리 풀린다\n· 의태: 파츠를 더 많이 배우고, 피로가 덜 쌓인다\n· 공감: 팬 글의 감정이 색으로 보이고, 섬뜩해도 의심받지 않는다\n\n" +
                "7일 동안 7포인트라 전부 찍을 수는 없다. 다 골랐으면 [잠들기].";

            public static readonly string Shaky = "의태 피로가 80을 넘었다.\n이제 낮에 얼굴이 멋대로 바뀔 수 있다. [휴식]하면 피로가 내려간다.";

            public static readonly string Error = "이런 오류 창은 의심도나 의태 피로가 높을 때 뜬다.\n문구가 이상해질수록 위험하다는 뜻이다. [휴식]으로 수치를 낮추자.";

            public static readonly string Help =
                "하루 = 낮 한 번 + 밤 한 번, 모두 7일.\n\n" +
                "낮: [외출] 장소에서 만남 → 얼굴 파츠 획득 / [휴식] 피로·의심도 내림\n" +
                "밤: [SNS] 업로드 1개, 팬 글 답장 3개 → 특성 포인트 → 잠들기\n\n" +
                "판정: 완벽(눈·입 정답) · 성공(눈 정답, 입 중립) · 섬뜩(눈 정답, 입 다른 감정) · 시큰둥(눈 중립) · 오답(눈 틀림)\n\n" +
                "엔딩: 의심도 100이면 즉시 발각. 7일째 밤에 팔로워·의심도·해독 수로 결말이 갈린다.";
        }
    }
}
