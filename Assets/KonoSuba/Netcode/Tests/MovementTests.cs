using System.IO;
using NUnit.Framework;
using AnimLab.Netcode.Sim;

namespace AnimLab.Netcode.Tests
{
    public class MovementTests
    {
        [Test] public void FixedMathHasIntegerAccuracy()
        {
            Assert.AreEqual(Fix.Ratio(-3,2), Fix.Ratio(-3,4) * 2);
            Assert.AreEqual(Fix.Ratio(9,4), Fix.Ratio(3,2) * Fix.Ratio(3,2));
            Assert.AreEqual((Fix)3, Fix.Sqrt(9));
            Assert.Less(Fix.Abs(Fix.Sin(Fix.Pi / 2) - 1).Raw, 16);
            Assert.Less(Fix.Abs(Fix.Atan2(1, 0) - Fix.Pi / 2).Raw, 2);
            Assert.Less(Fix.Abs(FixVec3.RotateY(FixVec3.Forward, Fix.Pi / 2).X - 1).Raw, 16);
        }
        [Test] public void InputsRoundTripAllMagnitudesAndButtons()
        {
            for (byte m = 0; m < 16; m++) for (byte b = 0; b < 32; b++)
            {
                var p = new PlayerInput { Magnitude = m, MoveAngle = 231, LookYaw = 1023, Buttons = (Buttons)b, Targeted = (b & 1) != 0 };
                using (var stream = new MemoryStream()) {
                    p.Write(new BinaryWriter(stream)); Assert.AreEqual(PlayerInput.WireSize, stream.Length);
                    stream.Position = 0; Assert.AreEqual(p, PlayerInput.Read(new BinaryReader(stream)));
                }
            }
        }
        [Test] public void SameInputsMatchEveryTickForThreeMinutes()
        {
            var a = MatchState.Create(); var b = a; var ea = new EventBuffer(); var eb = new EventBuffer();
            bool roll = false, jump = false, longJump = false, plunge = false;
            for (int i = 0; i < 10800; i++)
            {
                var p = BotInputs.Movement(i, 0); var q = BotInputs.Movement(i, 1);
                World.Step(ref a, p, q, ea); World.Step(ref b, p, q, eb);
                Assert.AreEqual(StateCodec.Checksum(a), StateCodec.Checksum(b), "tick " + i);
                roll |= a.P0.Motion == Motion.Roll; jump |= a.P0.Motion == Motion.Air;
                longJump |= a.P0.LongJump; plunge |= a.P0.Motion == Motion.Plunge;
                Assert.GreaterOrEqual(a.P0.Position.Y.Raw, 0);
                Assert.LessOrEqual(a.P0.Position.Flat.Length.Raw, World.ArenaRadius.Raw);
            }
            Assert.IsTrue(roll && jump && longJump && plunge);
        }
        [Test] public void SnapshotRoundTripAndRestoreReplay()
        {
            var s = MatchState.Create(); var e = new EventBuffer();
            for (int i = 0; i < 160; i++) World.Step(ref s, BotInputs.Movement(i,0), BotInputs.Movement(i,1), e);
            var restored = StateCodec.Decode(StateCodec.Encode(s));
            Assert.AreEqual(StateCodec.Checksum(s), StateCodec.Checksum(restored));
            for (int i = 160; i < 480; i++) {
                World.Step(ref s, BotInputs.Movement(i,0), BotInputs.Movement(i,1), e);
                World.Step(ref restored, BotInputs.Movement(i,0), BotInputs.Movement(i,1), e);
                Assert.AreEqual(StateCodec.Checksum(s), StateCodec.Checksum(restored));
            }
        }
        [Test] public void RollQueuesLongJumpAndLocksCooldown()
        {
            var s = MatchState.Create(); var e = new EventBuffer();
            World.Step(ref s, new PlayerInput { Magnitude = 15, MoveAngle = 64, Buttons = Buttons.Roll }, default, e);
            Assert.AreEqual(Motion.Roll, s.P0.Motion);
            for (int i = 0; i < 29; i++) World.Step(ref s, new PlayerInput { Buttons = Buttons.Jump }, default, e);
            Assert.IsTrue(s.P0.LongJump); Assert.AreEqual(Motion.Air, s.P0.Motion);
            Assert.Greater(s.P0.Velocity.Length.Raw, ((Fix)7).Raw);
        }
    }
}
