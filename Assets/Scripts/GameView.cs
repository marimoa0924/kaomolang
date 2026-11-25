// 프로토타입 화면 (스펙 v4): 주인공의 태블릿 화면 하나 안에서 창을 여닫는다.
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

        enum Win { Places, Encounter, Home, Sns, Notebook, Traits, Diary, Ending, Rest }

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
            [Win.Places] = 480, [Win.Encounter] = 820, [Win.Home] = 820, [Win.Sns] = 700, [Win.Notebook] = 420, [Win.Traits] = 520, [Win.Diary] = 640, [Win.Ending] = 600, [Win.Rest] = 460,
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
        (string stamp, string face, string sub)? homeResult; // 집 활동 결과: 창 안에 도장, 닫기 버튼만 남는다
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
        float flyT = -9, frenzyUntil;
        float shutT = -99;          // 멘탈붕괴 셧다운 시작 시각
        bool shutSwitched = true;   // 셧다운 중 날을 넘겼는지

        List<Step> steps;
        int step;
        Dictionary<string, Rect> anchors = new(), lastAnchors = new();
        Vector2 origin; // 지금 그리는 창의 화면 위치. Mark가 창 안 좌표를 화면 좌표로 바꿀 때 쓴다

        Font dot, body, round; // DotGothic16: 카오모지·숫자·아이콘 / Gowun Dodum: 한글 본문 / Dongle: 감정 이름·엔딩 제목
        GUIStyle label, small, big, dotStyle, faceStyle, postStyle, symStyle, roundStyle, winStyle, titleStyle, errTitleStyle, toastStyle,
                 btn, offBtn, closeBtn, card, paper, bubble, iconStyle, taskbar, panel, coachStyle, hiliteStyle, bezelStyle, stampStyle,
                 cardBtn, badgeOn, badgeOff, clockOn, clockOff, curtainStyle, selCard, linkBtn, tabOn, tabOff, cellFrame, specialOn, specialOff, passerbyTag;
        readonly Dictionary<string, Texture2D> texCache = new();
        readonly Dictionary<string, GUIStyle> stickerStyles = new();
        readonly Dictionary<Emo, GUIStyle> chips = new(), partBtn = new(), partSel = new(), tintText = new(), tintCards = new();
        Texture2D bgDay, bgSmear, circle, bgAct2, bgAct3;

        // v4 화면 상태
        readonly Dictionary<int, GameState> daySaves = new(); // 날마다 자동 저장: 지난 날로 되돌아가기 (메모리에만)
        bool noticeDue = true;                                 // 막이 걷히면 오늘의 공지 카드를 띄운다
        string wobble;                                         // 피로 위험 구간의 입력 흔들림: 얼굴에 끼어든 외계 문자
        float typeT = -9, snsOpenT = -9;                       // 얼굴이 한 글자씩 찍히는 연출 / 단골의 입력 중 표시
        int snsOpenDay;
        int blankSlot;                                         // 빈칸 긴 글에서 지금 고르는 빈칸
        readonly (Emo eye, Emo mouth)[] blankFaces = new (Emo, Emo)[2];
        int blankFor = -1;
        List<(string text, bool clue)> street;                 // 외출하면 만남 전에 스쳐 가는 거리 말풍선 셋
        float streetT = -99;
        readonly Dictionary<string, Emo> pageGuess = new();
        readonly List<Decor> mixSel = new();

        Step Cur => steps != null && step < steps.Count ? steps[step] : null;
        int mainSteps; // 기본 튜토리얼 단계 수 (뒤에 붙는 특수 이모지 안내는 제외)
        bool InTutorial => steps != null && step < mainSteps;
        bool CoachVisible => Cur != null && popups.Count == 0 && S.Ending == Ending.None && (Cur.When?.Invoke() ?? true) && CurtainDone && ShutDone;

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
                new("stats", $"목표: {Balance.Days}일 동안 들키지 않고\n팔로워 {Balance.FollowerGoal:#,0}명 모으기!\n수치에 마우스를 올리면 설명이 나온다.", null, true),
                new("clock", "하루는 오전 → 오후 → 밤.\n오전과 오후에 한 번씩 움직이고, 밤엔 SNS.", null, true),
                new("icon_outing", "[외출]을 눌러 보자.", () => open.Contains(Win.Places) || open.Contains(Win.Encounter) || !Morning()),
                new("place_0", "장소를 하나 고르자.\n하트는 호감.", () => open.Contains(Win.Encounter) || !Morning()),
                new("situation", "상대의 지금 기분을 보자.", () => !Morning(), true),
                new("eyes", "낮에는 눈만 본다.\n어울리는 눈을 골라 보자.", () => selEye != Emo.Neutral || !Morning()),
                new("react", "반응하자!\n성공이든 실패든 상대의 입을 얻는다.", () => enc != null || !Morning()),
                new("close_result", "결과를 봤으면 × 로 창을 닫자.\n창을 닫아야 시간이 흐른다.", () => !Morning()),
                new("icon_home", "오후다. 한 번 더 움직이자.\n[외출]이나 [집]에서 하나를 하고\n창을 닫으면 밤이 된다.", () => S.IsNight || open.Contains(Win.Home)),
                new("home_cards", "집에서는 쉬면서 피로(~)가 내려간다.\n오른쪽 숫자만큼. 낮잠이 가장 많이 내린다.", () => S.IsNight || homeResult != null, true, () => open.Contains(Win.Home)),
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
                new("send", "결과를 봤으면 창을 닫자.", () => open.Contains(Win.Rest) || open.Contains(Win.Traits) || PastDay1()),
                new("rest", "잠들기 전에 쉬는 방법을 하나 고른다.\n본모습으로 쉬면 많이 풀리지만 의심이 오른다.", () => open.Contains(Win.Traits) || open.Contains(Win.Diary) || PastDay1()),
                new("trait_0", "포인트로 특성을 올리자.", () => S.Points == 0 || PastDay1()),
                new("sleep", "잠들기 전에 그림 일기를 쓴다.", () => open.Contains(Win.Diary) || PastDay1()),
                new("pledge", "오늘 한 일이 스티커로 남는다.\n내일을 도울 다짐 스티커를 하나 고르자.", () => pledgeSel.HasValue || PastDay1()),
            };
            mainSteps = steps.Count;
        }

        void Start() => daySaves[S.Day] = S.Clone();

        void Update()
        {
            S.NoExtras = InTutorial; // 튜토리얼 동안은 특별 이벤트가 끼어들지 않는다
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
                NewDay();
            }
            if (S.JustBrokeDown)
            {
                // 멘탈붕괴: 화면이 폭주하다 셧다운. 그날은 그대로 끝
                S.JustBrokeDown = false;
                shutT = Time.time; shutSwitched = false;
                popups.Clear();
                for (int i = 0; i < 7; i++)
                    toasts.Add(new Toast { T0 = Time.time + i * 0.08f, Id = 200 + toastSeq++ % 50, Body = Glitch(ErrFat[i % ErrFat.Length], 0.5f) });
            }
            if (!shutSwitched && Time.time - shutT >= ShutHalt)
            {
                // 화면이 꺼져 있는 동안 날을 넘긴다
                shutSwitched = true;
                S.ForceEndDay();
                open.Clear(); popups.Clear(); toasts.Clear();
                enc = null; homeResult = null; diary.Clear(); replyFx.Clear();
                uploadR = null; target = -2; snsTab = -1; pledgeSel = null; traitsEndNight = false;
                followersAtDayStart = S.Followers;
                NewDay();
            }
            if (S.JustAwakened)
            {
                // 100 판정 각성: 그날은 끝나지 않는다
                S.JustAwakened = false;
                popups.Enqueue(new Popup { Title = "☆ 각성 ☆", Draw = () =>
                {
                    GUILayout.Label("☆(ﾟ∀ﾟ)☆", new GUIStyle(faceStyle) { fontSize = 48 });
                    GUILayout.Label($"~ 피로 {Balance.AwakenFat}\n오늘 밤: 성공 → 완벽", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
                } });
            }
            if (S.NewAbilities.Count > 0 && !open.Contains(Win.Encounter))
            {
                foreach (var (place, lv) in S.NewAbilities)
                    popups.Enqueue(new Popup { Title = "★ 능력 ★", Draw = () =>
                    {
                        GUILayout.Label($"{PlaceById(place).Icon}  {Hearts(lv)}", new GUIStyle(dotStyle) { fontSize = 34, alignment = TextAnchor.MiddleCenter, richText = true });
                        GUILayout.Label(Abilities[(place, lv)], new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
                    } });
                S.NewAbilities.Clear();
            }
            if (noticeDue && CurtainDone && ShutDone && S.Ending == Ending.None && popups.Count == 0)
            {
                noticeDue = false;
                ShowMorning();
            }
            if (S.Ending != Ending.None && !open.Contains(Win.Ending) && !open.Contains(Win.Diary) && CurtainDone && ShutDone)
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
            var shake = Time.time - shutT < ShutRiot ? new Vector3(UnityEngine.Random.Range(-14f, 14f), UnityEngine.Random.Range(-10f, 10f), 0) * k : Vector3.zero;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - VW * k) / 2, (Screen.height - VH * k) / 2, 0) + shake, Quaternion.identity, new Vector3(k, k, 1));

            GUI.DrawTexture(new Rect(0, 0, VW, VH), S.Fat >= 70 || S.BrokeDown ? bgSmear : S.Act == 3 ? bgAct3 : S.Act == 2 ? bgAct2 : bgDay); // 한 번 붕괴하면 배경은 번진 채로
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
            DrawShutdown();

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
            if (w == Win.Sns && S.NightDone && S.Ending == Ending.None && !open.Contains(Win.Diary) && !open.Contains(Win.Traits)) Open(Win.Rest);
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
            if (!InTutorial) S.RerollFirstDay(); // 튜토리얼이 아니면 1일차 글도 무작위
            daySaves.Clear();
            daySaves[1] = S.Clone();
            noticeDue = true;
            open.Clear(); popups.Clear(); toasts.Clear(); floaters.Clear(); diary.Clear(); pledgeSel = null;
            traitsEndNight = false;
            followersAtDayStart = 0;
            enc = null; homeResult = null; replyFx.Clear(); uploadR = null; target = -2; snsTab = -1; knownMood.Clear();
            selEye = selMouth = Emo.Neutral;
            selDecor = null;
            wobble = null; street = null;
        }

        // 날이 바뀌면: 자동 저장, 아침 공지는 막이 걷힌 뒤
        void NewDay()
        {
            if (S.Ending == Ending.None) daySaves[S.Day] = S.Clone();
            noticeDue = true;
            wobble = null; street = null;
        }

        // 지난 날 아침으로 되돌아간다 (Papers, Please의 날짜 저장). shortcut: 메모리에만, 정식판에선 파일 저장
        void Rewind(int day)
        {
            S = daySaves[day].Clone();
            foreach (var d in daySaves.Keys.Where(d => d > day).ToList()) daySaves.Remove(d);
            open.Clear(); popups.Clear(); toasts.Clear(); floaters.Clear(); diary.Clear(); pledgeSel = null;
            traitsEndNight = false;
            followersAtDayStart = S.Followers;
            enc = null; homeResult = null; replyFx.Clear(); uploadR = null; target = -2; snsTab = -1;
            selEye = selMouth = Emo.Neutral; selDecor = null; wobble = null; street = null;
            noticeDue = true;
        }

        void ShowMorning()
        {
            var extra = new List<string>();
            if (S.Day == Balance.SecondSetDay) extra.Add("거리에 새 기호가 섞이기 시작했다. 수첩에 칸이 늘어난다.");
            if (S.Day == Balance.DecorComboDay) extra.Add("[집]에서 장식 둘을 섞어 새 장식을 만들 수 있다.");
            var n = S.Today;
            if (n != null && !InTutorial)
                popups.Enqueue(new Popup { Title = "! 오늘의 유행 공지", Draw = () =>
                {
                    GUILayout.Label(n.Title, new GUIStyle(roundStyle) { fontSize = 44, alignment = TextAnchor.MiddleCenter });
                    // 팔로워 30%부터 공지 문장에도 카오모지가 섞이고, 80%면 서명이 내 얼굴이 된다
                    GUILayout.Label(n.Rule + (FanKaomoji > 0 ? "  " + FanFaces[S.Day % FanFaces.Length] : ""), new GUIStyle(label) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
                    GUILayout.Label($"— {(Conform > 0.8f ? Mimic : "행성 유행 위원회")}", new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleRight });
                    foreach (var e in extra) GUILayout.Label(e, small);
                } });
            else if (extra.Count > 0)
                popups.Enqueue(new Popup { Title = "!", Draw = () => { foreach (var e in extra) GUILayout.Label(e, label); } });
            if (S.CallHintSymbol is { } sym && S.Pencil.TryGetValue(sym, out var g))
                popups.Enqueue(new Popup { Title = "((☆)) 고향 통신", Draw = () =>
                {
                    GUILayout.Label($"{sym}  ≒  {E[g].Shape}", new GUIStyle(faceStyle) { fontSize = 40 });
                    GUILayout.Label("수첩에 연필로 적어 두었다. 확정은 그림 페이지에서.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
                } });
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

        // 예전처럼 대상의 아래 → 위 → 오른쪽 → 왼쪽 순서로 놓되, 가리면 다음 후보로.
        // 1순위: 대상이 든 창을 가리지 않는 자리, 2순위: 대상만이라도 가리지 않는 자리
        void PlaceCoach()
        {
            float h = Mathf.Max(coachH, 70), w = CoachW;
            if (!lastAnchors.TryGetValue(Cur.Target, out var a)) { coachRect.x = (VW - w) / 2; coachRect.y = 70; return; }
            Rect Fit(float x, float y) => new Rect(Mathf.Clamp(x, 4, VW - w - 4), Mathf.Clamp(y, 4, VH - BarH - h - 4), w, h);
            var cands = new[] { Fit(a.x, a.yMax + 12), Fit(a.x, a.y - h - 12), Fit(a.xMax + 12, a.y), Fit(a.x - w - 12, a.y) };
            Rect? win = open.Where(o => rects.ContainsKey(o) && rects[o].Contains(a.center)).Select(o => (Rect?)rects[o]).LastOrDefault();
            var pick = cands.FirstOrDefault(r => win == null ? !r.Overlaps(a) : !r.Overlaps(win.Value));
            if (pick.width == 0) pick = cands.FirstOrDefault(r => !r.Overlaps(a));
            if (pick.width == 0) pick = cands[0];
            coachRect.x = pick.x; coachRect.y = pick.y;
        }

        void DrawCoach()
        {
            blocked = false;
            GUILayout.Label(Cur.Text, new GUIStyle(label) { fontSize = 17 });
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{step + 1}/{steps.Count}", dotStyle, GUILayout.ExpandWidth(false));
            if (GUILayout.Button("건너뛰기", small, GUILayout.ExpandWidth(false))) pending = () => { step = steps.Count; S.RerollFirstDay(); };
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
            float fa = Time.time - flyT;
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
            if (InTutorial || Event.current.type != EventType.Repaint || !r.Contains(Event.current.mousePosition)) return; // 튜토리얼 동안은 마우스 설명 없음
            var mouse = Event.current.mousePosition + o;
            // 마우스가 지금 그리는 창이 아닌 다른 창 위에 있으면 (가려진 요소) 설명을 띄우지 않는다
            if (open.Any(w => w != curWin && rects.TryGetValue(w, out var wr) && wr.Contains(mouse))) return;
            tipNext = (text, new Rect(r.x + o.x, r.y + o.y, r.width, r.height));
        }

        void DrawTip()
        {
            var t = tip ?? (Time.time - pinT < 2.2f ? (pinTip, pinRect) : ((string, Rect)?)null);
            if (t == null || InTutorial) return;
            var (text, a) = t.Value;
            var size = label.CalcSize(new GUIContent(text));
            var r = new Rect(Mathf.Clamp(a.xMax + 8, 4, VW - size.x - 30), Mathf.Clamp(a.y, 4, VH - BarH - size.y - 24), size.x + 24, size.y + 20);
            if (r.x < a.xMax && r.xMax > a.x) r.y = a.yMax + 6; // 대상을 가리면 아래로
            GUI.Window(97, r, _ => GUI.Label(new Rect(10, 8, r.width - 16, r.height - 12), text, label), GUIContent.none, coachStyle);
            GUI.BringWindowToFront(97);
        }

        const float CurtainIn = 0.4f, CurtainEndText = 1.2f, CurtainHold = CurtainEndText + 1.2f; // 로딩 막대는 멘탈붕괴 재부팅에만
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
                float W = VW + Bezel * 2, H = VH + Bezel * 2;
                var big = new GUIStyle(roundStyle) { fontSize = 72, alignment = TextAnchor.MiddleCenter };
                if (t < CurtainIn + CurtainEndText) { GUI.Label(new Rect(0, H / 2 - 60, W, 90), $"{curtainDay}일차 종료", big); return; }
                if (S.Ending != Ending.None) { GUI.Label(new Rect(0, H / 2 - 60, W, 90), "…", big); return; }
                GUI.Label(new Rect(0, H / 2 - 60, W, 90), $"{S.Day}일차", big);
                GUI.Label(new Rect(0, H / 2 + 30, W, 40), $"♥ {S.Followers:#,0} / {Balance.FollowerGoal:#,0}", new GUIStyle(dotStyle) { fontSize = 32, alignment = TextAnchor.MiddleCenter });
                // 막이 바뀌는 밤: 막 제목 카드 (Inscryption의 장 넘김)
                if (Balance.Act(S.Day) != Balance.Act(S.Day - 1))
                    GUI.Label(new Rect(0, H / 2 - 150, W, 80), ActNames[S.Act], new GUIStyle(roundStyle) { fontSize = 56, alignment = TextAnchor.MiddleCenter });
            }, GUIContent.none, curtainStyle);
            GUI.BringWindowToFront(96);
        }

        // 멘탈붕괴 셧다운: 폭주(흔들림·잡음·에러 폭탄) → 브라운관처럼 꺼짐 → 정지 화면 → 재부팅 → 다음 날
        // 멘탈붕괴 → 경고문(SYSTEM HALT) → 재부팅(느린 로딩) → 평소처럼 N일차 → 막이 걷힌다
        const float ShutRiot = 1.0f, ShutOff = 1.7f, ShutHalt = 2.8f, ShutBoot = 4.6f, LoadTime = 4.0f,
            ShutDay = ShutBoot + LoadTime, ShutLift = ShutDay + 1.4f, ShutEnd = ShutLift + 0.4f;
        bool ShutDone => Time.time - shutT > ShutEnd;

        void DrawShutdown()
        {
            float t = Time.time - shutT;
            if (ShutDone) return;
            float lift = t > ShutLift ? 1 - (t - ShutLift) / (ShutEnd - ShutLift) : 1; // 마지막엔 막처럼 위로 걷힌다
            GUI.Window(95, new Rect(-Bezel, -Bezel, VW + Bezel * 2, (VH + Bezel * 2) * lift), _ =>
            {
                float W = VW + Bezel * 2, H = VH + Bezel * 2;
                var cover = Tex(Pal.Bezel);
                if (t < ShutRiot)
                {
                    // 폭주: 화면을 가로지르는 잡음 줄과 깨진 얼굴이 쏟아진다
                    for (int i = 0; i < 14; i++)
                        GUI.DrawTexture(new Rect(0, UnityEngine.Random.Range(0f, H), W, UnityEngine.Random.Range(2f, 18f)), Tex(i % 3 == 0 ? Pal.Hot : i % 3 == 1 ? Pal.Glitch2 : Pal.Glitch1));
                    for (int i = 0; i < 6; i++)
                        GUI.Label(new Rect(UnityEngine.Random.Range(0f, W - 200), UnityEngine.Random.Range(0f, H - 60), 260, 60), Glitch("(ﾟДﾟ)(ﾟДﾟ)", 0.4f), new GUIStyle(faceStyle) { fontSize = 34 });
                    return;
                }
                if (t < ShutOff)
                {
                    // 브라운관이 꺼지듯: 위아래가 닫혀 한 줄이 되고, 그 줄이 점으로 줄어든다
                    float p = (t - ShutRiot) / (ShutOff - ShutRiot);
                    float gap = Mathf.Max(3, H * (1 - Mathf.Clamp01(p * 2)));
                    GUI.DrawTexture(new Rect(0, 0, W, (H - gap) / 2), cover);
                    GUI.DrawTexture(new Rect(0, (H + gap) / 2, W, (H - gap) / 2), cover);
                    float lw = p < 0.5f ? W : W * (1 - (p - 0.5f) * 2) + 6;
                    if (p >= 0.5f) GUI.DrawTexture(new Rect(0, (H - gap) / 2, W, gap), cover);
                    GUI.DrawTexture(new Rect((W - lw) / 2, H / 2 - 2, lw, 4), Tex(Pal.Lightest));
                    return;
                }
                GUI.DrawTexture(new Rect(0, 0, W, H), cover);
                var big = new GUIStyle(dotStyle) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
                if (t < ShutBoot)
                {
                    // 정지: 깨진 얼굴 하나와 깜빡이는 정지 문구
                    if ((int)(t * 3) % 2 == 0) GUI.Label(new Rect(0, H / 2 - 90, W, 70), Glitch("(・_・)", Mathf.Clamp01((t - ShutOff) * 0.5f)), new GUIStyle(faceStyle) { fontSize = 56 });
                    GUI.Label(new Rect(0, H / 2 + 10, W, 50), Glitch("SYSTEM HALT", 0.25f), big);
                    GUI.Label(new Rect(0, H / 2 + 60, W, 30), Glitch("MIMIC.EXE NOT RESPONDING", 0.3f), new GUIStyle(dotStyle) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
                    return;
                }
                if (t < ShutDay)
                {
                    // 재부팅: 막대가 천천히, 가끔 멈칫하며 차오른다
                    float b = Mathf.Clamp01((t - ShutBoot) / LoadTime);
                    b = b < 0.4f ? b : b < 0.6f ? 0.4f + (b - 0.4f) * 0.25f : 0.45f + (b - 0.6f) * 1.375f;
                    int bars = Mathf.RoundToInt(b * 16);
                    GUI.Label(new Rect(0, H / 2 - 40, W, 50), "REBOOT", big);
                    GUI.Label(new Rect(0, H / 2 + 10, W, 40), new string('▒', bars) + new string('·', 16 - bars), big);
                    return;
                }
                // 평소 날 넘김처럼 N일차
                GUI.Label(new Rect(0, H / 2 - 60, W, 90), S.Ending != Ending.None ? "…" : $"{S.Day}일차", new GUIStyle(roundStyle) { fontSize = 72, alignment = TextAnchor.MiddleCenter });
                if (S.Ending == Ending.None)
                    GUI.Label(new Rect(0, H / 2 + 30, W, 40), $"♥ {S.Followers:#,0} / {Balance.FollowerGoal:#,0}", new GUIStyle(dotStyle) { fontSize = 32, alignment = TextAnchor.MiddleCenter });
            }, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(95);
        }

        // 한 번 붕괴한 뒤로는 화면 곳곳이 가끔 어긋난다
        string Weird(string text)
        {
            if (!S.BrokeDown) return text;
            int tick = (int)(Time.time * 2.5f) + text.Length;
            return tick % 7 == 0 ? Glitch(text, 0.45f) : text;
        }

        // 피로가 오를수록 배경이 번지고(스미어 그라데이션) 화면에 잡음 줄이 낀다
        void DrawGlitchBars()
        {
            if (S.BrokeDown && Event.current.type == EventType.Repaint && UnityEngine.Random.value < 0.04f) // 붕괴 뒤: 이따금 한 줄씩
                GUI.DrawTexture(new Rect(0, UnityEngine.Random.Range(0, VH), VW, UnityEngine.Random.Range(1, 4)), Tex(Pal.Glitch1));
            float f = S.Fat / 100f;
            if (f <= 0.6f || Event.current.type != EventType.Repaint) return;
            for (int i = 0; i < (int)((f - 0.5f) * 12); i++)
                if (UnityEngine.Random.value < 0.5f)
                    GUI.DrawTexture(new Rect(0, UnityEngine.Random.Range(0, VH), VW, UnityEngine.Random.Range(2, 7)), Tex(i % 2 == 0 ? Pal.Glitch1 : Pal.Glitch2));
        }

        string Monologue()
        {
            string line = S.AwakenDay == S.Day ? "☆(ﾟ∀ﾟ)☆" : S.BrokeDown ? "(ﾟ_ﾟ)(ﾟ_ﾟ)(ﾟ_ﾟ)(ﾟ_ﾟ)" : S.Fat < 40 ? "" : S.Fat < 70 ? "(-_-;)" : S.Fat < 100 ? "(・_・;)…(・_・;)" : "(ﾟДﾟ)(ﾟДﾟ)(ﾟДﾟ)(ﾟДﾟ)"; // 주인공은 한글을 쓰지 않는다
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
            if (S.BrokeDown) heat = Mathf.Max(heat, 0.65f); // 붕괴 뒤엔 오류가 더 자주
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
                ("icon_sns", "((•))", "SNS", () => { S.MarkSeen(); commentScroll = Vector2.zero; if (snsOpenDay != S.Day) { snsOpenDay = S.Day; snsOpenT = Time.time; } Open(Win.Sns); Open(Win.Notebook); focusReq = (int)Win.Sns + 1; }, S.IsNight, "SNS는 밤에 열린다.\n오전·오후에 먼저 움직이자."),
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
            if (GUI.Button(nr, "<size=34>¶</size>\n<size=14>해독 수첩</size>", iconStyle) && !blocked) pending = () => { if (open.Contains(Win.Notebook)) Close(Win.Notebook); else Open(Win.Notebook); }; // 한 번 더 누르면 닫힌다
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
            if (S.Today != null && !InTutorial) { GUILayout.Label($"! {S.Today.Title}", label); Explain(S.Today.Rule); }
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
            $"♥ 팔로워: {Balance.Days}일째에 {Balance.FollowerGoal:#,0}명이 넘으면 성공.",
            $"!? 의심: 100이면 정체가 들킨다. {Balance.ConquerSusMax} 이상이면 진엔딩도 막힌다.",
            $"~ 피로: {Balance.LeakFrom}부터 새어 나오고(지친 사진), {Balance.DangerFrom}부터 위험(게시물 의심, 입력 흔들림). 100에서 각성 아니면 셧다운.",
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
                GUI.Label(lr, Weird(names[i]), i == (int)S.Slot ? clockOn : clockOff);
            }
            // 바늘: 회전 행렬 대신 점을 이어 그린다 (영역 안 회전은 위치가 어긋난다)
            float ha = (angles[(int)S.Slot] - 90) * Mathf.Deg2Rad;
            for (int d = 0; d <= 28; d += 2)
                GUI.DrawTexture(new Rect(c.x + Mathf.Cos(ha) * d - 2, c.y + Mathf.Sin(ha) * d - 2, 4, 4), Tex(Pal.Hot));
            GUI.Label(new Rect(c.x - 34, c.y - 4, 68, 24), $"{S.Day}일차", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
            var ring = Sym.Keys.ToArray();
            for (int i = 0; i < (S.Act - 1) * 4; i++)
            {
                float a = (i * 45 + 22 - 90) * Mathf.Deg2Rad;
                GUI.Label(new Rect(c.x + Mathf.Cos(a) * 70 - 10, c.y + Mathf.Sin(a) * 70 - 11, 20, 22), ring[(i * 7 + S.Day) % ring.Length], new GUIStyle(dotStyle) { fontSize = 14, alignment = TextAnchor.MiddleCenter });
            }
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
            if (daySaves.Count > 1 && GUI.Button(new Rect(VW - 200, VH - BarH + 4, 44, BarH - 8), "◁", btn) && !blocked)
                pending = () => popups.Enqueue(new Popup { Title = "◁ 되감기", Ok = "×", Draw = () =>
                {
                    GUILayout.Label("그날 아침으로 돌아간다.", label);
                    GUILayout.BeginHorizontal();
                    foreach (var d in daySaves.Keys.OrderBy(d => d))
                        Btn($"{d}", () => { popups.Clear(); Rewind(d); }, true, null, GUILayout.Width(44), GUILayout.Height(36));
                    GUILayout.EndHorizontal();
                } });
            var slot = S.Slot switch { Slot.Morning => "○", Slot.Afternoon => "◑", _ => "●" };
            GUI.Label(new Rect(VW - 150, VH - BarH + 4, 140, BarH - 8), $"{S.Day}일차  {slot}",
                new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleRight, fontSize = 18 });
        }

        // 창 제목도 글자 대신 아이콘
        string Title(Win w) => w switch
        {
            Win.Places => "→", Win.Encounter => S.PendingPlace != null ? ((enc?.Ev ?? S.Pending)?.Kind == EvKind.Special ? "★ " : "") + PlaceById(S.PendingPlace).Icon : "→",
            Win.Home => "△", Win.Sns => "((•))", Win.Notebook => "¶", Win.Traits => "↑", Win.Diary => "zZ", Win.Rest => "~", _ => "★",
        };

        // ---------- 창 공통 ----------

        void TitleBar(string title, float width, Action onClose)
        {
            GUI.Label(new Rect(4, 4, width - 8, 26), Weird(title), titleStyle);
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
            TitleBar(Title(w), width, w is Win.Ending or Win.Diary or Win.Rest ? null : () => Close(w));
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
                case Win.Rest: DrawRest(); break;
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
            float fat = S.OutingFat;
            var list = GameData.Places;
            for (int i = 0; i < list.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < list.Length; j++)
                {
                    var p = list[j];
                    Btn($"<size=40>{p.Icon}</size>\n<size=20>{Hearts(S.Aff.GetValueOrDefault(p.Id))}</size>",
                        () => { S.PrepareOuting(p.Id); enc = null; selEye = selMouth = Emo.Neutral; Close(Win.Places); Open(Win.Encounter); street = InTutorial ? null : Street(); streetT = Time.time; },
                        true, iconStyle, GUILayout.Height(120));
                    Mark($"place_{j}");
                    Explain($"{p.Name}\n{(S.HasAbility(p.Id, 2) ? "●" : "○")} 2단계: {Abilities[(p.Id, 2)]}\n{(S.HasAbility(p.Id, 3) ? "●" : "○")} 3단계: {Abilities[(p.Id, 3)]}");
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
            StreetBubble();
            bool special = ev.Kind == EvKind.Special;
            // 특수 이벤트: 깜빡이는 띠, 반짝이는 테두리, 얼굴 주위를 도는 별, 보상(특수 이모지) 미리보기
            if (special)
                GUILayout.Label("★ SPECIAL EVENT ★", (int)(Time.time * 3) % 2 == 0 ? specialOn : specialOff, GUILayout.Height(34));
            GUILayout.BeginVertical(special ? tintCards[Emo.Joy] : ev.Kind == EvKind.Passerby ? card : paper);
            GUILayout.BeginHorizontal();
            GUILayout.Label(ev.Portrait, new GUIStyle(faceStyle) { fontSize = 34, alignment = TextAnchor.MiddleLeft });
            Mark("npc");
            if (special && Event.current.type == EventType.Repaint)
            {
                var pr = GUILayoutUtility.GetLastRect();
                for (int i = 0; i < 6; i++)
                {
                    float ang = i * Mathf.PI / 3 + Time.time * 2;
                    GUI.Label(new Rect(pr.center.x + Mathf.Cos(ang) * 70 - 10, pr.center.y + Mathf.Sin(ang) * 26 - 10, 20, 20), "★", new GUIStyle(dotStyle) { fontSize = 14 + i % 2 * 6 });
                }
            }
            GUILayout.FlexibleSpace();
            if (special && enc == null && ev.Decor != null)
                GUILayout.Label($"+ {ev.Decor.L}(?){ev.Decor.R}", new GUIStyle(faceStyle) { fontSize = 22 });
            else if (S.PendingPlace != null)
                GUILayout.Label(Hearts(S.Aff.GetValueOrDefault(S.PendingPlace)), new GUIStyle(dotStyle) { fontSize = 22 });
            GUILayout.EndHorizontal();
            GUILayout.Label(ev.Situation, label);
            GUILayout.Space(6);
            GUILayout.Label(enc == null ? $"“{ev.Say}”" : enc.Ok ? ev.WinText : ev.FailText, bubble);
            GUILayout.EndVertical();
            if (special && Event.current.type == EventType.Repaint && (int)(Time.time * 4) % 2 == 0)
            {
                var cr = GUILayoutUtility.GetLastRect();
                GUI.Box(new Rect(cr.x - 4, cr.y - 4, cr.width + 8, cr.height + 8), GUIContent.none, hiliteStyle);
            }
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

        // 거리 말풍선 (Headliner: NoviNews의 산책): 어제 내 얼굴, 오역된 얼굴, 못 푼 기호가 든 문장. 해독 단서는 누르면 스크랩
        const float StreetEach = 2f;
        List<(string, bool)> Street()
        {
            var lines = new List<(string, bool)>();
            int seed = S.Day * 13 + S.Visits.Values.Sum();
            bool mimicDay = S.Today?.Kind == NoticeKind.MimicDay;
            string mine = S.YesterdayPostFace ?? lastFace;
            lines.Add((mimicDay ? string.Concat(Enumerable.Repeat(Mimic, 3)) : mine != null ? Censor($"요즘 다들 이 얼굴 해 {mine}", seed) : Censor("오늘 하늘 맑다", seed), false));
            var mis = S.MisusedFaces.FirstOrDefault();
            int clues = S.HasAbility("station", 2) || S.Today?.Kind == NoticeKind.StayIn ? 2 : 1;
            if (clues < 2) lines.Add((mimicDay ? string.Concat(Enumerable.Repeat(Mimic, 2)) : mis != null ? Censor($"장례식에서도 {mis} 이 얼굴 쓰더라", seed + 1) : Censor("다들 얼굴로만 말하네", seed + 1), false));
            var hard = Pool.Where(q => q.Text.Split(' ').Any(t => Sym.ContainsKey(t) && !S.Solved(t))).ToList();
            for (int k = 0; k < clues && hard.Count > 0; k++) lines.Add((hard[(seed + k * 17) % hard.Count].Text, true));
            return lines;
        }

        void StreetBubble()
        {
            if (street == null || enc != null) return;
            int i = (int)((Time.time - streetT) / StreetEach);
            if (i >= street.Count) return;
            var (text, clue) = street[i];
            GUILayout.BeginHorizontal();
            GUILayout.Label("(・_・)", new GUIStyle(dotStyle) { fontSize = 18 }, GUILayout.Width(60), GUILayout.Height(40));
            if (clue)
            {
                bool kept = S.Scraps.Contains(text);
                if (GUILayout.Button(kept ? $"{text}  ¶" : text, bubble) && !blocked && !kept) { S.Scrap(text); Fly("¶", "icon_notebook"); }
                Explain("못 읽는 기호가 든 말. 누르면 수첩에 스크랩한다.");
            }
            else GUILayout.Label(text, bubble);
            if (GUILayout.Button("▶▶", closeBtn, GUILayout.Width(40), GUILayout.Height(30)) && !blocked) streetT -= StreetEach;
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
                if (hr.sub != null) GUILayout.Label(hr.sub, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 });
                GUILayout.Space(10);
                Btn("<size=20>×</size>", () => Close(Win.Home), true, null, GUILayout.Height(44));
                Mark("close_result");
            }
            else
            {
                GUILayout.BeginVertical();
                HomeCard("○", "거울 연습", Balance.FatHome, Mirror, "눈과 입을 다른 감정으로 섞어 본다.\n말이 되는 조합(시너지)이면 성공, 수첩에 남는다.");
                Mark("home_mirror");
                HomeCard("((•))", "피드 구경", Balance.FatHome, Peek, "오늘 밤 팬 글 하나를\n미리 보고 색 단서를 받는다.");
                HomeCard("zZ", "낮잠", Balance.FatNap, Nap, $"크게 쉰다. 의심도 {Balance.NapSus}.", $"!? {Balance.NapSus:+0;-0}");
                if (S.CanCombine) HomeCard("∞", "장식 섞기", Balance.FatHome, MixPopup, "장식 둘을 섞어 새 장식 하나를 만든다.\n왼쪽은 첫째의 왼쪽, 오른쪽은 둘째의 오른쪽.");
                GUILayout.EndVertical();
                Mark("home_cards");
            }
            GUILayout.EndVertical();

            if (homeResult == null) { GUILayout.Space(12); Builder(true, true); }
            GUILayout.EndHorizontal();
        }

        float homeT;
        void HomeCard(string icon, string name, float fat, Action act, string help, string extra = null)
        {
            Btn($"<size=26>{icon}</size>   <size=16>{name}</size>", act, true, cardBtn, GUILayout.Height(74));
            var r = GUILayoutUtility.GetLastRect();
            HoverFat(fat);
            Explain(help);
            // 쉬면서 피로가 내려간다는 걸 늘 보이게: 카드 오른쪽에 효과 배지
            var badge = $"~ {fat:+0;-0}" + (extra != null ? $"  {extra}" : "");
            var bs = new GUIStyle(clockOn) { font = dot, fontSize = 16, padding = new RectOffset(8, 8, 2, 2) };
            var w = bs.CalcSize(new GUIContent(badge)).x;
            GUI.Label(new Rect(r.xMax - w - 10, r.center.y - 13, w, 26), badge, bs);
        }

        // 집 활동 결과에 쉬어서 내려간 수치를 한 줄로
        string RestLine((int f, float s, float fat) b)
        {
            var parts = new List<string> { $"~ 피로 {Mathf.RoundToInt(S.Fat - b.fat):+0;-0}" };
            if (Mathf.RoundToInt(S.Sus - b.s) != 0) parts.Add($"!? 의심 {Mathf.RoundToInt(S.Sus - b.s):+0;-0}");
            return string.Join("   ", parts);
        }

        void HomeDone(string stamp, string face, string sub = null) { homeResult = (stamp, face, sub); homeT = Time.time; }

        void Mirror()
        {
            var before = Snap();
            var face = Face(selEye, selMouth);
            var (syn, isNew) = S.DoMirror(selEye, selMouth);
            Floats(before);
            if (syn == null)
                HomeDone("실패", face, (selEye == selMouth
                    ? "눈과 입이 같은 감정이다.\n눈과 입을 다르게 섞어 보자."
                    : $"{E[selEye].Name} 눈과 {E[selMouth].Name} 입은 어울리지 않는다.") + "\n" + RestLine(before));
            else
                HomeDone("성공", face, (isNew ? $"새 시너지 '{syn.Name}'!\n수첩에 기록했다." : $"'{syn.Name}' 얼굴. 이미 아는 시너지다.") + "\n" + RestLine(before));
            if (isNew) diary.Add(new Sticker($"+{face}", E[selEye].Color));
        }

        void Peek()
        {
            var before = Snap();
            var picks = S.DoPeek();
            Floats(before);
            HomeDone("구경", null, RestLine(before));
            diary.Add(new Sticker("((•))", Pal.Bubble));
            foreach (var i in picks)
            {
                var p = S.Tonight[i];
                popups.Enqueue(new Popup { Title = "((•))", Draw = () => PostBody(p, true) });
            }
        }

        // 장식 조합 (8일차부터, Strange Horticulture의 조제). 둘을 골라야 시간이 쓰인다
        void MixPopup()
        {
            mixSel.Clear();
            popups.Enqueue(new Popup { Title = "∞ 장식 섞기", Ok = "∞", Draw = () =>
            {
                GUILayout.BeginHorizontal();
                foreach (var d in S.Decors)
                    if (GUILayout.Button($"{d.L} {d.R}", mixSel.Contains(d) ? partSel[Emo.Shy] : partBtn[Emo.Shy], GUILayout.Width(70), GUILayout.Height(36)))
                    { if (mixSel.Contains(d)) mixSel.Remove(d); else if (mixSel.Count < 2) mixSel.Add(d); }
                GUILayout.EndHorizontal();
                var face = mixSel.Count == 2 ? Face(Emo.Joy, Emo.Joy, new Decor("", "", mixSel[0].L, mixSel[1].R)) : "( ? )";
                GUILayout.Label(face, new GUIStyle(faceStyle) { fontSize = 34 });
            }, OnOk = () =>
            {
                if (mixSel.Count < 2) return;
                var before = Snap();
                var mix = S.CombineDecors(mixSel[0], mixSel[1]);
                Floats(before);
                if (mix == null) return;
                HomeDone("섞기", Face(Emo.Joy, Emo.Joy, mix), RestLine(before));
                diary.Add(new Sticker(Face(Emo.Joy, Emo.Joy, mix), Pal.Special));
            } });
        }

        void Nap()
        {
            var before = Snap();
            S.DoNap();
            Floats(before);
            HomeDone("꿀잠", null, RestLine(before));
            diary.Add(new Sticker("zZ", Pal.Bubble));
        }

        // ---------- SNS: 두 탭. [내 게시물] 인스타그램식 사진 올리기 / [멘션] 트위터식 팬 글에 멘션 달기 ----------

        const string MyHandleBase = "@kao_kao";
        string MyHandle => Weird(MyHandleBase);
        int snsTab = -1;          // 0 내 게시물, 1 멘션, -1은 아직 안 고름(열 때 할 일이 남은 쪽으로)
        int target = -2;          // 멘션 탭에서 답할 글
        string uploadFace;
        int uploadGain, likeTarget;
        readonly List<(string glyph, string color, string text)> postComments = new();
        Vector2 postScroll;
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

        static readonly string[] SnsHelp =
        {
            "▣ 내 게시물: 사진 속 장면의 분위기에 어울리는 감정의 얼굴을 붙여 게시한다. 반응이 좋았던 장소는 다음부터 사진에 분위기 모양이 보인다.",
            "@ 멘션: 팬 글의 감정을 추측해 얼굴로 답한다. 어려우면 ‖ 나중에 (다음 밤에 서운해하며 돌아온다).",
            "기호: 맞히면 스티커가 붙고, 다 모이면 해독돼 그 감각의 색으로 보인다. 기호에 마우스를 올리면 수첩의 같은 칸이 두근거린다.",
            "★? 같은 색 칩: 글에 그 모양 감각의 색이 비친다는 뜻. 어떤 감각인지는 수첩에서 같은 모양을 찾자. 글 일부에만 비치면 다른 감각이 섞였을 수 있다.",
            "색이 다른 기호가 섞인 글은 눈과 입을 다르게 한 시너지 얼굴이 맞을 수 있다.",
            "끝까지 글자로 쓰는 사람(다들 얼굴만 쓰는 걸 이상하게 여기는 사람)에게 답하면 의심이 크게 더 오른다.",
        };

        void DrawSns()
        {
            AutoTarget();
            int left = S.Replies.Count(r => r == null);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"▣ 내 게시물  {(S.Uploaded ? "●" : "○")}", snsTab == 0 ? tabOn : tabOff, GUILayout.Height(34)) && !blocked) snsTab = 0;
            Mark("tab_post");
            if (GUILayout.Button($"@ 멘션  {S.Replies.Length - left}/{S.Replies.Length}", snsTab == 1 ? tabOn : tabOff, GUILayout.Height(34)) && !blocked) { snsTab = 1; commentScroll = Vector2.zero; } // 멘션은 맨 위 글부터
            Mark("tab_mention");
            if (GUILayout.Button("?", closeBtn, GUILayout.Width(34), GUILayout.Height(34)) && !blocked)
                pending = () => popups.Enqueue(new Popup { Title = "?", Draw = () => { foreach (var line in SnsHelp) GUILayout.Label(line, label); } });
            Mark("sns_help");
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
            GUILayout.BeginVertical(card, GUILayout.Height(S.Uploaded ? 470 : 330));
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
            var caption = place?.Photo ?? "어질러진 내 방. 별일 없는 하루.";
            if (S.Today?.Kind == NoticeKind.NoPhoto) caption = new string(caption.Select(ch => ch == ' ' ? ' ' : '■').ToArray());
            GUI.Label(new Rect(photo.x + 16, photo.yMax - 54, photo.width - 32, 30), caption, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 });
            if (S.Leaking) GUI.Label(new Rect(photo.x + 8, photo.y + 6, 90, 30), S.InDanger ? "~(~(~" : "~(", new GUIStyle(dotStyle) { fontSize = 20 }); // 사진 구석의 촉수 잔상
            if (S.Leaking && place != null)
            {
                float m = S.TiredMultiplier(place.Mood);
                if (Math.Abs(m - 1) > 0.01f) GUI.Label(new Rect(photo.x + 8, photo.y + 36, 160, 24), $"지친 사진 ×{m:0.0#}", small);
            }
            if (place != null && knownMood.Contains(place.Id))
                GUI.Label(new Rect(photo.xMax - 60, photo.y + 10, 48, 30), E[place.Mood].Shape, chips[place.Mood]);
            var face = S.Uploaded ? uploadFace : Typed(Wobbled(Face(selEye, selMouth, selDecor)));
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
            // 좋아요는 사진 왼쪽 아래에서 차오른다
            GUILayout.Space(6);
            int likes = S.Uploaded ? Mathf.RoundToInt(likeTarget * Mathf.Clamp01((Time.time - uploadT) / 2.5f)) : 0;
            GUILayout.Label($"♥ {likes:#,0}", new GUIStyle(dotStyle) { fontSize = 22 }, GUILayout.Height(30));
            GUILayout.Space(4);
            // 올린 뒤에는 팬들의 댓글이 하나씩 달린다 (깨진 글)
            if (S.Uploaded)
            {
                GUILayout.Label($"댓글 {postComments.Count}", small);
                postScroll = GUILayout.BeginScrollView(postScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.Height(200));
                for (int k = 0; k < postComments.Count && Time.time - uploadT > 0.8f + k * 0.35f; k++)
                {
                    var (g, c, text) = postComments[k];
                    GUILayout.BeginHorizontal();
                    Avatar(g, c, 24);
                    GUILayout.Space(6);
                    GUILayout.Label(Censor(text, k * 13 + S.Day), new GUIStyle(dotStyle) { fontSize = 15, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(24));
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
                GUILayout.EndScrollView();
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
            commentScroll.x = 0; // 가로 스크롤 없음
            commentScroll = GUILayout.BeginScrollView(commentScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.Height(318));
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
            bool regular = post.Author != null && Regulars.Contains(post.Author);
            string bond = regular ? $"  {new string('♥', S.IntimacyOf(post.Author))}{new string('·', 3 - S.IntimacyOf(post.Author))}{(S.SulkyTonight(post.Author) ? " (｀ε´)" : "")}" : "";
            GUILayout.Label($"{(post.Id.StartsWith("f") || post.Id.StartsWith("m") ? "→ " : "")}{(Conform >= 0.9f && !post.Holdout ? Mimic : Handle(post))}{bond}", new GUIStyle(dotStyle) { fontSize = 14 });
            if (regular) Explain("단골 친밀도. 친해질수록 답장 조각이 줄어든다.\n1: 입 후보 셋  2: 입이 저절로  3: 눈 후보 둘\n틀리거나 미루면 서운해한다: 다음 밤 입은 따끔·쿨만.");
            // 글 조각을 모아 두었다가 폭(maxW)을 넘으면 다음 줄로: 가로 스크롤이 생기지 않게
            var items = new List<(string text, GUIStyle style, Action after)>();
            if (color) items.Add(($"{E[hue].Shape}?", chips[hue], () => HoverEmo(hue)));
            // 팔로워가 늘수록 팬들도 카오모지로 말한다: 글 사이사이나 끝에, 정답과 상관없는 얼굴로
            int kao = FanKaomoji, seed = post.Id.Sum(ch => ch);
            var kaoAt = Enumerable.Range(0, kao).Select(n => (seed + n * 5) % (tokens.Length + 1)).ToList();
            void KaoBefore(int k) { foreach (var n in Enumerable.Range(0, kao).Where(n => kaoAt[n] == k)) items.Add((FanFaces[(seed + n * 3) % FanFaces.Length], postStyle, null)); }
            bool firstSym = true;
            for (int k = 0; k < tokens.Length; k++)
            {
                KaoBefore(k);
                var t = tokens[k];
                bool tint = k < tinted;
                if (!Sym.TryGetValue(t, out var e))
                {
                    if (!post.Holdout && Replaced(seed, k)) { items.Add((CrowdFace(seed + k), postStyle, null)); continue; } // 낱말 대신 얼굴
                    items.Add((t, tint ? tintText[hue] : postStyle, tint ? () => HoverEmo(hue) : null));
                    continue;
                }
                bool mark = firstSym && i >= 0;
                firstSym = false;
                // 해독한 기호는 그 감각의 색으로만. 이름은 수첩에서
                // 연필로 추측한 기호는 그 감각 색에 물음표 (맞는지는 알려 주지 않는다)
                bool guessed = !S.Solved(t) && S.Pencil.TryGetValue(t, out var g);
                items.Add((guessed ? t + "?" : t, S.Solved(t) ? chips[e] : guessed ? tintText[S.Pencil[t]] : tint ? tintText[hue] : symStyle, () => { HoverSym(t); if (mark) Mark($"sym_{i}"); }));
            }
            KaoBefore(tokens.Length);

            float maxW = forceColor ? 360 : 520, x = 0;
            GUILayout.BeginHorizontal();
            foreach (var (text, style, after) in items)
            {
                float w = style.CalcSize(new GUIContent(text)).x + style.margin.horizontal;
                if (x > 0 && x + w > maxW) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); x = 0; }
                GUILayout.Label(text, style, GUILayout.ExpandWidth(false));
                after?.Invoke();
                x += w;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (S.Followers > 0) GUILayout.Label(Reveal(post), small);
            GUILayout.Label(string.Join("  ", post.Clues.Take(S.Traits[Trait.Decode] >= 1 ? 2 : 1)), small);
            if (i >= 0) Mark($"clue_{i}");
        }

        static readonly string[] FanFaces = { "(・_・)ノ", "(・o・)", "(・∀・)", "(・ｪ・)", "(・.・)", "ヽ(・_・)ノ" };

        // 팔로워 비율만큼 실제 문장의 글자가 읽힌다 (글자마다 정해진 순서로 하나씩 풀린다)
        string Reveal(Post p) => Censor(p.Plain, p.Id.Sum(c => c), p.Holdout);

        // 한글만 가린다(기호와 카오모지는 그대로). 팔로워 비율만큼 정해진 순서로 풀린다
        string Censor(string text, int seed, bool holdout = false)
        {
            float ratio = Mathf.Clamp01((float)S.Followers / Balance.FollowerGoal);
            var censored = new string(text.Select((c, k) => c < 0xAC00 || c > 0xD7A3 || (seed + k * 37) % 100 < ratio * 100 ? c : '■').ToArray());
            return holdout ? censored : Kaomojify(censored, seed);
        }

        // 팔로워 40%부터 사람들이 한국어를 버리기 시작해 90%면 거의 카오모지만. 80%를 넘으면 모두 주인공의 얼굴을 따라 한다
        float Conform => Mathf.Clamp01(((float)S.Followers / Balance.FollowerGoal - 0.4f) / 0.5f);
        string lastFace; // 주인공이 마지막으로 지은 얼굴
        string Mimic => lastFace ?? "(・_・)";
        string CrowdFace(int seed) => Conform > 0.8f ? Mimic : FanFaces[Math.Abs(seed) % FanFaces.Length];
        bool Replaced(int seed, int k) => (seed * 31 + k * 57) % 100 < Conform * 100;

        string Kaomojify(string text, int seed)
        {
            if (Conform <= 0) return text;
            var words = text.Split(' ');
            return string.Join(" ", words.Select((w, k) => Replaced(seed, k) ? CrowdFace(seed + k) : w));
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
            // 단골은 올라오기 전에 입력 중 표시: 길수록 감정이 강하고(시너지), 썼다 지우듯 깜빡이면 섬뜩을 반기는 글
            float typing = TypingFor(post, i);
            if (rep == null && Time.time - snsOpenT < typing)
            {
                GUILayout.Label(Handle(post), new GUIStyle(dotStyle) { fontSize = 14 });
                bool blink = post.WantsCreepy && (int)(Time.time * 5) % 2 == 0;
                GUILayout.Label(blink ? " " : new string('·', 1 + (int)(Time.time * 3) % 3) + " 입력 중", label, GUILayout.Height(30));
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                Mark($"post_{i}");
                GUILayout.Space(6);
                return;
            }
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
                    GUILayout.Label($"{MyHandle}  {rep.Face}{(rep.Face2 != null ? "  " + rep.Face2 : "")}  {HitGlyph(rep.EyeHit)}{HitGlyph(rep.MouthHit)}", new GUIStyle(dotStyle) { fontSize = 16, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(28));
                    Explain("눈 칸 · 입 칸 반응\n✓ 그 칸 그대로 정답\n△ 정답에 있는 감정, 다른 칸\n· 정답에 없는 감정");
                    GUILayout.BeginVertical(GUILayout.Width(110));
                    StampIn(ResultName(rep.R.Value), fx.Item2, 24);
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    if (fx.Item1 != null)
                    {
                        GUILayout.BeginHorizontal();
                        Avatar(glyph, color, 28);
                        GUILayout.Label(Censor(fx.Item1, post.Id.Sum(c => c) + 7, post.Holdout), label, GUILayout.Height(28));
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

        static string HitGlyph(Hit h) => h switch { Hit.Exact => "✓", Hit.Elsewhere => "△", _ => "·" };

        float TypingFor(Post p, int i) =>
            InTutorial || p.Author == null || !Regulars.Contains(p.Author) ? 0 : (p.WantsCreepy ? 3f : p.Answers.Any(a => a.Eye != a.Mouth) ? 2.6f : 1.2f) + i * 0.3f;

        // 입력 흔들림: 피로 위험 구간에서 고를 때 20% 확률로 외계 문자 하나가 끼어든다. 지우지 않고 보내면 섬뜩
        string Wobbled(string face) => wobble == null ? face : face.Insert(Math.Max(1, face.Length / 2), wobble);
        // 얼굴이 한 글자씩 찍힌다 (Emily is Away의 타이핑)
        string Typed(string face) { int n = Mathf.Clamp((int)((Time.time - typeT) * 24), 1, face.Length); return face.Substring(0, n); }

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

            var tp = !post && target >= 0 ? S.Tonight[target] : null;
            var rule = tp != null ? S.RuleFor(tp) : new ComposeRule();
            if (rule.AutoMouth.HasValue) selMouth = rule.AutoMouth.Value;
            if (rule.Eyes != null && !rule.Eyes.Contains(selEye)) selEye = rule.Eyes[0];
            if (rule.Mouths != null && !rule.Mouths.Contains(selMouth)) selMouth = rule.Mouths[0];
            bool isBlank = tp != null && tp.IsBlank;
            if (isBlank && blankFor != target) { blankFor = target; blankSlot = 0; blankFaces[0] = blankFaces[1] = (Emo.Neutral, Emo.Neutral); }
            if (isBlank)
            {
                // 빈칸 긴 글: 빈칸마다 얼굴 하나. 누른 빈칸을 아래에서 고친다
                blankFaces[blankSlot] = (selEye, selMouth);
                GUILayout.BeginHorizontal();
                for (int k = 0; k < 2; k++)
                {
                    int kk = k;
                    if (GUILayout.Button($"□{k + 1}  {Face(blankFaces[k].eye, blankFaces[k].mouth)}", blankSlot == k ? tabOn : tabOff, GUILayout.Height(32)) && !blocked)
                    { blankSlot = kk; (selEye, selMouth) = blankFaces[kk]; }
                }
                GUILayout.EndHorizontal();
                Explain("빈칸(□)이 둘인 글. 빈칸마다 따로 판정하고, 둘 다 맞히면 완벽 하나를 더 받는다.");
            }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(370));
            PartRow(S.Eyes, true, true, rule.Eyes);
            Mark("eyes");
            PartRow(S.Mouths, false, true, rule.AutoMouth.HasValue ? new List<Emo> { rule.AutoMouth.Value } : rule.Mouths);
            Mark("mouths");
            GUILayout.EndVertical();
            var syn = SynergyOf(selEye, selMouth);
            GUILayout.BeginVertical(tintCards[selEye], GUILayout.ExpandWidth(true), GUILayout.Height(104));
            GUILayout.Label(Typed(Wobbled(Face(selEye, selMouth, post ? selDecor : null))), new GUIStyle(faceStyle) { fontSize = 34 }, GUILayout.Height(64));
            if (wobble != null) { if (GUILayout.Button("← 지우기", linkBtn) && !blocked) wobble = null; }
            else if (syn != null) GUILayout.Label(S.FoundSynergies.Contains(syn.Name) ? syn.Name : "?", new GUIStyle(roundStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
            GUILayout.EndVertical();
            if (post) Btn("<size=18>▣ 게시</size>", Upload, !S.Uploaded, null, GUILayout.Width(120), GUILayout.Height(104));
            else Btn("<size=18>@ 보내기</size>", () => { if (isBlank) ReplyBlank(target); else Reply(target); }, target >= 0, null, GUILayout.Width(120), GUILayout.Height(104));
            Mark("send");
            if (!post && target >= 0) HoverFat(Balance.FatReply);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void Upload()
        {
            var before = Snap();
            var decor = selDecor;
            var face = Wobbled(Face(selEye, selMouth, decor));
            var (r, f) = S.DoUpload(selEye, selMouth, decor, wobble != null);
            wobble = null;
            if (decor != null) { frenzyUntil = Time.time + 1.8f; S.Decors.Remove(decor); selDecor = null; } // shortcut: 장식은 한 번 쓰면 사라진다고 가정, 스펙 확인 필요
            if (S.Went != null && r is Result.Perfect or Result.Success) knownMood.Add(S.Went);
            Floats(before);
            uploadR = r; uploadT = Time.time; uploadFace = face; uploadGain = f;
            lastFace = Face(selEye, selMouth);
            // 좋아요는 팔로워 수에 비례, 반응이 좋을수록 많이. 댓글도 그만큼 달린다
            float factor = r switch { Result.Perfect => 0.5f, Result.Success => 0.35f, Result.Creepy => 0.6f, Result.Meh => 0.1f, Result.Wrong => 0.03f, _ => 0.08f };
            likeTarget = Mathf.RoundToInt(Mathf.Max(30, S.Followers) * factor) + (decor != null ? 999 : 0);
            postComments.Clear(); postScroll = Vector2.zero;
            int baseN = r switch { Result.Perfect => 5, Result.Success => 4, Result.Creepy => 4, Result.Wrong => 3, Result.Meh => 2, _ => 2 };
            int n = Mathf.Min(12, baseN + S.Followers / 800); // 팔로워가 늘수록 댓글도 늘어난다
            string[] lines = r switch
            {
                Result.Perfect or Result.Success => new[] { "너무 귀여워", "오늘도 최고야", "이 표정 뭐야ㅋㅋ", "저장했어", "매일 올려 줘", "나만 알고 싶은 계정",
                    "여기 어디야?", "사진 분위기 미쳤다", "프사로 해도 돼?", "알림 설정했어", "얼굴 보고 힘 났어", "오늘 하루 이걸로 버틴다" },
                Result.Creepy => new[] { "왜 무섭지…", "표정이 이상해", "근데 계속 보게 돼", "새벽에 보면 안 될 듯", "이거 합성이야?", "눈이 웃는데 입이…",
                    "소름 돋았어", "중독성 있어", "캡처해 둠" },
                Result.Wrong => new[] { "분위기랑 안 맞아", "??", "무슨 표정이야", "사진이랑 따로 노네", "오늘 컨디션 별로야?", "언팔할까…" },
                Result.Meh => new[] { "음", "그냥 그렇네", "무표정이네", "오늘은 조용하다" },
                _ => new[] { "방 깨끗하네", "오늘은 집콕?", "쉬는 날이구나", "방 구경 좋아", "이불 귀엽다" },
            };
            int pick = S.Day * 7;
            for (int k = 0; k < n; k++)
                postComments.Add(("♣♠§∞‡"[(k + S.Day) % 5].ToString(), new[] { Pal.BtnFace, Pal.Bubble, Pal.TitleA, Pal.Gray }[k % 4],
                    Conform > 0.8f ? string.Concat(Enumerable.Repeat(face, 2 + k % 3)) // 모두 내 얼굴을 따라 한다
                    : FanKaomoji > 0 && k % 2 == 0 ? $"{lines[(pick + k) % lines.Length]} {FanFaces[(k + S.Day) % FanFaces.Length]}" : lines[(pick + k) % lines.Length]));
            diary.Add(new Sticker($"↑{face}", Pal.Bubble));
            AfterNightAction();
        }

        void Reply(int i)
        {
            if (i < 0) return;
            var before = Snap();
            var post = S.Tonight[i];
            var (r, _, newly, newSyn) = S.DoReply(i, selEye, selMouth, wobble != null);
            wobble = null;
            Floats(before);
            lastFace = Face(selEye, selMouth);
            replyFx[post.Id] = (newSyn != null ? $"{Fan(post, r)}  [{newSyn.Name}]" : Fan(post, r), Time.time);
            var rs = S.Replies[i];
            diary.Add(new Sticker($"{Face(selEye, selMouth)}{ResultMark(r)}{HitGlyph(rs.EyeHit)}{HitGlyph(rs.MouthHit)}", E[selEye].Color));
            foreach (var s in newly) diary.Add(new Sticker($"{s}{E[Sym[s]].Shape}", E[Sym[s]].Color));
            MaybeError();
            AfterNightAction();
        }

        void ReplyBlank(int i)
        {
            if (i < 0) return;
            blankFaces[blankSlot] = (selEye, selMouth);
            var before = Snap();
            var post = S.Tonight[i];
            var ((e1, m1), (e2, m2)) = (blankFaces[0], blankFaces[1]);
            var (r1, r2, _, newly) = S.DoReplyBlank(i, e1, m1, e2, m2, wobble != null);
            wobble = null; blankFor = -1;
            Floats(before);
            lastFace = Face(e2, m2);
            var worse = r1 > r2 ? r1 : r2;
            replyFx[post.Id] = (Fan(post, worse), Time.time);
            diary.Add(new Sticker($"{Face(e1, m1)}{ResultMark(r1)} {Face(e2, m2)}{ResultMark(r2)}", E[e1].Color));
            foreach (var s in newly) diary.Add(new Sticker($"{s}{E[Sym[s]].Shape}", E[Sym[s]].Color));
            MaybeError();
            AfterNightAction();
        }

        static string ResultMark(Result r) => r switch
        {
            Result.Perfect => "↑↑", Result.Success => "↑", Result.Creepy => "~?", Result.Meh => "…", _ => "×", // 감정 모양(★▼◆!♥)과 겹치지 않게
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

        void PartRow(List<Emo> owned, bool eye, bool live, List<Emo> only = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(eye ? "눈" : "입", new GUIStyle(label) { alignment = TextAnchor.MiddleLeft }, GUILayout.Width(26), GUILayout.Height(46));
            foreach (var e in PartOrder)
            {
                var opts = new[] { GUILayout.Width(50), GUILayout.Height(46) };
                var part = eye ? E[e].EyeL + " " + E[e].EyeR : E[e].Mouth;
                if (only != null && !only.Contains(e)) { GUILayout.Label(owned.Contains(e) ? part : "?", offBtn, opts); continue; } // 단골이 좁혀 준 후보 밖
                if (only == null && !owned.Contains(e)) { GUILayout.Label("?", offBtn, opts); continue; }
                bool sel = eye ? selEye == e : selMouth == e;
                if (GUILayout.Button(part, sel ? partSel[e] : partBtn[e], opts) && live && !blocked)
                {
                    if (eye) selEye = e; else selMouth = e;
                    typeT = Time.time;
                    if (open.Contains(Win.Sns) && S.InDanger && UnityEngine.Random.value < Balance.InputWobble)
                        wobble = Sym.Keys.ElementAt(UnityEngine.Random.Range(0, Sym.Count));
                }
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
                    if (S.Solved(s)) { ColorCell(E[e].Color, E[e].Color, s, E[e].Name, 72, true, hot); continue; }
                    // 못 푼 칸: 누를 때마다 연필 추측이 바뀐다 (맞는지는 알려 주지 않는다)
                    string pencil = S.Pencil.TryGetValue(s, out var g) ? $"≒{E[g].Shape} " : "";
                    var bg = S.Pencil.ContainsKey(s) ? E[g].Color : S.IsPending(s) ? Pal.Lightest : Pal.Paper;
                    var r = ColorCell(bg, bg, s, S.IsPending(s) ? $"{pencil}검증" : S.Stickers.TryGetValue(s, out var c) ? $"{pencil}{c}/{need}" : pencil == "" ? "?" : pencil, 72, true, hot);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none) && !blocked) pending = () => CyclePencil(s);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            if (seen.Count > 0) GUILayout.Label("칸을 누르면 연필로 추측을 적는다.", small);
            // 검증 대기가 셋 모이면 그림 페이지: 장면과 짝지어 한꺼번에 확정
            if (S.CanOpenPage) { Btn($"▤ 그림 페이지  ({S.PageSymbols.Count})", OpenPage, true, null, GUILayout.Height(36)); Mark("page"); }
            else if (S.PendingSymbols.Count > 0)
                GUILayout.Label(S.Day < S.PageLockedUntil ? "▤ 페이지가 잠겼다. 내일 밤 다시." : $"▤ 검증 대기 {S.PendingSymbols.Count}/3", small);
            if (S.Scraps.Count > 0)
            {
                GUILayout.Label($"스크랩 {S.Scraps.Count}", label);
                foreach (var sc in S.Scraps.Skip(Math.Max(0, S.Scraps.Count - 3))) GUILayout.Label(sc, new GUIStyle(dotStyle) { fontSize = 14 });
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

        void CyclePencil(string s)
        {
            var order = new List<Emo?> { null, Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };
            Emo? cur = S.Pencil.TryGetValue(s, out var g) ? g : null;
            S.SetPencil(s, order[(order.IndexOf(cur) + 1) % order.Count]);
        }

        void OpenPage()
        {
            pageGuess.Clear();
            foreach (var s in S.PageSymbols) if (S.Pencil.TryGetValue(s, out var g)) pageGuess[s] = g;
            popups.Enqueue(new Popup { Title = "▤ 그림 페이지", Ok = "도장", Draw = () =>
            {
                GUILayout.Label("기호마다 어울리는 장면을 고른다. 모두 맞아야 확정된다.", small);
                foreach (var s in S.PageSymbols)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(s, new GUIStyle(symStyle) { alignment = TextAnchor.MiddleCenter }, GUILayout.Width(36), GUILayout.Height(52));
                    foreach (var e in Feelings)
                    {
                        bool on = pageGuess.TryGetValue(s, out var g) && g == e;
                        var st = new GUIStyle(on ? partSel[e] : partBtn[e]) { font = body, fontSize = 11, wordWrap = true };
                        if (GUILayout.Button(Scenes[e], st, GUILayout.Width(66), GUILayout.Height(52))) pageGuess[s] = e;
                    }
                    GUILayout.EndHorizontal();
                }
            }, OnOk = () =>
            {
                if (S.PageSymbols.Any(s => !pageGuess.ContainsKey(s))) { OpenPage(); return; }
                var page = S.PageSymbols;
                var before = Snap();
                var (ok, wrong) = S.TryConfirm(new Dictionary<string, Emo>(pageGuess));
                Floats(before);
                if (ok)
                {
                    frenzyUntil = Time.time + 1f;
                    foreach (var s in page) diary.Add(new Sticker($"{s}{E[Sym[s]].Shape}", E[Sym[s]].Color));
                    popups.Enqueue(new Popup { Title = "▤ 확정", Draw = () =>
                    {
                        StampIn("확정", Time.time - 0.3f, 72);
                        GUILayout.Label(string.Join("  ", page.Select(s => $"{s}={E[Sym[s]].Name}")), new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
                    } });
                }
                else popups.Enqueue(new Popup { Title = "▤", Draw = () =>
                {
                    StampIn("틀림", Time.time - 0.3f, 72);
                    GUILayout.Label(wrong != null ? $"{wrong} 의 짝이 틀렸다. (카페 3단계)" : "어딘가 틀렸다. 내일 밤까지 잠긴다.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
                } });
            } });
        }

        // 칸 하나: 왼쪽 반은 a색, 오른쪽 반은 b색 (같으면 한 색). 위에 이름, 아래에 작은 글
        Rect ColorCell(string a, string b, string top, string bottom, float w, bool bigTop = false, bool hot = false)
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
            return r;
        }

        // ---------- 특성 ----------

        void DrawTraits()
        {
            GUILayout.Label($"↑ {S.Points}", new GUIStyle(dotStyle) { fontSize = 22 });
            TraitRow(Trait.Decode, "해독", "단서 +1", "스티커 1장으로 해독");
            TraitRow(Trait.Mimic, "의태", $"외출 피로 {Balance.FatOut} → {Balance.FatOutMimic}", "멘탈붕괴 때 의심 절반");
            TraitRow(Trait.Empathy, "공감", "팬 글에 감정 색", "섬뜩해도 의심 그대로");
            // 장소 능력 (Persona 5의 협력자 랭크)
            var got = GameData.Places.SelectMany(p => new[] { 2, 3 }.Where(lv => S.HasAbility(p.Id, lv)).Select(lv => $"{p.Icon} {Abilities[(p.Id, lv)]}")).ToList();
            if (got.Count > 0)
            {
                GUILayout.BeginVertical(card);
                GUILayout.Label("★ 능력", label);
                foreach (var a in got) GUILayout.Label(a, small);
                GUILayout.EndVertical();
            }
            if (traitsEndNight)
            {
                Btn("<size=20>zZ ▶</size>", () => Close(Win.Traits), true, null, GUILayout.Height(44));
                Mark("sleep");
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

        // ---------- 정산 회복 선택 (Papers, Please의 저녁 지출): 잠 −12 대신 하나를 고른다 ----------

        void DrawRest()
        {
            GUILayout.BeginVertical();
            HomeCard("zZ", "그냥 자기", Balance.RestSleep, () => Rest(Recovery.Sleep, "zZ"), "대가 없음.");
            HomeCard("~(・)~", "본모습으로 쉬기", Balance.RestTrue, () => Rest(Recovery.TrueForm, "~(・)~"), $"많이 풀리지만 의심 +{Balance.RestTrueSus}.", $"!? +{Balance.RestTrueSus}");
            HomeCard("((☆))", "고향 통신", Balance.RestCall, () => Rest(Recovery.HomeCall, "((☆))"), "내일 아침 못 푼 기호 하나에\n정답 감각을 연필로 적어 준다.");
            if (S.CanSnack) HomeCard("∪", "야식 먹기", Balance.RestSnack, () => Rest(Recovery.Snack, "∪"), $"내일 외출 피로 +{Balance.SnackOutingFat}.");
            GUILayout.EndVertical();
            Mark("rest");
        }

        void Rest(Recovery r, string icon)
        {
            var before = Snap();
            S.ApplyRecovery(r);
            Floats(before);
            diary.Add(new Sticker(icon, Pal.Bubble));
            open.Remove(Win.Rest);
            traitsEndNight = true;
            Open(Win.Traits);
        }

        // ---------- 그림 일기: 글씨 없이 그림과 스티커로 하루를 기록한다 ----------

        // 다짐 스티커: 하나를 골라야 잠들 수 있다. 다음 날을 조금 돕는다
        static readonly (Pledge p, string icon, string color, string tip)[] Pledges =
        {
            (Pledge.Hint, "?+", Pal.TitleA, "공감 연습: 내일 밤 글 하나의 색이 미리 보인다."),
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
        static string Fan(Post p, Result r) => p.Holdout
            ? (r is Result.Perfect or Result.Success ? "…너는 아직 말이 통하는구나" : "너도 똑같네. 얼굴뿐이야.")
            : (p.Author, r) switch
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
            // 기본 스킨의 회색·검정 스크롤바를 팔레트 색으로 (검정은 글자와 기호만)
            var track = new GUIStyle { fixedWidth = 12, border = new RectOffset(1, 1, 1, 1), margin = new RectOffset(4, 0, 0, 0) };
            track.normal.background = Bevel(Pal.BarBg, Pal.BarBg, Pal.Line);
            var thumb = new GUIStyle { fixedWidth = 12, border = new RectOffset(1, 1, 1, 1) };
            thumb.normal.background = thumb.hover.background = thumb.active.background = Bevel(Pal.TitleA, Pal.Lightest, Pal.Line);
            var noButton = new GUIStyle { fixedWidth = 0, fixedHeight = 0 };
            GUI.skin.verticalScrollbar = track;
            GUI.skin.verticalScrollbarThumb = thumb;
            GUI.skin.verticalScrollbarUpButton = GUI.skin.verticalScrollbarDownButton = noButton;
            GUI.skin.horizontalScrollbar = new GUIStyle(track) { fixedWidth = 0, fixedHeight = 12 };
            GUI.skin.horizontalScrollbarThumb = new GUIStyle(thumb) { fixedWidth = 0, fixedHeight = 12 };
            GUI.skin.horizontalScrollbarLeftButton = GUI.skin.horizontalScrollbarRightButton = noButton;
            GUI.skin.scrollView = GUIStyle.none;
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
            bgAct2 = Gradient(Pal.Bubble, Pal.Paper, true); bgAct2.filterMode = FilterMode.Bilinear;
            bgAct3 = Gradient(Pal.Line, Pal.ErrA, true); bgAct3.filterMode = FilterMode.Bilinear;
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
            specialOn = Boxed(Bevel(Pal.Hot, Pal.Lightest, Pal.Line), 4);
            specialOn.font = dot; specialOn.fontSize = 22; specialOn.alignment = TextAnchor.MiddleCenter;
            specialOff = new GUIStyle(specialOn);
            specialOff.normal.background = Bevel(Pal.TitleA, Pal.Lightest, Pal.Line);
            Still(specialOff);
            passerbyTag = Boxed(Bevel(Pal.Gray, Pal.Lightest, Pal.Line), 2);
            passerbyTag.font = dot; passerbyTag.fontSize = 14; passerbyTag.alignment = TextAnchor.MiddleCenter;
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
