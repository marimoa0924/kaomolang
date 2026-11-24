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
            public Action OnOk;
        }

        class Toast { public string Title, Body; public float T0; public int Id; }

        // 튜토리얼 한 단계: Target을 가리키고, Done이 참이 되거나 [다음]을 누르면 넘어간다
        class Step
        {
            public string Target, Text;
            public Func<bool> Done;
            public bool Next;
            public Step(string target, string text, Func<bool> done, bool next = false) { Target = target; Text = text; Done = done; Next = next; }
        }

        const float VW = 1280, VH = 720, BarH = 38, PopupW = 420, CoachW = 300, ToastW = 330, ToastH = 84;
        const int PopupId = 99, CoachId = 98;
        static readonly Dictionary<Win, float> WinWidth = new()
        {
            [Win.Places] = 560, [Win.Encounter] = 820, [Win.Sns] = 960, [Win.Notebook] = 600, [Win.Traits] = 540, [Win.Ending] = 560,
        };
        static readonly Emo[] PartOrder = { Emo.Neutral, Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };

        GameState S = new();
        readonly List<Win> open = new();
        readonly Dictionary<Win, Rect> rects = new();
        readonly Queue<Popup> popups = new();
        readonly List<Toast> toasts = new();
        readonly HashSet<string> tips = new();
        Rect popupRect = new((VW - PopupW) / 2, 170, PopupW, 0), coachRect = new(0, 0, CoachW, 0);
        Action pending;       // 버튼 동작은 다음 Update에서 실행해 IMGUI 레이아웃 불일치를 피한다
        int? focusReq;
        int toastSeq;
        string place;
        bool traitsEndNight, blocked;
        Emo selEye = Emo.Neutral, selMouth = Emo.Neutral;

        List<Step> steps;
        int step;
        Dictionary<string, Rect> anchors = new(), lastAnchors = new();
        Vector2 origin; // 지금 그리는 창의 화면 위치. Mark가 창 안 좌표를 화면 좌표로 바꿀 때 쓴다

        Font font;
        GUIStyle label, small, big, faceStyle, postStyle, symStyle, winStyle, titleStyle, errTitleStyle, toastStyle,
                 btn, closeBtn, card, paper, bubble, iconStyle, taskbar, panel, coachStyle, hiliteStyle;
        readonly Dictionary<string, Texture2D> texCache = new();
        readonly Dictionary<Emo, GUIStyle> chips = new(), tintCards = new(), partBtn = new(), partSel = new();
        Texture2D bgDay, bgNight;

        Step Cur => steps != null && step < steps.Count ? steps[step] : null;
        bool CoachVisible => Cur != null && popups.Count == 0 && S.Ending == Ending.None;

        // ---------- 수명 주기 ----------

        void Awake()
        {
            // 한글과 카오모지 글리프가 있는 OS 폰트
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial Unicode MS" }, 16);
            Say("모행성 매뉴얼.txt", "너는 인플루언서로 위장한 외계인.\n7일 동안 들키지 말고 팔로워를 모으자.", "시작");
            steps = new List<Step>
            {
                new("icon_outing", "낮이다.\n[외출]을 눌러 보자.", () => open.Contains(Win.Places) || open.Contains(Win.Encounter) || S.IsNight),
                new("place_0", "장소를 하나 골라 보자.\n장소마다 배울 표정이 다르다.", () => open.Contains(Win.Encounter) || S.IsNight),
                new("situation", "상대의 상황이다.\n어떤 기분일까?", () => S.IsNight, true),
                new("eyes", "눈 = 감정.\n웃는 눈 ^ ^ 을 골라 보자.", () => selEye == Emo.Joy || S.IsNight),
                new("mouths", "입 = 뉘앙스.\n모르겠으면 중립 _ 이 안전하다.", () => S.IsNight, true),
                new("react", "반응하자!", () => S.IsNight),
                new("stats", "결과는 여기 쌓인다.\n의심도 100이면 들킨다.", () => open.Contains(Win.Sns), true),
                new("legend", "감정마다 색이 있다.\n스티커와 카드 색이 이걸 따른다.", () => open.Contains(Win.Sns), true),
                new("icon_sns", "밤이다.\n[SNS]를 열자.", () => open.Contains(Win.Sns) || S.NightDone),
                new("post_0", "팬 글이다.\n■는 못 읽는 글자.", () => S.Replied[0].HasValue || S.Day > 1, true),
                new("sym_0", "이 기호에 감정이 담겨 있다.", () => S.Replied[0].HasValue || S.Day > 1, true),
                new("clue_0", "단서를 보자.\n케이크 사진이면…?", () => S.Replied[0].HasValue || S.Day > 1, true),
                new("reply_0", "얼굴을 만들고\n이 버튼으로 답장!", () => S.Replied[0].HasValue || S.Day > 1),
                new("upload", "사진도 올리자.\n오늘 간 곳에 어울리는 얼굴로.", () => S.Uploaded || S.Day > 1),
                new("feed", "남은 글에도 답장하자.", () => S.NightDone || S.Day > 1),
                new("trait_0", "포인트로 특성을 올리자.", () => S.Points == 0 || S.Day > 1),
                new("sleep", "잠들면 다음 날이다.", () => S.Day > 1),
                new("icon_notebook", "맞힌 기호는\n[해독 수첩]에 모인다.", null, true),
                new("icon_rest", "피곤하면 [휴식]도 있다.\n이제 혼자 해 보자!", null, true),
            };
        }

        void Update()
        {
            if (pending != null) { var a = pending; pending = null; a(); }
            while (Cur?.Done?.Invoke() == true) step++;
            if (S.Ending != Ending.None && !open.Contains(Win.Ending))
            {
                open.Clear(); popups.Clear();
                Open(Win.Ending);
            }
        }

        void OnGUI()
        {
            InitStyles();
            if (Event.current.type == EventType.Repaint) { (lastAnchors, anchors) = (anchors, lastAnchors); anchors.Clear(); }
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), S.IsNight ? bgNight : bgDay);
            float k = Mathf.Min(Screen.width / VW, Screen.height / VH);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - VW * k) / 2, (Screen.height - VH * k) / 2, 0), Quaternion.identity, new Vector3(k, k, 1));

            blocked = popups.Count > 0;
            DrawIcons();
            DrawWidgets();
            DrawTaskbar();

            foreach (var w in open.ToList())
                rects[w] = GUILayout.Window((int)w + 1, rects[w], _ => DrawWin(w), GUIContent.none, winStyle, GUILayout.Width(WinWidth[w]));

            if (CoachVisible)
            {
                PlaceCoach();
                coachRect = GUILayout.Window(CoachId, coachRect, _ => DrawCoach(), GUIContent.none, coachStyle, GUILayout.Width(CoachW));
                GUI.BringWindowToFront(CoachId);
            }

            DrawToasts();

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
                if (S.Ending == Ending.None) Say($"{S.Day}일째 아침", $"남은 날: {Balance.Days - S.Day + 1}일");
            }
        }

        void Say(string title, string body, string ok = "확인", Action onOk = null) =>
            popups.Enqueue(new Popup { Title = title, Body = body, Ok = ok, OnOk = onOk });

        void Tip(string key, string body)
        {
            if (tips.Add(key)) Say("모행성 매뉴얼.txt", body, "알겠어");
        }

        void Restart()
        {
            S = new GameState();
            open.Clear(); popups.Clear(); toasts.Clear();
            traitsEndNight = false;
            selEye = selMouth = Emo.Neutral;
            Say("1일째 아침", "다시 지구에 내려왔다.");
        }

        // ---------- 튜토리얼 코치 ----------

        // 그린 직후 호출: 이 요소의 화면 위치를 기록하고, 튜토리얼이 가리키는 중이면 반짝이는 테두리를 씌운다
        void Mark(string key) => MarkRect(key, GUILayoutUtility.GetLastRect(), origin);

        void MarkRect(string key, Rect r, Vector2 o)
        {
            if (Event.current.type != EventType.Repaint) return;
            anchors[key] = new Rect(r.x + o.x, r.y + o.y, r.width, r.height);
            if (!CoachVisible || Cur.Target != key) return;
            GUI.color = new Color(1, 1, 1, 0.55f + 0.45f * Mathf.Sin(Time.time * 6));
            GUI.Box(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), GUIContent.none, hiliteStyle);
            GUI.color = Color.white;
        }

        void PlaceCoach()
        {
            float h = Mathf.Max(coachRect.height, 90);
            if (!lastAnchors.TryGetValue(Cur.Target, out var a)) { coachRect.x = (VW - CoachW) / 2; coachRect.y = 70; return; }
            // 대상 아래, 자리가 없으면 위, 그것도 안 되면 오른쪽
            coachRect.x = Mathf.Clamp(a.x, 8, VW - CoachW - 8);
            coachRect.y = a.yMax + 12;
            if (coachRect.y + h > VH - BarH) coachRect.y = a.y - h - 12;
            if (coachRect.y < 4) { coachRect.x = Mathf.Min(a.xMax + 12, VW - CoachW - 8); coachRect.y = Mathf.Clamp(a.y, 4, VH - BarH - h); }
        }

        void DrawCoach()
        {
            blocked = false;
            GUILayout.Label(Cur.Text, new GUIStyle(label) { fontSize = 17, fontStyle = FontStyle.Bold });
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{step + 1}/{steps.Count}", small, GUILayout.ExpandWidth(false));
            if (GUILayout.Button("건너뛰기", small, GUILayout.ExpandWidth(false))) pending = () => step = steps.Count;
            GUILayout.FlexibleSpace();
            if (Cur.Next) Btn("<b>다음 ▶</b>", () => step++, true, null, GUILayout.Width(90));
            GUILayout.EndHorizontal();
        }

        // ---------- 경고 토스트: 우측 하단에서 올라왔다가 내려간다 ----------

        const float ToastRise = 0.35f, ToastHold = 3.5f;

        void DrawToasts()
        {
            toasts.RemoveAll(t => Time.time - t.T0 > ToastRise * 2 + ToastHold);
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                float age = Time.time - t.T0;
                float p = age < ToastRise ? age / ToastRise : age < ToastRise + ToastHold ? 1 : 1 - (age - ToastRise - ToastHold) / ToastRise;
                p = 1 - (1 - p) * (1 - p); // ease-out
                float shown = VH - BarH - 10 - ToastH - (toasts.Count - 1 - i) * (ToastH + 8);
                var r = new Rect(VW - ToastW - 12, Mathf.Lerp(VH, shown, p), ToastW, ToastH);
                GUI.Window(t.Id, r, _ =>
                {
                    GUI.Label(new Rect(4, 4, ToastW - 8, 24), t.Title, errTitleStyle);
                    GUI.Label(new Rect(12, 32, ToastW - 24, ToastH - 36), t.Body, label);
                }, GUIContent.none, toastStyle);
                GUI.BringWindowToFront(t.Id);
            }
        }

        static readonly string[] ErrSus = { "알 수 없는 시선이 감지되었습니다.", "표정 모듈이 응답하지 않습니다.", "피부.exe 가 예기치 않게 종료되었습니다.", "촉수를 숨기시겠습니까?" };
        static readonly string[] ErrFat = { "의태 드라이버가 과열되었습니다.", "얼굴 온도가 위험 수준입니다.", "얼굴이 흐르고 있습니다. (^▽T)", "ㅇ ㅏ ㄴ ㄴ ㅕ ㅇ 하 세 요" };

        // 인외 연출: 수치가 높을수록 자주, 더 이상한 문구로. 실제 효과는 없고 경고등 역할만 한다
        void MaybeError()
        {
            float sus = S.Sus / 100f, fat = S.Fat / 100f, heat = Mathf.Max(sus, fat);
            if (heat < 0.5f || UnityEngine.Random.value > heat) return;
            bool bySus = sus >= fat;
            var pool = bySus ? ErrSus : ErrFat;
            int i = Mathf.Min(pool.Length - 1, (int)((heat - 0.5f) * 2 * pool.Length));
            var why = bySus ? $"의심도 {Mathf.RoundToInt(S.Sus)} — 조심하자" : $"의태 피로 {Mathf.RoundToInt(S.Fat)} — 쉬어야 한다";
            toasts.Add(new Toast
            {
                Title = "시스템 오류", T0 = Time.time, Id = 200 + toastSeq++ % 50,
                Body = $"{Glitch(pool[i], (heat - 0.5f) * 0.6f)}\n<color=#9a8aa8><size=12>{why}</size></color>",
            });
        }

        static string Glitch(string s, float p) =>
            new string(s.Select(c => c != ' ' && UnityEngine.Random.value < p ? "■▒▓#"[UnityEngine.Random.Range(0, 4)] : c).ToArray());

        // ---------- 바탕화면 ----------

        void DrawIcons()
        {
            var icons = new (string key, string glyph, string name, Action act, bool on)[]
            {
                ("icon_outing", "☞", "외출", ClickOuting, !S.IsNight),
                ("icon_rest", "♨", "휴식", ClickRest, !S.IsNight),
                ("icon_sns", "♥", "SNS", ClickSns, S.IsNight),
                ("icon_notebook", "▤", "해독 수첩", () => Open(Win.Notebook), true),
                ("icon_traits", "★", S.Points > 0 ? $"특성 ({S.Points})" : "특성", () => Open(Win.Traits), true),
            };
            iconStyle.normal.textColor = iconStyle.hover.textColor = S.IsNight ? Color.white : C("#4a3a5a");
            for (int i = 0; i < icons.Length; i++)
            {
                var (key, glyph, name, act, on) = icons[i];
                var r = new Rect(24, 24 + i * 104, 92, 96);
                GUI.color = new Color(1, 1, 1, on ? 1 : 0.45f); // 못 쓰는 아이콘도 눌러서 이유를 알 수 있게 흐리게만
                if (GUI.Button(r, $"<size=38>{glyph}</size>\n{name}", iconStyle) && !blocked) pending = act;
                GUI.color = Color.white;
                MarkRect(key, r, Vector2.zero);
            }
        }

        void ClickOuting()
        {
            if (S.IsNight) { Say("지금은 밤", "외출은 낮에만 할 수 있다."); return; }
            Open(Win.Places);
        }

        void ClickRest()
        {
            if (S.IsNight) { Say("지금은 밤", "휴식은 낮에 고른다."); return; }
            popups.Enqueue(new Popup
            {
                Title = "휴식", Ok = "쉰다", Cancel = "취소",
                Body = $"방에서 쉬고 바로 밤이 된다.\n의태 피로 {Balance.RestFat}, 의심도 {Balance.RestSus}\n새 파츠는 얻지 못한다.",
                OnOk = () =>
                {
                    var before = Snap();
                    S.DoRest();
                    Say("밤이 되었다", $"푹 쉬었다.\n{Delta(before)}");
                },
            });
        }

        void ClickSns()
        {
            if (!S.IsNight) { Say("아직 낮", "SNS는 밤에 열린다.\n먼저 외출하거나 쉬자."); return; }
            S.MarkSeen();
            Open(Win.Sns);
        }

        void DrawWidgets()
        {
            origin = new Vector2(VW - 344, 20);
            GUILayout.BeginArea(new Rect(origin.x, origin.y, 324, 600));
            GUILayout.BeginVertical(panel);
            GUILayout.Label("상태", big);
            Bar("팔로워", S.Followers, Balance.FollowerGoal, "#a68bff", $"{S.Followers}/{Balance.FollowerGoal}");
            Bar("의심도", S.Sus, 100, "#ff5a5a", $"{Mathf.RoundToInt(S.Sus)}");
            Bar("의태 피로", S.Fat, 100, S.Fat >= Balance.FatShaky ? "#e0344f" : "#f4a261", $"{Mathf.RoundToInt(S.Fat)}");
            GUILayout.EndVertical();
            Mark("stats");

            GUILayout.BeginVertical(panel);
            GUILayout.Label("감정 색", big);
            Legend();
            GUILayout.EndVertical();
            Mark("legend");

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

        // 감정 이름과 색, 대표 얼굴을 한 줄로
        void Legend()
        {
            GUILayout.BeginHorizontal();
            foreach (var e in Feelings)
                GUILayout.Label($"{E[e].Name}\n<size=12>{Face(e, e)}</size>", chips[e], GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
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
                pending = () => Say("게임 방법", Help);
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

        const string Help =
            "낮: [외출] 표정 배우기 / [휴식] 피로·의심도 내리기\n" +
            "밤: [SNS] 업로드 1개 + 팬 글 답장 3개\n\n" +
            "눈 = 감정, 입 = 뉘앙스\n" +
            "완벽: 눈·입 정답 / 성공: 눈 정답, 입 중립\n섬뜩: 눈 정답, 입 다른 감정 / 오답: 눈 틀림";

        string Title(Win w) => w switch
        {
            Win.Places => "외출", Win.Encounter => place != null ? $"{PlaceById(place).Name} — {PlaceById(place).Npc}" : "만남",
            Win.Sns => $"SNS — {S.Day}일째 밤", Win.Notebook => "해독 수첩", Win.Traits => "특성", _ => "엔딩",
        };

        // ---------- 창 공통 ----------

        void TitleBar(string title, float width, Action onClose)
        {
            GUI.Label(new Rect(4, 4, width - 8, 26), title, titleStyle);
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
            origin = rects[w].position;
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
            TitleBar(p.Title, popupRect.width, null);
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
            var list = GameData.Places;
            for (int i = 0; i < list.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < list.Length; j++)
                {
                    var p = list[j];
                    int aff = S.Aff.GetValueOrDefault(p.Id), n = S.Visits.GetValueOrDefault(p.Id);
                    Btn($"<size=18><b>{p.Name}</b></size>\n{p.Npc}\n<size=12>가본 횟수 {n} · 호감 {(aff > 0 ? new string('♥', aff) : "-")}</size>",
                        () => { place = p.Id; selEye = selMouth = Emo.Neutral; Close(Win.Places); Open(Win.Encounter); },
                        true, null, GUILayout.Height(96));
                    Mark($"place_{j}");
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
            GUILayout.Label(ev.Situation, label);
            GUILayout.Space(6);
            GUILayout.Label($"“{ev.Say}”", bubble);
            GUILayout.EndVertical();
            Mark("situation");
            if (S.Fat >= Balance.FatShaky) GUILayout.Label("<color=#e0344f><b>의태 피로가 높다. 얼굴이 멋대로 바뀔지도.</b></color>", label);
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical();
            Builder();
            Btn("<b>이 표정으로 반응하기</b>", React, true, null, GUILayout.Height(40));
            Mark("react");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        void React()
        {
            var before = Snap();
            var p = PlaceById(place);
            var (r, ans, msgs, eye, mouth) = S.DoEncounter(place, selEye, selMouth);
            Close(Win.Encounter);
            Say($"{p.Npc}의 반응 — {ResultName(r)}", $"{Why(r, eye, mouth, ans, true)}\n{string.Join("\n", msgs)}\n{Delta(before)}\n\n밤이 되었다.");
            if (S.Fat >= Balance.FatShaky) Tip("shaky", "의태 피로 80 이상.\n낮에 얼굴이 멋대로 바뀔 수 있다.\n[휴식]하면 내려간다.");
            MaybeError();
        }

        // ---------- SNS ----------

        void DrawSns()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(560));
            GUILayout.BeginVertical(card);
            string photo = S.Went is null or "room" ? "방 사진" : $"{PlaceById(S.Went).Name} 사진";
            GUILayout.Label($"<b>오늘의 업로드</b> · {photo}", label);
            if (S.Uploaded) GUILayout.Label("올림 완료", label);
            else Btn($"지금 얼굴 {Face(selEye, selMouth)} 로 올리기", Upload);
            GUILayout.EndVertical();
            Mark("upload");

            GUILayout.BeginVertical();
            for (int i = 0; i < 3; i++) PostCard(i);
            GUILayout.EndVertical();
            Mark("feed");
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical();
            Builder();
            Btn("해독 수첩 열기", () => Open(Win.Notebook));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        void PostCard(int i)
        {
            var post = S.TodayPosts[i];
            var tokens = post.Text.Split(' ');
            bool empathy = S.Traits[Trait.Empathy] >= 1;
            GUILayout.BeginVertical(empathy ? tintCards[post.Emotion] : card);

            GUILayout.BeginHorizontal();
            bool firstSym = true;
            foreach (var t in tokens)
            {
                if (!Sym.TryGetValue(t, out var e)) { GUILayout.Label(t, postStyle, GUILayout.ExpandWidth(false)); continue; }
                if (S.Solved(t)) GUILayout.Label($"{t} {E[e].Name}", chips[e], GUILayout.ExpandWidth(false));
                else GUILayout.Label(t, symStyle, GUILayout.ExpandWidth(false));
                if (firstSym) { Mark($"sym_{i}"); firstSym = false; }
            }
            GUILayout.FlexibleSpace();
            if (empathy) GUILayout.Label("공감: 이 색의 느낌", small, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            GUILayout.Label($"단서: {string.Join(" · ", post.Clues.Take(S.Traits[Trait.Decode] >= 1 ? 2 : 1))}", small);
            Mark($"clue_{i}");

            // 수첩에서 알아낸 것을 글 아래에 바로 보여준다
            var notes = tokens.Where(Sym.ContainsKey).Select(t =>
                S.Solved(t) ? $"<b>{t} = {E[Sym[t]].Name}</b>"
                : S.Stickers.TryGetValue(t, out var c) ? $"{t}에 {E[Sym[t]].Name} 스티커 {c}장"
                : $"{t} = ?").ToList();
            GUILayout.Label($"수첩 메모: {string.Join(", ", notes)}", small);

            var rep = S.Replied[i];
            if (rep.HasValue) GUILayout.Label($"↳ 답장 {rep.Value.face} — <b>{ResultName(rep.Value.r)}</b>", label);
            else { Btn($"이 글에 {Face(selEye, selMouth)} 로 답장", () => Reply(i)); Mark($"reply_{i}"); }
            GUILayout.EndVertical();
            Mark($"post_{i}");
        }

        void Upload()
        {
            var before = Snap();
            var (r, _) = S.DoUpload(selEye, selMouth);
            Say("업로드 완료", (r.HasValue ? $"{Face(selEye, selMouth)} — {ResultName(r.Value)}" : "방 사진을 올렸다.") + $"\n{Delta(before)}");
            AfterNightAction();
        }

        void Reply(int i)
        {
            var before = Snap();
            var post = S.TodayPosts[i];
            var (r, _, newly) = S.DoReply(i, selEye, selMouth);
            var body = $"{Fan(r)}\n{Why(r, selEye, selMouth, post.Emotion, false)}\n{Delta(before)}";
            if (r is Result.Perfect or Result.Success)
                body += $"\n\n{string.Join(" ", post.Text.Split(' ').Where(Sym.ContainsKey))} 에 {E[post.Emotion].Name} 스티커!";
            foreach (var s in newly) body += $"\n<b>해독 완료! {s} = {E[Sym[s]].Name}</b>";
            Say($"팬의 반응 — {ResultName(r)}", body);
            if (newly.Count > 0) Tip("solve", "해독한 기호는 이제\n색 스티커로 뜻과 함께 보인다.");
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
            Say("오늘 밤 끝", "특성 포인트 +1");
        }

        // ---------- 카오모지 조립 바 (만남과 SNS 공용) ----------

        void Builder()
        {
            GUILayout.BeginVertical(card, GUILayout.Width(360));
            PartRow("눈 = 감정", S.Eyes, true);
            Mark("eyes");
            PartRow("입 = 뉘앙스 (모르겠으면 중립)", S.Mouths, false);
            Mark("mouths");
            // 미리보기 바탕을 눈의 감정 색으로
            GUILayout.BeginVertical(tintCards[selEye]);
            GUILayout.Label(Face(selEye, selMouth), faceStyle, GUILayout.Height(64));
            GUILayout.Label($"{E[selEye].Name} 눈 + {E[selMouth].Name} 입", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();
            GUILayout.EndVertical();
        }

        void PartRow(string title, List<Emo> owned, bool eye)
        {
            GUILayout.BeginVertical();
            GUILayout.Label(title, small);
            GUILayout.BeginHorizontal();
            foreach (var e in PartOrder)
            {
                var opts = new[] { GUILayout.Width(52), GUILayout.Height(50) };
                if (!owned.Contains(e)) { GUI.enabled = false; GUILayout.Button("?\n<size=10>미획득</size>", btn, opts); GUI.enabled = true; continue; }
                bool sel = eye ? selEye == e : selMouth == e;
                var txt = $"{(eye ? E[e].EyeL + " " + E[e].EyeR : E[e].Mouth)}\n<size=10>{E[e].Name}</size>";
                if (GUILayout.Button(txt, sel ? partSel[e] : partBtn[e], opts) && !blocked) { if (eye) selEye = e; else selMouth = e; }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        // ---------- 수첩, 특성, 엔딩 ----------

        void DrawNotebook()
        {
            int need = S.Traits[Trait.Decode] >= 2 ? 1 : 2;
            GUILayout.Label($"기호 하나 = 감정 하나. 스티커 {need}장이면 해독.", small);
            GUILayout.Label($"해독한 기호 {S.SolvedCount}/{Sym.Count}", big);
            Legend();
            var seen = S.Seen.ToList();
            if (seen.Count == 0) GUILayout.Label("아직 본 기호가 없다. 밤에 SNS를 열면 모인다.", label);
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
            GUILayout.Label($"남은 포인트 <b>{S.Points}</b>", label);
            TraitRow(Trait.Decode, "해독", "단서 하나 더", "스티커 한 장으로 해독");
            TraitRow(Trait.Mimic, "의태", "낮 성공 시 파츠 두 개", "피로 절반");
            TraitRow(Trait.Empathy, "공감", "팬 글 카드가 감정 색으로", "섬뜩해도 의심도 그대로");
            if (traitsEndNight)
            {
                Btn(S.Day >= Balance.Days ? "<b>잠들기 → 결말</b>" : $"<b>잠들기 → {S.Day + 1}일째 아침</b>", () => Close(Win.Traits), true, null, GUILayout.Height(40));
                Mark("sleep");
            }
        }

        void TraitRow(Trait t, string name, string l1, string l2)
        {
            int lv = S.Traits[t];
            GUILayout.BeginVertical(card);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{name}</b>  {new string('●', lv)}{new string('○', 2 - lv)}", big);
            GUILayout.FlexibleSpace();
            Btn("올리기", () => S.Raise(t), S.CanRaise(t), null, GUILayout.Width(90));
            Mark($"trait_{(int)t}");
            GUILayout.EndHorizontal();
            GUILayout.Label($"{(lv >= 1 ? "■" : "□")} Lv1 {l1}   {(lv >= 2 ? "■" : "□")} Lv2 {l2}", small);
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
            Result.Perfect => $"{Face(eye, mouth)} 눈과 입이 모두 {E[ans].Name}. 완벽!",
            Result.Success => $"{Face(eye, mouth)} 눈이 {E[ans].Name}, 입은 중립.",
            Result.Creepy => $"{Face(eye, mouth)} 눈은 {E[ans].Name}인데 입이 {E[mouth].Name}. 어딘가 이상하다.",
            Result.Meh => $"{Face(eye, mouth)} 눈이 중립이라 감정이 안 보였다.",
            _ => reveal ? $"{Face(eye, mouth)} 상대가 기대한 건 {E[ans].Name}였다." : $"{Face(eye, mouth)} 팬이 기대한 감정이 아니었다.",
        };

        static string Fan(Result r) => r switch
        {
            Result.Perfect => "팬: (꺄아아!!)", Result.Success => "팬: (좋아해 줬다)", Result.Creepy => "팬: (…무서운데 계속 보게 돼)",
            Result.Meh => "팬: (읽씹당한 기분)", _ => "팬: (??? 뭐임)",
        };

        // ---------- 스타일 ----------

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
        static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        static string Mix(string hex, float t) => Hex(Color.Lerp(Color.white, C(hex), t));

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

        // 가운데가 빈 테두리 (튜토리얼 강조용)
        static Texture2D Frame(string color)
        {
            var t = new Texture2D(8, 8) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < 8; x++)
                for (int y = 0; y < 8; y++)
                    t.SetPixel(x, y, x < 3 || y < 3 || x > 4 || y > 4 ? C(color) : Color.clear);
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

        // 마우스를 올리거나 창이 포커스돼도 모양이 바뀌지 않게 모든 상태를 normal로 맞춘다
        static GUIStyle Still(GUIStyle s)
        {
            foreach (var st in new[] { s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused })
            {
                st.background = s.normal.background;
                st.textColor = s.normal.textColor;
            }
            return s;
        }

        static GUIStyle Boxed(Texture2D bg, int pad = 10)
        {
            var s = new GUIStyle { border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(pad, pad, pad, pad), margin = new RectOffset(4, 4, 4, 4), richText = true, wordWrap = true };
            s.normal.background = bg;
            return Still(s);
        }

        static GUIStyle Button(Texture2D face, Texture2D hover, Texture2D pressed, Color text, int size = 14)
        {
            var s = new GUIStyle { fontSize = size, richText = true, wordWrap = true, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(3, 3, 3, 3) };
            s.normal.background = s.focused.background = s.onNormal.background = s.onFocused.background = face;
            s.hover.background = s.onHover.background = hover;
            s.active.background = s.onActive.background = pressed;
            foreach (var st in new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused }) st.textColor = text;
            return s;
        }

        void InitStyles()
        {
            if (btn != null) return;
            GUI.skin.font = font;
            var ink = C("#4a3a5a");
            GUIStyle Text(int size, Color col, FontStyle fs = FontStyle.Normal)
            {
                var s = new GUIStyle { fontSize = size, richText = true, wordWrap = true, fontStyle = fs, padding = new RectOffset(2, 2, 2, 2), margin = new RectOffset(2, 2, 2, 2) };
                s.normal.textColor = col;
                return Still(s);
            }

            label = Text(15, ink);
            small = Text(13, C("#7a6a8a"));
            big = Text(19, ink, FontStyle.Bold);
            faceStyle = Text(46, ink); faceStyle.alignment = TextAnchor.MiddleCenter;
            postStyle = Text(22, ink); postStyle.wordWrap = false;
            symStyle = Text(24, C("#2b2540"), FontStyle.Bold); symStyle.wordWrap = false; // 못 푼 기호는 감정 색과 겹치지 않게 진한 먹색

            bgDay = Gradient("#bde0fe", "#ffc8dd", true);
            bgNight = Gradient("#2b2540", "#6a4c93", true);

            winStyle = Boxed(Bevel("#fff4f8", "#ffffff", "#b48aa0"));
            winStyle.padding = new RectOffset(12, 12, 38, 12);
            toastStyle = Boxed(Bevel("#fff0f2", "#ffffff", "#e0344f"));
            coachStyle = Boxed(Bevel("#fffbe8", "#ffffff", "#ff3d8b"), 12);
            hiliteStyle = new GUIStyle { border = new RectOffset(3, 3, 3, 3) };
            hiliteStyle.normal.background = Frame("#ff3d8b");

            titleStyle = Text(14, Color.white, FontStyle.Bold);
            titleStyle.alignment = TextAnchor.MiddleLeft; titleStyle.wordWrap = false;
            titleStyle.padding = new RectOffset(10, 30, 0, 0);
            titleStyle.normal.background = Gradient("#ff8fb3", "#8ecdf0", false);
            Still(titleStyle);
            errTitleStyle = new GUIStyle(titleStyle);
            errTitleStyle.normal.background = Gradient("#e0344f", "#ffb3c1", false);
            Still(errTitleStyle);

            btn = Button(Bevel("#ffe3ee", "#ffffff", "#c08aa5"), Bevel("#fff0f6", "#ffffff", "#c08aa5"), Bevel("#ffd3e4", "#c08aa5", "#ffffff"), ink);
            closeBtn = new GUIStyle(btn) { padding = new RectOffset(0, 0, 0, 2), fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 0, 0) };

            card = Boxed(Bevel("#ffffff", "#ffffff", "#d9c2d0"));
            paper = Boxed(Bevel("#fffbe8", "#ffffff", "#d8c79a"));
            panel = Boxed(Bevel("#fff4f8", "#ffffff", "#b48aa0"), 12);
            bubble = Boxed(Bevel("#eef7ff", "#ffffff", "#9cc3e0"));
            bubble.fontSize = 22; bubble.normal.textColor = ink; bubble.alignment = TextAnchor.MiddleCenter;
            Still(bubble);
            taskbar = Boxed(Bevel("#ffc2d9", "#ffe3ee", "#d98fb0"), 0);

            iconStyle = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            iconStyle.hover.background = Tex("#ffffff55");

            foreach (var e in PartOrder)
            {
                var col = E[e].Color;
                var chip = Text(15, ink, FontStyle.Bold);
                chip.wordWrap = false; chip.alignment = TextAnchor.MiddleCenter;
                chip.padding = new RectOffset(6, 6, 3, 3); chip.margin = new RectOffset(2, 2, 3, 3);
                chip.normal.background = Bevel(col, Mix(col, 0.5f), Hex(Color.Lerp(C(col), Color.black, 0.25f)));
                chips[e] = Still(chip);
                tintCards[e] = Boxed(Bevel(Mix(col, 0.25f), "#ffffff", col));
                // 파츠 버튼: 평소엔 감정 색을 옅게, 고르면 진하게
                partBtn[e] = Button(Bevel(Mix(col, 0.3f), "#ffffff", col), Bevel(Mix(col, 0.5f), "#ffffff", col), Bevel(col, col, "#ffffff"), ink);
                partSel[e] = Button(Bevel(col, "#ffffff", "#4a3a5a"), Bevel(col, "#ffffff", "#4a3a5a"), Bevel(col, "#4a3a5a", "#ffffff"), ink);
                partSel[e].fontStyle = FontStyle.Bold;
            }
        }
    }
}
