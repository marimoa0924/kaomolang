// 게임 규칙 (스펙 v5). Unity에 의존하지 않아 EditMode 테스트로 검증한다.
using System;
using System.Collections.Generic;
using System.Linq;
using static Kaomoji.GameData;

namespace Kaomoji
{
    public class ReplyState
    {
        public Result? R;  // null이면 보류
        public Result? R2; // 빈칸 긴 글의 두 번째 빈칸
        public string Face, Face2;
        public bool Held => R == null;
    }

    // 답장 조립 규칙: 서운한 단골에게는 입 후보가 따끔·쿨만 (친밀도, 미룸)
    public class ComposeRule
    {
        public List<Emo> Mouths; // null이면 제한 없음
    }

    // 시스템마다 스위치와 처음 열리는 날 (Unlocks.csv). 미룬 것은 코드를 지우지 않고 꺼 둔다
    public enum Feature
    {
        Rest, Typing, Misuse,                                                                 // v5
        Notices, PlaceAbility, Intimacy, DecorMix, HiddenMood, Maintenance, Rewind, BlankPosts, // 미룸
    }

    public class GameState
    {
        public int Day = 1, Followers, Perfects;
        public float Sus, Fat, FatMax = 100;
        public bool BrokeDown, JustBrokeDown; // JustBrokeDown: 화면 연출용, 보여준 쪽이 끈다
        public string NoiseMsg;               // 피로 70을 넘은 순간의 메시지. 화면이 보여 주고 비운다
        public Slot Slot;
        public bool IsNight => Slot == Slot.Night;
        public bool Uploaded, HeldTonight;
        public string Went; // 오늘 마지막으로 간 장소 id, 없으면 방
        public Ending Ending = Ending.None;
        public int Act => Balance.Act(Day);

        // 눈 여섯 개와 민짜 입으로 시작한다. 모으는 건 입 다섯 개와 장식뿐
        public List<Emo> Eyes = new() { Emo.Neutral, Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy };
        public List<Emo> Mouths = new() { Emo.Neutral };
        public List<Decor> Decors = new();
        public Dictionary<string, int> Visits = new(), Aff = new(), AffWins = new(), Intimacy = new(), IntimacyWins = new();
        public HashSet<string> FoundSynergies = new(), DoneOnce = new(), MisusedFaces = new();
        HashSet<string> sulky = new(), sulkyTonight = new();
        public Dictionary<string, bool> RegularHappy = new(); // 단골이 지난 답장에 만족했는지
        public List<(string place, int lv)> NewAbilities = new(); // 화면에서 도장으로 보여 주고 비운다

        public Notice Today; // 아침 유행 공지 (미룸)
        public bool NoticesEnabled { get => On(Feature.Notices); set { if (value) Off.Remove(Feature.Notices); else Off.Add(Feature.Notices); } }

        public HashSet<Feature> Off = new();
        public Dictionary<Feature, Unlock> Unlocks = new(UnlockTable); // 판마다 복사해 둬야 테스트에서 따로 바꿀 수 있다
        public bool On(Feature f) => !Off.Contains(f) && Unlocks.TryGetValue(f, out var u) && u.On && Day >= u.Day;
        public int LastDay = Balance.Days;

        public bool Maintenance; // 셧다운 다음 날 정비 (미룸)
        bool maintenanceNext;
        public bool PendingHidden; // 호감 2단계부터 NPC가 기분을 숨긴다 (미룸)
        HashSet<NoticeKind> usedNotices = new();
        int practiceUntil, lastNoiseDay = -99, noiseNext;

        // 얼굴 기록: 따라 하기의 날, 얼굴 품귀, 거리 말풍선
        public string LastFace, YesterdayLastFace, YesterdayPostFace, MostUsedYesterday;
        Dictionary<string, int> faceUseToday = new();
        string postFaceToday;

        public List<Post> Tonight = new();
        public ReplyState[] Replies = Array.Empty<ReplyState>();
        Post heldPost;
        List<(Post post, string face)> literalPending = new();
        readonly List<Post> poolOrder;
        HashSet<string> usedPool = new();
        int poolNext, uneasyNext;
        bool susRose;

        public Encounter Pending; // 외출해서 지금 마주한 이벤트
        public string PendingPlace { get; private set; }
        readonly Random rng;

        public GameState(int? seed = null)
        {
            rng = seed.HasValue ? new Random(seed.Value) : new Random();
            poolOrder = Pool.OrderBy(_ => rng.Next()).ToList(); // 다회차: 판마다 다른 글
            StartDay();
        }

        // 날마다 저장해 두고 되돌아가기 위한 복사본
        public GameState Clone()
        {
            var c = (GameState)MemberwiseClone();
            c.Eyes = new(Eyes); c.Mouths = new(Mouths); c.Decors = new(Decors);
            c.Visits = new(Visits); c.Aff = new(Aff); c.AffWins = new(AffWins); c.Intimacy = new(Intimacy); c.IntimacyWins = new(IntimacyWins);
            c.FoundSynergies = new(FoundSynergies); c.DoneOnce = new(DoneOnce); c.MisusedFaces = new(MisusedFaces);
            c.sulky = new(sulky); c.sulkyTonight = new(sulkyTonight); c.RegularHappy = new(RegularHappy); c.NewAbilities = new(NewAbilities);
            c.usedNotices = new(usedNotices); c.faceUseToday = new(faceUseToday); c.Off = new(Off); c.Unlocks = new(Unlocks);
            c.Tonight = new(Tonight); c.Replies = (ReplyState[])Replies.Clone(); c.literalPending = new(literalPending); c.usedPool = new(usedPool);
            return c;
        }

        // ---------- 판정 ----------

        // 정답마다 판정해서 가장 좋은 것을 쓴다. 섬뜩을 원하는 팬에겐 섬뜩도 완벽.
        // 눈이 틀려도 정답과 같은 분위기 묶음이면 아쉬운 오답 (분위기 보고 찍었는데 틀리면 억울하니까)
        public static Result Judge(Emo eye, Emo mouth, IEnumerable<Answer> answers, bool wantsCreepy = false)
        {
            var best = Result.Wrong;
            foreach (var a in answers)
            {
                var r = eye != a.Eye ? (MoodOf(eye) == MoodOf(a.Eye) ? Result.Near : Result.Wrong)
                    : mouth == a.Mouth ? Result.Perfect : mouth == Emo.Neutral ? Result.Success : Result.Creepy;
                if (r == Result.Creepy && wantsCreepy) r = Result.Perfect;
                if (r < best) best = r;
            }
            return best;
        }

        public static Result Judge(Emo eye, Emo mouth, Emo ans) => Judge(eye, mouth, new[] { Pure(ans) });

        // ---------- 수치 ----------

        void AddSus(float n)
        {
            if (n > 0)
            {
                susRose = true;
                if (Maintenance || Day <= practiceUntil) n /= 2; // 얼굴 연습 뒤 사흘
            }
            Sus = Math.Clamp(Sus + n, 0, 100);
            if (Sus >= 100 && Ending == Ending.None) Ending = Ending.Found;
        }

        void AddFat(float n)
        {
            float before = Fat;
            Fat = Math.Clamp(Fat + n, 0, FatMax);
            // 70을 넘는 순간 노이즈와 메시지. 매번이면 짜증 나니까 사흘에 한 번, 매번 다음 내용
            if (before < Balance.NoiseFrom && Fat >= Balance.NoiseFrom && Day - lastNoiseDay >= Balance.NoiseGap)
            {
                NoiseMsg = NoiseMessages[noiseNext++ % NoiseMessages.Length];
                lastNoiseDay = Day;
            }
            if (Fat < FatMax || Ending != Ending.None) return;
            if (BrokeDown) { Ending = Ending.Collapse; return; }
            // 멘탈붕괴 셧다운: 의심이 크게 오르고 피로가 크게 내려가며 한계가 120이 된다
            BrokeDown = JustBrokeDown = maintenanceNext = true;
            FatMax = Balance.FatMaxAfterBreakdown;
            Fat += Balance.BreakdownFat;
            AddSus(Balance.BreakdownSus);
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

        bool Is(NoticeKind k) => Today?.Kind == k;

        // 팬 글에서 읽히는 한글 수: 하루 한두 글자로 시작해 팔로워와 6일차 성장으로 는다
        public int Letters(int hangul) =>
            Math.Min(hangul, 1 + (Day >= Balance.GrowLetters ? 1 : 0) + (int)(hangul * Math.Clamp((float)Followers / Balance.FollowerGoal, 0, 1)));

        // ---------- 장소 호감 (능력은 미룸) ----------

        public int AffLevel(string place) => Aff.GetValueOrDefault(place);
        public bool HasAbility(string place, int lv) => On(Feature.PlaceAbility) && AffLevel(place) >= lv;

        void WinAt(string place)
        {
            int lv = AffLevel(place);
            if (lv >= 3) return;
            AffWins[place] = AffWins.GetValueOrDefault(place) + 1;
            if (AffWins[place] < Balance.AffNeed[lv]) return;
            AffWins[place] = 0;
            Aff[place] = lv + 1;
            if (On(Feature.PlaceAbility) && Abilities.ContainsKey((place, lv + 1))) NewAbilities.Add((place, lv + 1));
        }

        // ---------- 낮 ----------

        public bool NoExtras; // 튜토리얼 동안은 행인·특수 이벤트 없이 고정 NPC만

        public Encounter PrepareOuting(string placeId)
        {
            var p = PlaceById(placeId);
            var extras = p.Passersby.Append(p.Special).Where(e => !DoneOnce.Contains(e.Id)).ToList();
            Pending = !NoExtras && extras.Count > 0 && rng.NextDouble() < Balance.ExtraEventChance
                ? extras[rng.Next(extras.Count)]
                : p.Story[Math.Min(AffLevel(placeId), 2)][rng.Next(2)]; // 같은 단계라도 장면은 두 가지 중 하나
            PendingPlace = placeId;
            PendingHidden = On(Feature.HiddenMood) && Pending.Kind == EvKind.Npc && AffLevel(placeId) >= 2 && rng.NextDouble() < 0.5;
            return Pending;
        }

        // 3일차 성장 '의태 숙련'부터 외출 피로 15 → 10
        public float OutingFat => (Day >= Balance.GrowOuting ? Balance.FatOutSkilled : Balance.FatOut)
            - (HasAbility("park", 2) ? 3 : 0) + (Is(NoticeKind.StayIn) ? 5 : 0);

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
                WinAt(PendingPlace); // 어떤 이벤트든 성공하면 호감이 오른다. 특수 이벤트는 장식까지
                if (ev.Decor != null) { decor = ev.Decor; Decors.Add(decor); DoneOnce.Add(ev.Id); }
                else got = Gain(ev.WinMouth);
            }
            else
            {
                AddSus(Balance.DayFailSus * (HasAbility("station", 3) ? 0.5f : 1));
                got = Gain(ev.FailMouth);
            }
            if (ev.Kind == EvKind.Passerby) DoneOnce.Add(ev.Id);
            Pending = null;
            AddFat(OutingFat);
            Advance();
            return (ok, got, decor);
        }

        Emo? Gain(Emo m)
        {
            if (Mouths.Contains(m)) return null;
            Mouths.Add(m);
            return m;
        }

        // 휴식 셋: 고민할 건 숫자뿐. 9일차 성장 '표정 근육'부터 피로가 5씩 더 내려간다
        public bool Practicing => Day <= practiceUntil; // 얼굴 연습 뒤 사흘: 오르는 의심 절반
        public static float RestFat(Rest r) => r == Rest.Full ? Balance.RestFull : r == Rest.Feed ? Balance.RestFeed : Balance.RestPractice;
        public float RestFatNow(Rest r) => RestFat(r) - (Day >= Balance.GrowRest ? Balance.RestGrowth : 0);

        public void DoRest(Rest r)
        {
            AddFat(RestFatNow(r));
            if (r == Rest.Full) AddSus(Balance.RestFullSus - (HasAbility("store", 2) ? 5 : 0));
            if (r == Rest.Feed) Followers += Balance.FeedFollowers + (HasAbility("station", 2) ? 100 : 0);
            if (r == Rest.Practice) practiceUntil = Day + Balance.PracticeDays - 1;
            Advance();
        }

        // 장식 조합 (미룸)
        public bool CanCombine => On(Feature.DecorMix) && Decors.Count >= 2;
        public Decor CombineDecors(Decor a, Decor b)
        {
            if (!CanCombine || a == b || !Decors.Contains(a) || !Decors.Contains(b)) return null;
            Decors.Remove(a); Decors.Remove(b);
            var mix = new Decor($"{a.Id}+{b.Id}", $"{a.Name}+{b.Name}", a.L, b.R);
            Decors.Add(mix);
            Advance();
            return mix;
        }

        // ---------- 하루의 시작 ----------

        // 막마다 글 난이도: 1막 감정 하나뿐인 쉬운 글, 3막 정답 둘·섬뜩을 반기는 글을 먼저
        bool FitsAct(Post p) => Act == 1 ? p.Answers.Length == 1 && !p.Answers[0].IsSynergy && !p.WantsCreepy && !p.IsBlank
            : Act == 3 ? p.Answers.Length > 1 || p.WantsCreepy : true;

        Post Next(Func<Post, bool> want) =>
            NextPool(p => want(p) && FitsAct(p) && (On(Feature.BlankPosts) || !p.IsBlank)) ?? NextPool(p => On(Feature.BlankPosts) || !p.IsBlank);

        void StartDay()
        {
            susRose = false;
            Maintenance = maintenanceNext && On(Feature.Maintenance); maintenanceNext = false;
            sulkyTonight = sulky; sulky = new();
            PickNotice();
            if (Is(NoticeKind.QuietNight)) AddFat(-5);
            if (Is(NoticeKind.AllNighter)) AddFat(10);
            if (Is(NoticeKind.HoldoutSilence)) AddSus(-5);
            if (IntimacyOf(Doubter) >= 3) AddSus(-5);

            // 단골의 이야기 글(그날 몫) + 나머지는 회차마다 섞인 풀에서 차례로
            var regulars = Day <= Posts.Length ? Posts[Day - 1].Where(p => p.Author != null) : Enumerable.Empty<Post>();
            Tonight = regulars.Select(p => p.Bad != null && RegularHappy.TryGetValue(p.Author, out var ok) && !ok ? p.Bad : p).ToList();
            // 학습 지도: 4일차엔 두 감정이 섞인 글, 5일차엔 분위기는 같고 감정이 다른 글 (간질은 반짝과 헷갈린다)
            if (Day == 4 && NextPool(p => !p.IsBlank && p.Answers.Any(a => a.IsSynergy)) is { } syn) Tonight.Add(syn);
            if (Day == 5 && NextPool(p => !p.IsBlank && p.Answers.All(a => a.Eye == Emo.Shy)) is { } near) Tonight.Add(near);
            int slots = Is(NoticeKind.QuietNight) ? Balance.PostsPerNight - 1 : Balance.PostsPerNight;
            while (Tonight.Count < slots && Next(p => true) is { } next) Tonight.Add(next);
            if (Is(NoticeKind.DoubleFace) && On(Feature.BlankPosts) && !Tonight.Any(p => p.IsBlank) && NextPool(p => p.IsBlank) is { } blank)
            {
                int slot = Tonight.FindLastIndex(p => p.Author == null);
                if (slot >= 0) Tonight[slot] = blank; else Tonight.Add(blank);
            }
            // 2막의 반어: 가라앉은 글에 웃는 이모지 (글자가 더 보여도 더 헷갈리게)
            if (Act == 2)
                for (int k = 0; k < Tonight.Count; k++)
                {
                    var p = Tonight[k];
                    if (p.Author != null || p.Holdout || p.Id.Sum(c => c) % 2 != 0 || !p.Answers.All(a => MoodOf(a.Eye) == 1)) continue;
                    var c = p.Clone();
                    c.Text = Emojify(p.Plain, p.Answers, p.Id.Sum(ch => ch), ironic: true);
                    Tonight[k] = c;
                }
            // 팔로워가 늘어 다들 카오모지로만 말하기 시작하면, 그걸 이상하게 여기는 글이 밤마다 하나씩 끼어든다
            int holdouts = Is(NoticeKind.HoldoutSilence) ? 0 : Is(NoticeKind.HoldoutCampaign) ? 2 : 1;
            for (int h = 0; h < holdouts && Followers >= Balance.FollowerGoal * Balance.UneasyFrom && uneasyNext < Uneasy.Count; h++)
            {
                int slot = Tonight.FindLastIndex(p => p.Author == null && !p.Holdout);
                if (slot >= 0) Tonight[slot] = Uneasy[uneasyNext++];
                else Tonight.Add(Uneasy[uneasyNext++]);
            }
            if (heldPost != null) Tonight.Add(Followup(heldPost, Day));
            heldPost = null;
            // 오역 후속 글: 어제 틀린 글의 팬이 내 얼굴을 엉뚱한 상황에 쓴다
            for (int k = 0; k < literalPending.Count; k++) Tonight.Add(MisuseFollowup(literalPending[k].post, literalPending[k].face, Day, k));
            literalPending.Clear();
            Replies = new ReplyState[Tonight.Count];
        }

        Post NextPool(Func<Post, bool> ok)
        {
            for (int i = poolNext; i < poolOrder.Count; i++)
            {
                var p = poolOrder[i];
                if (usedPool.Contains(p.Id) || !ok(p)) continue;
                usedPool.Add(p.Id);
                if (i == poolNext) poolNext++;
                return p;
            }
            return null;
        }

        void PickNotice()
        {
            Today = null;
            if (!NoticesEnabled || Day < 2 || Maintenance) return;
            if (Day >= LastDay) { Today = FinalNotice; return; }
            if (Day == LastDay - 1 && LastDay == Balance.Days) { Today = Notices.First(n => n.Kind == NoticeKind.MimicDay); return; }
            bool half = Followers >= Balance.FollowerGoal * Balance.UneasyFrom;
            var ok = Notices.Where(n => n.Kind != NoticeKind.MimicDay && n.MinAct <= Act && Act <= n.MaxAct && !usedNotices.Contains(n.Kind) && (!n.NeedHalf || half) && (n.Kind != NoticeKind.DoubleFace || On(Feature.BlankPosts))).ToList();
            if (ok.Count == 0) return;
            Today = ok[rng.Next(ok.Count)];
            usedNotices.Add(Today.Kind);
        }

        // 튜토리얼이 아닐 때: 1일차 글도 전부 풀에서 무작위로 (아직 아무 글에도 답하지 않았을 때만)
        public void RerollFirstDay()
        {
            if (Day != 1 || Replies.Any(r => r != null)) return;
            Tonight = new List<Post>();
            while (Tonight.Count < Balance.PostsPerNight && Next(p => true) is { } next) Tonight.Add(next);
            Replies = new ReplyState[Tonight.Count];
        }

        // ---------- 단골 친밀도 (미룸) ----------

        public int IntimacyOf(string author) => author == null || !On(Feature.Intimacy) ? 0 : Intimacy.GetValueOrDefault(author);
        public bool SulkyTonight(string author) => author != null && sulkyTonight.Contains(author);

        void Bond(string author, bool good)
        {
            if (author == null || !Regulars.Contains(author) || !On(Feature.Intimacy)) return;
            int lv = IntimacyOf(author);
            if (good)
            {
                if (lv >= 3) return;
                IntimacyWins[author] = IntimacyWins.GetValueOrDefault(author) + 1;
                if (IntimacyWins[author] >= Balance.IntimacyNeed[lv]) { Intimacy[author] = lv + 1; IntimacyWins[author] = 0; }
                return;
            }
            if (lv > 0) Intimacy[author] = lv - 1;
            IntimacyWins[author] = 0;
            sulky.Add(author);
        }

        public ComposeRule RuleFor(Post post) =>
            SulkyTonight(post.Author) && On(Feature.Intimacy) && !post.IsBlank ? new ComposeRule { Mouths = new() { Emo.Anger, Emo.Surprise } } : new ComposeRule();

        // ---------- 밤 ----------

        public bool NightDone => Uploaded && Replies.All(r => r != null);

        public (Result? r, int f) DoUpload(Emo eye, Emo mouth, Decor decor)
        {
            Uploaded = true;
            Result? r = null;
            float f = Balance.RoomUpload, s = 0;
            if (Went != null)
            {
                var mood = PlaceById(Went).Mood;
                r = Judge(eye, mouth, mood);
                (f, s) = Balance.Upload(r.Value);
                if (f > 0 && Is(NoticeKind.HeartDay) && mood == Emo.Shy) f *= 1.5f;
                if (f > 0 && HasAbility("store", 3)) f *= 1.2f;
                if (r == Result.Near && (Day >= Balance.GrowNear || HasAbility("cafe", 3))) f = 0;
            }
            if (decor != null) f += Balance.DecorBonus * (Is(NoticeKind.DecorBoom) ? 2 : 1);
            if (Is(NoticeKind.StarBan) && (eye == Emo.Joy || mouth == Emo.Joy)) s += 5;
            Followers = Math.Max(0, Followers + (int)f);
            AddSus(s);
            postFaceToday = Face(eye, mouth, decor);
            UseFace(Face(eye, mouth));
            return (r, (int)f);
        }

        void UseFace(string face)
        {
            faceUseToday[face] = faceUseToday.GetValueOrDefault(face) + 1;
            LastFace = face;
        }

        // 하나의 빈칸(또는 보통 글)을 판정하고 오늘의 규칙을 적용한다
        (Result r, float f, float s) Score(Post post, Answer[] answers, Emo eye, Emo mouth)
        {
            var r = Judge(eye, mouth, answers, post.WantsCreepy);
            if (Is(NoticeKind.NeutralWeek) && r == Result.Near) r = Result.Success;
            if (Is(NoticeKind.MimicDay) && YesterdayLastFace != null && FaceEye(YesterdayLastFace) == eye && r is Result.Success or Result.Creepy) r = Result.Perfect;
            var (bf, bs) = Balance.Reply(r);
            float f = bf, s = bs;
            if (post.WantsCreepy && r == Result.Perfect) s = 0;
            if (r == Result.Near && (Day >= Balance.GrowNear || HasAbility("cafe", 3))) f = 0; // 12일차 성장 '연기 몰입', 카페 3단계
            if (f > 0)
            {
                if (Is(NoticeKind.HeartDay) && answers.Any(a => a.Eye == Emo.Shy || a.Mouth == Emo.Shy)) f *= 1.5f;
                if (Is(NoticeKind.SameEyeMouth) && eye == mouth) f *= 1.1f;
                if (Is(NoticeKind.FaceShortage) && Face(eye, mouth) == MostUsedYesterday) f *= 0.7f;
                if (Is(NoticeKind.AllNighter)) f *= 1.3f;
                if (HasAbility("cafe", 2)) f *= 1.1f;
            }
            if (Is(NoticeKind.StarBan) && (eye == Emo.Joy || mouth == Emo.Joy)) s += 5;
            return (r, f, s);
        }

        static Emo? FaceEye(string face) => Feelings.Append(Emo.Neutral).Cast<Emo?>().FirstOrDefault(e => face.Contains($"({E[e.Value].EyeL}") && face.Contains($"{E[e.Value].EyeR})"));

        // newSyn = 처음 찾은 시너지
        public (Result r, int f, Answer newSyn) DoReply(int idx, Emo eye, Emo mouth)
        {
            var post = Tonight[idx];
            var (r, f, s) = Score(post, post.Answers, eye, mouth);
            if (post.Holdout) s += Balance.HoldoutSus * (IntimacyOf(Doubter) >= 3 ? 0.5f : 1); // 아직 글자를 쓰는 정상인에게 답하면 의심이 크게 더 오른다
            if (post.Author == Doubter)
            {
                if (r == Result.Wrong) s = Balance.DoubterWrongSus;
                if (r == Result.Perfect) s = Balance.DoubterPerfectSus;
            }
            if (post.Misuse != null)
            {
                if (r is Result.Perfect or Result.Success) s -= 5; // 오해가 풀렸다
                else MisusedFaces.Add(post.Misuse);                 // 그 얼굴이 계속 엉뚱하게 쓰인다 (거리에도)
            }
            if (post.Literal && On(Feature.Misuse) && r is Result.Wrong or Result.Creepy) literalPending.Add((post, Face(eye, mouth)));
            Finish(post, r, f, s);
            var syn = SynergyOf(eye, mouth);
            Answer newSyn = syn != null && r <= Result.Creepy && FoundSynergies.Add(syn.Name) ? syn : null;
            Replies[idx] = new ReplyState { R = r, Face = Face(eye, mouth) };
            UseFace(Face(eye, mouth));
            return (r, (int)f, newSyn);
        }

        // 빈칸 긴 글 (미룸): 빈칸마다 따로 판정, 둘 다 성공 이상이면 완벽 하나를 더
        public (Result r1, Result r2, int f) DoReplyBlank(int idx, Emo eye1, Emo mouth1, Emo eye2, Emo mouth2)
        {
            var post = Tonight[idx];
            var (r1, f1, s1) = Score(post, post.Answers, eye1, mouth1);
            var (r2, f2, s2) = Score(post, post.Answers2, eye2, mouth2);
            float f = f1 + f2 + (r1 <= Result.Success && r2 <= Result.Success ? Balance.Reply(Result.Perfect).f : 0);
            Finish(post, r1 < r2 ? r2 : r1, f, s1 + s2);
            Replies[idx] = new ReplyState { R = r1, R2 = r2, Face = Face(eye1, mouth1), Face2 = Face(eye2, mouth2) };
            UseFace(Face(eye2, mouth2));
            return (r1, r2, (int)f);
        }

        void Finish(Post post, Result r, float f, float s)
        {
            if (r == Result.Perfect) Perfects++;
            Followers = Math.Max(0, Followers + (int)f);
            AddSus(s);
            AddFat(Balance.FatReply);
            if (post.Author == null) return;
            RegularHappy[post.Author] = r is Result.Perfect or Result.Success;
            if (r is Result.Perfect or Result.Success) Bond(post.Author, true);
            else if (r == Result.Wrong) Bond(post.Author, false);
        }

        // 보류: 밤마다 한 글까지. 다음 밤 후속 글로 돌아온다
        public bool CanHold => !HeldTonight;
        public bool DoHold(int idx)
        {
            if (HeldTonight || Replies[idx] != null) return false;
            HeldTonight = true;
            heldPost = Tonight[idx];
            if (heldPost.Author != null) { RegularHappy[heldPost.Author] = false; Bond(heldPost.Author, false); }
            Replies[idx] = new ReplyState();
            return true;
        }

        void NextDay()
        {
            if (!susRose) AddSus(-Balance.SusDecay); // 의심이 오르지 않은 날은 조금 가라앉는다
            YesterdayLastFace = LastFace;
            YesterdayPostFace = postFaceToday;
            postFaceToday = null;
            MostUsedYesterday = faceUseToday.Count > 0 ? faceUseToday.OrderByDescending(x => x.Value).First().Key : null;
            faceUseToday.Clear();
            if (Day >= LastDay) { Ending = FinalEnding(); return; }
            Day++;
            Slot = Slot.Morning;
            Uploaded = HeldTonight = false;
            Went = null;
            StartDay();
        }

        public void EndNight()
        {
            if (Ending != Ending.None) return;
            AddFat(Balance.FatSleep - (HasAbility("park", 3) ? 5 : 0));
            NextDay();
        }

        // 셧다운: 그날은 그대로 끝나고 다음 날로 (남은 행동·답장은 사라진다)
        public void ForceEndDay()
        {
            ActionPending = false;
            Pending = null;
            if (Ending != Ending.None) return;
            NextDay();
        }

        public Ending FinalEnding()
        {
            if (Sus >= 100) return Ending.Found;
            if (Followers >= Balance.FollowerGoal && Sus < Balance.ConquerSusMax) return Ending.Conquer;
            if (Perfects >= Balance.AbsorbPerfect) return Ending.Absorb;
            return Ending.Forgotten;
        }

        public IEnumerable<string> EpilogueCuts => Places.Where(p => AffLevel(p.Id) >= 3).Select(p => Epilogue[p.Id]);
    }
}
