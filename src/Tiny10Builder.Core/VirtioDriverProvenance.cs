namespace Tiny10Builder.Core;

/// <summary>
/// Single record of where the embedded virtio drivers (Template/Drivers) come from. The SHA-256 pins live in
/// <see cref="TemplatePayloadStager.DriverHashes"/>; docs/virtio-scsi-driver.md describes the same source.
/// </summary>
internal static class VirtioDriverProvenance
{
    /// <summary>Volume label of the virtio-win ISO the files were verified against.</summary>
    internal const string IsoLabel = "virtio-win-0.1.240";
    /// <summary>SHA-256 of that ISO (627,519,488 bytes).</summary>
    internal const string IsoSha256 = "EBD48258668F7F78E026ED276C28A9D19D83E020FFA080AD69910DC86BBCBCC6";
    internal const string VioscsiSourceDirectory = "vioscsi/w10/amd64";
    internal const string VioserialSourceDirectory = "vioserial/w10/amd64";
    /// <summary>INF DriverVer version of both drivers (date 07/21/2023).</summary>
    internal const string DriverVersion = "100.93.104.24000";
    /// <summary>INF [Manufacturer] decoration: Windows 8.1 and later, which includes Windows 10.</summary>
    internal const string InfDecoration = "NTamd64.6.3";
}
