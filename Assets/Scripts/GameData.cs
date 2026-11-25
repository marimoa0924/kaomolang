// 카오모지 게임 프로토타입 데이터 (스펙 v2). Unity에 의존하지 않는 순수 C#.
// 수치는 모두 가안. Balance에서 조정한다.
using System.Collections.Generic;
using System.Linq;

namespace Kaomoji
{
    public enum Emo { Joy, Sad, Anger, Surprise, Shy, Neutral }
    public enum Result { Perfect, Success, Creepy, Meh, Wrong } // 앞일수록 좋은 판정
    public enum Ending { None, Found, Collapse, Conquer, Absorb, Forgotten }
    public enum Trait { Decode, Mimic, Empathy }
    public enum Slot { Morning, Afternoon, Night }
    public enum EvKind { Npc, Passerby, Special }
    public enum Hint { Full, Partial, None } // 색 단서가 얼마나 보이는지. 모호한 글은 일부만, 또는 안 보인다

    public static class Balance
    {
        public const int Days = 7;
        public const int FollowerGoal = 5000; // shortcut: 가안, 다음 플레이테스트로 조정
        public const float FatOut = 10, FatOutMimic = 5, FatReply = 3, FatSleep = -15, FatHome = -5, FatNap = -25;
        public const float NapSus = -10, DayFailSus = 8;
        public const float BreakdownSus = 30, BreakdownFat = -50, FatMaxAfterBreakdown = 120;
        public const float DoubterWrongSus = 15, DoubterPerfectSus = -10;
        public const int RoomUpload = 50, DecorBonus = 400;
        public const int ConquerSusMax = 70, AbsorbSolved = 12;
        public const double ExtraEventChance = 0.3; // 외출 때 행인·특수 이벤트가 고정 NPC 대신 나올 확률

        // (팔로워, 의심도)
        public static (int f, float s) Reply(Result r) => r switch
        {
            Result.Perfect => (300, 0), Result.Success => (180, 0), Result.Creepy => (400, 10),
            Result.Meh => (0, 0), _ => (-120, 10),
        };
        public static (int f, float s) Upload(Result r) => r switch
        {
            Result.Perfect => (200, 0), Result.Success => (120, 0), Result.Creepy => (250, 10),
            Result.Meh => (30, 0), _ => (-60, 5),
        };
    }

    public class EmoInfo
    {
        public string Name, Human, EyeL, EyeR, Mouth, Color, Shape; // Shape: 색이 비슷해도 구분되게 색 단서에 함께 붙는 모양
        public EmoInfo(string n, string h, string l, string r, string m, string c, string shape) { Name = n; Human = h; EyeL = l; EyeR = r; Mouth = m; Color = c; Shape = shape; }
        public string Eyes => EyeL + " " + EyeR;
    }

    // 정답 하나: 순수 감정(눈=입) 또는 시너지(눈≠입인데 말이 되는 조합)
    public class Answer
    {
        public Emo Eye, Mouth;
        public string Name;
        public Answer(Emo eye, Emo mouth, string name) { Eye = eye; Mouth = mouth; Name = name; }
        public bool IsSynergy => Eye != Mouth;
    }

    public class Decor
    {
        public string Id, Name, L, R;
        public Decor(string id, string name, string l, string r) { Id = id; Name = name; L = l; R = r; }
    }

    public class Encounter
    {
        public string Id, Who, Portrait, Situation, Say, WinText, FailText; // Portrait: 이름 대신 보여줄 얼굴 아이콘
        public EvKind Kind;
        public Emo[] Fits;           // 상대 감정에 어울리는 눈
        public Emo WinMouth, FailMouth; // 상대가 짓는 반응 표정의 입
        public Decor Decor;          // 특수 이벤트 성공 시
    }

    public class Place
    {
        public string Id, Name, Npc, Icon;
        public Emo Mood;
        public Encounter[] Story; // 호감 0, 1, 2단계
        public Encounter Passerby, Special;
    }

    public class Post
    {
        public string Id, Author, Text;
        public string Plain; // 실제로 하는 말. 팔로워가 늘수록 주인공이 조금씩 읽게 된다
        public string[] Clues; // [0] 기본, [1] 해독 1레벨부터
        public Hint Hint;
        public Answer[] Answers;
        public bool WantsCreepy;
        public Post Bad; // 단골이 지난 답장에 서운하면 대신 오는 글
    }

    public static class GameData
    {
        public const string Starlover = "별빛러버", Gloomcat = "우울한고양이", Doubter = "의심하는자";
        public static readonly string[] Regulars = { Starlover, Gloomcat, Doubter };
        public static readonly Emo[] Feelings = { Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };

        // 주인공은 감정을 몸의 감각으로 부른다
        public static readonly Dictionary<Emo, EmoInfo> E = new()
        {
            [Emo.Joy] = new("반짝", "기쁨", "^", "^", "▽", "#FFA9DE", "★"),
            [Emo.Sad] = new("축축", "슬픔", "T", "T", "﹏", "#BAE2FC", "▼"),
            [Emo.Anger] = new("따끔", "분노", "`", "´", "益", "#F29CFF", "◆"),
            [Emo.Surprise] = new("쿨", "놀람", "ﾟ", "ﾟ", "Д", "#B6FBFF", "●"),
            [Emo.Shy] = new("간질", "부끄러움", ">", "<", "ω", "#CCB8F7", "♥"),
            [Emo.Neutral] = new("민짜", "중립", "・", "・", "_", "#D6DAE5", "○"),
        };

        public static string Face(Emo eye, Emo mouth, Decor d = null) => $"{d?.L}({E[eye].EyeL}{E[mouth].Mouth}{E[eye].EyeR}){d?.R}";

        public static string ResultName(Result r) => r switch
        {
            Result.Perfect => "완벽", Result.Success => "성공", Result.Creepy => "섬뜩", Result.Meh => "시큰둥", _ => "오답",
        };

        public static Answer Pure(Emo e) => new(e, e, E[e].Name);

        // 팬 닉네임도 깨진 문자와 기호로 보인다. 단골은 늘 같은 모양이라 알아볼 수는 있다
        public static string Handle(Post p) => p.Author switch
        {
            Starlover => "▒빛■버 ★",
            Gloomcat => "■울■■양■ =ｪ=",
            Doubter => "■심■■■ ?",
            _ => "■▒■ " + "♣♠§∞‡"[p.Id.Sum(c => c) % 5], // 감정 모양(★▼◆●♥)과 겹치지 않는 기호
        };

        public static readonly Answer[] Synergies =
        {
            new(Emo.Sad, Emo.Joy, "괜찮은 척"),
            new(Emo.Joy, Emo.Anger, "질투"),
            new(Emo.Shy, Emo.Surprise, "두근"),
            new(Emo.Anger, Emo.Shy, "삐짐"),
            new(Emo.Surprise, Emo.Joy, "집착"),
        };
        public static Answer Syn(string name) => Synergies.First(s => s.Name == name);
        public static Answer SynergyOf(Emo eye, Emo mouth) => Synergies.FirstOrDefault(s => s.Eye == eye && s.Mouth == mouth);

        // 감정 기호 사전: 감정마다 4개. 폰트에 없는 † ∮ 대신 † ∮
        public static readonly Dictionary<string, Emo> Sym = new()
        {
            ["☆"] = Emo.Joy, ["♪"] = Emo.Joy, ["✿"] = Emo.Joy, ["◎"] = Emo.Joy,
            ["☂"] = Emo.Sad, ["◇"] = Emo.Sad, ["∴"] = Emo.Sad, ["≈"] = Emo.Sad,
            ["※"] = Emo.Anger, ["▲"] = Emo.Anger, ["♯"] = Emo.Anger, ["†"] = Emo.Anger,
            ["◉"] = Emo.Surprise, ["⁂"] = Emo.Surprise, ["∮"] = Emo.Surprise, ["⊙"] = Emo.Surprise,
            ["♡"] = Emo.Shy, ["∽"] = Emo.Shy, ["◐"] = Emo.Shy, ["❀"] = Emo.Shy,
        };

        public static readonly Decor Paw = new("paw", "고양이 수염", "=", "=");
        public static readonly Decor Tear = new("tear", "눈물방울", "。゜", "゜。");
        public static readonly Decor Hooray = new("hooray", "만세", "ヽ", "ノ");
        public static readonly Decor Heart = new("heart", "하트", "♥", "♥");

        // 이름 대신 보여주는 얼굴 아이콘. 상대의 감정은 드러내지 않는 민짜 얼굴
        static readonly Dictionary<string, string> Portraits = new()
        {
            ["펫시터"] = "U・ｪ・U", ["분실물센터 역무원"] = "[・_・]", ["야간 알바생"] = "(-_-)", ["신입 바리스타"] = "(・_・)♨",
            ["벤치의 할머니"] = "(・o・)", ["막차를 놓친 회사원"] = "(・_・)〓", ["꼬마 손님"] = "(・.・)", ["첫 데이트 커플"] = "(・_・)(・_・)",
            ["길고양이"] = "=・ｪ・=", ["역 피아노"] = "♪♪♪", ["복권 손님"] = "(・∀・)",
        };

        static Encounter Ev(string id, EvKind kind, string who, string sit, string say, Emo[] fits, Emo win, string winText, Emo fail, string failText, Decor decor = null) =>
            new() { Id = id, Kind = kind, Who = who, Portrait = Portraits[who], Situation = sit, Say = say, Fits = fits, WinMouth = win, WinText = winText, FailMouth = fail, FailText = failText, Decor = decor };

        static Emo[] F(params Emo[] e) => e;

        public static readonly Place[] Places =
        {
            new() { Id = "park", Icon = "♣", Name = "공원", Npc = "펫시터", Mood = Emo.Joy,
                Story = new[] {
                    Ev("park0", EvKind.Npc, "펫시터", "강아지 셋에게 끌려다니며 깔깔 웃는다.", "◁◆▽ ▣◈!",
                        F(Emo.Joy), Emo.Joy, "같이 웃어 줬다고 더 활짝 웃는다.", Emo.Surprise, "네 얼굴을 보고 흠칫한다."),
                    Ev("park1", EvKind.Npc, "펫시터", "강아지 하나가 곧 입양을 간다. 웃는데 눈가가 빨갛다.", "◆▽◁… ◁◆",
                        F(Emo.Sad), Emo.Shy, "알아봐 줘서 쑥스럽게 웃는다.", Emo.Anger, "\"지금 웃을 일이에요?\" 하고 쏘아본다."),
                    Ev("park2", EvKind.Npc, "펫시터", "입양 가는 날. 리드줄을 꼭 쥐고 있다.", "▽◆… ◈▣◁",
                        F(Emo.Sad, Emo.Joy), Emo.Joy, "\"잘 가!\" 하고 웃으며 손을 흔든다.", Emo.Sad, "결국 주저앉아 운다."),
                },
                Passerby = Ev("parkP", EvKind.Passerby, "벤치의 할머니", "비둘기에게 빵을 통째로 뺏겼다.", "▣?! ◁◁!",
                    F(Emo.Surprise, Emo.Anger), Emo.Joy, "\"그치, 어이없지?\" 하고 웃는다.", Emo.Sad, "혼자 머쓱해한다."),
                Special = Ev("parkS", EvKind.Special, "길고양이", "덤불에서 길고양이가 너를 노려본다.", "냐…",
                    F(Emo.Surprise), Emo.Joy, "고양이가 네 손에 앞발을 올렸다!", Emo.Anger, "하악 하고 도망갔다.", Paw) },

            new() { Id = "station", Icon = "〓", Name = "지하철역", Npc = "분실물센터 역무원", Mood = Emo.Sad,
                Story = new[] {
                    Ev("station0", EvKind.Npc, "분실물센터 역무원", "주인 없는 곰인형을 보며 울고 있다.", "◇▤◈ ▽◆…",
                        F(Emo.Sad), Emo.Joy, "위로받고 살짝 웃는다.", Emo.Surprise, "네 얼굴에 경악한다."),
                    Ev("station1", EvKind.Npc, "분실물센터 역무원", "인형이 여기 온 지 10년째라고 한다. 먼지를 털어 준다.", "◇▤… ▥◈▣",
                        F(Emo.Sad), Emo.Shy, "\"이상하죠, 인형한테 정이 들어서.\" 쑥스러워한다.", Emo.Anger, "\"비웃는 거예요?\" 날이 선다."),
                    Ev("station2", EvKind.Npc, "분실물센터 역무원", "인형 주인에게서 편지가 왔다! 펄쩍 뛴다.", "▥!! ◈▣◇!",
                        F(Emo.Surprise, Emo.Joy), Emo.Joy, "편지를 흔들며 같이 기뻐한다.", Emo.Sad, "\"안 기뻐요?\" 시무룩해진다."),
                },
                Passerby = Ev("stationP", EvKind.Passerby, "막차를 놓친 회사원", "막차가 떠났다. 그대로 주저앉았다.", "◇… ◇…",
                    F(Emo.Sad), Emo.Shy, "\"…고마워요.\" 고개를 숙인다.", Emo.Anger, "\"구경났어요?\" 하고 쏘아붙인다."),
                Special = Ev("stationS", EvKind.Special, "역 피아노", "누군가 역 피아노로 슬픈 곡을 친다. 모두 멈춰 섰다.", "♩… ♪…",
                    F(Emo.Sad), Emo.Sad, "너도 모르게 눈물방울이 맺혔다.", Emo.Surprise, "연주자가 너를 보고 손을 멈췄다.", Tear) },

            new() { Id = "store", Icon = "24", Name = "편의점", Npc = "야간 알바생", Mood = Emo.Anger,
                Story = new[] {
                    Ev("store0", EvKind.Npc, "야간 알바생", "진상 손님이 소리치고 나갔다. 알바생이 이를 간다.", "▨◁▣!! ◇▦▤!",
                        F(Emo.Anger), Emo.Joy, "같이 화내 줘서 피식 웃는다.", Emo.Anger, "\"손님까지 왜 그래요.\""),
                    Ev("store1", EvKind.Npc, "야간 알바생", "몰래 컵라면을 먹다가 너한테 들켰다.", "▩!? ◆▧…",
                        F(Emo.Surprise, Emo.Joy), Emo.Shy, "\"비밀이에요.\" 하고 귀가 빨개진다.", Emo.Anger, "\"신고하게요?\" 경계한다."),
                    Ev("store2", EvKind.Npc, "야간 알바생", "\"손님이 유일한 단골이에요.\" 폐기 삼각김밥을 내민다.", "▨◇▣▣… ▦~",
                        F(Emo.Joy, Emo.Shy), Emo.Joy, "처음으로 환하게 웃는다.", Emo.Sad, "머쓱하게 김밥을 거둔다."),
                },
                Passerby = Ev("storeP", EvKind.Passerby, "꼬마 손님", "동전을 세다가 모자라서 울상이다.", "◇◇… ▽?",
                    F(Emo.Sad, Emo.Surprise), Emo.Joy, "알바생이 몰래 깎아 주자 활짝 웃는다.", Emo.Sad, "엉엉 울기 시작한다."),
                Special = Ev("storeS", EvKind.Special, "복권 손님", "새벽 3시, 1등 당첨 복권이 나왔다! 다들 소리를 지른다.", "▩!!! ▩!!!",
                    F(Emo.Joy, Emo.Surprise), Emo.Joy, "다 같이 만세를 불렀다!", Emo.Surprise, "모두 너를 이상하게 쳐다본다.", Hooray) },

            new() { Id = "cafe", Icon = "♨", Name = "카페", Npc = "신입 바리스타", Mood = Emo.Shy,
                Story = new[] {
                    Ev("cafe0", EvKind.Npc, "신입 바리스타", "라떼아트 하트가 감자가 됐다. 얼굴이 빨갛다.", "▨◇▣▣▦~",
                        F(Emo.Shy, Emo.Joy), Emo.Joy, "\"다음엔 진짜 하트!\" 하고 웃는다.", Emo.Surprise, "잔을 떨어뜨릴 뻔한다."),
                    Ev("cafe1", EvKind.Npc, "신입 바리스타", "대회 연습 중. 손이 떨려 우유를 쏟았다.", "▥◈… ◆▣▤!",
                        F(Emo.Surprise, Emo.Sad), Emo.Shy, "\"보셨어요…?\" 쑥스럽게 닦는다.", Emo.Anger, "\"웃지 마세요!\" 발끈한다."),
                    Ev("cafe2", EvKind.Npc, "신입 바리스타", "대회 전날. \"보러 와 줄래요?\"", "▥◈◆▣▤… ◁▤▨?",
                        F(Emo.Shy, Emo.Joy), Emo.Shy, "고개를 푹 숙이고 웃는다.", Emo.Sad, "\"바쁘시구나…\""),
                },
                Passerby = Ev("cafeP", EvKind.Passerby, "첫 데이트 커플", "서로 눈을 못 마주치고 빨대만 문다.", "…♡ …♡",
                    F(Emo.Shy), Emo.Shy, "둘이 동시에 웃음이 터진다.", Emo.Surprise, "커플이 너를 보고 굳었다."),
                Special = Ev("cafeS", EvKind.Special, "신입 바리스타", "바리스타가 라떼를 내민다. 이번엔 진짜 하트다.", "▨♡!",
                    F(Emo.Shy, Emo.Joy), Emo.Joy, "하트를 따라 그렸다!", Emo.Sad, "하트가 금세 무너졌다.", Heart) },
        };

        public static Place PlaceById(string id) => Places.First(p => p.Id == id);

        static Post P(string author, string text, string c0, string c1, Hint hint, params Answer[] ans) =>
            new() { Author = author, Text = text, Clues = new[] { c0, c1 }, Hint = hint, Answers = ans };

        static Post Creepy(Post p) { p.WantsCreepy = true; return p; }
        static Post Or(Post p, Post bad) { p.Bad = bad; return p; }

        // 팬 글 21개: 하루 3개. 정답이 둘인 글 6개
        public static readonly Post[][] Posts =
        {
            new[] { P(Starlover, "■■ ☆ ■■■ !!!", "[사진] 케이크", "[시각] 오후 3시", Hint.Full, Pure(Emo.Joy)),
                    P(Gloomcat, "■■■ ☂ ■■ ...", "[사진] 비 오는 창문", "[시각] 새벽 3시", Hint.Full, Pure(Emo.Sad)),
                    P(null, "♪ ■■■■ ✿ ■", "[사진] 꼬리 흔드는 강아지", "[반응] 웃음×20", Hint.Full, Pure(Emo.Joy)) },
            new[] { P(Doubter, "■■ ※ ■■■■ ?", "[반응] 화남×8", "[시각] 밤 11시 50분", Hint.Full, Pure(Emo.Anger)),
                    Or(P(Gloomcat, "■ ◎ ■■ ☂ ■", "[사진] 따뜻한 코코아", "[반응] 미소×3", Hint.Partial, Syn("괜찮은 척"), Pure(Emo.Sad)),
                       P(Gloomcat, "◇ ■■■ ☂ ■■", "[사진] 텅 빈 의자", "[시각] 새벽 2시", Hint.Full, Pure(Emo.Sad))),
                    P(null, "■ ☆ ■■ ◎ ■■", "[사진] 리본 달린 선물 상자", "[반응] 케이크×5", Hint.Full, Pure(Emo.Joy)) },
            new[] { P(Starlover, "◉ ■■ ♪ ?!?!", "[사진] 네 사진만 모은 폴더", "[반응] 헉×14", Hint.Partial, Pure(Emo.Surprise), Pure(Emo.Joy)),
                    P(null, "■■ ♡ ■■■ ..", "[사진] 손편지", "[반응] 미소×3", Hint.Full, Pure(Emo.Shy)),
                    P(null, "▲ ■■■ ※ ■!!", "[사진] 금 간 폰 화면", "[반응] 빡침×9", Hint.Full, Pure(Emo.Anger)) },
            new[] { P(Doubter, "■ ⁂ ■■ ※ ■?", "[사진] 확대한 네 눈", "[반응] 의심×12", Hint.None, Pure(Emo.Surprise), Pure(Emo.Anger)),
                    Or(P(Gloomcat, "■■ ♪ ■ ∴ ~", "[사진] 다시 켠 화분 조명", "[시각] 오전 9시", Hint.Partial, Pure(Emo.Joy), Syn("괜찮은 척")),
                       P(Gloomcat, "∴ ■■ ≈ ■■■", "[사진] 시든 꽃", "[시각] 새벽 4시", Hint.Full, Pure(Emo.Sad))),
                    P(null, "■ ☆ ■■ ▲ ■", "[사진] 친구의 수상 소식", "[반응] 박수×30", Hint.None, Syn("질투")) },
            new[] { Creepy(P(Starlover, "◉ ■■ ✿ ■■ !!!", "[사진] 네 사진으로 도배된 방", "[반응] 소름×40", Hint.Partial, Syn("집착"))),
                    P(null, "∽ ■■■ ◉ ■", "[사진] 반쯤 가린 셀카", "[반응] 두근×11", Hint.Partial, Syn("두근"), Pure(Emo.Shy)),
                    P(null, "■■ ♯ ■ ▲ ■■!!", "[사진] 구겨진 영수증", "[시각] 밤 10시", Hint.Full, Pure(Emo.Anger)) },
            new[] { Or(P(Doubter, "∮ ■■■ ⊙ ?", "[사진] 정전된 거리", "[반응] 헉×20", Hint.Full, Pure(Emo.Surprise)),
                       P(Doubter, "※ ∮ ■■ † !!", "[반응] 신고×5", "[시각] 자정", Hint.None, Pure(Emo.Anger))),
                    Or(P(Gloomcat, "■ ◐ ■■ ✿ ..", "[사진] 무릎 위 고양이", "[반응] 간질×6", Hint.Partial, Pure(Emo.Shy), Pure(Emo.Joy)),
                       P(Gloomcat, "≈ ■■■ ◇ ■", "[사진] 이삿짐 상자", "[시각] 새벽 1시", Hint.Full, Pure(Emo.Sad))),
                    P(null, "♯ ■ ∽ ■■ ~", "[사진] 약속에 늦은 친구 메시지", "[반응] 흥×7", Hint.None, Syn("삐짐")) },
            new[] { Creepy(P(Starlover, "⊙ ■ ◎ ■■ ⊙ ■■■", "[사진] 네 얼굴을 오린 가면", "[시각] 새벽 4시 44분", Hint.None, Syn("집착"))),
                    P(null, "❀ ■■■ ◐ ■", "[사진] 커플 머그컵", "[반응] 하트×4", Hint.Full, Pure(Emo.Shy)),
                    Or(P(Doubter, "■ † ■■ ⁂ ?!?", "[사진] 하늘의 이상한 빛", "[반응] 외계인×99", Hint.Partial, Pure(Emo.Surprise)),
                       P(Doubter, "† ※ ■■ † !!!", "[반응] 박제×50", "[시각] 새벽 5시", Hint.None, Pure(Emo.Anger))) },
        };

        // 보류한 글은 다음 밤 서운함이 섞인 후속 글로 돌아온다
        public static Post Followup(Post held, int day) => new()
        {
            Id = $"f{day}", Author = held.Author,
            Text = $"■ {held.Text.Split(' ').First(Sym.ContainsKey)} ■■ ♯ ■ …?",
            Clues = new[] { "[반응] 서운함", "[시각] 답장 기다린 새벽" },
            Hint = Hint.Partial, Answers = new[] { Pure(Emo.Anger), Syn("삐짐") },
            Plain = "왜 어제 답 안 해 줬어…?",
        };

        static readonly Dictionary<string, string> Plains = new()
        {
            ["d1p0"] = "오늘 생일이라 케이크 먹었어!!", ["d1p1"] = "비가 와서 하루 종일 우울해...", ["d1p2"] = "우리 강아지가 꼬리를 흔들어 줬어",
            ["d2p0"] = "너 왜 매일 같은 시간에만 올려?", ["d2p1"] = "코코아 마시니까 조금 괜찮아졌어", ["d2p1b"] = "아무도 내 얘기 안 들어줘",
            ["d2p2"] = "선물 받았다 너무 신나",
            ["d3p0"] = "네 사진 벌써 백 장 모았어?!", ["d3p1"] = "손편지 받았는데 부끄러워..", ["d3p2"] = "폰 액정 또 깨졌어 진짜 짜증나!!",
            ["d4p0"] = "확대해 보니까 눈이 이상한데?", ["d4p1"] = "화분에 다시 불 켜 봤어", ["d4p1b"] = "꽃이 다 시들었어",
            ["d4p2"] = "친구가 상 받았대 축하해… 축하해",
            ["d5p0"] = "방을 네 사진으로 다 채웠어!!!", ["d5p1"] = "셀카 올렸는데 심장이 터질 것 같아", ["d5p2"] = "영수증 보니 바가지였어!!",
            ["d6p0"] = "정전된 날에도 글이 올라왔잖아?", ["d6p0b"] = "신고할 거야. 넌 사람이 아니야!!",
            ["d6p1"] = "고양이가 무릎에 올라왔어..", ["d6p1b"] = "이사 가기 싫어", ["d6p2"] = "약속에 또 늦었어 흥",
            ["d7p0"] = "네 얼굴 가면 만들었어 보여줄까", ["d7p1"] = "커플 머그컵 샀어 히히",
            ["d7p2"] = "하늘에 이상한 빛이 떴어?!", ["d7p2b"] = "다 박제해 뒀어. 끝이야!!!",
        };

        static GameData()
        {
            for (int d = 0; d < Posts.Length; d++)
                for (int i = 0; i < Posts[d].Length; i++)
                {
                    Posts[d][i].Id = $"d{d + 1}p{i}";
                    Posts[d][i].Plain = Plains[Posts[d][i].Id];
                    if (Posts[d][i].Bad != null) { Posts[d][i].Bad.Id = $"d{d + 1}p{i}b"; Posts[d][i].Bad.Plain = Plains[Posts[d][i].Bad.Id]; }
                }
        }

        public static readonly Dictionary<Ending, (string title, string text)> Endings = new()
        {
            [Ending.Conquer] = ("진엔딩 · 정복", "어느 날부터 행성이 카오모지만으로 말하기 시작한다. (^▽^) (^▽^) (^▽^)"),
            [Ending.Found] = ("실패 · 발각", "의태가 풀린 사진 한 장이 퍼졌다. 촉수가, 눈이 일곱 개가."),
            [Ending.Collapse] = ("실패 · 붕괴", "태블릿에 에러 창만 끝없이 뜬다. 사람 모양이 어땠는지 기억나지 않는다."),
            [Ending.Forgotten] = ("실패 · 잊힘", "아무도 기억하지 않는 계정. 모행성에서 소환 메시지가 온다."),
            [Ending.Absorb] = ("다른 방향 · 물듦", "너는 행성의 글자로 긴 글을 쓴다. 다 쓰고 나니, 원래 어디서 왔는지 잘 기억나지 않는다."),
        };

        // 진엔딩 에필로그: 호감 3단계까지 간 NPC마다 한 컷
        public static readonly Dictionary<string, string> Epilogue = new()
        {
            ["park"] = "펫시터가 입양 간 강아지의 새 사진을 보내 왔다. (^▽^)",
            ["station"] = "곰인형이 주인 품으로 돌아갔다. 역무원이 (T▽T) 로 인사한다.",
            ["store"] = "알바생이 정직원이 됐다. 단골 자리는 여전히 네 것.",
            ["cafe"] = "바리스타가 대회에서 상을 탔다. 하트 라떼 위에 (>ω<).",
        };
    }
}
