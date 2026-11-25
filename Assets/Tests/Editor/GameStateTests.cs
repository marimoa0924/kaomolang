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
            s.DoReply(0, Emo.Joy, Emo.Neutral); // 1일째 ☆
            Assert.IsFalse(s.Solved("☆"));
            s.EndNight();
            s.DoReply(2, Emo.Joy, Emo.Neutral); // 2일째 ☆
            Assert.IsTrue(s.Solved("☆"));
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
