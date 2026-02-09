using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(PackageName),
    TitleContextProperty = nameof(Version),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public class InspectionResultView
{
    private readonly InspectionResult _data;

    public InspectionResultView(InspectionResult data)
    {
        _data = data;
    }

    [MarkoutPropertyName("Package")]
    public string PackageName => _data.PackageName;

    public string Version => _data.Version;

    public string? Description => _data.Description;

    // ===== Field Collections for Serializer =====

    [MarkoutIgnoreInTable]
    public List<MarkoutField> Summary => GetCompactFields();

    [MarkoutSection(Name = "Package")]
    public List<MarkoutField> Metadata => GetMetadataFields();

    public string? Authors => _data.Authors;
    public string? License => _data.License;
    public string? Repository => _data.Repository;

    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutPropertyName("Updated")]
    public DateTimeOffset? Published => _data.Published;

    [MarkoutSection(Name = "Statistics")]
    [MarkoutSkipNull]
    [MarkoutValueFormatter(typeof(CompactNumberFormatter))]
    [MarkoutPropertyName("Downloads")]
    public long? TotalDownloads => _data.TotalDownloads;

    [MarkoutSection(Name = "Statistics")]
    [MarkoutSkipNull]
    [MarkoutValueFormatter(typeof(CompactNumberFormatter))]
    [MarkoutPropertyName("Version Downloads")]
    public long? VersionDownloads => _data.VersionDownloads;

    [MarkoutSection(Name = "Statistics")]
    [MarkoutSkipNull]
    [MarkoutPropertyName("Version Count")]
    public int? VersionCount => _data.VersionCount;

    [MarkoutSection(Name = "Statistics")]
    [MarkoutSkipNull]
    [MarkoutValueFormatter(typeof(ByteSizeFormatter))]
    [MarkoutPropertyName("Package Size")]
    public long? PackageSize => _data.PackageSize;

    public bool? IsVerified => _data.IsVerified;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Owners")]
    public List<string>? Owners => _data.Owners;

    [MarkoutIgnoreInTable]
    public PackageDeprecation? Deprecation => _data.Deprecation;

    [MarkoutSection(Name = "Vulnerabilities")]
    [MarkoutIgnoreInTable]
    public List<PackageVulnerability>? Vulnerabilities => _data.Vulnerabilities;

    [MarkoutPropertyName("Vulnerabilities")]
    public string? VulnerabilitiesDisplay => Vulnerabilities is { Count: > 0 }
        ? $"{Vulnerabilities.Count} known ({string.Join(", ", Vulnerabilities.Select(v => v.Severity).Distinct())})"
        : null;

    [MarkoutSkipDefault]
    public bool HasReadme => _data.HasReadme;

    [MarkoutSkipDefault]
    public bool IsToolPackage => _data.IsToolPackage;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Package Types")]
    public List<string>? PackageTypes => _data.PackageTypes;

    [MarkoutPropertyName("Package Type")]
    public string PackageType => _data.ToolFormat?.Contains("Version=\"2\"") == true
        ? "Tool v2"
        : _data.IsToolPackage ? "Tool" : "Library";

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Content")]
    public List<string>? ContentDirectories => _data.ContentDirectories;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Target Frameworks")]
    public List<string>? TargetFrameworks => _data.TargetFrameworks;

    [MarkoutPropertyName("Target Frameworks")]
    public int TargetFrameworkCount => _data.TargetFrameworks?.Count ?? 0;

    [MarkoutPropertyName("TFM")]
    public string? Tfm => _data.Tfm;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Supported RIDs")]
    public List<string>? SupportedRids => _data.SupportedRids;

    [MarkoutPropertyName("Runtime Identifiers")]
    public int SupportedRidCount => _data.SupportedRids?.Count ?? 0;

    [MarkoutPropertyName("Libraries")]
    public int AssemblyCount => _data.AssemblyCount;

    [MarkoutPropertyName("Framework Dependent")]
    [MarkoutSkipDefault]
    public bool IsFrameworkDependent => _data.IsFrameworkDependent;

    [MarkoutPropertyName("RID-Specific Assets")]
    [MarkoutSkipDefault]
    public bool HasRidSpecificAssets => _data.HasRidSpecificAssets;

    [MarkoutPropertyName("Native Dependencies")]
    [MarkoutSkipDefault]
    public bool HasNativeDependencies => _data.HasNativeDependencies;

    [MarkoutPropertyName("Tool Format")]
    public string? ToolFormat => _data.ToolFormat;

    [MarkoutPropertyName("RID-Specific Pointer Package")]
    [MarkoutSkipDefault]
    public bool IsRidSpecificPointerPackage => _data.IsRidSpecificPointerPackage;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Tool Commands")]
    public List<string>? ToolCommands => _data.ToolCommands;

    [MarkoutSection(Name = "RID Packages")]
    public List<RidPackageReferenceView>? RuntimeIdentifierPackages => _data.RuntimeIdentifierPackages?
        .Select(r => new RidPackageReferenceView(r))
        .ToList();

    [MarkoutPropertyName("Runtime Target RID")]
    public string? RuntimeTargetRid => _data.RuntimeTargetRid;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Native Files")]
    public List<string>? NativeFiles => _data.NativeFiles;

    [MarkoutIgnoreInTable]
    public List<DependencyGroup>? DependencyGroups => _data.DependencyGroups;

    [MarkoutSection(Name = "Package Dependencies")]
    public List<FlatDependency>? FlatDependencies => _data.DependencyGroups?
        .OrderBy(g => GetTfmSortOrder(g.TargetFramework))
        .ThenBy(g => g.TargetFramework)
        .SelectMany(g => g.Dependencies
            .OrderBy(d => d.Id)
            .Select(d => new FlatDependency
            {
                TargetFramework = g.TargetFramework,
                Id = d.Id,
                Version = d.Version
            }))
        .ToList();

    private static int GetTfmSortOrder(string tfm)
    {
        if (tfm.StartsWith(".NETStandard", StringComparison.OrdinalIgnoreCase) ||
            tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (tfm.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase) ||
            tfm.StartsWith("net4", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 3;
    }

    [MarkoutSection(Name = "Runtime Dependencies")]
    public List<PackageDependency>? RuntimeDependencies => _data.RuntimeDependencies;

    [MarkoutSection(Name = "Files")]
    public List<string>? Files => _data.Files;

    private List<MarkoutField> GetCompactFields()
    {
        var fields = new List<MarkoutField>();

        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));

        if (!string.IsNullOrEmpty(Tfm))
            fields.Add(new("TFM", Tfm));
        if (_data.Published.HasValue)
            fields.Add(new("Updated", _data.Published.Value.ToString("yyyy-MM-dd")));
        if (_data.Deprecation != null)
            fields.Add(new("Deprecated", "Yes"));
        if (_data.Vulnerabilities is { Count: > 0 })
            fields.Add(MarkoutField.Create("Vulnerabilities", _data.Vulnerabilities.Count));

        return fields;
    }

    private List<MarkoutField> GetMetadataFields()
    {
        var fields = new List<MarkoutField>();

        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));
        if (!string.IsNullOrEmpty(Tfm))
            fields.Add(new("TFM", Tfm));
        if (_data.Published.HasValue)
            fields.Add(new("Updated", _data.Published.Value.ToString("yyyy-MM-dd")));

        if (_data.Deprecation?.Summary != null)
            fields.Add(new("Deprecated Note", _data.Deprecation.Summary));

        if (!string.IsNullOrWhiteSpace(_data.Authors))
            fields.Add(new("Authors", _data.Authors));
        if (_data.Owners is { Count: > 0 })
            fields.Add(new("Owners", string.Join(", ", _data.Owners)));
        if (!string.IsNullOrWhiteSpace(_data.License))
            fields.Add(new("License", _data.License));
        if (!string.IsNullOrWhiteSpace(_data.Repository))
            fields.Add(new("Repository", _data.Repository));

        if (_data.IsVerified == true)
            fields.Add(MarkoutField.Create("Verified", true));

        if (_data.ContentDirectories is { Count: > 0 })
            fields.Add(new("Content", string.Join(", ", _data.ContentDirectories)));
        if (TargetFrameworkCount > 0)
            fields.Add(MarkoutField.Create("Target Frameworks", TargetFrameworkCount));
        if (SupportedRidCount > 0)
            fields.Add(MarkoutField.Create("Runtime Identifiers", SupportedRidCount));
        if (_data.AssemblyCount > 1)
            fields.Add(MarkoutField.Create("Libraries", _data.AssemblyCount));
        if (_data.HasReadme)
            fields.Add(MarkoutField.Create("Readme", true));
        if (_data.Vulnerabilities is { Count: > 0 })
            fields.Add(MarkoutField.Create("Vulnerabilities", _data.Vulnerabilities.Count));

        if (_data.ToolCommands is { Count: > 0 })
            fields.Add(new("Tool Commands", string.Join(", ", _data.ToolCommands)));

        if (_data.IsFrameworkDependent)
            fields.Add(MarkoutField.Create("Framework Dependent", true));
        if (_data.IsRidSpecificPointerPackage)
            fields.Add(MarkoutField.Create("RID-Specific Pointer", true));
        if (!string.IsNullOrWhiteSpace(_data.RuntimeTargetRid))
            fields.Add(new("Runtime Target RID", _data.RuntimeTargetRid));

        return fields;
    }

}
