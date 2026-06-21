using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using Markout;
using NuGetFetch;

namespace DotnetInspector.Views;

[MarkoutSerializable(
    TitleProperty = nameof(PackageName),
    TitleContextProperty = nameof(TitleVersion),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public class InspectionResultView
{
    private readonly InspectionResult _data;
    private readonly bool _includeTitleVersion;

    public InspectionResultView(InspectionResult data, bool includeTitleVersion = true)
    {
        _data = data;
        _includeTitleVersion = includeTitleVersion;
    }

    [MarkoutPropertyName("Package")]
    public string PackageName => _data.PackageName;

    public string Version => _data.Version;

    public string? TitleVersion => _includeTitleVersion ? _data.Version : null;

    public string? Description => _data.Description;

    // ===== Field Collections for Serializer =====

    [MarkoutSection(Name = PackageSections.Summary, Headless = true)]
    public List<MarkoutField> Summary => GetCompactFields();

    [MarkoutIgnoreInTable]
    public List<DependencyGroup>? DependencyGroups => _data.DependencyGroups;

    [MarkoutSection(Name = PackageSections.Dependencies)]
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

    [MarkoutSection(Name = PackageSections.Files)]
    public List<PackageFileRow>? Files => _data.Files?
        .Select(f => new PackageFileRow(f.Path, f.Size, f.IsReadme, f.IsAgents))
        .ToList();

    [MarkoutSection(Name = PackageSections.LibraryFiles)]
    public List<LibraryFileRow>? LibraryFiles => _data.LibraryFiles?
        .Select(f => new LibraryFileRow(TfmResolver.ExtractTfmFromPath(f) ?? "", f))
        .OrderByDescending(f => TfmResolver.GetTfmPriority(f.Tfm))
        .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
        .ToList();

    [MarkoutSection(Name = PackageSections.Manifest)]
    public List<ManifestRow>? Manifest => !HasManifest ? null : GetManifestRows();

    [MarkoutIgnore]
    public bool HasManifest => !string.IsNullOrWhiteSpace(_data.PackageName)
        || !string.IsNullOrWhiteSpace(_data.Version)
        || !string.IsNullOrWhiteSpace(_data.ToolFormat)
        || _data.ToolCommands is { Count: > 0 }
        || _data.RuntimeIdentifierPackages is { Count: > 0 };

    [MarkoutSection(Name = PackageSections.PackageInfo)]
    public List<MarkoutField> Metadata => GetMetadataFields();

    [MarkoutSection(Name = PackageSections.RuntimeDependencies)]
    public List<PackageDependency>? RuntimeDependencies => _data.RuntimeDependencies;

    [MarkoutIgnore]
    public bool HasAuditSignals => _data.AuditSignals is { Count: > 0 };

    [MarkoutSection(Name = PackageSections.Signals, ShowWhenProperty = nameof(HasAuditSignals))]
    public List<AuditSignalRow>? SignalsSection =>
        _data.AuditSignals?.Select(s => new AuditSignalRow(s.Area, s.Signal, s.Value, s.Evidence)).ToList();

    [MarkoutSection(Name = PackageSections.Signature)]
    public SigningSection? SigningSectionData => _data.SignatureResult is { } sig
        ? new SigningSection
        {
            AuthorVerified = sig.AuthorVerified ? "Yes" : sig.IsUnsigned ? "No" : null,
            Publisher = !string.IsNullOrEmpty(sig.Publisher)
                ? $"{sig.Publisher}{(sig.AuthorVerified ? " (Verified)" : "")}"
                : null,
            Repository = sig.Repository,
            RepositoryVerified = sig.RepositoryVerified ? "Yes" : null,
            Signed = _data.Signed == true ? "Yes" : sig.IsUnsigned ? "No" : "Unknown",
            Status = sig.StatusMessage,
        }
        : null;

    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutPropertyName("Published")]
    public DateTimeOffset? Published => _data.Published;

    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutSkipNull]
    [MarkoutValueFormatter(typeof(CompactNumberFormatter))]
    [MarkoutPropertyName("Downloads")]
    public long? TotalDownloads => _data.TotalDownloads;

    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutSkipNull]
    [MarkoutValueFormatter(typeof(CompactNumberFormatter))]
    [MarkoutPropertyName("Version Downloads")]
    public long? VersionDownloads => _data.VersionDownloads;

    [MarkoutSection(Name = PackageSections.Statistics)]
    [MarkoutSkipNull]
    [MarkoutPropertyName("Version Count")]
    public int? VersionCount => _data.VersionCount;

    [MarkoutSection(Name = PackageSections.TargetFrameworks)]
    public List<TargetFrameworkRow>? TargetFrameworkRows => _data.TargetFrameworks?
        .OrderByDescending(TfmResolver.GetTfmPriority)
        .Select(tfm => new TargetFrameworkRow(tfm))
        .ToList();

    [MarkoutSection(Name = PackageSections.Vulnerabilities)]
    [MarkoutIgnoreInTable]
    public List<PackageVulnerability>? Vulnerabilities => _data.Vulnerabilities;

    public string? Authors => _data.Authors;
    public string? License => _data.License;
    public string? LicenseUrl => _data.LicenseUrl;
    public string? Repository => _data.Repository;
    public string? RepositoryType => _data.RepositoryType;
    public string? RepositoryCommit => _data.RepositoryCommit;

    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutPropertyName("Built")]
    public DateTimeOffset? BuiltDate => _data.BuiltDate;

    public bool? IsVerified => _data.IsVerified;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Owners")]
    public List<string>? Owners => _data.Owners;

    [MarkoutIgnoreInTable]
    public PackageDeprecation? Deprecation => _data.Deprecation;

    [MarkoutPropertyName("Vulnerabilities")]
    public string? VulnerabilitiesDisplay => Vulnerabilities is { Count: > 0 }
        ? $"{Vulnerabilities.Count} known ({string.Join(", ", Vulnerabilities.Select(v => v.Severity).Distinct())})"
        : null;

    [MarkoutSkipDefault]
    public bool HasReadme => _data.HasReadme;

    [MarkoutPropertyName("Readme")]
    public string? ReadmeFile => _data.HasReadme
        ? _data.ReadmeFile ?? "README.md"
        : _data.ReadmeFile;

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

    [MarkoutPropertyName("TFM Count")]
    public int TargetFrameworkCount => _data.TargetFrameworks?.Count ?? 0;

    [MarkoutPropertyName("Highest TFM")]
    public string? HighestTfm => _data.TargetFrameworks is { Count: > 0 }
        ? _data.TargetFrameworks.OrderByDescending(TfmResolver.GetTfmPriority).First()
        : null;

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

    [MarkoutPropertyName("RID Pointer Package")]
    [MarkoutSkipDefault]
    public bool IsRidSpecificPointerPackage => _data.IsRidSpecificPointerPackage;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Tool Commands")]
    public List<string>? ToolCommands => _data.ToolCommands;

    [MarkoutPropertyName("Runtime Target RID")]
    public string? RuntimeTargetRid => _data.RuntimeTargetRid;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Native Files")]
    public List<string>? NativeFiles => _data.NativeFiles;

    private List<MarkoutField> GetCompactFields()
    {
        List<MarkoutField> fields = [];

        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));

        if (!string.IsNullOrEmpty(HighestTfm))
            fields.Add(new("Highest TFM", HighestTfm));
        if (TargetFrameworkCount > 0)
            fields.Add(new("TFM Count", TargetFrameworkCount.ToString()));
        if (_data.BuiltDate.HasValue)
            fields.Add(new("Built", _data.BuiltDate.Value.ToString("yyyy-MM-dd")));
        else if (_data.Published.HasValue)
            fields.Add(new("Published", _data.Published.Value.ToString("yyyy-MM-dd")));
        if (!string.IsNullOrEmpty(_data.Source))
            fields.Add(new("Source", _data.Source));
        if (_data.Deprecation != null)
            fields.Add(new("Deprecated", "Yes"));
        if (_data.Vulnerabilities is { Count: > 0 })
            fields.Add(new("Vulnerabilities", _data.Vulnerabilities.Count.ToString()));

        return fields;
    }

    private List<MarkoutField> GetMetadataFields()
    {
        List<MarkoutField> fields = [];

        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));
        if (_data.PackageSize.HasValue)
            fields.Add(new("Size", new ByteSizeFormatter().Format(_data.PackageSize.Value)));
        if (!string.IsNullOrEmpty(HighestTfm))
            fields.Add(new("Highest TFM", HighestTfm));
        if (TargetFrameworkCount > 0)
            fields.Add(new("TFM Count", TargetFrameworkCount.ToString()));
        if (_data.BuiltDate.HasValue)
            fields.Add(new("Built", _data.BuiltDate.Value.ToString("yyyy-MM-dd")));
        if (_data.Published.HasValue)
            fields.Add(new("Published", _data.Published.Value.ToString("yyyy-MM-dd")));
        if (!string.IsNullOrEmpty(_data.Source))
            fields.Add(new("Source", _data.Source));

        if (_data.Deprecation?.Summary != null)
            fields.Add(new("Deprecated Note", _data.Deprecation.Summary));

        if (!string.IsNullOrWhiteSpace(_data.Authors))
            fields.Add(new("Authors", _data.Authors));
        if (_data.Owners is { Count: > 0 })
            fields.Add(new("Owners", string.Join(", ", _data.Owners)));
        if (!string.IsNullOrWhiteSpace(_data.License))
            fields.Add(new("License", _data.License));
        if (!string.IsNullOrWhiteSpace(_data.LicenseUrl))
            fields.Add(new("License URL", _data.LicenseUrl));
        if (!string.IsNullOrWhiteSpace(_data.Repository))
            fields.Add(new("Repository", _data.Repository));
        if (!string.IsNullOrWhiteSpace(_data.RepositoryType))
            fields.Add(new("Repository Type", _data.RepositoryType));
        if (!string.IsNullOrWhiteSpace(_data.RepositoryCommit))
            fields.Add(new("Repository Commit", _data.RepositoryCommit));

        if (_data.IsVerified == true)
            fields.Add(new("Verified", "Yes"));

        if (_data.Signed.HasValue)
            fields.Add(new("Signed", _data.Signed.Value ? "Yes" : "No"));

        if (_data.ContentDirectories is { Count: > 0 })
            fields.Add(new("Content", string.Join(", ", _data.ContentDirectories)));
        if (SupportedRidCount > 0)
            fields.Add(new("Runtime Identifiers", SupportedRidCount.ToString()));
        if (_data.AssemblyCount > 1)
            fields.Add(new("Libraries", _data.AssemblyCount.ToString()));
        if (_data.HasReadme)
            fields.Add(new("Readme", _data.ReadmeFile ?? "README.md"));
        if (_data.Vulnerabilities is { Count: > 0 })
            fields.Add(new("Vulnerabilities", _data.Vulnerabilities.Count.ToString()));

        if (_data.ToolCommands is { Count: > 0 })
            fields.Add(new("Tool Commands", string.Join(", ", _data.ToolCommands)));

        if (_data.IsFrameworkDependent)
            fields.Add(new("Framework Dependent", "Yes"));
        if (_data.IsRidSpecificPointerPackage)
            fields.Add(new("RID-Specific Pointer", "Yes"));
        if (!string.IsNullOrWhiteSpace(_data.RuntimeTargetRid))
            fields.Add(new("Runtime Target RID", _data.RuntimeTargetRid));

        return fields;
    }

    private List<ManifestRow> GetManifestRows()
    {
        List<ManifestRow> rows = [];

        if (!string.IsNullOrWhiteSpace(_data.ManifestVersion))
            rows.Add(new("Info", "Manifest Version", _data.ManifestVersion, "n/a"));
        if (!string.IsNullOrWhiteSpace(_data.PackageName))
            rows.Add(new("Info", "Package", _data.PackageName, "n/a"));
        if (!string.IsNullOrWhiteSpace(_data.Version))
            rows.Add(new("Info", "Version", _data.Version, "n/a"));
        rows.Add(new("Info", "Type", PackageType, "n/a"));
        if (_data.ToolCommands is { Count: > 0 })
            rows.Add(new("Info", "Commands", string.Join(", ", _data.ToolCommands), "n/a"));
        if (_data.RuntimeIdentifierPackages is { Count: > 0 })
        {
            rows.AddRange(_data.RuntimeIdentifierPackages.Select(r =>
                new ManifestRow("RID Package", r.RuntimeIdentifier, r.PackageId, r.AvailableDisplay)));
        }

        return rows;
    }

}

public class SigningSection
{
    [MarkoutPropertyName("Author Verified")]
    public string? AuthorVerified { get; init; }
    public string? Publisher { get; init; }
    public string? Repository { get; init; }
    [MarkoutPropertyName("Repository Verified")]
    public string? RepositoryVerified { get; init; }
    public string Signed { get; init; } = "Unknown";
    public string? Status { get; init; }
}

[MarkoutSerializable]
public record ManifestRow(
    string Kind,
    string Name,
    string Value,
    string? Available);

[MarkoutSerializable]
public record TargetFrameworkRow(
    [property: MarkoutPropertyName("TFM")] string Tfm);

[MarkoutSerializable]
public record LibraryFileRow(
    [property: MarkoutPropertyName("TFM")] string Tfm,
    [property: MarkoutPropertyName("File")] string File);

[MarkoutSerializable]
public record PackageFileRow(
    [property: MarkoutPropertyName("Path")] string Path,
    [property: MarkoutPropertyName("Size")] long Size,
    [property: MarkoutPropertyName("Readme")]
    [property: MarkoutBoolFormat("readme", "")] bool IsReadme,
    [property: MarkoutPropertyName("Agents")]
    [property: MarkoutBoolFormat("agents", "")] bool IsAgents);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(InspectionResultView))]
[MarkoutContext(typeof(LibraryInspectionView))]
[MarkoutContext(typeof(LibraryInspectionReport))]
[MarkoutContext(typeof(ReferenceRow))]
[MarkoutContext(typeof(ExtensionMethodRow))]
[MarkoutContext(typeof(ClassifiedMethodRow))]
[MarkoutContext(typeof(PInvokeMethodRow))]
[MarkoutContext(typeof(ResourceRow))]
[MarkoutContext(typeof(CustomAttributeRow))]
[MarkoutContext(typeof(TypeForwarderRow))]
[MarkoutContext(typeof(AuditSignalRow))]
[MarkoutContext(typeof(SwitchRow))]
[MarkoutContext(typeof(IntegrationRow))]
[MarkoutContext(typeof(IntegrationOpportunityRow))]
[MarkoutContext(typeof(IntegrationSignalRow))]
[MarkoutContext(typeof(IntegrationApiSignalRow))]
[MarkoutContext(typeof(DependencyGroup))]
[MarkoutContext(typeof(PackageDependency))]
[MarkoutContext(typeof(FlatDependency))]
[MarkoutContext(typeof(TargetFrameworkRow))]
[MarkoutContext(typeof(LibraryFileRow))]
[MarkoutContext(typeof(PackageFileRow))]
[MarkoutContext(typeof(ManifestRow))]
[MarkoutContext(typeof(RidPackageReferenceView))]
[MarkoutContext(typeof(EmptyDepsView))]
public partial class InspectionContext : MarkoutSerializerContext
{
}
