namespace Tiny10Builder.Core;

/// <summary>Platform-neutral servicing-family mapping shared by the tool check and the engine's image consistency checks.</summary>
internal static class ServicingFamily
{
    internal static int Base(int build) => build switch
    {
        19041 or 19042 or 19043 or 19044 or 19045 => 19041, // Windows 10 2004-22H2 share the 2004 servicing core.
        22631 => 22621,                  // 23H2 enables features on the 22H2 core.
        26200 or 26300 => 26100,         // 25H2 and 26H2 share the 24H2 core.
        _ => build                      // Never infer compatibility for an unknown Insider build.
    };

    /// <summary>
    /// Offline servicing refreshes the stale enablement-package build in the WIM metadata (a 22H2 image read as 19041 before and
    /// 19045 after), so two readings of the same image are compared by servicing family, never by raw build number.
    /// </summary>
    internal static bool Same(int buildA, int buildB) => Base(buildA) == Base(buildB);
}
