using System.Linq;
using NUnit.Framework;

namespace Kaomoji.Tests
{
    public class GameStateTests
    {
        [Test]
        public void JudgeTable()
        {
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Joy, Emo.Joy, Emo.Joy));
            Assert.AreEqual(Result.Success, GameState.Judge(Emo.Joy, Emo.Neutral, Emo.Joy));
            Assert.AreEqual(Result.Creepy, GameState.Judge(Emo.Joy, Emo.Sad, Emo.Joy));
            Assert.AreEqual(Result.Meh, GameState.Judge(Emo.Neutral, Emo.Joy, Emo.Joy));
            Assert.AreEqual(Result.Wrong, GameState.Judge(Emo.Sad, Emo.Joy, Emo.Joy));
            Assert.AreEqual("(`益´)", GameData.Face(Emo.Anger, Emo.Anger));
            Assert.AreEqual("=(^▽^)=", GameData.Face(Emo.Joy, Emo.Joy, GameData.Paw));
            Assert.AreEqual(30, GameData.Sym.Count); // 첫 세트 20 + 6일차부터 두 번째 세트 10
        }

        [Test]
        public void SynergyMultiAnswerAndCreepyFan()
        {
            var ans = new[] { GameData.Pure(Emo.Sad), GameData.Syn("괜찮은 척") };
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Sad, Emo.Joy, ans));   // T▽T 시너지 정답
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Sad, Emo.Sad, ans));
            Assert.AreEqual(Result.Creepy, GameState.Judge(Emo.Sad, Emo.Anger, ans));
            var obsess = new[] { GameData.Syn("집착") };
            Assert.AreEqual(Result.Creepy, GameState.Judge(Emo.Surprise, Emo.Sad, obsess));
            Assert.AreEqual(Result.Perfect, GameState.Judge(Emo.Surprise, Emo.Sad, obsess, wantsCreepy: true));
            // 8개 글이 정답 둘
            Assert.AreEqual(8, GameData.Posts.SelectMany(d => d).Count(p => p.Answers.Length == 2));
        }

        [Test]
        public void StartsWithAllEyesAndDayHasThreeSlots()
        {
            var s = new GameState(1);
            Assert.AreEqual(6, s.Eyes.Count);
            Assert.AreEqual(1, s.Mouths.Count);
            s.DoNap();
            Assert.AreEqual(Slot.Morning, s.Slot); // 결과 창을 닫기 전엔 시간이 안 흐른다
            Assert.IsFalse(s.CanAct);
            s.EndAction();
            Assert.AreEqual(Slot.Afternoon, s.Slot);
            s.DoPeek();
            s.EndAction();
            Assert.IsTrue(s.IsNight);
            Assert.IsTrue(s.ShowsColor(s.Tonight[0]));
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
            var wrong = new[] { Emo.Joy, Emo.Sad, Emo.Anger, Emo.Surprise, Emo.Shy }.First(e => !ev.Fits.Contains(e));
            var sus = s.Sus;
            (ok, mouth, _) = s.DoEncounter(wrong);
            Assert.IsFalse(ok);
            Assert.Greater(s.Sus, sus);
        }

        [Test]
        public void AllSuccessCollectsEveryMouthByDay5()
        {
            // 하루 한 번만 외출하고(나머지는 쉼) 장소를 돌며 모두 성공해도 5일차 안에 입 다섯 개가 모인다
            var s = new GameState(5) { NoExtras = true };
            string[] route = { "park", "station", "store", "cafe", "park" };
            foreach (var place in route)
            {
                var ev = s.PrepareOuting(place);
                Assert.IsTrue(s.DoEncounter(ev.Fits[0]).ok);
                s.EndAction();
                s.DoNap(); s.EndAction();
                s.EndNight();
            }
            Assert.AreEqual(6, s.Mouths.Count);
            Assert.LessOrEqual(s.Day, 6);
        }

        [Test]
        public void UneasyPostsAppearWhenFollowersGrow()
        {
            var s = new GameState(3);
            Assert.IsFalse(s.Tonight.Any(p => p.Holdout));
            s.Followers = (int)(Balance.FollowerGoal * Balance.UneasyFrom);
            s.EndNight();
            Assert.AreEqual(1, s.Tonight.Count(p => p.Holdout)); // 밤마다 하나
            Assert.AreEqual(Balance.PostsPerNight, s.Tonight.Count);

            // 정상인에게 답하면: 팔로워 판정은 똑같고(시큰둥이면 변화 없음) 의심만 크게 오른다
            int idx = s.Tonight.FindIndex(p => p.Holdout);
            int f0 = s.Followers; float s0 = s.Sus;
            s.DoReply(idx, Emo.Neutral, Emo.Neutral);
            Assert.AreEqual(f0, s.Followers);
            Assert.AreEqual(Balance.HoldoutSus, s.Sus - s0, 0.01);
        }

        [Test]
        public void FirstDayCanBeRandom()
        {
            var s = new GameState(4);
            s.RerollFirstDay();
            Assert.IsTrue(s.Tonight.All(p => p.Id.StartsWith("r") || p.Id.StartsWith("x")));
            Assert.IsFalse(s.Tonight.Any(p => p.Author != null)); // 단골의 고정 글(케이크 등) 대신 풀에서
            Assert.AreEqual(Balance.PostsPerNight, s.Tonight.Count);
        }

        [Test]
        public void HeldPostReturnsAsFollowup()
        {
            var s = new GameState(1) { NoticesEnabled = false };
            Assert.IsTrue(s.DoHold(1));
            Assert.IsFalse(s.DoHold(0)); // 밤마다 하나만
            s.DoReply(0, Emo.Joy, Emo.Neutral);
            s.DoReply(2, Emo.Joy, Emo.Neutral);
            s.DoUpload(Emo.Joy, Emo.Joy, null);
            Assert.IsTrue(s.NightDone);
            s.EndNight();
            Assert.AreEqual(Balance.PostsPerNight + 1, s.Tonight.Count);
            Assert.AreEqual(GameData.Gloomcat, s.Tonight.Last().Author); // 보류한 글이 후속 글로
            Assert.IsTrue(s.SulkyTonight(GameData.Gloomcat));            // 서운한 단골: 입은 따끔·쿨만
            CollectionAssert.AreEquivalent(new[] { Emo.Anger, Emo.Surprise }, s.RuleFor(s.Tonight.Last()).Mouths);
        }

        [Test]
        public void SymbolSolvesAfterTwoStickers()
        {
            var s = new GameState(1);
            s.DoReply(0, Emo.Joy, Emo.Neutral); // 1일째 별빛러버 글의 ☆
            Assert.AreEqual(1, s.Stickers["☆"]);
            Assert.IsFalse(s.Solved("☆"));
            s.Stickers["☆"]++;
            Assert.IsTrue(s.IsPending("☆"));  // 스티커 두 장은 검증 대기일 뿐, 확정은 그림 페이지에서
            Assert.IsFalse(s.Solved("☆"));
        }

        [Test]
        public void MentionPoolIsLargeAndShuffledPerRun()
        {
            Assert.GreaterOrEqual(GameData.Pool.Count, 100); // 정식판 30일 × 하루 2~3자리도 채울 만큼
            Assert.AreEqual(GameData.Pool.Count, GameData.Pool.Select(p => p.Id).Distinct().Count());
            foreach (var p in GameData.Pool)
            {
                // 깨진 글에는 정답 감정의 기호가 들어 있어야 해독이 가능하다
                var emos = p.Answers.SelectMany(a => new[] { a.Eye, a.Mouth }).Distinct();
                foreach (var e in emos) Assert.IsTrue(p.Text.Split(' ').Any(t => GameData.Sym.TryGetValue(t, out var x) && x == e), p.Id);
            }
            var a = new GameState(1); var b = new GameState(2);
            Assert.AreEqual(Balance.PostsPerNight, a.Tonight.Count);
            var seenA = new System.Collections.Generic.List<string>(); var seenB = new System.Collections.Generic.List<string>();
            for (int d = 0; d < Balance.Days; d++) { seenA.AddRange(a.Tonight.Select(p => p.Id)); seenB.AddRange(b.Tonight.Select(p => p.Id)); a.EndNight(); b.EndNight(); }
            Assert.AreEqual(seenA.Count, seenA.Distinct().Count()); // 한 판 안에서 같은 글이 두 번 나오지 않는다
            CollectionAssert.AreNotEqual(seenA, seenB);             // 판마다 다르다
        }

        [Test]
        public void PledgesHelpTomorrow()
        {
            var s = new GameState(1);
            s.MarkSeen();
            s.EndNight();
            s.ApplyPledge(Pledge.Hint);
            Assert.IsTrue(s.ShowsColor(s.Tonight[0]));
            s.ApplyPledge(Pledge.Review);
            Assert.AreEqual(1, s.Stickers.Values.Sum());
            s.Fat = 30;
            s.ApplyPledge(Pledge.Sleep);
            Assert.AreEqual(20, s.Fat, 0.01);
        }

        [Test]
        public void BreakdownThenCollapse()
        {
            var s = new GameState(1) { Fat = 95, AwakenChanceOverride = 0 };
            s.PrepareOuting("park");
            s.DoEncounter(Emo.Joy); // +15 → 100, 셧다운
            Assert.IsTrue(s.BrokeDown);
            Assert.AreEqual(120, s.FatMax);
            Assert.AreEqual(50, s.Fat, 0.01);
            Assert.AreEqual(Ending.None, s.Ending);
            int points = s.Points;
            s.ForceEndDay(); // 붕괴한 날은 그대로 끝
            Assert.AreEqual(2, s.Day);
            Assert.AreEqual(Slot.Morning, s.Slot);
            Assert.AreEqual(points, s.Points);
            s.Fat = 118;
            s.DoReply(0, Emo.Joy, Emo.Neutral); // +4 → 120
            Assert.AreEqual(Ending.Collapse, s.Ending);
        }

        [Test]
        public void EndingRules()
        {
            var s = new GameState(1) { Sus = 95 };
            s.DoReply(0, Emo.Neutral, Emo.Neutral);
            Assert.AreEqual(Ending.None, s.Ending);
            s.DoReply(1, Emo.Joy, Emo.Neutral);
            Assert.AreEqual(Ending.Found, s.Ending);

            s = new GameState(1) { Followers = Balance.FollowerGoal };
            Assert.AreEqual(Ending.Conquer, s.FinalEnding());
            s.Sus = 80;
            Assert.AreEqual(Ending.Forgotten, s.FinalEnding());
            s.Followers = 0;
            foreach (var k in GameData.Sym.Keys.Take(Balance.AbsorbSolved)) s.Confirmed.Add(k);
            Assert.AreEqual(Ending.Absorb, s.FinalEnding());

            s = new GameState(1) { NoticesEnabled = false };
            for (int d = 0; d < Balance.Days; d++) { s.GrantNightPoint(); s.GrantNightPoint(); s.EndNight(); }
            Assert.AreEqual(Ending.Forgotten, s.Ending);
            Assert.AreEqual(Balance.Days, s.Points);
        }
        // ---------- v4 ----------

        static Emo Wrong(Answer[] ans) => GameData.Feelings.First(e => ans.All(a => a.Eye != e));

        [Test]
        public void AwakenInsteadOfShutdown()
        {
            var s = new GameState(1) { Fat = 95, AwakenChanceOverride = 1 };
            s.PrepareOuting("park");
            s.DoEncounter(Emo.Joy);
            Assert.IsTrue(s.JustAwakened);
            Assert.IsFalse(s.JustBrokeDown);
            Assert.AreEqual(Balance.AwakenFat, s.Fat, 0.01);
            Assert.AreEqual(Balance.FatMaxAfterBreakdown, s.FatMax); // 판정은 한 판에 한 번
            var r = s.DoReply(0, Emo.Joy, Emo.Neutral).r;            // 각성한 밤: 성공이 완벽으로
            Assert.AreEqual(Result.Perfect, r);
        }

        [Test]
        public void FatigueZonesAndRecovery()
        {
            var s = new GameState(1) { Fat = 50 };
            Assert.IsTrue(s.Leaking);
            Assert.AreEqual(Balance.TiredSad, s.TiredMultiplier(Emo.Sad), 0.01);
            Assert.AreEqual(Balance.TiredJoy, s.TiredMultiplier(Emo.Joy), 0.01);
            s.ApplyRecovery(Recovery.TrueForm);
            Assert.AreEqual(50 + Balance.RestTrue, s.Fat, 0.01);
            Assert.AreEqual(Balance.RestTrueSus, s.Sus, 0.01);
            Assert.IsFalse(s.CanSnack); // 편의점 2단계 능력
            s.Fat = 69;
            s.PrepareOuting("park");
            s.DoEncounter(Emo.Joy);
            Assert.IsTrue(s.Decors.Contains(GameData.Tentacle)); // 위험 구간 첫 진입
        }

        [Test]
        public void SusDecaysOnQuietDays()
        {
            var s = new GameState(1) { Sus = 10, NoticesEnabled = false };
            s.EndNight();
            Assert.AreEqual(10 - Balance.SusDecay, s.Sus, 0.01);
        }

        [Test]
        public void PageConfirmsAllOrLocks()
        {
            var s = new GameState(1);
            var three = GameData.Sym.Keys.Where(k => !GameData.SecondSet.Contains(k)).Take(3).ToList();
            foreach (var k in three) s.Stickers[k] = 2;
            Assert.IsTrue(s.CanOpenPage);
            var guess = three.ToDictionary(k => k, k => GameData.Sym[k]);
            var bad = new System.Collections.Generic.Dictionary<string, Emo>(guess) { [three[0]] = Wrong(new[] { GameData.Pure(GameData.Sym[three[0]]) }) };
            var (ok, wrong) = s.TryConfirm(bad);
            Assert.IsFalse(ok);
            Assert.IsNull(wrong);           // 어느 것이 틀렸는지 모른다
            Assert.IsFalse(s.CanOpenPage);  // 다음 날 밤까지 잠김
            s.EndNight();
            Assert.IsTrue(s.CanOpenPage);
            int f = s.Followers;
            Assert.IsTrue(s.TryConfirm(guess).ok);
            Assert.IsTrue(three.All(s.Solved));
            Assert.AreEqual(f + Balance.ConfirmFollowers, s.Followers);
        }

        [Test]
        public void PlaceAffinityUnlocksAbilities()
        {
            var s = new GameState(2) { NoExtras = true };
            for (int i = 0; i < 7; i++)
            {
                var ev = s.PrepareOuting("cafe");
                s.DoEncounter(ev.Fits[0]);
                s.EndAction();
            }
            Assert.AreEqual(3, s.AffLevel("cafe")); // 1 + 2 + 4
            Assert.IsTrue(s.NewAbilities.Contains(("cafe", 3)));
        }

        [Test]
        public void IntimacyShrinksComposer()
        {
            var s = new GameState(1);
            var star = s.Tonight[0];
            Assert.AreEqual(GameData.Starlover, star.Author);
            Assert.IsNull(s.RuleFor(star).Mouths);
            s.Intimacy[GameData.Starlover] = 2;
            Assert.AreEqual(star.Answers[0].Mouth, s.RuleFor(star).AutoMouth);
            s.Intimacy[GameData.Starlover] = 3;
            Assert.AreEqual(2, s.RuleFor(star).Eyes.Count);
            s.Intimacy[GameData.Starlover] = 1;
            s.DoReply(0, Wrong(star.Answers), Emo.Neutral); // 틀리면 서운함
            Assert.AreEqual(0, s.IntimacyOf(GameData.Starlover));
        }

        [Test]
        public void BlankPostsJudgeEachBlank()
        {
            var blank = GameData.Pool.First(p => p.IsBlank);
            var s = new GameState(1);
            s.Tonight[2] = blank;
            int f = s.Followers;
            var (r1, r2, _, _) = s.DoReplyBlank(2, blank.Answers[0].Eye, blank.Answers[0].Mouth, blank.Answers2[0].Eye, blank.Answers2[0].Mouth);
            Assert.AreEqual(Result.Perfect, r1);
            Assert.AreEqual(Result.Perfect, r2);
            Assert.AreEqual(f + 3 * Balance.Reply(Result.Perfect).f, s.Followers); // 둘 다 맞히면 보너스 하나 더
            Assert.IsNotNull(s.Replies[2].Face2);
        }

        [Test]
        public void LiteralMistakeComesBackAsMisuse()
        {
            var lit = GameData.Pool.First(p => p.Literal && !p.IsBlank);
            var s = new GameState(1) { NoticesEnabled = false };
            s.Tonight[2] = lit;
            s.DoReply(2, Wrong(lit.Answers), Emo.Neutral);
            s.EndNight();
            var mis = s.Tonight.Last();
            Assert.IsNotNull(mis.Misuse);
            s.DoReply(s.Tonight.Count - 1, Wrong(mis.Answers), Emo.Neutral);
            Assert.IsTrue(s.MisusedFaces.Contains(mis.Misuse));
        }

        [Test]
        public void SecondSetAndNoticesFromLaterDays()
        {
            var s = new GameState(7);
            for (int d = 1; d < Balance.SecondSetDay; d++) s.EndNight();
            Assert.IsNotNull(s.Today); // 2일차부터 아침 공지
            var all = new System.Collections.Generic.List<string>();
            for (int d = 0; d < 4; d++) { all.AddRange(s.Tonight.SelectMany(p => p.Text.Split(' '))); s.EndNight(); }
            Assert.IsTrue(all.Any(GameData.SecondSet.Contains));
            while (s.Day < Balance.Days - 1) s.EndNight();
            Assert.AreEqual(NoticeKind.MimicDay, s.Today.Kind);
            s.EndNight();
            Assert.AreEqual(NoticeKind.Final, s.Today.Kind);
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
    }
}
