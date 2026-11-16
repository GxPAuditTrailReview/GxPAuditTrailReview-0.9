using AuditTrailReview.Engine.Security;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

[TestClass]
public class FtpSettingsResolverTests
{
    private static IConfiguration CreateConfigurationWithEncryptionKey() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

    [TestMethod]
    public void Resolve_NoRow_ReturnsDisabled()
    {
        var result = FtpSettingsResolver.Resolve(null, new ConfigurationBuilder().Build());

        Assert.IsFalse(result.Enabled);
    }

    [TestMethod]
    public void Resolve_NewRowDefaults_IsDisabled()
    {
        var result = FtpSettingsResolver.Resolve(new FtpConfiguration(), new ConfigurationBuilder().Build());

        Assert.IsFalse(result.Enabled);
    }

    [TestMethod]
    public void Resolve_EncryptedPassword_ReturnsDecryptedPassword()
    {
        var configuration = CreateConfigurationWithEncryptionKey();
        var row = new FtpConfiguration
        {
            Enabled = true,
            Host = " ftp.example.com ",
            Username = "svc",
            PasswordEncrypted = ApiKeyProtector.Encrypt("pa55", configuration),
            SystemId = 7
        };

        var result = FtpSettingsResolver.Resolve(row, configuration);

        Assert.IsTrue(result.Enabled);
        Assert.AreEqual("ftp.example.com", result.Host);
        Assert.AreEqual("pa55", result.Password);
        Assert.AreEqual(7, result.SystemId);
    }

    [TestMethod]
    [DataRow(null, FtpEncryptionMode.None, 21)]
    [DataRow(null, FtpEncryptionMode.Explicit, 21)]
    [DataRow(null, FtpEncryptionMode.Implicit, 990)]
    [DataRow(2121, FtpEncryptionMode.Implicit, 2121)]
    public void EffectivePort_ConfiguredOrDefault_ReturnsExpected(int? configured, FtpEncryptionMode mode, int expected)
    {
        Assert.AreEqual(expected, FtpSettingsResolver.EffectivePort(configured, mode));
    }

    [TestMethod]
    public void NormalizeThumbprint_SeparatorsAndCase_AreStripped()
    {
        Assert.AreEqual("AABBCC", FtpSettingsResolver.NormalizeThumbprint("aa:bb cc"));
        Assert.IsNull(FtpSettingsResolver.NormalizeThumbprint("  "));
    }

    [TestMethod]
    public void IsCertificateAcceptable_ValidChain_Accepted()
    {
        Assert.IsTrue(FtpSettingsResolver.IsCertificateAcceptable(true, null, true, "AA", "BB"));
    }

    [TestMethod]
    public void IsCertificateAcceptable_InvalidChainValidationOn_Rejected()
    {
        Assert.IsFalse(FtpSettingsResolver.IsCertificateAcceptable(true, null, false, "AA", "BB"));
    }

    [TestMethod]
    public void IsCertificateAcceptable_InvalidChainValidationOff_Accepted()
    {
        Assert.IsTrue(FtpSettingsResolver.IsCertificateAcceptable(false, null, false, "AA", "BB"));
    }

    [TestMethod]
    public void IsCertificateAcceptable_PinnedThumbprintMatches_AcceptedEvenWithBadChain()
    {
        Assert.IsTrue(FtpSettingsResolver.IsCertificateAcceptable(true, "aa:bb", false, "AABB", "CCDD"));
    }

    [TestMethod]
    public void IsCertificateAcceptable_PinnedThumbprintMismatch_RejectedEvenWithValidationOff()
    {
        Assert.IsFalse(FtpSettingsResolver.IsCertificateAcceptable(false, "1234", true, "AABB", "CCDD"));
    }
}
