using System.Runtime.Versioning;
using System.Text.Json;
using Tiny10Builder.Cli;
using Tiny10Builder.Core;
using Xunit;

namespace Tiny10Builder.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class CliTests : IDisposable
{
    private const string ProMessage = "No Windows 10 Pro image in this source.";
    private readonly string root = Path.Combine(Path.GetTempPath(), "tiny10-cli-" + Guid.NewGuid().ToString("N"));
    private string Workspace => Path.Combine(root, "ws");

    public CliTests() => Directory.CreateDirectory(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch { } }

    private sealed class FakeEngine(Func<BuildRecipe, CancellationToken, Task<CliBuildResult>>? build = null) : ICliEngine
    {
        public BuildRecipe? Seen;
        public int Calls;
        public Task<CliBuildResult> BuildAsync(BuildRecipe recipe, IProgress<BuildProgress>? progress, CancellationToken token)
        {
            Calls++; Seen = recipe;
            return build?.Invoke(recipe, token) ?? Task.FromResult(new CliBuildResult(recipe.OutputPath, "ABC123", "10.0.19045.5555", false));
        }
    }

    private string Iso() { var p = Path.Combine(root, "win10.iso"); File.WriteAllText(p, "x"); return p; }

    private string Recipe(bool profile)
    {
        var recipe = new BuildRecipe();
        if (profile)
            recipe = recipe with
            {
                Mode = BuildMode.Tiny11Core, Flavor = BuildFlavor.Core, CoreAcknowledged = true,
                SelectedOptions = CleanupCatalog.ForMode(BuildMode.Tiny11Core),
                TemplateProfile = TemplateProfile.ProxmoxCloudbaseInit
            };
        var path = Path.Combine(root, profile ? "profile.json" : "plain.json");
        recipe.Write(path);
        return path;
    }

    private async Task<(int Code, string Out, string Err)> Run(FakeEngine engine, CancellationToken token, params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = await CliApp.RunAsync(args, engine, o, e, token);
        return (code, o.ToString(), e.ToString());
    }

    private string[] Build(string recipe, string iso) => ["build", "--recipe", recipe, "--iso", iso, "--workspace", Workspace];

    [Fact]
    public async Task Missing_recipe_exits_2_and_does_not_create_workspace()
    {
        var engine = new FakeEngine();
        var r = await Run(engine, default, Build(Path.Combine(root, "nope.json"), Iso()));
        Assert.Equal(2, r.Code);
        Assert.Contains("nope.json", r.Err);
        Assert.False(Directory.Exists(Workspace));
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Malformed_recipe_exits_2()
    {
        var bad = Path.Combine(root, "bad.json"); File.WriteAllText(bad, "{ not json");
        var r = await Run(new FakeEngine(), default, Build(bad, Iso()));
        Assert.Equal(2, r.Code);
        Assert.False(Directory.Exists(Workspace));
    }

    [Fact]
    public async Task Nonexistent_iso_exits_2_without_workspace_writes()
    {
        var engine = new FakeEngine();
        var r = await Run(engine, default, Build(Recipe(false), Path.Combine(root, "missing.iso")));
        Assert.Equal(2, r.Code);
        Assert.Contains("missing.iso", r.Err);
        Assert.False(Directory.Exists(Workspace));
        Assert.Equal(0, engine.Calls);
    }

    [Fact]
    public async Task Relative_iso_path_is_rejected_by_path_safety()
    {
        var r = await Run(new FakeEngine(), default, Build(Recipe(false), @"..\win10.iso"));
        Assert.Equal(2, r.Code);
        Assert.False(Directory.Exists(Workspace));
    }

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("")]
    public async Task Unknown_or_missing_subcommand_exits_2(string sub)
    {
        var r = await Run(new FakeEngine(), default, sub.Length == 0 ? Array.Empty<string>() : new[] { sub });
        Assert.Equal(2, r.Code);
        Assert.NotEmpty(r.Err);
    }

    [Fact]
    public async Task Missing_required_argument_exits_2()
    {
        var r = await Run(new FakeEngine(), default, "build", "--recipe", Recipe(false), "--iso", Iso());
        Assert.Equal(2, r.Code);
        Assert.Contains("--workspace", r.Err);
        Assert.False(Directory.Exists(Workspace));
    }

    [Fact]
    public async Task Iso_without_pro_exits_2_with_message_on_stderr_only()
    {
        var engine = new FakeEngine((_, _) => throw new InvalidDataException(ProMessage));
        var r = await Run(engine, default, Build(Recipe(true), Iso()));
        Assert.Equal(2, r.Code);
        Assert.Contains(ProMessage, r.Err);
        Assert.Equal("", r.Out);
        Assert.False(Directory.Exists(Workspace));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Success_writes_report_with_exactly_the_five_fields(bool profile)
    {
        var iso = Iso();
        var engine = new FakeEngine();
        var r = await Run(engine, default, Build(Recipe(profile), iso));
        Assert.Equal(0, r.Code);
        Assert.Contains("ABC123", r.Out);
        Assert.Equal(iso, engine.Seen!.SourcePath);
        Assert.Equal(Workspace, engine.Seen.WorkspacePath);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Workspace, "report.json")));
        var props = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal(new[] { "EdgePreserved", "IsoPath", "SourceBuild", "Sha256", "WebView2Present" }.Order(), props.Keys.Order());
        Assert.Equal(engine.Seen.OutputPath, props["IsoPath"].GetString());
        Assert.Equal("ABC123", props["Sha256"].GetString());
        Assert.Equal("10.0.19045.5555", props["SourceBuild"].GetString());
        Assert.Equal(profile ? JsonValueKind.True : JsonValueKind.False, props["EdgePreserved"].ValueKind);
        Assert.Equal(JsonValueKind.False, props["WebView2Present"].ValueKind);
    }

    [Fact]
    public async Task Other_exception_exits_1_and_stack_trace_only_with_verbose()
    {
        FakeEngine Boom() => new((_, _) => throw new InvalidOperationException("dism exploded"));
        var quiet = await Run(Boom(), default, Build(Recipe(false), Iso()));
        Assert.Equal(1, quiet.Code);
        Assert.Contains("dism exploded", quiet.Err);
        Assert.DoesNotContain("   at ", quiet.Err);
        var loud = await Run(Boom(), default, Build(Recipe(false), Iso()).Append("--verbose").ToArray());
        Assert.Equal(1, loud.Code);
        Assert.Contains("InvalidOperationException", loud.Err);
    }

    [Fact]
    public async Task Cancellation_exits_1_with_cancelled()
    {
        using var cts = new CancellationTokenSource();
        var engine = new FakeEngine((_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult<CliBuildResult>(null!); });
        var r = await Run(engine, cts.Token, Build(Recipe(false), Iso()));
        Assert.Equal(1, r.Code);
        Assert.Contains("Cancelled", r.Err);
        Assert.False(File.Exists(Path.Combine(Workspace, "report.json")));
    }

    // --- oscdimg acquisition (BuildEngineAdapter seams; no network) ---
    private static CliBuildResult Ok(BuildRecipe r) => new(r.OutputPath, "ABC123", "10.0.19045.5555", false);

    private static BuildEngineAdapter Adapter(Func<string> find, Func<string, IProgress<string>?, CancellationToken, Task<string>> acquire,
        Action<BuildRecipe> seen)
        => new(acquire, find, (r, _, _) => { seen(r); return Task.FromResult(Ok(r)); });

    [Fact]
    public async Task Missing_oscdimg_invokes_acquirer_and_passes_its_path_to_the_engine()
    {
        var calls = 0; string? dir = null; BuildRecipe? seen = null;
        var adapter = Adapter(() => "", (d, _, _) => { calls++; dir = d; return Task.FromResult(@"C:\tools\oscdimg.exe"); }, r => seen = r);
        var r = await Run2(adapter, Build(Recipe(false), Iso()));
        Assert.Equal(0, r.Code);
        Assert.Equal(1, calls);
        Assert.Equal(@"C:\tools\oscdimg.exe", seen!.OscdimgPath);
        Assert.EndsWith(Path.Combine("Tiny10Builder", "tools"), dir);
        Assert.False(dir!.StartsWith(Workspace, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Recipe_with_oscdimg_path_does_not_invoke_finder_or_acquirer()
    {
        var recipe = await BuildEngineAdapter.ResolveOscdimgAsync(new BuildRecipe { OscdimgPath = @"D:\o\oscdimg.exe" },
            () => throw new Xunit.Sdk.XunitException("find called"),
            (_, _, _) => throw new Xunit.Sdk.XunitException("acquire called"), null, default);
        Assert.Equal(@"D:\o\oscdimg.exe", recipe.OscdimgPath);
    }

    [Fact]
    public async Task Installed_oscdimg_does_not_invoke_acquirer()
    {
        var calls = 0; BuildRecipe? seen = null;
        var adapter = Adapter(() => @"C:\adk\oscdimg.exe", (_, _, _) => { calls++; return Task.FromResult("x"); }, r => seen = r);
        var r = await Run2(adapter, Build(Recipe(false), Iso()));
        Assert.Equal(0, r.Code);
        Assert.Equal(0, calls);
        Assert.Equal(@"C:\adk\oscdimg.exe", seen!.OscdimgPath);
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("network")]
    public async Task Acquirer_failure_exits_1_never_2(string kind)
    {
        Exception ex = kind == "hash" ? new InvalidDataException("checksum does not match") : new HttpRequestException("no network");
        var adapter = Adapter(() => "", (_, _, _) => throw ex, _ => { });
        var r = await Run2(adapter, Build(Recipe(false), Iso()));
        Assert.Equal(1, r.Code);
        Assert.Contains(ex.Message, r.Err);
    }

    [Fact]
    public async Task Acquirer_progress_messages_are_forwarded_as_build_progress()
    {
        var got = new List<BuildProgress>();
        var recipe = await BuildEngineAdapter.ResolveOscdimgAsync(new BuildRecipe(), () => "",
            (_, p, _) => { p!.Report("Downloading tool."); return Task.FromResult("x"); },
            new SyncProgress(got.Add), default);
        Assert.Equal("x", recipe.OscdimgPath);
        Assert.Contains(got, g => g.Message == "Downloading tool.");
    }

    private sealed class SyncProgress(Action<BuildProgress> a) : IProgress<BuildProgress>
    {
        public void Report(BuildProgress value) => a(value);
    }

    private async Task<(int Code, string Out, string Err)> Run2(ICliEngine engine, params string[] args)
    {
        var o = new StringWriter(); var e = new StringWriter();
        var code = await CliApp.RunAsync(args, engine, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void Production_http_handler_does_not_auto_follow_redirects()
    {
        using var handler = BuildEngineAdapter.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        using var client = BuildEngineAdapter.CreateClient();
        Assert.Equal(TimeSpan.FromMinutes(5), client.Timeout);
    }
}
