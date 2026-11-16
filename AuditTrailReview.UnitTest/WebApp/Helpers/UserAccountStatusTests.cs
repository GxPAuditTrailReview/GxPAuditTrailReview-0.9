using AuditTrailReview.Database;
using AuditTrailReview.WebApp.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Helpers;

/// <summary>Unit tests for <see cref="UserAccountStatus"/>.</summary>
[TestClass]
public class UserAccountStatusTests
{
    [TestMethod]
    public void IsDeactivated_LockoutEndInFuture_ReturnsTrue()
    {
        var user = new ApplicationUser { LockoutEnabled = true, LockoutEnd = DateTimeOffset.MaxValue };

        Assert.IsTrue(UserAccountStatus.IsDeactivated(user));
    }

    [TestMethod]
    public void IsDeactivated_LockoutEndInPast_ReturnsFalse()
    {
        var user = new ApplicationUser { LockoutEnabled = true, LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-5) };

        Assert.IsFalse(UserAccountStatus.IsDeactivated(user));
    }

    [TestMethod]
    public void IsDeactivated_LockoutEnabledFalse_ReturnsFalse()
    {
        var user = new ApplicationUser { LockoutEnabled = false, LockoutEnd = DateTimeOffset.MaxValue };

        Assert.IsFalse(UserAccountStatus.IsDeactivated(user));
    }

    [TestMethod]
    public void IsDeactivated_NoLockoutEnd_ReturnsFalse()
    {
        var user = new ApplicationUser { LockoutEnabled = true, LockoutEnd = null };

        Assert.IsFalse(UserAccountStatus.IsDeactivated(user));
    }
}
