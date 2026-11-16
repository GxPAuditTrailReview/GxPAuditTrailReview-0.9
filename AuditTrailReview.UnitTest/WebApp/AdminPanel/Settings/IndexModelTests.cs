using AuditTrailReview.Database;
using AuditTrailReview.Engine.AI;
using AuditTrailReview.Engine.Security;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.AdminPanel.Settings;
using AuditTrailReview.WebApp.Services;
using ClosedXML.Excel;
using AuditTrailReview.Engine.AI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.Settings;

/// <summary>
/// Unit tests for the Audit Trail tab's four sub-tabs on <see cref="IndexModel"/>
/// (Pages/AdminPanel/Settings/Index.cshtml.cs) -- the page that consolidates what used to be four
/// separate single-purpose edit pages (Edit/EditGeneral/EditEmail/EditAI, now deleted) into one
/// tabbed page. Each sub-tab is filtered to its own Object value ("SecuritySettings"/
/// "GeneralSettings"/"EmailSettings"/"AiSettings") -- ports the four former *ModelTests classes'
/// tests 1:1 against the consolidated model's per-group properties. Security/General's
/// OnPostSave*Async handlers still aren't tested here (no test file covered them before the
/// consolidation either); OnPostSaveAiAsync and (since EmailSettings gained the same
/// ApiKeyProtector-backed SMTP password encryption) OnPostSaveEmailAsync now are -- see the "AI
/// tab save" and "Email tab save" regions below -- since each needed real coverage
/// (ciphertext-only storage, and that the secret never reaches dbo.AuditTrail).
/// </summary>
[TestClass]
public class IndexModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IndexModel CreateModel(
        ApplicationDbContext ctx,
        IConfiguration? configuration = null,
        SecurityOptionsState? securityOptionsState = null)
    {
        var model = new IndexModel(
            ctx,
            new AuditTrailService(ctx),
            configuration ?? new ConfigurationBuilder().Build(),
            securityOptionsState ?? new SecurityOptionsState(new SecuritySettings()),
            PageModelTestHelpers.CreateDateTimeDisplayService(ctx));
        PageModelTestHelpers.InitializePageContext(model);
        return model;
    }

    /// <summary>A valid base64 32-byte Security:EncryptionKey -- needed by any test that calls
    /// OnPostSaveAiAsync with a non-blank ApiKeyInput, or OnPostSaveEmailAsync with a non-blank
    /// SmtpPasswordInput (ApiKeyProtector.Encrypt throws without one). The default CreateModel()
    /// config is deliberately left without this key, matching the GET-only tests above that
    /// never touch ApiKeyProtector.</summary>
    private static IConfiguration CreateConfigurationWithEncryptionKey() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

    // ---- Security sub-tab (ported from EditModelTests) -------------------------------------

    [TestMethod]
    public async Task OnGetAsync_SecuritySettingsAuditEntry_Included()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SecuritySettingsAuditEntry_Included));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "SecuritySettings", Field = "PasswordRequiredLength", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailSecurityEntries.Count);
        Assert.AreEqual("PasswordRequiredLength", model.AuditTrailSecurityEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInSecurity()
    {
        // EmailSettings/GeneralSettings/AiSettings/EngineSettings/Rule/SystemType entries all
        // share ObjectType == "ConfigPanel" -- only Object == "SecuritySettings" belongs here.
        await using var ctx = CreateContext(nameof(OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInSecurity));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EmailSettings", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.AuditTrailSecurityEntries.Count);
        Assert.AreEqual(0, model.AuditTrailSecurityTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_SecurityAuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SecurityAuditTrailSearchTerm_FiltersToMatchingEntries));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "SecuritySettings", Field = "PasswordRequiredLength", Timestamp = DateTime.UtcNow, Operation = "Update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "SecuritySettings", Field = "LockoutDurationMinutes", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSecuritySearchTerm = "Lockout";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailSecurityEntries.Count);
        Assert.AreEqual("LockoutDurationMinutes", model.AuditTrailSecurityEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_SecurityAuditTrailPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SecurityAuditTrailPaging_ReturnsRequestedPage));
        for (var i = 0; i < IndexModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "SecuritySettings", Field = $"Field{i}", Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSecurityPage = 2;

        await model.OnGetAsync();

        Assert.AreEqual(2, model.AuditTrailSecurityTotalPages);
        Assert.AreEqual(1, model.AuditTrailSecurityEntries.Count);
    }

    [TestMethod]
    public async Task OnPostSaveSecurityAsync_ValidSettings_ReloadsSecurityOptionsStateImmediately()
    {
        // Saving the Security tab must push the new values into SecurityOptionsState in the same
        // request that saves them to the database -- that's what lets ASP.NET Core Identity pick
        // up the change on the very next request with no application restart, replacing the old
        // PendingRestart/"restart the application" behavior.
        await using var ctx = CreateContext(nameof(OnPostSaveSecurityAsync_ValidSettings_ReloadsSecurityOptionsStateImmediately));
        var securityOptionsState = new SecurityOptionsState(new SecuritySettings { PasswordRequiredLength = 8 });
        var model = CreateModel(ctx, securityOptionsState: securityOptionsState);
        model.SecuritySettings = new SecuritySettings
        {
            PasswordRequiredLength = 12,
            PasswordRequireDigit = true,
            PasswordRequireLowercase = true,
            PasswordRequireUppercase = true,
            PasswordRequireNonAlphanumeric = false,
            PasswordRequiredUniqueChars = 4,
            LockoutMaxFailedAccessAttempts = 3,
            LockoutDurationMinutes = 15,
            LockoutAllowedForNewUsers = true,
            RequireConfirmedAccount = true
        };

        var result = await model.OnPostSaveSecurityAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        Assert.AreEqual(12, securityOptionsState.Current.PasswordRequiredLength);
        Assert.AreEqual(3, securityOptionsState.Current.LockoutMaxFailedAccessAttempts);
        Assert.IsTrue(securityOptionsState.Current.RequireConfirmedAccount);
    }

    [TestMethod]
    public async Task OnPostSaveSecurityAsync_SessionSettings_SavedAndReloaded()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSecurityAsync_SessionSettings_SavedAndReloaded));
        ctx.SecuritySettings.Add(new SecuritySettings());
        await ctx.SaveChangesAsync();
        var state = new SecurityOptionsState(new SecuritySettings());
        var model = CreateModel(ctx, securityOptionsState: state);
        model.SecuritySettings = new SecuritySettings { IdleTimeoutMinutes = 30, AbsoluteSessionHours = 8, SessionWarningMinutes = 3 };

        await model.OnPostSaveSecurityAsync();

        var saved = ctx.SecuritySettings.Single();
        Assert.AreEqual(30, saved.IdleTimeoutMinutes);
        Assert.AreEqual(8, saved.AbsoluteSessionHours);
        Assert.AreEqual(3, saved.SessionWarningMinutes);
        Assert.AreEqual(30, state.Current.IdleTimeoutMinutes);
        Assert.IsTrue(ctx.AuditTrail.Any(a => a.Object == nameof(SecuritySettings) && a.Field == nameof(SecuritySettings.IdleTimeoutMinutes)));
    }

    [TestMethod]
    public async Task OnPostSaveSecurityAsync_WarningNotShorterThanIdle_RejectedWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSecurityAsync_WarningNotShorterThanIdle_RejectedWithoutSaving));
        var state = new SecurityOptionsState(new SecuritySettings());
        var model = CreateModel(ctx, securityOptionsState: state);
        model.SecuritySettings = new SecuritySettings { IdleTimeoutMinutes = 5, SessionWarningMinutes = 5 };

        await model.OnPostSaveSecurityAsync();

        Assert.AreEqual(0, ctx.SecuritySettings.Count());
        Assert.AreEqual(20, state.Current.IdleTimeoutMinutes);
    }

    [TestMethod]
    public async Task OnPostSaveSecurityAsync_IdleLongerThanAbsoluteMaximum_RejectedWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSecurityAsync_IdleLongerThanAbsoluteMaximum_RejectedWithoutSaving));
        var model = CreateModel(ctx);
        model.SecuritySettings = new SecuritySettings { IdleTimeoutMinutes = 120, AbsoluteSessionHours = 1, SessionWarningMinutes = 2 };

        await model.OnPostSaveSecurityAsync();

        Assert.AreEqual(0, ctx.SecuritySettings.Count());
    }

    // ---- General sub-tab (ported from EditGeneralModelTests) -------------------------------

    [TestMethod]
    public async Task OnGetAsync_GeneralSettingsAuditEntry_Included()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_GeneralSettingsAuditEntry_Included));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "GeneralSettings", Field = "DisplayTimeZone", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailGeneralEntries.Count);
        Assert.AreEqual("DisplayTimeZone", model.AuditTrailGeneralEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInGeneral()
    {
        // RuleBasedReviewEngine's own EngineSettings changes also log under ObjectType ==
        // "ConfigPanel", Object == "EngineSettings" -- not to be confused with this page's own
        // GeneralSettings row despite the similar name.
        await using var ctx = CreateContext(nameof(OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInGeneral));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.AuditTrailGeneralEntries.Count);
        Assert.AreEqual(0, model.AuditTrailGeneralTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_GeneralAuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_GeneralAuditTrailSearchTerm_FiltersToMatchingEntries));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "GeneralSettings", Field = "DisplayTimeZone", Timestamp = DateTime.UtcNow, Operation = "Update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "GeneralSettings", Field = "DateTimeFormat", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailGeneralSearchTerm = "DateTimeFormat";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailGeneralEntries.Count);
        Assert.AreEqual("DateTimeFormat", model.AuditTrailGeneralEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_GeneralAuditTrailPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_GeneralAuditTrailPaging_ReturnsRequestedPage));
        for (var i = 0; i < IndexModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "GeneralSettings", Field = $"Field{i}", Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailGeneralPage = 2;

        await model.OnGetAsync();

        Assert.AreEqual(2, model.AuditTrailGeneralTotalPages);
        Assert.AreEqual(1, model.AuditTrailGeneralEntries.Count);
    }

    // ---- Email sub-tab (ported from EditEmailModelTests) -----------------------------------

    [TestMethod]
    public async Task OnGetAsync_EmailSettingsAuditEntry_Included()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_EmailSettingsAuditEntry_Included));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EmailSettings", Field = "Host", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailEmailEntries.Count);
        Assert.AreEqual("Host", model.AuditTrailEmailEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInEmail()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInEmail));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "SecuritySettings", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.AuditTrailEmailEntries.Count);
        Assert.AreEqual(0, model.AuditTrailEmailTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_EmailAuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_EmailAuditTrailSearchTerm_FiltersToMatchingEntries));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EmailSettings", Field = "Host", Timestamp = DateTime.UtcNow, Operation = "Update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EmailSettings", Field = "Port", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailEmailSearchTerm = "Port";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailEmailEntries.Count);
        Assert.AreEqual("Port", model.AuditTrailEmailEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_EmailAuditTrailPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_EmailAuditTrailPaging_ReturnsRequestedPage));
        for (var i = 0; i < IndexModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EmailSettings", Field = $"Field{i}", Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailEmailPage = 2;

        await model.OnGetAsync();

        Assert.AreEqual(2, model.AuditTrailEmailTotalPages);
        Assert.AreEqual(1, model.AuditTrailEmailEntries.Count);
    }

    // ---- FTP tab (OnGetAsync / OnPostSaveFtpAsync) -- master switch, password encryption ----

    [TestMethod]
    public async Task OnGetAsync_NoFtpRow_FtpIngestionIsOffByDefault()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NoFtpRow_FtpIngestionIsOffByDefault));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsFalse(model.FtpSettings.Enabled);
    }

    [TestMethod]
    public async Task OnPostSaveFtpAsync_EnabledWithoutHost_RejectedAndNothingSaved()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveFtpAsync_EnabledWithoutHost_RejectedAndNothingSaved));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.FtpSettings = new FtpConfiguration { Enabled = true };

        await model.OnPostSaveFtpAsync();

        Assert.AreEqual(0, await ctx.FtpConfiguration.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSaveFtpAsync_DisabledWithEmptyFields_Saved()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveFtpAsync_DisabledWithEmptyFields_Saved));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.FtpSettings = new FtpConfiguration { Enabled = false };

        await model.OnPostSaveFtpAsync();

        var saved = await ctx.FtpConfiguration.AsNoTracking().SingleAsync();
        Assert.IsFalse(saved.Enabled);
    }

    [TestMethod]
    public async Task OnPostSaveFtpAsync_PasswordProvided_StoresEncryptedAndKeepsItOutOfAuditTrail()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveFtpAsync_PasswordProvided_StoresEncryptedAndKeepsItOutOfAuditTrail));
        var configuration = CreateConfigurationWithEncryptionKey();
        ctx.SystemATR.Add(new SystemATRInfo { Id = 1, SystemPrefixID = "SYS", SystemName = "System one" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, configuration);
        model.FtpSettings = new FtpConfiguration
        {
            Enabled = true, Host = "ftp.example.com", Username = "svc", SystemId = 1,
            EncryptionMode = FtpEncryptionMode.Explicit
        };
        model.FtpPasswordInput = "s3cret-ftp-password";

        await model.OnPostSaveFtpAsync();

        var saved = await ctx.FtpConfiguration.AsNoTracking().SingleAsync();
        Assert.IsTrue(saved.Enabled);
        Assert.IsFalse(saved.PasswordEncrypted!.Contains("s3cret-ftp-password"));
        Assert.AreEqual("s3cret-ftp-password", ApiKeyProtector.Decrypt(saved.PasswordEncrypted, configuration));

        var auditValues = await ctx.AuditTrail.Select(a => a.NewValue).ToListAsync();
        Assert.IsFalse(auditValues.Any(v => v != null && v.Contains("s3cret-ftp-password")));
        Assert.IsFalse(await ctx.AuditTrail.AnyAsync(a => a.Field == "PasswordEncrypted"));
    }

    [TestMethod]
    public async Task OnPostSaveFtpAsync_PasswordBlank_PreservesExistingPassword()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveFtpAsync_PasswordBlank_PreservesExistingPassword));
        var configuration = CreateConfigurationWithEncryptionKey();
        var ciphertext = ApiKeyProtector.Encrypt("original", configuration);
        ctx.FtpConfiguration.Add(new FtpConfiguration { PasswordEncrypted = ciphertext });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, configuration);
        model.FtpSettings = new FtpConfiguration { Enabled = false };

        await model.OnPostSaveFtpAsync();

        Assert.AreEqual(ciphertext, (await ctx.FtpConfiguration.AsNoTracking().SingleAsync()).PasswordEncrypted);
    }

    [TestMethod]
    public async Task OnPostSaveFtpAsync_EnabledToggled_AppearsInAuditEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveFtpAsync_EnabledToggled_AppearsInAuditEntry));
        ctx.FtpConfiguration.Add(new FtpConfiguration { Enabled = true });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.FtpSettings = new FtpConfiguration { Enabled = false };

        await model.OnPostSaveFtpAsync();

        var entry = await ctx.AuditTrail.SingleAsync(a => a.Object == "FtpConfiguration" && a.Field == "Enabled");
        Assert.AreEqual("True", entry.OldValue);
        Assert.AreEqual("False", entry.NewValue);
    }

    // ---- Email tab save (OnPostSaveEmailAsync) -- SMTP password encryption, audit safety ----

    [TestMethod]
    public async Task OnPostSaveEmailAsync_PasswordProvided_StoresEncryptedNotPlaintext()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveEmailAsync_PasswordProvided_StoresEncryptedNotPlaintext));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com" };
        model.SmtpPasswordInput = "s3cret-smtp-password";

        await model.OnPostSaveEmailAsync();

        var saved = await ctx.EmailSettings.AsNoTracking().FirstAsync();
        Assert.IsNotNull(saved.PasswordEncrypted);
        Assert.IsFalse(saved.PasswordEncrypted!.Contains("s3cret-smtp-password"));
    }

    [TestMethod]
    public async Task OnPostSaveEmailAsync_PasswordInputBlank_PreservesExistingEncryptedPassword()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveEmailAsync_PasswordInputBlank_PreservesExistingEncryptedPassword));
        var configuration = CreateConfigurationWithEncryptionKey();
        var existingCiphertext = ApiKeyProtector.Encrypt("original-password", configuration);
        ctx.EmailSettings.Add(new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com", PasswordEncrypted = existingCiphertext });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, configuration);
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com" };
        model.SmtpPasswordInput = null;

        await model.OnPostSaveEmailAsync();

        var saved = await ctx.EmailSettings.AsNoTracking().FirstAsync();
        Assert.AreEqual(existingCiphertext, saved.PasswordEncrypted);
    }

    [TestMethod]
    public async Task OnPostSaveEmailAsync_ClearSmtpPasswordChecked_RemovesStoredPassword()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveEmailAsync_ClearSmtpPasswordChecked_RemovesStoredPassword));
        var configuration = CreateConfigurationWithEncryptionKey();
        ctx.EmailSettings.Add(new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com", PasswordEncrypted = ApiKeyProtector.Encrypt("original-password", configuration) });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, configuration);
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com" };
        model.ClearSmtpPassword = true;

        await model.OnPostSaveEmailAsync();

        var saved = await ctx.EmailSettings.AsNoTracking().FirstAsync();
        Assert.IsNull(saved.PasswordEncrypted);
    }

    [TestMethod]
    public async Task OnPostSaveEmailAsync_UserNameChanged_AppearsInAuditEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveEmailAsync_UserNameChanged_AppearsInAuditEntry));
        ctx.EmailSettings.Add(new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com", UserName = "old-user" });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx);
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com", UserName = "new-user" };

        await model.OnPostSaveEmailAsync();

        var userNameEntry = await ctx.AuditTrail.SingleAsync(a => a.Object == "EmailSettings" && a.Field == "UserName");
        Assert.AreEqual("old-user", userNameEntry.OldValue);
        Assert.AreEqual("new-user", userNameEntry.NewValue);
    }

    [TestMethod]
    public async Task OnPostSaveEmailAsync_PasswordChangedOnlyNothingElseChanged_StillProducesAuditEntry()
    {
        // Same reasoning as OnPostSaveAiAsync_ApiKeyChangedOnlyNothingElseChanged_StillProducesAuditEntry:
        // without EmailSettingsAuditSnapshot.PasswordFingerprint, a pure password rotation with
        // no other field touched would otherwise leave zero trace in dbo.AuditTrail.
        await using var ctx = CreateContext(nameof(OnPostSaveEmailAsync_PasswordChangedOnlyNothingElseChanged_StillProducesAuditEntry));
        ctx.EmailSettings.Add(new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com" });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        var saved = await ctx.EmailSettings.AsNoTracking().FirstAsync();
        model.EmailSettings = new EmailSettings { Host = saved.Host, Port = saved.Port, EnableSsl = saved.EnableSsl, SenderEmail = saved.SenderEmail, SenderName = saved.SenderName, UserName = saved.UserName };
        model.SmtpPasswordInput = "rotated-password";

        await model.OnPostSaveEmailAsync();

        var entries = await ctx.AuditTrail.Where(a => a.Object == "EmailSettings").ToListAsync();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("PasswordFingerprint", entries[0].Field);
        Assert.AreEqual("none", entries[0].OldValue);
        Assert.AreNotEqual("none", entries[0].NewValue);
        StringAssert.Contains(entries[0].Reason, "SMTP password changed");
    }

    [TestMethod]
    public async Task OnPostSaveEmailAsync_PasswordProvided_NeverWrittenToAuditTrail()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveEmailAsync_PasswordProvided_NeverWrittenToAuditTrail));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com" };
        model.SmtpPasswordInput = "s3cret-smtp-password";

        await model.OnPostSaveEmailAsync();

        var entries = await ctx.AuditTrail.Where(a => a.Object == "EmailSettings").ToListAsync();
        Assert.IsTrue(entries.All(e => e.Field != "PasswordEncrypted"));
        Assert.IsTrue(entries.All(e =>
            (e.OldValue ?? string.Empty).Contains("s3cret-smtp-password") == false &&
            (e.NewValue ?? string.Empty).Contains("s3cret-smtp-password") == false));
    }

    // ---- Email tab "Test connection" (OnPostTestEmailConnectionAsync) ----------------------
    // Only the missing-configuration paths are covered without a real SMTP server -- same reason
    // OnPostTestAiConnectionAsync's tests above stop short of a real HTTP call.

    [TestMethod]
    public async Task OnPostTestEmailConnectionAsync_NoHostAnywhere_ReturnsFailureWithoutSending()
    {
        await using var ctx = CreateContext(nameof(OnPostTestEmailConnectionAsync_NoHostAnywhere_ReturnsFailureWithoutSending));
        var model = CreateModel(ctx);
        model.EmailSettings = new EmailSettings();

        var result = await model.OnPostTestEmailConnectionAsync() as JsonResult;

        Assert.IsNotNull(result, "Expected a JsonResult.");
        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.success);
        StringAssert.Contains((string)value.message, "SMTP host is not configured");
    }

    [TestMethod]
    public async Task OnPostTestEmailConnectionAsync_HostButNoSenderEmail_ReturnsThatErrorMessage()
    {
        await using var ctx = CreateContext(nameof(OnPostTestEmailConnectionAsync_HostButNoSenderEmail_ReturnsThatErrorMessage));
        var model = CreateModel(ctx);
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com" };

        var result = await model.OnPostTestEmailConnectionAsync() as JsonResult;

        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.success);
        StringAssert.Contains((string)value.message, "From address is not configured");
    }

    [TestMethod]
    public async Task OnPostTestEmailConnectionAsync_HostAndSenderButNoCredentials_ReturnsThatErrorMessage()
    {
        await using var ctx = CreateContext(nameof(OnPostTestEmailConnectionAsync_HostAndSenderButNoCredentials_ReturnsThatErrorMessage));
        var model = CreateModel(ctx);
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com" };

        var result = await model.OnPostTestEmailConnectionAsync() as JsonResult;

        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.success);
        StringAssert.Contains((string)value.message, "SMTP username/password is not configured");
    }

    [TestMethod]
    public async Task OnPostTestEmailConnectionAsync_ClearSmtpPasswordCheckedWithExistingStoredPassword_IgnoresStoredPassword()
    {
        await using var ctx = CreateContext(nameof(OnPostTestEmailConnectionAsync_ClearSmtpPasswordCheckedWithExistingStoredPassword_IgnoresStoredPassword));
        var configuration = CreateConfigurationWithEncryptionKey();
        ctx.EmailSettings.Add(new EmailSettings
        {
            Host = "smtp.example.com", SenderEmail = "noreply@example.com", UserName = "noreply@example.com",
            PasswordEncrypted = ApiKeyProtector.Encrypt("original-password", configuration)
        });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, configuration);
        model.EmailSettings = new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com", UserName = "noreply@example.com" };
        model.ClearSmtpPassword = true;

        var result = await model.OnPostTestEmailConnectionAsync() as JsonResult;

        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.success);
        StringAssert.Contains((string)value.message, "SMTP username/password is not configured");
    }

    // ---- AI sub-tab (ported from EditAIModelTests) -----------------------------------------

    [TestMethod]
    public async Task OnGetAsync_AiSettingsAuditEntry_Included()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AiSettingsAuditEntry_Included));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "AiSettings", Field = "Temperature", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailAiEntries.Count);
        Assert.AreEqual("Temperature", model.AuditTrailAiEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInAi()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_OtherConfigPanelAuditEntry_NotIncludedInAi));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.AuditTrailAiEntries.Count);
        Assert.AreEqual(0, model.AuditTrailAiTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_AiAuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AiAuditTrailSearchTerm_FiltersToMatchingEntries));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "AiSettings", Field = "Temperature", Timestamp = DateTime.UtcNow, Operation = "Update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "AiSettings", Field = "MaxOutputTokens", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailAiSearchTerm = "MaxOutputTokens";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailAiEntries.Count);
        Assert.AreEqual("MaxOutputTokens", model.AuditTrailAiEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_AiAuditTrailPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AiAuditTrailPaging_ReturnsRequestedPage));
        for (var i = 0; i < IndexModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "AiSettings", Field = $"Field{i}", Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailAiPage = 2;

        await model.OnGetAsync();

        Assert.AreEqual(2, model.AuditTrailAiTotalPages);
        Assert.AreEqual(1, model.AuditTrailAiEntries.Count);
    }

    // ---- AI tab save (OnPostSaveAiAsync) -- API key encryption, model allow-list, audit safety

    [TestMethod]
    public async Task OnPostSaveAiAsync_ApiKeyProvided_StoresEncryptedNotPlaintext()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ApiKeyProvided_StoresEncryptedNotPlaintext));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.ApiKeyInput = "sk-test-secret-12345";

        await model.OnPostSaveAiAsync();

        var saved = await ctx.AiSettings.AsNoTracking().FirstAsync();
        Assert.IsNotNull(saved.ApiKeyEncrypted);
        Assert.IsFalse(saved.ApiKeyEncrypted!.Contains("sk-test-secret-12345"));
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ApiKeyInputBlank_PreservesExistingEncryptedKey()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ApiKeyInputBlank_PreservesExistingEncryptedKey));
        var configuration = CreateConfigurationWithEncryptionKey();
        var existingCiphertext = ApiKeyProtector.Encrypt("sk-original-key", configuration);
        ctx.AiSettings.Add(new AiSettings { Model = "gpt-4o-mini", ApiKeyEncrypted = existingCiphertext });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, configuration);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.ApiKeyInput = null;

        await model.OnPostSaveAiAsync();

        var saved = await ctx.AiSettings.AsNoTracking().FirstAsync();
        Assert.AreEqual(existingCiphertext, saved.ApiKeyEncrypted);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ClearApiKeyChecked_RemovesStoredKey()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ClearApiKeyChecked_RemovesStoredKey));
        var configuration = CreateConfigurationWithEncryptionKey();
        ctx.AiSettings.Add(new AiSettings { Model = "gpt-4o-mini", ApiKeyEncrypted = ApiKeyProtector.Encrypt("sk-original-key", configuration) });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, configuration);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.ClearApiKey = true;

        await model.OnPostSaveAiAsync();

        var saved = await ctx.AiSettings.AsNoTracking().FirstAsync();
        Assert.IsNull(saved.ApiKeyEncrypted);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ModelOutsideAllowList_RejectsAndDoesNotSave()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ModelOutsideAllowList_RejectsAndDoesNotSave));
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "not-a-real-model" };

        var result = await model.OnPostSaveAiAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        Assert.IsFalse(await ctx.AiSettings.AnyAsync());
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_AzureProviderMissingEndpoint_RejectsAndDoesNotSave()
    {
        // AiSettings.Validate (IValidatableObject) requires Endpoint once Provider != OpenAI --
        // TryValidateModel in OnPostSaveAiAsync must surface it.
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_AzureProviderMissingEndpoint_RejectsAndDoesNotSave));
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini", Provider = AiProvider.AzureOpenAI };

        var result = await model.OnPostSaveAiAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        Assert.IsFalse(await ctx.AiSettings.AnyAsync());
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_AzureProviderWithEndpoint_SavesEndpoint()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_AzureProviderWithEndpoint_SavesEndpoint));
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings
        {
            Model = "gpt-4o-mini",
            Provider = AiProvider.AzureOpenAI,
            Endpoint = "https://my-resource.openai.azure.com/openai/v1"
        };

        var result = await model.OnPostSaveAiAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        var saved = await ctx.AiSettings.AsNoTracking().FirstAsync();
        Assert.AreEqual(AiProvider.AzureOpenAI, saved.Provider);
        Assert.AreEqual("https://my-resource.openai.azure.com/openai/v1", saved.Endpoint);
    }

    [TestMethod]
    public async Task OnGetAsync_LegacyMicrosoftFoundryProvider_NormalizedToAzureOpenAiForDisplay()
    {
        // AiProvider.MicrosoftFoundry was retired 2026-11-05 (merged into AiProvider.AzureOpenAI,
        // which now behaves identically -- see AiProvider's remarks). AiProviderOptions no longer
        // offers it as a selectable option, so a row saved with it before the merge must be
        // normalized to AzureOpenAI on load, or the <select> would render with nothing selected.
        await using var ctx = CreateContext(nameof(OnGetAsync_LegacyMicrosoftFoundryProvider_NormalizedToAzureOpenAiForDisplay));
        ctx.AiSettings.Add(new AiSettings { Model = "gpt-4o-mini", Provider = AiProvider.MicrosoftFoundry });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(AiProvider.AzureOpenAI, model.AiSettings.Provider);
    }

    [TestMethod]
    public void AiProviderOptions_ExcludesRetiredMicrosoftFoundryValue()
    {
        using var ctx = CreateContext(nameof(AiProviderOptions_ExcludesRetiredMicrosoftFoundryValue));
        var model = CreateModel(ctx);

        var values = model.AiProviderOptions.Select(o => o.Value).ToList();

        CollectionAssert.AreEquivalent(
            new[] { ((int)AiProvider.OpenAI).ToString(), ((int)AiProvider.AzureOpenAI).ToString() },
            values);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ModelChanged_AppearsInAuditEntry()
    {
        // "gpt-4o-mini" is the only currently-allowed value, so this simulates a legacy row
        // saved before the allow-list was narrowed to just that one model -- the posted value
        // only needs to be in AllowedChatModels; the existing stored value does not.
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ModelChanged_AppearsInAuditEntry));
        ctx.AiSettings.Add(new AiSettings { Model = "gpt-4o-legacy" });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };

        await model.OnPostSaveAiAsync();

        var modelFieldEntry = await ctx.AuditTrail.SingleAsync(a => a.Object == "AiSettings" && a.Field == "Model");
        Assert.AreEqual("gpt-4o-legacy", modelFieldEntry.OldValue);
        Assert.AreEqual("gpt-4o-mini", modelFieldEntry.NewValue);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ApiKeyChangedOnlyNothingElseChanged_StillProducesAuditEntry()
    {
        // A pure key rotation/removal with no other field touched must still leave a trace --
        // LogUpdateAsync only writes rows for fields that actually differ, so without
        // AiSettingsAuditSnapshot.ApiKeyFingerprint, this save would otherwise create zero
        // audit rows at all, silently losing the "(API key changed)" reason text.
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ApiKeyChangedOnlyNothingElseChanged_StillProducesAuditEntry));
        ctx.AiSettings.Add(new AiSettings { Model = "gpt-4o-mini" });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        var saved = await ctx.AiSettings.AsNoTracking().FirstAsync();
        model.AiSettings = new AiSettings
        {
            Provider = saved.Provider, Model = saved.Model, Endpoint = saved.Endpoint,
            Temperature = saved.Temperature, MaxOutputTokens = saved.MaxOutputTokens,
            RequestTimeoutSeconds = saved.RequestTimeoutSeconds, FullScanRowThreshold = saved.FullScanRowThreshold,
            MaxFindingsPerAnalysis = saved.MaxFindingsPerAnalysis, SystemMessage = saved.SystemMessage
        };
        model.ApiKeyInput = "sk-rotated-key";

        await model.OnPostSaveAiAsync();

        var entries = await ctx.AuditTrail.Where(a => a.Object == "AiSettings").ToListAsync();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("ApiKeyFingerprint", entries[0].Field);
        Assert.AreEqual("none", entries[0].OldValue);
        Assert.AreNotEqual("none", entries[0].NewValue);
        StringAssert.Contains(entries[0].Reason, "API key changed");
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ApiKeyProvided_NeverWrittenToAuditTrail()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ApiKeyProvided_NeverWrittenToAuditTrail));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.ApiKeyInput = "sk-test-secret-12345";

        await model.OnPostSaveAiAsync();

        var entries = await ctx.AuditTrail.Where(a => a.Object == "AiSettings").ToListAsync();
        Assert.IsTrue(entries.All(e => e.Field != "ApiKeyEncrypted"));
        Assert.IsTrue(entries.All(e =>
            (e.OldValue ?? string.Empty).Contains("sk-test-secret-12345") == false &&
            (e.NewValue ?? string.Empty).Contains("sk-test-secret-12345") == false));
    }

    // ---- AI-assisted scan review toggle (ported from the deleted AdminPanel/
    //      AIEnhancedReviewEngineModelTests -- that standalone page's toggle + prompt template
    //      now live on this same AI Analysis tab, saved together by OnPostSaveAiAsync) ---------

    [TestMethod]
    public async Task OnGetAsync_NoEngineSettingsRow_AiAnalysisEnabledDefaultsToTrue()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NoEngineSettingsRow_AiAnalysisEnabledDefaultsToTrue));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsTrue(model.AiAnalysisEnabled);
    }

    [TestMethod]
    public async Task OnGetAsync_ExistingEngineSettingsRowDisabled_LoadsFalse()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingEngineSettingsRowDisabled_LoadsFalse));
        ctx.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = false });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsFalse(model.AiAnalysisEnabled);
    }

    [TestMethod]
    public async Task OnGetAsync_SetsPromptTemplate_ToAuditTrailPromptBuilderDescribeTemplate()
    {
        // PromptTemplate is display-only (read-only textarea on the AI Analysis tab) -- it must
        // always be sourced from AuditTrailPromptBuilder.DescribeTemplate() so it can never
        // drift from what OpenAIService actually sends. Assert it by construction, not by
        // duplicating the template text here.
        await using var ctx = CreateContext(nameof(OnGetAsync_SetsPromptTemplate_ToAuditTrailPromptBuilderDescribeTemplate));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(AuditTrailPromptBuilder.DescribeTemplate(), model.PromptTemplate);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_NoExistingEngineSettingsRow_CreatesRowWithPostedAiAnalysisEnabledValue()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_NoExistingEngineSettingsRow_CreatesRowWithPostedAiAnalysisEnabledValue));
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.AiAnalysisEnabled = false;

        await model.OnPostSaveAiAsync();

        var saved = await ctx.EngineSettings.SingleAsync();
        Assert.IsFalse(saved.AiAnalysisEnabled);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ExistingEngineSettingsRow_TogglesAiAnalysisEnabledInDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ExistingEngineSettingsRow_TogglesAiAnalysisEnabledInDatabase));
        ctx.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = true });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.AiAnalysisEnabled = false;

        await model.OnPostSaveAiAsync();

        var updated = await ctx.EngineSettings.AsNoTracking().SingleAsync();
        Assert.IsFalse(updated.AiAnalysisEnabled);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_AiAnalysisEnabledChanged_LogsAuditUpdateEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_AiAnalysisEnabledChanged_LogsAuditUpdateEntry));
        ctx.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = true });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.AiAnalysisEnabled = false;

        await model.OnPostSaveAiAsync();

        var entry = await ctx.AuditTrail.SingleAsync(a => a.Object == nameof(EngineSettings) && a.Field == "AiAnalysisEnabled");
        Assert.AreEqual("True", entry.OldValue);
        Assert.AreEqual("False", entry.NewValue);
        Assert.AreEqual("Update", entry.Operation);
        Assert.AreEqual(AuditObjectType.ConfigPanel.ToString(), entry.ObjectType);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_AiAnalysisEnabledUnchanged_NoAuditEntryLoggedForIt()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_AiAnalysisEnabledUnchanged_NoAuditEntryLoggedForIt));
        ctx.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = true });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.AiAnalysisEnabled = true;

        await model.OnPostSaveAiAsync();

        Assert.IsFalse(await ctx.AuditTrail.AnyAsync(a => a.Object == nameof(EngineSettings)));
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ExistingEngineSettingsRow_DoesNotChangeOtherEngineSettingsFields()
    {
        // OnPostSaveAiAsync's SaveAiAnalysisEnabledAsync helper binds and mutates only
        // AiAnalysisEnabled on the shared EngineSettings singleton row -- the same row
        // Rule-Based Review Engine's "Engine Settings" tab writes to. Saving here must never
        // touch the rule-threshold fields that page owns.
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ExistingEngineSettingsRow_DoesNotChangeOtherEngineSettingsFields));
        ctx.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = true, UsernameMinLength = 7 });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.AiAnalysisEnabled = false;

        await model.OnPostSaveAiAsync();

        var updated = await ctx.EngineSettings.AsNoTracking().SingleAsync();
        Assert.AreEqual(7, updated.UsernameMinLength);
    }

    [TestMethod]
    public async Task OnPostSaveAiAsync_ModelOutsideAllowList_DoesNotSaveAiAnalysisEnabledEither()
    {
        // A rejected AiSettings save must not partially apply the toggle -- both fields are
        // submitted by the same form/button now.
        await using var ctx = CreateContext(nameof(OnPostSaveAiAsync_ModelOutsideAllowList_DoesNotSaveAiAnalysisEnabledEither));
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "not-a-real-model" };
        model.AiAnalysisEnabled = false;

        await model.OnPostSaveAiAsync();

        Assert.IsFalse(await ctx.EngineSettings.AnyAsync());
    }

    [TestMethod]
    public async Task OnGetAsync_AiAnalysisEnabledAuditEntry_IncludedInAiSubTab()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AiAnalysisEnabledAuditEntry_IncludedInAiSubTab));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "AiAnalysisEnabled", OldValue = "True", NewValue = "False", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailAiEntries.Count);
        Assert.AreEqual("False", model.AuditTrailAiEntries[0].NewValue);
    }

    [TestMethod]
    public async Task OnGetAsync_AiSubTabCombinesAiSettingsAndAiAnalysisEnabledEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AiSubTabCombinesAiSettingsAndAiAnalysisEnabledEntries));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "AiSettings", Field = "Temperature", Timestamp = DateTime.UtcNow, Operation = "Update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "AiAnalysisEnabled", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(2, model.AuditTrailAiTotalCount);
    }

    [TestMethod]
    public async Task OnGetExportAllAsync_AiAnalysisSheet_IncludesEnabledFlagAndPromptTemplate()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAllAsync_AiAnalysisSheet_IncludesEnabledFlagAndPromptTemplate));
        ctx.EngineSettings.Add(new EngineSettings { AiAnalysisEnabled = false });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetExportAllAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        using var stream = new MemoryStream(fileResult!.FileContents);
        using var workbook = new XLWorkbook(stream);

        var aiSheet = workbook.Worksheet("AI Analysis");
        var aiCellValues = Enumerable.Range(1, aiSheet.LastRowUsed()!.RowNumber())
            .SelectMany(row => Enumerable.Range(1, aiSheet.LastColumnUsed()!.ColumnNumber())
                .Select(col => aiSheet.Cell(row, col).GetString()))
            .ToList();

        Assert.IsTrue(aiCellValues.Contains("AI Analysis Enabled"));
        Assert.IsTrue(aiCellValues.Contains("No"));
        Assert.IsTrue(aiCellValues.Contains("Prompt Template"));
        Assert.IsTrue(aiCellValues.Any(v => v.Contains("audit trail")),
            "Expected the exported prompt template to contain text from AuditTrailPromptBuilder.DescribeTemplate().");
    }

    [TestMethod]
    public async Task OnGetExportAllAsync_AuditTrailSheet_IncludesAiAnalysisEnabledEntryLabeledAiAnalysis()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAllAsync_AuditTrailSheet_IncludesAiAnalysisEnabledEntryLabeledAiAnalysis));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "AiAnalysisEnabled", Operation = "Update", OldValue = "True", NewValue = "False", Timestamp = DateTime.UtcNow });
        // A sibling EngineSettings change this page does not own (Rule-Based Review Engine's own
        // "Engine Settings" tab) must not leak into this export.
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "UsernameMinLength", Operation = "Update", Timestamp = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetExportAllAsync();

        var fileResult = result as FileContentResult;
        using var stream = new MemoryStream(fileResult!.FileContents);
        using var workbook = new XLWorkbook(stream);
        var auditSheet = workbook.Worksheet("Audit Trail");

        var rows = Enumerable.Range(2, auditSheet.LastRowUsed()!.RowNumber() - 1)
            .Select(r => (Area: auditSheet.Cell(r, 1).GetString(), Field: auditSheet.Cell(r, 5).GetString()))
            .ToList();

        Assert.AreEqual(1, rows.Count, "Only the AiAnalysisEnabled field change should be exported.");
        Assert.AreEqual("AI Analysis", rows[0].Area);
        Assert.AreEqual("AiAnalysisEnabled", rows[0].Field);
    }

    // ---- AI tab "Test connection" (OnPostTestAiConnectionAsync) -----------------------------
    // Only the no-API-key and misconfigured-provider paths are covered without a real HTTP call
    // -- see OpenAIServiceEndpointTests' own "TestConnectionAsync" region for why (OpenAIService
    // has no HTTP-level test coverage anywhere in this project).

    [TestMethod]
    public async Task OnPostTestAiConnectionAsync_NoApiKeyAnywhere_ReturnsFailureWithoutCallingProvider()
    {
        await using var ctx = CreateContext(nameof(OnPostTestAiConnectionAsync_NoApiKeyAnywhere_ReturnsFailureWithoutCallingProvider));
        var model = CreateModel(ctx);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };

        var result = await model.OnPostTestAiConnectionAsync() as JsonResult;

        Assert.IsNotNull(result, "Expected a JsonResult.");
        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.success);
        StringAssert.Contains((string)value.message, "No API key is configured");
    }

    [TestMethod]
    public async Task OnPostTestAiConnectionAsync_ClearApiKeyCheckedWithExistingStoredKey_IgnoresStoredKey()
    {
        await using var ctx = CreateContext(nameof(OnPostTestAiConnectionAsync_ClearApiKeyCheckedWithExistingStoredKey_IgnoresStoredKey));
        var configuration = CreateConfigurationWithEncryptionKey();
        ctx.AiSettings.Add(new AiSettings { Model = "gpt-4o-mini", ApiKeyEncrypted = ApiKeyProtector.Encrypt("sk-original-key", configuration) });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, configuration);
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini" };
        model.ClearApiKey = true;

        var result = await model.OnPostTestAiConnectionAsync() as JsonResult;

        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.success);
        StringAssert.Contains((string)value.message, "No API key is configured");
    }

    [TestMethod]
    public async Task OnPostTestAiConnectionAsync_AzureProviderMissingEndpoint_ReturnsThatErrorMessage()
    {
        // ApiKeyInput is non-blank, so OnPostTestAiConnectionAsync gets as far as calling
        // OpenAIService.TestConnectionAsync -- which then fails inside BuildChatUrl before any
        // HTTP request is attempted (no network access needed for this test), the same path
        // OpenAIServiceEndpointTests.TestConnectionAsync_AzureProviderMissingEndpoint_* covers
        // directly against OpenAIService.
        await using var ctx = CreateContext(nameof(OnPostTestAiConnectionAsync_AzureProviderMissingEndpoint_ReturnsThatErrorMessage));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.AiSettings = new AiSettings { Model = "gpt-4o-mini", Provider = AiProvider.AzureOpenAI };
        model.ApiKeyInput = "sk-test-key";

        var result = await model.OnPostTestAiConnectionAsync() as JsonResult;

        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.success);
        StringAssert.Contains((string)value.message, "Endpoint is not configured");
    }

    // ---- OnGetExportAllAsync -----------------------------------------------------------------

    [TestMethod]
    public async Task OnGetExportAllAsync_ReturnsFileWithExpectedWorksheets()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAllAsync_ReturnsFileWithExpectedWorksheets));
        ctx.GeneralSettings.Add(new GeneralSettings { DateTimeFormat = "yyyy-MM-dd HH:mm", DisplayTimeZone = "UTC" });
        ctx.SecuritySettings.Add(new SecuritySettings { PasswordRequiredLength = 10 });
        ctx.EmailSettings.Add(new EmailSettings { Host = "smtp.example.com", SenderEmail = "noreply@example.com" });
        ctx.AiSettings.Add(new AiSettings { Model = "gpt-4o-mini", ApiKeyEncrypted = "super-secret-ciphertext" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "SecuritySettings", Field = "PasswordRequiredLength", Operation = "Update", Timestamp = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetExportAllAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);

        CollectionAssert.AreEquivalent(
            new[] { "General", "Security", "Email", "FTP", "AI Analysis", "API Keys", "MCP Keys", "Audit Trail" },
            workbook.Worksheets.Select(w => w.Name).ToList());

        var aiSheet = workbook.Worksheet("AI Analysis");
        var aiCellValues = Enumerable.Range(1, aiSheet.LastRowUsed()!.RowNumber())
            .SelectMany(row => Enumerable.Range(1, aiSheet.LastColumnUsed()!.ColumnNumber())
                .Select(col => aiSheet.Cell(row, col).GetString()))
            .ToList();
        Assert.IsTrue(aiCellValues.All(v => !v.Contains("super-secret-ciphertext")),
            "The AI Analysis sheet must never contain the encrypted API key.");
        Assert.IsTrue(aiCellValues.Contains("OpenAI API Key"));
        Assert.IsTrue(aiCellValues.Contains("Present"), "A saved ApiKeyEncrypted should report as Present.");

        var auditSheet = workbook.Worksheet("Audit Trail");
        Assert.AreEqual("Security", auditSheet.Cell(2, 1).GetString());
        Assert.AreEqual("PasswordRequiredLength", auditSheet.Cell(2, 5).GetString());
    }

    // ---- API tab (dbo.ApiKeyCredential -- generate/revoke, replacing IntegrationHub.API's old
    //      static ApiKeyAuth:Keys config list) ------------------------------------------------

    [TestMethod]
    public async Task OnPostGenerateApiKeyAsync_ValidName_CreatesEncryptedRowAndStashesPlaintextOnce()
    {
        await using var ctx = CreateContext(nameof(OnPostGenerateApiKeyAsync_ValidName_CreatesEncryptedRowAndStashesPlaintextOnce));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewApiKeyName = "Partner X";

        var result = await model.OnPostGenerateApiKeyAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        var saved = await ctx.ApiKeyCredential.AsNoTracking().FirstAsync();
        Assert.AreEqual("Partner X", saved.Name);
        Assert.IsFalse(string.IsNullOrEmpty(saved.EncryptedKey));
        Assert.IsNull(saved.RevokedUtc);

        var rawKey = model.TempData["GeneratedApiKeyPlaintext"] as string;
        Assert.IsFalse(string.IsNullOrEmpty(rawKey));
        Assert.AreEqual(rawKey, ApiKeyProtector.Decrypt(saved.EncryptedKey, CreateConfigurationWithEncryptionKey()));
        StringAssert.EndsWith(rawKey, saved.KeyHint);

        var entries = await ctx.AuditTrail.Where(a => a.Object == nameof(ApiKeyCredential)).ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.Field != "EncryptedKey"),
            "The generated key's ciphertext must never be written to dbo.AuditTrail.");
        Assert.IsTrue(entries.All(e =>
            (e.OldValue ?? string.Empty).Contains(rawKey!) == false &&
            (e.NewValue ?? string.Empty).Contains(rawKey!) == false),
            "The raw (plaintext) key must never be written to dbo.AuditTrail.");
    }

    [TestMethod]
    public async Task OnPostGenerateApiKeyAsync_BlankName_DoesNotCreateRow()
    {
        await using var ctx = CreateContext(nameof(OnPostGenerateApiKeyAsync_BlankName_DoesNotCreateRow));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewApiKeyName = "   ";

        var result = await model.OnPostGenerateApiKeyAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        Assert.IsFalse(await ctx.ApiKeyCredential.AnyAsync());
    }

    [TestMethod]
    public async Task OnPostRevokeApiKeyAsync_ActiveKey_SetsRevokedUtcAndKeepsRow()
    {
        await using var ctx = CreateContext(nameof(OnPostRevokeApiKeyAsync_ActiveKey_SetsRevokedUtcAndKeepsRow));
        ctx.ApiKeyCredential.Add(new ApiKeyCredential { Name = "Partner X", EncryptedKey = "ciphertext", KeyHint = "ab12", CreatedUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var id = (await ctx.ApiKeyCredential.AsNoTracking().FirstAsync()).Id;

        var model = CreateModel(ctx);
        var result = await model.OnPostRevokeApiKeyAsync(id);

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        var saved = await ctx.ApiKeyCredential.AsNoTracking().FirstAsync();
        Assert.IsNotNull(saved.RevokedUtc);
        Assert.AreEqual(1, await ctx.ApiKeyCredential.CountAsync());

        var entries = await ctx.AuditTrail.Where(a => a.Object == nameof(ApiKeyCredential)).ToListAsync();
        Assert.IsTrue(entries.Any(e => e.Field == "RevokedUtc"));
    }

    [TestMethod]
    public async Task OnPostRevokeApiKeyAsync_AlreadyRevokedKey_NoAdditionalAuditEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostRevokeApiKeyAsync_AlreadyRevokedKey_NoAdditionalAuditEntry));
        ctx.ApiKeyCredential.Add(new ApiKeyCredential { Name = "Partner X", EncryptedKey = "ciphertext", KeyHint = "ab12", CreatedUtc = DateTime.UtcNow, RevokedUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var id = (await ctx.ApiKeyCredential.AsNoTracking().FirstAsync()).Id;

        var model = CreateModel(ctx);
        await model.OnPostRevokeApiKeyAsync(id);

        var entries = await ctx.AuditTrail.Where(a => a.Object == nameof(ApiKeyCredential)).ToListAsync();
        Assert.AreEqual(0, entries.Count);
    }

    [TestMethod]
    public async Task OnPostRevokeApiKeyAsync_UnknownId_NoOp()
    {
        await using var ctx = CreateContext(nameof(OnPostRevokeApiKeyAsync_UnknownId_NoOp));
        var model = CreateModel(ctx);

        var result = await model.OnPostRevokeApiKeyAsync(id: 12345);

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        Assert.IsFalse(await ctx.ApiKeyCredential.AnyAsync());
    }

    [TestMethod]
    public async Task OnGetAsync_ApiKeysListedNewestFirst()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ApiKeysListedNewestFirst));
        ctx.ApiKeyCredential.Add(new ApiKeyCredential { Name = "Older", EncryptedKey = "c1", KeyHint = "aaaa", CreatedUtc = DateTime.UtcNow.AddDays(-1) });
        ctx.ApiKeyCredential.Add(new ApiKeyCredential { Name = "Newer", EncryptedKey = "c2", KeyHint = "bbbb", CreatedUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx);
        await model.OnGetAsync();

        Assert.AreEqual(2, model.ApiKeys.Count);
        Assert.AreEqual("Newer", model.ApiKeys[0].Name);
        Assert.AreEqual("Older", model.ApiKeys[1].Name);
    }

    // ---- MCP tab (dbo.McpApiKeyCredential -- generate/revoke, replacing the MCP server's old
    //      static McpAuth:Keys config list). Identical in every respect to the API tab's tests
    //      above except for the table/handlers involved -- the two key pools are deliberately
    //      disjoint (see AuditTrailReview.Model.McpApiKeyCredential's remarks). -----------------

    [TestMethod]
    public async Task OnPostGenerateMcpApiKeyAsync_ValidName_CreatesEncryptedRowAndStashesPlaintextOnce()
    {
        await using var ctx = CreateContext(nameof(OnPostGenerateMcpApiKeyAsync_ValidName_CreatesEncryptedRowAndStashesPlaintextOnce));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewMcpApiKeyName = "QA Dashboard";

        var result = await model.OnPostGenerateMcpApiKeyAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        var saved = await ctx.McpApiKeyCredential.AsNoTracking().FirstAsync();
        Assert.AreEqual("QA Dashboard", saved.Name);
        Assert.IsFalse(string.IsNullOrEmpty(saved.EncryptedKey));
        Assert.IsNull(saved.RevokedUtc);

        var rawKey = model.TempData["GeneratedMcpApiKeyPlaintext"] as string;
        Assert.IsFalse(string.IsNullOrEmpty(rawKey));
        Assert.AreEqual(rawKey, ApiKeyProtector.Decrypt(saved.EncryptedKey, CreateConfigurationWithEncryptionKey()));
        StringAssert.EndsWith(rawKey, saved.KeyHint);

        var entries = await ctx.AuditTrail.Where(a => a.Object == nameof(McpApiKeyCredential)).ToListAsync();
        Assert.IsTrue(entries.Count > 0);
        Assert.IsTrue(entries.All(e => e.Field != "EncryptedKey"),
            "The generated key's ciphertext must never be written to dbo.AuditTrail.");
        Assert.IsTrue(entries.All(e =>
            (e.OldValue ?? string.Empty).Contains(rawKey!) == false &&
            (e.NewValue ?? string.Empty).Contains(rawKey!) == false),
            "The raw (plaintext) key must never be written to dbo.AuditTrail.");
    }

    [TestMethod]
    public async Task OnPostGenerateMcpApiKeyAsync_ExistingUserSelected_StoresUserId()
    {
        await using var ctx = CreateContext(nameof(OnPostGenerateMcpApiKeyAsync_ExistingUserSelected_StoresUserId));
        ctx.Users.Add(new ApplicationUser { Id = "u-1", UserName = "alice" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewMcpApiKeyName = "Alice laptop";
        model.NewMcpApiKeyUserId = "u-1";

        await model.OnPostGenerateMcpApiKeyAsync();

        Assert.AreEqual("u-1", (await ctx.McpApiKeyCredential.AsNoTracking().SingleAsync()).UserId);
    }

    [TestMethod]
    public async Task OnPostGenerateMcpApiKeyAsync_UnknownUserSelected_RejectsAndCreatesNoKey()
    {
        await using var ctx = CreateContext(nameof(OnPostGenerateMcpApiKeyAsync_UnknownUserSelected_RejectsAndCreatesNoKey));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewMcpApiKeyName = "Ghost";
        model.NewMcpApiKeyUserId = "no-such-user";

        await model.OnPostGenerateMcpApiKeyAsync();

        Assert.IsFalse(await ctx.McpApiKeyCredential.AnyAsync());
        Assert.IsNotNull(model.TempData["Error"]);
    }

    [TestMethod]
    public async Task OnPostGenerateMcpApiKeyAsync_NoUserSelected_CreatesSharedKeyWithNullUserId()
    {
        await using var ctx = CreateContext(nameof(OnPostGenerateMcpApiKeyAsync_NoUserSelected_CreatesSharedKeyWithNullUserId));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewMcpApiKeyName = "Shared";

        await model.OnPostGenerateMcpApiKeyAsync();

        Assert.IsNull((await ctx.McpApiKeyCredential.AsNoTracking().SingleAsync()).UserId);
    }

    [TestMethod]
    public async Task OnPostGenerateMcpApiKeyAsync_BlankName_DoesNotCreateRow()
    {
        await using var ctx = CreateContext(nameof(OnPostGenerateMcpApiKeyAsync_BlankName_DoesNotCreateRow));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewMcpApiKeyName = "   ";

        var result = await model.OnPostGenerateMcpApiKeyAsync();

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        Assert.IsFalse(await ctx.McpApiKeyCredential.AnyAsync());
    }

    [TestMethod]
    public async Task OnPostRevokeMcpApiKeyAsync_ActiveKey_SetsRevokedUtcAndKeepsRow()
    {
        await using var ctx = CreateContext(nameof(OnPostRevokeMcpApiKeyAsync_ActiveKey_SetsRevokedUtcAndKeepsRow));
        ctx.McpApiKeyCredential.Add(new McpApiKeyCredential { Name = "QA Dashboard", EncryptedKey = "ciphertext", KeyHint = "ab12", CreatedUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var id = (await ctx.McpApiKeyCredential.AsNoTracking().FirstAsync()).Id;

        var model = CreateModel(ctx);
        var result = await model.OnPostRevokeMcpApiKeyAsync(id);

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        var saved = await ctx.McpApiKeyCredential.AsNoTracking().FirstAsync();
        Assert.IsNotNull(saved.RevokedUtc);
        Assert.AreEqual(1, await ctx.McpApiKeyCredential.CountAsync());

        var entries = await ctx.AuditTrail.Where(a => a.Object == nameof(McpApiKeyCredential)).ToListAsync();
        Assert.IsTrue(entries.Any(e => e.Field == "RevokedUtc"));
    }

    [TestMethod]
    public async Task OnPostRevokeMcpApiKeyAsync_AlreadyRevokedKey_NoAdditionalAuditEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostRevokeMcpApiKeyAsync_AlreadyRevokedKey_NoAdditionalAuditEntry));
        ctx.McpApiKeyCredential.Add(new McpApiKeyCredential { Name = "QA Dashboard", EncryptedKey = "ciphertext", KeyHint = "ab12", CreatedUtc = DateTime.UtcNow, RevokedUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var id = (await ctx.McpApiKeyCredential.AsNoTracking().FirstAsync()).Id;

        var model = CreateModel(ctx);
        await model.OnPostRevokeMcpApiKeyAsync(id);

        var entries = await ctx.AuditTrail.Where(a => a.Object == nameof(McpApiKeyCredential)).ToListAsync();
        Assert.AreEqual(0, entries.Count);
    }

    [TestMethod]
    public async Task OnPostRevokeMcpApiKeyAsync_UnknownId_NoOp()
    {
        await using var ctx = CreateContext(nameof(OnPostRevokeMcpApiKeyAsync_UnknownId_NoOp));
        var model = CreateModel(ctx);

        var result = await model.OnPostRevokeMcpApiKeyAsync(id: 12345);

        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
        Assert.IsFalse(await ctx.McpApiKeyCredential.AnyAsync());
    }

    [TestMethod]
    public async Task OnGetAsync_McpApiKeysListedNewestFirst()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_McpApiKeysListedNewestFirst));
        ctx.McpApiKeyCredential.Add(new McpApiKeyCredential { Name = "Older", EncryptedKey = "c1", KeyHint = "aaaa", CreatedUtc = DateTime.UtcNow.AddDays(-1) });
        ctx.McpApiKeyCredential.Add(new McpApiKeyCredential { Name = "Newer", EncryptedKey = "c2", KeyHint = "bbbb", CreatedUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx);
        await model.OnGetAsync();

        Assert.AreEqual(2, model.McpApiKeys.Count);
        Assert.AreEqual("Newer", model.McpApiKeys[0].Name);
        Assert.AreEqual("Older", model.McpApiKeys[1].Name);
    }

    [TestMethod]
    public async Task OnPostGenerateApiKeyAsync_DoesNotCreateMcpApiKeyCredentialRow()
    {
        // The two tabs/tables are deliberately independent -- generating an API key must never
        // create or touch an MCP key, and vice versa (covered by the MCP-side tests above).
        await using var ctx = CreateContext(nameof(OnPostGenerateApiKeyAsync_DoesNotCreateMcpApiKeyCredentialRow));
        var model = CreateModel(ctx, CreateConfigurationWithEncryptionKey());
        model.NewApiKeyName = "Partner X";

        await model.OnPostGenerateApiKeyAsync();

        Assert.IsFalse(await ctx.McpApiKeyCredential.AnyAsync());
    }
}
