// 게임 규칙 (스펙 v4). Unity에 의존하지 않아 EditMode 테스트로 검증한다.
using System;
using System.Collections.Generic;
using System.Linq;
using static Kaomoji.GameData;

namespace Kaomoji
{
    public enum Hit { Exact, Elsewhere, None } // 칸별 반응 (Wordle): 정답 그대로 / 정답에 있지만 다른 칸 / 정답에 없음

    public class ReplyState
    {
        public Result? R;  // null이면 보류
        public Result? R2; // 빈칸 긴 글의 두 번째 빈칸
        public string Face, Face2;
        public Hit EyeHit, MouthHit;
        public bool Held => R == null;
    }

    // 답장 조립 규칙: 서운한 단골에게는 입 후보가 따끔·쿨만 (Florence의 각진 조각)
    public class ComposeRule
    {
        public List<Emo> Mouths; // null이면 제한 없음
    }

    // P2 기능은 설정에서 하나씩 끄고 켠다: 시스템을 한꺼번에 검증하면 무엇이 재미를 만들었는지 모른다
    public enum Feature { Intimacy, PlaceAbility, Notices, Street, BlankPosts, Misuse }

    public class GameState
    {
        public int Day = 1, Followers, Points;
        public float Sus, Fat, FatMax = 100;
        public bool BrokeDown, JustBrokeDown, JustAwakened; // Just*: 화면 연출용, 보여준 쪽이 끈다
        public int AwakenDay = -1;
        public double? AwakenChanceOverride; // 테스트용
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
        public Dictionary<string, int> Stickers = new(), Visits = new(), Aff = new(), AffWins = new(), Intimacy = new(), IntimacyWins = new();
        public HashSet<string> Seen = new(), FoundSynergies = new(), Hinted = new(), DoneOnce = new(), Confirmed = new(), MisusedFaces = new();
        HashSet<string> sulky = new(), sulkyTonight = new();
        public Dictionary<string, Emo> Pencil = new(); // 연필 추측: 맞는지는 알려 주지 않는다
        public List<string> Scraps = new();            // 거리 말풍선에서 스크랩한 문장
        public int PageLockedUntil;                    // 묶음 확정에 틀리면 다음 날 밤까지 잠긴다
        public Dictionary<string, bool> RegularHappy = new(); // 단골이 지난 답장에 만족했는지
        public Dictionary<Trait, int> Traits = new() { [Trait.Decode] = 0, [Trait.Mimic] = 0, [Trait.Empathy] = 0 };
        public List<(string place, int lv)> NewAbilities = new(); // 화면에서 도장으로 보여 주고 비운다

        // 아침 유행 공지
        public Notice Today;
        public bool NoticesEnabled { get => On(Feature.Notices); set { if (value) Off.Remove(Feature.Notices); else Off.Add(Feature.Notices); } }

        // 테스트 설정: 1차는 P1만 켜고 5일, 2차는 P2까지 켜고 15일
        public HashSet<Feature> Off = new();
        public bool On(Feature f) => !Off.Contains(f);
        public int LastDay = Balance.Days;

        // 랜덤 판정은 날짜별 시드로 고정: 되돌아가도 같은 조건이면 같은 결과
        readonly int runSeed;
        int wobbleRolls, dangerPerfects;
        double DayRoll(int salt) => new Random(unchecked(runSeed * 397 + Day * 7919 + salt)).NextDouble();
        public bool Maintenance; // 셧다운 다음 날: 공지 대신 정비 카드, 의심 증가 절반
        bool maintenanceNext;
        public bool PendingHidden; // 호감 2단계부터: NPC가 기분을 숨겨 말 대신 장면으로 읽는다
        public Dictionary<string, HashSet<string>> Habits = new(); // 단골의 말버릇 기호
        HashSet<NoticeKind> usedNotices = new();

        // 얼굴 기록: 따라 하기의 날, 얼굴 품귀, 거리 말풍선
        public string LastFace, YesterdayLastFace, YesterdayPostFace, MostUsedYesterday;
        Dictionary<string, int> faceUseToday = new();

        public List<Post> Tonight = new();
        public ReplyState[] Replies = Array.Empty<ReplyState>();
        Post heldPost;
        List<(Post post, string face)> literalPending = new();
        readonly List<Post> poolOrder;
        HashSet<string> usedPool = new();
        int poolNext, uneasyNext;
        bool susRose, gotTentacle, callHint, sleptPledge;
        float snackFat, snackFatToday;
        public string CallHintSymbol; // 고향 통신이 연필로 적어 준 기호 (그날 아침 화면용)

        public Encounter Pending; // 외출해서 지금 마주한 이벤트
        public string PendingPlace { get; private set; }
        bool nightPointGiven;
        readonly Random rng;

        public GameState(int? seed = null)
        {
            runSeed = seed ?? Environment.TickCount;
            rng = new Random(runSeed);
            poolOrder = Pool.OrderBy(_ => rng.Next()).ToList(); // 다회차: 판마다 다른 글
            StartDay();
        }

        // 날마다 저장해 두고 되돌아가기 위한 복사본
        public GameState Clone()
        {
            var c = (GameState)MemberwiseClone();
            c.Eyes = new(Eyes); c.Mouths = new(Mouths); c.Decors = new(Decors);
            c.Stickers = new(Stickers); c.Visits = new(Visits); c.Aff = new(Aff); c.AffWins = new(AffWins);
            c.Intimacy = new(Intimacy); c.IntimacyWins = new(IntimacyWins);
            c.Seen = new(Seen); c.FoundSynergies = new(FoundSynergies); c.Hinted = new(Hinted); c.DoneOnce = new(DoneOnce);
            c.Confirmed = new(Confirmed); c.MisusedFaces = new(MisusedFaces); c.sulky = new(sulky); c.sulkyTonight = new(sulkyTonight);
            c.Pencil = new(Pencil); c.Scraps = new(Scraps); c.RegularHappy = new(RegularHappy); c.Traits = new(Traits);
            c.NewAbilities = new(NewAbilities); c.usedNotices = new(usedNotices); c.faceUseToday = new(faceUseToday);
            c.Tonight = new(Tonight); c.Replies = (ReplyState[])Replies.Clone(); c.literalPending = new(literalPending); c.usedPool = new(usedPool);
            c.Off = new(Off); c.Habits = Habits.ToDictionary(x => x.Key, x => new HashSet<string>(x.Value));
            return c;
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

        // 칸별 반응: 이 칸의 감정이 그 칸의 정답이면 Exact, 정답의 다른 칸에 있으면 Elsewhere, 없으면 None
        public static (Hit eye, Hit mouth) Hits(Emo eye, Emo mouth, IEnumerable<Answer> answers)
        {
            var a = answers.ToList();
            Hit M(Emo e, Func<Answer, Emo> same, Func<Answer, Emo> other) =>
                a.Any(x => same(x) == e) ? Hit.Exact : a.Any(x => other(x) == e) ? Hit.Elsewhere : Hit.None;
            return (M(eye, x => x.Eye, x => x.Mouth), M(mouth, x => x.Mouth, x => x.Eye));
        }

        // ---------- 수치 ----------

        void AddSus(float n)
        {
            if (n > 0) { susRose = true; if (Maintenance) n /= 2; }
            Sus = Math.Clamp(Sus + n, 0, 100);
            if (Sus >= 100 && Ending == Ending.None) Ending = Ending.Found;
        }

        public bool Leaking => Fat >= Balance.LeakFrom;   // 새어남: 지친 사진
        public bool InDanger => Fat >= Balance.DangerFrom; // 위험: 지친 사진 확대, 게시물 의심, 입력 흔들림

        void AddFat(float n)
        {
            Fat = Math.Clamp(Fat + n, 0, FatMax);
            if (InDanger && !gotTentacle) { gotTentacle = true; Decors.Add(Tentacle); } // 위험 구간 첫 진입: 본모습 장식 해금
            if (Fat < FatMax || Ending != Ending.None) return;
            if (BrokeDown) { Ending = Ending.Collapse; return; }
            // 100 판정 (Darkest Dungeon의 결의 판정): 한 판에 한 번. 대개 셧다운, 가끔 각성
            BrokeDown = true;
            FatMax = Balance.FatMaxAfterBreakdown;
            if (DayRoll(1) < AwakenChance)
            {
                JustAwakened = true; // 각성: 그날은 끝나지 않고, 피로 70, 그날 밤 성공이 완벽으로
                AwakenDay = Day;
                Fat = Balance.AwakenFat;
                return;
            }
            JustBrokeDown = true;
            maintenanceNext = true;
            Fat += Balance.BreakdownFat;
            AddSus(Traits[Trait.Mimic] >= 2 ? Balance.BreakdownSus / 2 : Balance.BreakdownSus);
        }

        // 각성 확률: 기본 20% + 위험 구간 완벽 답장마다 5%(최대 20%) + 공원 3단계 + 잠 다짐
        public double AwakenChance => AwakenChanceOverride ?? Balance.AwakenChance + Math.Min(dangerPerfects, 4) * 0.05
            + (HasAbility("park", 3) ? Balance.AwakenPark : 0) + (sleptPledge ? Balance.AwakenSlept : 0);

        // 입력 흔들림: 위험 구간에서 파츠를 고를 때마다 굴린다. 편의점 3단계면 확률 절반
        public double WobbleChance => Balance.InputWobble * (HasAbility("store", 3) ? 0.5 : 1);
        public bool RollWobble() => InDanger && DayRoll(100 + wobbleRolls++) < WobbleChance;

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

        // ---------- 장소 호감 단계와 능력 (Persona 5) ----------

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
            PendingHidden = Pending.Kind == EvKind.Npc && AffLevel(placeId) >= 2 && rng.NextDouble() < 0.5;
            return Pending;
        }

        public float OutingFat => (Traits[Trait.Mimic] >= 1 ? Balance.FatOutMimic : Balance.FatOut)
            - (HasAbility("park", 2) ? 3 : 0) + snackFatToday + (Is(NoticeKind.StayIn) ? 5 : 0);

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
                // 어떤 이벤트든 성공하면 그 장소의 호감이 오른다 (15일용: 1→2는 2번, 2→3은 4번). 특수 이벤트는 장식까지
                WinAt(PendingPlace);
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

        // 거울 연습: 시너지 조합이면 이름을 수첩에 기록. isNew = 처음 찾음
        public (Answer syn, bool isNew) DoMirror(Emo eye, Emo mouth)
        {
            AddFat(Balance.FatHome);
            Advance();
            var syn = SynergyOf(eye, mouth);
            return (syn, syn != null && FoundSynergies.Add(syn.Name));
        }

        // 장식 조합 (8일차부터, Strange Horticulture의 조제): 장식 둘을 섞어 새 장식 하나
        public bool CanCombine => Day >= Balance.DecorComboDay && Decors.Count >= 2;
        public Decor CombineDecors(Decor a, Decor b)
        {
            if (!CanCombine || a == b || !Decors.Contains(a) || !Decors.Contains(b)) return null;
            Decors.Remove(a); Decors.Remove(b);
            var mix = new Decor($"{a.Id}+{b.Id}", $"{a.Name}+{b.Name}", a.L, b.R);
            Decors.Add(mix);
            AddFat(Balance.FatHome);
            Advance();
            return mix;
        }

        // 피드 구경: 오늘 밤 글 하나(카페 2단계면 둘)를 미리 보고 색 단서를 받는다
        public List<int> DoPeek()
        {
            AddFat(Balance.FatHome);
            Advance();
            var picks = Tonight.Select((p, i) => (p, i)).Where(x => !Hinted.Contains(x.p.Id)).Take(HasAbility("cafe", 2) ? 2 : 1).Select(x => x.i).ToList();
            foreach (var i in picks) Hinted.Add(Tonight[i].Id);
            return picks;
        }

        public void DoNap()
        {
            AddFat(Balance.FatNap);
            AddSus(Balance.NapSus);
            Advance();
        }

        // ---------- 하루의 시작 ----------

        void StartDay()
        {
            susRose = false;
            wobbleRolls = 0;
            Maintenance = maintenanceNext; maintenanceNext = false;
            snackFatToday = snackFat; snackFat = 0;
            sulkyTonight = sulky; sulky = new();
            PickNotice();
            if (Is(NoticeKind.QuietNight)) AddFat(-5);
            if (Is(NoticeKind.AllNighter)) AddFat(10);
            if (Is(NoticeKind.HoldoutSilence)) AddSus(-5);
            if (IntimacyOf(Doubter) >= 3) AddSus(-5); // 의심하는자 친밀도 3: 매일 의심 −5

            // 단골의 이야기 글(그날 몫) + 나머지는 회차마다 섞인 풀에서 차례로
            var regulars = Day <= Posts.Length ? Posts[Day - 1].Where(p => p.Author != null) : Enumerable.Empty<Post>();
            Tonight = regulars.Select(p => p.Bad != null && RegularHappy.TryGetValue(p.Author, out var ok) && !ok ? p.Bad : p).ToList();
            // 친해진 단골은 글이 미묘해진다: 1 시너지 글, 2 빈칸 글, 3 정답이 둘이거나 섬뜩을 반기는 글
            for (int k = 0; k < Tonight.Count; k++)
            {
                var reg = Tonight[k];
                int lv = ClueLevel(reg.Author);
                if (lv < 1 || rng.NextDouble() >= 0.5) continue;
                Func<Post, bool> harder = lv >= 3 ? q => q.Answers.Length > 1 || q.WantsCreepy
                    : lv == 2 && On(Feature.BlankPosts) ? q => q.IsBlank
                    : q => !q.IsBlank && q.Answers.Any(a => a.Eye != a.Mouth);
                if (NextPool(harder) is not { } src) continue;
                var v = src.Clone();
                v.Author = reg.Author; v.Id = $"i{Day}_{k}"; v.Holdout = false;
                Tonight[k] = v;
            }
            int slots = Is(NoticeKind.QuietNight) ? Balance.PostsPerNight - 1 : Balance.PostsPerNight;
            while (Tonight.Count < slots && NextPool(p => On(Feature.BlankPosts) || !p.IsBlank) is { } next) Tonight.Add(next);
            if (Is(NoticeKind.DoubleFace) && On(Feature.BlankPosts) && !Tonight.Any(p => p.IsBlank) && NextPool(p => p.IsBlank) is { } blank)
            {
                int slot = Tonight.FindLastIndex(p => p.Author == null);
                if (slot >= 0) Tonight[slot] = blank; else Tonight.Add(blank);
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
            // 6일차부터 두 번째 기호 세트(정상인들의 새 유행어)가 섞인다
            if (Day >= Balance.SecondSetDay) Tonight = Tonight.Select(SecondSetVariant).ToList();
            Replies = new ReplyState[Tonight.Count];

            // 고향 통신: 못 푼 기호 하나에 정답 감각을 연필로 적어 준다 (확정은 아니다)
            CallHintSymbol = null;
            if (callHint)
            {
                callHint = false;
                CallHintSymbol = Seen.FirstOrDefault(t => !Confirmed.Contains(t) && !IsPending(t) && !Pencil.ContainsKey(t));
                if (CallHintSymbol != null) Pencil[CallHintSymbol] = Sym[CallHintSymbol];
            }
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

        Post SecondSetVariant(Post p)
        {
            if (p.Author != null) return p; // 단골은 예전 말투 그대로
            var tokens = p.Text.Split(' ');
            int seed = p.Id.Sum(c => c);
            bool changed = false;
            for (int k = 0; k < tokens.Length; k++)
            {
                if (!Sym.TryGetValue(tokens[k], out var e) || SecondSet.Contains(tokens[k]) || (seed + k) % 2 != 0) continue;
                var second = SecondSet.Where(s => Sym[s] == e).ToList();
                tokens[k] = second[(seed + k) % second.Count];
                changed = true;
            }
            if (!changed) return p;
            var c = p.Clone();
            c.Text = string.Join(" ", tokens);
            return c;
        }

        void PickNotice()
        {
            Today = null;
            if (!NoticesEnabled || Day < 2 || Maintenance) return;
            if (Day >= LastDay) { Today = FinalNotice; return; }
            if (Day == LastDay - 1 && LastDay == Balance.Days) { Today = Notices.First(n => n.Kind == NoticeKind.MimicDay); return; } // 따라 하기 카드
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
            while (Tonight.Count < Balance.PostsPerNight && NextPool(p => !p.IsBlank) is { } next) Tonight.Add(next);
            Replies = new ReplyState[Tonight.Count];
        }

        // ---------- 해독: 스티커 → 검증 대기 → 그림 페이지 묶음 확정 (Chants of Sennaar, Obra Dinn) ----------

        public int StickerNeed => Traits[Trait.Decode] >= 2 ? 1 : 2;
        public bool Solved(string s) => Confirmed.Contains(s);
        public bool IsPending(string s) => !Confirmed.Contains(s) && Stickers.GetValueOrDefault(s) >= StickerNeed;
        public int SolvedCount => Confirmed.Count;
        public List<string> PendingSymbols => Sym.Keys.Where(IsPending).ToList();
        int Unconfirmed => Sym.Count - Confirmed.Count;

        // 검증 대기가 셋 모이면(남은 기호가 셋 미만이면 하나·둘로도) 그림 페이지가 열린다
        public bool CanOpenPage => Day >= PageLockedUntil && (PendingSymbols.Count >= 3 || (PendingSymbols.Count > 0 && Unconfirmed < 3));
        public List<string> PageSymbols => PendingSymbols.Take(3).ToList();

        // 짝이 다 맞아야 한꺼번에 확정. 틀리면 어느 것이 틀렸는지 알려 주지 않고 다음 날 밤까지 잠긴다 (카페 3단계면 하나 표시)
        public (bool ok, string wrong) TryConfirm(Dictionary<string, Emo> guess)
        {
            var page = PageSymbols;
            if (!CanOpenPage || page.Any(s => !guess.ContainsKey(s))) return (false, null);
            var wrong = page.FirstOrDefault(s => guess[s] != Sym[s]);
            if (wrong == null)
            {
                foreach (var s in page) { Confirmed.Add(s); Pencil.Remove(s); }
                Followers += Balance.ConfirmFollowers;
                return (true, null);
            }
            PageLockedUntil = Day + 1;
            return (false, HasAbility("cafe", 3) ? wrong : null);
        }

        public void SetPencil(string sym, Emo? e)
        {
            if (Confirmed.Contains(sym)) return;
            if (e == null) Pencil.Remove(sym); else Pencil[sym] = e.Value;
        }

        public void Scrap(string text) { if (!Scraps.Contains(text)) Scraps.Add(text); }

        // ---------- 단골 친밀도 ----------

        public int IntimacyOf(string author) => author == null || !On(Feature.Intimacy) ? 0 : Intimacy.GetValueOrDefault(author);
        // 열려 있는 단서 수: 서운한 밤엔 하나 닫힌다
        public int ClueLevel(string author) => Math.Max(0, IntimacyOf(author) - (SulkyTonight(author) ? 1 : 0));
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
            // 서운함: 다음 밤 그 단골의 단서가 하나 닫히고 입은 따끔·쿨만
            if (lv > 0) Intimacy[author] = lv - 1;
            IntimacyWins[author] = 0;
            sulky.Add(author);
        }

        public ComposeRule RuleFor(Post post) =>
            SulkyTonight(post.Author) && On(Feature.Intimacy) && !post.IsBlank ? new ComposeRule { Mouths = new() { Emo.Anger, Emo.Surprise } } : new ComposeRule();

        // ---------- 밤 ----------

        public bool NightDone => Uploaded && Replies.All(r => r != null);

        // 색 단서를 볼 수 있는지: 공감 1레벨이거나 피드 구경으로 미리 본 글
        public bool ShowsColor(Post p) => p.Hint != Hint.None && (Traits[Trait.Empathy] >= 1 || Hinted.Contains(p.Id));

        // 지친 사진 (Cultist Simulator): 피로 40 이상에서 올린 사진. 정답이 축축이면 더 먹히고 반짝이면 덜 먹힌다
        public float TiredMultiplier(Emo mood)
        {
            if (!Leaking) return 1;
            if (mood == Emo.Sad) return (InDanger ? Balance.TiredSadDanger : Balance.TiredSad) + (IntimacyOf(Gloomcat) >= 3 ? 0.2f : 0);
            if (mood == Emo.Joy) return Balance.TiredJoy;
            return 1;
        }

        public (Result? r, int f) DoUpload(Emo eye, Emo mouth, Decor decor, bool glitched = false)
        {
            Uploaded = true;
            Result? r = null;
            float f = Balance.RoomUpload, s = 0;
            if (Went != null)
            {
                var mood = PlaceById(Went).Mood;
                r = glitched ? Result.Creepy : Judge(eye, mouth, mood);
                var (bf, bs) = Balance.Upload(r.Value);
                f = bf; s = bs;
                if (f > 0) f *= TiredMultiplier(mood) * (Is(NoticeKind.HeartDay) && mood == Emo.Shy ? 1.5f : 1);
            }
            if (f > 0 && IntimacyOf(Starlover) >= 3) f *= 1.2f; // 별빛러버 친밀도 3: 좋아요 +20%
            if (decor != null) f += Balance.DecorBonus * (Is(NoticeKind.DecorBoom) ? 2 : 1);
            if (InDanger) s += Balance.DangerPostSus;
            if (Is(NoticeKind.StarBan) && (eye == Emo.Joy || mouth == Emo.Joy)) s += 5;
            Followers = Math.Max(0, Followers + (int)f);
            AddSus(s);
            YesterdayPostFaceToday = Face(eye, mouth, decor);
            UseFace(Face(eye, mouth));
            return (r, (int)f);
        }
        string YesterdayPostFaceToday;

        void UseFace(string face)
        {
            faceUseToday[face] = faceUseToday.GetValueOrDefault(face) + 1;
            LastFace = face;
        }

        // 하나의 빈칸(또는 보통 글)을 판정하고 오늘의 규칙을 적용한다
        (Result r, float f, float s) Score(Post post, Answer[] answers, Emo eye, Emo mouth, bool glitched)
        {
            var r = Judge(eye, mouth, answers, post.WantsCreepy);
            if (Is(NoticeKind.NeutralWeek) && r == Result.Meh) r = Result.Success;
            if (Is(NoticeKind.MimicDay) && YesterdayLastFace != null && FaceEye(YesterdayLastFace) == eye && r is Result.Success or Result.Creepy) r = Result.Perfect;
            if (AwakenDay == Day && r == Result.Success) r = Result.Perfect; // 각성한 밤: 성공이 완벽으로
            if (glitched && r != Result.Wrong) r = Result.Creepy;           // 입력 흔들림을 모르고 보냈다
            var (bf, bs) = Balance.Reply(r);
            float f = bf, s = bs;
            if (post.WantsCreepy && r == Result.Perfect) s = 0;
            if (r == Result.Creepy && Traits[Trait.Empathy] >= 2) s = 0;
            if (f > 0)
            {
                if (Is(NoticeKind.HeartDay) && answers.Any(a => a.Eye == Emo.Shy || a.Mouth == Emo.Shy)) f *= 1.5f;
                if (Is(NoticeKind.SameEyeMouth) && eye == mouth) f *= 1.1f;
                if (Is(NoticeKind.FaceShortage) && Face(eye, mouth) == MostUsedYesterday) f *= 0.7f;
                if (Is(NoticeKind.AllNighter)) f *= 1.3f;
            }
            if (Is(NoticeKind.StarBan) && (eye == Emo.Joy || mouth == Emo.Joy)) s += 5;
            return (r, f, s);
        }

        static Emo? FaceEye(string face) => Feelings.Append(Emo.Neutral).Cast<Emo?>().FirstOrDefault(e => face.Contains($"({E[e.Value].EyeL}") && face.Contains($"{E[e.Value].EyeR})"));

        // newly = 이번에 검증 대기가 된 기호, newSyn = 처음 찾은 시너지
        public (Result r, int f, List<string> newly, Answer newSyn) DoReply(int idx, Emo eye, Emo mouth, bool glitched = false)
        {
            var post = Tonight[idx];
            var (r, f, s) = Score(post, post.Answers, eye, mouth, glitched);
            if (post.Holdout) s += IntimacyOf(Doubter) >= 3 ? Balance.HoldoutSus / 2 : Balance.HoldoutSus; // 의심하는자 친밀도 3이면 절반 // 아직 글자를 쓰는 정상인에게 답하면 의심이 크게 더 오른다
            if (post.Author == Doubter)
            {
                if (r == Result.Wrong) s = Balance.DoubterWrongSus;
                if (r == Result.Perfect) s = Balance.DoubterPerfectSus;
            }
            if (post.Misuse != null)
            {
                if (r is Result.Perfect or Result.Success) s -= 5;  // 오해가 풀렸다
                else MisusedFaces.Add(post.Misuse);                  // 그 얼굴이 계속 엉뚱하게 쓰인다 (거리에도)
            }
            if (post.Literal && On(Feature.Misuse) && r is Result.Wrong or Result.Creepy) literalPending.Add((post, Face(eye, mouth)));
            var newly = Finish(post, r, f, s);
            var syn = SynergyOf(eye, mouth);
            Answer newSyn = syn != null && FoundSynergies.Add(syn.Name) ? syn : null;
            var (em, mm) = Hits(eye, mouth, post.Answers);
            Replies[idx] = new ReplyState { R = r, Face = Face(eye, mouth), EyeHit = em, MouthHit = mm };
            UseFace(Face(eye, mouth));
            return (r, (int)f, newly, newSyn);
        }

        // 빈칸 긴 글 (Golden Idol): 빈칸마다 따로 판정, 둘 다 성공 이상이면 완벽 하나를 더. 답장 하나로 친다
        public (Result r1, Result r2, int f, List<string> newly) DoReplyBlank(int idx, Emo eye1, Emo mouth1, Emo eye2, Emo mouth2, bool glitched = false)
        {
            var post = Tonight[idx];
            var (r1, f1, s1) = Score(post, post.Answers, eye1, mouth1, glitched);
            var (r2, f2, s2) = Score(post, post.Answers2, eye2, mouth2, glitched);
            float f = f1 + f2 + (r1 <= Result.Success && r2 <= Result.Success ? Balance.Reply(Result.Perfect).f : 0);
            var newly = Finish(post, r1 < r2 ? r1 : r2, f, s1 + s2);
            var (em, mm) = Hits(eye1, mouth1, post.Answers);
            Replies[idx] = new ReplyState { R = r1, R2 = r2, Face = Face(eye1, mouth1), Face2 = Face(eye2, mouth2), EyeHit = em, MouthHit = mm };
            UseFace(Face(eye2, mouth2));
            return (r1, r2, (int)f, newly);
        }

        List<string> Finish(Post post, Result r, float f, float s)
        {
            if (InDanger && r == Result.Perfect && !BrokeDown) dangerPerfects++;
            Followers = Math.Max(0, Followers + (int)f);
            AddSus(s);
            AddFat(Balance.FatReply);
            var newly = new List<string>();
            if (r is Result.Perfect or Result.Success)
                foreach (var t in post.Text.Split(' ').Where(Sym.ContainsKey))
                {
                    var was = IsPending(t) || Confirmed.Contains(t);
                    Stickers[t] = Stickers.GetValueOrDefault(t) + 1;
                    if (!was && IsPending(t)) newly.Add(t);
                }
            if (post.Author != null)
            {
                RegularHappy[post.Author] = r is Result.Perfect or Result.Success;
                if (r is Result.Perfect or Result.Success) Bond(post.Author, true);
                else if (r == Result.Wrong) Bond(post.Author, false);
            }
            return newly;
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

        public void MarkSeen()
        {
            foreach (var p in Tonight)
                foreach (var t in p.Text.Split(' ').Where(Sym.ContainsKey))
                {
                    Seen.Add(t);
                    if (p.Author != null && Regulars.Contains(p.Author)) (Habits.TryGetValue(p.Author, out var h) ? h : Habits[p.Author] = new()).Add(t);
                }
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

        // 정산 회복 선택 (Papers, Please의 저녁 지출): 잠들기 전에 하나. 잠 −12를 대신한다
        public bool CanSnack => HasAbility("store", 2);
        public void ApplyRecovery(Recovery r)
        {
            switch (r)
            {
                case Recovery.Sleep: AddFat(Balance.RestSleep); break;
                case Recovery.TrueForm: AddFat(Balance.RestTrue); AddSus(Balance.RestTrueSus); break;
                case Recovery.HomeCall: AddFat(Balance.RestCall); callHint = true; break;
                case Recovery.Snack: if (CanSnack) { AddFat(Balance.RestSnack); snackFat = Balance.SnackOutingFat; } break;
            }
        }

        void NextDay()
        {
            if (!susRose) AddSus(-Balance.SusDecay); // 의심이 오르지 않은 날은 조금 가라앉는다
            YesterdayLastFace = LastFace;
            YesterdayPostFace = YesterdayPostFaceToday;
            YesterdayPostFaceToday = null;
            MostUsedYesterday = faceUseToday.Count > 0 ? faceUseToday.OrderByDescending(x => x.Value).First().Key : null;
            faceUseToday.Clear();
            sleptPledge = false;
            if (Day >= LastDay) { Ending = FinalEnding(); return; }
            Day++;
            Slot = Slot.Morning;
            Uploaded = HeldTonight = false;
            Went = null;
            StartDay();
        }

        public void EndNight()
        {
            GrantNightPoint();
            nightPointGiven = false;
            if (Ending != Ending.None) return;
            NextDay();
        }

        // 그림 일기의 다짐 스티커: 하루를 정리하며 고른 하나가 다음 날을 조금 돕는다 (EndNight 뒤에 부른다)
        public void ApplyPledge(Pledge p)
        {
            switch (p)
            {
                case Pledge.Hint: // 오늘 밤 글 하나의 색이 미리 보인다
                    var post = Tonight.FirstOrDefault(x => !Hinted.Contains(x.Id));
                    if (post != null) Hinted.Add(post.Id);
                    break;
                case Pledge.Sleep: // 더 푹 잔다. 100 판정 각성 확률도 조금
                    AddFat(-10);
                    sleptPledge = true;
                    break;
                case Pledge.Review: // 아직 못 푼 기호 하나에 스티커 한 장
                    var sym = Seen.FirstOrDefault(t => !Confirmed.Contains(t) && !IsPending(t));
                    if (sym != null) Stickers[sym] = Stickers.GetValueOrDefault(sym) + 1;
                    break;
            }
        }

        // 셧다운: 그날은 그대로 끝나고 다음 날로 (특성 포인트 없음, 남은 행동·답장도 사라진다)
        public void ForceEndDay()
        {
            nightPointGiven = false;
            ActionPending = false;
            Pending = null;
            if (Ending != Ending.None) return;
            NextDay();
        }

        public Ending FinalEnding()
        {
            if (Sus >= 100) return Ending.Found;
            if (Followers >= Balance.FollowerGoal && Sus < Balance.ConquerSusMax) return Ending.Conquer;
            if (SolvedCount >= Balance.AbsorbSolved && Followers < Balance.FollowerGoal) return Ending.Absorb;
            return Ending.Forgotten;
        }

        public IEnumerable<string> EpilogueCuts => Places.Where(p => AffLevel(p.Id) >= 3).Select(p => Epilogue[p.Id]);
    }
}
