using System.Text.Json;
using Tiny10Builder.Core;

namespace Tiny10Builder.Cli;

public sealed record CliBuildResult(string IsoPath, string Sha256, string SourceBuild, bool WebView2Present);

public interface ICliEngine
{
    Task<CliBuildResult> BuildAsync(BuildRecipe recipe, IProgress<BuildProgress>? progress, CancellationToken token);
}

/// <summary>Exit codes: 0 success, 1 build failure or cancellation, 2 validation/usage error.</summary>
public static class CliApp
{
    public const string Usage = "Usage: tiny10 build --recipe <file.json> --iso <path> --workspace <dir> [--verbose]";

    public static async Task<int> RunAsync(string[] args, ICliEngine engine, TextWriter stdout, TextWriter stderr,
        CancellationToken token)
    {
        if (args.Length == 0 || args[0] != "build")
            return Usage2(stderr, args.Length == 0 ? "Missing subcommand." : $"Unknown subcommand '{args[0]}'.");

        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var verbose = false;
        for (var i = 1; i < args.Length; i++)
        {
            var name = args[i];
            if (name == "--verbose") { verbose = true; continue; }
            if (name is not ("--recipe" or "--iso" or "--workspace"))
                return Usage2(stderr, $"Unknown option '{name}'.");
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return Usage2(stderr, $"Option {name} needs a value.");
            if (!options.TryAdd(name, args[++i])) return Usage2(stderr, $"Option {name} was given twice.");
        }
        foreach (var required in new[] { "--recipe", "--iso", "--workspace" })
            if (!options.ContainsKey(required)) return Usage2(stderr, $"Missing required option {required}.");

        BuildRecipe recipe;
        string iso, workspace;
        try
        {
            var recipePath = options["--recipe"];
            if (!File.Exists(recipePath)) return Usage2(stderr, $"Recipe file not found: {recipePath}");
            recipe = BuildRecipe.Read(recipePath);
            iso = PathSafety.Full(options["--iso"]);
            workspace = PathSafety.Full(options["--workspace"]);
            PathSafety.RejectReparsePoints(iso);
            PathSafety.RejectReparsePoints(workspace);
            if (!File.Exists(iso) && !Directory.Exists(iso)) return Usage2(stderr, $"ISO not found: {iso}");
            recipe = recipe with
            {
                SourcePath = iso,
                WorkspacePath = workspace,
                OutputPath = string.IsNullOrWhiteSpace(recipe.OutputPath) ? Path.Combine(workspace, "tiny10.iso") : recipe.OutputPath
            };
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            return Usage2(stderr, e.Message, usage: false);
        }

        try
        {
            var progress = new Progress<BuildProgress>(p => stderr.WriteLine($"[{p.Stage}] {p.Message}"));
            var result = await engine.BuildAsync(recipe, progress, token).ConfigureAwait(false);
            var report = new Dictionary<string, object>
            {
                ["IsoPath"] = result.IsoPath,
                ["Sha256"] = result.Sha256,
                ["SourceBuild"] = result.SourceBuild,
                ["EdgePreserved"] = recipe.TemplateProfile == TemplateProfile.ProxmoxCloudbaseInit,
                ["WebView2Present"] = result.WebView2Present
            };
            Directory.CreateDirectory(workspace);
            await File.WriteAllTextAsync(Path.Combine(workspace, "report.json"),
                JsonSerializer.Serialize(report, JsonDefaults.Options), CancellationToken.None).ConfigureAwait(false);
            stdout.WriteLine($"ISO: {result.IsoPath}");
            stdout.WriteLine($"SHA-256: {result.Sha256}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            stderr.WriteLine("Cancelled");
            return 1;
        }
        catch (InvalidDataException e)
        {
            stderr.WriteLine($"Error: {e.Message}");
            return 2;
        }
        catch (Exception e)
        {
            stderr.WriteLine(verbose ? $"Build failed: {e}" : $"Build failed: {e.Message}");
            return 1;
        }
    }

    private static int Usage2(TextWriter stderr, string message, bool usage = true)
    {
        stderr.WriteLine(usage ? $"Error: {message} {Usage}" : $"Error: {message}");
        return 2;
    }
}
