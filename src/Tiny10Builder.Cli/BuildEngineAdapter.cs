using System.Runtime.Versioning;
using System.Text.Json;
using Tiny10Builder.Core;

namespace Tiny10Builder.Cli;

/// <summary>Production <see cref="ICliEngine"/> over the real <see cref="BuildEngine"/>.</summary>
[SupportedOSPlatform("windows")]
public sealed class BuildEngineAdapter : ICliEngine
{
    public async Task<CliBuildResult> BuildAsync(BuildRecipe recipe, IProgress<BuildProgress>? progress, CancellationToken token)
    {
        if (string.IsNullOrEmpty(recipe.DismPath)) recipe = recipe with { DismPath = ToolLocator.FindDism() };
        if (string.IsNullOrEmpty(recipe.OscdimgPath)) recipe = recipe with { OscdimgPath = ToolLocator.FindOscdimg() };
        var engine = new BuildEngine(new ProcessRunner(), new MediaSourceFactory(), new OfflineRegistry(),
            new ImageOperations(), new BuildHost());
        var result = await engine.BuildAsync(recipe, progress, token).ConfigureAwait(false);
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
