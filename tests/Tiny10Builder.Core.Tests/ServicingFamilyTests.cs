using System.Runtime.Versioning;
using Tiny10Builder.Core;
using Xunit;

namespace Tiny10Builder.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class ServicingFamilyTests
{
    private const string CheckMessage = "The final installation image failed its edition/build/architecture check.";

    [Theory]
    [InlineData(19041, 19045, true)]
    [InlineData(19045, 19041, true)]
    [InlineData(19045, 22621, false)]
    [InlineData(22621, 22631, true)]
    [InlineData(26100, 26200, true)]
    [InlineData(22000, 22621, false)]
    public void SameServicingFamily_compares_servicing_bases(int a, int b, bool expected) =>
        Assert.Equal(expected, ToolLocator.SameServicingFamily(a, b));

    private static bool IsCompressedExport(string file) => file.Contains(@"\compress\", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task A_re_serviced_export_reading_19045_is_accepted_for_a_19041_source()
    {
        using var fixture = new BuildFixture(19041, "x64");
        fixture.Runner.ReadingFor = file => IsCompressedExport(file) ? (19045, "x64") : null;
        var result = await fixture.Engine.BuildAsync(fixture.Recipe, null, CancellationToken.None);
        Assert.True(File.Exists(result.IsoPath));
    }

    [Fact]
    public async Task An_export_of_another_servicing_family_still_fails_the_final_check()
    {
        using var fixture = new BuildFixture(19041, "x64");
        fixture.Runner.ReadingFor = file => IsCompressedExport(file) ? (22621, "x64") : null;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Engine.BuildAsync(fixture.Recipe, null, CancellationToken.None));
        Assert.Contains(CheckMessage, error.ToString());
    }

    [Fact]
    public async Task An_export_with_another_architecture_still_fails_the_final_check()
    {
        using var fixture = new BuildFixture(19041, "x64");
        fixture.Runner.ReadingFor = file => IsCompressedExport(file) ? (19045, "x86") : null;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Engine.BuildAsync(fixture.Recipe, null, CancellationToken.None));
        Assert.Contains(CheckMessage, error.ToString());
    }

    [Fact]
    public async Task Resume_accepts_a_re_serviced_working_image_reading_19045_against_a_recorded_19041()
    {
        using var fixture = new BuildFixture(19041, "x64");
        var job = await JobScenarios.InterruptAtCommitAsync(fixture);
        fixture.Runner.ReadingFor = file => Path.GetFileName(file).Equals("working.wim", StringComparison.OrdinalIgnoreCase) ||
            IsCompressedExport(file) ? (19045, "x64") : null;
        var result = await fixture.Engine.ResumeAsync(JobScenarios.JobFile(job), JobScenarios.OriginalRecipe(job.WorkDirectory),
            null, CancellationToken.None);
        Assert.True(File.Exists(result.IsoPath));
    }

    [Fact]
    public async Task Resume_still_refuses_a_working_image_of_another_servicing_family()
    {
        using var fixture = new BuildFixture(19041, "x64");
        var job = await JobScenarios.InterruptAtCommitAsync(fixture);
        fixture.Runner.ReadingFor = file => Path.GetFileName(file).Equals("working.wim", StringComparison.OrdinalIgnoreCase) ? (22621, "x64") : null;
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Engine.ResumeAsync(
            JobScenarios.JobFile(job), JobScenarios.OriginalRecipe(job.WorkDirectory), null, CancellationToken.None));
        Assert.Contains("different Windows image", refused.Message);
    }
}
