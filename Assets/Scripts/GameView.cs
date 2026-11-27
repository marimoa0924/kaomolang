// 프로토타입 화면 (스펙 v5): 주인공의 태블릿 화면 하나 안에서 창을 여닫는다.
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

        enum Win { Places, Encounter, Home, Sns, Diary, Ending, Settings, Messenger }

        class Popup { public string Title, Ok = "▶"; public Action Draw, OnOk; }
        class Toast { public string Body, Title = "♥ ERROR ♥"; public float T0; public int Id; }
        class Floater { public string Anchor, Text; public float T0; }
        class Sticker
        {
            public string Text, Color, Place; public Memory? Kind; // Kind가 있으면 오늘의 한 장면으로 고를 수 있다
            public Sticker(string t, string c, Memory? kind = null, string place = null) { Text = t; Color = c; Kind = kind; Place = place; }
        }
        int diaryPick = -1; float diaryPickT = -9;
        class EncResult { public Encounter Ev; public bool Ok; public Emo? Mouth; public Decor Decor; public float T; }

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

        static float VW = 1280; // 화면 비율에 맞춰 늘어난다 (패드가 화면을 꽉 채우게)
        const float VH = 720, Bezel = 18, BarH = 38, PopupW = 420, ToastW = 330, ToastH = 84;
        const int PopupId = 99, FxId = 300;
        static readonly Dictionary<Win, float> WinWidth = new()
        {
            [Win.Places] = 480, [Win.Encounter] = 820, [Win.Home] = 560, [Win.Sns] = 790, [Win.Diary] = 640, [Win.Ending] = 600, [Win.Settings] = 460, [Win.Messenger] = 420,
        };
        static readonly Emo[] PartOrder = { Emo.Neutral, Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };

        GameState S = new();
        readonly List<Win> open = new();
        readonly Dictionary<Win, Rect> rects = new();
        readonly Queue<Popup> popups = new();
        readonly List<Toast> toasts = new();
        readonly List<Floater> floaters = new();
        readonly List<Sticker> diary = new();
        Rect popupRect = new((VW - PopupW) / 2, 170, PopupW, 0);
        Action pending;       // 버튼 동작은 다음 Update에서 실행해 IMGUI 레이아웃 불일치를 피한다
        int? focusReq;
        int toastSeq, followersAtDayStart;
        bool blocked;
        // 고르려는 행동이 수치를 어떻게 바꾸는지: 게이지에서 깜빡인다 (피로, 의심, 팔로워, 의심 절반 방패)
        (float fat, float sus, float f, bool shield) hover, hoverNext;
        float hoverFat => hover.fat;
        Emo selEye = Emo.Neutral, selMouth = Emo.Neutral;
        Decor selDecor;
        EncResult enc;
        (string stamp, string face, string sub)? homeResult; // 집 활동 결과: 창 안에 도장, 닫기 버튼만 남는다
        readonly Dictionary<string, (string fan, float t, string delta)> replyFx = new(); // 댓글 카드 안에 뜨는 판정, 팬의 한마디, 바뀐 수치
        string uploadDelta;
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

        Dictionary<string, Rect> anchors = new(), lastAnchors = new();
        Vector2 origin; // 지금 그리는 창의 화면 위치. Mark가 창 안 좌표를 화면 좌표로 바꿀 때 쓴다

        Font dot, body, round; // DotGothic16: 카오모지·숫자·아이콘 / Gowun Dodum: 한글 본문 / Dongle: 감정 이름·엔딩 제목
        GUIStyle label, small, big, dotStyle, faceStyle, postStyle, roundStyle, winStyle, titleStyle, errTitleStyle, toastStyle,
                 btn, offBtn, closeBtn, card, paper, bubble, iconStyle, taskbar, panel, coachStyle, hiliteStyle, bezelStyle, stampStyle,
                 cardBtn, badgeOn, badgeOff, clockOn, curtainStyle, selCard, linkBtn, tabOn, tabOff, cellFrame, specialOn, passerbyTag;
        readonly Dictionary<string, Texture2D> texCache = new();
        readonly Dictionary<string, GUIStyle> stickerStyles = new();
        readonly Dictionary<Emo, GUIStyle> chips = new(), partBtn = new(), partSel = new(), tintCards = new();
        Texture2D bgDay, bgSmear, bgAct2, bgAct3, noise;
        float noiseUntil = -9; // 피로 70 메시지와 셧다운에 덮이는 아날로그 노이즈

        // v4 화면 상태
        readonly Dictionary<int, GameState> daySaves = new(); // 날마다 자동 저장: 지난 날로 되돌아가기 (메모리에만)
        bool noticeDue = true;                                 // 막이 걷히면 오늘의 공지 카드를 띄운다
        float typeT = -9, snsOpenT = -9;                       // 얼굴이 한 글자씩 찍히는 연출 / 단골의 입력 중 표시
        int snsOpenDay;
        int blankSlot;                                         // 빈칸 긴 글에서 지금 고르는 빈칸
        readonly (Emo eye, Emo mouth)[] blankFaces = new (Emo, Emo)[2];
        int blankFor = -1;
        readonly List<Decor> mixSel = new();

        bool firstRun = true;                         // 첫 판 1일차만 정해진 쉬운 글과 만남
        bool InTutorial => firstRun && S.Day == 1;

        // ---------- 수명 주기 ----------

        void Awake()
        {
            // 학습 지도: 시스템마다 열리는 날. 판을 만들기 전에 읽어야 1일차부터 맞게 숨겨져
            var map = Resources.Load<TextAsset>("Unlocks");
            if (map != null) GameData.UnlockTable = GameData.ParseUnlocks(map.text);
            // else Debug.LogWarning("Unlocks.csv 없음: 전부 1일에 열림");
            S = new GameState();
            dot = Resources.Load<Font>("Fonts/DotGothic16-Regular");
            body = Resources.Load<Font>("Fonts/GowunDodum-Regular");
            round = Resources.Load<Font>("Fonts/Dongle-Regular");
        }

        void Start() { daySaves[S.Day] = S.Clone(); Open(Win.Messenger); } // 첫 지령은 바로 펼쳐 둔다

        void Update()
        {
            S.NoExtras = InTutorial; // 튜토리얼 동안은 특별 이벤트가 끼어들지 않는다
            if (pending != null) { var a = pending; pending = null; a(); }
            // 관제 지령: 조건이 처음 맞는 순간 메신저에 한 통. 🔔에 안 읽은 수
            foreach (var m in Orders.Where(m => !ordersSent.Contains(m.key) && m.when()).ToList())
            {
                ordersSent.Add(m.key);
                inbox.Add(m.text);
                if (!open.Contains(Win.Messenger)) unread++;
            }
            if (S.NoiseMsg != null)
            {
                // 피로 70: 그림체 밖에서 덮치는 노이즈 1초와 메시지 하나 (드물게, 매번 다른 내용)
                noiseUntil = Time.time + 1f;
                toasts.Add(new Toast { T0 = Time.time, Id = 200 + toastSeq++ % 50, Body = S.NoiseMsg, Title = "🔋 LOW BATTERY" });
                S.NoiseMsg = null;
            }
            if (!curtainSwitched && Time.time - curtainT >= CurtainIn)
            {
                curtainSwitched = true;
                var before = Snap();
                var pick = diaryPick >= 0 && diaryPick < diary.Count ? diary[diaryPick] : null;
                S.EndNight();
                if (pick?.Kind is { } m && S.Ending == Ending.None) S.ApplyMemory(m, pick.Place);
                diary.Clear(); replyFx.Clear(); diaryPick = -1;
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
                noiseUntil = Time.time + ShutRiot;
                popups.Clear();
                for (int i = 0; i < 7; i++)
                    toasts.Add(new Toast { T0 = Time.time + i * 0.08f, Id = 200 + toastSeq++ % 50, Body = Glitch(ErrFat[i % ErrFat.Length], 0.5f), Title = "🔋 LOW BATTERY" });
            }
            if (!shutSwitched && Time.time - shutT >= ShutHalt)
            {
                // 화면이 꺼져 있는 동안 날을 넘긴다
                shutSwitched = true;
                S.ForceEndDay();
                open.Clear(); popups.Clear(); toasts.Clear();
                enc = null; homeResult = null; diary.Clear(); replyFx.Clear(); diaryPick = -1;
                uploadR = null; target = -2; snsTab = -1;
                followersAtDayStart = S.Followers;
                NewDay();
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
                hover = hoverNext; hoverNext = default;
                tip = tipNext; tipNext = null;
            }
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Tex(Pal.Letterbox));
            // 여백 없이 꽉 채운다: 높이에 맞춰 키우고 남는 가로는 패드를 넓힌다 (16:9보다 좁으면 1280 폭에 맞춘다)
            float k = Mathf.Min(Screen.height / (VH + Bezel * 2), Screen.width / (1280 + Bezel * 2));
            VW = Screen.width / k - Bezel * 2;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - VW * k) / 2, (Screen.height - VH * k) / 2, 0), Quaternion.identity, new Vector3(k, k, 1));

            GUI.DrawTexture(new Rect(0, 0, VW, VH), S.Fat >= Balance.NoiseFrom || S.BrokeDown ? bgSmear : S.Act == 3 ? bgAct3 : S.Act == 2 ? bgAct2 : bgDay); // 한 번 붕괴하면 배경은 번진 채로
            blocked = popups.Count > 0;
            DrawPoster();
            DrawIcons();
            DrawWidgets();
            DrawStatusBar();
            DrawFloaters();
            GUI.Box(new Rect(-Bezel, -Bezel, VW + Bezel * 2, VH + Bezel * 2), GUIContent.none, bezelStyle);

            foreach (var w in open.ToList())
                rects[w] = GUILayout.Window((int)w + 1, rects[w], _ => DrawWin(w), GUIContent.none, winStyle, GUILayout.Width(WinWidth[w]));

            DrawToasts();
            DrawFx();
            DrawTip();
            DrawIdleHint();
            DrawCurtain();
            DrawShutdown();
            DrawNoise();

            if (popups.Count == 0) popupRect.x = (VW - PopupW) / 2; // 패드 폭이 화면 따라 바뀌니 새 팝업은 가운데로
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
                var r = new Rect(x, BarH + 20 + (open.Count + 1) * 14, wid, 0);
                if (DayGroup.Contains(w))
                    foreach (var other in DayGroup.Where(o => o != w && open.Contains(o)).ToList())
                    {
                        r.x = rects[other].x; r.y = rects[other].y;
                        open.Remove(other); // 행동 전이라 시간은 그대로 (행동 뒤엔 아이콘이 잠겨 여기 오지 않는다)
                    }
                if (w == Win.Sns) { r.x = 124; r.y = BarH + 8; }
                open.Add(w);
                rects[w] = r;
            }
            focusReq = (int)w + 1;
        }

        void Close(Win w)
        {
            open.Remove(w);
            if (w is Win.Encounter or Win.Home) S.EndAction();
            if (w == Win.Encounter) enc = null;
            if (w == Win.Sns && S.NightDone && S.Ending == Ending.None && !open.Contains(Win.Diary))
            {
                // 잠들기 전 그림 일기
                int gained = S.Followers - followersAtDayStart;
                if (gained != 0) diary.Add(new Sticker($"♥{gained:+#,0;-#,0}", Pal.BarFollow));
                if (Growth.TryGetValue(S.Day, out var g)) diary.Add(new Sticker(g, Pal.Special)); // 오늘 열린 성장은 도장처럼
                Open(Win.Diary);
            }
            if (w == Win.Diary)
            {
                curtainT = Time.time; curtainDay = S.Day; curtainSwitched = false;
            }
            if (w == Win.Home) homeResult = null;
        }

        void Restart() => Restart(S.Off, S.LastDay);

        // 설정(P2 끄고 켜기, 날 수)은 새 판에도 이어진다
        void Restart(HashSet<Feature> off, int lastDay)
        {
            S = new GameState { LastDay = lastDay };
            S.Off.UnionWith(off);
            firstRun = false;
            S.RerollFirstDay(); // 두 번째 판부터는 1일차 글도 무작위
            daySaves.Clear();
            daySaves[1] = S.Clone();
            noticeDue = true;
            open.Clear(); popups.Clear(); toasts.Clear(); floaters.Clear(); diary.Clear(); diaryPick = -1;
            followersAtDayStart = 0;
            enc = null; homeResult = null; replyFx.Clear(); uploadR = null; target = -2; snsTab = -1; knownMood.Clear();
            selEye = selMouth = Emo.Neutral;
            selDecor = null;
        }

        // 날이 바뀌면: 자동 저장, 아침 공지는 막이 걷힌 뒤
        void NewDay()
        {
            if (S.Ending == Ending.None) daySaves[S.Day] = S.Clone();
            noticeDue = true;
        }

        // 지난 날 아침으로 되돌아간다 (Papers, Please의 날짜 저장). shortcut: 메모리에만, 정식판에선 파일 저장
        void Rewind(int day)
        {
            S = daySaves[day].Clone();
            foreach (var d in daySaves.Keys.Where(d => d > day).ToList()) daySaves.Remove(d);
            open.Clear(); popups.Clear(); toasts.Clear(); floaters.Clear(); diary.Clear(); diaryPick = -1;
            followersAtDayStart = S.Followers;
            enc = null; homeResult = null; replyFx.Clear(); uploadR = null; target = -2; snsTab = -1;
            selEye = selMouth = Emo.Neutral; selDecor = null;
            noticeDue = true;
        }

        void ShowMorning()
        {
            var extra = new List<string>();
            if (S.On(Feature.DecorMix) && S.Unlocks[Feature.DecorMix].Day == S.Day) extra.Add("[집]에서 장식 둘을 섞어 새 장식을 만들 수 있다.");
            if (S.Maintenance && !InTutorial)
                popups.Enqueue(new Popup { Title = "! 정비 카드", Draw = () =>
                {
                    GUILayout.Label("(-_-)zZ", new GUIStyle(faceStyle) { fontSize = 40 });
                    GUILayout.Label("어제 셧다운으로 오늘은 공지 대신 정비.\n오늘 의심 증가가 절반이다.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
                } });
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
        }

        // ---------- 튜토리얼 코치 ----------

        // 그린 직후 호출: 이 요소의 화면 위치를 기록하고, 튜토리얼이 가리키는 중이면 깜빡이는 테두리를 씌운다
        void Mark(string key) => MarkRect(key, GUILayoutUtility.GetLastRect(), origin);

        void MarkRect(string key, Rect r, Vector2 o)
        {
            if (Event.current.type != EventType.Repaint) return;
            anchors[key] = new Rect(r.x + o.x, r.y + o.y, r.width, r.height);
            if (idleTarget != key) return; // 머뭇거릴 때 할 일에 테두리 (깜빡임 없이)
            GUI.Box(new Rect(r.x - 5, r.y - 5, r.width + 10, r.height + 10), GUIContent.none, hiliteStyle);
        }

        // 마우스를 올린 행동의 수치 변화를 게이지에 미리 보여준다
        void HoverFat(float fat, float sus = 0, float f = 0, bool shield = false)
        {
            if (Event.current.type == EventType.Repaint && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) hoverNext = (fat, sus, f, shield);
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
            One("bar_fat", b.fat - S.Fat); // 배터리는 피로의 반대: 피로가 오르면 배터리가 준다
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
            if (Event.current.type != EventType.Repaint || !r.Contains(Event.current.mousePosition)) return; // 튜토리얼 동안은 마우스 설명 없음
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
            var r = new Rect(Mathf.Clamp(a.xMax + 8, 4, VW - size.x - 30), Mathf.Clamp(a.y, BarH + 4, VH - size.y - 24), size.x + 24, size.y + 20);
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
                    // 폭주: 깨진 얼굴이 쏟아진다 (노이즈는 DrawNoise가 위에 덮는다)
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
                    GUI.Label(new Rect(0, H / 2 - 90, W, 70), Glitch("(・_・)", Mathf.Clamp01((t - ShutOff) * 0.5f)), new GUIStyle(faceStyle) { fontSize = 56 });
                    GUI.Label(new Rect(0, H / 2 + 10, W, 50), Glitch("BATTERY 0%", 0.25f), big);
                    GUI.Label(new Rect(0, H / 2 + 60, W, 30), Glitch("MIMIC.EXE POWERED OFF", 0.3f), new GUIStyle(dotStyle) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
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

        // 그림체 밖에서 덮치는 아날로그 노이즈와 주사선. 이 연출만 팔레트 예외 (밖에서 침범하는 느낌). 1초 안팎으로 짧게
        void DrawNoise()
        {
            float left = noiseUntil - Time.time;
            if (left <= 0 || Event.current.type != EventType.Repaint) return;
            noise ??= new Texture2D(160, 90) { filterMode = FilterMode.Point };
            var px = new Color32[160 * 90];
            for (int i = 0; i < px.Length; i++) { byte v = (byte)UnityEngine.Random.Range(0, 256); px[i] = new Color32(v, v, v, 255); }
            noise.SetPixels32(px); noise.Apply();
            var keep = GUI.color;
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(left) * 0.55f);
            GUI.DrawTexture(new Rect(-Bezel, -Bezel, VW + Bezel * 2, VH + Bezel * 2), noise);
            GUI.color = new Color(0, 0, 0, 0.35f);
            for (float y = (Time.time * 120) % 4; y < VH; y += 4) GUI.DrawTexture(new Rect(0, y, VW, 1), Texture2D.whiteTexture);
            GUI.color = keep;
        }

        string Monologue()
        {
            return S.BrokeDown ? "(ﾟ_ﾟ)(ﾟ_ﾟ)(ﾟ_ﾟ)(ﾟ_ﾟ)" : S.Fat < 40 ? "" : S.Fat < 70 ? "(-_-;)" : S.Fat < 100 ? "(・_・;)…(・_・;)" : "(ﾟДﾟ)(ﾟДﾟ)(ﾟДﾟ)(ﾟДﾟ)"; // 주인공은 한글을 쓰지 않는다
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
                float shown = VH - 10 - ToastH - (toasts.Count - 1 - i) * (ToastH + 8);
                var r = new Rect(VW - ToastW - 12, Mathf.Lerp(VH, shown, p), ToastW, ToastH);
                GUI.Window(t.Id, r, _ =>
                {
                    GUI.Label(new Rect(4, 4, ToastW - 8, 24), t.Title, errTitleStyle);
                    GUI.Label(new Rect(12, 34, ToastW - 24, ToastH - 38), t.Body, label);
                }, GUIContent.none, toastStyle);
                GUI.BringWindowToFront(t.Id);
            }
        }

        static readonly string[] ErrSus = { "알 수 없는 시선이 감지되었습니다.", "표정 모듈이 응답하지 않습니다.", "피부.exe 가 예기치 않게 종료되었습니다.", "촉수를 숨기시겠습니까?" };
        static readonly string[] ErrFat = NoiseMessages;

        // 의심이 높을 때 경고등. 피로 쪽 메시지는 GameState.NoiseMsg가 드물게 띄운다 (자주 뜨면 짜증 나니까)
        void MaybeError()
        {
            float heat = S.Sus / 100f;
            if (heat < 0.5f || UnityEngine.Random.value > heat) return;
            int i = Mathf.Min(ErrSus.Length - 1, (int)((heat - 0.5f) * 2 * ErrSus.Length));
            toasts.Add(new Toast { T0 = Time.time, Id = 200 + toastSeq++ % 50, Body = Glitch(ErrSus[i], (heat - 0.5f) * 0.6f) });
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
                ("icon_home", "△", "집", () => Open(Win.Home), S.CanAct && S.On(Feature.Rest), !S.On(Feature.Rest) ? "아직은 밖에 나가 볼 때." : S.IsNight ? "밤엔 집 활동 대신\nSNS를 하자." : WaitClose),
                ("icon_sns", "((•))", "SNS", () => { commentScroll = Vector2.zero; if (snsOpenDay != S.Day) { snsOpenDay = S.Day; snsOpenT = Time.time; } Open(Win.Sns); }, S.IsNight, "SNS는 밤에 열린다.\n오전·오후에 먼저 움직이자."),
                ("icon_settings", "▦", "설정", () => Open(Win.Settings), true, null),
            };
            for (int i = 0; i < icons.Length; i++)
            {
                var (key, glyph, name, act, on, why) = icons[i];
                var r = new Rect(24, BarH + 20 + i * 104, 92, 96);
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
                if (now) GUI.Label(new Rect(r.xMax - 28, r.y + 2, 26, 26), "!", badgeOn);
            }
        }

        // 바탕화면 장식: 주인공이 감정을 공부하려고 붙여 둔 포스터. 누를 필요도 기능도 없다
        void DrawPoster()
        {
            var r = new Rect(VW - 300, VH - 250, 270, 230);
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(2, r.center);
            GUI.Box(r, GUIContent.none, paper);
            for (int i = 0; i < PartOrder.Length; i++)
            {
                var e = E[PartOrder[i]];
                float y = r.y + 14 + i * 35;
                GUI.DrawTexture(new Rect(r.x + 16, y + 4, 24, 24), Tex(e.Color));
                GUI.Label(new Rect(r.x + 16, y + 2, 24, 28), e.Shape, new GUIStyle(dotStyle) { alignment = TextAnchor.MiddleCenter });
                GUI.Label(new Rect(r.x + 52, y, 80, 32), e.Name, new GUIStyle(roundStyle) { fontSize = 26 });
                GUI.Label(new Rect(r.x + 132, y, 120, 32), Face(PartOrder[i], PartOrder[i]), new GUIStyle(dotStyle) { fontSize = 20 });
            }
            GUI.matrix = keep;
        }

        void DrawWidgets()
        {
            origin = new Vector2(VW - 344, BarH + 16);
            GUILayout.BeginArea(new Rect(origin.x, origin.y, 324, 400));

            GUILayout.BeginVertical(panel);
            GUILayout.Label($"목표  ♥ {Balance.FollowerGoal:#,0}  ·  {S.LastDay - S.Day + 1}일 남음", label);
            if (S.Today != null && !InTutorial) { GUILayout.Label($"! {S.Today.Title}", label); Explain(S.Today.Rule); }
            if (S.Maintenance) { GUILayout.Label("! 정비 중", label); Explain("셧다운 다음 날. 오늘 의심 증가가 절반이다."); }
            bool frenzy = Time.time < frenzyUntil;
            Bar("bar_f", "♥ 팔로워", frenzy ? UnityEngine.Random.Range(0, Balance.FollowerGoal) : S.Followers, Balance.FollowerGoal, Pal.BarFollow,
                frenzy ? $"{S.Followers + UnityEngine.Random.Range(-999, 99999):#,0}!!" : $"{S.Followers:#,0}", hover.f, StatHelp[0]);
            // 얼굴 연습 중이거나 그걸 고르려 하면 의심 옆에 ×½ 방패
            bool shield = S.Practicing || hover.shield;
            Bar("bar_s", "!? 의심", S.Sus, 100, Pal.BarSus, $"{Mathf.RoundToInt(S.Sus)}{(shield ? "  ×½" : "")}", hover.sus, StatHelp[1]);
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
            $"♥ 팔로워: 마지막 날에 {Balance.FollowerGoal:#,0}명이 넘으면 성공.",
            "!? 의심: 100이면 정체가 들킨다.",
        };

        void Bar(string key, string name, float v, float max, string color, string value, float preview, string help)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.Width(74));
            var r = GUILayoutUtility.GetRect(130, 26, GUILayout.ExpandWidth(true));
            r.y += 7; r.height = 12;
            GUI.DrawTexture(r, Tex(Pal.BarBg));
            var f = r; f.width *= Mathf.Clamp01(v / max);
            GUI.DrawTexture(f, Tex(color));
            if (preview != 0) // 깜빡이면 눈이 아프니 그대로 보여 준다: 바뀔 구간을 다른 색으로
            {
                float a = Mathf.Clamp(v + Mathf.Min(0, preview), 0, max), b = Mathf.Clamp(v + Mathf.Max(0, preview), 0, max);
                GUI.DrawTexture(new Rect(r.x + r.width * a / max, r.y, r.width * (b - a) / max, r.height), Tex(preview > 0 ? Pal.Hot : Pal.Ease));
            }
            GUILayout.Label(preview != 0 ? $"→ {Mathf.RoundToInt(Mathf.Clamp(v + preview, 0, max)):#,0}" : value, dotStyle, GUILayout.Width(84)); // 미리보기 중엔 바뀐 뒤의 값
            GUILayout.EndHorizontal();
            Mark(key);
            Explain(help);
        }

        // 태블릿 상태 표시줄: 왼쪽 날·시간, 가운데 열린 창, 오른쪽 알림과 배터리(= 의태 피로, 숫자는 [?]에서만)
        void DrawStatusBar()
        {
            GUI.Box(new Rect(0, 0, VW, BarH), GUIContent.none, taskbar);
            var when = new Rect(10, 4, 170, BarH - 8);
            GUI.Label(when, $"{S.Day}일차  {Weird(S.Slot switch { Slot.Morning => "오전", Slot.Afternoon => "오후", _ => "밤" })}", new GUIStyle(dotStyle) { fontSize = 18, alignment = TextAnchor.MiddleLeft });
            MarkRect("status", when, Vector2.zero);
            float x = 190;
            foreach (var w in open)
            {
                if (GUI.Button(new Rect(x, 4, 70, BarH - 8), Title(w), btn)) focusReq = (int)w + 1;
                x += 74;
            }
            if (S.On(Feature.Rewind) && daySaves.Count > 1 && GUI.Button(new Rect(VW - 250, 4, 44, BarH - 8), "◁", btn) && !blocked)
                pending = () => popups.Enqueue(new Popup { Title = "◁ 되감기", Ok = "×", Draw = () =>
                {
                    GUILayout.BeginHorizontal();
                    foreach (var d in daySaves.Keys.OrderBy(d => d))
                        Btn($"{d}", () => { popups.Clear(); Rewind(d); }, true, null, GUILayout.Width(44), GUILayout.Height(36));
                    GUILayout.EndHorizontal();
                } });
            // 알림: 밤에 답할 SNS가 남아 있으면 흔들린다
            var bell = new Rect(VW - 256, 4, 56, BarH - 8);
            if (GUI.Button(bell, unread > 0 ? $"🔔{unread}" : "🔔", btn) && !blocked) pending = () => Open(Win.Messenger);
            MarkRect("bell", bell, Vector2.zero);
            // 배터리: 피로가 오를수록 칸이 닳는다. 위험할 땐 깜빡인다
            var bat = new Rect(VW - 180, 9, 120, BarH - 18);
            GUI.Label(new Rect(bat.x - 30, 4, 28, BarH - 8), "🔋", new GUIStyle(dotStyle) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
            GUI.Box(bat, GUIContent.none, cellFrame);
            // 고르려는 행동으로 찰 칸은 하늘색, 닳을 칸은 회색으로 미리 (깜빡임 없이)
            int Cells(float fat) => Mathf.CeilToInt(5 * (1 - Mathf.Clamp(fat, 0, S.FatMax) / S.FatMax));
            int now = Cells(S.Fat), after = Cells(S.Fat + hoverFat);
            for (int i = 0; i < Mathf.Max(now, after); i++)
                GUI.DrawTexture(new Rect(bat.x + 4 + i * 23, bat.y + 4, 19, bat.height - 8), Tex(i >= now ? Pal.Ease : i >= after ? Pal.Gray : Pal.BarFat));
            // 정확한 남은 양. 고르려는 행동이 있으면 바뀐 뒤 값
            int left = Mathf.RoundToInt(S.FatMax - S.Fat), leftAfter = Mathf.RoundToInt(Mathf.Clamp(S.FatMax - S.Fat - hoverFat, 0, S.FatMax));
            var num = new Rect(bat.xMax + 4, 4, 56, BarH - 8);
            GUI.Label(num, hoverFat != 0 ? $"→{leftAfter}" : $"{left}", new GUIStyle(dotStyle) { fontSize = 18, alignment = TextAnchor.MiddleLeft });
            MarkRect("bar_fat", new Rect(bat.x, bat.y + 26, bat.width + 60, bat.height), Vector2.zero);
            ExplainRect($"배터리 {left}/{S.FatMax}. 외출·답장에 닳고 잠·휴식으로 찬다. 다 닳지 않게 쉬자." + (S.BrokeDown ? " 한 번 꺼졌다. 다시 다 닳으면 끝." : ""), new Rect(bat.x - 30, 0, bat.width + 90, BarH), Vector2.zero);
        }

        // 창 제목도 글자 대신 아이콘
        string Title(Win w) => w switch
        {
            Win.Places => "→", Win.Encounter => S.PendingPlace != null ? ((enc?.Ev ?? S.Pending)?.Kind == EvKind.Special ? "★ " : "") + PlaceById(S.PendingPlace).Icon : "→",
            Win.Home => "△", Win.Sns => "((•))", Win.Diary => "zZ", Win.Settings => "▦", Win.Messenger => "📡", _ => "★",
        };

        // ---------- 관제 메신저: 모행성이 짧은 지령을 보낸다. 주인공은 카오모지로만 답한다 ----------

        readonly List<string> inbox = new();
        readonly HashSet<string> ordersSent = new();
        int unread;
        Vector2 inboxScroll;
        (string key, Func<bool> when, string text)[] orders;
        (string key, Func<bool> when, string text)[] Orders => orders ??= new (string, Func<bool>, string)[]
        {
            ("start", () => true, $"관제실이다. 임무: 인간 팔로워 {Balance.FollowerGoal:#,0}을 모아 행성을 네 얼굴로 물들여라. 정체가 들키면 끝이다."),
            ("day", () => true, "하루는 오전·오후에 한 번씩 움직이고, 밤에는 SNS를 한다. 창을 닫아야 시간이 흐른다."),
            ("out", () => true, "먼저 [외출]해서 사람을 만나라. 상대의 기분에 어울리는 눈을 지으면 된다."),
            ("mouth", () => S.Mouths.Count > 1, "반응 표정에서 입을 얻었군. 낮에 모은 눈과 입으로 밤에 얼굴을 만든다."),
            ("night", () => S.IsNight, "밤이다. [SNS]에서 사진에 얼굴을 붙여 올리고, 팬 글 셋에 얼굴로 답하라."),
            ("read", () => open.Contains(Win.Sns), "■는 못 읽는 글자. 이모지와 보이는 글자 몇 개로 마음을 짐작하라. 어려우면 ‖ 나중에."),
            ("diary", () => S.NightDone, "보고 완료. 일기에 오늘 한 장면을 골라 붙이고 자라. 고른 장면이 내일을 돕는다."),
            ("battery", () => S.Day >= 2, "위쪽 🔋를 봐라. 외출과 답장에 닳는다. 다 닳으면 의태가 꺼진다."),
            ("rest", () => S.On(Feature.Rest), "[집]을 열었다. 쉬면 배터리가 찬다. 고를 건 카드 오른쪽 그림뿐이다."),
            ("low", () => S.Fat >= Balance.NoiseFrom, "경고: 배터리 부족. 의태가 흐려진다. 쉬어라."),
            ("decor", () => S.Decors.Count > 0, "특수 장식을 얻었군. 밤에 게시물에 붙이면 팔로워가 몰린다."),
            ("off", () => S.BrokeDown, "의태가 한 번 꺼졌다. 다시 꺼지면 돌아올 수 없다."),
            ("skill", () => S.Day >= Balance.GrowOuting, "의태가 손에 익었다. 외출에 드는 배터리가 줄었다."),
            ("letters", () => S.Day >= Balance.GrowLetters, "글자 모양이 몇 개 더 눈에 익었다."),
            ("muscle", () => S.Day >= Balance.GrowRest, "표정 근육이 붙었다. 쉬면 배터리가 더 찬다."),
            ("acting", () => S.Day >= Balance.GrowNear, "연기에 몰입했다. 아쉽게 틀려도 팔로워가 떠나지 않는다."),
        };

        void DrawMessenger()
        {
            unread = 0;
            inboxScroll = GUILayout.BeginScrollView(inboxScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none, GUILayout.Height(360));
            foreach (var m in inbox)
            {
                GUILayout.BeginHorizontal();
                Avatar("📡", Pal.Bubble, 30);
                GUILayout.Label(m, bubble);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("(・_・)ゞ", new GUIStyle(dotStyle) { fontSize = 16 }); // 주인공은 카오모지로만 답한다
                Avatar("ψ", Pal.Hot, 24);
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
        }

        // 머뭇거리면(몇 초 동안 입력 없음) 지금 할 일 한 줄을 커서 옆에, 할 곳에 테두리. 움직이면 사라진다
        float lastInput;
        Vector2 lastMouse;
        string idleTarget;
        const float IdleAfter = 6f;

        (string text, string target) NowTodo()
        {
            if (open.Contains(Win.Diary)) return diaryPick < 0 && diary.Any(d => d.Kind != null) ? ("오늘 한 장면을 골라 붙이자", "diary") : ("zZ로 잠들자", "sleep");
            if (open.Contains(Win.Encounter)) return enc == null ? ("어울리는 눈을 골라 ▶", "eyes") : ("× 로 창 닫기", "close_result");
            if (open.Contains(Win.Home)) return homeResult == null ? ("휴식 하나 고르기", "home_cards") : ("× 로 창 닫기", "close_result");
            if (open.Contains(Win.Places)) return ("장소 하나 고르기", "place_0");
            if (open.Contains(Win.Sns))
                return S.NightDone ? ("× 닫기", "send") : !S.Uploaded ? (snsTab == 0 ? "얼굴 골라 ▣ 게시" : "▣ 내 게시물 탭", snsTab == 0 ? "upload" : "tab_post")
                    : (snsTab == 1 ? "@ 멘션 달기" : "@ 멘션 탭", snsTab == 1 ? "feed" : "tab_mention");
            if (S.IsNight && !S.NightDone) return ("((•)) SNS 열기", "icon_sns");
            if (S.CanAct) return ("→ 외출" + (S.On(Feature.Rest) ? " 또는 △ 집" : ""), "icon_outing");
            return (null, null);
        }

        void DrawIdleHint()
        {
            var e = Event.current;
            // 게임 화면은 MouseMove 이벤트가 안 와서, 그릴 때마다 커서 위치가 바뀌었는지로 움직임을 본다
            bool moved = e.type == EventType.Repaint && (e.mousePosition - lastMouse).sqrMagnitude > 1;
            if (moved || e.type is EventType.MouseDown or EventType.KeyDown or EventType.ScrollWheel) { lastInput = Time.time; lastMouse = e.mousePosition; }
            bool idle = Time.time - lastInput > IdleAfter && popups.Count == 0 && CurtainDone && ShutDone && S.Ending == Ending.None;
            var (text, target) = idle ? NowTodo() : (null, null);
            if (e.type == EventType.Repaint) idleTarget = target;
            if (text == null) return;
            var size = label.CalcSize(new GUIContent(text));
            var r = new Rect(Mathf.Clamp(lastMouse.x + 18, 4, VW - size.x - 28), Mathf.Clamp(lastMouse.y + 18, BarH + 4, VH - size.y - 20), size.x + 24, size.y + 16);
            GUI.Window(94, r, _ => GUI.Label(new Rect(10, 6, r.width - 16, r.height - 10), text, label), GUIContent.none, coachStyle);
            GUI.BringWindowToFront(94);
        }

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
            TitleBar(Title(w), width, w is Win.Ending or Win.Diary ? null : () => Close(w));
            switch (w)
            {
                case Win.Places: DrawPlaces(); break;
                case Win.Encounter: DrawEncounter(); break;
                case Win.Home: DrawHome(); break;
                case Win.Sns: DrawSns(); break;
                case Win.Diary: DrawDiary(); break;
                case Win.Ending: DrawEnding(); break;
                case Win.Settings: DrawSettings(); break;
                case Win.Messenger: DrawMessenger(); break;
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

        // 다음 하트까지 남은 성공 수: 2단계 뒤엔 성공 4번이 필요해 하트만으론 멈춘 것처럼 보인다
        string AffPips(string id)
        {
            int lv = S.AffLevel(id);
            if (lv >= 3) return "";
            int w = S.AffWins.GetValueOrDefault(id);
            return new string('●', w) + new string('○', Balance.AffNeed[lv] - w);
        }

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
                    Btn($"<size=40>{p.Icon}</size>\n<size=20>{Hearts(S.Aff.GetValueOrDefault(p.Id))}</size>\n<size=14>{AffPips(p.Id)}</size>",
                        () => { S.PrepareOuting(p.Id); enc = null; selEye = selMouth = Emo.Neutral; Close(Win.Places); Open(Win.Encounter); },
                        true, iconStyle, GUILayout.Height(120));
                    Mark($"place_{j}");
                    Explain(p.Name);
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
            bool special = ev.Kind == EvKind.Special;
            // 특수 이벤트: 깜빡이는 띠, 반짝이는 테두리, 얼굴 주위를 도는 별, 보상(특수 이모지) 미리보기
            if (special)
                GUILayout.Label("★ SPECIAL EVENT ★", specialOn, GUILayout.Height(34));
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
                GUILayout.Label($"{Hearts(S.Aff.GetValueOrDefault(S.PendingPlace))} <size=14>{AffPips(S.PendingPlace)}</size>", new GUIStyle(dotStyle) { fontSize = 22, richText = true });
            GUILayout.EndHorizontal();
            GUILayout.Label(ev.Situation, label);
            GUILayout.Space(6);
            GUILayout.Label(enc == null ? (S.PendingHidden ? "“……”" : $"“{ev.Say}”") : enc.Ok ? ev.WinText : ev.FailText, bubble);
            if (enc == null && S.PendingHidden) Explain("친해지자 기분을 숨긴다. 말 대신 상황으로 읽자.");
            GUILayout.EndVertical();
            if (special && Event.current.type == EventType.Repaint)
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

        void React()
        {
            var ev = S.Pending;
            if (ev == null) return;
            var before = Snap();
            var (ok, mouth, decor) = S.DoEncounter(selEye);
            enc = new EncResult { Ev = ev, Ok = ok, Mouth = mouth, Decor = decor, T = Time.time };
            Floats(before);
            diary.Add(new Sticker($"{ev.Portrait} {(ok ? "♥" : "×")}", ok ? Pal.Paper : Pal.Gray, ok ? Memory.MeetWin : Memory.MeetFail, S.Went));
            if (mouth.HasValue)
            {
                Fly(E[mouth.Value].Mouth, "gain");
                diary.Add(new Sticker($"(・{E[mouth.Value].Mouth}・)", E[mouth.Value].Color));
            }
            if (decor != null)
            {
                diary.Add(new Sticker(Face(Emo.Joy, Emo.Joy, decor), Pal.Special));
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

        // ---------- 집: 휴식 셋. 고민할 건 카드 오른쪽 숫자뿐 ----------

        void DrawHome()
        {
            if (homeResult is { } hr)
            {
                GUILayout.Space(20);
                StampIn(hr.stamp, homeT, 80);
                if (hr.sub != null) GUILayout.Label(hr.sub, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = 17 });
                GUILayout.Space(10);
                Btn("<size=20>×</size>", () => Close(Win.Home), true, null, GUILayout.Height(44));
                Mark("close_result");
                return;
            }
            HomeCard("zZ", "푹 쉬기", (S.RestFatNow(Rest.Full), Balance.RestFullSus, 0, false), () => DoRest(Rest.Full, "zZ"));
            HomeCard("((•))", "피드 구경", (S.RestFatNow(Rest.Feed), 0, Balance.FeedFollowers, false), () => DoRest(Rest.Feed, "((•))"));
            HomeCard("( ._.)", "얼굴 연습", (S.RestFatNow(Rest.Practice), 0, 0, true), () => DoRest(Rest.Practice, "( ._.)"));
            if (S.CanCombine) HomeCard("∞", "장식 섞기", default, MixPopup);
            Mark("home_cards");
        }

        void DoRest(Rest r, string icon)
        {
            var before = Snap();
            S.DoRest(r);
            Floats(before);
            HomeDone(icon, RestLine(before));
            diary.Add(new Sticker(icon, Pal.Bubble));
        }

        float homeT;
        // 효과는 숫자 대신 상태 표시줄과 같은 그림으로: 🔋▲▲ 배터리가 찬다, !?▼ 의심이 내려간다, ♥▲ 팔로워, !?×½ 방패
        // 이 선택이 실제로 바꾼 것: 문장 대신 수치 기호로 (♥ 팔로워, !? 의심, 🔋 배터리). 정상인(Aa)에게 답해 오른 의심은 따로 붙인다
        string Delta((int f, float s, float fat) b, bool holdout = false)
        {
            int f = S.Followers - b.f, sus = Mathf.RoundToInt(S.Sus - b.s); float fat = S.Fat - b.fat;
            var parts = new List<string>();
            if (f != 0) parts.Add($"♥{f:+#,0;-#,0}");
            if (sus != 0) parts.Add($"!?{sus:+0;-0}{(holdout && sus > 0 ? " Aa" : "")}");
            if (Math.Abs(fat) >= 0.5f) parts.Add($"🔋{-fat:+0;-0}");
            return string.Join("  ", parts);
        }

        static string Arrows(float d, float unit) => new string(d < 0 ? '▼' : '▲', Math.Max(1, Mathf.RoundToInt(Math.Abs(d) / unit)));

        void HomeCard(string icon, string name, (float fat, float sus, float f, bool shield) fx, Action act)
        {
            Btn($"<size=26>{icon}</size>   <size=16>{name}</size>", act, true, cardBtn, GUILayout.Height(74));
            var r = GUILayoutUtility.GetLastRect();
            HoverFat(fx.fat, fx.sus, fx.f, fx.shield);
            var parts = new List<string>();
            if (fx.fat != 0) parts.Add("🔋" + Arrows(-fx.fat, 15));
            if (fx.sus != 0) parts.Add("!?" + Arrows(fx.sus, 10));
            if (fx.f != 0) parts.Add("♥" + Arrows(fx.f, 150));
            if (fx.shield) parts.Add("!?×½");
            if (parts.Count == 0) return;
            var badge = string.Join("  ", parts);
            var bs = new GUIStyle(clockOn) { font = dot, fontSize = 16, padding = new RectOffset(8, 8, 2, 2) };
            var w = bs.CalcSize(new GUIContent(badge)).x;
            GUI.Label(new Rect(r.xMax - w - 10, r.center.y - 13, w, 26), badge, bs);
        }

        // 집 활동 결과에 쉬어서 내려간 수치를 한 줄로
        string RestLine((int f, float s, float fat) b)
        {
            var parts = new List<string> { $"🔋 배터리 {Mathf.RoundToInt(b.fat - S.Fat):+0;-0}" };
            if (Mathf.RoundToInt(S.Sus - b.s) != 0) parts.Add($"!? 의심 {Mathf.RoundToInt(S.Sus - b.s):+0;-0}");
            return string.Join("   ", parts);
        }

        void HomeDone(string stamp, string sub) { homeResult = (stamp, null, sub); homeT = Time.time; }

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
                HomeDone(Face(Emo.Joy, Emo.Joy, mix), RestLine(before));
                diary.Add(new Sticker(Face(Emo.Joy, Emo.Joy, mix), Pal.Special));
            } });
        }

        // ---------- SNS: 두 탭. [내 게시물] 인스타그램식 사진 올리기 / [멘션] 트위터식 팬 글에 멘션 달기 ----------

        const string MyHandleBase = "@kao_kao";
        string MyHandle => Weird(MyHandleBase);
        int snsTab = -1;          // 0 내 게시물, 1 멘션, -1은 아직 안 고름(열 때 할 일이 남은 쪽으로)
        int target = -2;          // 멘션 탭에서 답할 글
        string uploadFace;
        int likeTarget;
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
            "▣ 내 게시물: 사진 속 장면의 분위기에 어울리는 감정의 얼굴을 붙여 게시한다.",
            "@ 멘션: 팬 글의 마음을 짐작해 얼굴로 답한다. 어려우면 ‖ 나중에 (다음 밤에 서운해하며 돌아온다).",
            "■는 못 읽는 글자. 이모지는 뜻 그대로다. 팔로워가 늘면 읽히는 글자도 는다.",
            "틀려도 분위기가 같으면 아쉬운 오답(…). 분위기까지 틀리면 큰 오답(×).",
            "판정 아래 기호는 그 답이 바꾼 것: ♥ 팔로워, !? 의심, 🔋 배터리. Aa는 아직 글자를 쓰는 사람에게 답해 의심이 오른 것.",
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
            if (place != null && knownMood.Contains(place.Id))
                GUI.Label(new Rect(photo.xMax - 60, photo.y + 10, 48, 30), E[place.Mood].Shape, chips[place.Mood]);
            var face = S.Uploaded ? uploadFace : Typed(Face(selEye, selMouth, selDecor));
            var sr = new Rect(photo.xMax - 200, photo.yMax - 58, 186, 48);
            var keep = GUI.matrix;
            GUIUtility.RotateAroundPivot(-6, sr.center);
            GUI.Label(sr, face, StickerStyle(Pal.Lightest));
            GUI.matrix = keep;
            if (S.Uploaded)
            {
                GUILayout.BeginArea(new Rect(photo.x + 10, photo.y + 8, 240, 130));
                if (uploadR.HasValue) StampIn(ResultName(uploadR.Value), uploadT, 56);
                GUILayout.Label(uploadDelta, new GUIStyle(dotStyle) { fontSize = 15, alignment = TextAnchor.MiddleCenter });
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

        // 글 본문: 닉네임, 가려진 글(이모지와 읽히는 글자 몇 개), 사진 단서
        void PostBody(Post post, int i = -1)
        {
            int lv = S.IntimacyOf(post.Author);
            string bond = S.On(Feature.Intimacy) && Regulars.Contains(post.Author ?? "") ? $"  {new string('♥', lv)}{new string('·', 3 - lv)}{(S.SulkyTonight(post.Author) ? " (｀ε´)" : "")}" : "";
            GUILayout.Label($"{(post.Id.StartsWith("f") || post.Id.StartsWith("m") ? "→ " : "")}{(Conform >= 0.9f && !post.Holdout ? Mimic : Handle(post))}{bond}", new GUIStyle(dotStyle) { fontSize = 14 });
            GUILayout.Label(Censor(post.Text, post.Id.Sum(c => c), post.Holdout), new GUIStyle(postStyle) { wordWrap = true });
            GUILayout.Label(post.Clues[0], small);
            if (i >= 0) Mark($"clue_{i}");
        }

        static readonly string[] FanFaces = { "(・_・)ノ", "(・o・)", "(・∀・)", "(・ｪ・)", "(・.・)", "ヽ(・_・)ノ" };

        // 한글만 가린다(이모지와 카오모지는 그대로). 읽히는 글자 수는 GameState.Letters가 정하고, 어느 글자인지는 seed로 고정
        string Censor(string text, int seed, bool holdout = false)
        {
            var hangul = Enumerable.Range(0, text.Length).Where(k => text[k] >= 0xAC00 && text[k] <= 0xD7A3).ToList();
            var shown = hangul.OrderBy(k => (seed + k * 37) % 101).Take(S.Letters(hangul.Count)).ToHashSet();
            var censored = new string(text.Select((c, k) => c >= 0xAC00 && c <= 0xD7A3 && !shown.Contains(k) ? '■' : c).ToArray());
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
                // 섬뜩을 반기는 글은 깜빡임 대신 '썼다 지웠다'를 글로
                GUILayout.Label(new string('·', 1 + (int)(Time.time * 3) % 3) + (post.WantsCreepy ? " 입력 중… 지우는 중…" : " 입력 중"), label, GUILayout.Height(30));
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                Mark($"post_{i}");
                GUILayout.Space(6);
                return;
            }
            PostBody(post, i);
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
                    var fx = replyFx.TryGetValue(post.Id, out var v) ? v : (null, -9f, null);
                    GUILayout.BeginHorizontal();
                    Avatar("ψ", Pal.Hot, 28);
                    GUILayout.Label($"{MyHandle}  {rep.Face}{(rep.Face2 != null ? "  " + rep.Face2 : "")}", new GUIStyle(dotStyle) { fontSize = 16, alignment = TextAnchor.MiddleLeft }, GUILayout.Height(28));
                    GUILayout.BeginVertical(GUILayout.Width(110));
                    StampIn(ResultName(rep.R.Value), fx.Item2, 24);
                    if (fx.Item3 != null) GUILayout.Label(fx.Item3, new GUIStyle(dotStyle) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true });
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

        // 단골은 올라오기 전에 입력 중 표시: 길수록 감정이 강하고(시너지), 썼다 지우듯 깜빡이면 섬뜩을 반기는 글
        float TypingFor(Post p, int i) =>
            InTutorial || !S.On(Feature.Typing) || p.Author == null || !Regulars.Contains(p.Author) ? 0 : (p.WantsCreepy ? 3f : p.Answers.Any(a => a.Eye != a.Mouth) ? 2.6f : 1.2f) + i * 0.3f;

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
            PartRow(S.Eyes, true, true);
            Mark("eyes");
            PartRow(S.Mouths, false, true, rule.Mouths);
            Mark("mouths");
            GUILayout.EndVertical();
            var syn = SynergyOf(selEye, selMouth);
            GUILayout.BeginVertical(tintCards[selEye], GUILayout.ExpandWidth(true), GUILayout.Height(104));
            GUILayout.Label(Typed(Face(selEye, selMouth, post ? selDecor : null)), new GUIStyle(faceStyle) { fontSize = 34 }, GUILayout.Height(64));
            if (syn != null) GUILayout.Label(S.FoundSynergies.Contains(syn.Name) ? syn.Name : "?", new GUIStyle(roundStyle) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
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
            var face = Face(selEye, selMouth, decor);
            var (r, f) = S.DoUpload(selEye, selMouth, decor);
            if (decor != null) { frenzyUntil = Time.time + 1.8f; S.Decors.Remove(decor); selDecor = null; } // shortcut: 장식은 한 번 쓰면 사라진다고 가정, 스펙 확인 필요
            if (S.Went != null && r is Result.Perfect or Result.Success) knownMood.Add(S.Went);
            Floats(before);
            uploadR = r; uploadT = Time.time; uploadFace = face; uploadDelta = Delta(before);
            lastFace = Face(selEye, selMouth);
            // 좋아요는 팔로워 수에 비례, 반응이 좋을수록 많이. 댓글도 그만큼 달린다
            float factor = r switch { Result.Perfect => 0.5f, Result.Success => 0.35f, Result.Creepy => 0.6f, Result.Near => 0.1f, Result.Wrong => 0.03f, _ => 0.08f };
            likeTarget = Mathf.RoundToInt(Mathf.Max(30, S.Followers) * factor) + (decor != null ? 999 : 0);
            postComments.Clear(); postScroll = Vector2.zero;
            int baseN = r switch { Result.Perfect => 5, Result.Success => 4, Result.Creepy => 4, Result.Wrong => 3, Result.Near => 2, _ => 2 };
            int n = Mathf.Min(12, baseN + S.Followers / 800); // 팔로워가 늘수록 댓글도 늘어난다
            string[] lines = r switch
            {
                Result.Perfect or Result.Success => new[] { "너무 귀여워", "오늘도 최고야", "이 표정 뭐야ㅋㅋ", "저장했어", "매일 올려 줘", "나만 알고 싶은 계정",
                    "여기 어디야?", "사진 분위기 미쳤다", "프사로 해도 돼?", "알림 설정했어", "얼굴 보고 힘 났어", "오늘 하루 이걸로 버틴다" },
                Result.Creepy => new[] { "왜 무섭지…", "표정이 이상해", "근데 계속 보게 돼", "새벽에 보면 안 될 듯", "이거 합성이야?", "눈이 웃는데 입이…",
                    "소름 돋았어", "중독성 있어", "캡처해 둠" },
                Result.Wrong => new[] { "분위기랑 안 맞아", "??", "무슨 표정이야", "사진이랑 따로 노네", "오늘 컨디션 별로야?", "언팔할까…" },
                Result.Near => new[] { "음 거의 맞는데", "그냥 그렇네", "분위기는 알겠어", "오늘은 조용하다" },
                _ => new[] { "방 깨끗하네", "오늘은 집콕?", "쉬는 날이구나", "방 구경 좋아", "이불 귀엽다" },
            };
            int pick = S.Day * 7;
            for (int k = 0; k < n; k++)
                postComments.Add(("♣♠§∞‡"[(k + S.Day) % 5].ToString(), new[] { Pal.BtnFace, Pal.Bubble, Pal.TitleA, Pal.Gray }[k % 4],
                    Conform > 0.8f ? string.Concat(Enumerable.Repeat(face, 2 + k % 3)) // 모두 내 얼굴을 따라 한다
                    : FanKaomoji > 0 && k % 2 == 0 ? $"{lines[(pick + k) % lines.Length]} {FanFaces[(k + S.Day) % FanFaces.Length]}" : lines[(pick + k) % lines.Length]));
            diary.Add(new Sticker($"↑{face}", Pal.Bubble, Memory.Post));
        }

        void Reply(int i)
        {
            if (i < 0) return;
            var before = Snap();
            var post = S.Tonight[i];
            var (r, _, newSyn) = S.DoReply(i, selEye, selMouth);
            Floats(before);
            lastFace = Face(selEye, selMouth);
            replyFx[post.Id] = (newSyn != null ? $"{Fan(post, r)}  [{newSyn.Name}]" : Fan(post, r), Time.time, Delta(before, post.Holdout));
            diary.Add(new Sticker($"{Face(selEye, selMouth)}{ResultMark(r)}", E[selEye].Color, r <= Result.Success ? Memory.ReplyGood : Memory.ReplyBad));
            MaybeError();
        }

        void ReplyBlank(int i)
        {
            if (i < 0) return;
            blankFaces[blankSlot] = (selEye, selMouth);
            var before = Snap();
            var post = S.Tonight[i];
            var ((e1, m1), (e2, m2)) = (blankFaces[0], blankFaces[1]);
            var (r1, r2, _) = S.DoReplyBlank(i, e1, m1, e2, m2);
            blankFor = -1;
            Floats(before);
            lastFace = Face(e2, m2);
            var worse = r1 > r2 ? r1 : r2;
            replyFx[post.Id] = (Fan(post, worse), Time.time, Delta(before, post.Holdout));
            diary.Add(new Sticker($"{Face(e1, m1)}{ResultMark(r1)} {Face(e2, m2)}{ResultMark(r2)}", E[e1].Color, worse <= Result.Success ? Memory.ReplyGood : Memory.ReplyBad));
            MaybeError();
        }

        static string ResultMark(Result r) => r switch
        {
            Result.Perfect => "↑↑", Result.Success => "↑", Result.Creepy => "~?", Result.Near => "…", _ => "×", // 감정 모양(★▼◆!♥)과 겹치지 않게
        };

        void Hold(int i)
        {
            if (!S.DoHold(i)) return;
            diary.Add(new Sticker("‖", Pal.Gray));
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
                }
                Mark($"{(eye ? "eye" : "mouth")}_{e}");
            }
            GUILayout.EndHorizontal();
        }

        // ---------- 설정: 테스트 차수와 P2 기능 하나씩 끄고 켜기 ----------

        // 설정 창의 이름과 한 줄 설명 (마우스를 올리면 보인다)
        static readonly Dictionary<Feature, (string name, string what)> FeatureNames = new()
        {
            [Feature.Rest] = ("휴식", "[집]에서 푹 쉬기·피드 구경·얼굴 연습 중 하나로 배터리를 채운다."),
            [Feature.Typing] = ("입력 중 표시", "단골 글이 올라오기 전 '입력 중'이 뜬다. 길수록 감정이 세고, 깜빡이면 섬뜩을 반기는 글."),
            [Feature.Misuse] = ("오역 후속 글", "일부 글에 크게 틀리거나 섬뜩하게 답하면, 다음 밤 그 팬이 내 얼굴을 엉뚱한 곳에 쓴 글이 온다."),
            [Feature.Notices] = ("아침 공지", "2일차부터 아침마다 유행 공지 한 장. 그날 하루 규칙이 하나 바뀐다."),
            [Feature.PlaceAbility] = ("장소 능력", "장소 호감 2·3단계마다 작은 보너스가 붙는다 (외출 배터리 소모 −3 등)."),
            [Feature.Intimacy] = ("단골 친밀도", "단골에게 맞히면 ♥가 쌓이고, 틀리거나 미루면 서운해서 다음 밤 입이 따끔·쿨만 남는다."),
            [Feature.DecorMix] = ("장식 섞기", "[집]에서 특수 장식 둘을 섞어 새 장식을 만든다."),
            [Feature.HiddenMood] = ("기분 숨김", "호감 2단계부터 NPC가 절반 확률로 말을 숨긴다(“……”). 상황 설명만 보고 고른다."),
            [Feature.Maintenance] = ("정비 카드", "셧다운 다음 날 아침 정비 카드가 오고, 그날 의심 증가가 절반."),
            [Feature.Rewind] = ("되감기", "날마다 자동 저장. 위쪽 ◁ 버튼으로 지난 날 아침으로 돌아간다."),
            [Feature.BlankPosts] = ("빈칸 긴 글", "빈칸(□)이 둘인 글. 빈칸마다 얼굴을 따로 골라 답한다."),
        };

        // 켜면 지금 바로 열리고 새 판에도 이어진다 (표의 날짜보다 먼저). 끄면 이 판과 새 판 모두에서 숨긴다
        void Toggle(Feature f, bool on)
        {
            if (on) { S.Off.Add(f); return; }
            S.Off.Remove(f);
            GameData.UnlockTable[f] = S.Unlocks[f] = new Unlock(Math.Min(S.Unlocks[f].Day, S.Day), true);
        }

        void DrawSettings()
        {
            GUILayout.BeginHorizontal();
            Btn("1막만, 5일", () => Restart(S.Off, 5), true, null, GUILayout.Height(40));
            Btn("15일 전체", () => Restart(S.Off, Balance.Days), true, null, GUILayout.Height(40));
            GUILayout.EndHorizontal();
            GUILayout.Label($"누르면 새 판. 지금 {S.LastDay}일. 날짜는 Unlocks.csv", small);
            // 두 칸씩 나눠 그려야 창에 들어간다
            var all = FeatureNames.Keys.ToList();
            for (int k = 0; k < all.Count; k += 2)
            {
                GUILayout.BeginHorizontal();
                foreach (var f in all.Skip(k).Take(2))
                {
                    var u = S.Unlocks[f]; bool on = S.On(f);
                    string state = on ? "켜짐" : S.Off.Contains(f) || !u.On ? "꺼짐" : $"{u.Day}일부터";
                    Btn($"{(on ? "●" : "○")} {FeatureNames[f].name}  {state}", () => Toggle(f, on), true, on ? tabOn : tabOff, GUILayout.Height(30), GUILayout.Width(205));
                    Explain(FeatureNames[f].what);
                }
                GUILayout.EndHorizontal();
            }
        }

        // ---------- 그림 일기: 글씨 없이 그림과 스티커로 하루를 기록한다 ----------

        // 오늘의 한 장면: 고를 수 있는 스티커마다 내일 효과 (상태 표시줄과 같은 그림)
        static string MemoryBadge(Memory m) => m switch
        {
            Memory.MeetWin => "♥ 호감▲", Memory.MeetFail => "내일 → 🔋▲", Memory.ReplyGood => "♥▲",
            Memory.ReplyBad => "!?▼", _ => "내일 ↑ ♥×1.2",
        };

        void DrawDiary()
        {
            // 글씨 없이 그림과 스티커로. 고를 수 있는 스티커 중 하나를 크게 붙여야 잠들 수 있다 (고르려면 봐야 하니까)
            GUILayout.BeginVertical(paper);
            GUILayout.Label($"{S.Day}/{S.LastDay}", new GUIStyle(dotStyle) { fontSize = 26 });
            var area = GUILayoutUtility.GetRect(580, 250);
            var keep = GUI.matrix;
            var mouse = Event.current.mousePosition;
            for (int i = 0; i < diary.Count; i++)
            {
                var st = diary[i];
                // 스크랩북처럼 조금씩 비뚤게 붙인다. 고른 건 아래 칸으로 옮겨 가 빈자리가 남는다
                float x = area.x + 10 + i % 4 * 142 + i * 23 % 17, y = area.y + 6 + i / 4 * 62 + i * 31 % 9;
                var r = new Rect(x, y, 128, 52);
                GUI.matrix = keep;
                GUIUtility.RotateAroundPivot(i * 37 % 15 - 7, r.center);
                if (i == diaryPick) { GUI.Label(r, "", offBtn); continue; }
                if (st.Kind is not { } m) { GUI.Label(r, st.Text, StickerStyle(st.Color)); continue; }
                bool hot = r.Contains(Event.current.mousePosition);
                if (GUI.Button(r, hot ? MemoryBadge(m) : st.Text, StickerStyle(st.Color)) && !blocked) { diaryPick = i; diaryPickT = Time.time; }
                if (hot && Event.current.type == EventType.Repaint) hoverNext = (0, m == Memory.ReplyBad ? -5 : 0, m == Memory.ReplyGood ? 100 : 0, false); // 오른쪽 위 막대도 미리 움직인다
            }
            GUI.matrix = keep;
            // 오늘의 한 장면: 크게, 꾹 눌러 붙인다
            var big = GUILayoutUtility.GetRect(580, 90);
            if (diaryPick >= 0 && diaryPick < diary.Count)
            {
                var st = diary[diaryPick];
                float sc = 1.4f + 0.3f * Mathf.Max(0, 1 - (Time.time - diaryPickT) * 5);
                var r = new Rect(big.center.x - 90 * sc / 1.4f, big.y + 4, 180 * sc / 1.4f, 76);
                GUIUtility.RotateAroundPivot(-4, r.center);
                GUI.Label(r, $"{st.Text}   {MemoryBadge(st.Kind.Value)}", new GUIStyle(StickerStyle(st.Color)) { fontSize = Mathf.RoundToInt(16 * sc) });
                GUI.matrix = keep;
            }
            else GUI.Label(big, "☆ ?", new GUIStyle(dotStyle) { fontSize = 30, alignment = TextAnchor.MiddleCenter });
            GUILayout.EndVertical();
            Mark("diary");

            bool any = diary.Any(d => d.Kind != null);
            Btn("<size=20>zZ ▶</size>", () => Close(Win.Diary), diaryPick >= 0 || !any, null, GUILayout.Height(44));
            Mark("sleep");
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
            GUILayout.Label($"팔로워 {S.Followers:#,0}   의심 {Mathf.RoundToInt(S.Sus)}   완벽 {S.Perfects}   시너지 {S.FoundSynergies.Count}/{Synergies.Length}",
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
                Result.Near => "음… 비슷한데", _ => "??? 뭐임",
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

            bgDay = Gradient(Pal.BgTop, Pal.BgBottom, true);
            bgDay.filterMode = FilterMode.Bilinear; // 배경만 스펙대로 두 팔레트 색 사이를 부드럽게 잇는다
            bgSmear = Gradient(Pal.SmearTop, Pal.SmearBottom, true);
            bgSmear.filterMode = FilterMode.Bilinear;
            bgAct2 = Gradient(Pal.Bubble, Pal.Paper, true); bgAct2.filterMode = FilterMode.Bilinear;
            bgAct3 = Gradient(Pal.Line, Pal.ErrA, true); bgAct3.filterMode = FilterMode.Bilinear;

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
            curtainStyle = Boxed(Tex("#D5B7F5"), 0);
            selCard = Boxed(Bevel(Pal.BtnLight, Pal.Lightest, Pal.Hot), 6); // 입력창이 향하는 곳
            linkBtn = Button(Tex(Pal.Lightest), Tex(Pal.BtnLight), Tex(Pal.BtnPress), body, 13); // 인스타그램식 '답글 달기' 글자 버튼
            linkBtn.normal.background = linkBtn.focused.background = linkBtn.onNormal.background = null;
            linkBtn.fontStyle = FontStyle.Bold; linkBtn.padding = new RectOffset(4, 4, 2, 2);
            tabOn = Button(Bevel(Pal.TitleA, Pal.Lightest, Pal.Line), Bevel(Pal.TitleA, Pal.Lightest, Pal.Line), Bevel(Pal.TitleA, Pal.Line, Pal.Lightest), body, 16);
            tabOn.fontStyle = FontStyle.Bold;
            specialOn = Boxed(Bevel(Pal.Hot, Pal.Lightest, Pal.Line), 4);
            specialOn.font = dot; specialOn.fontSize = 22; specialOn.alignment = TextAnchor.MiddleCenter;
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
                // 파츠 버튼: 평소엔 밝은 바탕에 감정 색 테두리, 고르면 감정 색으로 칠한다
                partBtn[e] = Button(Bevel(Pal.Lightest, Pal.Lightest, col), Bevel(Pal.WinFace, Pal.Lightest, col), Bevel(col, col, Pal.Lightest), dot, 16);
                partSel[e] = Button(Bevel(col, Pal.Lightest, Pal.Line), Bevel(col, Pal.Lightest, Pal.Line), Bevel(col, Pal.Line, Pal.Lightest), dot, 16);
            }
        }
    }
}
