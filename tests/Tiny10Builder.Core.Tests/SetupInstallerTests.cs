using System.Runtime.Versioning;
using System.Text.Json;
using Tiny10Builder.Core;
using Xunit;

namespace Tiny10Builder.Core.Tests;

[SupportedOSPlatform("windows")]
public sealed class SetupInstallerTests
{
    // AC-S2: Even regular profile media must expose the SCSI target before installation.
    [Theory]
    [InlineData(TemplateProfile.None, false)]
    [InlineData(TemplateProfile.ProxmoxCloudbaseInit, true)]
    public void Template_profile_alone_requires_boot_edit(TemplateProfile profile, bool expected) =>
        Assert.Equal(expected, SetupInstaller.RequiresBootEdit(new() { Flavor = BuildFlavor.Regular, SelectedOptions = [], TemplateProfile = profile }));

    [Fact]
    public void Win10_media_never_needs_the_legacy_installer_so_Core_and_component_removal_do_not_force_a_boot_edit()
    {
        // Spike result: stock Windows 10 22H2 sources\\setup.exe is already the classic installer and honours autounattend.xml.
        foreach (var flavor in new[] { BuildFlavor.Core, BuildFlavor.Regular })
        {
            var recipe = new BuildRecipe { Flavor = flavor };
            Assert.False(SetupInstaller.RequiresLegacy(recipe));
            Assert.False(SetupInstaller.RequiresBootEdit(recipe));
        }
        var componentRemoval = new BuildRecipe
        {
            ExpertActions = [new(ImageItemKind.ComponentPayload, "amd64_component", ExpertActionMode.Remove)]
        };
        Assert.False(SetupInstaller.RequiresLegacy(componentRemoval));
        Assert.False(SetupInstaller.RequiresBootEdit(componentRemoval));
        Assert.True(SetupInstaller.RequiresBootEdit(new BuildRecipe { Flavor = BuildFlavor.Core, TemplateProfile = TemplateProfile.ProxmoxCloudbaseInit }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Core_and_custom_component_deletion_leave_Setup_untouched_without_winpeshl_or_a_legacy_report_flag(bool custom)
    {
        using var fixture = new BuildFixture();
        var recipe = fixture.Recipe with
        {
            SelectedOptions = [], KeepWorkingFiles = true,
            Flavor = custom ? BuildFlavor.Regular : BuildFlavor.Core, CoreAcknowledged = !custom,
            ExpertAcknowledged = custom,
            ExpertActions = custom ? [new(ImageItemKind.ComponentPayload, "amd64_critical-component", ExpertActionMode.Remove)] : []
        };
        var sawWinpeshl = false;
        fixture.Runner.OnCommand = args =>
        {
            if (!args.Contains("/Unmount-Image") || !args.Contains("/Commit")) return;
            var mount = args.Single(arg => arg.StartsWith("/MountDir:"))["/MountDir:".Length..];
            sawWinpeshl |= File.Exists(Path.Combine(mount, @"Windows\System32\winpeshl.ini"));
        };
        var result = await fixture.Engine.BuildAsync(recipe, null, CancellationToken.None);
        Assert.False(sawWinpeshl);
        Assert.Single(fixture.Runner.Calls, args => args.Contains("/Mount-Image"));
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(fixture.Source, @"sources\boot.wim")),
            await File.ReadAllBytesAsync(Path.Combine(result.WorkDirectory, @"media\sources\boot.wim")));
        using var report = JsonDocument.Parse(File.ReadAllText(result.ReportPath));
        Assert.False(report.RootElement.TryGetProperty("LegacyInstallerEnabled", out _));
    }

    // AC-S2: The boot-image stage is exactly what the Proxmox profile runs (the seam only forces it for a unit-test recipe).
    [Fact]
    public async Task Every_boot_image_index_gets_vioscsi_and_is_committed_on_a_private_copy_before_the_atomic_replace()
    {
        using var fixture = new BuildFixture();
        var recipe = fixture.UseSetupImageEdit(fixture.Recipe with { KeepWorkingFiles = true });
        var original = await File.ReadAllBytesAsync(Path.Combine(fixture.Source, @"sources\boot.wim"));
        var stagedDuringEdits = new List<bool>();
        fixture.Runner.OnCommand = args =>
        {
            if (!args.Contains("/Unmount-Image") || !args.Contains("/Commit")) return;
            var job = Path.GetDirectoryName(args.Single(arg => arg.StartsWith("/MountDir:"))["/MountDir:".Length..])!;
            var bootWim = Path.Combine(job, @"media\sources\boot.wim");
            if (File.Exists(bootWim)) stagedDuringEdits.Add(File.ReadAllBytes(bootWim).AsSpan().SequenceEqual(original));
        };
        var result = await fixture.Engine.BuildAsync(recipe, null, CancellationToken.None);
        var calls = fixture.Runner.Calls;
        var privateBoot = Path.Combine(result.WorkDirectory, "setup", "boot.wim");
        var bootMounts = calls.Where(args => args.Contains("/Mount-Image") && args.Contains("/ImageFile:" + privateBoot)).ToArray();
        Assert.Equal(2, bootMounts.Length);
        Assert.Contains(bootMounts, args => args.Contains("/Index:1"));
        Assert.Contains(bootMounts, args => args.Contains("/Index:2"));
        var drivers = calls.Where(args => args.Contains("/Add-Driver")).ToArray();
        Assert.Equal(2, drivers.Length);
        Assert.All(drivers, args => Assert.Contains(args, arg => arg.EndsWith("vioscsi.inf", StringComparison.Ordinal)));
        Assert.All(drivers, args => Assert.Contains(args, arg => arg.StartsWith("/Driver:", StringComparison.Ordinal) &&
            arg.Contains(@"tiny11-template\drivers\", StringComparison.OrdinalIgnoreCase)));
        // Each index: Mount-Image, then Add-Driver, then Unmount /Commit, in that order.
        var sequence = calls.Select((args, i) => (args, i)).Where(x => x.args.Contains("/Add-Driver")).Select(x => x.i).ToArray();
        foreach (var at in sequence)
        {
            Assert.Contains("/Mount-Image", calls[at - 1]);
            Assert.Contains("/Unmount-Image", calls[at + 1]);
            Assert.Contains("/Commit", calls[at + 1]);
        }
        // While the private copy was being edited the staged boot.wim in the media was never touched.
        Assert.NotEmpty(stagedDuringEdits);
        Assert.All(stagedDuringEdits, same => Assert.True(same));
        Assert.False(File.Exists(privateBoot));   // moved over the staged one atomically
    }

    // AC-S2: A failure on the second boot index leaves the staged boot.wim untouched and the whole edit repeats on resume.
    [Fact]
    public async Task A_failure_on_the_second_boot_index_leaves_the_staged_boot_wim_unchanged_and_resume_repeats_both_indexes()
    {
        using var fixture = new BuildFixture();
        var recipe = fixture.UseSetupImageEdit(fixture.Recipe with { KeepWorkingFiles = true });
        var bootMounts = 0;
        fixture.Runner.OnCommand = args =>
        {
            if (!args.Contains("/Mount-Image") || !args.Any(arg => arg.EndsWith(@"setup\boot.wim", StringComparison.Ordinal))) return;
            if (++bootMounts == 2) throw new IOException("Injected second boot-index failure");
        };
        var failure = await Assert.ThrowsAsync<BuildFailureException>(() => fixture.Engine.BuildAsync(recipe, null, CancellationToken.None));
        Assert.Equal(2, bootMounts);
        Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(fixture.Source, @"sources\boot.wim")),
            await File.ReadAllBytesAsync(Path.Combine(failure.WorkDirectory, @"media\sources\boot.wim")));
        Assert.Equal(SetupProgress.Pending, CheckpointStore.Read(failure.WorkDirectory).SetupState);
        fixture.Runner.OnCommand = null;
        var before = fixture.Runner.Calls.Count;
        await fixture.Engine.ResumeAsync(JobScenarios.JobFile(failure), recipe, null, CancellationToken.None);
        var calls = fixture.Runner.Calls.Skip(before).ToArray();
        Assert.Equal(2, calls.Count(args => args.Contains("/Mount-Image")));
        Assert.Equal(2, calls.Count(args => args.Contains("/Add-Driver") && args.Any(arg => arg.EndsWith("vioscsi.inf", StringComparison.Ordinal))));
    }

    // AC-S2: Optional real DISM validation commits and reopens every boot image; it does not build an ISO.
    [TemplateArtifactFact("TINY11_SETUP_ISO_CHECK")]
    public async Task Scsi_driver_survives_every_real_boot_image_commit_when_configured()
    {
        var iso = TemplateArtifactFactAttribute.Require("TINY11_SETUP_ISO_CHECK");
        using var fixture = new BuildFixture();
        using var media = new MediaSourceFactory().Open(iso);
        var mount = NativeTests.CreateOwnedMount(fixture.Root, fakeWindows: false);
        var job = Path.GetDirectoryName(mount)!;
        var working = Path.Combine(job, "boot.wim");
        File.Copy(Path.Combine(media.RootPath, @"sources\boot.wim"), working);
        File.SetAttributes(working, FileAttributes.Normal);
        var drivers = Path.Combine(job, "drivers");
        Directory.CreateDirectory(drivers);
        foreach (var name in new[] { "vioscsi.cat", "vioscsi.inf", "vioscsi.sys" })
            await File.WriteAllBytesAsync(Path.Combine(drivers, name), TemplateGuestAssets.Driver(name));
        var dism = new DismClient(ToolLocator.FindDism(), new ProcessRunner(), null,
            new(job, Path.Combine(job, "dism.log"), false));
        var images = await dism.GetImagesAsync(working, CancellationToken.None);
        var attempted = false;
        fixture.AllowCleanup = false;
        try
        {
            foreach (var image in images)
            {
                attempted = true;
                await dism.RunAsync(CancellationToken.None, "/Mount-Image", $"/ImageFile:{working}", $"/Index:{image.Index}", $"/MountDir:{mount}", "/CheckIntegrity");
                await dism.RunAsync(CancellationToken.None, "/Add-Driver", $"/Image:{mount}", $"/Driver:{Path.Combine(drivers, "vioscsi.inf")}");
                await dism.RunAsync(CancellationToken.None, "/Unmount-Image", $"/MountDir:{mount}", "/Commit", "/CheckIntegrity");
                attempted = false;
                attempted = true;
                await dism.RunAsync(CancellationToken.None, "/Mount-Image", $"/ImageFile:{working}", $"/Index:{image.Index}", $"/MountDir:{mount}", "/ReadOnly", "/CheckIntegrity");
                Assert.Equal(TemplateGuestAssets.Driver("vioscsi.sys"), await File.ReadAllBytesAsync(Path.Combine(mount, @"Windows\System32\drivers\vioscsi.sys")));
                var inventory = await dism.RunAsync(CancellationToken.None, "/Get-Drivers", $"/Image:{mount}");
                Assert.Contains("vioscsi.inf", inventory, StringComparison.OrdinalIgnoreCase);
                await dism.RunAsync(CancellationToken.None, "/Unmount-Image", $"/MountDir:{mount}", "/Discard");
                attempted = false;
            }
            Assert.Equal(images.Select(image => image.Index), (await dism.GetImagesAsync(working, CancellationToken.None)).Select(image => image.Index));
        }
        finally
        {
            if (attempted)
            {
                var mounted = await dism.RunAsync(CancellationToken.None, "/Get-MountedWimInfo");
                if (mounted.Contains(mount, StringComparison.OrdinalIgnoreCase))
                    await dism.RunAsync(CancellationToken.None, "/Unmount-Image", $"/MountDir:{mount}", "/Discard");
            }
            fixture.AllowCleanup = true;
        }
    }
}
