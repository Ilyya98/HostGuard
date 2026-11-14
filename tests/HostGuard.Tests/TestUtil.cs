using System;
using NUnit.Framework;

namespace HostGuard.Tests
{
    delegate void ReadAction(ref BoundedReader reader);

    static class TestUtil
    {
        /// <summary>Runs <paramref name="read"/> over <paramref name="data"/> and returns the reason it was refused.</summary>
        public static RejectReason Rejects(byte[] data, ReadAction read, MessageLimits limits = null)
        {
            try
            {
                var r = new BoundedReader(data, limits);
                read(ref r);
            }
            catch (MessageRejectedException e)
            {
                return e.Reason;
            }
            Assert.Fail("Expected the data to be refused.");
            return 0;
        }

        public static RejectReason Rejects(Action write)
        {
            try { write(); }
            catch (MessageRejectedException e) { return e.Reason; }
            Assert.Fail("Expected the write to be refused.");
            return 0;
        }
    }
}
