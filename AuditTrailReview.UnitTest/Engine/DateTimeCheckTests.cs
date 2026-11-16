using System;
using AuditTrailReview.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine
{
    [TestClass]
    public class DateTimeCheckTests
    {
        [TestMethod]
        public void IsUtcDate_String_ReturnsTrue_ForZSuffix()
        {
            var dateString = "2021-01-01T00:00:00Z";

            var result = DateTimeCheck.IsUtcDate(dateString);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsUtcDate_String_ReturnsTrue_ForZeroOffset()
        {
            var dateString = "2021-01-01T00:00:00+00:00";

            var result = DateTimeCheck.IsUtcDate(dateString);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void IsUtcDate_String_ReturnsFalse_ForNonZeroOffset()
        {
            var dateString = "2021-01-01T02:00:00+02:00";

            var result = DateTimeCheck.IsUtcDate(dateString);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public void IsUtcDate_String_ReturnsFalse_ForInvalidString()
        {
            var dateString = "not a date";

            var result = DateTimeCheck.IsUtcDate(dateString);

            Assert.IsFalse(result);
        }
    }
}