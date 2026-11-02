using System.Runtime.Versioning;
using Tiny10Builder.Core;
using Xunit;

namespace Tiny10Builder.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class EdgeKeepTests
{
    private static readonly string[] EdgeDirectories =
    [
        @"Program Files\Microsoft\Edge", @"Program Files (x86)\Microsoft\Edge",
        @"Program Files\Microsoft\EdgeUpdate", @"Program Files (x86)\Microsoft\EdgeUpdate",
        @"Program Files\Microsoft\EdgeCore", @"Program Files (x86)\Microsoft\EdgeCore",
        @"Program Files\Microsoft\EdgeWebView", @"Program Files (x86)\Microsoft\EdgeWebView"
    ];

    private static string Seed(string mount, IEnumerable<string> relativeDirectories)
    {
        foreach (var directory in relativeDirectories)
        {
            Directory.CreateDirectory(Path.Combine(mount, directory));
            File.WriteAllText(Path.Combine(mount, directory, "payload.bin"), "original payload");
        }
        return mount;
    }

    [Fact]
    public void Proxmox_profile_keeps_every_Edge_tree_and_still_removes_OneDriveSetup()
    {
        using var fixture = new BuildFixture();
        var mount = NativeTests.CreateOwnedMount(fixture.Root);
        Seed(mount, EdgeDirectories.Append(@"Windows\System32"));
        File.WriteAllText(Path.Combine(mount, @"Windows\System32\OneDriveSetup.exe"), "x");
        new ImageOperations().RemoveOriginalFiles(mount, "x64", ["onedrive-files"], _ => { }, false, TemplateProfile.ProxmoxCloudbaseInit);
        Assert.All(EdgeDirectories, d => Assert.True(File.Exists(Path.Combine(mount, d, "payload.bin")), d));
        Assert.False(File.Exists(Path.Combine(mount, @"Windows\System32\OneDriveSetup.exe")));
    }

    [Fact]
    public void Proxmox_profile_keeps_Edge_even_when_a_custom_recipe_selects_edge_files()
    {
        using var fixture = new BuildFixture();
        var mount = NativeTests.CreateOwnedMount(fixture.Root);
        Seed(mount, EdgeDirectories);
        var log = new List<string>();
        new ImageOperations().RemoveOriginalFiles(mount, "x64", ["edge-files"], log.Add, false, TemplateProfile.ProxmoxCloudbaseInit);
        Assert.All(EdgeDirectories, d => Assert.True(File.Exists(Path.Combine(mount, d, "payload.bin")), d));
        Assert.Contains(log, l => l.Contains("preserved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Without_the_profile_edge_files_still_deletes_the_Edge_trees()
    {
        using var fixture = new BuildFixture();
        var mount = NativeTests.CreateOwnedMount(fixture.Root);
        Seed(mount, EdgeDirectories);
        new ImageOperations().RemoveOriginalFiles(mount, "x64", ["edge-files"], _ => { });
        Assert.All(EdgeDirectories, d => Assert.False(Directory.Exists(Path.Combine(mount, d)), d));
    }

    [Fact]
    public void TrimCore_with_the_profile_keeps_Edge_WinSxS_families_and_removes_unrelated_ones()
    {
        using var fixture = new BuildFixture();
        var mount = NativeTests.CreateOwnedMount(fixture.Root);
        var store = Path.Combine(mount, @"Windows\WinSxS");
        string[] keep =
        [
            "amd64_microsoft-windows-edge-angle_31bf3856ad364e35_10.0.19041.1_none_x",
            "amd64_microsoft-windows-e..crosoftedgedevtools_31bf3856ad364e35_10.0.19041.1_none_x",
            "amd64_microsoft-windows-e..microsoftedgebchost_31bf3856ad364e35_10.0.19041.1_none_x",
            "amd64_microsoft-edge-webview_fixture"
        ];
        string[] core = ["amd64_microsoft.windows.common-controls_x", "amd64_microsoft.windows.gdiplus_x", "amd64_microsoft-windows-servicingstack_x"];
        foreach (var name in keep.Concat(core).Append("amd64_unrelated-optional_x"))
        {
            Directory.CreateDirectory(Path.Combine(store, name));
            File.WriteAllText(Path.Combine(store, name, "payload.dll"), "x");
        }
        new ImageOperations().TrimCore(mount, "x64", _ => { }, CancellationToken.None, false, TemplateProfile.ProxmoxCloudbaseInit);
        Assert.All(keep.Concat(core), n => Assert.True(Directory.Exists(Path.Combine(store, n)), n));
        Assert.False(Directory.Exists(Path.Combine(store, "amd64_unrelated-optional_x")));
    }

    [Fact]
    public void EdgeKeepPatterns_cover_the_measured_Windows_10_Edge_families()
    {
        foreach (var required in new[] { "microsoft-edge", "microsoft-windows-edge", "microsoftedge", "microsoft-edge-webview" })
            Assert.Contains(required, ImageOperations.EdgeKeepPatterns, StringComparer.OrdinalIgnoreCase);
    }
}
