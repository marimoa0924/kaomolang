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
            Assert.AreEqual(20, GameData.Sym.Count);
        }

        [Test]
        public void FirstMeetingTeachesPlaceEmotion()
        {
            var s = new GameState(1);
            var (r, _, _, _, _) = s.DoEncounter("station", Emo.Joy, Emo.Neutral);
            Assert.AreEqual(Result.Success, r);
            Assert.Contains(Emo.Sad, s.Eyes);
            Assert.IsTrue(s.IsNight);
        }

        [Test]
        public void ShakyFaceIsJudgedAsActuallyMade()
        {
            var s = new GameState(3) { Fat = 90 };
            s.Eyes.Add(Emo.Sad);
            var (r, ans, msgs, eye, mouth) = s.DoEncounter("park", Emo.Joy, Emo.Neutral);
            Assert.AreEqual(GameState.Judge(eye, mouth, ans), r);
            Assert.IsTrue(msgs[0].StartsWith("의태가 흔들렸"));
        }

        [Test]
        public void SymbolSolvesAfterTwoStickers()
        {
            var s = new GameState(1);
            s.DoReply(0, Emo.Joy, Emo.Neutral); // 1일째 ☆
            Assert.IsFalse(s.Solved("☆"));
            s.EndNight();
            s.DoReply(1, Emo.Joy, Emo.Neutral); // 2일째 ☆
            Assert.IsTrue(s.Solved("☆"));
        }

        [Test]
        public void EndingRules()
        {
            var s = new GameState(1) { Sus = 95 };
            s.DoReply(0, Emo.Neutral, Emo.Neutral);
            Assert.AreEqual(Ending.None, s.Ending);
            s.Eyes.Add(Emo.Sad);
            s.DoReply(0, Emo.Sad, Emo.Neutral);
            Assert.AreEqual(Ending.Found, s.Ending);

            s = new GameState(1) { Followers = 600 };
            Assert.AreEqual(Ending.Conquer, s.FinalEnding());
            s.Sus = 80;
            Assert.AreEqual(Ending.Normal, s.FinalEnding());
            s.Followers = 0;
            foreach (var k in GameData.Sym.Keys.ToList()) s.Stickers[k] = 2;
            Assert.AreEqual(Ending.Absorb, s.FinalEnding());

            s = new GameState(1);
            for (int d = 0; d < Balance.Days; d++) { s.GrantNightPoint(); s.GrantNightPoint(); s.EndNight(); }
            Assert.AreEqual(Ending.Normal, s.Ending);
            Assert.AreEqual(Balance.Days, s.Points);
        }
    }
}
