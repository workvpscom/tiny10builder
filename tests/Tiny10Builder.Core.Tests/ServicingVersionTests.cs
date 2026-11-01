using System.Runtime.Versioning;
using Tiny10Builder.Core;
using Xunit;

namespace Tiny10Builder.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class ServicingVersionTests
{
    private static WindowsImage Image(int build) =>
        new(1, "Windows 11 Pro", "", "x64", new(10, 0, build, 9457), "Professional", "Client", 1);

    private static WindowsImage Image10(int build, int revision) =>
        new(1, "Windows 10 Pro", "", "x64", new(10, 0, build, revision), "Professional", "Client", 1);

    [Fact]
    public void Windows_10_inbox_tools_with_legacy_6_2_file_version_are_accepted_for_a_19041_image()
    {
        ToolLocator.EnsureServicingVersion(new Version(6, 2, 19041, 746), Image10(19041, 2965));
        ToolLocator.EnsureServicingVersion(new Version(10, 0, 19041, 1), Image10(19041, 2965));
    }

    [Fact]
    public void Legacy_6_2_numbered_Windows_10_tools_are_still_checked_against_the_servicing_base()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ToolLocator.EnsureServicingVersion(new Version(6, 2, 19041, 746), Image(22621)));
        Assert.Contains("6.2.19041.746", error.Message);
        Assert.Contains("build 22621", error.Message);
    }

    [Fact]
    public void Genuine_Windows_8_tools_below_10240_are_still_rejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ToolLocator.EnsureServicingVersion(new Version(6, 3, 9600, 17031), Image10(19041, 2965)));
        Assert.Throws<InvalidOperationException>(() =>
            ToolLocator.EnsureServicingVersion(new Version(6, 2, 9200, 0), Image10(19041, 2965)));
    }

    [Fact]
    public void NormalizeToolVersion_maps_only_Windows_10_numbered_6_2_servicing_versions()
    {
        Assert.Equal(new Version(10, 0, 19041, 746), ToolLocator.NormalizeToolVersion(new Version(6, 2, 19041, 746)));
        Assert.Equal(new Version(6, 2, 9200, 0), ToolLocator.NormalizeToolVersion(new Version(6, 2, 9200, 0)));
        Assert.Equal(new Version(10, 0, 22621, 1), ToolLocator.NormalizeToolVersion(new Version(10, 0, 22621, 1)));
    }

    [Theory]
    [InlineData(26100, 6584, 26300)]
    [InlineData(26100, 9457, 26300)]
    [InlineData(26100, 1, 26200)]
    [InlineData(22621, 1, 22631)]
    [InlineData(19041, 1, 19045)]
    [InlineData(19041, 1, 19041)]
    [InlineData(19045, 1, 19045)]
    [InlineData(26100, 1, 22631)]
    [InlineData(29691, 1, 26300)]
    [InlineData(29691, 1, 29691)]
    public void Known_shared_servicing_families_are_accepted_without_changing_image_identity(int tool, int revision, int build)
    {
        var image = Image(build);
        ToolLocator.EnsureServicingVersion(new Version(10, 0, tool, revision), image);
        Assert.Equal(build, image.Version.Build);
        Assert.Equal(9457, image.Version.Revision);
    }

    [Theory]
    [InlineData(19041, 22621)]
    [InlineData(22621, 26100)]
    [InlineData(26100, 28000)]
    [InlineData(26100, 29691)]
    [InlineData(26100, 26301)]
    [InlineData(26200, 26201)]
    [InlineData(29691, 99999)]
    public void Older_servicing_families_and_unknown_newer_builds_are_not_silently_allowed(int tool, int build)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ToolLocator.EnsureServicingVersion(new Version(10, 0, tool, 1000), Image(build)));
        Assert.Contains($"build {build}", error.Message);
        Assert.Contains("detects them automatically", error.Message);
        Assert.Contains("Windows ADK", error.Message);
        Assert.DoesNotContain("Select a matching", error.Message);
    }

    [Fact]
    public void A_newer_executable_wrapper_cannot_hide_an_older_or_missing_engine()
    {
        var wrapper = new Version(10, 0, 28000, 2113);
        var api = new Version(10, 0, 29691, 1000);
        Assert.Equal(api, ToolLocator.ResolveServicingVersion(wrapper, api, api));
        Assert.Equal(wrapper, ToolLocator.ResolveServicingVersion(wrapper, api, null));
        var older = new Version(10, 0, 26100, 6584);
        Assert.Equal(older, ToolLocator.ResolveServicingVersion(wrapper, api, older));
        Assert.Equal(older, ToolLocator.ResolveServicingVersion(wrapper, older, api));
        Assert.Throws<InvalidOperationException>(() => ToolLocator.EnsureServicingVersion(new Version(6, 3, 99999), Image(26300)));
    }

    [Fact]
    public async Task Installed_inbox_provider_build_agrees_with_the_actual_DISM_banner()
    {
        var dism = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\dism.exe");
        var version = ToolLocator.ServicingVersionOf(dism);
        var process = await new ProcessRunner().RunAsync(dism, ["/English", "/?"], null, CancellationToken.None);
        Assert.Equal(0, process.ExitCode);
        Assert.Contains($"Version: {version.Major}.{version.Minor}.{version.Build}.", process.Output);
    }

    [Fact]
    public async Task Existing_26100_ADK_accepts_real_26300_metadata_without_rewriting_it_when_configured()
    {
        var iso = Environment.GetEnvironmentVariable("TINY11_SERVICING_ISO");
        var tool = Environment.GetEnvironmentVariable("TINY11_SERVICING_DISM");
        if (string.IsNullOrEmpty(iso) || string.IsNullOrEmpty(tool)) return;
        ToolLocator.Validate(tool, "dism.exe");
        Assert.Equal(26100, ToolLocator.ServicingVersionOf(tool).Build);
        var originalTime = File.GetLastWriteTimeUtc(iso);
        using (var source = new MediaSourceFactory().Open(iso))
        {
            var images = await new DismClient(tool, new ProcessRunner()).GetImagesAsync(
                InstallationMedia.ImageFile(source.RootPath), CancellationToken.None);
            var image = Assert.Single(images);
            Assert.Equal(26300, image.Version.Build);
            ToolLocator.EnsureServicingVersion(tool, image);
            Assert.Equal(26300, image.Version.Build);
            Assert.Throws<InvalidOperationException>(() => ToolLocator.EnsureServicingVersion(
                new Version(10, 0, 22621, 1), image));
        }
        Assert.Equal(originalTime, File.GetLastWriteTimeUtc(iso));
    }
}
