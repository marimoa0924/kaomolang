using System.Linq;
using NUnit.Framework;

namespace Kaomoji.Tests
{
    public class GameStateTests
    {
        static Emo Far(Answer[] ans) => GameData.Feelings.First(e => ans.All(a => GameData.MoodOf(a.Eye) != GameData.MoodOf(e)));

        [Test]
        public void JudgeTableWithNearMiss()
        {
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Joy, Emo.Joy, Emo.Joy));
            Assert.AreEqual(Result.Success, GameState.Judge(Emo.Joy, Emo.Neutral, Emo.Joy));
            Assert.AreEqual(Result.Creepy, GameState.Judge(Emo.Joy, Emo.Sad, Emo.Joy));
            Assert.AreEqual(Result.Near, GameState.Judge(Emo.Shy, Emo.Joy, Emo.Joy));      // 들뜸끼리
            Assert.AreEqual(Result.Near, GameState.Judge(Emo.Anger, Emo.Anger, Emo.Sad));  // 가라앉음끼리
            Assert.AreEqual(Result.Near, GameState.Judge(Emo.Neutral, Emo.Joy, Emo.Surprise)); // 멍함끼리
            Assert.AreEqual(Result.Wrong, GameState.Judge(Emo.Sad, Emo.Joy, Emo.Joy));
            Assert.AreEqual("(`益´)", GameData.Face(Emo.Anger, Emo.Anger));
            Assert.AreEqual("=(^▽^)=", GameData.Face(Emo.Joy, Emo.Joy, GameData.Paw));
        }

        [Test]
        public void SynergyMultiAnswerAndCreepyFan()
        {
            var ans = new[] { GameData.Pure(Emo.Sad), GameData.Syn("괜찮은 척") };
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Sad, Emo.Joy, ans));
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Sad, Emo.Sad, ans));
            Assert.AreEqual(Result.Creepy, GameState.Judge(Emo.Sad, Emo.Anger, ans));
            var obsess = new[] { GameData.Syn("집착") };
            Assert.AreEqual(Result.Creepy, GameState.Judge(Emo.Surprise, Emo.Sad, obsess));
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Surprise, Emo.Sad, obsess, wantsCreepy: true));
        }

        [Test]
        public void PostsAreEmojiNotSymbols()
        {
            foreach (var p in GameData.Pool.Concat(GameData.Posts.SelectMany(d => d)))
            {
                var emos = p.Answers.Concat(p.Answers2 ?? new Answer[0]).SelectMany(a => new[] { a.Eye, a.Mouth }).Distinct();
                Assert.IsTrue(emos.All(e => GameData.Emojis[e].Any(p.Text.Contains)), p.Id); // 정답 감정의 이모지가 들어 있다
                Assert.IsFalse(p.Text.Contains("ㅋ") || p.Text.Contains("ㅠ"), p.Id);            // 감탄사는 이모지로
            }
            var irony = GameData.Emojify("슬퍼", new[] { GameData.Pure(Emo.Sad) }, 0, ironic: true);
            Assert.IsTrue(irony.Contains("😂") && !GameData.Emojis[Emo.Sad].Any(irony.Contains)); // 반어: 슬픈 글에 웃는 이모지
        }

        [Test]
        public void DayHasThreeSlotsAndRestsAreNumbersOnly()
        {
            var s = new GameState(1) { Fat = 50, Sus = 20 };
            Assert.AreEqual(6, s.Eyes.Count);
            Assert.AreEqual(1, s.Mouths.Count);
            s.DoRest(Rest.Full);
            Assert.AreEqual(Slot.Morning, s.Slot); // 결과 창을 닫기 전엔 시간이 안 흐른다
            Assert.IsFalse(s.CanAct);
            s.EndAction();
            Assert.AreEqual(20, s.Fat, 0.01);
            Assert.AreEqual(10, s.Sus, 0.01);
            int f = s.Followers;
            s.DoRest(Rest.Feed); s.EndAction();
            Assert.AreEqual(f + Balance.FeedFollowers, s.Followers);
            Assert.IsTrue(s.IsNight);
        }

        [Test]
        public void PracticeHalvesSuspicionForThreeDays()
        {
            var s = new GameState(1) { NoticesEnabled = false };
            s.DoRest(Rest.Practice); s.EndAction();
            float s0 = s.Sus;
            s.DoReply(0, Far(s.Tonight[0].Answers), Emo.Neutral); // 큰 오답 +10 → +5
            Assert.AreEqual(5, s.Sus - s0, 0.01);
            for (int d = 0; d < 3; d++) s.EndNight();
            s0 = s.Sus;
            s.DoReply(0, Far(s.Tonight[0].Answers), Emo.Neutral);
            Assert.Greater(s.Sus - s0, 5); // 사흘이 지나면 그대로
        }

        [Test]
        public void LinearGrowthOpensByDay()
        {
            var s = new GameState(1);
            Assert.AreEqual(Balance.FatOut, s.OutingFat);
            Assert.AreEqual(Balance.RestFull, s.RestFatNow(Rest.Full));
            int early = s.Letters(10);
            while (s.Day < Balance.GrowOuting) s.EndNight();
            Assert.AreEqual(Balance.FatOutSkilled, s.OutingFat);
            while (s.Day < Balance.GrowLetters) s.EndNight();
            Assert.AreEqual(early + 1, s.Letters(10));
            while (s.Day < Balance.GrowRest) s.EndNight();
            Assert.AreEqual(Balance.RestFull - Balance.RestGrowth, s.RestFatNow(Rest.Full));
            while (s.Day < Balance.GrowNear) s.EndNight();
            int f = s.Followers;
            var p = s.Tonight[0];
            var near = GameData.Feelings.Append(Emo.Neutral).First(e => p.Answers.All(x => x.Eye != e) && GameData.MoodOf(e) == GameData.MoodOf(p.Answers[0].Eye));
            Assert.AreEqual(Result.Near, s.DoReply(0, near, Emo.Neutral).r);
            Assert.AreEqual(f, s.Followers); // 연기 몰입: 아쉬운 오답에 팔로워가 줄지 않는다
        }

        [Test]
        public void FirstTwoDaysCrossSeventyOnceThenNoiseIsRare()
        {
            var s = new GameState(5) { NoExtras = true };
            string first = null;
            for (int d = 0; d < 2; d++)
            {
                for (int k = 0; k < 2; k++) { var ev = s.PrepareOuting("park"); s.DoEncounter(ev.Fits[0]); s.EndAction(); }
                for (int i = 0; i < s.Tonight.Count; i++) s.DoReply(i, Emo.Neutral, Emo.Neutral);
                first ??= s.NoiseMsg;
                if (d == 0) Assert.IsNull(s.NoiseMsg);
                s.EndNight();
            }
            Assert.AreEqual(GameData.NoiseMessages[0], first); // 둘째 날 밤 처음 70을 넘는다
            s.NoiseMsg = null;
            s.Fat = 60; s.DoRest(Rest.Feed); s.EndAction(); s.Fat = 69;
            s.PrepareOuting("park"); s.DoEncounter(Emo.Joy);
            Assert.IsNull(s.NoiseMsg); // 사흘 안엔 다시 안 뜬다
            for (int d = 0; d < 3; d++) s.EndNight();
            s.Fat = 69; s.PrepareOuting("park"); s.DoEncounter(Emo.Joy);
            Assert.AreEqual(GameData.NoiseMessages[1], s.NoiseMsg); // 다음 내용
        }

        [Test]
        public void EncounterGivesReactionMouthEitherWay()
        {
            var s = new GameState(1);
            var ev = s.PrepareOuting("station");
            var (ok, mouth, _) = s.DoEncounter(ev.Fits[0]);
            Assert.IsTrue(ok);
            Assert.AreEqual(ev.WinMouth, mouth);
            s.EndAction();
            ev = s.PrepareOuting("park");
            var wrong = GameData.Feelings.First(e => !ev.Fits.Contains(e));
            var sus = s.Sus;
            (ok, mouth, _) = s.DoEncounter(wrong);
            Assert.IsFalse(ok);
            Assert.Greater(s.Sus, sus);
        }

        [Test]
        public void AllSuccessCollectsEveryMouthByDay5()
        {
            var s = new GameState(5) { NoExtras = true };
            foreach (var place in new[] { "park", "station", "store", "cafe", "park" })
            {
                var ev = s.PrepareOuting(place);
                Assert.IsTrue(s.DoEncounter(ev.Fits[0]).ok);
                s.EndAction();
                s.DoRest(Rest.Full); s.EndAction();
                s.EndNight();
            }
            Assert.AreEqual(6, s.Mouths.Count);
        }

        [Test]
        public void LearningMapPostsByAct()
        {
            var s = new GameState(2);
            Assert.IsTrue(s.Tonight.Where(p => p.Author == null).All(p => p.Answers.Length == 1 && !p.Answers[0].IsSynergy)); // 1막: 쉬운 글
            while (s.Day < 4) s.EndNight();
            Assert.IsTrue(s.Tonight.Any(p => p.Answers.Any(a => a.IsSynergy)));  // 4일차: 섞인 글
            s.EndNight();
            Assert.IsTrue(s.Tonight.Any(p => p.Answers.All(a => a.Eye == Emo.Shy))); // 5일차: 반짝과 헷갈리는 간질
            bool ironic = false;
            while (s.Day < 10) { s.EndNight(); ironic |= s.Tonight.Any(p => p.Text.Contains("😂") && p.Answers.All(a => GameData.MoodOf(a.Eye) == 1)); }
            Assert.IsTrue(ironic); // 2막: 반어 이모지
        }

        [Test]
        public void UneasyPostsAppearWhenFollowersGrow()
        {
            var s = new GameState(3);
            Assert.IsFalse(s.Tonight.Any(p => p.Holdout));
            s.Followers = (int)(Balance.FollowerGoal * Balance.UneasyFrom);
            s.EndNight();
            Assert.AreEqual(1, s.Tonight.Count(p => p.Holdout));
            int idx = s.Tonight.FindIndex(p => p.Holdout);
            float s0 = s.Sus;
            var a = s.Tonight[idx].Answers[0];
            s.DoReply(idx, a.Eye, a.Mouth);
            Assert.AreEqual(Balance.HoldoutSus, s.Sus - s0, 0.01);
        }

        [Test]
        public void FirstDayCanBeRandom()
        {
            var s = new GameState(4);
            s.RerollFirstDay();
            Assert.IsFalse(s.Tonight.Any(p => p.Author != null));
            Assert.AreEqual(Balance.PostsPerNight, s.Tonight.Count);
        }

        [Test]
        public void HeldPostReturnsAsFollowup()
        {
            var s = new GameState(1) { NoticesEnabled = false };
            Assert.IsTrue(s.DoHold(1));
            Assert.IsFalse(s.DoHold(0));
            s.DoReply(0, Emo.Joy, Emo.Neutral);
            s.DoReply(2, Emo.Joy, Emo.Neutral);
            s.DoUpload(Emo.Joy, Emo.Joy, null);
            Assert.IsTrue(s.NightDone);
            s.EndNight();
            Assert.IsTrue(s.Tonight.Any(p => p.Id.StartsWith("f") && p.Author == GameData.Gloomcat)); // 서운한 후속 글
        }

        [Test]
        public void MentionPoolIsLargeAndShuffledPerRun()
        {
            Assert.GreaterOrEqual(GameData.Pool.Count, 100);
            Assert.AreEqual(GameData.Pool.Count, GameData.Pool.Select(p => p.Id).Distinct().Count());
            var a = new GameState(1); var b = new GameState(2);
            var seenA = new System.Collections.Generic.List<string>(); var seenB = new System.Collections.Generic.List<string>();
            for (int d = 0; d < Balance.Days; d++) { seenA.AddRange(a.Tonight.Select(p => p.Id)); seenB.AddRange(b.Tonight.Select(p => p.Id)); a.EndNight(); b.EndNight(); }
            Assert.AreEqual(seenA.Count, seenA.Distinct().Count());
            CollectionAssert.AreNotEqual(seenA, seenB);
        }

        [Test]
        public void BreakdownThenCollapse()
        {
            var s = new GameState(1) { Fat = 95 };
            s.PrepareOuting("park");
            s.DoEncounter(Emo.Joy);
            Assert.IsTrue(s.BrokeDown && s.JustBrokeDown);
            Assert.AreEqual(120, s.FatMax);
            Assert.AreEqual(50, s.Fat, 0.01);
            s.ForceEndDay();
            Assert.AreEqual(2, s.Day);
            s.Fat = 118;
            s.DoReply(0, Emo.Joy, Emo.Neutral);
            Assert.AreEqual(Ending.Collapse, s.Ending);
        }

        [Test]
        public void EndingRules()
        {
            var s = new GameState(1) { Sus = 95 };
            s.DoReply(1, Emo.Joy, Emo.Neutral); // 우울한고양이 글에 반짝: 큰 오답 +10
            Assert.AreEqual(Ending.Found, s.Ending);
            s = new GameState(1) { Followers = Balance.FollowerGoal };
            Assert.AreEqual(Ending.Conquer, s.FinalEnding());
            s.Sus = 80;
            Assert.AreEqual(Ending.Forgotten, s.FinalEnding());
            s.Perfects = Balance.AbsorbPerfect;
            Assert.AreEqual(Ending.Absorb, s.FinalEnding());
            s = new GameState(1) { NoticesEnabled = false };
            for (int d = 0; d < Balance.Days; d++) s.EndNight();
            Assert.AreEqual(Ending.Forgotten, s.Ending);
        }

        [Test]
        public void SusDecaysOnQuietDays()
        {
            var s = new GameState(1) { Sus = 10 };
            s.EndNight();
            Assert.AreEqual(10 - Balance.SusDecay, s.Sus, 0.01);
        }

        [Test]
        public void LiteralMistakeComesBackAsMisuse()
        {
            var lit = GameData.Pool.First(p => p.Literal && !p.IsBlank);
            var s = new GameState(1);
            s.Tonight[2] = lit;
            s.DoReply(2, Far(lit.Answers), Emo.Neutral);
            s.EndNight();
            var mis = s.Tonight.Last();
            Assert.IsNotNull(mis.Misuse);
            s.DoReply(s.Tonight.Count - 1, Far(mis.Answers), Emo.Neutral);
            Assert.IsTrue(s.MisusedFaces.Contains(mis.Misuse));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var s = new GameState(1);
            var c = s.Clone();
            s.DoReply(0, Emo.Joy, Emo.Neutral);
            s.Mouths.Add(Emo.Joy);
            Assert.IsNull(c.Replies[0]);
            Assert.AreEqual(1, c.Mouths.Count);
        }

        // 진짜 Unlocks.csv를 읽는다. 테스트 파일 위치에서 찾아가야 Unity와 dotnet 둘 다 된다
        static System.Collections.Generic.Dictionary<Feature, Unlock> RealUnlocks([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            GameData.ParseUnlocks(System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(here), "..", "..", "Resources", "Unlocks.csv")));

        [Test]
        public void UnlockMapDefersV4Systems()
        {
            var t = RealUnlocks();
            Assert.AreEqual(3, t[Feature.Rest].Day);
            foreach (var f in new[] { Feature.Notices, Feature.PlaceAbility, Feature.Intimacy, Feature.DecorMix, Feature.BlankPosts })
                Assert.IsFalse(t[f].On, f.ToString());
            var old = GameData.UnlockTable;
            GameData.UnlockTable = t;
            try
            {
                var s = new GameState(1);
                Assert.IsFalse(s.On(Feature.Rest));
                for (int d = 0; d < 10; d++) { Assert.IsNull(s.Today); Assert.IsFalse(s.Tonight.Any(p => p.IsBlank)); s.EndNight(); }
                Assert.IsTrue(s.On(Feature.Rest));
            }
            finally { GameData.UnlockTable = old; }
        }

        [Test]
        public void PlaceAbilitiesDoSomethingWhenOn()
        {
            var s = new GameState(1) { Fat = 50 };
            s.Unlocks[Feature.PlaceAbility] = new Unlock(1, true);
            foreach (var p in new[] { "park", "station", "store", "cafe" }) s.Aff[p] = 3;
            Assert.AreEqual(Balance.FatOut - 3, s.OutingFat);                 // 공원 2
            int f = s.Followers;
            s.DoRest(Rest.Feed); s.EndAction();
            Assert.AreEqual(f + Balance.FeedFollowers + 100, s.Followers);    // 역 2
            float fat = s.Fat;
            s.EndNight();
            Assert.AreEqual(fat + Balance.FatSleep - 5, s.Fat, 0.01);       // 공원 3
        }

        [Test]
        public void DiaryMemoryHelpsTomorrow()
        {
            var s = new GameState(1) { Sus = 20 };
            s.EndNight(); s.ApplyMemory(Memory.MeetFail);
            Assert.AreEqual(Balance.FatOut - 5, s.OutingFat);          // 실패에서 배운다
            s.EndNight();
            Assert.AreEqual(Balance.FatOutSkilled, s.OutingFat);        // 하루만
            float sus = s.Sus; s.ApplyMemory(Memory.ReplyBad);
            Assert.AreEqual(sus - 5, s.Sus, 0.01);
            int f = s.Followers; s.ApplyMemory(Memory.ReplyGood);
            Assert.AreEqual(f + 100, s.Followers);
            s.ApplyMemory(Memory.MeetWin, "park");
            Assert.AreEqual(1, s.AffLevel("park"));                     // 호감 진행 +1 (0→1은 한 번)
        }
    }
}
