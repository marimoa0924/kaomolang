using System;
// 카오모지 게임 프로토타입 데이터 (스펙 v5). Unity에 의존하지 않는 순수 C#.
// 수치는 모두 가안. Balance에서 조정한다.
using System.Collections.Generic;
using System.Linq;

namespace Kaomoji
{
    public enum Emo { Joy, Sad, Anger, Surprise, Shy, Neutral }
    public enum Result { Perfect, Success, Creepy, Near, Wrong } // 앞일수록 좋은 판정. Near: 분위기는 맞은 아쉬운 오답
    public enum Ending { None, Found, Collapse, Conquer, Absorb, Forgotten }
    public enum Slot { Morning, Afternoon, Night }
    public enum EvKind { Npc, Passerby, Special }
    public enum Rest { Full, Feed, Practice } // 집 휴식 셋: 고민할 건 숫자뿐
    public enum Memory { MeetWin, MeetFail, ReplyGood, ReplyBad, Post } // 그림 일기에서 크게 붙인 오늘의 한 장면
    public enum NoticeKind { HeartDay, StarBan, NeutralWeek, NoPhoto, DoubleFace, SameEyeMouth, QuietNight, HoldoutCampaign, DecorBoom, StayIn, FaceShortage, AllNighter, HoldoutSilence, MimicDay, Final }

    public static class Balance
    {
        public const int Days = 15; // v4: 세 막 (1~5 적응, 6~10 확산, 11~15 정복). 정식판은 30일
        public static int Act(int day) => day <= 5 ? 1 : day <= 10 ? 2 : 3;
        public const int PostsPerNight = 3;
        public const float UneasyFrom = 0.5f; // 팔로워가 목표의 이만큼이면 '이상하다'는 글이 섞이기 시작
        public const int FollowerGoal = 12000;
        // 1~2일차 외출 두 번씩이면 둘째 날 밤 70을 처음 넘는다 (외출 15×4 + 답장 4×3, 잠 -12)
        public const float FatOut = 15, FatOutSkilled = 10, FatReply = 4, FatSleep = -12, DayFailSus = 8;
        public const float SusDecay = 2;          // 의심이 한 번도 오르지 않은 날 밤에 내려간다
        public const float NoiseFrom = 70;        // 피로가 이걸 넘으면 노이즈와 메시지 (사흘에 한 번까지)
        public const int NoiseGap = 3;
        // 휴식 셋 (피로, 다른 효과)
        public const float RestFull = -30, RestFullSus = -10, RestFeed = -15, RestPractice = -15, RestGrowth = 5;
        public const int FeedFollowers = 150, PracticeDays = 3;
        // 선형 성장: 이날부터 자동으로 열린다
        public const int GrowOuting = 3, GrowLetters = 6, GrowRest = 9, GrowNear = 12;
        public static readonly int[] AffNeed = { 1, 2, 3 };       // 장소 호감 0→1, 1→2, 2→3에 필요한 성공 수
        public static readonly int[] IntimacyNeed = { 2, 3, 4 }; // 단골 친밀도 (미룸)
        public const float BreakdownSus = 30, BreakdownFat = -50, FatMaxAfterBreakdown = 120;
        public const float DoubterWrongSus = 15, DoubterPerfectSus = -10;
        public const float HoldoutSus = 18; // 정상인(이상하게 여기는 사람)에게 답하면
        public const int RoomUpload = 50, DecorBonus = 400;
        public const int ConquerSusMax = 70, AbsorbPerfect = 20; // 물듦: 완벽 답장이 이만큼 쌓였는데 팔로워는 못 채움
        public const double ExtraEventChance = 0.3; // 외출 때 행인·특수 이벤트가 고정 NPC 대신 나올 확률

        // (팔로워, 의심도)
        public static (int f, float s) Reply(Result r) => r switch
        {
            Result.Perfect => (300, 0), Result.Success => (180, 0), Result.Creepy => (400, 10),
            Result.Near => (-40, 0), _ => (-120, 10),
        };
        public static (int f, float s) Upload(Result r) => r switch
        {
            Result.Perfect => (200, 0), Result.Success => (120, 0), Result.Creepy => (250, 10),
            Result.Near => (-20, 0), _ => (-60, 5),
        };
    }

    public class EmoInfo
    {
        public string Name, Human, EyeL, EyeR, Mouth, Color, Shape; // Shape: 색이 비슷해도 구분되게 색 단서에 함께 붙는 모양. 다른 UI 기호(●○ 진행 표시 등)와 겹치지 않게
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
        public string Photo; // 사진 속 장면. 아트 전까지 분위기를 추측할 단서
        public Emo Mood;
        public Encounter[][] Story;  // 호감 0, 1, 2단계 × 장면 두 가지
        public Encounter[] Passersby; // 한 번씩만 만나는 사람들
        public Encounter Special;
    }

    public class Post
    {
        public string Id, Author, Text;
        public string Plain; // 실제로 하는 말. 팔로워가 늘수록 주인공이 조금씩 읽게 된다
        public string[] Clues; // [0] 사진 단서만 보인다
        public Answer[] Answers;
        public bool WantsCreepy;
        public bool Holdout; // 다들 카오모지로만 말하는 걸 이상하게 여기는 사람: 끝까지 글자로 쓴다
        public Post Bad; // 단골이 지난 답장에 서운하면 대신 오는 글
        public Answer[] Answers2;  // 빈칸 긴 글의 두 번째 빈칸 정답 (null이면 보통 글)
        public bool Literal;       // 오역 표시: 틀리면 그 팬이 내 얼굴을 엉뚱하게 쓴 후속 글이 온다
        public string Misuse;      // 오역 후속 글이 쓰고 있는 내 얼굴
        public bool IsBlank => Answers2 != null;
        public Post Clone() => (Post)MemberwiseClone();
    }

    // 해금 표 한 줄: 몇 일차에 열리는지, 켜져 있는지, 열리는 날 코치가 짚어 줄 한 줄
    public readonly struct Unlock
    {
        public readonly int Day; public readonly bool On;
        public Unlock(int day, bool on) { Day = day; On = on; }
    }

    public static class GameData
    {
        // 기본: v5 시스템은 1일부터, 미룬 것은 꺼짐. 게임은 Unlocks.csv로 날짜를 덮어쓴다
        public static Dictionary<Feature, Unlock> UnlockTable = Enum.GetValues(typeof(Feature)).Cast<Feature>().ToDictionary(f => f, f => new Unlock(1, f <= Feature.Misuse));

        // CSV 형식: 시스템,열리는 날,켜짐(1/0). #으로 시작하는 줄과 모르는 이름은 건너뛴다
        public static Dictionary<Feature, Unlock> ParseUnlocks(string csv)
        {
            var table = new Dictionary<Feature, Unlock>(UnlockTable); // 표에 없는 시스템은 기본값 그대로
            foreach (var raw in csv.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var f = line.Split(',');
                if (f.Length < 3 || !Enum.TryParse(f[0].Trim(), out Feature key) || !int.TryParse(f[1].Trim(), out var day)) continue;
                table[key] = new Unlock(day, f[2].Trim() == "1");
            }
            return table;
        }

        public const string Starlover = "별빛러버", Gloomcat = "우울한고양이", Doubter = "의심하는자";
        public static readonly string[] Regulars = { Starlover, Gloomcat, Doubter };
        public static readonly Emo[] Feelings = { Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };

        // 주인공은 감정을 몸의 감각으로 부른다
        public static readonly Dictionary<Emo, EmoInfo> E = new()
        {
            [Emo.Joy] = new("반짝", "기쁨", "^", "^", "▽", "#FFA9DE", "★"),
            [Emo.Sad] = new("축축", "슬픔", "T", "T", "﹏", "#BAE2FC", "▼"),
            [Emo.Anger] = new("따끔", "분노", "`", "´", "益", "#F29CFF", "◆"),
            [Emo.Surprise] = new("쿨", "놀람", "ﾟ", "ﾟ", "Д", "#B6FBFF", "!"),
            [Emo.Shy] = new("간질", "부끄러움", ">", "<", "ω", "#CCB8F7", "♥"),
            [Emo.Neutral] = new("민짜", "중립", "・", "・", "_", "#D6DAE5", "-"),
        };

        public static string Face(Emo eye, Emo mouth, Decor d = null) => $"{d?.L}({E[eye].EyeL}{E[mouth].Mouth}{E[eye].EyeR}){d?.R}";

        public static string ResultName(Result r) => r switch
        {
            Result.Perfect => "완벽", Result.Success => "성공", Result.Creepy => "섬뜩", Result.Near => "아쉬움", _ => "오답",
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

        // 분위기 묶음: 같은 묶음 감정으로 틀리면 아쉬운 오답. 들뜸 반짝·간질, 가라앉음 축축·따끔, 멍함 쿨·민짜
        public static int MoodOf(Emo e) => e switch { Emo.Joy or Emo.Shy => 0, Emo.Sad or Emo.Anger => 1, _ => 2 };

        // 팬 글의 진짜 이모지 (뜻 그대로). 폰트는 흑백 Noto Emoji
        public static readonly Dictionary<Emo, string[]> Emojis = new()
        {
            [Emo.Joy] = new[] { "✨", "😆", "🎉" }, [Emo.Sad] = new[] { "😭", "💧", "😢" }, [Emo.Anger] = new[] { "💢", "😡", "🔥" },
            [Emo.Surprise] = new[] { "😱", "❗", "😳" }, [Emo.Shy] = new[] { "🥺", "💕", "☺" }, [Emo.Neutral] = new[] { "😐" },
        };

        // 감탄사·의성어는 글자 대신 이모지로
        static readonly (string re, string emoji)[] Interjections = { ("ㅋ+", "😂"), ("ㅎ+", "☺"), ("[ㅠㅜ]+", "😭"), ("헉", "😱"), ("엉엉", "😭"), ("흑흑", "😢"), ("히히", "☺") };

        // 실제 문장 → 팬 글: 감탄사를 이모지로 바꾸고 정답 감정 이모지를 가운데와 끝에 하나씩. 한글은 화면에서 가린다
        public static string Emojify(string plain, IEnumerable<Answer> answers, int seed, bool ironic = false)
        {
            foreach (var (re, emoji) in Interjections) plain = System.Text.RegularExpressions.Regex.Replace(plain, re, emoji);
            var emos = answers.SelectMany(a => new[] { a.Eye, a.Mouth }).Distinct().ToList();
            string Pick(int k)
            {
                var e = emos[k % emos.Count];
                return ironic && MoodOf(e) == 1 ? "😂" : Emojis[e][(seed + k) % Emojis[e].Length]; // 2막의 반어: 가라앉은 글에 웃는 이모지
            }
            var words = plain.Split(' ').ToList();
            words.Insert(Math.Max(1, words.Count / 2), Pick(0));
            words.Add(Pick(1));
            return string.Join(" ", words);
        }

        // 선형 성장이 열리는 날 그림 일기에 찍히는 도장 (글씨 없이 그림으로)
        public static readonly Dictionary<int, string> Growth = new()
        {
            [Balance.GrowOuting] = "→ ~↓", [Balance.GrowLetters] = "■ → 가", [Balance.GrowRest] = "zZ ~↓↓", [Balance.GrowNear] = "… ♥",
        };

        // 피로가 70을 넘을 때 뜨는 메시지: 매번 다음 것
        public static readonly string[] NoiseMessages =
        {
            "의태 드라이버가 과열되었습니다.", "얼굴이 흐르고 있습니다. (^▽T)", "피부.exe 가 응답하지 않습니다.",
            "ㅇ ㅏ ㄴ ㄴ ㅕ ㅇ 하 세 요", "촉수를 숨기시겠습니까?", "표정 모듈 온도: 위험", "사람 모양을 다시 불러오는 중…",
        };


        // 장소 호감 단계 능력 (Persona 5의 협력자 랭크)
        public static readonly Dictionary<(string place, int lv), string> Abilities = new()
        {
            [("park", 2)] = "외출 피로 −3", [("park", 3)] = "잠 피로 −5 더",
            [("station", 2)] = "피드 구경 팔로워 +100 더", [("station", 3)] = "만남 실패 의심 절반",
            [("store", 2)] = "푹 쉬기 의심 −5 더", [("store", 3)] = "게시물 팔로워 ×1.2",
            [("cafe", 2)] = "답장 팔로워 ×1.1", [("cafe", 3)] = "아쉬운 오답에 팔로워가 줄지 않는다",
        };

        public static readonly string[] ActNames = { "", "1막 · 적응", "2막 · 확산", "3막 · 정복" };

        public class Notice
        {
            public NoticeKind Kind; public string Title, Rule; public int MinAct, MaxAct = 3; public bool NeedHalf, NeedNoHalf;
        }
        // 아침 유행 공지 (Papers, Please의 일일 규칙): 2일차부터 하루 한 장, 막 안에서는 무작위
        public static readonly Notice[] Notices =
        {
            new() { Kind = NoticeKind.HeartDay, Title = "♥의 날", Rule = "간질이 정답인 글과 게시물의 팔로워 ×1.5", MinAct = 1 },
            new() { Kind = NoticeKind.StarBan, Title = "★ 자제령", Rule = "반짝 눈·입을 쓸 때마다 의심 +5", MinAct = 1 },
            new() { Kind = NoticeKind.NeutralWeek, Title = "민짜 주간", Rule = "아쉬운 오답이 성공으로 올라간다", MinAct = 1 },
            new() { Kind = NoticeKind.SameEyeMouth, Title = "똑같은 눈입", Rule = "눈과 입이 같은 감정인 답장에 팔로워 +10%", MinAct = 1 },
            new() { Kind = NoticeKind.QuietNight, Title = "조용한 밤", Rule = "멘션이 둘로 줄고 피로 −5", MinAct = 1 },
            new() { Kind = NoticeKind.StayIn, Title = "외출 자제", Rule = "외출 피로 +5, 대신 거리 말풍선의 해독 단서가 둘", MinAct = 1 },
            new() { Kind = NoticeKind.DecorBoom, Title = "장식 열풍", Rule = "특수 이모지 효과 두 배", MinAct = 1 },
            new() { Kind = NoticeKind.NoPhoto, Title = "사진 금지", Rule = "게시물 사진의 장면 설명이 가려진다", MinAct = 2 },
            new() { Kind = NoticeKind.DoubleFace, Title = "쌍얼굴 유행", Rule = "멘션 하나가 빈칸 긴 글로 바뀐다", MinAct = 2 },
            new() { Kind = NoticeKind.FaceShortage, Title = "얼굴 품귀", Rule = "어제 가장 많이 쓴 얼굴을 오늘 쓰면 팔로워 ×0.7", MinAct = 2 },
            new() { Kind = NoticeKind.AllNighter, Title = "밤샘 챌린지", Rule = "피로 +10으로 시작, 모든 답장 팔로워 ×1.3", MinAct = 2 },
            new() { Kind = NoticeKind.HoldoutCampaign, Title = "정상인 캠페인", Rule = "정상인 글이 하나 더 온다", MinAct = 2, NeedHalf = true },
            new() { Kind = NoticeKind.HoldoutSilence, Title = "정상인 침묵", Rule = "오늘 정상인 글이 없고 의심 −5", MinAct = 2 },
            new() { Kind = NoticeKind.MimicDay, Title = "따라 하기의 날", Rule = "어제 마지막 얼굴과 같은 감정의 눈으로 답하면 완벽", MinAct = 3 },
        };
        public static readonly Notice FinalNotice = new() { Kind = NoticeKind.Final, Title = "마지막 공지", Rule = "오늘이 마지막 날이다", MinAct = 3 };

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
            ["연 날리던 아이"] = "(・ｪ・)", ["길 잃은 관광객"] = "(・o・)?", ["취한 아저씨"] = "(-∀-)", ["노트북 하는 학생"] = "(・_・)≡",
        };

        static Encounter Ev(string id, EvKind kind, string who, string sit, string say, Emo[] fits, Emo win, string winText, Emo fail, string failText, Decor decor = null) =>
            new() { Id = id, Kind = kind, Who = who, Portrait = Portraits[who], Situation = sit, Say = say, Fits = fits, WinMouth = win, WinText = winText, FailMouth = fail, FailText = failText, Decor = decor };

        static Emo[] F(params Emo[] e) => e;

        // 단계마다 장면이 두 가지(무작위). 반응 입(성공 시)은 장소마다 다르게 깔아서,
        // 여러 장소를 돌며 다 성공하면 사나흘, 쉬어 가며 해도 5일차쯤엔 입 다섯 개가 모인다.
        //   1단계: 공원 반짝 / 역 간질 / 편의점 따끔 / 카페 쿨,  2단계: 공원·역 축축 / 편의점 쿨 / 카페 간질,  3단계: 공원 간질 / 역 쿨 / 편의점·카페 반짝
        public static readonly Place[] Places =
        {
            new() { Id = "park", Photo = "꼬리 흔드는 강아지들, 햇살 아래 웃음소리", Icon = "♣", Name = "공원", Npc = "펫시터", Mood = Emo.Joy,
                Story = new[] {
                    new[] {
                        Ev("park0a", EvKind.Npc, "펫시터", "강아지 셋에게 끌려다니며 깔깔 웃는다.", "◁◆▽ ▣◈!",
                            F(Emo.Joy), Emo.Joy, "같이 웃어 줬다고 더 활짝 웃는다.", Emo.Surprise, "네 얼굴을 보고 흠칫한다."),
                        Ev("park0b", EvKind.Npc, "펫시터", "강아지가 펫시터 모자를 물고 도망간다. 웃으며 쫓아간다.", "▽◁! ◆◆!",
                            F(Emo.Joy, Emo.Surprise), Emo.Joy, "모자를 되찾고 헤헤 웃는다.", Emo.Anger, "\"웃을 일 아니거든요?\" 하고 째려본다.") },
                    new[] {
                        Ev("park1a", EvKind.Npc, "펫시터", "강아지 하나가 곧 입양을 간다. 웃는데 눈가가 빨갛다.", "◆▽◁… ◁◆",
                            F(Emo.Sad), Emo.Sad, "\"보내기 싫어요…\" 입술을 삐죽이며 운다.", Emo.Anger, "\"지금 웃을 일이에요?\" 하고 쏘아본다."),
                        Ev("park1b", EvKind.Npc, "펫시터", "강아지에게 오늘이 마지막 산책이라고 속삭인다.", "◁… ◆▽…",
                            F(Emo.Sad), Emo.Sad, "강아지를 꼭 안고 훌쩍인다.", Emo.Surprise, "네 표정에 놀라 리드줄을 놓칠 뻔한다.") },
                    new[] {
                        Ev("park2a", EvKind.Npc, "펫시터", "입양 가는 날. 리드줄을 꼭 쥐고 있다.", "▽◆… ◈▣◁",
                            F(Emo.Sad, Emo.Joy), Emo.Shy, "\"같이 있어 줘서 고마워요.\" 쑥스럽게 웃는다.", Emo.Anger, "\"가 주세요.\" 하고 등을 돌린다."),
                        Ev("park2b", EvKind.Npc, "펫시터", "새 주인이 보낸 사진을 보여 준다. 강아지가 잘 지낸다.", "◈▽! ◁◆♪",
                            F(Emo.Joy), Emo.Shy, "\"저 좀 울 뻔했어요.\" 귀가 빨개진다.", Emo.Surprise, "사진을 얼른 감춘다.") },
                },
                Passersby = new[] {
                    Ev("parkP1", EvKind.Passerby, "벤치의 할머니", "비둘기에게 빵을 통째로 뺏겼다.", "▣?! ◁◁!",
                        F(Emo.Surprise, Emo.Anger), Emo.Joy, "\"그치, 어이없지?\" 하고 웃는다.", Emo.Anger, "\"뭘 봐요?\" 하고 쏘아본다."),
                    Ev("parkP2", EvKind.Passerby, "연 날리던 아이", "연이 나무에 걸렸다. 아이가 울먹인다.", "◇… ◇◇…",
                        F(Emo.Sad), Emo.Shy, "\"…괜찮아요.\" 눈물을 닦고 배시시 웃는다.", Emo.Anger, "\"뭘 봐!\" 하고 소리친다."),
                },
                Special = Ev("parkS", EvKind.Special, "길고양이", "덤불에서 길고양이가 너를 노려본다.", "냐…",
                    F(Emo.Surprise), Emo.Joy, "고양이가 네 손에 앞발을 올렸다!", Emo.Anger, "하악 하고 도망갔다.", Paw) },

            new() { Id = "station", Photo = "비 젖은 승강장, 주인 없는 곰인형", Icon = "〓", Name = "지하철역", Npc = "분실물센터 역무원", Mood = Emo.Sad,
                Story = new[] {
                    new[] {
                        Ev("station0a", EvKind.Npc, "분실물센터 역무원", "주인 없는 곰인형을 보며 울고 있다.", "◇▤◈ ▽◆…",
                            F(Emo.Sad), Emo.Shy, "위로받고 머쓱하게 웃는다.", Emo.Surprise, "네 얼굴에 경악한다."),
                        Ev("station0b", EvKind.Npc, "분실물센터 역무원", "분실물 장부를 넘기다 한숨을 쉰다. 10년째 그대로인 칸이 있다.", "◇▤… ▥◈▣",
                            F(Emo.Sad), Emo.Shy, "\"이상하죠, 물건한테 정이 들어서.\" 쑥스러워한다.", Emo.Anger, "\"놀리는 거예요?\" 날이 선다.") },
                    new[] {
                        Ev("station1a", EvKind.Npc, "분실물센터 역무원", "곰인형의 귀가 터졌다. 바늘을 들고 쩔쩔맨다.", "▥!? ◇▤…",
                            F(Emo.Sad, Emo.Surprise), Emo.Sad, "\"제가 망친 것 같아요…\" 울먹인다.", Emo.Anger, "\"보지 마세요!\" 바늘을 내려놓는다."),
                        Ev("station1b", EvKind.Npc, "분실물센터 역무원", "곰인형 주인을 찾는 전단을 붙인다. 아무도 보지 않는다.", "◇… ◇▤◈",
                            F(Emo.Sad), Emo.Sad, "전단을 쓰다듬으며 눈물을 훔친다.", Emo.Surprise, "\"…왜 그런 얼굴이에요?\" 놀란다.") },
                    new[] {
                        Ev("station2a", EvKind.Npc, "분실물센터 역무원", "인형 주인에게서 편지가 왔다! 펄쩍 뛴다.", "▥!! ◈▣◇!",
                            F(Emo.Surprise, Emo.Joy), Emo.Surprise, "입을 떡 벌리고 편지를 흔든다.", Emo.Anger, "\"안 기뻐요?\" 정색한다."),
                        Ev("station2b", EvKind.Npc, "분실물센터 역무원", "백발의 할아버지가 곰인형 앞에 멈춰 섰다. 역무원의 손이 떨린다.", "◈…!? ▥▥",
                            F(Emo.Surprise), Emo.Surprise, "\"설마… 설마!\" 숨을 들이켠다.", Emo.Anger, "\"방해하지 마세요.\" 쏘아붙인다.") },
                },
                Passersby = new[] {
                    Ev("stationP1", EvKind.Passerby, "막차를 놓친 회사원", "막차가 떠났다. 그대로 주저앉았다.", "◇… ◇…",
                        F(Emo.Sad), Emo.Shy, "\"…고마워요.\" 고개를 숙인다.", Emo.Anger, "\"구경났어요?\" 하고 쏘아붙인다."),
                    Ev("stationP2", EvKind.Passerby, "길 잃은 관광객", "지도를 거꾸로 들고 두리번거린다.", "??? ▣◁?",
                        F(Emo.Surprise), Emo.Joy, "\"땡큐!\" 하고 활짝 웃는다.", Emo.Surprise, "\"…?\" 뒷걸음친다."),
                },
                Special = Ev("stationS", EvKind.Special, "역 피아노", "누군가 역 피아노로 슬픈 곡을 친다. 모두 멈춰 섰다.", "♩… ♪…",
                    F(Emo.Sad), Emo.Sad, "너도 모르게 눈물방울이 맺혔다.", Emo.Surprise, "연주자가 너를 보고 손을 멈췄다.", Tear) },

            new() { Id = "store", Photo = "새벽 3시, 진상 손님이 엎지른 컵라면", Icon = "24", Name = "편의점", Npc = "야간 알바생", Mood = Emo.Anger,
                Story = new[] {
                    new[] {
                        Ev("store0a", EvKind.Npc, "야간 알바생", "진상 손님이 소리치고 나갔다. 알바생이 이를 간다.", "▨◁▣!! ◇▦▤!",
                            F(Emo.Anger), Emo.Anger, "\"그쵸? 진짜 너무하죠!\" 같이 씩씩댄다.", Emo.Surprise, "\"손님까지 왜 그래요?\" 놀란다."),
                        Ev("store0b", EvKind.Npc, "야간 알바생", "누가 컵라면 국물을 진열대에 쏟고 갔다.", "▨!! ▦▤▨!",
                            F(Emo.Anger, Emo.Surprise), Emo.Anger, "걸레를 쥐고 같이 투덜댄다.", Emo.Surprise, "네 표정에 걸레를 떨어뜨린다.") },
                    new[] {
                        Ev("store1a", EvKind.Npc, "야간 알바생", "몰래 컵라면을 먹다가 너한테 들켰다.", "▩!? ◆▧…",
                            F(Emo.Surprise, Emo.Joy), Emo.Surprise, "\"헉, 비밀이에요!\" 젓가락을 문 채 굳는다.", Emo.Anger, "\"신고하게요?\" 경계한다."),
                        Ev("store1b", EvKind.Npc, "야간 알바생", "새벽 4시, 정전이다. 알바생이 손전등을 켠다.", "▩?! …▧",
                            F(Emo.Surprise), Emo.Surprise, "\"깜짝이야!\" 손전등으로 너를 비춘다.", Emo.Anger, "\"장난치지 마요!\" 화를 낸다.") },
                    new[] {
                        Ev("store2a", EvKind.Npc, "야간 알바생", "\"손님이 유일한 단골이에요.\" 폐기 삼각김밥을 내민다.", "▨◇▣▣… ▦~",
                            F(Emo.Joy, Emo.Shy), Emo.Joy, "처음으로 환하게 웃는다.", Emo.Anger, "머쓱하게 김밥을 거둔다. 표정이 굳는다."),
                        Ev("store2b", EvKind.Npc, "야간 알바생", "정직원 면접에 붙었다며 영수증 뒷면에 쓴 합격 문자를 보여 준다.", "▩!! ▨◇▣!",
                            F(Emo.Joy, Emo.Surprise), Emo.Joy, "\"축하해 줘서 고마워요!\" 활짝 웃는다.", Emo.Anger, "\"…축하 안 해 줘요?\" 뾰로통해진다.") },
                },
                Passersby = new[] {
                    Ev("storeP1", EvKind.Passerby, "꼬마 손님", "동전을 세다가 모자라서 울상이다.", "◇◇… ▽?",
                        F(Emo.Sad, Emo.Surprise), Emo.Joy, "알바생이 몰래 깎아 주자 활짝 웃는다.", Emo.Anger, "\"뭘 봐요!\" 하고 휙 돌아선다."),
                    Ev("storeP2", EvKind.Passerby, "취한 아저씨", "아저씨가 계산대 앞에서 노래를 부른다.", "♩♩~ ▽▽~",
                        F(Emo.Surprise, Emo.Joy), Emo.Joy, "\"같이 불러요!\" 하고 껄껄 웃는다.", Emo.Anger, "\"뭐야, 기분 나쁘게.\""),
                },
                Special = Ev("storeS", EvKind.Special, "복권 손님", "새벽 3시, 1등 당첨 복권이 나왔다! 다들 소리를 지른다.", "▩!!! ▩!!!",
                    F(Emo.Joy, Emo.Surprise), Emo.Joy, "다 같이 만세를 불렀다!", Emo.Surprise, "모두 너를 이상하게 쳐다본다.", Hooray) },

            new() { Id = "cafe", Photo = "하트가 되다 만 라떼, 빨개진 볼", Icon = "♨", Name = "카페", Npc = "신입 바리스타", Mood = Emo.Shy,
                Story = new[] {
                    new[] {
                        Ev("cafe0a", EvKind.Npc, "신입 바리스타", "라떼아트 하트가 감자가 됐다. 얼굴이 빨갛다.", "▨◇▣▣▦~",
                            F(Emo.Shy, Emo.Joy), Emo.Surprise, "\"어? 귀엽대요?\" 눈이 휘둥그레진다.", Emo.Anger, "\"놀리지 마세요!\" 발끈한다."),
                        Ev("cafe0b", EvKind.Npc, "신입 바리스타", "처음 보는 너에게 시럽을 세 번이나 넣었다.", "▨!? ◇◇◇",
                            F(Emo.Shy, Emo.Surprise), Emo.Surprise, "\"헉, 죄송해요!\" 입을 틀어막는다.", Emo.Anger, "\"그냥 드세요!\" 볼이 부푼다.") },
                    new[] {
                        Ev("cafe1a", EvKind.Npc, "신입 바리스타", "대회 연습 중. 손이 떨려 우유를 쏟았다.", "▥◈… ◆▣▤!",
                            F(Emo.Surprise, Emo.Sad), Emo.Shy, "\"보셨어요…?\" 쑥스럽게 닦는다.", Emo.Anger, "\"웃지 마세요!\" 발끈한다."),
                        Ev("cafe1b", EvKind.Npc, "신입 바리스타", "연습한 하트를 너에게 먼저 보여 준다. 반쯤 하트다.", "▨♡? ◇▣",
                            F(Emo.Shy, Emo.Joy), Emo.Shy, "\"다음엔 완벽하게 할게요.\" 고개를 숙인다.", Emo.Surprise, "\"별로예요?\" 놀라 잔을 거둔다.") },
                    new[] {
                        Ev("cafe2a", EvKind.Npc, "신입 바리스타", "대회 전날. \"보러 와 줄래요?\"", "▥◈◆▣▤… ◁▤▨?",
                            F(Emo.Shy, Emo.Joy), Emo.Joy, "\"꼭이에요!\" 환하게 웃는다.", Emo.Anger, "\"바쁘시구나.\" 입술을 깨문다."),
                        Ev("cafe2b", EvKind.Npc, "신입 바리스타", "대회에서 은상을 탔다! 트로피를 들고 뛰어온다.", "▩!! ▨♡!!",
                            F(Emo.Joy, Emo.Surprise), Emo.Joy, "트로피를 흔들며 방방 뛴다.", Emo.Anger, "\"…기쁜 거 맞아요?\" 정색한다.") },
                },
                Passersby = new[] {
                    Ev("cafeP1", EvKind.Passerby, "첫 데이트 커플", "서로 눈을 못 마주치고 빨대만 문다.", "…♡ …♡",
                        F(Emo.Shy), Emo.Shy, "둘이 동시에 웃음이 터진다.", Emo.Surprise, "커플이 너를 보고 굳었다."),
                    Ev("cafeP2", EvKind.Passerby, "노트북 하는 학생", "마감 직전인지 키보드를 부술 듯 친다.", "▨▨▨!! ▩!",
                        F(Emo.Anger, Emo.Surprise), Emo.Anger, "\"교수님 진짜…!\" 같이 분노해 준다.", Emo.Surprise, "깜짝 놀라 저장을 안 누른다."),
                },
                Special = Ev("cafeS", EvKind.Special, "신입 바리스타", "바리스타가 라떼를 내민다. 이번엔 진짜 하트다.", "▨♡!",
                    F(Emo.Shy, Emo.Joy), Emo.Joy, "하트를 따라 그렸다!", Emo.Sad, "하트가 금세 무너졌다.", Heart) },
        };

        public static Place PlaceById(string id) => Places.First(p => p.Id == id);

        static Post P(string author, string c0, string c1, params Answer[] ans) =>
            new() { Author = author, Clues = new[] { c0, c1 }, Answers = ans };

        static Post Creepy(Post p) { p.WantsCreepy = true; return p; }
        static Post Or(Post p, Post bad) { p.Bad = bad; return p; }

        // 단골 셋의 이야기 글: 15일 동안 한 명당 5개 (소개 → 갈등 → 결말). 1일차는 튜토리얼용으로 지나가는 팬 글 하나를 함께 둔다.
        // 별빛러버 1·4·7·10·13일, 우울한고양이 1·3·6·9·12일, 의심하는자 2·5·8·11·14일
        public static readonly Post[][] Posts =
        {
            new[] { P(Starlover, "[사진] 케이크", "[시각] 오후 3시", Pure(Emo.Joy)),
                    P(Gloomcat, "[사진] 비 오는 창문", "[시각] 새벽 3시", Pure(Emo.Sad)),
                    P(null, "[사진] 꼬리 흔드는 강아지", "[반응] 웃음×20", Pure(Emo.Joy)) },
            new[] { P(Doubter, "[반응] 화남×8", "[시각] 밤 11시 50분", Pure(Emo.Anger)) },
            new[] { Or(P(Gloomcat, "[사진] 따뜻한 코코아", "[반응] 미소×3", Syn("괜찮은 척"), Pure(Emo.Sad)),
                       P(Gloomcat, "[사진] 텅 빈 의자", "[시각] 새벽 2시", Pure(Emo.Sad))) },
            new[] { P(Starlover, "[사진] 네 사진만 모은 폴더", "[반응] 헉×14", Pure(Emo.Surprise), Pure(Emo.Joy)) },
            new[] { P(Doubter, "[사진] 확대한 네 눈", "[반응] 의심×12", Pure(Emo.Surprise), Pure(Emo.Anger)) },
            new[] { Or(P(Gloomcat, "[사진] 다시 켠 화분 조명", "[시각] 오전 9시", Pure(Emo.Joy), Syn("괜찮은 척")),
                       P(Gloomcat, "[사진] 시든 꽃", "[시각] 새벽 4시", Pure(Emo.Sad))) },
            new[] { Creepy(P(Starlover, "[사진] 네 사진으로 도배된 방", "[반응] 소름×40", Syn("집착"))) },
            new[] { Or(P(Doubter, "[사진] 정전된 거리", "[반응] 헉×20", Pure(Emo.Surprise)),
                       P(Doubter, "[반응] 신고×5", "[시각] 자정", Pure(Emo.Anger))) },
            new[] { Or(P(Gloomcat, "[사진] 무릎 위 고양이", "[반응] 간질×6", Pure(Emo.Shy), Pure(Emo.Joy)),
                       P(Gloomcat, "[사진] 이삿짐 상자", "[시각] 새벽 1시", Pure(Emo.Sad))) },
            new[] { Creepy(P(Starlover, "[사진] 네 얼굴을 오린 가면", "[시각] 새벽 4시 44분", Syn("집착"))) },
            new[] { Or(P(Doubter, "[사진] 하늘의 이상한 빛", "[반응] 외계인×99", Pure(Emo.Surprise)),
                       P(Doubter, "[반응] 박제×50", "[시각] 새벽 5시", Pure(Emo.Anger))) },
            new[] { Or(P(Gloomcat, "[사진] 동네 공원 벤치", "[시각] 오후 1시", Pure(Emo.Joy), Syn("괜찮은 척")),
                       P(Gloomcat, "[사진] 닫힌 커튼", "[시각] 새벽 3시", Pure(Emo.Sad))) },
            new[] { P(Starlover, "[사진] 1년 전 첫 댓글 캡처", "[반응] 두근×31", Pure(Emo.Shy), Syn("두근")) },
            new[] { Or(P(Doubter, "[사진] 관찰 기록으로 가득한 메모장", "[시각] 밤 12시", Syn("삐짐"), Pure(Emo.Anger)),
                       P(Doubter, "[반응] 박제×80", "[시각] 새벽 5시", Pure(Emo.Anger))) },
            new Post[0],
        };

        // 예전에 날짜별로 넣었던 지나가는 팬 글: 이제 글 풀로 간다
        static readonly Post[] ExtraPool =
        {
            P(null, "[사진] 리본 달린 선물 상자", "[반응] 케이크×5", Pure(Emo.Joy)),
            P(null, "[사진] 손편지", "[반응] 미소×3", Pure(Emo.Shy)),
            P(null, "[사진] 금 간 폰 화면", "[반응] 빡침×9", Pure(Emo.Anger)),
            P(null, "[사진] 친구의 수상 소식", "[반응] 박수×30", Syn("질투")),
            P(null, "[사진] 반쯤 가린 셀카", "[반응] 두근×11", Syn("두근"), Pure(Emo.Shy)),
            P(null, "[사진] 구겨진 영수증", "[시각] 밤 10시", Pure(Emo.Anger)),
            P(null, "[사진] 약속에 늦은 친구 메시지", "[반응] 흥×7", Syn("삐짐")),
            P(null, "[사진] 커플 머그컵", "[반응] 하트×4", Pure(Emo.Shy)),
        };
        static readonly string[] ExtraPlains =
        {
            "선물 받았다 너무 신나", "손편지 받았는데 부끄러워..", "폰 액정 또 깨졌어 진짜 짜증나!!", "친구가 상 받았대 축하해… 축하해",
            "셀카 올렸는데 심장이 터질 것 같아", "영수증 보니 바가지였어!!", "약속에 또 늦었어 흥", "커플 머그컵 샀어 히히",
        };

        // 보류한 글은 다음 밤 서운함이 섞인 후속 글로 돌아온다
        public static Post Followup(Post held, int day) => new()
        {
            Id = $"f{day}", Author = held.Author,
            Text = Emojify("왜 어제 답 안 해 줬어…?", new[] { Pure(Emo.Anger) }, day),
            Clues = new[] { "[반응] 서운함", "[시각] 답장 기다린 새벽" },
            Answers = new[] { Pure(Emo.Anger), Syn("삐짐") },
            Plain = "왜 어제 답 안 해 줬어…?",
        };

        public static Post MisuseFollowup(Post original, string face, int day, int n)
        {
            var scene = original.Clues[0].Replace("[사진] ", "");
            var plain = $"{scene} 사진에 네 얼굴 붙였어! 이거 맞지?";
            return new Post
            {
                Id = $"m{day}_{n}", Author = original.Author, Plain = $"{scene} 사진에 네 얼굴 {face} 붙였어! 이거 맞지?",
                Text = face + " " + Emojify(plain, original.Answers, plain.Sum(c => c)),
                Clues = new[] { $"[사진] {scene}에 붙은 {face}", "[반응] 갸웃×7" },
                Answers = original.Answers, Misuse = face,
            };
        }

        static readonly Dictionary<string, string> Plains = new()
        {
            ["d1p0"] = "오늘 생일이라 케이크 먹었어!!", ["d1p1"] = "비가 와서 하루 종일 우울해...", ["d1p2"] = "우리 강아지가 꼬리를 흔들어 줬어",
            ["d2p0"] = "너 왜 매일 같은 시간에만 올려?",
            ["d3p0"] = "코코아 마시니까 조금 괜찮아졌어", ["d3p0b"] = "아무도 내 얘기 안 들어줘",
            ["d4p0"] = "네 사진 벌써 백 장 모았어?!",
            ["d5p0"] = "확대해 보니까 눈이 이상한데?",
            ["d6p0"] = "화분에 다시 불 켜 봤어", ["d6p0b"] = "꽃이 다 시들었어",
            ["d7p0"] = "방을 네 사진으로 다 채웠어!!!",
            ["d8p0"] = "정전된 날에도 글이 올라왔잖아?", ["d8p0b"] = "신고할 거야. 넌 사람이 아니야!!",
            ["d9p0"] = "고양이가 무릎에 올라왔어..", ["d9p0b"] = "이사 가기 싫어",
            ["d10p0"] = "네 얼굴 가면 만들었어 보여줄까",
            ["d11p0"] = "하늘에 이상한 빛이 떴어?!", ["d11p0b"] = "다 박제해 뒀어. 끝이야!!!",
            ["d12p0"] = "오늘 처음으로 밖에 나가 봤어 생각보다 괜찮아", ["d12p0b"] = "이제 아무것도 안 할래…",
            ["d13p0"] = "처음 팔로우한 날 기억해? 그때 첫 댓글 아직 캡처해 뒀어..!",
            ["d14p0"] = "내가 틀렸을지도 몰라. 그래도 계속 지켜볼 거야", ["d14p0b"] = "네 정체 다 알아냈어. 곧 터뜨린다!!!",
        };

        // ---------- 멘션 글 풀 ----------
        // 단골 셋의 이야기 글(Posts)을 뺀 나머지 자리는 이 풀에서 회차마다 무작위로 뽑는다 (다회차, 정식판 30일 대비).
        // 한 줄: 정답|실제 문장|단서1|단서2|색 단서(F 전부, P 일부, N 없음)
        // 정답 코드: J반짝 S축축 A따끔 U쿨 H간질, 시너지 T괜찮은척 G질투 D두근 P삐짐 O집착, 정답 둘은 J+U처럼
        // 깨진 글(■과 감정 기호)은 실제 문장에서 자동으로 만든다.
        const string PoolTable = @"
J|오늘 시험 끝났다 자유다!!|[사진] 찢어진 문제지|[시각] 오후 2시|F
J|첫 월급 받아서 엄마 선물 샀어|[사진] 꽃다발과 봉투|[반응] 박수×12|F
J|드디어 고양이 입양했어!!|[사진] 박스 속 아기 고양이|[반응] 하트×40|F
J|오늘 날씨 미쳤다 하늘 봐|[사진] 새파란 하늘|[시각] 오전 11시|P
J|떡볶이 무한리필 성공|[사진] 산더미 떡볶이|[반응] 웃음×9|F
J|버스 딱 맞춰 탔다 오늘 운 좋아|[사진] 버스 손잡이|[시각] 오전 8시|P
J|친구가 깜짝 생일파티 해 줬어|[사진] 고깔모자|[반응] 케이크×21|F
J|복권 오천 원 당첨ㅋㅋ|[사진] 긁은 복권|[반응] 웃음×15|P
J|헬스 백일 달성!!|[사진] 운동화|[반응] 박수×30|F
J|드라마 마지막 화 해피엔딩이야|[사진] 엔딩 크레딧|[시각] 밤 11시|P
J|첫눈 온다 첫눈!!|[사진] 하얀 창밖|[반응] 눈사람×50|F
J|강아지랑 바다 왔어|[사진] 모래사장 발자국|[반응] 하트×18|F
J|라면 물 딱 맞췄다|[사진] 완벽한 라면|[반응] 박수×4|F
J|오늘 처음으로 자전거 탔어!|[사진] 보조 바퀴 뗀 자전거|[반응] 박수×16|F
J|퇴근이다 퇴근!!|[사진] 사무실 불 끄는 손|[시각] 오후 6시|F
J|월급날이다 치킨 시킨다|[사진] 치킨 상자|[반응] 침×22|F
J|화분에 꽃이 처음 폈어|[사진] 작은 꽃봉오리|[반응] 축하×7|P
J|오랜만에 푹 잤다 개운해|[사진] 햇살 든 이불|[시각] 오전 10시|P
S|오늘도 혼자 밥 먹었어|[사진] 편의점 도시락 하나|[시각] 밤 9시|F
S|키우던 햄스터가 떠났어|[사진] 빈 쳇바퀴|[반응] 눈물×33|F|L
S|면접 또 떨어졌다|[사진] 구겨진 정장|[시각] 오후 6시|F|L
S|이사 가는 친구 배웅했어|[사진] 떠나는 트럭|[반응] 눈물×8|P
S|비 오는데 우산 잃어버렸어|[사진] 젖은 신발|[시각] 밤 10시|F
S|할머니 집 팔렸대|[사진] 오래된 대문|[반응] 눈물×12|P|L
S|아무도 내 생일 몰랐어|[사진] 초 하나 꽂은 빵|[시각] 자정|F|L
S|좋아하던 가게가 문 닫았어|[사진] 임대 문의 종이|[반응] 슬픔×20|P|L
S|폰 사진이 다 날아갔어|[사진] 텅 빈 앨범|[시각] 새벽 2시|N
S|연락이 끊긴 친구 꿈을 꿨어|[사진] 흐릿한 단체 사진|[시각] 새벽 5시|P
S|키링 줄이 끊어졌어|[사진] 떨어진 키링|[반응] 토닥×3|P
S|휴가 끝났다 내일 출근|[사진] 캐리어|[시각] 일요일 밤|F
S|동생이 군대 갔어|[사진] 빈 방 침대|[반응] 토닥×15|F|L
S|오늘따라 집이 너무 조용해|[사진] 꺼진 TV|[시각] 밤 12시|P
A|택배 또 분실됐어!!|[사진] 빈 현관|[반응] 화남×14|F|L
A|층간소음 새벽 세 시에 실화냐|[사진] 천장|[시각] 새벽 3시|F
A|지하철에서 발 밟고 사과도 안 해|[사진] 밟힌 운동화|[반응] 빡침×22|F
A|조별과제 나만 했어!!|[사진] 혼자 쓴 보고서|[반응] 분노×40|F|L
A|배달 음식 반이 쏟아져서 왔어|[사진] 엎어진 짜장면|[반응] 화남×9|F
A|게임 마지막 판에 렉 걸림|[사진] 멈춘 화면|[시각] 새벽 1시|P
A|새치기하고 모른 척하더라|[사진] 긴 줄|[반응] 빡침×17|F
A|와이파이 비번 누가 바꿨어!!|[사진] 연결 안 됨|[반응] 화남×6|P
A|우산 빌려 갔는데 안 돌려줌|[사진] 빈 우산꽂이|[반응] 분노×11|P
A|알람 끄고 다시 잠들었다 망했어|[사진] 지각 문자|[시각] 오전 9시 40분|N
A|에어컨 고장 났는데 수리 일주일 뒤래|[사진] 땀범벅 선풍기|[반응] 분노×19|F
A|친구가 스포했어!!!|[사진] 영화 티켓|[반응] 분노×28|F|L
A|주차 자리에 누가 짐을 쌓아 놨어|[사진] 막힌 주차칸|[반응] 화남×10|F
A|회의가 또 연장됐어|[사진] 꽉 찬 일정표|[시각] 오후 8시|P
U|길에서 연예인 봤어!!|[사진] 흔들린 뒷모습|[반응] 헉×60|F
U|자고 일어났더니 눈이 1미터?!|[사진] 문을 막은 눈|[반응] 헉×35|F
U|냉장고에서 작년 케이크 나옴|[사진] 곰팡이 상자|[반응] 소름×19|P
U|우리 집 고양이가 문을 열었어?!|[사진] 열린 문|[반응] 헉×27|F
U|시험 범위가 바뀌었대?!|[사진] 공지 캡처|[시각] 시험 전날 밤|P
U|하늘에 무지개가 두 개야|[사진] 쌍무지개|[반응] 와×44|F
U|십 년 전 일기장을 찾았어|[사진] 낡은 공책|[반응] 헉×10|N
U|엘리베이터에 갇혔었어!!|[사진] 비상벨|[반응] 헉×23|F|L
U|산 지 하루 만에 할인함|[사진] 가격표 두 장|[반응] 헉×15|P
U|우리 동네에 외계인 목격담이?!|[사진] 흐린 빛 사진|[반응] 외계인×88|F
U|유리컵이 혼자 깨졌어?!|[사진] 깨진 컵|[반응] 소름×12|P
U|택배 상자에 다른 사람 물건이?!|[사진] 모르는 인형|[반응] 헉×9|P
U|지갑에서 오만 원이 나왔어?!|[사진] 접힌 지폐|[반응] 헉×14|F
U|옆집이 우리 고모였대|[사진] 현관문 두 개|[반응] 헉×31|P
H|짝사랑한테 인사받았어..|[사진] 떨리는 손|[반응] 두근×12|F
H|발표 때 목소리 떨렸어|[사진] 발표 자료|[반응] 토닥×6|P|L
H|칭찬받았는데 뭐라 할지 몰랐어|[사진] 칭찬 스티커|[반응] 미소×9|F
H|넘어졌는데 다들 봤어..|[사진] 무릎 밴드|[반응] 웃음×13|P|L
H|첫 영상 올렸어 봐 줘..|[사진] 업로드 화면|[반응] 응원×20|F
H|노래방에서 고음 삑사리 났어|[사진] 마이크|[반응] 웃음×31|P
H|잘못 보낸 문자 읽음 표시 떴다|[사진] 메신저 화면|[시각] 밤 12시|F
H|머리 잘랐는데 너무 짧아..|[사진] 모자 쓴 셀카|[반응] 귀여워×24|F
H|모르는 사람한테 손 흔들었어|[사진] 손 흔드는 그림|[반응] 웃음×18|P
H|사진 찍힐 때 눈 감았어|[사진] 눈 감은 단체 사진|[반응] 귀여워×7|N
H|선생님을 엄마라고 불렀어|[사진] 교실|[반응] 웃음×44|F
H|처음으로 춤 영상 찍어 봤어|[사진] 거울 앞|[반응] 귀여워×21|P
H|좋아요 백 개 넘었어 고마워..|[사진] 알림 숫자|[반응] 축하×19|F
H|길에서 내 노래 흥얼거리다 들켰어|[사진] 이어폰|[반응] 웃음×11|P
T|괜찮아 원래 혼자가 편해|[사진] 혼자 켠 케이크|[시각] 밤 11시|P
T|떨어졌지만 다음엔 붙겠지 뭐|[사진] 불합격 메일|[반응] 토닥×14|P
T|헤어졌는데 생각보다 괜찮아ㅎㅎ|[사진] 지운 커플 사진|[시각] 새벽 3시|N
T|웃으면서 출근했어 웃으면서|[사진] 월요일 아침 지하철|[시각] 오전 7시|P
T|오늘도 무사히 버텼다 하하|[사진] 퇴근길 노을|[시각] 오후 7시|P
T|아픈데 그래도 학교 왔어 히히|[사진] 마스크와 체온계|[반응] 토닥×9|N
G|친구가 나보다 먼저 합격했대 축하해|[사진] 합격 인증|[반응] 박수×25|N
G|동생이 새 폰 샀대 좋겠다~|[사진] 언박싱|[반응] 부러워×11|P
G|옆자리 사람만 칭찬받음 ㅎㅎ|[사진] 상장|[반응] 박수×5|N
G|전 애인이 결혼한대 축하해~|[사진] 청첩장|[시각] 밤 2시|N
G|룸메가 또 택배 받았다 좋겠다|[사진] 쌓인 상자|[반응] 부러워×8|N
G|단짝이 새 친구랑 여행 갔대 좋겠네|[사진] 바다 사진|[반응] 부러워×13|N
D|고백받았어!!! 어떡해|[사진] 쪽지|[반응] 두근×70|P
D|내일 첫 데이트야 잠이 안 와|[사진] 옷 고르는 침대|[시각] 새벽 1시|P
D|좋아하는 사람이 내 글에 좋아요 눌렀어|[사진] 알림 화면|[반응] 꺄×33|N
D|무대 올라가기 오 분 전|[사진] 무대 뒤|[반응] 응원×40|P
D|그 사람이랑 우산 같이 썼어|[사진] 우산 하나|[반응] 두근×26|P
D|오디션 결과 발표 일 분 전|[사진] 새로고침 버튼|[시각] 오후 5시 59분|N
P|또 나만 빼고 놀러 갔대 흥|[사진] 단체 사진|[반응] 흥×9|N
P|생일인데 문자 하나 없냐|[사진] 조용한 폰|[시각] 밤 10시|P
P|기다렸는데 답장 없어 흥|[사진] 읽음 표시|[반응] 흥×12|N
P|내 푸딩 누가 먹었어|[사진] 빈 푸딩 컵|[반응] 흥×20|P
P|나만 단톡방 초대 안 됐어|[사진] 단톡방 목록|[반응] 흥×7|N
P|약속 시간 한 시간 지났는데 연락 없음|[사진] 시계|[시각] 오후 3시|P
O|너 사진 전부 저장했어 전부|[사진] 폴더 수백 개|[반응] 소름×30|N
O|매일 네 글 기다려 매일매일|[사진] 새로고침 화면|[시각] 새벽 4시|N
O|네가 간 카페 나도 가 봤어|[사진] 같은 자리 같은 컵|[반응] 소름×21|N
J+U|깜짝 선물이 문 앞에 있어!!|[사진] 리본 상자|[반응] 헉×20|P
S+T|괜찮은데 왜 눈물이 나지|[사진] 젖은 베개|[시각] 새벽 4시|P
A+U|내 자전거가 사라졌어?!|[사진] 끊긴 자물쇠|[반응] 헉×17|P
H+D|그 사람이 내 이름을 불렀어|[사진] 떨리는 메모|[반응] 두근×22|P
S+A|또 약속 취소래 진짜…|[사진] 취소 문자|[시각] 오후 7시|N
J+H|칭찬 스티커 백 개 모았다!|[사진] 스티커 판|[반응] 박수×13|F
U+J|길고양이가 내 무릎에 올라왔어|[사진] 무릎 위 고양이|[반응] 귀여워×45|P
A+P|동생이 내 옷 몰래 입고 나감|[사진] 텅 빈 옷장|[반응] 흥×15|P
S+H|발표 망쳐서 숨고 싶어|[사진] 이불 속|[시각] 밤 11시|P
G+J|친구 결혼식 다녀왔어 행복해 보이더라|[사진] 부케|[반응] 축하×30|N
J+T|이사 끝! 허리는 나갔지만 좋아|[사진] 쌓인 상자|[반응] 토닥×10|P
U+A|월세가 또 오른대?!|[사진] 고지서|[반응] 헉×24|P
S>J|시험 망친 날엔 □ 얼굴이었는데 떡볶이 먹고 □ 얼굴이 됐어|[사진] 빈 떡볶이 접시|[시각] 밤 9시|P
A>J|택배 늦어서 □ 얼굴이었는데 덤으로 사탕이 와서 □ 얼굴|[사진] 사탕 한 봉지|[반응] 웃음×8|P
J>S|아침엔 □ 얼굴로 나갔는데 소나기 맞고 □ 얼굴로 들어왔어|[사진] 젖은 우산|[시각] 오후 6시|P
U>H|고백받고 □ 얼굴이 됐다가 친구들이 봐서 □ 얼굴|[사진] 쪽지|[반응] 꺄×20|P
S>A|강아지 아파서 □ 얼굴이었는데 동생이 놀려서 □ 얼굴|[사진] 동물병원|[시각] 밤 10시|N
H>J|발표 전엔 □ 얼굴이었는데 박수받고 □ 얼굴|[사진] 발표 자료|[반응] 박수×15|P
A>U|새치기당해서 □ 얼굴이었는데 그게 아는 사람이라 □ 얼굴|[사진] 긴 줄|[반응] 헉×9|N
J>A|월급날이라 □ 얼굴이었는데 카드값 보고 □ 얼굴|[사진] 카드 명세서|[시각] 오후 7시|P
U>S|복권 맞은 줄 알고 □ 얼굴이었는데 지난주 거라 □ 얼굴|[사진] 복권|[반응] 토닥×12|N
S>H|혼자 영화 보러 가서 □ 얼굴이었는데 옆자리가 짝사랑이라 □ 얼굴|[사진] 영화표 두 장|[시각] 밤 8시|P
H>A|넘어져서 □ 얼굴이었는데 친구가 영상 찍어서 □ 얼굴|[사진] 무릎 밴드|[반응] 웃음×30|P
J>U|생일이라 □ 얼굴이었는데 문 열자 다들 숨어 있어서 □ 얼굴|[사진] 고깔모자|[반응] 헉×18|P
A>S|동생이랑 싸워서 □ 얼굴이었는데 동생이 울어서 □ 얼굴|[사진] 반쪽 난 과자|[시각] 오후 5시|N
U>J|정전돼서 □ 얼굴이었는데 별이 쏟아져서 □ 얼굴|[사진] 별 사진|[반응] 와×25|P
H>S|칭찬받고 □ 얼굴이었는데 그게 마지막 출근이라 □ 얼굴|[사진] 꽃다발|[시각] 오후 6시|N
S>U|휴가 끝나 □ 얼굴이었는데 비행기가 하루 밀려서 □ 얼굴|[사진] 결항 안내판|[반응] 헉×14|P
";

        // 팔로워가 늘어 다들 카오모지로만 말하기 시작하면, 그걸 이상하게 여기는 사람들의 글 (밤마다 하나씩 섞인다)
        const string UneasyTable = @"
U|요즘 다들 왜 말 대신 얼굴만 보내?|[사진] 카오모지로 가득한 단톡방|[반응] 헉×13|P
S|친구가 더 이상 글자로 대답을 안 해|[사진] 얼굴만 쌓인 메신저|[시각] 새벽 2시|P
A|회사 공지까지 카오모지로 왔어 이게 맞아?|[사진] 얼굴 한 줄짜리 공지|[반응] 화남×20|F
U+S|엄마가 내 이름 대신 얼굴만 보내|[사진] 엄마의 메시지|[시각] 밤 11시|N
S|나만 아직 글자를 쓰는 것 같아 무서워|[사진] 혼자 쓴 긴 글|[반응] 없음|N
A+U|이거 다 저 계정 때문 아니야?|[사진] 네 프로필 캡처|[반응] 의심×44|P
U|뉴스 앵커가 오늘 한마디도 안 했어|[사진] 웃기만 하는 앵커|[반응] 헉×50|P
S+T|괜찮아 나도 곧 따라 하게 되겠지|[사진] 얼굴로 가득한 연습장|[시각] 새벽 4시|N
A|글자를 쓰면 다들 이상하게 쳐다봐|[사진] 지하철 사람들의 같은 표정|[반응] 화남×9|P
U|길거리 간판이 전부 얼굴로 바뀌었어|[사진] 얼굴 간판|[반응] 헉×70|P
";

        public static readonly List<Post> Uneasy = new();

        public static readonly List<Post> Pool = new();

        static readonly Dictionary<char, Answer> Codes = new()
        {
            ['J'] = Pure(Emo.Joy), ['S'] = Pure(Emo.Sad), ['A'] = Pure(Emo.Anger), ['U'] = Pure(Emo.Surprise), ['H'] = Pure(Emo.Shy),
            ['T'] = Syn("괜찮은 척"), ['G'] = Syn("질투"), ['D'] = Syn("두근"), ['P'] = Syn("삐짐"), ['O'] = Syn("집착"),
        };

        static IEnumerable<Post> ParseTable(string table, string idPrefix)
        {
            int n = 0;
            foreach (var line in table.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var f = line.Split('|');
                // 빈칸 긴 글은 'S>J'처럼 첫 빈칸>둘째 빈칸
                var parts = f[0].Split('>');
                var answers = parts[0].Split('+').Select(c => Codes[c[0]]).ToArray();
                var answers2 = parts.Length > 1 ? parts[1].Split('+').Select(c => Codes[c[0]]).ToArray() : null;
                var post = P(null, f[2], f[3], answers);
                post.Answers2 = answers2;
                post.Literal = f.Length > 5 && f[5] == "L";
                post.Id = $"{idPrefix}{n++}";
                post.Plain = f[1];
                post.Text = Emojify(f[1], answers2 == null ? answers : answers.Concat(answers2).ToArray(), f[1].Sum(c => c));
                post.WantsCreepy = f[0] == "O"; // 집착 글은 섬뜩한 답장도 반긴다
                yield return post;
            }
        }

        static GameData()
        {
            for (int d = 0; d < Posts.Length; d++)
                for (int i = 0; i < Posts[d].Length; i++)
                {
                    Posts[d][i].Id = $"d{d + 1}p{i}";
                    Posts[d][i].Plain = Plains[Posts[d][i].Id];
                    Posts[d][i].Text = Emojify(Posts[d][i].Plain, Posts[d][i].Answers, d * 7 + i);
                    var bad = Posts[d][i].Bad;
                    if (bad != null) { bad.Id = $"d{d + 1}p{i}b"; bad.Plain = Plains[bad.Id]; bad.Text = Emojify(bad.Plain, bad.Answers, d * 7 + i + 1); }
                }

            // 풀: 직접 쓴 지나가는 팬 글 + 표에서 만든 글
            for (int i = 0; i < ExtraPool.Length; i++) { ExtraPool[i].Id = $"x{i}"; ExtraPool[i].Plain = ExtraPlains[i]; ExtraPool[i].Text = Emojify(ExtraPlains[i], ExtraPool[i].Answers, i); }
            Pool.AddRange(ExtraPool);
            Pool.AddRange(ParseTable(PoolTable, "r"));
            Uneasy.AddRange(ParseTable(UneasyTable, "u"));
            foreach (var p in Uneasy) p.Holdout = true;
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
