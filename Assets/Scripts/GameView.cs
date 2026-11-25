// 프로토타입 화면 (스펙 v2): 주인공의 태블릿 화면 하나 안에서 창을 여닫는다.
// shortcut: IMGUI와 코드로 만든 단색 텍스처뿐. 아트 단계에서 uGUI/UI Toolkit으로 교체하고 GameState는 그대로 쓴다.
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

        enum Win { Places, Encounter, Home, Sns, Notebook, Traits, Ending }

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

        const float VW = 1280, VH = 720, Bezel = 18, BarH = 38, PopupW = 420, CoachW = 300, ToastW = 330, ToastH = 84;
        const int PopupId = 99, CoachId = 98, FxId = 300;
        static readonly Dictionary<Win, float> WinWidth = new()
        {
            [Win.Places] = 560, [Win.Encounter] = 820, [Win.Home] = 820, [Win.Sns] = 960, [Win.Notebook] = 620, [Win.Traits] = 540, [Win.Ending] = 600,
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
        bool traitsEndNight, blocked;
        Emo selEye = Emo.Neutral, selMouth = Emo.Neutral;
        Decor selDecor;

        // 연출
        string popFace, stampText;
        float popT = -9, stampT = -9, frenzyUntil, crackT = -9;

        List<Step> steps;
        int step;
        Dictionary<string, Rect> anchors = new(), lastAnchors = new();
        Vector2 origin; // 지금 그리는 창의 화면 위치. Mark가 창 안 좌표를 화면 좌표로 바꿀 때 쓴다

        Font font;
        GUIStyle label, small, big, faceStyle, postStyle, symStyle, winStyle, titleStyle, errTitleStyle, toastStyle,
                 btn, closeBtn, card, paper, bubble, iconStyle, taskbar, panel, coachStyle, hiliteStyle, bezelStyle, stampStyle;
        readonly Dictionary<string, Texture2D> texCache = new();
        readonly Dictionary<Emo, GUIStyle> chips = new(), tintCards = new(), partBtn = new(), partSel = new(), tintText = new();
        Texture2D bgDay, bgNight;

        Step Cur => steps != null && step < steps.Count ? steps[step] : null;
        bool CoachVisible => Cur != null && popups.Count == 0 && S.Ending == Ending.None;

        // ---------- 수명 주기 ----------

        void Awake()
        {
            // 한글과 카오모지 글리프가 있는 OS 폰트
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Noto Sans CJK KR", "NanumGothic", "Arial Unicode MS" }, 16);
            Say("모행성 매뉴얼.txt", "너는 인플루언서로 위장한 외계인.\n7일 동안 들키지 말고 팔로워를 모으자.", "시작");
            bool Morning() => S.Slot == Slot.Morning;
            bool PastDay1() => S.Day > 1;
            steps = new List<Step>
            {
                new("icon_outing", "오전이다.\n[외출]을 눌러 보자.", () => open.Contains(Win.Places) || open.Contains(Win.Encounter) || !Morning()),
                new("place_0", "장소를 하나 고르자.", () => open.Contains(Win.Encounter) || !Morning()),
                new("situation", "상대의 지금 기분을 보자.", () => !Morning(), true),
                new("eyes", "낮에는 눈만 본다.\n상대 기분에 어울리는 눈을 골라 보자.", () => selEye != Emo.Neutral || !Morning()),
                new("react", "반응하자!\n성공이든 실패든 상대의 입을 얻는다.", () => !Morning()),
                new("stats", "결과는 여기 쌓인다.\n의심도 100이면 들킨다.", () => S.IsNight, true),
                new("icon_home", "오후다. 한 번 더 움직인다.\n[외출]이나 [집] 활동 중 하나.", () => S.IsNight),
                new("icon_sns", "밤이다.\n[SNS]를 열자.", () => open.Contains(Win.Sns) || S.NightDone || PastDay1()),
                new("post_0", "팬 글이다.\n■는 못 읽는 글자.", () => S.Replies[0] != null || PastDay1(), true),
                new("sym_0", "이 기호에 감정이 담겨 있다.", () => S.Replies[0] != null || PastDay1(), true),
                new("clue_0", "단서를 보자.\n케이크 사진이면…?", () => S.Replies[0] != null || PastDay1(), true),
                new("reply_0", "얼굴을 만들고\n이 버튼으로 답장!", () => S.Replies[0] != null || PastDay1()),
                new("hold_1", "어려운 글은 [보류].\n다음 밤에 서운해하며 돌아온다.", () => S.Replies[1] != null || PastDay1(), true),
                new("upload", "사진도 올리자.\n오늘 간 곳에 어울리는 얼굴로.", () => S.Uploaded || PastDay1()),
                new("feed", "남은 글에도 답하자.", () => S.NightDone || PastDay1()),
                new("trait_0", "포인트로 특성을 올리자.", () => S.Points == 0 || PastDay1()),
                new("sleep", "잠들면 피로가 풀린다.", PastDay1),
                new("icon_notebook", "맞힌 기호와 시너지는\n[해독 수첩]에 모인다.", null, true),
                new("legend", "감정 이름과 색.\n이제 혼자 해 보자!", null, true),
            };
        }

        void Update()
        {
            if (pending != null) { var a = pending; pending = null; a(); }
            while (Cur?.Done?.Invoke() == true) step++;
            if (S.JustBrokeDown)
            {
                S.JustBrokeDown = false;
                crackT = Time.time;
                Say("멘탈붕괴", $"화면이 깨졌다. 의태를 잠깐 놓쳤다.\n의심도가 크게 오르고, 피로 한계가 {S.FatMax}로 늘었다.\n한 번 더 한계에 닿으면 끝이다.");
            }
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
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Tex("#1d1828"));
            float k = Mathf.Min(Screen.width / (VW + Bezel * 2), Screen.height / (VH + Bezel * 2));
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - VW * k) / 2, (Screen.height - VH * k) / 2, 0), Quaternion.identity, new Vector3(k, k, 1));

            GUI.DrawTexture(new Rect(0, 0, VW, VH), S.IsNight ? bgNight : S.Slot == Slot.Afternoon ? Tex("#ffd9c2") : bgDay);
            DrawFatigueSmear();
            blocked = popups.Count > 0;
            DrawIcons();
            DrawWidgets();
            DrawTaskbar();
            GUI.Box(new Rect(-Bezel, -Bezel, VW + Bezel * 2, VH + Bezel * 2), GUIContent.none, bezelStyle);
            GUI.Label(new Rect(VW / 2 - 6, -Bezel + 3, 12, 12), "<color=#555>●</color>", small);

            foreach (var w in open.ToList())
                rects[w] = GUILayout.Window((int)w + 1, rects[w], _ => DrawWin(w), GUIContent.none, winStyle, GUILayout.Width(WinWidth[w]));

            if (CoachVisible)
            {
                PlaceCoach();
                coachRect = GUILayout.Window(CoachId, coachRect, _ => DrawCoach(), GUIContent.none, coachStyle, GUILayout.Width(CoachW));
                GUI.BringWindowToFront(CoachId);
            }

            DrawToasts();
            DrawFx();

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
                rects[w] = new Rect(x, 20 + open.Count * 14, wid, 0);
            }
            focusReq = (int)w + 1;
        }

        void Close(Win w)
        {
            open.Remove(w);
            if (w == Win.Traits && traitsEndNight)
            {
                traitsEndNight = false;
                var before = Snap();
                S.EndNight();
                if (S.Ending == Ending.None) Say($"{S.Day}일째 아침", $"푹 잤다. {Delta(before)}\n남은 날: {Balance.Days - S.Day + 1}일");
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
            selDecor = null;
            Say("1일째 아침", "다시 지구에 내려왔다.");
        }

        string SlotName => S.Slot switch { Slot.Morning => "오전", Slot.Afternoon => "오후", _ => "밤" };

        // 낮 행동을 마친 뒤 다음 칸을 알려준다
        string NextSlotLine => S.IsNight ? "밤이 되었다. [SNS]를 열자." : "오후가 되었다. 한 번 더 움직일 수 있다.";

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

        // ---------- 연출: 완벽 판정 팝, 시너지 도장, 화면 깨짐, 피로 번짐 ----------

        void Pop(string face) { popFace = face; popT = Time.time; }
        void Stamp(string text) { stampText = text; stampT = Time.time; }

        void DrawFx()
        {
            float pa = Time.time - popT, sa = Time.time - stampT, ca = Time.time - crackT;
            if (pa < 1.2f)
            {
                // 카오모지가 튀어오르며 반짝인다
                float bounce = pa < 0.25f ? pa / 0.25f * 1.3f : 1 + 0.3f * Mathf.Cos((pa - 0.25f) * 12) * Mathf.Max(0, 1 - (pa - 0.25f) * 1.5f);
                var r = new Rect(VW / 2 - 260, VH / 2 - 160, 520, 260);
                GUI.Window(FxId, r, _ =>
                {
                    GUI.Label(new Rect(0, 60, 520, 140), popFace, new GUIStyle(faceStyle) { fontSize = Mathf.RoundToInt(64 * bounce) });
                    for (int i = 0; i < 8; i++)
                    {
                        float ang = i * Mathf.PI / 4 + pa * 3, rad = 80 + pa * 120;
                        GUI.Label(new Rect(260 + Mathf.Cos(ang) * rad * 1.6f - 15, 130 + Mathf.Sin(ang) * rad * 0.8f - 15, 30, 30),
                            $"<color=#ffd23f><size={20 + i % 3 * 6}>☆</size></color>", label);
                    }
                }, GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(FxId);
            }
            if (sa < 1.8f)
            {
                // 수첩에 도장이 쾅
                float s = sa < 0.18f ? 2 - sa / 0.18f : 1;
                var r = new Rect(VW / 2 - 200, VH / 2 - 90, 400, 180);
                GUI.Window(FxId + 1, r, _ =>
                {
                    GUIUtility.RotateAroundPivot(-12, new Vector2(200, 90));
                    GUIUtility.ScaleAroundPivot(new Vector2(s, s), new Vector2(200, 90));
                    GUI.Label(new Rect(40, 40, 320, 100), stampText, stampStyle);
                }, GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(FxId + 1);
            }
            if (ca < 2.5f)
            {
                // 태블릿 화면이 깨진다
                GUI.Window(FxId + 2, new Rect(0, 0, VW, VH), _ =>
                {
                    var c = new Vector2(VW * 0.55f, VH * 0.4f);
                    var keep = GUI.matrix;
                    for (int i = 0; i < 9; i++)
                    {
                        GUI.matrix = keep;
                        GUIUtility.RotateAroundPivot(i * 40 + 13, c);
                        GUI.DrawTexture(new Rect(c.x, c.y - 1.5f, 260 + i * 37 % 300, 3), Tex("#ffffff"));
                    }
                    GUI.matrix = keep;
                }, GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(FxId + 2);
            }
        }

        // 피로가 오를수록 배경이 번지고 화면에 잡음이 낀다
        void DrawFatigueSmear()
        {
            float f = S.Fat / 100f;
            if (f > 0.4f)
            {
                GUI.color = new Color(1, 1, 1, Mathf.Min(0.45f, (f - 0.4f) * 0.9f));
                GUI.DrawTexture(new Rect(0, 0, VW, VH), Tex("#8a2b6d"));
                GUI.color = Color.white;
            }
            if (f > 0.7f && Event.current.type == EventType.Repaint)
                for (int i = 0; i < (int)((f - 0.6f) * 10); i++)
                    if (UnityEngine.Random.value < 0.5f)
                        GUI.DrawTexture(new Rect(0, UnityEngine.Random.Range(0, VH), VW, UnityEngine.Random.Range(2, 7)), Tex(i % 2 == 0 ? "#ff8fc855" : "#8ecdf055"));
        }

        string Monologue()
        {
            string line = S.Fat < 40 ? "오늘도 사람 얼굴 잘 했다 (^_^)" : S.Fat < 70 ? "얼굴 근육이 좀 뻐근하다" : S.Fat < 100 ? "사람 얼굴은… 어떻게 하더라" : "나는 사람 사람 사람 사람";
            // 피로가 오를수록 혼잣말에 행성의 기호가 섞인다
            var syms = Sym.Keys.ToArray();
            var rnd = new System.Random(S.Day * 31 + (int)S.Fat);
            float p = Mathf.Clamp01((S.Fat - 40) / 100f);
            return new string(line.Select(c => c != ' ' && rnd.NextDouble() < p ? syms[rnd.Next(syms.Length)][0] : c).ToArray());
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
                ("icon_home", "♨", "집", ClickHome, !S.IsNight),
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
            if (S.IsNight) { Say("지금은 밤", "외출은 오전과 오후에만 할 수 있다."); return; }
            Open(Win.Places);
        }

        void ClickHome()
        {
            if (S.IsNight) { Say("지금은 밤", "집 활동은 오전과 오후에 고른다."); return; }
            Open(Win.Home);
        }

        void ClickSns()
        {
            if (!S.IsNight) { Say($"아직 {SlotName}", "SNS는 밤에 열린다.\n오전과 오후에 한 번씩 움직이자."); return; }
            S.MarkSeen();
            Open(Win.Sns);
        }

        void DrawWidgets()
        {
            origin = new Vector2(VW - 344, 16);
            GUILayout.BeginArea(new Rect(origin.x, origin.y, 324, 640));
            GUILayout.BeginVertical(panel);
            GUILayout.Label("상태", big);
            bool frenzy = Time.time < frenzyUntil;
            int shownF = frenzy ? S.Followers + UnityEngine.Random.Range(-999, 9999) : S.Followers;
            Bar("팔로워", frenzy ? UnityEngine.Random.Range(0, Balance.FollowerGoal) : S.Followers, Balance.FollowerGoal, "#a68bff",
                frenzy ? $"<color=#e0344f><b>{shownF:N0}!!</b></color>" : $"{S.Followers:N0}/{Balance.FollowerGoal:N0}");
            Bar("의심도", S.Sus, 100, "#ff5a5a", $"{Mathf.RoundToInt(S.Sus)}");
            Bar("의태 피로", S.Fat, S.FatMax, S.Fat >= 80 ? "#e0344f" : "#f4a261", $"{Mathf.RoundToInt(S.Fat)}/{S.FatMax}");
            GUILayout.Label($"<i>혼잣말: {Monologue()}</i>", small);
            GUILayout.EndVertical();
            Mark("stats");

            GUILayout.BeginVertical(panel);
            GUILayout.Label("감정", big);
            Legend();
            GUILayout.EndVertical();
            Mark("legend");

            GUILayout.BeginVertical(paper);
            GUILayout.Label($"할 일 — {SlotName}", big);
            if (!S.IsNight)
            {
                Todo(S.Slot > Slot.Morning, "오전: [외출] 또는 [집]");
                Todo(false, "오후: [외출] 또는 [집]");
            }
            else
            {
                Todo(S.Uploaded, "[SNS] 사진 올리기");
                int n = S.Replies.Count(r => r != null);
                Todo(n == S.Replies.Length, $"[SNS] 팬 글 {n}/{S.Replies.Length} (보류 1개까지)");
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
            var r = GUILayoutUtility.GetRect(130, 22, GUILayout.ExpandWidth(true));
            r.y += 6; r.height = 12;
            GUI.DrawTexture(r, Tex("#f3e3ec"));
            var f = r; f.width *= Mathf.Clamp01(v / max);
            GUI.DrawTexture(f, Tex(color));
            GUILayout.Label(value, small, GUILayout.Width(90));
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
            var slot = S.IsNight ? "<color=#5b4b8a>● 밤</color>" : S.Slot == Slot.Afternoon ? "<color=#d0602a>◐ 오후</color>" : "<color=#e07a00>○ 오전</color>";
            GUI.Label(new Rect(VW - 190, VH - BarH + 4, 180, BarH - 8), $"<b>{S.Day}일째 / {Balance.Days}</b>   {slot}",
                new GUIStyle(label) { alignment = TextAnchor.MiddleRight, wordWrap = false });
        }

        const string Help =
            "오전·오후: [외출] 표정 반응 모으기 / [집] 거울 연습·피드 구경·낮잠\n" +
            "밤: [SNS] 업로드 1개 + 팬 글 답장 (1개는 보류 가능)\n\n" +
            "낮: 상대 기분에 어울리는 눈이면 성공\n" +
            "밤: 완벽(눈·입 정답 또는 시너지) / 성공(눈 정답, 입 민짜)\n섬뜩(눈 정답, 입 엉뚱) / 오답(눈 틀림)";

        string Title(Win w) => w switch
        {
            Win.Places => "외출", Win.Encounter => S.Pending != null ? S.Pending.Who : "만남", Win.Home => "집",
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
                case Win.Home: DrawHome(); break;
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
            GUILayout.Label($"의태 피로 +{(S.Traits[Trait.Mimic] >= 1 ? Balance.FatOutMimic : Balance.FatOut)}. 가끔 행인이나 특별한 일이 생긴다.", small);
            var list = GameData.Places;
            for (int i = 0; i < list.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < list.Length; j++)
                {
                    var p = list[j];
                    int aff = S.Aff.GetValueOrDefault(p.Id);
                    Btn($"<size=18><b>{p.Name}</b></size>\n{p.Npc}\n<size=12>호감 {new string('♥', aff)}{new string('♡', 3 - aff)}</size>",
                        () => { S.PrepareOuting(p.Id); selEye = selMouth = Emo.Neutral; Close(Win.Places); Open(Win.Encounter); },
                        true, null, GUILayout.Height(96));
                    Mark($"place_{j}");
                }
                GUILayout.EndHorizontal();
            }
        }

        void DrawEncounter()
        {
            var ev = S.Pending;
            if (ev == null) { GUILayout.Label("아무도 없다.", label); return; }
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(400));
            var kind = ev.Kind switch { EvKind.Passerby => "<color=#d0602a>행인 · 한 번뿐인 만남</color>", EvKind.Special => "<color=#e0344f>특별한 일!</color>", _ => "" };
            if (kind != "") GUILayout.Label(kind, label);
            GUILayout.BeginVertical(paper);
            GUILayout.Label($"<b>{ev.Who}</b>", label);
            GUILayout.Label(ev.Situation, label);
            GUILayout.Space(6);
            GUILayout.Label($"“{ev.Say}”", bubble);
            GUILayout.EndVertical();
            Mark("situation");
            GUILayout.Label("낮에는 <b>눈</b>만 본다. 상대 기분에 어울리는 눈이면 성공.", small);
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical();
            Builder(false);
            Btn("<b>이 눈으로 반응하기</b>", React, true, null, GUILayout.Height(40));
            Mark("react");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        void React()
        {
            var before = Snap();
            var ev = S.Pending;
            if (ev == null) return;
            var (ok, mouth, decor) = S.DoEncounter(selEye);
            Close(Win.Encounter);
            var body = $"{Face(selEye, Emo.Neutral)} → {(ok ? ev.WinText : ev.FailText)}";
            if (mouth.HasValue) body += $"\n\n상대의 표정을 따라 했다 → <b>{E[mouth.Value].Name} 입 {E[mouth.Value].Mouth}</b> 획득!";
            if (decor != null) body += $"\n\n<b>장식 획득: {decor.Name} {Face(Emo.Joy, Emo.Joy, decor)}</b>\n업로드에 붙이면 팔로워가 크게 오른다.";
            body += $"\n{Delta(before)}\n\n{NextSlotLine}";
            Say(ok ? "성공" : "실패", body);
            if (ok && ev.Kind == EvKind.Npc && S.Aff.GetValueOrDefault(S.Went) >= 3) Tip("aff3", $"{ev.Who}와(과) 가장 친해졌다.\n진엔딩에서 에필로그가 붙는다.");
            MaybeError();
        }

        // ---------- 집 ----------

        void DrawHome()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(400));
            GUILayout.BeginVertical(card);
            GUILayout.Label($"<b>거울 연습</b>  <color=#7a6a8a>피로 {Balance.FatHome}</color>", label);
            GUILayout.Label("오른쪽에서 얼굴을 만들어 본다. 시너지 조합이면 수첩에 이름이 남는다.", small);
            Btn($"이 얼굴 {Face(selEye, selMouth)} 연습하기", Mirror);
            GUILayout.EndVertical();
            Mark("home_mirror");

            GUILayout.BeginVertical(card);
            GUILayout.Label($"<b>피드 구경</b>  <color=#7a6a8a>피로 {Balance.FatHome}</color>", label);
            GUILayout.Label("오늘 밤 팬 글 하나를 미리 보고, 그 글의 색 단서를 받는다.", small);
            Btn("피드 구경하기", Peek);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(card);
            GUILayout.Label($"<b>낮잠</b>  <color=#7a6a8a>피로 {Balance.FatNap}, 의심도 {Balance.NapSus}</color>", label);
            GUILayout.Label("아무것도 얻지 않는 대신 크게 회복한다.", small);
            Btn("낮잠 자기", Nap);
            GUILayout.EndVertical();
            GUILayout.EndVertical();

            GUILayout.Space(12);
            Builder(true);
            GUILayout.EndHorizontal();
        }

        void Mirror()
        {
            var before = Snap();
            var face = Face(selEye, selMouth);
            var (syn, isNew) = S.DoMirror(selEye, selMouth);
            Close(Win.Home);
            if (isNew) Stamp($"시너지 발견!\n{syn.Name} {face}");
            Say("거울 연습", (syn == null ? $"거울 속 {face} 가 어색하게 웃는다." : isNew ? $"<b>시너지 '{syn.Name}'</b>을(를) 찾았다! 수첩에 기록했다." : $"'{syn.Name}' 얼굴을 다시 연습했다.")
                + $"\n{Delta(before)}\n\n{NextSlotLine}");
        }

        void Peek()
        {
            var before = Snap();
            int i = S.DoPeek();
            Close(Win.Home);
            if (i < 0) { Say("피드 구경", $"새 글이 없다.\n{Delta(before)}\n\n{NextSlotLine}"); return; }
            var p = S.Tonight[i];
            var color = p.Hint == Hint.None ? "색이 보이지 않는다. 감정이 모호한 글이다."
                : $"색 단서: <b>{E[p.Answers[0].Eye].Name}</b> 빛이 {(p.Hint == Hint.Partial ? "글 일부에만" : "")} 비친다.";
            Say("피드 구경", $"오늘 밤 {Author(p)} 의 글을 미리 봤다.\n\n<size=20>{p.Text}</size>\n{color}\n{Delta(before)}\n\n{NextSlotLine}");
        }

        void Nap()
        {
            var before = Snap();
            S.DoNap();
            Close(Win.Home);
            Say("낮잠", $"쿨쿨.\n{Delta(before)}\n\n{NextSlotLine}");
        }

        // ---------- SNS ----------

        static string Author(Post p) => p.Author == null ? "지나가는 팬" : $"<b>{p.Author}</b>";

        void DrawSns()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(560));
            GUILayout.BeginVertical(card);
            string photo = S.Went == null ? "방 사진" : $"{PlaceById(S.Went).Name} 사진";
            GUILayout.Label($"<b>오늘의 업로드</b> · {photo}", label);
            if (S.Uploaded) GUILayout.Label("올림 완료", label);
            else
            {
                if (S.Decors.Count > 0)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("장식", small, GUILayout.Width(36));
                    if (GUILayout.Button("없음", selDecor == null ? partSel[Emo.Neutral] : partBtn[Emo.Neutral], GUILayout.Width(60)) && !blocked) selDecor = null;
                    foreach (var d in S.Decors)
                        if (GUILayout.Button($"{d.L} {d.R}", selDecor == d ? partSel[Emo.Shy] : partBtn[Emo.Shy], GUILayout.Width(70)) && !blocked) selDecor = d;
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                Btn($"지금 얼굴 {Face(selEye, selMouth, selDecor)} 로 올리기", Upload);
            }
            GUILayout.EndVertical();
            Mark("upload");

            GUILayout.BeginVertical();
            for (int i = 0; i < S.Tonight.Count; i++) PostCard(i);
            GUILayout.EndVertical();
            Mark("feed");
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical();
            Builder(true);
            Btn("해독 수첩 열기", () => Open(Win.Notebook));
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        void PostCard(int i)
        {
            var post = S.Tonight[i];
            var tokens = post.Text.Split(' ');
            // 색 단서: 다 보이거나, 글 앞쪽 절반에만, 또는 안 보인다
            bool color = S.ShowsColor(post);
            int tinted = !color ? 0 : post.Hint == Hint.Full ? tokens.Length : tokens.Length / 2;
            var tint = tintText[post.Answers[0].Eye];

            GUILayout.BeginVertical(card);
            GUILayout.Label($"{Author(post)}{(post.Id.StartsWith("f") ? " <color=#e0344f>(어제 보류한 글의 후속)</color>" : "")}", small);
            GUILayout.BeginHorizontal();
            bool firstSym = true;
            for (int k = 0; k < tokens.Length; k++)
            {
                var t = tokens[k];
                var st = k < tinted ? tint : postStyle;
                if (!Sym.TryGetValue(t, out var e)) { GUILayout.Label(t, st, GUILayout.ExpandWidth(false)); continue; }
                if (S.Solved(t)) GUILayout.Label($"{t} {E[e].Name}", chips[e], GUILayout.ExpandWidth(false));
                else GUILayout.Label(t, k < tinted ? tint : symStyle, GUILayout.ExpandWidth(false));
                if (firstSym) { Mark($"sym_{i}"); firstSym = false; }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Label($"단서: {string.Join(" · ", post.Clues.Take(S.Traits[Trait.Decode] >= 1 ? 2 : 1))}" +
                (color ? "" : post.Hint == Hint.None && S.Traits[Trait.Empathy] >= 1 ? " · 색이 안 보인다" : ""), small);
            Mark($"clue_{i}");

            // 수첩에서 알아낸 것을 글 아래에 바로 보여준다
            var notes = tokens.Where(Sym.ContainsKey).Distinct().Select(t =>
                S.Solved(t) ? $"<b>{t} = {E[Sym[t]].Name}</b>"
                : S.Stickers.TryGetValue(t, out var c) ? $"{t}에 {E[Sym[t]].Name} 스티커 {c}장"
                : $"{t} = ?").ToList();
            GUILayout.Label($"수첩 메모: {string.Join(", ", notes)}", small);

            var rep = S.Replies[i];
            if (rep != null) GUILayout.Label(rep.Held ? "↳ 보류함 — 내일 밤 돌아온다" : $"↳ 답장 {rep.Face} — <b>{ResultName(rep.R.Value)}</b>", label);
            else
            {
                GUILayout.BeginHorizontal();
                Btn($"{Face(selEye, selMouth)} 로 답장", () => Reply(i));
                Mark($"reply_{i}");
                Btn("보류", () => Hold(i), S.CanHold, null, GUILayout.Width(70));
                Mark($"hold_{i}");
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            Mark($"post_{i}");
        }

        void Upload()
        {
            var before = Snap();
            var decor = selDecor;
            var (r, _) = S.DoUpload(selEye, selMouth, decor);
            if (decor != null) { frenzyUntil = Time.time + 1.8f; S.Decors.Remove(decor); selDecor = null; } // shortcut: 장식은 한 번 쓰면 사라진다고 가정, 스펙 확인 필요
            Say("업로드 완료", (r.HasValue ? $"{Face(selEye, selMouth, decor)} — {ResultName(r.Value)}" : "방 사진을 올렸다.")
                + (decor != null ? "\n<b>장식 효과로 팔로워 폭주!</b>" : "") + $"\n{Delta(before)}");
            if (r == Result.Perfect) Pop(Face(selEye, selMouth, decor));
            AfterNightAction();
        }

        void Reply(int i)
        {
            var before = Snap();
            var post = S.Tonight[i];
            var (r, _, newly, newSyn) = S.DoReply(i, selEye, selMouth);
            var body = $"{Fan(post, r)}\n{Why(r, selEye, selMouth)}\n{Delta(before)}";
            if (r is Result.Perfect or Result.Success)
                body += $"\n\n{string.Join(" ", post.Text.Split(' ').Where(Sym.ContainsKey).Distinct())} 에 스티커!";
            foreach (var s in newly) body += $"\n<b>해독 완료! {s} = {E[Sym[s]].Name}</b>";
            if (newSyn != null) { body += $"\n<b>시너지 '{newSyn.Name}' 발견!</b>"; Stamp($"시너지 발견!\n{newSyn.Name} {Face(newSyn.Eye, newSyn.Mouth)}"); }
            Say($"팬의 반응 — {ResultName(r)}", body);
            if (r == Result.Perfect) Pop(Face(selEye, selMouth));
            if (newly.Count > 0) Tip("solve", "해독한 기호는 이제\n색 스티커로 뜻과 함께 보인다.");
            if (post.Author == Doubter && r == Result.Wrong) Tip("doubter", "의심하는자에게 틀리면\n의심도가 더 많이 오른다.");
            MaybeError();
            AfterNightAction();
        }

        void Hold(int i)
        {
            if (!S.DoHold(i)) return;
            Say("보류", $"{Author(S.Tonight[i])} 의 글을 내일로 미뤘다.\n팔로워는 오르지 않는다.");
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

        // ---------- 카오모지 조립 바 (만남, 집, SNS 공용) ----------

        void Builder(bool withMouth)
        {
            GUILayout.BeginVertical(card, GUILayout.Width(360));
            PartRow("눈 — 감정", S.Eyes, true);
            Mark("eyes");
            if (withMouth)
            {
                PartRow("입 — 뉘앙스 (모르겠으면 민짜)", S.Mouths, false);
                Mark("mouths");
            }
            else GUILayout.Label($"입: {S.Mouths.Count - 1}/5 모음. 낮에 상대의 반응 표정으로 얻는다.", small);
            var mouth = withMouth ? selMouth : Emo.Neutral;
            var syn = withMouth ? SynergyOf(selEye, mouth) : null;
            // 미리보기 바탕을 눈의 감정 색으로
            GUILayout.BeginVertical(tintCards[selEye]);
            GUILayout.Label(Face(selEye, mouth), faceStyle, GUILayout.Height(64));
            var synName = syn == null ? "" : S.FoundSynergies.Contains(syn.Name) ? $" = <b>{syn.Name}</b>" : " = <b>???</b> (시너지?)";
            GUILayout.Label($"{E[selEye].Name} 눈 + {E[mouth].Name} 입{synName}", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
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

            GUILayout.Space(6);
            GUILayout.Label($"시너지 {S.FoundSynergies.Count}/{Synergies.Length}", big);
            GUILayout.BeginHorizontal();
            foreach (var syn in Synergies)
            {
                bool found = S.FoundSynergies.Contains(syn.Name);
                GUILayout.Label(found ? $"<b>{syn.Name}</b>\n{Face(syn.Eye, syn.Mouth)}" : "???\n( ? ? )", found ? chips[syn.Eye] : paper, GUILayout.Width(104));
            }
            GUILayout.EndHorizontal();
        }

        void DrawTraits()
        {
            GUILayout.Label($"남은 포인트 <b>{S.Points}</b>", label);
            TraitRow(Trait.Decode, "해독", "단서 하나 더", "스티커 한 장으로 해독");
            TraitRow(Trait.Mimic, "의태", $"외출 피로 +{Balance.FatOut} → +{Balance.FatOutMimic}", "멘탈붕괴 때 의심도 절반");
            TraitRow(Trait.Empathy, "공감", "팬 글에 감정 색이 보임 (모호한 글은 일부만)", "섬뜩해도 의심도 그대로");
            if (traitsEndNight)
            {
                Btn(S.Day >= Balance.Days ? "<b>잠들기 → 결말</b>" : $"<b>잠들기 → {S.Day + 1}일째 아침</b>  <color=#7a6a8a>(피로 {Balance.FatSleep})</color>",
                    () => Close(Win.Traits), true, null, GUILayout.Height(40));
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
            GUILayout.Label($"{(lv >= 1 ? "■" : "□")} Lv1 {l1}\n{(lv >= 2 ? "■" : "□")} Lv2 {l2}", small);
            GUILayout.EndVertical();
        }

        void DrawEnding()
        {
            var (title, text) = Endings[S.Ending];
            var center = new GUIStyle(label) { fontSize = 17, alignment = TextAnchor.MiddleCenter };
            GUILayout.Space(16);
            GUILayout.Label(title, new GUIStyle(big) { fontSize = 30, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);
            GUILayout.Label(text, center);
            if (S.Ending == Ending.Conquer)
                foreach (var cut in S.EpilogueCuts) GUILayout.Label(cut, paper);
            GUILayout.Space(10);
            GUILayout.Label($"팔로워 {S.Followers:N0} · 의심도 {Mathf.RoundToInt(S.Sus)} · 해독 {S.SolvedCount}/{Sym.Count} · 시너지 {S.FoundSynergies.Count}/{Synergies.Length}",
                new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
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

        static string Why(Result r, Emo eye, Emo mouth)
        {
            var syn = SynergyOf(eye, mouth);
            return r switch
            {
                Result.Perfect => syn != null ? $"{Face(eye, mouth)} {syn.Name}! 딱 맞는 얼굴." : $"{Face(eye, mouth)} 눈과 입이 모두 맞았다.",
                Result.Success => $"{Face(eye, mouth)} 눈이 맞았다. 입은 민짜.",
                Result.Creepy => $"{Face(eye, mouth)} 눈은 맞는데 입이 엉뚱하다. 어딘가 이상하다.",
                Result.Meh => $"{Face(eye, mouth)} 민짜 눈이라 감정이 안 보였다.",
                _ => $"{Face(eye, mouth)} 팬이 기대한 감정이 아니었다.",
            };
        }

        static string Fan(Post p, Result r) => (p.Author, r) switch
        {
            (Doubter, Result.Perfect) => "의심하는자: (…내가 너무 예민했나)",
            (Doubter, Result.Wrong) => "의심하는자: (역시. 사람이 아니야.)",
            (Starlover, Result.Perfect) when p.WantsCreepy => "별빛러버: (그 얼굴!!! 그 얼굴이 보고 싶었어!!!)",
            (Gloomcat, Result.Perfect or Result.Success) => "우울한고양이: (…조금 나아졌어)",
            (Gloomcat, _) => "우울한고양이: (그렇구나…)",
            _ => r switch
            {
                Result.Perfect => "팬: (꺄아아!!)", Result.Success => "팬: (좋아해 줬다)", Result.Creepy => "팬: (…무서운데 계속 보게 돼)",
                Result.Meh => "팬: (읽씹당한 기분)", _ => "팬: (??? 뭐임)",
            },
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

        // 가운데가 빈 테두리 (튜토리얼 강조, 태블릿 베젤, 도장)
        static Texture2D Frame(string color, int size, int thick)
        {
            var t = new Texture2D(size, size) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                    t.SetPixel(x, y, x < thick || y < thick || x >= size - thick || y >= size - thick ? C(color) : Color.clear);
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
            hiliteStyle.normal.background = Frame("#ff3d8b", 8, 3);
            bezelStyle = new GUIStyle { border = new RectOffset(18, 18, 18, 18) };
            bezelStyle.normal.background = Frame("#2b2540", 40, 18);
            stampStyle = Text(28, C("#e0344f"), FontStyle.Bold);
            stampStyle.alignment = TextAnchor.MiddleCenter;
            stampStyle.border = new RectOffset(4, 4, 4, 4);
            stampStyle.normal.background = Frame("#e0344f", 12, 4);
            Still(stampStyle);

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
                // 색 단서: 글자 뒤에 감정 색을 옅게 깐다
                var tt = Text(22, ink);
                tt.wordWrap = false; tt.margin = new RectOffset(0, 0, 2, 2);
                tt.normal.background = Tex(Mix(col, 0.45f));
                tintText[e] = Still(tt);
                // 파츠 버튼: 평소엔 감정 색을 옅게, 고르면 진하게
                partBtn[e] = Button(Bevel(Mix(col, 0.3f), "#ffffff", col), Bevel(Mix(col, 0.5f), "#ffffff", col), Bevel(col, col, "#ffffff"), ink);
                partSel[e] = Button(Bevel(col, "#ffffff", "#4a3a5a"), Bevel(col, "#ffffff", "#4a3a5a"), Bevel(col, "#4a3a5a", "#ffffff"), ink);
                partSel[e].fontStyle = FontStyle.Bold;
            }
        }
    }
}
