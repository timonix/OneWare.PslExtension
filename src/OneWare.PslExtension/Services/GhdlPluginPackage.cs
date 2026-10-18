using OneWare.Essentials.PackageManager;

namespace OneWare.PslExtension.Services;

/// <summary>
/// The GHDL plugin for yosys, needed to read VHDL (and PSL) natively. The Linux and macOS OSS CAD Suite
/// include it, the Windows one does not. A yosys plugin only loads into the yosys it was built for,
/// so there is one build per OSS CAD Suite release, built and released by the fork ghdl-yosys-plugin-windows-release
/// under the tag oss-cad-suite-&lt;version&gt;. The package version is the OSS CAD Suite version it belongs to.
/// </summary>
public static class GhdlPluginPackage
{
    public const string Id = "pslextension-ghdl-plugin";

    private const string ReleaseUrl = "https://github.com/timonix/ghdl-yosys-plugin-windows-release/releases/download";

    /// <summary>OSS CAD Suite versions (as in its VERSION file) that have a Windows plugin build.</summary>
    private static readonly string[] OssCadSuiteVersions = ["20250827", "20260219", "20260817"];

    public static readonly Package Package = new()
    {
        Category = "Binaries",
        Id = Id,
        Type = "NativeTool",
        Name = "OSS CAD Suite, GHDL plugin",
        Description = "Lets the yosys of the Windows OSS CAD Suite read VHDL. Used by the PSL Extension to run SymbiYosys without Docker, install the version of your OSS CAD Suite",
        License = "GPL-3.0-or-later",
        Links = [new PackageLink { Name = "GitHub", Url = "https://github.com/timonix/ghdl-yosys-plugin-windows-release" }],
        Versions = OssCadSuiteVersions.Select(CreateVersion).ToArray()
    };

    /// <summary>Package version for an OSS CAD Suite VERSION file value, e.g. 20260817 -> 2026.08.17</summary>
    public static string ToPackageVersion(string ossCadSuiteVersion)
    {
        return ossCadSuiteVersion.Length == 8
            ? $"{ossCadSuiteVersion[..4]}.{ossCadSuiteVersion[4..6]}.{ossCadSuiteVersion[6..]}"
            : ossCadSuiteVersion;
    }

    public static PackageVersion? FindVersion(string ossCadSuiteVersion)
    {
        var version = ToPackageVersion(ossCadSuiteVersion);
        return Package.Versions!.FirstOrDefault(x => x.Version == version);
    }

    private static PackageVersion CreateVersion(string ossCadSuiteVersion)
    {
        var version = ToPackageVersion(ossCadSuiteVersion);
        return new PackageVersion
        {
            Version = version,
            Targets =
            [
                new PackageTarget
                {
                    Target = "win-x64",
                    Url = $"{ReleaseUrl}/oss-cad-suite-{version}/ghdl-plugin-win-x64-{ossCadSuiteVersion}.zip"
                }
            ]
        };
    }
}
