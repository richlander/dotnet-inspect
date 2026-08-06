using System.Text.Json.Serialization;
using InertText;

namespace DotnetInspector.Models;

/// <summary>
/// JSON projection over the same contained text used by the package view.
/// </summary>
internal sealed class PackageInspectionJson
{
    private readonly InspectionResult _data;
    private readonly PackageInspectionText _text;

    private PackageInspectionJson(InspectionResult data)
    {
        _data = data;
        _text = new PackageInspectionText(data);
    }

    public static PackageInspectionJson Create(InspectionResult data) => new(data);

    public string PackageName => _text.PackageName.ToString();
    public string? ManifestVersion => Render(_text.ManifestVersion);
    public string Version => _text.Version.ToString();
    public string? Source => Render(_text.Source);
    public string? Description => Render(_text.Description);
    public string? Authors => Render(_text.Authors);
    public string? License => Render(_text.License);
    public string? LicenseUrl => Render(_text.LicenseUrl);
    public string? Repository => Render(_text.Repository);
    public string? RepositoryType => Render(_text.RepositoryType);
    public string? RepositoryCommit => Render(_text.RepositoryCommit);
    public DateTimeOffset? Published => _data.Published;
    public DateTimeOffset? BuiltDate => _data.BuiltDate;
    public long? TotalDownloads => _data.TotalDownloads;
    public long? VersionDownloads => _data.VersionDownloads;
    public int? VersionCount => _data.VersionCount;
    public long? PackageSize => _data.PackageSize;
    public bool? IsVerified => _data.IsVerified;
    public List<string>? Owners => Render(_text.Owners);
    public PackageDeprecationJson? Deprecation => _text.Deprecation is { } value
        ? new(value)
        : null;
    public List<PackageVulnerabilityJson>? Vulnerabilities => _text.Vulnerabilities?
        .Select(value => new PackageVulnerabilityJson(value))
        .ToList();
    public bool HasReadme => _data.HasReadme;
    public bool HasAgentDocumentation => _data.HasAgentDocumentation;
    public bool IsToolPackage => _data.IsToolPackage;
    public List<string>? PackageTypes => Render(_text.PackageTypes);
    public List<string>? ContentDirectories => Render(_text.ContentDirectories);
    public List<string>? TargetFrameworks => Render(_text.TargetFrameworks);
    public List<string>? SupportedRids => Render(_text.SupportedRids);
    public int AssemblyCount => _data.AssemblyCount;
    public PackageBinarySignals? BinarySignals => _data.BinarySignals;
    public bool IsFrameworkDependent => _data.IsFrameworkDependent;
    public bool HasRidSpecificAssets => _data.HasRidSpecificAssets;
    public bool HasNativeDependencies => _data.HasNativeDependencies;
    public string? ToolFormat => Render(_text.ToolFormat);
    public bool IsRidSpecificPointerPackage => _data.IsRidSpecificPointerPackage;
    public List<string>? ToolCommands => Render(_text.ToolCommands);
    public List<RidPackageReferenceJson>? RuntimeIdentifierPackages =>
        _text.RuntimeIdentifierPackages?
            .Select(value => new RidPackageReferenceJson(value))
            .ToList();
    public string? RuntimeTargetRid => Render(_text.RuntimeTargetRid);
    public List<string>? NativeFiles => Render(_text.NativeFiles);
    public List<string>? LibraryFiles => Render(_text.LibraryFiles);
    public List<PackageDependencyGroupJson>? DependencyGroups => _text.DependencyGroups?
        .Select(value => new PackageDependencyGroupJson(value))
        .ToList();
    public List<PackageDependencyJson>? RuntimeDependencies => _text.RuntimeDependencies?
        .Select(value => new PackageDependencyJson(value))
        .ToList();
    public List<PackageFileJson>? Files => _text.Files?
        .Select(value => new PackageFileJson(value))
        .ToList();
    public List<PackageSourceFileJson>? SourceFiles => _text.SourceFiles?
        .Select(value => new PackageSourceFileJson(value))
        .ToList();
    public PackageSignatureJson? SignatureResult => _text.SignatureResult is { } value
        ? new(value)
        : null;
    public List<PackageAuditSignalJson>? AuditSignals => _text.AuditSignals?
        .Select(value => new PackageAuditSignalJson(value))
        .ToList();

    [JsonIgnore]
    public bool RequiredContainment => _text.RequiredContainment;

    private static string? Render(InertString? value) => value?.ToString();

    private static List<string>? Render(List<InertString>? values)
        => values?.Select(value => value.ToString()).ToList();
}

internal sealed class PackageDeprecationJson(PackageDeprecationText text)
{
    public List<string>? Reasons => text.Reasons?
        .Select(value => value.ToString())
        .ToList();
    public string? Message => text.Message?.ToString();
    public string? AlternatePackageId => text.AlternatePackageId?.ToString();
    public string Summary => text.Summary.ToString();
}

internal sealed class PackageVulnerabilityJson(PackageVulnerabilityText text)
{
    public string Severity => text.Severity.ToString();
    public string? CveId => text.CveId?.ToString();
    public string? Summary => text.Summary?.ToString();
    public string? AdvisoryUrl => text.AdvisoryUrl?.ToString();
    public string? GhsaId => text.GhsaId?.ToString();
}

internal sealed class RidPackageReferenceJson(RidPackageReferenceText text)
{
    public string RuntimeIdentifier => text.RuntimeIdentifier.ToString();
    public string PackageId => text.PackageId.ToString();
}

internal sealed class PackageDependencyGroupJson(PackageDependencyGroupText text)
{
    public string TargetFramework => text.TargetFramework.ToString();
    public List<PackageDependencyJson> Dependencies => text.Dependencies
        .Select(value => new PackageDependencyJson(value))
        .ToList();
}

internal sealed class PackageDependencyJson(PackageDependencyText text)
{
    public string Id => text.Id.ToString();
    public string Version => text.Version.ToString();
}

internal sealed class PackageFileJson(PackageFileText text)
{
    public string Path => text.Path.ToString();
    public long Size => text.Size;
}

internal sealed class PackageSourceFileJson(PackageSourceFileText text)
{
    public string Library => text.Library.ToString();
    public string Type => text.Type.ToString();
    public string? Url => text.Url?.ToString();
}

internal sealed class PackageSignatureJson(PackageSignatureText text)
{
    public string? Publisher => text.Publisher?.ToString();
    public bool AuthorVerified => text.AuthorVerified;
    public bool RepositoryVerified => text.RepositoryVerified;
    public string? Repository => text.Repository?.ToString();
    public string? StatusMessage => text.StatusMessage?.ToString();
    public bool IsUnsigned => text.IsUnsigned;
}

internal sealed class PackageAuditSignalJson(PackageAuditSignalText text)
{
    public string Area => text.Area.ToString();
    public string Signal => text.Signal.ToString();
    public string Value => text.Value.ToString();
    public string Evidence => text.Evidence.ToString();
}
