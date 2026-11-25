// 프로토타입 화면 (스펙 v2): 주인공의 태블릿 화면 하나 안에서 창을 여닫는다.
// 규칙: 색은 캔디 파스텔 32색(Pal)만, 글씨는 검정. 결과·효과는 문장 대신 도장·게이지·숫자 이펙트로.
// shortcut: IMGUI와 코드로 만든 텍스처뿐. 픽셀 아이콘이 나오면 uGUI/UI Toolkit으로 교체하고 GameState는 그대로 쓴다.
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

        enum Win { Places, Encounter, Home, Sns, Notebook, Traits, Diary, Ending }

        class Popup { public string Title, Ok = "▶"; public Action Draw, OnOk; }
        class Toast { public string Body; public float T0; public int Id; }
        class Floater { public string Anchor, Text; public float T0; }
        class Sticker { public string Text, Color; public Sticker(string t, string c) { Text = t; Color = c; } }
        class EncResult { public Encounter Ev; public bool Ok; public Emo? Mouth; public Decor Decor; public float T; }

        // 튜토리얼 한 단계: Target을 가리키고, Done이 참이 되거나 [다음]을 누르면 넘어간다
        class Step
        {
            public string Target, Text;
            public Func<bool> Done;
            public bool Next;
            public Func<bool> When; // 이 조건일 때만 보인다 (예: 밤에만 할 수 있는 일은 밤에만 안내)
            public Step(string target, string text, Func<bool> done, bool next = false, Func<bool> when = null) { Target = target; Text = text; Done = done; Next = next; When = when; }
        }

        // 캔디 파스텔 32색 중 쓰는 것들. 이 밖의 색은 쓰지 않는다
        static class Pal
        {
            public const string Letterbox = "#DFD5F7", BgTop = "#CBECFF", BgBottom = "#FFD3EC", SmearTop = "#F5D8F6", SmearBottom = "#EEB1F7",
                Bezel = "#CCB8F7", Lightest = "#ECFEFF", WinFace = "#E8F6FF", Line = "#CCB8F7", Gray = "#D6DAE5",
                TitleA = "#FFA9DE", TitleB = "#BAE2FC", ErrA = "#F29CFF", ErrB = "#FFA4D5",
                BtnFace = "#FFDCF2", BtnLight = "#FFE3FA", BtnDark = "#EEB1F7", BtnPress = "#FFB9DE",
                Paper = "#FFDDF8", PaperDark = "#F1CCF5", Bubble = "#D8EFFF", BubbleDark = "#BAE2FC", Taskbar = "#FFC1F2",
                Hot = "#F29CFF", Heart = "#FFA4D5", BarBg = "#E2F5F9", BarFollow = "#CCB8F7", BarSus = "#F29CFF", BarFat = "#FFA4D5",
                Ease = "#DBF4F9", Glitch1 = "#FFA4D5", Glitch2 = "#B6FBFF", Special = "#FFE3FA";
        }

        const float VW = 1280, VH = 720, Bezel = 18, BarH = 38, PopupW = 420, CoachW = 300, ToastW = 330, ToastH = 84;
        const int PopupId = 99, CoachId = 98, FxId = 300;
        static readonly Dictionary<Win, float> WinWidth = new()
        {
            [Win.Places] = 480, [Win.Encounter] = 820, [Win.Home] = 820, [Win.Sns] = 700, [Win.Notebook] = 420, [Win.Traits] = 520, [Win.Diary] = 640, [Win.Ending] = 600,
        };
        static readonly Emo[] PartOrder = { Emo.Neutral, Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };

        GameState S = new();
        readonly List<Win> open = new();
        readonly Dictionary<Win, Rect> rects = new();
        readonly Queue<Popup> popups = new();
        readonly List<Toast> toasts = new();
        readonly List<Floater> floaters = new();
        readonly List<Sticker> diary = new();
        Rect popupRect = new((VW - PopupW) / 2, 170, PopupW, 0), coachRect = new(0, 0, CoachW, 0);
        Action pending;       // 버튼 동작은 다음 Update에서 실행해 IMGUI 레이아웃 불일치를 피한다
        int? focusReq;
        int toastSeq, followersAtDayStart;
        bool traitsEndNight, blocked;
        float hoverFat, hoverFatNext; // 고르려는 행동이 피로를 얼마나 바꾸는지: 게이지에서 깜빡인다
        Emo selEye = Emo.Neutral, selMouth = Emo.Neutral;
        Decor selDecor;
        EncResult enc;
        bool decorTutorial;
        (string stamp, string face)? homeResult; // 집 활동 결과: 창 안에 도장, 닫기 버튼만 남는다
        readonly Dictionary<string, (string fan, float t)> replyFx = new(); // 댓글 카드 안에 뜨는 판정과 팬의 한마디
        float uploadT = -9;
        Result? uploadR;
        bool statHelp;
        (string text, Rect r)? tip, tipNext;   // 마우스를 올리면 뜨는 설명
        string pinTip; Rect pinRect; float pinT = -9; // 못 쓰는 아이콘을 누르면 잠깐 뜨는 이유

        // 날이 지나갈 때 내려오는 막: N일차 종료 → N+1일차
        float curtainT = -9;
        int curtainDay;
        bool curtainSwitched = true; // 시작할 땐 넘길 날이 없다

        // 연출
        string flyText, flyTo;
        float crackT = -9, flyT = -9, frenzyUntil;

        List<Step> steps;
        int step;
        Dictionary<string, Rect> anchors = new(), lastAnchors = new();
        Vector2 origin; // 지금 그리는 창의 화면 위치. Mark가 창 안 좌표를 화면 좌표로 바꿀 때 쓴다

        Font dot, body, round; // DotGothic16: 카오모지·숫자·아이콘 / Gowun Dodum: 한글 본문 / Dongle: 감정 이름·엔딩 제목
        GUIStyle label, small, big, dotStyle, faceStyle, postStyle, symStyle, roundStyle, winStyle, titleStyle, errTitleStyle, toastStyle,
                 btn, offBtn, closeBtn, card, paper, bubble, iconStyle, taskbar, panel, coachStyle, hiliteStyle, bezelStyle, stampStyle,
                 cardBtn, badgeOn, badgeOff, clockOn, clockOff, curtainStyle, selCard, linkBtn, tabOn, tabOff, cellFrame;
        readonly Dictionary<string, Texture2D> texCache = new();
        readonly Dictionary<string, GUIStyle> stickerStyles = new();
        readonly Dictionary<Emo, GUIStyle> chips = new(), partBtn = new(), partSel = new(), tintText = new(), tintCards = new();
        Texture2D bgDay, bgSmear, circle;

        Step Cur => steps != null && step < steps.Count ? steps[step] : null;
        bool CoachVisible => Cur != null && popups.Count == 0 && S.Ending == Ending.None && (Cur.When?.Invoke() ?? true) && CurtainDone;

        // ---------- 수명 주기 ----------

        void Awake()
        {
            dot = Resources.Load<Font>("Fonts/DotGothic16-Regular");
            body = Resources.Load<Font>("Fonts/GowunDodum-Regular");
            round = Resources.Load<Font>("Fonts/Dongle-Regular");
            bool Morning() => S.Slot == Slot.Morning;
            bool PastDay1() => S.Day > 1;
            steps = new List<Step>
            {
                new("stats", $"목표: 7일 동안 들키지 않고\n팔로워 {Balance.FollowerGoal:#,0}명 모으기!\n수치에 마우스를 올리면 설명이 나온다.", null, true),
                new("clock", "하루는 오전 → 오후 → 밤.\n오전과 오후에 한 번씩 움직이고, 밤엔 SNS.", null, true),
                new("icon_outing", "[외출]을 눌러 보자.", () => open.Contains(Win.Places) || open.Contains(Win.Encounter) || !Morning()),
                new("place_0", "장소를 하나 고르자.\n하트는 호감.", () => open.Contains(Win.Encounter) || !Morning()),
                new("situation", "상대의 지금 기분을 보자.", () => !Morning(), true),
                new("eyes", "낮에는 눈만 본다.\n어울리는 눈을 골라 보자.", () => selEye != Emo.Neutral || !Morning()),
                new("react", "반응하자!\n성공이든 실패든 상대의 입을 얻는다.", () => enc != null || !Morning()),
                new("close_result", "결과를 봤으면 × 로 창을 닫자.\n창을 닫아야 시간이 흐른다.", () => !Morning()),
                new("icon_home", "오후다. 한 번 더 움직이자.\n[외출]이나 [집]에서 하나를 하고\n창을 닫으면 밤이 된다.", () => S.IsNight),
                new("icon_sns", "밤이다.\n[SNS]를 열자.", () => open.Contains(Win.Sns) || S.NightDone || PastDay1(), false, () => S.IsNight),
                new("nb", "SNS를 열면 [해독 수첩]이 옆에 같이 열린다.\n감각의 이름·색·모양은 여기서만 본다.", () => S.Replies[0] != null || PastDay1(), true),
                new("upload", "먼저 내 게시물. 사진 속 장면을 읽고\n어울리는 얼굴을 골라 [▣ 게시].", () => S.Uploaded || PastDay1()),
                new("tab_mention", "[@ 멘션] 탭: 팬들의 글에\n카오모지로 멘션을 단다.", () => snsTab == 1 || S.Replies[0] != null || PastDay1()),
                new("post_0", "팬의 글이다.\n■는 못 읽는 글자.", () => S.Replies[0] != null || PastDay1(), true),
                new("sym_0", "이 기호에 감정이 담겨 있다.\n마우스를 올리면 수첩에서 같은 기호가 반짝인다.", () => S.Replies[0] != null || PastDay1(), true),
                new("clue_0", "단서를 보자.\n케이크 사진이면…?", () => S.Replies[0] != null || PastDay1(), true),
                new("reply_0", "[@ 멘션 달기]를 누르고\n아래에서 얼굴을 만들어 [@ 보내기]!", () => S.Replies[0] != null || PastDay1()),
                new("hold_1", "어려운 글은 [‖ 나중에].\n다음 밤에 서운해하며 돌아온다.", () => S.Replies[1] != null || PastDay1(), true),
                new("feed", "남은 글에도 멘션을 달자.", () => S.Replies.All(r => r != null) || PastDay1()),
                new("send", "결과를 봤으면 창을 닫자.", () => open.Contains(Win.Traits) || PastDay1()),
                new("trait_0", "포인트로 특성을 올리자.", () => S.Points == 0 || PastDay1()),
                new("sleep", "잠들기 전에 그림 일기를 쓴다.", () => open.Contains(Win.Diary) || PastDay1()),
                new("pledge", "오늘 한 일이 스티커로 남는다.\n내일을 도울 다짐 스티커를 하나 고르자.", () => pledgeSel.HasValue || PastDay1()),
            };
        }

        void Update()
        {
            if (pending != null) { var a = pending; pending = null; a(); }
            while (Cur?.Done?.Invoke() == true) step++;
            if (!curtainSwitched && Time.time - curtainT >= CurtainIn)
            {
                curtainSwitched = true;
                var before = Snap();
                S.EndNight();
                if (pledgeSel.HasValue && S.Ending == Ending.None) S.ApplyPledge(pledgeSel.Value);
                pledgeSel = null;
                diary.Clear(); replyFx.Clear();
                uploadR = null; target = -2; snsTab = -1;
                open.Clear(); enc = null; homeResult = null; // 날이 바뀌면 모든 창을 닫는다
                followersAtDayStart = S.Followers;
                Floats(before);
            }
            if (S.JustBrokeDown) { S.JustBrokeDown = false; crackT = Time.time; diary.Add(new Sticker("(ﾟДﾟ)!!", Pal.Hot)); }
            if (S.Ending != Ending.None && !open.Contains(Win.Ending) && !open.Contains(Win.Diary) && CurtainDone)
            {
                open.Clear(); popups.Clear();
                Open(Win.Ending);
            }
        }

        void OnGUI()
        {
            InitStyles();
            if (Event.current.type == EventType.Repaint)
            {
                (lastAnchors, anchors) = (anchors, lastAnchors); anchors.Clear();
                hoverFat = hoverFatNext; hoverFatNext = 0;
                tip = tipNext; tipNext = null;
                hoverSym = hoverSymNext; hoverSymNext = null;
                hoverEmo = hoverEmoNext; hoverEmoNext = null;
            }
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Tex(Pal.Letterbox));
            float k = Mathf.Min(Screen.width / (VW + Bezel * 2), Screen.height / (VH + Bezel * 2));
            if (k >= 1) k = Mathf.Floor(k); // 정수 배율로만 키워야 글자가 번지지 않는다
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - VW * k) / 2, (Screen.height - VH * k) / 2, 0), Quaternion.identity, new Vector3(k, k, 1));

            GUI.DrawTexture(new Rect(0, 0, VW, VH), S.Fat >= 70 ? bgSmear : bgDay);
            DrawGlitchBars();
            blocked = popups.Count > 0;
            DrawIcons();
            DrawWidgets();
            DrawFloaters();
            DrawTaskbar();
            GUI.Box(new Rect(-Bezel, -Bezel, VW + Bezel * 2, VH + Bezel * 2), GUIContent.none, bezelStyle);

            foreach (var w in open.ToList())
                rects[w] = GUILayout.Window((int)w + 1, rects[w], _ => DrawWin(w), GUIContent.none, winStyle, GUILayout.Width(WinWidth[w]));

            DrawToasts();
            DrawFx();
            DrawTip();
            // 튜토리얼 상자는 설명 툴팁보다 위에
            if (CoachVisible)
            {
                PlaceCoach();
                coachRect.height = 0; // GUILayout.Window는 넘긴 높이를 최소로 쓴다: 0이어야 글에 맞게 줄어든다
                coachRect = GUILayout.Window(CoachId, coachRect, _ => DrawCoach(), GUIContent.none, coachStyle, GUILayout.Width(CoachW));
                coachH = coachRect.height;
                GUI.BringWindowToFront(CoachId);
            }
            DrawCurtain();

            if (popups.Count > 0)
            {
                popupRect.height = 0;
                popupRect = GUILayout.Window(PopupId, popupRect, _ => DrawPopup(), GUIContent.none, winStyle, GUILayout.Width(PopupW));
                GUI.BringWindowToFront(PopupId);
                GUI.FocusWindow(PopupId);
            }
            else if (focusReq.HasValue) { GUI.BringWindowToFront(focusReq.Value); GUI.FocusWindow(focusReq.Value); focusReq = null; }
        }

        // ---------- 창 관리 ----------

        // 외출(장소·만남)과 집은 한 창을 같이 쓴다: 다른 쪽을 열면 같은 자리에서 내용만 바뀐다
        static readonly Win[] DayGroup = { Win.Places, Win.Encounter, Win.Home };

        void Open(Win w)
        {
            if (!open.Contains(w))
            {
                float wid = WinWidth[w];
                float x = wid > 820 ? 130 : 140 + (790 - wid) / 2 + (open.Count + 1) * 14;
                var r = new Rect(x, 20 + (open.Count + 1) * 14, wid, 0);
                if (DayGroup.Contains(w))
                    foreach (var other in DayGroup.Where(o => o != w && open.Contains(o)).ToList())
                    {
                        r.x = rects[other].x; r.y = rects[other].y;
                        open.Remove(other); // 행동 전이라 시간은 그대로 (행동 뒤엔 아이콘이 잠겨 여기 오지 않는다)
                    }
                // SNS와 해독 수첩은 나란히 놓이는 자리가 정해져 있다
                if (w == Win.Sns) { r.x = 124; r.y = 12; }
                if (w == Win.Notebook) { r.x = VW - 420 - 24; r.y = 12; }
                open.Add(w);
                rects[w] = r;
            }
            focusReq = (int)w + 1;
        }

        void Close(Win w)
        {
            open.Remove(w);
            if (w is Win.Encounter or Win.Home) S.EndAction();
            if (w == Win.Sns) open.Remove(Win.Notebook);
            if (w == Win.Sns && S.NightDone && S.Ending == Ending.None && !open.Contains(Win.Diary)) { traitsEndNight = true; Open(Win.Traits); }
            if (w == Win.Encounter) enc = null;
            if (w == Win.Traits && traitsEndNight)
            {
                // 잠들기 전 그림 일기
                traitsEndNight = false;
                int gained = S.Followers - followersAtDayStart;
                if (gained != 0) diary.Add(new Sticker($"♥{gained:+#,0;-#,0}", Pal.BarFollow));
                Open(Win.Diary);
            }
            if (w == Win.Diary)
            {
                curtainT = Time.time; curtainDay = S.Day; curtainSwitched = false;
            }
            if (w == Win.Home) homeResult = null;
        }

        void Restart()
        {
            S = new GameState();
            open.Clear(); popups.Clear(); toasts.Clear(); floaters.Clear(); diary.Clear(); pledgeSel = null;
            traitsEndNight = false;
            followersAtDayStart = 0;
            enc = null; homeResult = null; replyFx.Clear(); uploadR = null; target = -2; snsTab = -1; knownMood.Clear();
            selEye = selMouth = Emo.Neutral;
            selDecor = null;
        }

        // ---------- 튜토리얼 코치 ----------

        // 그린 직후 호출: 이 요소의 화면 위치를 기록하고, 튜토리얼이 가리키는 중이면 깜빡이는 테두리를 씌운다
        void Mark(string key) => MarkRect(key, GUILayoutUtility.GetLastRect(), origin);

        void MarkRect(string key, Rect r, Vector2 o)
        {
            if (Event.current.type != EventType.Repaint) return;
            anchors[key] = new Rect(r.x + o.x, r.y + o.y, r.width, r.height);
            if (!CoachVisible || Cur.Target != key || Time.time * 3 % 2 > 1.4f) return; // 반투명 대신 깜빡임으로 (팔레트 밖 색을 만들지 않게)
            GUI.Box(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), GUIContent.none, hiliteStyle);
        }

        // 마우스를 올린 행동의 피로 변화를 게이지에 미리 보여준다
        void HoverFat(float d)
        {
            if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoverFatNext = d;
        }

        float coachH = 90;

        // 후보 자리(대상의 아래·위·오른쪽·왼쪽, 화면 구석) 중 대상과 열린 창을 가장 덜 가리는 곳
        void PlaceCoach()
        {
            float h = Mathf.Max(coachH, 70), w = CoachW;
            if (!lastAnchors.TryGetValue(Cur.Target, out var a)) a = new Rect(VW / 2, 60, 0, 0);
            var cands = new[]
            {
                new Vector2(a.x, a.yMax + 10), new Vector2(a.x, a.y - h - 10),
                new Vector2(a.xMax + 10, a.y), new Vector2(a.x - w - 10, a.y),
                new Vector2(8, VH - BarH - h - 8), new Vector2(VW - w - 8, VH - BarH - h - 8),
                new Vector2(8, 8), new Vector2(VW - w - 8, 8), new Vector2((VW - w) / 2, VH - BarH - h - 8),
            };
            float Overlap(Rect r1, Rect r2) => Mathf.Max(0, Mathf.Min(r1.xMax, r2.xMax) - Mathf.Max(r1.x, r2.x)) * Mathf.Max(0, Mathf.Min(r1.yMax, r2.yMax) - Mathf.Max(r1.y, r2.y));
            float best = float.MaxValue;
            foreach (var c in cands)
            {
                var r = new Rect(Mathf.Clamp(c.x, 4, VW - w - 4), Mathf.Clamp(c.y, 4, VH - BarH - h - 4), w, h);
                float score = Overlap(r, a) * 50 + open.Sum(o => rects.TryGetValue(o, out var wr) ? Overlap(r, wr) : 0)
                    + Vector2.Distance(r.center, a.center) * 0.5f; // 같은 점수면 대상에 가까운 쪽
                if (score < best) { best = score; coachRect.x = r.x; coachRect.y = r.y; }
            }
        }

        void DrawCoach()
        {
            blocked = false;
            GUILayout.Label(Cur.Text, new GUIStyle(label) { fontSize = 17 });
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{step + 1}/{steps.Count}", dotStyle, GUILayout.ExpandWidth(false));
            if (GUILayout.Button("건너뛰기", small, GUILayout.ExpandWidth(false))) pending = () => step = steps.Count;
            GUILayout.FlexibleSpace();
            if (Cur.Next) Btn("▶", () => step++, true, null, GUILayout.Width(60));
            GUILayout.EndHorizontal();
        }

        // ---------- 연출: 판정 도장, 완벽 팝, 숫자 튀어오름, 파츠 날아가기, 화면 깨짐, 피로 번짐 ----------

        void Fly(string text, string to) { flyText = text; flyTo = to; flyT = Time.time; }

        void Float(string anchor, string text) => floaters.Add(new Floater { Anchor = anchor, Text = text, T0 = Time.time });

        (int f, float s, float fat) Snap() => (S.Followers, S.Sus, S.Fat);

        // 수치 변화는 문장 대신 게이지 옆에 튀어오르는 숫자로
        void Floats((int f, float s, float fat) b)
        {
            void One(string a, float d) { if (Mathf.RoundToInt(d) != 0) Float(a, $"{(d > 0 ? "+" : "")}{Mathf.RoundToInt(d):#,0}"); }
            One("bar_f", S.Followers - b.f);
            One("bar_s", S.Sus - b.s);
            One("bar_fat", S.Fat - b.fat);
        }

        void DrawFloaters()
        {
            floaters.RemoveAll(f => Time.time - f.T0 > 1.4f);
            foreach (var f in floaters)
            {
                if (!lastAnchors.TryGetValue(f.Anchor, out var a)) continue;
                float t = Time.time - f.T0;
                float jump = t < 0.25f ? t / 0.25f * 26 : 26 - (t - 0.25f) * 6;
                GUI.Label(new Rect(a.xMax - 60, a.y - jump, 90, 30), f.Text, new GUIStyle(dotStyle) { fontSize = 22 });
            }
        }

        void DrawFx()
        {
            float ca = Time.time - crackT, fa = Time.time - flyT;
            if (ca < 2.5f)
            {
                // 멘탈붕괴: 태블릿 화면이 깨진다
                GUI.Window(FxId + 2, new Rect(0, 0, VW, VH), _ =>
                {
                    var c = new Vector2(VW * 0.55f, VH * 0.4f);
                    var keep = GUI.matrix;
                    for (int i = 0; i < 9; i++)
                    {
                        GUI.matrix = keep;
                        GUIUtility.RotateAroundPivot(i * 40 + 13, c);
                        GUI.DrawTexture(new Rect(c.x, c.y - 1.5f, 260 + i * 37 % 300, 3), Tex(Pal.Line));
                    }
                    GUI.matrix = keep;
                }, GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(FxId + 2);
            }
            if (fa < 0.8f && lastAnchors.TryGetValue("npc", out var from) && lastAnchors.TryGetValue(flyTo, out var to))
            {
                // 얻은 파츠가 조립 바로 날아가 박힌다
                float t = Mathf.Min(1, fa / 0.6f);
                var p = Vector2.Lerp(from.center, to.center, t) + new Vector2(0, -Mathf.Sin(t * Mathf.PI) * 80);
                GUI.Window(FxId + 3, new Rect(p.x - 40, p.y - 30, 80, 60), _ =>
                    GUI.Label(new Rect(0, 0, 80, 60), flyText, new GUIStyle(faceStyle) { fontSize = 34 }), GUIContent.none, GUIStyle.none);
                GUI.BringWindowToFront(FxId + 3);
            }
        }

        // 그린 직후 호출: 마우스가 올라가 있으면 설명을 띄운다
        void Explain(string text) => ExplainRect(text, GUILayoutUtility.GetLastRect(), origin);

        void ExplainRect(string text, Rect r, Vector2 o)
        {
            if (Event.current.type != EventType.Repaint || !r.Contains(Event.current.mousePosition)) return;
            var mouse = Event.current.mousePosition + o;
            // 마우스가 지금 그리는 창이 아닌 다른 창 위에 있으면 (가려진 요소) 설명을 띄우지 않는다
            if (open.Any(w => w != curWin && rects.TryGetValue(w, out var wr) && wr.Contains(mouse))) return;
            tipNext = (text, new Rect(r.x + o.x, r.y + o.y, r.width, r.height));
        }

        void DrawTip()
        {
            var t = tip ?? (Time.time - pinT < 2.2f ? (pinTip, pinRect) : ((string, Rect)?)null);
            if (t == null) return;
            var (text, a) = t.Value;
            var size = label.CalcSize(new GUIContent(text));
            var r = new Rect(Mathf.Clamp(a.xMax + 8, 4, VW - size.x - 30), Mathf.Clamp(a.y, 4, VH - BarH - size.y - 24), size.x + 24, size.y + 20);
            if (r.x < a.xMax && r.xMax > a.x) r.y = a.yMax + 6; // 대상을 가리면 아래로
            GUI.Window(97, r, _ => GUI.Label(new Rect(10, 8, r.width - 16, r.height - 12), text, label), GUIContent.none, coachStyle);
            GUI.BringWindowToFront(97);
        }

        const float CurtainIn = 0.4f, CurtainHold = 2.2f;
        bool CurtainDone => Time.time - curtainT > CurtainIn * 2 + CurtainHold;

        // 날이 지나갈 때: 막이 내려오고 N일차 종료 → N+1일차, 다시 올라간다
        void DrawCurtain()
        {
            float t = Time.time - curtainT;
            if (CurtainDone) return;
            float p = t < CurtainIn ? t / CurtainIn : t < CurtainIn + CurtainHold ? 1 : 1 - (t - CurtainIn - CurtainHold) / CurtainIn;
            GUI.Window(96, new Rect(-Bezel, -Bezel, VW + Bezel * 2, (VH + Bezel * 2) * p), _ =>
            {
                if (p < 1) return;
                bool first = t < CurtainIn + CurtainHold / 2;
                var big = new GUIStyle(roundStyle) { fontSize = 72, alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(0, VH / 2 - 60, VW + Bezel * 2, 90), first ? $"{curtainDay}일차 종료" : S.Ending != Ending.None ? "…" : $"{S.Day}일차", big);
                if (!first && S.Ending == Ending.None)
                    GUI.Label(new Rect(0, VH / 2 + 30, VW + Bezel * 2, 40), $"♥ {S.Followers:#,0} / {Balance.FollowerGoal:#,0}",
                        new GUIStyle(dotStyle) { fontSize = 32, alignment = TextAnchor.MiddleCenter });
            }, GUIContent.none, curtainStyle);
            GUI.BringWindowToFront(96);
        }

        // 피로가 오를수록 배경이 번지고(스미어 그라데이션) 화면에 잡음 줄이 낀다
        void DrawGlitchBars()
        {
            float f = S.Fat / 100f;
            if (f <= 0.6f || Event.current.type != EventType.Repaint) return;
            for (int i = 0; i < (int)((f - 0.5f) * 12); i++)
                if (UnityEngine.Random.value < 0.5f)
                    GUI.DrawTexture(new Rect(0, UnityEngine.Random.Range(0, VH), VW, UnityEngine.Random.Range(2, 7)), Tex(i % 2 == 0 ? Pal.Glitch1 : Pal.Glitch2));
        }

        string Monologue()
        {
            string line = S.Fat < 40 ? "" : S.Fat < 70 ? "(-_-;)" : S.Fat < 100 ? "(・_・;)…(・_・;)" : "(ﾟДﾟ)(ﾟДﾟ)(ﾟДﾟ)(ﾟДﾟ)"; // 주인공은 한글을 쓰지 않는다
            // 피로가 오를수록 혼잣말에 행성의 기호가 섞인다
            var syms = Sym.Keys.ToArray();
            var rnd = new System.Random(S.Day * 31 + (int)S.Fat);
            float p = Mathf.Clamp01((S.Fat - 40) / 100f);
            return new string(line.Select(c => c != ' ' && rnd.NextDouble() < p ? syms[rnd.Next(syms.Length)][0] : c).ToArray());
        }

        // ---------- 에러 팝업: 우측 하단에서 올라왔다가 내려간다 ----------

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
                    GUI.Label(new Rect(4, 4, ToastW - 8, 24), "♥ ERROR ♥", errTitleStyle);
                    GUI.Label(new Rect(12, 34, ToastW - 24, ToastH - 38), t.Body, label);
                }, GUIContent.none, toastStyle);
                GUI.BringWindowToFront(t.Id);
            }
        }

        static readonly string[] ErrSus = { "알 수 없는 시선이 감지되었습니다.", "표정 모듈이 응답하지 않습니다.", "피부.exe 가 예기치 않게 종료되었습니다.", "촉수를 숨기시겠습니까?" };
        static readonly string[] ErrFat = { "의태 드라이버가 과열되었습니다.", "얼굴 온도가 위험 수준입니다.", "얼굴이 흐르고 있습니다. (^▽T)", "ㅇ ㅏ ㄴ ㄴ ㅕ ㅇ 하 세 요" };

        // 인외 연출: 수치가 높을수록 자주, 더 깨진 글씨로. 실제 효과는 없고 경고등 역할만 한다
        void MaybeError()
        {
            float sus = S.Sus / 100f, fat = S.Fat / 100f, heat = Mathf.Max(sus, fat);
            if (heat < 0.5f || UnityEngine.Random.value > heat) return;
            var pool = sus >= fat ? ErrSus : ErrFat;
            int i = Mathf.Min(pool.Length - 1, (int)((heat - 0.5f) * 2 * pool.Length));
            toasts.Add(new Toast { T0 = Time.time, Id = 200 + toastSeq++ % 50, Body = Glitch(pool[i], (heat - 0.5f) * 0.6f) });
        }

        static string Glitch(string s, float p) =>
            new string(s.Select(c => c != ' ' && UnityEngine.Random.value < p ? "■▒▓#"[UnityEngine.Random.Range(0, 4)] : c).ToArray());

        // ---------- 바탕화면 ----------

        const string WaitClose = "결과 창을 닫으면\n다음 시간으로 넘어간다.";

        void DrawIcons()
        {
            var icons = new (string key, string glyph, string name, Action act, bool on, string why)[]
            {
                ("icon_outing", "→", "외출", () => Open(Win.Places), S.CanAct, S.IsNight ? "밤엔 밖에 나가지 않는다.\n지금은 SNS 시간!" : WaitClose),
                ("icon_home", "△", "집", () => Open(Win.Home), S.CanAct, S.IsNight ? "밤엔 집 활동 대신\nSNS를 하자." : WaitClose),
                ("icon_sns", "((•))", "SNS", () => { S.MarkSeen(); Open(Win.Sns); Open(Win.Notebook); focusReq = (int)Win.Sns + 1; }, S.IsNight, "SNS는 밤에 열린다.\n오전·오후에 먼저 움직이자."),
                ("icon_traits", "↑", "특성", () => Open(Win.Traits), true, null),
            };
            for (int i = 0; i < icons.Length; i++)
            {
                var (key, glyph, name, act, on, why) = icons[i];
                var r = new Rect(24, 24 + i * 104, 92, 96);
                int size = glyph.Length > 2 ? 22 : 34; // 긴 아이콘은 줄바꿈되지 않게 작게
                if (GUI.Button(r, $"<size={size}>{glyph}</size>\n<size=14>{name}</size>", iconStyle) && !blocked)
                {
                    if (on) pending = act;
                    else { pinTip = why; pinRect = r; pinT = Time.time; }
                }
                MarkRect(key, r, Vector2.zero);
                if (!on) { ExplainRect(why, r, Vector2.zero); GUI.Label(new Rect(r.xMax - 30, r.y + 2, 28, 24), S.IsNight ? "zZ" : "…", badgeOff); }
                // 지금 할 수 있는 행동에 깜빡이는 배지
                bool now = on && (key == "icon_sns" ? !S.NightDone : key is "icon_outing" or "icon_home");
                if (now && Time.time * 2 % 2 < 1.3f) GUI.Label(new Rect(r.xMax - 28, r.y + 2, 26, 26), "!", badgeOn);
                if (key == "icon_traits" && S.Points > 0) GUI.Label(new Rect(r.xMax - 28, r.y + 2, 26, 26), $"{S.Points}", badgeOn);
            }
            // 해독 수첩은 오른쪽 아래
            var nr = new Rect(VW - 116, VH - BarH - 108, 92, 96);
            if (GUI.Button(nr, "<size=34>¶</size>\n<size=14>해독 수첩</size>", iconStyle) && !blocked) pending = () => Open(Win.Notebook);
            MarkRect("icon_notebook", nr, Vector2.zero);
        }

        void DrawWidgets()
        {
            origin = new Vector2(VW - 344, 16);
            GUILayout.BeginArea(new Rect(origin.x, origin.y, 324, 660));

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Clock();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(panel);
            GUILayout.Label($"목표  ♥ {Balance.FollowerGoal:#,0}  ·  {Balance.Days - S.Day + 1}일 남음", label);
            bool frenzy = Time.time < frenzyUntil;
            Bar("bar_f", "♥ 팔로워", frenzy ? UnityEngine.Random.Range(0, Balance.FollowerGoal) : S.Followers, Balance.FollowerGoal, Pal.BarFollow,
                frenzy ? $"{S.Followers + UnityEngine.Random.Range(-999, 99999):#,0}!!" : $"{S.Followers:#,0}", 0, StatHelp[0]);
            Bar("bar_s", "!? 의심", S.Sus, 100, Pal.BarSus, $"{Mathf.RoundToInt(S.Sus)}", 0, StatHelp[1]);
            Bar("bar_fat", "~ 피로", S.Fat, S.FatMax, Pal.BarFat, $"{Mathf.RoundToInt(S.Fat)}/{S.FatMax}", hoverFat, StatHelp[2]);
            if (statHelp) foreach (var h in StatHelp) GUILayout.Label(h, small);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Monologue(), small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(statHelp ? "×" : "?", closeBtn, GUILayout.Width(26), GUILayout.Height(24)) && !blocked) statHelp = !statHelp;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            Mark("stats");
            GUILayout.EndArea();
        }

        static readonly string[] StatHelp =
        {
            $"♥ 팔로워: 7일째에 {Balance.FollowerGoal:#,0}명이 넘으면 성공.",
            $"!? 의심: 100이면 정체가 들킨다. {Balance.ConquerSusMax} 이상이면 진엔딩도 막힌다.",
            "~ 피로: 한계에 닿으면 멘탈붕괴. 잠과 집 활동으로 내린다.",
        };

        // 동그란 시계: 오전·오후·밤 칸 중 지금 칸이 칠해지고 바늘이 가리킨다
        void Clock()
        {
            GUILayout.BeginVertical();
            var r = GUILayoutUtility.GetRect(150, 150, GUILayout.Width(150));
            Mark("clock");
            ExplainRect("하루는 오전 → 오후 → 밤.\n오전·오후에 한 번씩 움직이고\n밤에는 SNS를 한다.", r, origin);
            GUI.DrawTexture(r, circle);
            var c = r.center;
            float[] angles = { -60, 60, 180 }; // 위에서 시계방향: 오전, 오후, 밤
            string[] names = { "오전", "오후", "밤" };
            for (int i = 0; i < 3; i++)
            {
                float a = (angles[i] - 90) * Mathf.Deg2Rad;
                var lr = new Rect(c.x + Mathf.Cos(a) * 48 - 24, c.y + Mathf.Sin(a) * 48 - 13, 48, 26);
                GUI.Label(lr, names[i], i == (int)S.Slot ? clockOn : clockOff);
            }
            // 바늘: 회전 행렬 대신 점을 이어 그린다 (영역 안 회전은 위치가 어긋난다)
            float ha = (angles[(int)S.Slot] - 90) * Mathf.Deg2Rad;
            for (int d = 0; d <= 28; d += 2)
                GUI.DrawTexture(new Rect(c.x + Mathf.Cos(ha) * d - 2, c.y + Mathf.Sin(ha) * d - 2, 4, 4), Tex(Pal.Hot));
            GUI.Label(new Rect(c.x - 34, c.y - 4, 68, 24), $"{S.Day}일차", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            int left = S.IsNight ? 0 : 2 - (int)S.Slot;
            GUILayout.Label(S.IsNight ? "밤: SNS에 답장하기" : $"남은 행동 {new string('●', left)}{new string('○', 2 - left)}", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();
        }

        void Bar(string key, string name, float v, float max, string color, string value, float preview, string help)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(74));
            var r = GUILayoutUtility.GetRect(130, 26, GUILayout.ExpandWidth(true));
            r.y += 7; r.height = 12;
            GUI.DrawTexture(r, Tex(Pal.BarBg));
            var f = r; f.width *= Mathf.Clamp01(v / max);
            GUI.DrawTexture(f, Tex(color));
            if (preview != 0 && Time.time * 4 % 2 < 1)
            {
                float a = Mathf.Clamp(v + Mathf.Min(0, preview), 0, max), b = Mathf.Clamp(v + Mathf.Max(0, preview), 0, max);
                GUI.DrawTexture(new Rect(r.x + r.width * a / max, r.y, r.width * (b - a) / max, r.height), Tex(preview > 0 ? Pal.Hot : Pal.Ease));
            }
            GUILayout.Label(value, dotStyle, GUILayout.Width(84));
            GUILayout.EndHorizontal();
            Mark(key);
            Explain(help);
        }

        void DrawTaskbar()
        {
            GUI.Box(new Rect(0, VH - BarH, VW, BarH), GUIContent.none, taskbar);
            float x = 8;
            foreach (var w in open)
            {
                if (GUI.Button(new Rect(x, VH - BarH + 4, 70, BarH - 8), Title(w), btn)) focusReq = (int)w + 1;
                x += 74;
            }
            var slot = S.Slot switch { Slot.Morning => "○", Slot.Afternoon => "◑", _ => "●" };
            GUI.Label(new Rect(VW - 150, VH - BarH + 4, 140, BarH - 8), $"{S.Day}일차  {slot}",
                new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleRight, fontSize = 18 });
        }

        // 창 제목도 글자 대신 아이콘
        string Title(Win w) => w switch
        {
            Win.Places => "→", Win.Encounter => S.PendingPlace != null ? PlaceById(S.PendingPlace).Icon : "→",
            Win.Home => "△", Win.Sns => "((•))", Win.Notebook => "¶", Win.Traits => "↑", Win.Diary => "zZ", _ => "★",
        };

        // ---------- 창 공통 ----------

        void TitleBar(string title, float width, Action onClose)
        {
            GUI.Label(new Rect(4, 4, width - 8, 26), title, titleStyle);
            if (onClose != null && GUI.Button(new Rect(width - 30, 7, 22, 20), "×", closeBtn) && !blocked) pending = onClose;
        }

        // 못 누르는 버튼은 반투명 대신 회색 칸으로
        bool Btn(string t, Action act, bool on = true, GUIStyle st = null, params GUILayoutOption[] opts)
        {
            if (!on) { GUILayout.Label(t, offBtn, opts); return false; }
            bool r = GUILayout.Button(t, st ?? btn, opts);
            if (r && !blocked) pending = act;
            return r;
        }

        Win? curWin; // 지금 그리는 창. 바탕화면이면 null

        void DrawWin(Win w)
        {
            curWin = w;
            blocked = popups.Count > 0;
            origin = rects[w].position;
            float width = rects[w].width;
            TitleBar(Title(w), width, w is Win.Ending or Win.Diary ? null : () => Close(w));
            switch (w)
            {
                case Win.Places: DrawPlaces(); break;
                case Win.Encounter: DrawEncounter(); break;
                case Win.Home: DrawHome(); break;
                case Win.Sns: DrawSns(); break;
                case Win.Notebook: DrawNotebook(); break;
                case Win.Traits: DrawTraits(); break;
                case Win.Diary: DrawDiary(); break;
                case Win.Ending: DrawEnding(); break;
            }
            GUI.DragWindow(new Rect(0, 0, width, 32));
            curWin = null;
        }

        void DrawPopup()
        {
            blocked = false;
            var p = popups.Peek();
            TitleBar(p.Title, popupRect.width, null);
            p.Draw?.Invoke();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Btn(p.Ok, () => { popups.Dequeue(); p.OnOk?.Invoke(); }, true, null, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            GUI.DragWindow(new Rect(0, 0, popupRect.width, 32));
        }

        // ---------- 외출: 장소 아이콘 네 개와 하트 세 개가 전부 ----------

        string Hearts(int n) => $"<color={Pal.Heart}>{new string('♥', n)}</color><color={Pal.Gray}>{new string('♥', 3 - n)}</color>";

        void DrawPlaces()
        {
            float fat = S.Traits[Trait.Mimic] >= 1 ? Balance.FatOutMimic : Balance.FatOut;
            var list = GameData.Places;
            for (int i = 0; i < list.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < list.Length; j++)
                {
                    var p = list[j];
                    Btn($"<size=40>{p.Icon}</size>\n<size=20>{Hearts(S.Aff.GetValueOrDefault(p.Id))}</size>",
                        () => { S.PrepareOuting(p.Id); enc = null; selEye = selMouth = Emo.Neutral; Close(Win.Places); Open(Win.Encounter); },
                        true, iconStyle, GUILayout.Height(120));
                    Mark($"place_{j}");
                    HoverFat(fat);
                }
                GUILayout.EndHorizontal();
            }
        }

        void DrawEncounter()
        {
            var ev = enc?.Ev ?? S.Pending;
            if (ev == null) return;
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(400));
            // 행인과 특수 이벤트는 바탕색만 다르다
            GUILayout.BeginVertical(ev.Kind == EvKind.Special ? tintCards[Emo.Joy] : ev.Kind == EvKind.Passerby ? card : paper);
            GUILayout.BeginHorizontal();
            GUILayout.Label(ev.Portrait, new GUIStyle(faceStyle) { fontSize = 34, alignment = TextAnchor.MiddleLeft });
            Mark("npc");
            GUILayout.FlexibleSpace();
            if (S.PendingPlace != null && (ev.Kind == EvKind.Npc || ev.Who == PlaceById(S.PendingPlace).Npc))
                GUILayout.Label(Hearts(S.Aff.GetValueOrDefault(S.PendingPlace)), new GUIStyle(dotStyle) { fontSize = 22 });
            GUILayout.EndHorizontal();
            GUILayout.Label(ev.Situation, label);
            GUILayout.Space(6);
            GUILayout.Label(enc == null ? $"“{ev.Say}”" : enc.Ok ? ev.WinText : ev.FailText, bubble);
            GUILayout.EndVertical();
            Mark("situation");
            GUILayout.EndVertical();

            GUILayout.Space(12);
            GUILayout.BeginVertical();
            if (enc == null)
            {
                Builder(false, true);
                Btn($"<size=20>{Face(selEye, Emo.Neutral)} ▶</size>", React, true, null, GUILayout.Height(44));
                Mark("react");
            }
            else
            {
                // 결과는 창 안에: 큰 도장, 얻은 것, 닫기 버튼만
                GUILayout.Space(20);
                StampIn(enc.Ok ? "성공" : "실패", enc.T, 96);
                GUILayout.Space(16);
                string gain = enc.Mouth.HasValue ? $"+ {E[enc.Mouth.Value].Mouth}" : enc.Decor != null ? $"+ {Face(Emo.Joy, Emo.Joy, enc.Decor)}" : " ";
                GUILayout.Label(gain, new GUIStyle(faceStyle) { fontSize = 40 }, GUILayout.Height(60));
                Mark("gain");
                GUILayout.FlexibleSpace();
                Btn("<size=20>×</size>", () => Close(Win.Encounter), true, null, GUILayout.Height(44));
                Mark("close_result");
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        void React()
        {
            var ev = S.Pending;
            if (ev == null) return;
            var before = Snap();
            var (ok, mouth, decor) = S.DoEncounter(selEye);
            enc = new EncResult { Ev = ev, Ok = ok, Mouth = mouth, Decor = decor, T = Time.time };
            Floats(before);
            diary.Add(new Sticker($"{ev.Portrait} {(ok ? "♥" : "×")}", ok ? Pal.Paper : Pal.Gray));
            if (mouth.HasValue)
            {
                Fly(E[mouth.Value].Mouth, "gain");
                diary.Add(new Sticker($"(・{E[mouth.Value].Mouth}・)", E[mouth.Value].Color));
            }
            if (decor != null)
            {
                diary.Add(new Sticker(Face(Emo.Joy, Emo.Joy, decor), Pal.Special));
                if (!decorTutorial)
                {
                    decorTutorial = true;
                    steps.AddRange(new[]
                    {
                        new Step("gain", "특수 이모지를 얻었다!\n밤에 내 게시물에 붙일 수 있다.", () => enc == null || S.Decors.Count == 0, true, () => enc != null),
                        new Step("icon_sns", "밤이다. 특수 이모지를 써 보자.\n[SNS]를 열자.", () => open.Contains(Win.Sns) || S.Decors.Count == 0, false, () => S.IsNight && !S.Uploaded),
                        new Step("tab_post", "[▣ 내 게시물] 탭으로.", () => snsTab == 0 || S.Decors.Count == 0 || S.Uploaded, false, () => open.Contains(Win.Sns)),
                        new Step("decor", "특수 이모지를 골라 붙이고 [▣ 게시]!\n팔로워가 폭주한다.", () => S.Decors.Count == 0 || S.Uploaded, false, () => open.Contains(Win.Sns) && snsTab == 0),
                    });
                }
            }
            MaybeError();
        }

        void StampIn(string text, float t0, int size = 52)
        {
            var r = GUILayoutUtility.GetRect(200, size + 30, GUILayout.ExpandWidth(true));
            float a = Time.time - t0, s = a < 0.18f ? 2 - a / 0.18f : 1;
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(-8, r.center);
            GUIUtility.ScaleAroundPivot(new Vector2(s, s), r.center);
            var st = new GUIStyle(stampStyle) { fontSize = size };
            var w = st.CalcSize(new GUIContent(text)).x + 30;
            GUI.Label(new Rect(r.center.x - w / 2, r.y, w, r.height), text, st);
            GUI.matrix = keep;
        }

        // ---------- 집: 거울 연습, 피드 구경, 낮잠 ----------

        void DrawHome()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(380));
            if (homeResult is { } hr)
            {
                GUILayout.Space(20);
                StampIn(hr.stamp, homeT, 80);
                if (hr.face != null) GUILayout.Label(hr.face, new GUIStyle(faceStyle) { fontSize = 40 });
                GUILayout.Space(10);
                Btn("<size=20>×</size>", () => Close(Win.Home), true, null, GUILayout.Height(44));
                Mark("close_result");
            }
            else
            {
                HomeCard("○", "거울 연습", Balance.FatHome, Mirror, "가진 파츠로 얼굴 연습.\n시너지를 찾으면 수첩에 남는다.");
                Mark("home_mirror");
                HomeCard("((•))", "피드 구경", Balance.FatHome, Peek, "오늘 밤 팬 글 하나를\n미리 보고 색 단서를 받는다.");
                HomeCard("zZ", "낮잠", Balance.FatNap, Nap, $"크게 쉰다. 의심도 {Balance.NapSus}.");
            }
            GUILayout.EndVertical();

            if (homeResult == null) { GUILayout.Space(12); Builder(true, true); }
            GUILayout.EndHorizontal();
        }

        float homeT;
        void HomeCard(string icon, string name, float fat, Action act, string help)
        {
            Btn($"<size=26>{icon}</size>   <size=16>{name}</size>", act, true, cardBtn, GUILayout.Height(74));
            HoverFat(fat);
            Explain(help);
        }

        void HomeDone(string stamp, string face) { homeResult = (stamp, face); homeT = Time.time; }

        void Mirror()
        {
            var before = Snap();
            var face = Face(selEye, selMouth);
            var (syn, isNew) = S.DoMirror(selEye, selMouth);
            Floats(before);
            HomeDone(isNew ? "시너지!" : "연습", face);
            if (isNew) diary.Add(new Sticker($"★{face}", E[selEye].Color));
        }

        void Peek()
        {
            var before = Snap();
            int i = S.DoPeek();
            Floats(before);
            HomeDone("구경", null);
            diary.Add(new Sticker("((•))", Pal.Bubble));
            if (i < 0) return;
            var p = S.Tonight[i];
            popups.Enqueue(new Popup { Title = "((•))", Draw = () => PostBody(p, true) });
        }

        void Nap()
        {
            var before = Snap();
            S.DoNap();
            Floats(before);
            HomeDone("꿀잠", null);
            diary.Add(new Sticker("zZ", Pal.Bubble));
        }

        // ---------- SNS: 두 탭. [내 게시물] 인스타그램식 사진 올리기 / [멘션] 트위터식 팬 글에 멘션 달기 ----------

        const string MyHandle = "@kao_kao";
        int snsTab = -1;          // 0 내 게시물, 1 멘션, -1은 아직 안 고름(열 때 할 일이 남은 쪽으로)
        int target = -2;          // 멘션 탭에서 답할 글
        string uploadFace;
        int uploadGain, likeTarget;
        readonly List<(string glyph, string color, string text)> postComments = new();
        Vector2 commentScroll;
        Rect commentView;
        readonly HashSet<string> knownMood = new(); // 올려서 반응이 좋았던 장소는 사진 분위기 칩이 보인다

        void AutoTarget()
        {
            if (snsTab < 0) snsTab = 0; // 열면 내 게시물부터
            if (target >= 0 && target < S.Replies.Length && S.Replies[target] == null) return;
            target = Array.FindIndex(S.Replies, r => r == null);
        }

        void Avatar(string glyph, string color, float size = 34)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            GUI.DrawTexture(r, AvatarTex(color));
            GUI.Label(r, glyph, new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleCenter, fontSize = (int)(size * 0.45f) });
        }

        readonly Dictionary<string, Texture2D> avatarTex = new();
        Texture2D AvatarTex(string color) => avatarTex.TryGetValue(color, out var t) ? t : avatarTex[color] = Circle(color, Pal.Line, 64, 3);

        static (string glyph, string color) FanAvatar(Post p) => p.Author switch
        {
            Starlover => ("★", Pal.TitleA), Gloomcat => ("ｪ", Pal.Bubble), Doubter => ("?", Pal.Gray),
            _ => (Handle(p).Substring(Handle(p).Length - 1), Pal.BtnFace),
        };

        void DrawSns()
        {
            AutoTarget();
            int left = S.Replies.Count(r => r == null);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"▣ 내 게시물  {(S.Uploaded ? "●" : "○")}", snsTab == 0 ? tabOn : tabOff, GUILayout.Height(34)) && !blocked) snsTab = 0;
            Mark("tab_post");
            if (GUILayout.Button($"@ 멘션  {S.Replies.Length - left}/{S.Replies.Length}", snsTab == 1 ? tabOn : tabOff, GUILayout.Height(34)) && !blocked) snsTab = 1;
            Mark("tab_mention");
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            if (snsTab == 0) MyPost(); else Timeline();
            GUILayout.Space(4);
            if (S.NightDone) { Btn("<size=18>× 닫기</size>", () => Close(Win.Sns), true, null, GUILayout.Height(48)); Mark("send"); }
            else if (!(snsTab == 0 && S.Uploaded)) Composer();
        }

        // [내 게시물]: 프로필, 오늘 간 곳 사진(장면 설명), 그 위에 붙는 카오모지, 좋아요
        void MyPost()
        {
            GUILayout.BeginVertical(card, GUILayout.Height(330));
            GUILayout.BeginHorizontal();
            Avatar("ψ", Pal.Hot);
            GUILayout.Label(MyHandle, new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleLeft }, GUILayout.Height(34));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            var place = S.Went == null ? null : PlaceById(S.Went);
            var photo = GUILayoutUtility.GetRect(600, S.Uploaded ? 150 : 210, GUILayout.ExpandWidth(true));
            GUI.Box(photo, GUIContent.none, bubble);
            GUI.Label(new Rect(photo.x, photo.y + 6, photo.width, photo.height - 60), place?.Icon ?? "△", new GUIStyle(dotStyle) { fontSize = 80, alignment = TextAnchor.MiddleCenter });
            // 아트 전까지는 사진 속 장면을 글로: 분위기를 추측할 단서
            GUI.Label(new Rect(photo.x + 16, photo.yMax - 54, photo.width - 32, 30), place?.Photo ?? "어질러진 내 방. 별일 없는 하루.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 });
            if (place != null && knownMood.Contains(place.Id))
                GUI.Label(new Rect(photo.xMax - 60, photo.y + 10, 48, 30), E[place.Mood].Shape, chips[place.Mood]);
            var face = S.Uploaded ? uploadFace : Face(selEye, selMouth, selDecor);
            var sr = new Rect(photo.xMax - 200, photo.yMax - 58, 186, 48);
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(-6, sr.center);
            GUI.Label(sr, face, StickerStyle(Pal.Lightest));
            GUI.matrix = keep;
            if (S.Uploaded && uploadR.HasValue)
            {
                GUILayout.BeginArea(new Rect(photo.x + 10, photo.y + 8, 240, 100));
                StampIn(ResultName(uploadR.Value), uploadT, 56);
                GUILayout.EndArea();
            }
            ExplainRect(place == null ? "방 사진은 얼굴과 상관없이 조금 오른다." : "사진 속 장면의 분위기에 어울리는 감정의 얼굴을 붙이자.\n반응이 좋으면 다음부터 분위기 칩이 보인다.", photo, origin);
            // 좋아요는 사진 왼쪽 아래에서 차오른다
            GUILayout.Space(6);
            int likes = S.Uploaded ? Mathf.RoundToInt(likeTarget * Mathf.Clamp01((Time.time - uploadT) / 2.5f)) : 0;
            GUILayout.Label($"♥ {likes:#,0}", new GUIStyle(dotStyle) { fontSize = 22 }, GUILayout.Height(30));
            GUILayout.Space(4);
            // 올린 뒤에는 팬들의 댓글이 하나씩 달린다 (깨진 글)
            if (S.Uploaded)
                for (int k = 0; k < postComments.Count && Time.time - uploadT > 0.8f + k * 0.6f; k++)
                {
                    var (g, c, text) = postComments[k];
                    GUILayout.BeginHorizontal();
                    Avatar(g, c, 24);
                    GUILayout.Space(6);
                    GUILayout.Label(Censor(text, k * 13 + S.Day), new GUIStyle(dotStyle) { fontSize = 15, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(24));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
            GUILayout.EndVertical();
            Mark("upload");
        }

        // [멘션]: 팬들의 글 타임라인. 글마다 멘션 달기, 나중에
        void Timeline()
        {
            GUILayout.BeginVertical(card, GUILayout.Height(330));
            var saved = origin;
            origin = saved + commentView.position - commentScroll; // 스크롤 안의 좌표를 창 좌표로
            commentScroll = GUILayout.BeginScrollView(commentScroll, GUILayout.Height(318));
            for (int i = 0; i < S.Tonight.Count; i++) Tweet(i);
            GUILayout.EndScrollView();
            origin = saved;
            if (Event.current.type == EventType.Repaint) commentView = GUILayoutUtility.GetLastRect();
            GUILayout.EndVertical();
            Mark("feed");
        }

        // 글 본문: 닉네임, 깨진 글(색 단서와 해독된 기호 포함), 읽히는 글자, 단서. 타임라인과 피드 구경이 같이 쓴다
        void PostBody(Post post, bool forceColor, int i = -1)
        {
            var tokens = post.Text.Split(' ');
            // 색 단서: 다 보이거나, 글 앞쪽 절반에만, 또는 안 보인다. 비슷한 색은 모양 아이콘으로 구분
            bool color = post.Hint != Hint.None && (forceColor || S.ShowsColor(post));
            int tinted = !color ? 0 : post.Hint == Hint.Full ? tokens.Length : tokens.Length / 2;
            var hue = post.Answers[0].Eye;
            string colorTip = $"이 글에 {E[hue].Shape} 모양의 색이 비친다.\n어떤 감각인지는 옆 수첩에서 같은 모양을 찾자." +
                (post.Hint == Hint.Partial ? "\n글 일부에만 비친다. 다른 감각이 섞였을지도." : "");

            GUILayout.Label($"{(post.Id.StartsWith("f") ? "→ " : "")}{Handle(post)}", new GUIStyle(dotStyle) { fontSize = 14 });
            GUILayout.BeginHorizontal();
            if (color) { GUILayout.Label($"{E[hue].Shape}?", chips[hue], GUILayout.ExpandWidth(false)); Explain(colorTip); HoverEmo(hue); }
            // 팔로워가 늘수록 팬들도 카오모지로 말한다: 글 사이사이나 끝에, 정답과 상관없는 얼굴로
            int kao = FanKaomoji, seed = post.Id.Sum(ch => ch);
            var kaoAt = Enumerable.Range(0, kao).Select(n => (seed + n * 5) % (tokens.Length + 1)).ToList();
            void KaoBefore(int k) { foreach (var n in Enumerable.Range(0, kao).Where(n => kaoAt[n] == k)) GUILayout.Label(FanFaces[(seed + n * 3) % FanFaces.Length], postStyle, GUILayout.ExpandWidth(false)); }
            bool firstSym = true;
            for (int k = 0; k < tokens.Length; k++)
            {
                KaoBefore(k);
                var t = tokens[k];
                if (!Sym.TryGetValue(t, out var e))
                {
                    GUILayout.Label(t, k < tinted ? tintText[hue] : postStyle, GUILayout.ExpandWidth(false));
                    if (k < tinted) { Explain(colorTip); HoverEmo(hue); }
                    continue;
                }
                if (S.Solved(t))
                {
                    GUILayout.Label(t, chips[e], GUILayout.ExpandWidth(false)); // 해독한 기호는 그 감각의 색으로만. 이름은 수첩에서
                }
                else
                {
                    GUILayout.Label(t, k < tinted ? tintText[hue] : symStyle, GUILayout.ExpandWidth(false));
                }
                HoverSym(t);
                if (firstSym && i >= 0) { Mark($"sym_{i}"); firstSym = false; }
            }
            KaoBefore(tokens.Length);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (S.Followers > 0) GUILayout.Label(Reveal(post), small);
            GUILayout.Label(string.Join("  ", post.Clues.Take(S.Traits[Trait.Decode] >= 1 ? 2 : 1)), small);
            if (i >= 0) Mark($"clue_{i}");
        }

        static readonly string[] FanFaces = { "(・_・)ノ", "(・o・)", "(・∀・)", "(・ｪ・)", "(・.・)", "ヽ(・_・)ノ" };

        // 팔로워 비율만큼 실제 문장의 글자가 읽힌다 (글자마다 정해진 순서로 하나씩 풀린다)
        string Reveal(Post p) => Censor(p.Plain, p.Id.Sum(c => c));

        // 한글만 가린다(기호와 카오모지는 그대로). 팔로워 비율만큼 정해진 순서로 풀린다
        string Censor(string text, int seed)
        {
            float ratio = Mathf.Clamp01((float)S.Followers / Balance.FollowerGoal);
            return new string(text.Select((c, k) => c < 0xAC00 || c > 0xD7A3 || (seed + k * 37) % 100 < ratio * 100 ? c : '■').ToArray());
        }

        int FanKaomoji => S.Followers >= Balance.FollowerGoal * 0.9f ? 3 : S.Followers >= Balance.FollowerGoal * 0.6f ? 2 : S.Followers >= Balance.FollowerGoal * 0.3f ? 1 : 0;

        void Tweet(int i)
        {
            var post = S.Tonight[i];
            var rep = S.Replies[i];
            var (glyph, color) = FanAvatar(post);
            GUILayout.BeginVertical(target == i && rep == null ? selCard : GUIStyle.none);
            GUILayout.BeginHorizontal();
            Avatar(glyph, color);
            GUILayout.BeginVertical();
            PostBody(post, false, i);
            if (rep == null)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(target == i ? "@ 멘션 쓰는 중…" : "@ 멘션 달기", linkBtn, GUILayout.ExpandWidth(false)) && !blocked) target = i;
                Mark($"reply_{i}");
                if (S.CanHold) { if (GUILayout.Button("‖ 나중에", linkBtn, GUILayout.ExpandWidth(false)) && !blocked) pending = () => Hold(i); }
                Mark($"hold_{i}");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // 내 멘션과 팬의 반응: 왼쪽 선으로 이어진 한 단계 스레드
            if (rep != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(16);
                var line = GUILayoutUtility.GetRect(3, 10, GUILayout.Width(3), GUILayout.ExpandHeight(true));
                GUI.DrawTexture(line, Tex(Pal.Line));
                GUILayout.Space(12);
                GUILayout.BeginVertical();
                if (rep.Held) GUILayout.Label("‖ 나중에 답하기로 했다", small);
                else
                {
                    var fx = replyFx.TryGetValue(post.Id, out var v) ? v : (null, -9f);
                    GUILayout.BeginHorizontal();
                    Avatar("ψ", Pal.Hot, 28);
                    GUILayout.Label($"{MyHandle}  {rep.Face}", new GUIStyle(dotStyle) { fontSize = 16, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(28));
                    GUILayout.BeginVertical(GUILayout.Width(110));
                    StampIn(ResultName(rep.R.Value), fx.Item2, 24);
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    if (fx.Item1 != null)
                    {
                        GUILayout.BeginHorizontal();
                        Avatar(glyph, color, 28);
                        GUILayout.Label(Censor(fx.Item1, post.Id.Sum(c => c) + 7), label, GUILayout.Height(28));
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            Mark($"post_{i}");
            GUILayout.Space(6);
        }

        // 입력창: 탭마다 따로. 내 게시물은 사진에 붙일 얼굴, 멘션은 누구에게 보낼 얼굴
        void Composer()
        {
            bool post = snsTab == 0;
            GUILayout.BeginVertical(card);
            GUILayout.BeginHorizontal();
            Avatar("ψ", Pal.Hot, 28);
            GUILayout.Label(post ? MyHandle : "→", new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleLeft }, GUILayout.Height(28));
            if (!post && target >= 0) { var (tg, tc) = FanAvatar(S.Tonight[target]); Avatar(tg, tc, 28); } // 누구에게: 글자 대신 상대 아바타
            GUILayout.FlexibleSpace();
            if (post && !S.Uploaded && S.Decors.Count > 0)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-", selDecor == null ? partSel[Emo.Neutral] : partBtn[Emo.Neutral], GUILayout.Width(40), GUILayout.Height(30)) && !blocked) selDecor = null;
                foreach (var d in S.Decors)
                    if (GUILayout.Button($"{d.L} {d.R}", selDecor == d ? partSel[Emo.Shy] : partBtn[Emo.Shy], GUILayout.Width(60), GUILayout.Height(30)) && !blocked) selDecor = d;
                GUILayout.EndHorizontal();
                Mark("decor");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(370));
            PartRow(S.Eyes, true, true);
            Mark("eyes");
            PartRow(S.Mouths, false, true);
            Mark("mouths");
            GUILayout.EndVertical();
            var syn = SynergyOf(selEye, selMouth);
            GUILayout.BeginVertical(tintCards[selEye], GUILayout.ExpandWidth(true), GUILayout.Height(104));
            GUILayout.Label(Face(selEye, selMouth, post ? selDecor : null), new GUIStyle(faceStyle) { fontSize = 34 }, GUILayout.Height(64));
            if (syn != null) GUILayout.Label(S.FoundSynergies.Contains(syn.Name) ? syn.Name : "?", new GUIStyle(roundStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
            GUILayout.EndVertical();
            if (post) Btn("<size=18>▣ 게시</size>", Upload, !S.Uploaded, null, GUILayout.Width(120), GUILayout.Height(104));
            else Btn("<size=18>@ 보내기</size>", () => Reply(target), target >= 0, null, GUILayout.Width(120), GUILayout.Height(104));
            Mark("send");
            if (!post && target >= 0) HoverFat(Balance.FatReply);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void Upload()
        {
            var before = Snap();
            var decor = selDecor;
            var face = Face(selEye, selMouth, decor);
            var (r, f) = S.DoUpload(selEye, selMouth, decor);
            if (decor != null) { frenzyUntil = Time.time + 1.8f; S.Decors.Remove(decor); selDecor = null; } // shortcut: 장식은 한 번 쓰면 사라진다고 가정, 스펙 확인 필요
            if (S.Went != null && r is Result.Perfect or Result.Success) knownMood.Add(S.Went);
            Floats(before);
            uploadR = r; uploadT = Time.time; uploadFace = face; uploadGain = f;
            // 좋아요는 팔로워 수에 비례, 반응이 좋을수록 많이. 댓글도 그만큼 달린다
            float factor = r switch { Result.Perfect => 0.5f, Result.Success => 0.35f, Result.Creepy => 0.6f, Result.Meh => 0.1f, Result.Wrong => 0.03f, _ => 0.08f };
            likeTarget = Mathf.RoundToInt(Mathf.Max(30, S.Followers) * factor) + (decor != null ? 999 : 0);
            postComments.Clear();
            int n = r switch { Result.Perfect => 4, Result.Success => 3, Result.Creepy => 3, Result.Wrong => 2, _ => 1 };
            string[] lines = r switch
            {
                Result.Perfect or Result.Success => new[] { "너무 귀여워", "오늘도 최고야", "이 표정 뭐야ㅋㅋ", "저장했어" },
                Result.Creepy => new[] { "왜 무섭지…", "표정이 이상해", "근데 계속 보게 돼" },
                Result.Wrong => new[] { "분위기랑 안 맞아", "??" },
                _ => new[] { "음" },
            };
            for (int k = 0; k < n; k++)
                postComments.Add(("♣♠§∞‡"[(k + S.Day) % 5].ToString(), new[] { Pal.BtnFace, Pal.Bubble, Pal.TitleA, Pal.Gray }[k % 4], FanKaomoji > 0 ? $"{lines[k % lines.Length]} {FanFaces[(k + S.Day) % FanFaces.Length]}" : lines[k % lines.Length]));
            diary.Add(new Sticker($"↑{face}", Pal.Bubble));
            AfterNightAction();
        }

        void Reply(int i)
        {
            if (i < 0) return;
            var before = Snap();
            var post = S.Tonight[i];
            var (r, _, newly, newSyn) = S.DoReply(i, selEye, selMouth);
            Floats(before);
            replyFx[post.Id] = (newSyn != null ? $"{Fan(post, r)}  [{newSyn.Name}]" : Fan(post, r), Time.time);
            diary.Add(new Sticker($"{Face(selEye, selMouth)}{ResultMark(r)}", E[selEye].Color));
            foreach (var s in newly) diary.Add(new Sticker($"{s}{E[Sym[s]].Shape}", E[Sym[s]].Color));
            MaybeError();
            AfterNightAction();
        }

        static string ResultMark(Result r) => r switch
        {
            Result.Perfect => "★", Result.Success => "○", Result.Creepy => "!?", Result.Meh => "…", _ => "×",
        };

        void Hold(int i)
        {
            if (!S.DoHold(i)) return;
            diary.Add(new Sticker("‖", Pal.Gray));
            AfterNightAction();
        }

        // 다 끝나도 창은 닫지 않는다. 결과를 보고 직접 닫으면 특성 창이 열린다
        void AfterNightAction()
        {
            if (S.NightDone && S.Ending == Ending.None) S.GrantNightPoint();
        }

        // ---------- 카오모지 조립 바 (만남, 집, SNS 공용) ----------

        void Builder(bool withMouth, bool live)
        {
            GUILayout.BeginVertical(card, GUILayout.Width(360));
            PartRow(S.Eyes, true, live);
            Mark("eyes");
            if (withMouth) { PartRow(S.Mouths, false, live); Mark("mouths"); }
            var mouth = withMouth ? selMouth : Emo.Neutral;
            var syn = withMouth ? SynergyOf(selEye, mouth) : null;
            // 미리보기 바탕은 눈의 감정 색
            GUILayout.BeginVertical(tintCards[selEye]);
            GUILayout.Label(Face(selEye, mouth, live && withMouth && open.Contains(Win.Sns) ? selDecor : null), faceStyle, GUILayout.Height(64));
            if (syn != null) GUILayout.Label(S.FoundSynergies.Contains(syn.Name) ? syn.Name : "?", new GUIStyle(roundStyle) { alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();
            GUILayout.EndVertical();
        }

        void PartRow(List<Emo> owned, bool eye, bool live)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(eye ? "눈" : "입", new GUIStyle(label) { alignment = TextAnchor.MiddleLeft }, GUILayout.Width(26), GUILayout.Height(46));
            foreach (var e in PartOrder)
            {
                var opts = new[] { GUILayout.Width(50), GUILayout.Height(46) };
                if (!owned.Contains(e)) { GUILayout.Label("?", offBtn, opts); continue; }
                bool sel = eye ? selEye == e : selMouth == e;
                if (GUILayout.Button(eye ? E[e].EyeL + " " + E[e].EyeR : E[e].Mouth, sel ? partSel[e] : partBtn[e], opts) && live && !blocked)
                { if (eye) selEye = e; else selMouth = e; }
                Mark($"{(eye ? "eye" : "mouth")}_{e}");
            }
            GUILayout.EndHorizontal();
        }

        // ---------- 수첩: 감정 이름은 여기서만 본다 ----------

        string hoverSym, hoverSymNext;
        Emo? hoverEmo, hoverEmoNext;
        void HoverSym(string t) { if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoverSymNext = t; }
        void HoverEmo(Emo e) { if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoverEmoNext = e; }

        void DrawNotebook()
        {
            // SNS 옆에 나란히 놓고 쓰도록 좁게. 순수 감정은 한 색, 시너지는 눈 색 + 입 색 반반
            int need = S.Traits[Trait.Decode] >= 2 ? 1 : 2;
            GUILayout.BeginHorizontal();
            foreach (var e in Feelings) ColorCell(E[e].Color, E[e].Color, E[e].Name, $"{E[e].Shape}{Face(e, e)}", 72, false, hoverEmo == e);
            GUILayout.EndHorizontal();
            Mark("nb");

            GUILayout.Label($"기호 {S.SolvedCount}/{Sym.Count}", label);
            var seen = S.Seen.ToList();
            if (seen.Count == 0) GUILayout.Label("밤에 SNS를 열면 기호가 모인다.", small);
            for (int i = 0; i < seen.Count; i += 5)
            {
                GUILayout.BeginHorizontal();
                foreach (var s in seen.Skip(i).Take(5))
                {
                    var e = Sym[s];
                    bool hot = hoverSym == s;
                    if (S.Solved(s)) ColorCell(E[e].Color, E[e].Color, s, E[e].Name, 72, true, hot);
                    else if (S.Stickers.TryGetValue(s, out var c)) ColorCell(Pal.Lightest, Pal.Lightest, s, $"{E[e].Shape}{new string('●', c)}{new string('○', need - c)}", 72, true, hot);
                    else ColorCell(Pal.Paper, Pal.Paper, s, "?", 72, true, hot);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.Label($"시너지 {S.FoundSynergies.Count}/{Synergies.Length}", label);
            GUILayout.BeginHorizontal();
            foreach (var syn in Synergies)
            {
                bool found = S.FoundSynergies.Contains(syn.Name);
                if (found) ColorCell(E[syn.Eye].Color, E[syn.Mouth].Color, syn.Name, Face(syn.Eye, syn.Mouth), 72);
                else ColorCell(Pal.Paper, Pal.Paper, "?", "( ? ? )", 72);
            }
            GUILayout.EndHorizontal();
        }

        // 칸 하나: 왼쪽 반은 a색, 오른쪽 반은 b색 (같으면 한 색). 위에 이름, 아래에 작은 글
        void ColorCell(string a, string b, string top, string bottom, float w, bool bigTop = false, bool hot = false)
        {
            var r = GUILayoutUtility.GetRect(w, 58, GUILayout.Width(w), GUILayout.Height(58));
            GUI.DrawTexture(new Rect(r.x, r.y, r.width / 2, r.height), Tex(a));
            GUI.DrawTexture(new Rect(r.x + r.width / 2, r.y, r.width / 2, r.height), Tex(b));
            GUI.Box(r, GUIContent.none, cellFrame);
            // SNS에서 가리킨 기호·감각: 칸의 글자가 두근두근 커졌다 작아진다
            int pulse = hot ? Mathf.RoundToInt(6 + 6 * Mathf.Sin(Time.time * 10)) : 0;
            var topStyle = bigTop ? new GUIStyle(symStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 22 + pulse } : new GUIStyle(roundStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 24 + pulse };
            GUI.Label(new Rect(r.x, r.y + 2, r.width, 30), top, topStyle);
            GUI.Label(new Rect(r.x, r.y + 32, r.width, 24), bottom, new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 12 });
        }

        // ---------- 특성 ----------

        void DrawTraits()
        {
            GUILayout.Label($"↑ {S.Points}", new GUIStyle(dotStyle) { fontSize = 22 });
            TraitRow(Trait.Decode, "해독", "단서 +1", "스티커 1장으로 해독");
            TraitRow(Trait.Mimic, "의태", $"외출 피로 {Balance.FatOut} → {Balance.FatOutMimic}", "멘탈붕괴 때 의심 절반");
            TraitRow(Trait.Empathy, "공감", "팬 글에 감정 색", "섬뜩해도 의심 그대로");
            if (traitsEndNight)
            {
                Btn("<size=20>zZ ▶</size>", () => Close(Win.Traits), true, null, GUILayout.Height(44));
                Mark("sleep");
                HoverFat(Balance.FatSleep);
            }
        }

        void TraitRow(Trait t, string name, string l1, string l2)
        {
            int lv = S.Traits[t];
            GUILayout.BeginVertical(card);
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, big, GUILayout.ExpandWidth(false));
            GUILayout.Label($"{new string('●', lv)}{new string('○', 2 - lv)}", new GUIStyle(dotStyle) { fontSize = 18 }, GUILayout.Height(28));
            GUILayout.FlexibleSpace();
            Btn("↑", () => S.Raise(t), S.CanRaise(t), null, GUILayout.Width(56));
            Mark($"trait_{(int)t}");
            GUILayout.EndHorizontal();
            TraitLine(lv >= 1, "Lv1", l1);
            TraitLine(lv >= 2, "Lv2", l2);
            GUILayout.EndVertical();
        }

        void TraitLine(bool on, string lv, string text)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(on ? "●" : "○", dotStyle, GUILayout.Width(20), GUILayout.Height(22));
            GUILayout.Label($"{lv}  {text}", small, GUILayout.Height(22));
            GUILayout.EndHorizontal();
        }

        // ---------- 그림 일기: 글씨 없이 그림과 스티커로 하루를 기록한다 ----------

        // 다짐 스티커: 하나를 골라야 잠들 수 있다. 다음 날을 조금 돕는다
        static readonly (Pledge p, string icon, string color, string tip)[] Pledges =
        {
            (Pledge.Hint, "★?", Pal.TitleA, "공감 연습: 내일 밤 글 하나의 색이 미리 보인다."),
            (Pledge.Sleep, "zZ+", Pal.Bubble, "푹 자기: 피로가 10 더 내려간다."),
            (Pledge.Review, "¶+", Pal.BtnFace, "복습: 아직 못 푼 기호 하나에 스티커가 한 장 붙는다."),
        };
        Pledge? pledgeSel;
        float pledgeT = -9;
        Rect pledgeFrom;

        void DrawDiary()
        {
            // 글씨 없이 그림과 스티커로: 오늘 한 일(정산) + 내일의 다짐 하나
            GUILayout.BeginVertical(paper);
            GUILayout.Label($"{S.Day}/{Balance.Days}", new GUIStyle(dotStyle) { fontSize = 26 });
            var area = GUILayoutUtility.GetRect(580, 250);
            var keep = GUI.matrix;
            for (int i = 0; i < diary.Count; i++)
            {
                var st = diary[i];
                // 스크랩북처럼 조금씩 비뚤게 붙인다
                float x = area.x + 10 + i % 4 * 142 + i * 23 % 17, y = area.y + 6 + i / 4 * 62 + i * 31 % 9;
                var r = new Rect(x, y, 128, 52);
                GUI.matrix = keep;
                GUIUtility.RotateAroundPivot(i * 37 % 15 - 7, r.center);
                GUI.Label(r, st.Text, StickerStyle(st.Color));
            }
            // 고른 다짐 스티커: 일기장 오른쪽 아래로 날아가 꾹 붙는다
            if (pledgeSel.HasValue)
            {
                var pl = Pledges.First(x => x.p == pledgeSel);
                var to = new Rect(area.xMax - 150, area.yMax - 64, 128, 56);
                float t = Mathf.Clamp01((Time.time - pledgeT) / 0.35f);
                var pos = Vector2.Lerp(pledgeFrom.center, to.center, t);
                float sc = t < 1 ? 1.6f - 0.6f * t : 1 + 0.15f * Mathf.Max(0, 1 - (Time.time - pledgeT - 0.35f) * 6); // 붙는 순간 꾹
                var r = new Rect(pos.x - 64 * sc, pos.y - 28 * sc, 128 * sc, 56 * sc);
                GUI.matrix = keep;
                GUIUtility.RotateAroundPivot(8 - 16 * t + 4, r.center);
                GUI.Label(r, pl.icon, new GUIStyle(StickerStyle(pl.color)) { fontSize = Mathf.RoundToInt(18 * sc) });
            }
            GUI.matrix = keep;
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("→", new GUIStyle(dotStyle) { fontSize = 26, alignment = TextAnchor.MiddleCenter }, GUILayout.Width(40), GUILayout.Height(56));
            foreach (var (p, icon, color, tip) in Pledges)
            {
                bool sel = pledgeSel == p;
                if (GUILayout.Button(sel ? "" : icon, sel ? offBtn : StickerStyle(color), GUILayout.Width(120), GUILayout.Height(56)) && !blocked && !sel)
                { pledgeSel = p; pledgeT = Time.time; pledgeFrom = GUILayoutUtility.GetLastRect(); } // 고른 칸은 비고 스티커는 일기장으로
                Explain(tip);
            }
            GUILayout.EndHorizontal();
            Mark("pledge");
            Btn("<size=20>zZ ▶</size>", () => Close(Win.Diary), pledgeSel.HasValue, null, GUILayout.Height(44));
        }

        GUIStyle StickerStyle(string color)
        {
            if (stickerStyles.TryGetValue(color, out var s)) return s;
            s = Boxed(Bevel(color, Pal.Lightest, Pal.Line), 6);
            s.font = dot; s.fontSize = 18; s.alignment = TextAnchor.MiddleCenter; s.wordWrap = false;
            return stickerStyles[color] = Still(s);
        }

        // ---------- 엔딩 ----------

        void DrawEnding()
        {
            var (title, text) = Endings[S.Ending];
            GUILayout.Space(16);
            GUILayout.Label(title, new GUIStyle(roundStyle) { fontSize = 48, alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(10);
            GUILayout.Label(text, new GUIStyle(label) { fontSize = 17, alignment = TextAnchor.MiddleCenter });
            if (S.Ending == Ending.Conquer)
                foreach (var cut in S.EpilogueCuts) GUILayout.Label(cut, paper);
            GUILayout.Space(10);
            GUILayout.Label($"팔로워 {S.Followers:#,0}   의심 {Mathf.RoundToInt(S.Sus)}   해독 {S.SolvedCount}/{Sym.Count}   시너지 {S.FoundSynergies.Count}/{Synergies.Length}",
                new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleCenter });
            // 진엔딩 조건: 무엇이 모자랐는지
            if (S.Ending != Ending.Conquer)
                GUILayout.Label($"진엔딩 조건   팔로워 {Balance.FollowerGoal:#,0} 이상 {(S.Followers >= Balance.FollowerGoal ? "○" : "×")}   의심 {Balance.ConquerSusMax} 미만 {(S.Sus < Balance.ConquerSusMax ? "○" : "×")}",
                    new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            GUILayout.Space(16);
            Btn("<size=20>◀◀</size>", Restart, true, null, GUILayout.Height(44));
        }

        // 팬의 한마디는 읽는 재미가 있는 글이라 남긴다
        static string Fan(Post p, Result r) => (p.Author, r) switch
        {
            (Doubter, Result.Perfect) => "…내가 너무 예민했나",
            (Doubter, Result.Wrong) => "역시. 사람이 아니야.",
            (Starlover, Result.Perfect) when p.WantsCreepy => "그 얼굴!!! 그 얼굴이 보고 싶었어!!!",
            (Gloomcat, Result.Perfect or Result.Success) => "…조금 나아졌어",
            (Gloomcat, _) => "그렇구나…",
            _ => r switch
            {
                Result.Perfect => "꺄아아!!", Result.Success => "좋아요♥", Result.Creepy => "…무서운데 계속 보게 돼",
                Result.Meh => "읽씹당한 기분", _ => "??? 뭐임",
            },
        };

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

        // 시계판: 테두리 있는 원
        static Texture2D Circle(string face, string ring, int size = 128, int thick = 4)
        {
            var t = new Texture2D(size, size) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) / 2f;
            for (int x = 0; x < size; x++)
                for (int y = 0; y < size; y++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    t.SetPixel(x, y, d > c ? Color.clear : d > c - thick ? C(ring) : C(face));
                }
            t.Apply();
            return t;
        }

        // 두 칸짜리 텍스처: 포인트 필터면 딱 두 색, 바이리니어면 그라데이션
        static Texture2D Gradient(string a, string b, bool vertical)
        {
            var t = new Texture2D(vertical ? 1 : 2, vertical ? 2 : 1) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
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

        GUIStyle Boxed(Texture2D bg, int pad = 10)
        {
            var s = new GUIStyle { font = body, border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(pad, pad, pad, pad), margin = new RectOffset(4, 4, 4, 4), richText = true, wordWrap = true };
            s.normal.background = bg;
            s.normal.textColor = Color.black;
            return Still(s);
        }

        GUIStyle Button(Texture2D face, Texture2D hover, Texture2D pressed, Font f = null, int size = 15)
        {
            var s = new GUIStyle { font = f ?? body, fontSize = size, richText = true, wordWrap = true, alignment = TextAnchor.MiddleCenter,
                border = new RectOffset(1, 1, 1, 1), padding = new RectOffset(8, 8, 6, 6), margin = new RectOffset(3, 3, 3, 3) };
            s.normal.background = s.focused.background = s.onNormal.background = s.onFocused.background = face;
            s.hover.background = s.onHover.background = hover;
            s.active.background = s.onActive.background = pressed;
            foreach (var st in new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused }) st.textColor = Color.black;
            return s;
        }

        void InitStyles()
        {
            if (btn != null) return;
            GUI.skin.font = body;
            GUIStyle Text(Font f, int size, FontStyle fs = FontStyle.Normal)
            {
                var s = new GUIStyle { font = f, fontSize = size, richText = true, wordWrap = true, fontStyle = fs, padding = new RectOffset(2, 2, 2, 2), margin = new RectOffset(2, 2, 2, 2) };
                s.normal.textColor = Color.black; // 글씨는 일단 검정
                return Still(s);
            }

            label = Text(body, 15);
            small = Text(body, 13);
            big = Text(body, 19, FontStyle.Bold);
            dotStyle = Text(dot, 16); dotStyle.wordWrap = false;
            roundStyle = Text(round, 30); roundStyle.wordWrap = false;
            faceStyle = Text(dot, 46); faceStyle.alignment = TextAnchor.MiddleCenter; faceStyle.wordWrap = false;
            postStyle = Text(dot, 22); postStyle.wordWrap = false;
            symStyle = Text(dot, 24); symStyle.wordWrap = false;

            bgDay = Gradient(Pal.BgTop, Pal.BgBottom, true);
            bgDay.filterMode = FilterMode.Bilinear; // 배경만 스펙대로 두 팔레트 색 사이를 부드럽게 잇는다
            bgSmear = Gradient(Pal.SmearTop, Pal.SmearBottom, true);
            bgSmear.filterMode = FilterMode.Bilinear;
            circle = Circle(Pal.Lightest, Pal.Line);

            winStyle = Boxed(Bevel(Pal.WinFace, Pal.Lightest, Pal.Line));
            winStyle.padding = new RectOffset(12, 12, 38, 12);
            toastStyle = Boxed(Bevel(Pal.BtnLight, Pal.Lightest, Pal.Hot));
            coachStyle = Boxed(Bevel(Pal.BtnLight, Pal.Lightest, Pal.Hot), 12);
            hiliteStyle = new GUIStyle { border = new RectOffset(3, 3, 3, 3) };
            hiliteStyle.normal.background = Frame(Pal.Hot, 8, 3);
            bezelStyle = new GUIStyle { border = new RectOffset(18, 18, 18, 18) };
            bezelStyle.normal.background = Frame(Pal.Bezel, 40, 18);
            stampStyle = Text(round, 52);
            stampStyle.alignment = TextAnchor.MiddleCenter; stampStyle.wordWrap = false;
            stampStyle.border = new RectOffset(5, 5, 5, 5);
            stampStyle.normal.background = Frame(Pal.Hot, 14, 5);
            Still(stampStyle);

            titleStyle = Text(dot, 18);
            titleStyle.alignment = TextAnchor.MiddleLeft; titleStyle.wordWrap = false;
            titleStyle.padding = new RectOffset(10, 30, 0, 0);
            titleStyle.normal.background = Gradient(Pal.TitleA, Pal.TitleB, false);
            Still(titleStyle);
            errTitleStyle = new GUIStyle(titleStyle) { fontSize = 15 };
            errTitleStyle.normal.background = Gradient(Pal.ErrA, Pal.ErrB, false);
            Still(errTitleStyle);

            btn = Button(Bevel(Pal.BtnFace, Pal.BtnLight, Pal.BtnDark), Bevel(Pal.BtnLight, Pal.Lightest, Pal.BtnDark), Bevel(Pal.BtnPress, Pal.BtnDark, Pal.BtnLight));
            offBtn = Boxed(Bevel(Pal.Gray, Pal.Gray, Pal.Line), 6);
            offBtn.alignment = TextAnchor.MiddleCenter;
            offBtn.font = dot;
            closeBtn = new GUIStyle(btn) { font = dot, padding = new RectOffset(0, 0, 0, 2), margin = new RectOffset(0, 0, 0, 0) };

            card = Boxed(Bevel(Pal.Lightest, Pal.Lightest, Pal.Gray));
            paper = Boxed(Bevel(Pal.Paper, Pal.BtnLight, Pal.PaperDark));
            panel = Boxed(Bevel(Pal.WinFace, Pal.Lightest, Pal.Line), 12);
            bubble = Boxed(Bevel(Pal.Bubble, Pal.Lightest, Pal.BubbleDark));
            bubble.fontSize = 18; bubble.alignment = TextAnchor.MiddleCenter;
            Still(bubble);
            taskbar = Boxed(Bevel(Pal.Taskbar, Pal.Paper, Pal.BtnDark), 0);

            iconStyle = Button(Tex(Pal.Lightest), Tex(Pal.WinFace), Tex(Pal.BtnLight), dot, 14);
            iconStyle.normal.background = iconStyle.focused.background = iconStyle.onNormal.background = null; // 바탕화면 아이콘은 평소엔 바탕 없이
            iconStyle.wordWrap = false;
            cardBtn = Button(Bevel(Pal.Lightest, Pal.Lightest, Pal.Line), Bevel(Pal.BtnLight, Pal.Lightest, Pal.Line), Bevel(Pal.BtnPress, Pal.Line, Pal.Lightest), body, 16);
            cardBtn.alignment = TextAnchor.MiddleLeft; cardBtn.padding = new RectOffset(16, 8, 6, 6);
            badgeOn = Boxed(Bevel(Pal.Hot, Pal.Lightest, Pal.Line), 0);
            badgeOn.font = body; badgeOn.fontSize = 18; badgeOn.fontStyle = FontStyle.Bold; badgeOn.alignment = TextAnchor.MiddleCenter; badgeOn.wordWrap = false;
            badgeOff = Boxed(Bevel(Pal.Gray, Pal.Lightest, Pal.Line), 0);
            badgeOff.font = dot; badgeOff.fontSize = 12; badgeOff.alignment = TextAnchor.MiddleCenter; badgeOff.wordWrap = false;
            clockOn = Boxed(Bevel(Pal.Hot, Pal.Lightest, Pal.Line), 0);
            clockOn.alignment = TextAnchor.MiddleCenter; clockOn.fontSize = 14; clockOn.wordWrap = false;
            clockOff = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = false };
            curtainStyle = Boxed(Tex("#D5B7F5"), 0);
            selCard = Boxed(Bevel(Pal.BtnLight, Pal.Lightest, Pal.Hot), 6); // 입력창이 향하는 곳
            linkBtn = Button(Tex(Pal.Lightest), Tex(Pal.BtnLight), Tex(Pal.BtnPress), body, 13); // 인스타그램식 '답글 달기' 글자 버튼
            linkBtn.normal.background = linkBtn.focused.background = linkBtn.onNormal.background = null;
            linkBtn.fontStyle = FontStyle.Bold; linkBtn.padding = new RectOffset(4, 4, 2, 2);
            tabOn = Button(Bevel(Pal.TitleA, Pal.Lightest, Pal.Line), Bevel(Pal.TitleA, Pal.Lightest, Pal.Line), Bevel(Pal.TitleA, Pal.Line, Pal.Lightest), body, 16);
            tabOn.fontStyle = FontStyle.Bold;
            cellFrame = new GUIStyle { border = new RectOffset(2, 2, 2, 2) };
            cellFrame.normal.background = Frame(Pal.Line, 8, 2);
            tabOff = Button(Bevel(Pal.WinFace, Pal.Lightest, Pal.Line), Bevel(Pal.BtnLight, Pal.Lightest, Pal.Line), Bevel(Pal.BtnPress, Pal.Line, Pal.Lightest), body, 16);

            foreach (var e in PartOrder)
            {
                var col = E[e].Color;
                var chip = Text(dot, 16);
                chip.wordWrap = false; chip.alignment = TextAnchor.MiddleCenter;
                chip.padding = new RectOffset(6, 6, 3, 3); chip.margin = new RectOffset(2, 2, 3, 3);
                chip.normal.background = Bevel(col, col, Pal.Line);
                chip.border = new RectOffset(1, 1, 1, 1); // 없으면 4x4 텍스처가 통째로 늘어나 모서리 색이 크게 번진다
                chips[e] = Still(chip);
                tintCards[e] = Boxed(Bevel(col, Pal.Lightest, Pal.Line));
                // 색 단서: 글자 뒤에 감정 색을 깐다
                var tt = Text(dot, 22);
                tt.wordWrap = false; tt.margin = new RectOffset(0, 0, 2, 2);
                tt.normal.background = Tex(col);
                tintText[e] = Still(tt);
                // 파츠 버튼: 평소엔 밝은 바탕에 감정 색 테두리, 고르면 감정 색으로 칠한다
                partBtn[e] = Button(Bevel(Pal.Lightest, Pal.Lightest, col), Bevel(Pal.WinFace, Pal.Lightest, col), Bevel(col, col, Pal.Lightest), dot, 16);
                partSel[e] = Button(Bevel(col, Pal.Lightest, Pal.Line), Bevel(col, Pal.Lightest, Pal.Line), Bevel(col, Pal.Line, Pal.Lightest), dot, 16);
            }
        }
    }
}
