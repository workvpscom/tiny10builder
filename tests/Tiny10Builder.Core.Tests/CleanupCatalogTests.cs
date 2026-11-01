using Tiny10Builder.Core;
using Xunit;

namespace Tiny10Builder.Core.Tests;

public sealed class CleanupCatalogTests
{
    private static readonly string[] Win11OnlyIds =
        ["copilot", "copilot-app", "devhome", "outlook", "outlook-new", "teams", "teams-new", "terminal", "recall-feature", "clipchamp", "oem"];

    private static string[] Fixture(string file, string prefix) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixtures", "win10-19045", file))
            .Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(" : ", StringComparison.Ordinal) + 3)..].Trim()).ToArray();

    [Fact]
    public void Core_has_no_hardware_bypass_and_none_of_the_Windows_11_only_ids()
    {
        var core = CleanupCatalog.ForMode(BuildMode.Tiny11Core);
        Assert.DoesNotContain("hardware-bypass", core);
        foreach (var id in Win11OnlyIds) Assert.DoesNotContain(id, core);
        Assert.DoesNotContain(CleanupCatalog.Options, o => o.Id == "hardware-bypass" || Win11OnlyIds.Contains(o.Id));
    }

    [Fact]
    public void Core_keeps_the_required_ids_and_leaves_paint_camera_and_calculator_alone()
    {
        var core = CleanupCatalog.ForMode(BuildMode.Tiny11Core);
        foreach (var id in new[] { "language-extras", "onedrive-files", "reserved-storage", "device-encryption" })
            Assert.Contains(id, core);
        var removedApps = CleanupCatalog.Options.Where(o => core.Contains(o.Id) && o.Kind == OptionKind.App)
            .SelectMany(o => o.Targets).ToArray();
        foreach (var kept in new[] { "Microsoft.MSPaint", "Microsoft.WindowsCamera", "Microsoft.WindowsCalculator", "Microsoft.Windows.Photos",
                     "Microsoft.ScreenSketch", "Microsoft.WindowsStore", "Microsoft.StorePurchaseApp", "Microsoft.DesktopAppInstaller" })
            Assert.DoesNotContain(kept, removedApps);
        Assert.DoesNotContain(removedApps, t => t.StartsWith("Microsoft.VCLibs", StringComparison.Ordinal) ||
            t.EndsWith("Extension", StringComparison.Ordinal) || t.StartsWith("Microsoft.WebMedia", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_removes_the_Windows_10_apps_that_exist_in_the_image()
    {
        var core = CleanupCatalog.ForMode(BuildMode.Tiny11Core);
        var removed = CleanupCatalog.Options.Where(o => core.Contains(o.Id) && o.Kind == OptionKind.App).SelectMany(o => o.Targets).ToArray();
        foreach (var app in new[] { "Microsoft.549981C3F5F10", "Microsoft.Microsoft3DViewer", "Microsoft.MixedReality.Portal", "Microsoft.SkypeApp",
                     "Microsoft.XboxApp", "Microsoft.YourPhone", "Microsoft.ZuneMusic", "Microsoft.MicrosoftSolitaireCollection",
                     "Microsoft.MicrosoftStickyNotes", "Microsoft.People", "Microsoft.WindowsMaps", "Microsoft.BingWeather",
                     "Microsoft.MicrosoftOfficeHub", "Microsoft.Office.OneNote", "Microsoft.GetHelp", "Microsoft.WindowsFeedbackHub",
                     "Microsoft.Wallet", "Microsoft.WindowsAlarms", "Microsoft.WindowsSoundRecorder", "microsoft.windowscommunicationsapps" })
            Assert.Contains(app, removed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_app_target_exists_in_the_Windows_10_provisioned_package_list()
    {
        var packages = Fixture("appx.txt", "PackageName");
        Assert.NotEmpty(packages);
        foreach (var target in CleanupCatalog.Options.Where(o => o.Kind == OptionKind.App).SelectMany(o => o.Targets))
            Assert.Contains(packages, p => p.StartsWith(target + "_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_capability_target_exists_in_the_Windows_10_capability_list()
    {
        var capabilities = Fixture("caps.txt", "Capability Identity");
        Assert.NotEmpty(capabilities);
        foreach (var target in CleanupCatalog.Options.Where(o => o.Kind == OptionKind.Capability).SelectMany(o => o.Targets))
            Assert.Contains(capabilities, c => c.StartsWith(target + ".", StringComparison.OrdinalIgnoreCase) ||
                                               c.StartsWith(target + "~", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_feature_target_exists_in_the_Windows_10_feature_list()
    {
        var features = Fixture("feat.txt", "Feature Name");
        Assert.NotEmpty(features);
        foreach (var target in CleanupCatalog.Options.Where(o => o.Kind == OptionKind.Feature).SelectMany(o => o.Targets))
            Assert.Contains(target, features, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Policy_catalog_has_no_setup_hardware_bypass_or_Windows_11_only_values()
    {
        var all = CleanupCatalog.Options.SelectMany(o => PolicyCatalog.ForOption(o.Id)).ToArray();
        Assert.DoesNotContain(all, v => v.Key.Contains("LabConfig") || v.Key.Contains("MoSetup") ||
            v.Key.Contains("UnsupportedHardwareNotificationCache") || v.Name.StartsWith("Bypass", StringComparison.Ordinal));
        Assert.DoesNotContain(all, v => v.Name is "TaskbarMn" or "ShowCopilotButton" || v.Key.Contains("WindowsCopilot") ||
            v.Key.Contains("WindowsAI") || v.Key.Contains("Windows Chat"));
        Assert.Empty(PolicyCatalog.ForOption("hardware-bypass"));
    }

    [Fact]
    public void Core_recipe_unattend_contains_no_hardware_bypass_keys()
    {
        var recipe = new BuildRecipe { Flavor = BuildFlavor.Core, CoreAcknowledged = true,
            SelectedOptions = CleanupCatalog.ForMode(BuildMode.Tiny11Core), PreferLocalAccount = true };
        var image = new WindowsImage(6, "Windows 10 Pro", "", "x64", new Version(10, 0, 19045, 1), "Professional", "Client", 1);
        var text = UnattendBuilder.Create(image, recipe);
        foreach (var marker in new[] { "LabConfig", "MoSetup", "BypassTPM", "BypassSecureBoot", "BypassCPU", "BypassRAM", "BypassStorage" })
            Assert.DoesNotContain(marker, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_old_recipe_naming_hardware_bypass_is_rejected_with_a_clear_message()
    {
        var recipe = new BuildRecipe { SelectedOptions = ["hardware-bypass"] };
        var error = Assert.Throws<InvalidDataException>(() => recipe.ValidateOptions());
        Assert.Contains("hardware-bypass", error.Message);
    }
}
