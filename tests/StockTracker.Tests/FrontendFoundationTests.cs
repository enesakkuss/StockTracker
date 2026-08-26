using Xunit;

namespace StockTracker.Tests;

public class FrontendFoundationTests
{
    private readonly string _wwwrootPath;

    public FrontendFoundationTests()
    {
        var baseDir = AppContext.BaseDirectory;
        var solutionDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", ".."));
        _wwwrootPath = Path.Combine(solutionDir, "src", "StockTracker.Api", "wwwroot");
        if (!Directory.Exists(_wwwrootPath))
        {
            _wwwrootPath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "src", "StockTracker.Api", "wwwroot"));
        }
    }

    [Fact]
    public void Frontend_AllCoreStaticFiles_ExistAndAreNonEmpty()
    {
        var requiredFiles = new[]
        {
            "index.html",
            Path.Combine("css", "styles.css"),
            Path.Combine("js", "config.js"),
            Path.Combine("js", "apiClient.js"),
            Path.Combine("js", "auth.js"),
            Path.Combine("js", "ui.js"),
            Path.Combine("js", "app.js")
        };

        foreach (var relPath in requiredFiles)
        {
            var fullPath = Path.Combine(_wwwrootPath, relPath);
            Assert.True(File.Exists(fullPath), $"Required frontend file missing: {relPath} at {fullPath}");
            var content = File.ReadAllText(fullPath);
            Assert.True(content.Length > 20, $"Frontend file is empty: {relPath}");
        }
    }

    [Fact]
    public void Frontend_Config_HasBillingDisabled()
    {
        var configPath = Path.Combine(_wwwrootPath, "js", "config.js");
        var content = File.ReadAllText(configPath);
        Assert.Contains("billingEnabled: false", content);
    }

    [Fact]
    public void Frontend_Html_ContainsAllExpectedSelectors()
    {
        var htmlPath = Path.Combine(_wwwrootPath, "index.html");
        var content = File.ReadAllText(htmlPath);

        var expectedIds = new[]
        {
            "id=\"url-input\"",
            "id=\"fetch-btn\"",
            "id=\"fetch-status-msg\"",
            "id=\"product-card\"",
            "id=\"empty-card\"",
            "id=\"inspect-status-banner\"",
            "id=\"product-store\"",
            "id=\"product-name\"",
            "id=\"product-img\"",
            "id=\"variant-grid\"",
            "id=\"select-all-variants-btn\"",
            "id=\"deselect-all-variants-btn\"",
            "id=\"tg-token\"",
            "id=\"tg-chat-id\"",
            "id=\"tg-test-btn\"",
            "id=\"tg-config-hint\"",
            "id=\"interval-select\"",
            "id=\"start-monitor-btn\"",
            "id=\"start-monitor-status-msg\"",
            "id=\"monitor-usage-badge\"",
            "id=\"monitors-container\"",
            "id=\"auth-logged-out\"",
            "id=\"auth-logged-in\"",
            "id=\"login-form\"",
            "id=\"register-form\"",
            "id=\"auth-view-wrapper\"",
            "id=\"app-shell-wrapper\"",
            "id=\"sidebar-user-name\"",
            "id=\"sidebar-user-email\"",
            "id=\"sidebar-user-avatar\"",
            "id=\"logout-btn\"",
            "id=\"sidebar-toggle-btn\"",
            "id=\"dash-total-monitors\"",
            "id=\"dash-active-monitors\"",
            "id=\"dash-paused-monitors\"",
            "id=\"dash-available-items\"",
            "id=\"dash-notifications-today\"",
            "id=\"dash-plan-info\"",
            "id=\"monitors-search-input\"",
            "id=\"refresh-monitors-btn\"",
            "id=\"pagination-info\"",
            "id=\"prev-page-btn\"",
            "id=\"next-page-btn\"",
            "id=\"edit-monitor-modal\"",
            "id=\"edit-monitor-form\"",
            "id=\"notif-filter-store\"",
            "id=\"notif-filter-date-from\"",
            "id=\"notif-filter-date-to\"",
            "id=\"notif-filter-btn\"",
            "id=\"notif-reset-btn\"",
            "id=\"notifications-container\"",
            "id=\"notif-pagination-info\"",
            "id=\"notif-prev-page-btn\"",
            "id=\"notif-next-page-btn\"",
            "id=\"tg-status-badge\"",
            "id=\"tg-status-masked-token\"",
            "id=\"tg-status-chat-id\"",
            "id=\"view-tg-settings-form\"",
            "id=\"view-tg-token\"",
            "id=\"view-tg-chat-id\"",
            "id=\"view-tg-test-btn\"",
            "id=\"view-tg-disconnect-btn\"",
            "id=\"settings-profile-form\"",
            "id=\"settings-firstname-input\"",
            "id=\"settings-lastname-input\"",
            "id=\"settings-email-display\"",
            "id=\"settings-profile-save-btn\"",
            "id=\"settings-preferences-form\"",
            "id=\"settings-pref-telegram-enabled\"",
            "id=\"settings-pref-language-select\"",
            "id=\"settings-pref-interval-select\"",
            "id=\"settings-pref-timezone-select\"",
            "id=\"settings-pref-save-btn\"",
            "id=\"settings-account-email\"",
            "id=\"settings-account-created-at\"",
            "id=\"settings-account-last-login\"",
            "id=\"settings-account-tg-status\"",
            "id=\"settings-revoke-all-btn\""
        };

        foreach (var id in expectedIds)
        {
            Assert.Contains(id, content);
        }
    }

    [Fact]
    public void Frontend_AppShell_ContainsAllRequiredNavigationViews()
    {
        var htmlPath = Path.Combine(_wwwrootPath, "index.html");
        var content = File.ReadAllText(htmlPath);

        var views = new[]
        {
            "data-view=\"inspector\"",
            "data-view=\"monitors\"",
            "data-view=\"dashboard\"",
            "data-view=\"notifications\"",
            "data-view=\"telegram\"",
            "data-view=\"settings\"",
            "id=\"view-inspector\"",
            "id=\"view-monitors\"",
            "id=\"view-dashboard\"",
            "id=\"view-notifications\"",
            "id=\"view-telegram\"",
            "id=\"view-settings\""
        };

        foreach (var v in views)
        {
            Assert.Contains(v, content);
        }
    }

    [Fact]
    public void Frontend_ApiClient_HandlesStandardErrorCodes()
    {
        var clientPath = Path.Combine(_wwwrootPath, "js", "apiClient.js");
        var content = File.ReadAllText(clientPath);

        Assert.Contains("case 400:", content);
        Assert.Contains("case 401:", content);
        Assert.Contains("case 403:", content);
        Assert.Contains("case 404:", content);
        Assert.Contains("case 409:", content);
        Assert.Contains("case 422:", content);
        Assert.Contains("case 429:", content);
        Assert.Contains("case 500:", content);
        Assert.Contains("PLAN_LIMIT_REACHED", content);
        Assert.Contains("CHECK_INTERVAL_NOT_ALLOWED", content);
        Assert.Contains("DAILY_INSPECT_LIMIT_REACHED", content);
        Assert.Contains("UNSUPPORTED_STORE", content);
    }

    [Fact]
    public void Frontend_Files_DoNotContainHardcodedSecrets()
    {
        var jsDir = Path.Combine(_wwwrootPath, "js");
        var files = Directory.GetFiles(jsDir, "*.js");

        foreach (var file in files)
        {
            var content = File.ReadAllText(file);
            Assert.DoesNotContain("Admin123456!", content);
            Assert.DoesNotContain("mock_webhook_secret", content);
            Assert.DoesNotContain("StockTracker_Development_Super_Secret_Key", content);
            Assert.DoesNotContain("AIzaSy", content);
        }
    }
}
