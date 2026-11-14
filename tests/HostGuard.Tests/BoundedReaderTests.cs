using System;
using NUnit.Framework;
using static HostGuard.Tests.TestUtil;

namespace HostGuard.Tests
{
    public class BoundedReaderTests
    {
        [Test]
        public void RoundTripsEveryType()
        {
            var w = new BoundedWriter();
            w.WriteByte(7);
            w.WriteBool(true);
            w.WriteUInt16(65000);
            w.WriteInt32(-123456);
            w.WriteInt64(long.MinValue);
            w.WriteUInt64(ulong.MaxValue);
            w.WriteSingle(1.5f);
            w.WriteDouble(-2.25);
            w.WriteVarUInt32(uint.MaxValue);
            w.WriteCount(3);
            w.WriteString("héllo 世界 🙂");
            w.WriteBlock(new byte[] { 1, 2, 3 });

            var r = new BoundedReader(w.Written);
            Assert.AreEqual(7, r.ReadByte());
            Assert.IsTrue(r.ReadBool());
            Assert.AreEqual(65000, r.ReadUInt16());
            Assert.AreEqual(-123456, r.ReadInt32());
            Assert.AreEqual(long.MinValue, r.ReadInt64());
            Assert.AreEqual(ulong.MaxValue, r.ReadUInt64());
            Assert.AreEqual(1.5f, r.ReadSingle());
            Assert.AreEqual(-2.25, r.ReadDouble());
            Assert.AreEqual(uint.MaxValue, r.ReadVarUInt32());
            Assert.AreEqual(3, r.ReadCount());
            Assert.AreEqual("héllo 世界 🙂", r.ReadString());
            Assert.AreEqual(new byte[] { 1, 2, 3 }, r.ReadBlock(16).ToArray());
            r.EnsureEnd();
        }

        [Test]
        public void TruncatedDataIsRefused()
        {
            Assert.AreEqual(RejectReason.Truncated, Rejects(new byte[] { 1, 2, 3 }, (ref BoundedReader r) => r.ReadInt32()));
            Assert.AreEqual(RejectReason.Truncated, Rejects(new byte[] { 0x80 }, (ref BoundedReader r) => r.ReadVarUInt32()));
        }

        [Test]
        public void MessageAboveTheLimitIsRefusedUpFront()
        {
            var limits = new MessageLimits(16, 8, 8, 2);
            Assert.AreEqual(RejectReason.TooLarge, Rejects(new byte[17], (ref BoundedReader r) => { }, limits));
        }

        [Test]
        public void BooleansMustBeZeroOrOne()
        {
            Assert.AreEqual(RejectReason.Malformed, Rejects(new byte[] { 2 }, (ref BoundedReader r) => r.ReadBool()));
        }

        [Test]
        public void RangesAreChecked()
        {
            Assert.AreEqual(RejectReason.OutOfRange, Rejects(new byte[] { 9 }, (ref BoundedReader r) => r.ReadByte(0, 5)));
            Assert.AreEqual(RejectReason.OutOfRange,
                Rejects(BitConverter.GetBytes(100), (ref BoundedReader r) => r.ReadInt32(0, 99)));
        }

        [Test]
        public void NaNAndInfinityAreRefused()
        {
            Assert.AreEqual(RejectReason.NotFinite, Rejects(BitConverter.GetBytes(float.NaN), (ref BoundedReader r) => r.ReadSingle()));
            Assert.AreEqual(RejectReason.NotFinite,
                Rejects(BitConverter.GetBytes(double.PositiveInfinity), (ref BoundedReader r) => r.ReadDouble()));
            Assert.AreEqual(RejectReason.NotFinite, Rejects(() => new BoundedWriter().WriteSingle(float.NegativeInfinity)));
        }

        [Test]
        public void VarIntsMustBeShortestAndFitIn32Bits()
        {
            // 0 written in two bytes.
            Assert.AreEqual(RejectReason.Malformed, Rejects(new byte[] { 0x80, 0x00 }, (ref BoundedReader r) => r.ReadVarUInt32()));
            // A fifth byte with bits above 32.
            Assert.AreEqual(RejectReason.Malformed,
                Rejects(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x1F }, (ref BoundedReader r) => r.ReadVarUInt32()));
            // A sixth byte.
            Assert.AreNotEqual(0, Rejects(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 }, (ref BoundedReader r) => r.ReadVarUInt32()));
        }

        [Test]
        public void CountsAreCappedByTheLimitAndTheDataLeft()
        {
            var w = new BoundedWriter();
            w.WriteVarUInt32(5000);
            Assert.AreEqual(RejectReason.CountTooLarge, Rejects(w.Written.ToArray(), (ref BoundedReader r) => r.ReadCount()));

            // 100 elements claimed, but only 3 bytes follow.
            Assert.AreEqual(RejectReason.CountTooLarge, Rejects(new byte[] { 100, 1, 2, 3 }, (ref BoundedReader r) => r.ReadCount()));
            // 2 elements of at least 4 bytes, 6 bytes follow.
            Assert.AreEqual(RejectReason.CountTooLarge,
                Rejects(new byte[] { 2, 0, 0, 0, 0, 0, 0 }, (ref BoundedReader r) => r.ReadCount(10, 4)));
        }

        [Test]
        public void StringsAreLimitedAndStrict()
        {
            var limits = new MessageLimits(1024, 8, 16, 4);
            var w = new BoundedWriter();
            w.WriteString("123456789");
            Assert.AreEqual(RejectReason.StringTooLong, Rejects(w.Written.ToArray(), (ref BoundedReader r) => r.ReadString(), limits));
            Assert.AreEqual(RejectReason.StringTooLong, Rejects(w.Written.ToArray(), (ref BoundedReader r) => r.ReadString(4)));

            // An overlong '/' (0xC0 0xAF) and a lone continuation byte.
            Assert.AreEqual(RejectReason.InvalidUtf8, Rejects(new byte[] { 2, 0xC0, 0xAF }, (ref BoundedReader r) => r.ReadString()));
            Assert.AreEqual(RejectReason.InvalidUtf8, Rejects(new byte[] { 1, 0x80 }, (ref BoundedReader r) => r.ReadString()));
        }

        [TestCase('\0')]
        [TestCase('\u001B')]
        [TestCase('\u007F')]
        [TestCase('\u0085')]
        [TestCase('‮')]
        [TestCase('⁦')]
        [TestCase('\n')]
        public void ControlAndBidiCharactersAreRefused(char c)
        {
            byte[] utf8 = System.Text.Encoding.UTF8.GetBytes("a" + c + "b");
            var data = new byte[utf8.Length + 1];
            data[0] = (byte)utf8.Length;
            utf8.CopyTo(data, 1);
            Assert.AreEqual(RejectReason.ForbiddenCharacter, Rejects(data, (ref BoundedReader r) => r.ReadString()));
            Assert.AreEqual(RejectReason.ForbiddenCharacter, Rejects(() => new BoundedWriter().WriteString("a" + c + "b")));
        }

        [Test]
        public void NewlinesOnlyWhenAllowed()
        {
            var w = new BoundedWriter();
            w.WriteString("line 1\nline 2", allowNewlines: true);
            var r = new BoundedReader(w.Written);
            Assert.AreEqual("line 1\nline 2", r.ReadString(allowNewlines: true));
        }

        [Test]
        public void UnpairedSurrogatesCannotBeWritten()
        {
            Assert.AreEqual(RejectReason.InvalidUtf8, Rejects(() => new BoundedWriter().WriteString("a\uD800b")));
        }

        [Test]
        public void NestingIsLimited()
        {
            var limits = new MessageLimits(64, 8, 8, 2);
            Assert.AreEqual(RejectReason.TooDeep, Rejects(new byte[0], (ref BoundedReader r) =>
            {
                r.EnterNested();
                r.EnterNested();
                r.EnterNested();
            }, limits));
        }

        [Test]
        public void LeftoverBytesAreRefused()
        {
            Assert.AreEqual(RejectReason.TrailingBytes, Rejects(new byte[] { 1, 2 }, (ref BoundedReader r) =>
            {
                r.ReadByte();
                r.EnsureEnd();
            }));
        }

        [Test]
        public void WriterStopsAtTheMessageLimit()
        {
            var w = new BoundedWriter(new MessageLimits(16, 8, 8, 2));
            w.WriteBytes(new byte[12]);
            Assert.AreEqual(RejectReason.TooLarge, Rejects(() => w.WriteInt64(1)));
            w.Reset();
            w.WriteInt64(1);
            Assert.AreEqual(8, w.Length);
        }
    }
}
