using System.Runtime.Versioning;
using System.Text.Json;
using Tiny10Builder.Core;

namespace Tiny10Builder.Cli;

/// <summary>Production <see cref="ICliEngine"/> over the real <see cref="BuildEngine"/>.</summary>
[SupportedOSPlatform("windows")]
public sealed class BuildEngineAdapter : ICliEngine
{
    private static readonly Lazy<HttpClient> Http = new(CreateClient);

    // Redirects must not be followed by HttpClient: OscdimgAcquirer walks them itself and checks every hop against its host allow-list.
    internal static HttpClientHandler CreateHandler() => new() { AllowAutoRedirect = false };

    internal static HttpClient CreateClient() => new(CreateHandler()) { Timeout = TimeSpan.FromMinutes(5) };

    private readonly Func<string, IProgress<string>?, CancellationToken, Task<string>> acquireOscdimg;
    private readonly Func<string> findOscdimg;
    private readonly Func<BuildRecipe, IProgress<BuildProgress>?, CancellationToken, Task<CliBuildResult>> runEngine;
    private readonly Func<BuildRecipe, CancellationToken, Task> validate;

    /// <summary>Tools live outside the build workspace and the output so a failed or resumed build never touches them.</summary>
    internal static string ToolsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tiny10Builder", "tools");

    public BuildEngineAdapter() : this(null, null, null, null) { }

    internal BuildEngineAdapter(
        Func<string, IProgress<string>?, CancellationToken, Task<string>>? acquireOscdimg,
        Func<string>? findOscdimg,
        Func<BuildRecipe, IProgress<BuildProgress>?, CancellationToken, Task<CliBuildResult>>? runEngine,
        Func<BuildRecipe, CancellationToken, Task>? validate)
    {
        this.validate = validate ?? ValidateWithRealEngineAsync;
        this.acquireOscdimg = acquireOscdimg ?? ((dir, progress, token) => new OscdimgAcquirer(Http.Value).EnsureAsync(dir, progress, token));
        this.findOscdimg = findOscdimg ?? ToolLocator.FindOscdimg;
        this.runEngine = runEngine ?? RunRealEngineAsync;
    }

    public async Task<CliBuildResult> BuildAsync(BuildRecipe recipe, IProgress<BuildProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrEmpty(recipe.DismPath)) recipe = recipe with { DismPath = ToolLocator.FindDism() };
        // Validate the recipe, the host and the selected image BEFORE oscdimg is resolved: nothing is downloaded for a build that cannot start.
        await validate(recipe, token).ConfigureAwait(false);
        recipe = await ResolveOscdimgAsync(recipe, findOscdimg, acquireOscdimg, progress, token).ConfigureAwait(false);
        return await runEngine(recipe, progress, token).ConfigureAwait(false);
    }

    internal static async Task<BuildRecipe> ResolveOscdimgAsync(BuildRecipe recipe, Func<string> find,
        Func<string, IProgress<string>?, CancellationToken, Task<string>> acquire, IProgress<BuildProgress>? progress,
        CancellationToken token)
    {
        if (!string.IsNullOrEmpty(recipe.OscdimgPath)) return recipe;
        var installed = find();
        if (!string.IsNullOrEmpty(installed)) return recipe with { OscdimgPath = installed };
        var forward = progress is null ? null : new SyncProgress(m => progress.Report(new BuildProgress("Tools", m)));
        try
        {
            var acquired = await acquire(ToolsDirectory, forward, token).ConfigureAwait(false);
            return recipe with { OscdimgPath = acquired };
        }
        catch (InvalidDataException e)
        {
            // CliApp maps InvalidDataException to exit 2 (validation); a bad download is an environment failure (exit 1).
            throw new InvalidOperationException(e.Message, e);
        }
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private static BuildEngine CreateRealEngine() => new(new ProcessRunner(), new MediaSourceFactory(), new OfflineRegistry(),
        new ImageOperations(), new BuildHost());

    private static Task ValidateWithRealEngineAsync(BuildRecipe recipe, CancellationToken token) =>
        CreateRealEngine().PreflightAsync(recipe, token);

    private static async Task<CliBuildResult> RunRealEngineAsync(BuildRecipe recipe, IProgress<BuildProgress>? progress, CancellationToken token)
    {
        var engine = CreateRealEngine();
        var result = await engine.BuildAsync(recipe, progress, token).ConfigureAwait(false);
        // WebView2Present is a placeholder: nothing detects WebView2, so it is always false. (CliApp derives EdgePreserved from the recipe profile.)
        return new(result.IsoPath, result.Sha256, ReadSourceBuild(result.ReportPath), WebView2Present: false);
    }

    // BuildResult does not expose the image version; the engine's own report.json carries it as Image.Version.
    internal static string ReadSourceBuild(string reportPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(reportPath));
            var version = doc.RootElement.GetProperty("Image").GetProperty("Version");
            if (version.ValueKind == JsonValueKind.String) return version.GetString() ?? "unknown";
            return $"{version.GetProperty("Major")}.{version.GetProperty("Minor")}.{version.GetProperty("Build")}.{version.GetProperty("Revision")}";
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "unknown";
        }
    }
}
