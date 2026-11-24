// 카오모지 게임 프로토타입 데이터. Unity에 의존하지 않는 순수 C#.
// 수치는 모두 가안. Balance에서 조정한다.
using System.Collections.Generic;
using System.Linq;

namespace Kaomoji
{
    public enum Emo { Joy, Sad, Anger, Surprise, Shy, Neutral }
    public enum Result { Perfect, Success, Creepy, Meh, Wrong }
    public enum Ending { None, Found, Conquer, Absorb, Normal }
    public enum Trait { Decode, Mimic, Empathy }

    public static class Balance
    {
        public const int Days = 7;
        public const int FollowerGoal = 500; // shortcut: 스펙상 미정, 플레이테스트 후 조정
        public const int FatOut = 20, FatReply = 5, FatShaky = 80;
        public const int RestFat = -40, RestSus = -15;
        public const int RoomUpload = 5;
        public const int ConquerSusMax = 70;
        public const float AbsorbRatio = 0.8f;

        public static int DaySus(Result r) => r == Result.Creepy ? 8 : r == Result.Wrong ? 10 : 0;
        public static int DayAff(Result r) => r == Result.Perfect ? 2 : r == Result.Success ? 1 : 0;

        // (팔로워, 의심도)
        public static (int f, int s) Reply(Result r) => r switch
        {
            Result.Perfect => (40, 0), Result.Success => (25, 0), Result.Creepy => (60, 12),
            Result.Meh => (0, 0), _ => (-15, 10),
        };
        public static (int f, int s) Upload(Result r) => r switch
        {
            Result.Perfect => (30, 0), Result.Success => (20, 0), Result.Creepy => (40, 8),
            Result.Meh => (5, 0), _ => (-10, 5),
        };
    }

    public class EmoInfo
    {
        public string Name, EyeL, EyeR, Mouth, Color;
        public EmoInfo(string n, string l, string r, string m, string c) { Name = n; EyeL = l; EyeR = r; Mouth = m; Color = c; }
        public string Eyes => EyeL + " " + EyeR;
    }

    public class Encounter
    {
        public Emo Answer, Teach; // Answer = 반응 정답, Teach = 의태해서 배우는 상대 얼굴
        public string Say, Situation;
        public Encounter(Emo a, Emo t, string say, string sit) { Answer = a; Teach = t; Say = say; Situation = sit; }
    }

    public class Place
    {
        public string Id, Name, Npc;
        public Emo[] Mood;
        public Encounter[] Events;
    }

    public class Post
    {
        public Emo Emotion;
        public string Text;    // 공백으로 나뉜 토큰. 기호는 단독 토큰
        public string[] Clues; // [0] 기본, [1] 해독 1레벨부터
        public Post(Emo e, string t, string c0, string c1) { Emotion = e; Text = t; Clues = new[] { c0, c1 }; }
    }

    public static class GameData
    {
        public static readonly Emo[] Feelings = { Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };

        public static readonly Dictionary<Emo, EmoInfo> E = new()
        {
            [Emo.Joy] = new("기쁨", "^", "^", "▽", "#ffd23f"),
            [Emo.Sad] = new("슬픔", "T", "T", "﹏", "#5aa9ff"),
            [Emo.Anger] = new("분노", "`", "´", "益", "#ff5a5a"),
            [Emo.Surprise] = new("놀람", "゚", "゚", "Д", "#b07cff"),
            [Emo.Shy] = new("부끄러움", ">", "<", "ω", "#ff8fc8"),
            [Emo.Neutral] = new("중립", "・", "・", "_", "#c8c8d0"),
        };

        public static string Face(Emo eye, Emo mouth) => $"({E[eye].EyeL}{E[mouth].Mouth}{E[eye].EyeR})";

        public static string ResultName(Result r) => r switch
        {
            Result.Perfect => "완벽", Result.Success => "성공", Result.Creepy => "섬뜩", Result.Meh => "시큰둥", _ => "오답",
        };

        // 감정 기호 사전: 감정마다 4개
        public static readonly Dictionary<string, Emo> Sym = new()
        {
            ["☆"] = Emo.Joy, ["♪"] = Emo.Joy, ["✿"] = Emo.Joy, ["◎"] = Emo.Joy,
            ["☂"] = Emo.Sad, ["◇"] = Emo.Sad, ["∴"] = Emo.Sad, ["≈"] = Emo.Sad,
            ["※"] = Emo.Anger, ["▲"] = Emo.Anger, ["♯"] = Emo.Anger, ["✕"] = Emo.Anger,
            ["◉"] = Emo.Surprise, ["⁂"] = Emo.Surprise, ["↯"] = Emo.Surprise, ["⊙"] = Emo.Surprise,
            ["♡"] = Emo.Shy, ["∽"] = Emo.Shy, ["◐"] = Emo.Shy, ["❀"] = Emo.Shy,
        };

        // 첫 만남은 인사(기쁨)로 반응하고 상대 얼굴을 의태해 그 장소의 감정을 배운다.
        // 두 번째부터는 그 장소의 감정이 정답. (시작 파츠만으로 막히지 않게)
        public static readonly Place[] Places =
        {
            new() { Id = "park", Name = "공원", Npc = "펫시터", Mood = new[] { Emo.Joy }, Events = new[] {
                new Encounter(Emo.Joy, Emo.Joy, "◁◆▽ ▣◈!", "강아지 세 마리가 펫시터 주위를 빙글빙글 돈다. 펫시터가 활짝 웃으며 손을 흔든다."),
                new Encounter(Emo.Joy, Emo.Joy, "◆▽◁ ◁◆▽?", "펫시터가 리드줄 하나를 건넨다. 같이 걷자는 것 같다. 강아지가 네 신발을 핥는다.") } },
            new() { Id = "station", Name = "지하철역", Npc = "분실물센터 역무원", Mood = new[] { Emo.Sad }, Events = new[] {
                new Encounter(Emo.Joy, Emo.Sad, "◇▤◈ ▽◆…", "처음 보는 역무원이 곰인형을 상자에 넣다 말고 고개를 든다. 눈가가 젖어 있다. 일단 인사를 해야 한다."),
                new Encounter(Emo.Sad, Emo.Sad, "◇▤◈… ▥◈▣", "곰인형의 주인은 끝내 오지 않았다. 역무원이 인형을 \"보관 만료\" 상자로 옮긴다.") } },
            new() { Id = "store", Name = "편의점", Npc = "야간 알바생", Mood = new[] { Emo.Anger, Emo.Surprise }, Events = new[] {
                new Encounter(Emo.Joy, Emo.Anger, "▨◁▣!! ◇▦▤!", "진상 손님이 문을 쾅 닫고 나간다. 씩씩대던 알바생이 너를 보고 표정을 고친다. 손님으로서 인사할 차례."),
                new Encounter(Emo.Anger, Emo.Surprise, "▩▣◇!! ◆▧◆!", "그 손님이 또 왔다. 알바생이 \"같이 화 좀 내줘요\" 하는 눈빛이다. 그때 손님 주머니에서 개구리가 튀어나온다.") } },
            new() { Id = "cafe", Name = "카페", Npc = "신입 바리스타", Mood = new[] { Emo.Shy }, Events = new[] {
                new Encounter(Emo.Joy, Emo.Shy, "▨◇▣▣▦~", "신입 바리스타가 라떼아트를 망쳤다. 하트가 감자가 됐다. 얼굴이 빨개진 채 잔을 내민다. 일단 웃어주자."),
                new Encounter(Emo.Shy, Emo.Shy, "▥◈◆▣▤… ◁▤▨", "하트가 처음으로 성공했다. 바리스타가 잔을 내밀며 눈을 못 마주친다.") } },
        };

        public static Place PlaceById(string id) => Places.First(p => p.Id == id);

        // 팬 글 21개: 하루 3개
        public static readonly Post[][] Posts =
        {
            new[] { new Post(Emo.Joy, "■■ ☆ ■■■ !!!", "[사진] 케이크 사진", "[시각] 오후 3시"),
                    new Post(Emo.Sad, "■■■ ☂ ■■ ...", "[사진] 비 오는 창문 사진", "[시각] 새벽 3시 12분"),
                    new Post(Emo.Joy, "♪ ■■■■ ✿ ■", "[사진] 꼬리 흔드는 강아지 사진", "[반응] 웃음×20") },
            new[] { new Post(Emo.Anger, "■■ ※ ■■■■ !!", "[반응] 화남×8", "[시각] 밤 11시 50분"),
                    new Post(Emo.Joy, "■ ☆ ■■ ◎ ■■", "[사진] 리본 달린 선물 상자", "[반응] 케이크×5"),
                    new Post(Emo.Sad, "◇ ■■■ ☂ ■■", "[사진] 텅 빈 의자 사진", "[시각] 새벽 2시") },
            new[] { new Post(Emo.Surprise, "◉ ■■ ?!?!", "[사진] 흔들린 번개 사진", "[반응] 헉×14"),
                    new Post(Emo.Shy, "■■ ♡ ■■■ ..", "[사진] 손편지 사진", "[반응] 미소×3"),
                    new Post(Emo.Anger, "▲ ■■■ ※ ■!!", "[사진] 금 간 폰 화면", "[반응] 빡침×9") },
            new[] { new Post(Emo.Joy, "■■■ ♪ ■ ~~", "[사진] 놀이공원 사진", "[시각] 토요일 오후"),
                    new Post(Emo.Sad, "∴ ■■ ≈ ■■■", "[사진] 시든 꽃 사진", "[시각] 새벽 4시"),
                    new Post(Emo.Surprise, "■ ⁂ ■■ ◉ !?", "[사진] 복권 번호 사진", "[반응] 비명×30") },
            new[] { new Post(Emo.Shy, "∽ ■■■ ♡ ■", "[사진] 얼굴을 반쯤 가린 셀카", "[반응] 가림×11"),
                    new Post(Emo.Anger, "■■ ♯ ■ ▲ ■■!!", "[사진] 구겨진 영수증", "[시각] 밤 10시"),
                    new Post(Emo.Joy, "✿ ■■ ◎ ■■■", "[사진] 꽃다발 사진", "[반응] 꽃×7") },
            new[] { new Post(Emo.Surprise, "↯ ■■■ ⊙ ?!", "[사진] 정전된 거리 사진", "[시각] 밤 9시 정각"),
                    new Post(Emo.Shy, "■ ◐ ■■ ∽ ..", "[사진] 고백 쪽지 사진", "[반응] 화끈×6"),
                    new Post(Emo.Sad, "≈ ■■■ ◇ ■", "[사진] 이삿짐 상자 사진", "[시각] 새벽 1시") },
            new[] { new Post(Emo.Anger, "✕ ■■ ♯ ■■!!!", "[사진] 새치기 줄 사진", "[반응] 욕설×15"),
                    new Post(Emo.Shy, "❀ ■■■ ◐ ■", "[사진] 커플 머그컵 사진", "[반응] 하트×4"),
                    new Post(Emo.Surprise, "■ ⊙ ■■ ↯ ?!?", "[사진] 하늘의 이상한 빛 사진", "[반응] 외계인×99") },
        };

        public static readonly Dictionary<Ending, (string title, string text)> Endings = new()
        {
            [Ending.Found] = ("발각 엔딩", "의태가 풀린 사진 한 장이 퍼졌다. 촉수가, 눈이 일곱 개가. 피드가 난리가 났다."),
            [Ending.Conquer] = ("정복 엔딩", "어느 날부터 팬들이 글 대신 카오모지만으로 대화한다. (^▽^) (^▽^) (^▽^). 행성이 너의 말을 배웠다."),
            [Ending.Absorb] = ("물듦 엔딩", "너는 행성의 글자로 긴 글을 쓴다. 다 쓰고 나니, 원래 어디서 왔는지 잘 기억나지 않는다."),
            [Ending.Normal] = ("평범 엔딩", "그럭저럭 알려진 계정으로 남았다. 알림이 하나 온다. 모행성: \"보고서 언제 보냄?\""),
        };
    }
}
