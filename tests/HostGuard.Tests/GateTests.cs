using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace HostGuard.Tests
{
    public class GateTests
    {
        const ushort Move = 1;
        const ushort Say = 2;

        readonly struct MoveMsg
        {
            public readonly int Actor, Dx, Dy;
            public MoveMsg(int actor, int dx, int dy) { Actor = actor; Dx = dx; Dy = dy; }
        }

        sealed class World
        {
            public int SenderActor;
            public readonly List<string> Log = new List<string>();
        }

        static MessageRegistry<World> MakeRegistry()
        {
            var reg = new MessageRegistry<World>();
            reg.Register(Move, "Move", 16,
                (ref BoundedReader r) => new MoveMsg(r.ReadInt32(0, 1_000_000), r.ReadInt32(-1, 1), r.ReadInt32(-1, 1)),
                (m, w) => w.Log.Add($"move {m.Actor} {m.Dx},{m.Dy}"),
                (m, w) => m.Actor == w.SenderActor);
            reg.Register(Say, "Say", 300,
                (ref BoundedReader r) => r.ReadString(200),
                (s, w) => w.Log.Add("say " + s));
            reg.Seal();
            return reg;
        }

        static byte[] Frame(ushort type, Action<BoundedWriter> body)
        {
            var payload = new BoundedWriter();
            body(payload);
            var frame = new BoundedWriter();
            MessageRegistry<World>.WriteFrame(frame, type, payload);
            return frame.Written.ToArray();
        }

        static byte[] MoveFrame(int actor, int dx, int dy) => Frame(Move, w =>
        {
            w.WriteInt32(actor);
            w.WriteInt32(dx);
            w.WriteInt32(dy);
        });

        static RejectReason Dispatch(MessageRegistry<World> reg, byte[] frame, World world)
        {
            try { reg.Dispatch(frame, world); }
            catch (MessageRejectedException e) { return e.Reason; }
            Assert.Fail("Expected a refusal.");
            return 0;
        }

        [Test]
        public void ValidFramesAreApplied()
        {
            var reg = MakeRegistry();
            var world = new World { SenderActor = 4 };
            Assert.AreEqual(Move, reg.Dispatch(MoveFrame(4, 1, -1), world));
            Assert.AreEqual(Say, reg.Dispatch(Frame(Say, w => w.WriteString("hi")), world));
            CollectionAssert.AreEqual(new[] { "move 4 1,-1", "say hi" }, world.Log);
        }

        [Test]
        public void UnknownTypesAreRefused()
        {
            var reg = MakeRegistry();
            Assert.AreEqual(RejectReason.UnknownMessageType, Dispatch(reg, Frame(99, w => { }), new World()));
        }

        [Test]
        public void LengthMustMatchExactly()
        {
            var reg = MakeRegistry();
            var frame = MoveFrame(4, 0, 0);
            var longer = new byte[frame.Length + 1];
            frame.CopyTo(longer, 0);
            Assert.AreEqual(RejectReason.Malformed, Dispatch(reg, longer, new World()));
            Assert.AreEqual(RejectReason.Malformed, Dispatch(reg, frame.AsSpan(0, frame.Length - 1).ToArray(), new World()));
        }

        [Test]
        public void PerTypePayloadCapIsChecked()
        {
            var reg = MakeRegistry();
            var frame = Frame(Move, w => w.WriteBytes(new byte[17]));
            Assert.AreEqual(RejectReason.TooLarge, Dispatch(reg, frame, new World()));
        }

        [Test]
        public void PayloadMustBeReadCompletely()
        {
            var reg = MakeRegistry();
            var frame = Frame(Move, w =>
            {
                w.WriteInt32(4);
                w.WriteInt32(0);
                w.WriteInt32(0);
                w.WriteByte(0);
            });
            var world = new World { SenderActor = 4 };
            Assert.AreEqual(RejectReason.TrailingBytes, Dispatch(reg, frame, world));
            Assert.IsEmpty(world.Log);
        }

        [Test]
        public void AllowRuleRunsBeforeApply()
        {
            var reg = MakeRegistry();
            var world = new World { SenderActor = 4 };
            Assert.AreEqual(RejectReason.NotAllowed, Dispatch(reg, MoveFrame(5, 1, 0), world));
            Assert.AreEqual(RejectReason.OutOfRange, Dispatch(reg, MoveFrame(4, 2, 0), world));
            Assert.IsEmpty(world.Log);
        }

        [Test]
        public void RegistryMustBeSealedAndThenStaysFixed()
        {
            var reg = new MessageRegistry<World>();
            reg.Register(Say, "Say", 10, (ref BoundedReader r) => r.ReadByte(), (b, w) => { });
            Assert.Throws<InvalidOperationException>(() => reg.Dispatch(new byte[] { 2, 0, 0 }, new World()));
            Assert.Throws<ArgumentException>(() => reg.Register(Say, "Again", 10, (ref BoundedReader r) => r.ReadByte(), (b, w) => { }));
            reg.Seal();
            Assert.Throws<InvalidOperationException>(() => reg.Register(Move, "Late", 10, (ref BoundedReader r) => r.ReadByte(), (b, w) => { }));
        }

        [Test]
        public void RateLimiterAllowsABurstThenRefills()
        {
            var limiter = new RateLimiter(burst: 3, perSecond: 2);
            Assert.IsTrue(limiter.TryTake(1, 0));
            Assert.IsTrue(limiter.TryTake(1, 0));
            Assert.IsTrue(limiter.TryTake(1, 0));
            Assert.IsFalse(limiter.TryTake(1, 0));
            Assert.IsTrue(limiter.TryTake(2, 0), "Senders have separate buckets.");
            Assert.IsFalse(limiter.TryTake(1, 499));
            Assert.IsTrue(limiter.TryTake(1, 500));
            Assert.IsFalse(limiter.TryTake(1, 400), "A clock going backwards refills nothing.");
            Assert.IsTrue(limiter.TryTake(1, long.MaxValue), "Huge gaps do not overflow.");
        }

        [Test]
        public void GateStrikesAndDisconnects()
        {
            var gate = new HostGate<World>(MakeRegistry(), new RateLimiter(100, 100), strikeLimit: 3);
            var world = new World { SenderActor = 4 };
            Assert.IsTrue(gate.Receive(7, MoveFrame(4, 1, 0), 0, world).Accepted);

            var bad = new byte[] { 0xFF, 0xFF, 0 };
            var r1 = gate.Receive(7, bad, 0, world);
            Assert.AreEqual(GateVerdict.Rejected, r1.Verdict);
            Assert.AreEqual(RejectReason.UnknownMessageType, r1.Reason);
            Assert.AreEqual(GateVerdict.Rejected, gate.Receive(7, MoveFrame(5, 1, 0), 0, world).Verdict);
            Assert.AreEqual(GateVerdict.Disconnect, gate.Receive(7, bad, 0, world).Verdict);
            Assert.AreEqual(GateVerdict.Disconnect, gate.Receive(7, MoveFrame(4, 1, 0), 0, world).Verdict, "Stays refused.");
            Assert.AreEqual(1, world.Log.Count);

            gate.Forget(7);
            Assert.IsTrue(gate.Receive(7, MoveFrame(4, 1, 0), 0, world).Accepted);
        }

        [Test]
        public void GateRateLimitsFlooding()
        {
            var gate = new HostGate<World>(MakeRegistry(), new RateLimiter(2, 1), strikeLimit: 10);
            var world = new World { SenderActor = 4 };
            var frame = MoveFrame(4, 0, 1);
            Assert.IsTrue(gate.Receive(1, frame, 0, world).Accepted);
            Assert.IsTrue(gate.Receive(1, frame, 0, world).Accepted);
            var flooded = gate.Receive(1, frame, 0, world);
            Assert.AreEqual(RejectReason.RateLimited, flooded.Reason);
            Assert.AreEqual(1, gate.StrikesOf(1));
        }

        [Test]
        public void ApplyBugsAreNotSwallowed()
        {
            var reg = new MessageRegistry<World>();
            reg.Register<byte>(1, "Boom", 4, (ref BoundedReader r) => r.ReadByte(), (b, w) => throw new InvalidOperationException("bug"));
            reg.Seal();
            var gate = new HostGate<World>(reg, new RateLimiter(10, 10));
            Assert.Throws<InvalidOperationException>(() => gate.Receive(1, new byte[] { 1, 0, 1, 5 }, 0, new World()));
        }

        /// <summary>
        /// Seeded fuzzing: random frames, and valid frames with random byte flips, truncations and insertions. The gate
        /// must never throw anything but refusals, and never apply a frame the allow rule refuses.
        /// </summary>
        [Test]
        public void FuzzedFramesNeverEscapeTheGate([Values(1, 2, 3)] int seed)
        {
            var rng = new Random(seed); // test-only randomness; seeded so failures reproduce
            var reg = MakeRegistry();
            var gate = new HostGate<World>(reg, new RateLimiter(int.MaxValue / 1000, int.MaxValue / 1000), strikeLimit: int.MaxValue);
            var world = new World { SenderActor = 4 };
            var valid = new[] { MoveFrame(4, 1, 0), MoveFrame(5, 0, 1), Frame(Say, w => w.WriteString("hello there")) };

            for (int i = 0; i < 20000; i++)
            {
                byte[] frame;
                if (i % 4 == 0)
                {
                    frame = new byte[rng.Next(0, 40)];
                    rng.NextBytes(frame);
                }
                else
                {
                    var source = valid[rng.Next(valid.Length)];
                    var list = new List<byte>(source);
                    int edits = rng.Next(1, 4);
                    for (int e = 0; e < edits && list.Count > 0; e++)
                    {
                        int at = rng.Next(list.Count);
                        switch (rng.Next(3))
                        {
                            case 0: list[at] = (byte)rng.Next(256); break;
                            case 1: list.RemoveRange(at, list.Count - at); break;
                            default: list.Insert(at, (byte)rng.Next(256)); break;
                        }
                    }
                    frame = list.ToArray();
                }

                int before = world.Log.Count;
                gate.Receive(1, frame, i, world);
                if (world.Log.Count > before)
                    StringAssert.DoesNotStartWith("move 5", world.Log[world.Log.Count - 1]);
            }
        }
    }
}
