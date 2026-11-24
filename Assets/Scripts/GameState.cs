// 게임 규칙. Unity에 의존하지 않아 EditMode 테스트로 검증한다.
using System;
using System.Collections.Generic;
using System.Linq;
using static Kaomoji.GameData;

namespace Kaomoji
{
    public class GameState
    {
        public int Day = 1, Followers, Points;
        public float Sus, Fat;
        public bool IsNight, Uploaded;
        public string Went; // 장소 id, "room", 또는 null
        public Ending Ending = Ending.None;
        public readonly List<Emo> Eyes = new() { Emo.Neutral, Emo.Joy };
        public readonly List<Emo> Mouths = new() { Emo.Neutral };
        public readonly Dictionary<string, int> Stickers = new(), Visits = new(), Aff = new();
        public readonly HashSet<string> Seen = new();
        public readonly Dictionary<Trait, int> Traits = new() { [Trait.Decode] = 0, [Trait.Mimic] = 0, [Trait.Empathy] = 0 };
        public readonly (Result r, string face)?[] Replied = new (Result, string)?[3];

        readonly Random rng;
        public GameState(int? seed = null) { rng = seed.HasValue ? new Random(seed.Value) : new Random(); }

        public static Result Judge(Emo eye, Emo mouth, Emo ans)
        {
            if (eye == ans) return mouth == ans ? Result.Perfect : mouth == Emo.Neutral ? Result.Success : Result.Creepy;
            return eye == Emo.Neutral ? Result.Meh : Result.Wrong;
        }

        public Post[] TodayPosts => Posts[Day - 1];
        public bool NightDone => Uploaded && Replied.All(r => r.HasValue);
        public bool Solved(string s) => Stickers.TryGetValue(s, out var n) && n >= (Traits[Trait.Decode] >= 2 ? 1 : 2);
        public int SolvedCount => Sym.Keys.Count(Solved);
        public Encounter NextEvent(string placeId) => PlaceById(placeId).Events[Math.Min(Visits.GetValueOrDefault(placeId), 1)];

        void AddSus(float n)
        {
            Sus = Math.Clamp(Sus + n, 0, 100);
            if (Sus >= 100) Ending = Ending.Found;
        }
        void AddFat(float n) => Fat = Math.Clamp(Fat + (n > 0 && Traits[Trait.Mimic] >= 2 ? n / 2 : n), 0, 100);

        // 낮: 만남 이벤트 하나
        public (Result r, Emo ans, List<string> msgs) DoEncounter(string placeId, Emo eye, Emo mouth)
        {
            var p = PlaceById(placeId);
            var ev = NextEvent(placeId);
            var msgs = new List<string>();
            if (Fat >= Balance.FatShaky) // 의태가 흔들림: 눈이나 입 하나가 무작위로 바뀜
            {
                if (rng.Next(2) == 0) eye = Eyes[rng.Next(Eyes.Count)];
                else mouth = Mouths[rng.Next(Mouths.Count)];
                msgs.Add($"의태가 흔들렸다! 얼굴이 {Face(eye, mouth)} 로 바뀌었다.");
            }
            var r = Judge(eye, mouth, ev.Answer);
            Visits[placeId] = Visits.GetValueOrDefault(placeId) + 1;
            Went = placeId;
            IsNight = true;
            AddFat(Balance.FatOut);

            if (r is Result.Perfect or Result.Success)
            {
                Aff[placeId] = Aff.GetValueOrDefault(placeId) + Balance.DayAff(r);
                var order = new[] { ev.Teach }.Concat(p.Mood.Where(m => m != ev.Teach));
                var missing = order.SelectMany(e => new[] { (eye: true, e), (eye: false, e) })
                    .Where(x => !(x.eye ? Eyes : Mouths).Contains(x.e))
                    .Take(Traits[Trait.Mimic] >= 1 ? 2 : 1).ToList();
                foreach (var (isEye, e) in missing)
                {
                    (isEye ? Eyes : Mouths).Add(e);
                    msgs.Add($"{p.Npc}의 얼굴을 의태했다 → {E[e].Name} {(isEye ? "눈 " + E[e].Eyes : "입 " + E[e].Mouth)} 획득");
                }
                if (missing.Count == 0) msgs.Add("더 배울 얼굴은 없었다.");
            }
            else if (r is Result.Creepy or Result.Wrong)
            {
                AddSus(Balance.DaySus(r));
                msgs.Add(r == Result.Creepy ? "상대가 움찔한다. 주변 사람들이 수군댄다." : "상대가 어색하게 웃는다. 누군가 너를 빤히 본다.");
            }
            else msgs.Add("상대가 고개를 갸웃한다.");
            return (r, ev.Answer, msgs);
        }

        public void DoRest()
        {
            AddFat(Balance.RestFat);
            AddSus(Balance.RestSus);
            Went = "room";
            IsNight = true;
        }

        // 밤: 업로드. 방 사진이면 r == null
        public (Result? r, int f) DoUpload(Emo eye, Emo mouth)
        {
            Uploaded = true;
            if (Went == null || Went == "room") { Followers += Balance.RoomUpload; return (null, Balance.RoomUpload); }
            var r = PlaceById(Went).Mood.Select(m => Judge(eye, mouth, m)).Min(); // enum 순서상 작을수록 좋은 판정
            var (f, s) = Balance.Upload(r);
            Followers = Math.Max(0, Followers + f);
            AddSus(s);
            return (r, f);
        }

        // 밤: 팬 글 답장. newly = 이번에 풀린 기호
        public (Result r, int f, List<string> newly) DoReply(int idx, Emo eye, Emo mouth)
        {
            var post = TodayPosts[idx];
            var r = Judge(eye, mouth, post.Emotion);
            var (f, s) = Balance.Reply(r);
            if (r == Result.Creepy && Traits[Trait.Empathy] >= 2) s = 0;
            Followers = Math.Max(0, Followers + f);
            AddSus(s);
            AddFat(Balance.FatReply);
            var newly = new List<string>();
            if (r is Result.Perfect or Result.Success)
                foreach (var t in post.Text.Split(' ').Where(Sym.ContainsKey))
                {
                    var was = Solved(t);
                    Stickers[t] = Stickers.GetValueOrDefault(t) + 1;
                    if (!was && Solved(t)) newly.Add(t);
                }
            Replied[idx] = (r, Face(eye, mouth));
            return (r, f, newly);
        }

        public void MarkSeen()
        {
            foreach (var p in TodayPosts) foreach (var t in p.Text.Split(' ')) if (Sym.ContainsKey(t)) Seen.Add(t);
        }

        public bool CanRaise(Trait t) => Points > 0 && Traits[t] < 2;
        public void Raise(Trait t) { if (!CanRaise(t)) return; Points--; Traits[t]++; }

        // 답장을 끝내면 특성 창이 열리기 전에 포인트를 준다. 두 번 불러도 한 번만.
        bool nightPointGiven;
        public void GrantNightPoint()
        {
            if (nightPointGiven) return;
            Points++;
            nightPointGiven = true;
        }

        public void EndNight()
        {
            GrantNightPoint();
            nightPointGiven = false;
            if (Day >= Balance.Days) { Ending = FinalEnding(); return; }
            Day++;
            IsNight = false; Uploaded = false; Went = null;
            Array.Clear(Replied, 0, Replied.Length);
        }

        public Ending FinalEnding()
        {
            if (Sus >= 100) return Ending.Found;
            if (Followers >= Balance.FollowerGoal && Sus < Balance.ConquerSusMax) return Ending.Conquer;
            if (SolvedCount >= Sym.Count * Balance.AbsorbRatio && Followers < Balance.FollowerGoal) return Ending.Absorb;
            return Ending.Normal;
        }
    }
}
