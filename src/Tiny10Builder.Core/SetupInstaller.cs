namespace Tiny10Builder.Core;

public static class SetupInstaller
{
    /// <summary>
    /// Always false: stock Windows 10 22H2 sources\setup.exe is already the classic installer and honours autounattend.xml,
    /// so no winpeshl.ini override is needed (see docs/windows10-notes.md, "Spike result").
    /// </summary>
    public static bool RequiresLegacy(BuildRecipe recipe) => false;

    /// <summary>Only the Proxmox profile edits boot.wim (vioscsi into every index); other builds leave it untouched.</summary>
    internal static bool RequiresBootEdit(BuildRecipe recipe) =>
        recipe.TemplateProfile == TemplateProfile.ProxmoxCloudbaseInit;
}
