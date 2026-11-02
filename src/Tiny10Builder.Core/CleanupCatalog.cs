namespace Tiny10Builder.Core;

public static class CleanupCatalog
{
    public static IReadOnlyList<CleanupOption> Options { get; } =
    [
        App("cortana", "Cortana", "Remove the packaged Cortana app. Windows Search is preserved.", true, true, "Microsoft.549981C3F5F10"),
        App("mail", "Mail and Calendar", "Remove the Mail / Calendar apps.", true, true, "microsoft.windowscommunicationsapps"),
        App("xbox", "Xbox and gaming overlays", "Remove Xbox apps and Game Bar. Gaming sign-in and recording may be unavailable.", true, true,
            "Microsoft.XboxApp", "Microsoft.Xbox.TCUI", "Microsoft.XboxGameOverlay",
            "Microsoft.XboxGamingOverlay", "Microsoft.XboxIdentityProvider", "Microsoft.XboxSpeechToTextOverlay"),
        App("news-weather", "Weather", "Remove the packaged Weather app; local Windows Search is preserved.", true, true, "Microsoft.BingWeather"),
        App("office-promos", "Office Hub and OneNote", "Remove the Microsoft 365 Office Hub and the OneNote app.", true, true,
            "Microsoft.MicrosoftOfficeHub", "Microsoft.Office.OneNote"),
        App("solitaire", "Solitaire", "Remove bundled games.", true, true, "Microsoft.MicrosoftSolitaireCollection"),
        App("phone", "Your Phone", "Remove phone integration. Cross-device features may stop working.", true, true, "Microsoft.YourPhone"),
        App("feedback", "Feedback, tips and help", "Remove Feedback Hub, Get Help, and introductory apps.", true, true,
            "Microsoft.WindowsFeedbackHub", "Microsoft.GetHelp", "Microsoft.Getstarted"),
        App("social", "People and Skype", "Remove optional organizer and communication apps.", true, true,
            "Microsoft.People", "Microsoft.SkypeApp"),
        App("wallet", "Wallet", "Remove the Microsoft Wallet app.", true, true, "Microsoft.Wallet"),
        App("maps", "Maps", "Remove the Maps app.", true, true, "Microsoft.WindowsMaps"),
        App("mixed-reality", "3D Viewer and Mixed Reality", "Remove 3D Viewer and the Mixed Reality Portal.", true, true,
            "Microsoft.Microsoft3DViewer", "Microsoft.MixedReality.Portal"),
        App("media", "Media Player and Movies", "Remove bundled media playback apps; codecs and shared frameworks remain.", false, true,
            "Microsoft.ZuneMusic", "Microsoft.ZuneVideo"),
        App("utilities", "Clock, Recorder and Sticky Notes", "Remove optional personal utilities.", false, true,
            "Microsoft.WindowsAlarms", "Microsoft.WindowsSoundRecorder", "Microsoft.MicrosoftStickyNotes"),
        App("paint", "Paint", "Remove Paint. Leave unchecked to keep it.", false, false, "Microsoft.MSPaint"),
        App("camera", "Camera", "Remove the Camera app, not camera drivers.", false, false, "Microsoft.WindowsCamera"),
        new("edge-files", "Remove Edge browser", "Delete the Edge browser, EdgeUpdate and related Edge trees. Updates may reinstall Edge.",
            "Advanced / original builder", OptionKind.File, false, false, []),
        new("onedrive-files", "Remove OneDrive setup binaries", "Remove both architecture-specific OneDrive installers from the offline image.",
            "Advanced / original builder", OptionKind.File, false, false, []),
        new("language-extras", "Speech, OCR and handwriting payloads", "Remove optional language capabilities in every image language; retain basic language support.",
            "Optional features", OptionKind.Capability, false, false,
            ["Language.Handwriting", "Language.OCR", "Language.Speech", "Language.TextToSpeech"]),
        new("legacy-capabilities", "Retired accessories and wallpaper payloads", "Remove WordPad, Steps Recorder, Math Recognizer and legacy Media Player where installed.",
            "Optional features", OptionKind.Capability, false, true,
            ["Microsoft.Windows.WordPad", "App.StepsRecorder", "MathRecognizer", "Media.WindowsMediaPlayer"]),
        new("quickassist", "Quick Assist", "Remove Microsoft's remote-assistance client capability.",
            "Optional features", OptionKind.Capability, false, false, ["App.Support.QuickAssist"]),
        Feature("xps", "XPS printing", "Disable optional XPS printing and remove its payload.", false, true, "Printing-XPSServices-Features"),
        Feature("workfolders", "Work Folders", "Disable enterprise Work Folders sync and remove its payload.", false, true, "WorkFolders-Client"),
        Feature("legacy", "Legacy Internet Explorer", "Disable the retired Internet Explorer optional feature where present.", true, true,
            "Internet-Explorer-Optional-amd64"),
        Policy("ads", "Ads, recommendations and sponsored apps", "Suppress consumer content, Settings suggestions, Explorer promotions and welcome tips.", true, true),
        Policy("privacy", "Optional diagnostics and personalization", "Keep required diagnostic data. Disable advertising ID, personalized suggestions and input collection.", true, true),
        Policy("widgets", "News and Interests", "Disable the News and Interests taskbar feed by policy.", true, true),
        Policy("search", "Web search and search highlights", "Turn off web suggestions, cloud search and dynamic search highlights; keep local search.", true, true),
        Policy("onedrive", "OneDrive sync and backup prompts", "Disable OneDrive sync by policy. OneDrive binaries are retained; use this only if you do not need sync.", true, true),
        Policy("edge", "Edge first-run and promotional UI", "Suppress first-run, sidebar and recommendations. Preserve Edge.", true, true),
        Policy("oobe", "Setup privacy and finish-setting-up nags", "Suppress privacy setup and post-sign-in setup suggestions without creating accounts or accepting the EULA.", true, true),
        Policy("cleanup", "Component cleanup", "Regular mode preserves servicing. Core additionally resets the update base before its irreversible component pruning.", true, true),
        Policy("reserved-storage", "Disable reserved storage", "Stop reserving disk space for updates. This can leave less space available during servicing.", false, false),
        Policy("device-encryption", "Disable automatic device encryption", "Prevent automatic BitLocker device encryption; do not use if you require encryption by default.", false, false)
    ];

    private static CleanupOption App(string id, string title, string description, bool balanced, bool minimal, params string[] targets)
        => new(id, title, description, "Bundled apps", OptionKind.App, balanced, minimal, targets);
    private static CleanupOption Feature(string id, string title, string description, bool balanced, bool minimal, params string[] targets)
        => new(id, title, description, "Optional features", OptionKind.Feature, balanced, minimal, targets);
    private static CleanupOption Policy(string id, string title, string description, bool balanced, bool minimal)
        => new(id, title, description, "Windows settings", OptionKind.Policy, balanced, minimal, []);

    public static string[] ForPreset(CleanupPreset preset) => Options
        .Where(o => preset switch
        {
            CleanupPreset.Balanced => o.Balanced,
            CleanupPreset.Minimal => o.Minimal,
            CleanupPreset.Original => o.Kind == OptionKind.App || o.Id is not ("language-extras" or "legacy-capabilities"),
            _ => false
        })
        .Select(o => o.Id).ToArray();

    public static string[] ForMode(BuildMode mode) => mode switch
    {
        BuildMode.Tiny11 => ForPreset(CleanupPreset.Original),
        BuildMode.Tiny11Core => Options.Where(o => o.Minimal || o.Id is
            "language-extras" or "onedrive-files" or
            "reserved-storage" or "device-encryption")
            .Select(o => o.Id).ToArray(),
        BuildMode.Custom => [],
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static BuildMode ModeForRecipe(BuildRecipe recipe)
    {
        if (recipe.Mode is { } mode) return mode;
        var preset = recipe.Flavor == BuildFlavor.Core ? BuildMode.Tiny11Core : BuildMode.Tiny11;
        return recipe.ExpertActions.Length == 0 &&
            recipe.SelectedOptions.ToHashSet(StringComparer.Ordinal).SetEquals(ForMode(preset))
            ? preset : BuildMode.Custom;
    }

    public static bool MatchesApp(CleanupOption option, ProvisionedApp app) =>
        option.Kind == OptionKind.App && option.Targets.Any(target =>
            app.DisplayName.Equals(target, StringComparison.OrdinalIgnoreCase) &&
            app.PackageName.StartsWith(target + "_", StringComparison.OrdinalIgnoreCase));
}
