using Tiny10Builder.Core;
using System.Text.RegularExpressions;
using Xunit;

namespace Tiny10Builder.Core.Tests;

public sealed class TemplateGuestPolicyTests
{
    // AC-S4: The observed boot-storage state gates preparation and Sysprep, without a storage installer.
    [Fact]
    public void Scsi_boot_health_gates_preparation_and_finalization()
    {
        Assert.Contains("$storage = Get-TemplateScsiBootDriverState", TemplateGuestAssets.PrepareScript);
        Assert.Contains("if (-not $storage.Healthy)", TemplateGuestAssets.PrepareScript);
        var finalizer = TemplateGuestAssets.FinalizeScript;
        Assert.True(finalizer.IndexOf("$storage = Get-TemplateScsiBootDriverState", StringComparison.Ordinal) <
                    finalizer.IndexOf("Start-Process $sysprep", StringComparison.Ordinal));
        Assert.Contains("if (-not $storage.Healthy)", finalizer);
        Assert.DoesNotContain("drivers\\vioscsi.inf", TemplateGuestAssets.PrepareScript);
        Assert.DoesNotContain("pnputil", finalizer, StringComparison.OrdinalIgnoreCase);
    }

    // GP-08 / GP-T14: Approved policy does not force the clock or alter RTC.
    [Fact]
    public void Policy_keeps_nla_firewall_and_windows_time_management()
    {
        var policy = TemplateGuestAssets.PolicyScript;
        Assert.Contains("-Name UserAuthentication -Value 1", policy);
        Assert.Contains("Set-NetFirewallProfile -Profile Domain,Private,Public -Enabled True", policy);
        Assert.Contains("-LocalPort 3389 -RemoteAddress Any", policy);
        Assert.Contains("Set-Service W32Time -StartupType Automatic", policy);
        Assert.DoesNotContain("RealTimeIsUniversal", policy);
        Assert.DoesNotContain("w32tm.exe", policy);
        Assert.DoesNotContain("Set-Date", policy);
        Assert.DoesNotContain("10.10.", policy);
    }

    // GP-08 / GP-T14: VerifyOnly does not write policy; runtime checks remain necessary.
    [Fact]
    public void Preparation_and_finalize_verify_profile_policy_after_integrity()
    {
        Assert.Contains("param([switch]$VerifyOnly, [switch]$ActivateRdp)", TemplateGuestAssets.PolicyScript);
        Assert.Contains("if (-not $VerifyOnly)", TemplateGuestAssets.PolicyScript);
        Assert.Contains("if (-not $VerifyOnly -and $ActivateRdp)", TemplateGuestAssets.PolicyScript);
        Assert.True(TemplateGuestAssets.PrepareScript.IndexOf("Assert-TemplatePreparationContext", StringComparison.Ordinal) <
                    TemplateGuestAssets.PrepareScript.IndexOf("'Configure guest policy'", StringComparison.Ordinal));
        Assert.Contains("Configure-GuestPolicy.ps1') -VerifyOnly", TemplateGuestAssets.FinalizeScript);
    }

    // Module-backed cmdlets (NetSecurity, Storage, PnpDevice, CimCmdlets, LocalAccounts, Security) that a Core-trimmed
    // Windows 10 image must still resolve. The on-VM check (Task 11) uses exactly this list. The Python plugin
    // additionally calls Get-CimInstance, Get-NetIPAddress, Get-NetRoute and Get-DnsClientServerAddress.
    private static readonly Regex ModuleCmdlet = new(
        @"\b[A-Z][a-z]+-(?:Net[A-Za-z]+|Partition|PnpDevice[A-Za-z]*|CimInstance|DnsClient[A-Za-z]*|LocalUser|AuthenticodeSignature)\b",
        RegexOptions.Compiled);

    public static TheoryData<string, string> ExpectedModuleCmdlets => new()
    {
        { "Configure-GuestPolicy.ps1", "Get-CimInstance Get-NetFirewallAddressFilter Get-NetFirewallPortFilter Get-NetFirewallProfile Get-NetFirewallRule New-NetFirewallRule Remove-NetFirewallRule Set-NetFirewallProfile" },
        { "Create-DisabledServiceTransform.ps1", "" },
        { "Finalize-Template.ps1", "Get-CimInstance Set-LocalUser" },
        { "Initialize-Clone.ps1", "Get-CimInstance" },
        { "Prepare-Template.ps1", "Get-AuthenticodeSignature Get-CimInstance" },
        { "Template-State.ps1", "Get-CimInstance Get-Partition Get-PnpDeviceProperty" },
    };

    [Theory]
    [MemberData(nameof(ExpectedModuleCmdlets))]
    public void Guest_scripts_depend_on_the_known_module_cmdlets(string script, string expected)
    {
        var text = script switch
        {
            "Configure-GuestPolicy.ps1" => TemplateGuestAssets.PolicyScript,
            "Create-DisabledServiceTransform.ps1" => TemplateGuestAssets.TransformScript,
            "Finalize-Template.ps1" => TemplateGuestAssets.FinalizeScript,
            "Initialize-Clone.ps1" => TemplateGuestAssets.CloneScript,
            "Prepare-Template.ps1" => TemplateGuestAssets.PrepareScript,
            _ => TemplateGuestAssets.StateScript,
        };
        var actual = ModuleCmdlet.Matches(text).Select(m => m.Value).Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(expected.Split(' ', StringSplitOptions.RemoveEmptyEntries), actual);
    }

    // Template-State.ps1 duplicates the driver identity as literals; they must track the C# pins.
    [Fact]
    public void State_script_driver_literals_equal_the_provenance_and_stager_pins()
    {
        var state = TemplateGuestAssets.StateScript;
        Assert.Contains("$driver.DriverVersion -eq '" + VirtioDriverProvenance.DriverVersion + "'", state);
        var literals = Regex.Matches(state, "[0-9A-F]{64}").Select(m => m.Value).ToHashSet();
        var mentioned = new[] { "vioser.inf", "vioser.sys", "vioscsi.inf", "vioscsi.sys" };
        foreach (var name in mentioned)
            Assert.Contains(TemplatePayloadStager.DriverHashes[name], literals);
        Assert.Equal(mentioned.Length, literals.Count);
        Assert.All(literals, hash => Assert.Contains(hash, TemplatePayloadStager.DriverHashes.Values));
    }
}
