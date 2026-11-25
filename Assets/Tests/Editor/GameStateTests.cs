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
            Assert.AreEqual(20, GameData.Sym.Count);
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
            // 6개 글이 정답 둘
            Assert.AreEqual(6, GameData.Posts.SelectMany(d => d).Count(p => p.Answers.Length == 2));
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
        public void HeldPostReturnsAsFollowup()
        {
            var s = new GameState(1);
            Assert.IsTrue(s.DoHold(1));
            Assert.IsFalse(s.DoHold(0)); // 밤마다 하나만
            s.DoReply(0, Emo.Joy, Emo.Neutral);
            s.DoReply(2, Emo.Joy, Emo.Neutral);
            s.DoUpload(Emo.Joy, Emo.Joy, null);
            Assert.IsTrue(s.NightDone);
            s.EndNight();
            Assert.AreEqual(4, s.Tonight.Count);
            Assert.AreEqual(GameData.Gloomcat, s.Tonight[3].Author);
            Assert.AreEqual("d2p1b", s.Tonight[1].Id); // 서운한 단골은 다른 글을 쓴다
        }

        [Test]
        public void SymbolSolvesAfterTwoStickers()
        {
            var s = new GameState(1);
            s.DoReply(0, Emo.Joy, Emo.Neutral); // 1일째 별빛러버 글의 ☆
            Assert.AreEqual(1, s.Stickers["☆"]);
            Assert.IsFalse(s.Solved("☆"));
            s.Stickers["☆"]++;
            Assert.IsTrue(s.Solved("☆"));
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
            var s = new GameState(1) { Fat = 95 };
            s.PrepareOuting("park");
            s.DoEncounter(Emo.Joy); // +10 → 100
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
            s.DoReply(0, Emo.Joy, Emo.Neutral); // +3 → 120
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

            s = new GameState(1) { Followers = 6000 };
            Assert.AreEqual(Ending.Conquer, s.FinalEnding());
            s.Sus = 80;
            Assert.AreEqual(Ending.Forgotten, s.FinalEnding());
            s.Followers = 0;
            foreach (var k in GameData.Sym.Keys.Take(12)) s.Stickers[k] = 2;
            Assert.AreEqual(Ending.Absorb, s.FinalEnding());

            s = new GameState(1);
            for (int d = 0; d < Balance.Days; d++) { s.GrantNightPoint(); s.GrantNightPoint(); s.EndNight(); }
            Assert.AreEqual(Ending.Forgotten, s.Ending);
            Assert.AreEqual(Balance.Days, s.Points);
        }
    }
}
