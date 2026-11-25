// 게임 규칙 (스펙 v2). Unity에 의존하지 않아 EditMode 테스트로 검증한다.
using System;
using System.Collections.Generic;
using System.Linq;
using static Kaomoji.GameData;

namespace Kaomoji
{
    public class ReplyState
    {
        public Result? R; // null이면 보류
        public string Face;
        public bool Held => R == null;
    }

    public class GameState
    {
        public int Day = 1, Followers, Points;
        public float Sus, Fat, FatMax = 100;
        public bool BrokeDown, JustBrokeDown; // JustBrokeDown: 화면 연출용, 보여준 쪽이 끈다
        public Slot Slot;
        public bool IsNight => Slot == Slot.Night;
        public bool Uploaded, HeldTonight;
        public string Went; // 오늘 마지막으로 간 장소 id, 없으면 방
        public Ending Ending = Ending.None;

        // 눈 여섯 개와 민짜 입으로 시작한다. 모으는 건 입 다섯 개와 장식뿐
        public readonly List<Emo> Eyes = new() { Emo.Neutral, Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };
        public readonly List<Emo> Mouths = new() { Emo.Neutral };
        public readonly List<Decor> Decors = new();
        public readonly Dictionary<string, int> Stickers = new(), Visits = new(), Aff = new();
        public readonly HashSet<string> Seen = new(), FoundSynergies = new(), Hinted = new(), DoneOnce = new();
        public readonly Dictionary<string, bool> RegularHappy = new(); // 단골이 지난 답장에 만족했는지
        public readonly Dictionary<Trait, int> Traits = new() { [Trait.Decode] = 0, [Trait.Mimic] = 0, [Trait.Empathy] = 0 };

        public List<Post> Tonight = new();
        public ReplyState[] Replies = Array.Empty<ReplyState>();
        Post heldPost;

        public Encounter Pending; // 외출해서 지금 마주한 이벤트
        public string PendingPlace { get; private set; }
        bool nightPointGiven;
        readonly Random rng;

        public GameState(int? seed = null)
        {
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
            StartDay();
        }

        // ---------- 판정 ----------

        // 정답마다 판정해서 가장 좋은 것을 쓴다. 섬뜩을 원하는 팬에겐 섬뜩도 완벽.
        public static Result Judge(Emo eye, Emo mouth, IEnumerable<Answer> answers, bool wantsCreepy = false)
        {
            var best = eye == Emo.Neutral ? Result.Meh : Result.Wrong;
            foreach (var a in answers)
            {
                if (eye != a.Eye) continue;
                var r = mouth == a.Mouth ? Result.Perfect : mouth == Emo.Neutral ? Result.Success : Result.Creepy;
                if (r == Result.Creepy && wantsCreepy) r = Result.Perfect;
                if (r < best) best = r;
            }
            return best;
        }

        public static Result Judge(Emo eye, Emo mouth, Emo ans) => Judge(eye, mouth, new[] { Pure(ans) });

        // ---------- 수치 ----------

        void AddSus(float n)
        {
            Sus = Math.Clamp(Sus + n, 0, 100);
            if (Sus >= 100 && Ending == Ending.None) Ending = Ending.Found;
        }

        void AddFat(float n)
        {
            Fat = Math.Clamp(Fat + n, 0, FatMax);
            if (Fat < FatMax || Ending != Ending.None) return;
            if (BrokeDown) { Ending = Ending.Collapse; return; }
            // 멘탈붕괴: 처음 100에 닿는 순간
            BrokeDown = JustBrokeDown = true;
            FatMax = Balance.FatMaxAfterBreakdown;
            Fat += Balance.BreakdownFat;
            AddSus(Traits[Trait.Mimic] >= 2 ? Balance.BreakdownSus / 2 : Balance.BreakdownSus);
        }

        // 행동을 마쳐도 결과 창을 닫을 때(EndAction)까지 시간은 흐르지 않는다
        public bool ActionPending { get; private set; }
        void Advance() => ActionPending = true;
        public bool CanAct => !IsNight && !ActionPending;

        public void EndAction()
        {
            if (!ActionPending) return;
            ActionPending = false;
            Slot++;
        }

        // ---------- 낮 ----------

        public Encounter PrepareOuting(string placeId)
        {
            var p = PlaceById(placeId);
            var extras = new[] { p.Passerby, p.Special }.Where(e => !DoneOnce.Contains(e.Id)).ToList();
            Pending = extras.Count > 0 && rng.NextDouble() < Balance.ExtraEventChance
                ? extras[rng.Next(extras.Count)]
                : p.Story[Math.Min(Aff.GetValueOrDefault(placeId), 2)];
            PendingPlace = placeId;
            return Pending;
        }

        // 만남: 상대 감정에 어울리는 눈이면 성공. 성공이든 실패든 상대의 반응 입을 얻는다
        public (bool ok, Emo? mouth, Decor decor) DoEncounter(Emo eye)
        {
            var ev = Pending;
            bool ok = ev.Fits.Contains(eye);
            Visits[PendingPlace] = Visits.GetValueOrDefault(PendingPlace) + 1;
            Went = PendingPlace;
            Emo? got = null;
            Decor decor = null;
            if (ok)
            {
                // 고정 NPC가 나오는 특수 이벤트(카페의 바리스타)도 호감이 오른다
                if (ev.Kind == EvKind.Npc || ev.Who == PlaceById(PendingPlace).Npc) Aff[PendingPlace] = Math.Min(3, Aff.GetValueOrDefault(PendingPlace) + 1);
                if (ev.Decor != null) { decor = ev.Decor; Decors.Add(decor); DoneOnce.Add(ev.Id); }
                else got = Gain(ev.WinMouth);
            }
            else
            {
                AddSus(Balance.DayFailSus);
                got = Gain(ev.FailMouth);
            }
            if (ev.Kind == EvKind.Passerby) DoneOnce.Add(ev.Id);
            Pending = null;
            AddFat(Traits[Trait.Mimic] >= 1 ? Balance.FatOutMimic : Balance.FatOut);
            Advance();
            return (ok, got, decor);
        }

        Emo? Gain(Emo m)
        {
            if (Mouths.Contains(m)) return null;
            Mouths.Add(m);
            return m;
        }

        // 거울 연습: 시너지 조합이면 이름을 수첩에 기록. isNew = 처음 찾음
        public (Answer syn, bool isNew) DoMirror(Emo eye, Emo mouth)
        {
            AddFat(Balance.FatHome);
            Advance();
            var syn = SynergyOf(eye, mouth);
            return (syn, syn != null && FoundSynergies.Add(syn.Name));
        }

        // 피드 구경: 오늘 밤 글 하나를 미리 보고 색 단서를 받는다
        public int DoPeek()
        {
            AddFat(Balance.FatHome);
            Advance();
            int i = Tonight.FindIndex(p => !Hinted.Contains(p.Id));
            if (i >= 0) Hinted.Add(Tonight[i].Id);
            return i;
        }

        public void DoNap()
        {
            AddFat(Balance.FatNap);
            AddSus(Balance.NapSus);
            Advance();
        }

        // ---------- 밤 ----------

        void StartDay()
        {
            Tonight = Posts[Day - 1].Select(p => p.Bad != null && p.Author != null && RegularHappy.TryGetValue(p.Author, out var ok) && !ok ? p.Bad : p).ToList();
            if (heldPost != null) Tonight.Add(Followup(heldPost, Day));
            heldPost = null;
            Replies = new ReplyState[Tonight.Count];
        }

        public bool NightDone => Uploaded && Replies.All(r => r != null);
        public bool Solved(string s) => Stickers.TryGetValue(s, out var n) && n >= (Traits[Trait.Decode] >= 2 ? 1 : 2);
        public int SolvedCount => Sym.Keys.Count(Solved);

        // 색 단서를 볼 수 있는지: 공감 1레벨이거나 피드 구경으로 미리 본 글
        public bool ShowsColor(Post p) => p.Hint != Hint.None && (Traits[Trait.Empathy] >= 1 || Hinted.Contains(p.Id));

        public (Result? r, int f) DoUpload(Emo eye, Emo mouth, Decor decor)
        {
            Uploaded = true;
            Result? r = null;
            int f = Balance.RoomUpload;
            float s = 0;
            if (Went != null)
            {
                r = Judge(eye, mouth, PlaceById(Went).Mood);
                (f, s) = Balance.Upload(r.Value);
            }
            if (decor != null) f += Balance.DecorBonus;
            Followers = Math.Max(0, Followers + f);
            AddSus(s);
            return (r, f);
        }

        // newly = 이번에 풀린 기호, newSyn = 처음 찾은 시너지
        public (Result r, int f, List<string> newly, Answer newSyn) DoReply(int idx, Emo eye, Emo mouth)
        {
            var post = Tonight[idx];
            var r = Judge(eye, mouth, post.Answers, post.WantsCreepy);
            var (f, s) = Balance.Reply(r);
            if (post.WantsCreepy && r == Result.Perfect) s = 0;
            if (r == Result.Creepy && Traits[Trait.Empathy] >= 2) s = 0;
            if (post.Author == Doubter)
            {
                if (r == Result.Wrong) s = Balance.DoubterWrongSus;
                if (r == Result.Perfect) s = Balance.DoubterPerfectSus;
            }
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
            var syn = SynergyOf(eye, mouth);
            Answer newSyn = syn != null && FoundSynergies.Add(syn.Name) ? syn : null;
            if (post.Author != null) RegularHappy[post.Author] = r is Result.Perfect or Result.Success;
            Replies[idx] = new ReplyState { R = r, Face = Face(eye, mouth) };
            return (r, f, newly, newSyn);
        }

        // 보류: 밤마다 한 글까지. 다음 밤 후속 글로 돌아온다
        public bool CanHold => !HeldTonight;
        public bool DoHold(int idx)
        {
            if (HeldTonight || Replies[idx] != null) return false;
            HeldTonight = true;
            heldPost = Tonight[idx];
            if (heldPost.Author != null) RegularHappy[heldPost.Author] = false;
            Replies[idx] = new ReplyState();
            return true;
        }

        public void MarkSeen()
        {
            foreach (var p in Tonight) foreach (var t in p.Text.Split(' ')) if (Sym.ContainsKey(t)) Seen.Add(t);
        }

        public bool CanRaise(Trait t) => Points > 0 && Traits[t] < 2;
        public void Raise(Trait t) { if (!CanRaise(t)) return; Points--; Traits[t]++; }

        // 답장을 끝내면 특성 창이 열리기 전에 포인트를 준다. 두 번 불러도 한 번만.
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
            AddFat(Balance.FatSleep);
            if (Ending != Ending.None) return;
            if (Day >= Balance.Days) { Ending = FinalEnding(); return; }
            Day++;
            Slot = Slot.Morning;
            Uploaded = HeldTonight = false;
            Went = null;
            StartDay();
        }

        public Ending FinalEnding()
        {
            if (Sus >= 100) return Ending.Found;
            if (Followers >= Balance.FollowerGoal && Sus < Balance.ConquerSusMax) return Ending.Conquer;
            if (SolvedCount >= Balance.AbsorbSolved && Followers < Balance.FollowerGoal) return Ending.Absorb;
            return Ending.Forgotten;
        }

        public IEnumerable<string> EpilogueCuts => Places.Where(p => Aff.GetValueOrDefault(p.Id) >= 3).Select(p => Epilogue[p.Id]);
    }
}
