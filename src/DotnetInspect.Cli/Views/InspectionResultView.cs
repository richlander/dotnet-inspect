using System.Diagnostics.CodeAnalysis;
using InertText;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Ecosystems;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using DotnetInspect.Cli.Services;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(
    TitleProperty = nameof(PackageName),
    TitleContextProperty = nameof(TitleVersion),
    DescriptionProperty = nameof(Description),
    AutoFields = false)]
public class InspectionResultView
{
    private readonly InspectionResult _data;
    private PackageInspectionText? _text;
    private readonly bool _includeTitleVersion;
    private PackageInspectionText Text => _text ??= new PackageInspectionText(_data);

    private delegate string? PackageInfoValueResolver(InspectionResultView view);

    private readonly record struct PackageInfoFieldDefinition(
        string Name,
        PackageInfoValueResolver Resolve);

    private static readonly PackageInfoFieldDefinition[] PackageInfoFields =
    [
        new("Version", static view => view.Version),
        new("Type", static view => view.PackageType),
        new("Package Size (compressed)", static view =>
            view.PackageSizeBytes.HasValue
                ? new ByteSizeFormatter().Format(view.PackageSizeBytes.Value)
                : null),
        new("Selected TFM", static view =>
            view.PackageMeasurements?.SelectedTargetFramework?.ToString()),
        new("Selected-TFM Folders", static view =>
            view.PackageMeasurements?.SelectedTargetFrameworkFolders
                is { Count: > 0 } folders
                ? InertString.Join(
                    ", ",
                    TextPolicy.Field,
                    folders).ToString()
                : null),
        new("TFMs", static view =>
            view.PackageMeasurements?.AvailableTargetFrameworks
                is { } frameworks
                ? frameworks.Count == 0
                    ? "None"
                    : InertString.Join(
                        ", ",
                        TextPolicy.Field,
                        frameworks).ToString()
                : null),
        new("Selected-TFM Size", static view =>
            view.PackageMeasurements?.SelectedLibraryPayloadBytes
                is { } selectedBytes
                ? new ByteSizeFormatter().Format(selectedBytes)
                : null),
        new("Selected-TFM Library Count", static view =>
            view.PackageMeasurements?.SelectedLibraryCount
                is { } libraryCount
                ? libraryCount.ToString()
                : null),
        new("Selected-TFM Status", static view =>
            view.PackageMeasurementStatus),
        new("Ecosystem Dependencies", static view =>
            view.EcosystemDependenciesDisplay),
        new("Ecosystem Dependency Status", static view =>
            view.EcosystemDependencyStatus),
        new("Built", static view =>
            view._data.BuiltDate?.ToString("yyyy-MM-dd")),
        new("Published", static view =>
            view._data.Published?.ToString("yyyy-MM-dd")),
        new("Source", static view =>
            view.Text.Source?.ToString()),
        new("Deprecated Note", static view =>
            view.Text.Deprecation?.Summary.ToString()),
        new("Authors", static view =>
            view.Text.Authors is { } authors
                && !string.IsNullOrWhiteSpace(authors.ToString())
                    ? authors.ToString()
                    : null),
        new("Owners", static view =>
            view.Text.Owners is { Count: > 0 } owners
                ? InertString.Join(", ", TextPolicy.Field, owners).ToString()
                : null),
        new("License", static view =>
            view.Text.License is { } license
                && !string.IsNullOrWhiteSpace(license.ToString())
                    ? license.ToString()
                    : null),
        new("License URL", static view =>
            view.Text.LicenseUrl is { } licenseUrl
                && !string.IsNullOrWhiteSpace(licenseUrl.ToString())
                    ? licenseUrl.ToString()
                    : null),
        new("Repository", static view =>
            view.Text.Repository is { } repository
                && !string.IsNullOrWhiteSpace(repository.ToString())
                    ? repository.ToString()
                    : null),
        new("Repository Type", static view =>
            view.Text.RepositoryType is { } repositoryType
                && !string.IsNullOrWhiteSpace(repositoryType.ToString())
                    ? repositoryType.ToString()
                    : null),
        new("Repository Commit", static view =>
            view.Text.RepositoryCommit is { } repositoryCommit
                && !string.IsNullOrWhiteSpace(repositoryCommit.ToString())
                    ? repositoryCommit.ToString()
                    : null),
        new("Verified", static view =>
            view._data.IsVerified == true ? "Yes" : null),
        new("Signed", static view =>
            view._data.Signed.HasValue
                ? view._data.Signed.Value ? "Yes" : "No"
                : null),
        new("Content", static view =>
            view.Text.ContentDirectories is { Count: > 0 } directories
                ? InertString.Join(", ", TextPolicy.Field, directories).ToString()
                : null),
        new("Runtime Identifiers", static view =>
            view.SupportedRidCount > 0
                ? view.SupportedRidCount.ToString()
                : null),
        new("Libraries", static view =>
            view._data.AssemblyCount > 1
                ? view._data.AssemblyCount.ToString()
                : null),
        new("Readme", static view =>
            view._data.HasReadme ? view.ReadmeFile ?? "README.md" : null),
        new("Vulnerabilities", static view =>
            view._data.Vulnerabilities is { Count: > 0 } vulnerabilities
                ? vulnerabilities.Count.ToString()
                : null),
        new("Tool Commands", static view =>
            view.Text.ToolCommands is { Count: > 0 } commands
                ? InertString.Join(", ", TextPolicy.Field, commands).ToString()
                : null),
        new("Framework Dependent", static view =>
            view._data.IsFrameworkDependent ? "Yes" : null),
        new("RID-Specific Pointer", static view =>
            view._data.IsRidSpecificPointerPackage ? "Yes" : null),
        new("Runtime Target RID", static view =>
            view.Text.RuntimeTargetRid is { } runtimeTargetRid
                && !string.IsNullOrWhiteSpace(runtimeTargetRid.ToString())
                    ? runtimeTargetRid.ToString()
                    : null),
    ];

    internal static IReadOnlyList<string> PackageInfoFieldNames { get; } =
        PackageInfoFields
            .Select(static field => field.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal string? ResolvePackageInfoField(string name)
    {
        foreach (PackageInfoFieldDefinition definition in PackageInfoFields)
        {
            if (string.Equals(
                definition.Name,
                name,
                StringComparison.OrdinalIgnoreCase))
            {
                return definition.Resolve(this);
            }
        }

        return null;
    }

    public InspectionResultView(
        InspectionResult data,
        bool includeTitleVersion = true)
    {
        _data = data;
        _includeTitleVersion = includeTitleVersion;
    }

    /// <inheritdoc cref="PackageViewText"/>
    [MarkoutPropertyName("Package")]
    public string PackageName => Text.PackageName.ToString();

    /// <inheritdoc cref="PackageViewText"/>
    public string Version => Text.Version.ToString();

    /// <inheritdoc cref="PackageViewText"/>
    public string? TitleVersion => _includeTitleVersion ? Text.Version.ToString() : null;

    /// <inheritdoc cref="PackageViewText.QuoteProse"/>
    public string? Description => PackageViewText.QuoteProse(Text.Description);

    /// <summary>
    /// Whether any typed artifact text carried by this view required visual containment.
    /// </summary>
    /// <remarks>
    /// Aggregated before properties such as <see cref="Description"/> unwrap their
    /// <see cref="InertString"/> values for a structural serializer. The CLI owns what to do
    /// with the signal; the view only reports it.
    /// </remarks>
    [MarkoutIgnore]
    public bool RequiredContainment => Text.RequiredContainment;

    // ===== Field Collections for Serializer =====

    [MarkoutSection(Name = PackageSections.Summary, Headless = true)]
    public List<MarkoutField> Summary => GetCompactFields();

    [MarkoutIgnoreInTable]
    public List<PackageDependencyGroupRow>? DependencyGroups => Text.DependencyGroups?
        .Select(group => new PackageDependencyGroupRow(
            group.TargetFramework,
            group.Dependencies.Select(ToDependencyRow).ToList()))
        .ToList();

    [MarkoutSection(Name = PackageSections.Dependencies)]
    public List<FlatDependency>? FlatDependencies => Text.FlatDependencies?
        .Select(dependency => new FlatDependency(
            dependency.TargetFramework,
            dependency.Id,
            dependency.Version))
        .ToList();

    public static bool EcosystemDependencyCoverageIsComplete(
        List<PackageEcosystemDependencyRow>? rows) =>
        rows is null
        || rows.All(static row => row.Coverage is null);

    [MarkoutSection(Name = PackageSections.EcosystemDependencies)]
    [MarkoutIgnoreColumnWhen(
        nameof(EcosystemDependencyCoverageIsComplete),
        nameof(PackageEcosystemDependencyRow.Coverage))]
    public List<PackageEcosystemDependencyRow>? EcosystemDependencies =>
        RecognitionDocument is { } document
            ? (_data.EcosystemDependencyRows
                    ?? document.Classification.Recognized)
                .Select(entry =>
                    PackageEcosystemDependencyRow.Create(
                        entry,
                        document))
                .ToList()
            : null;

    [MarkoutSection(
        Name = PackageSections.DependencyHierarchy,
        EmptyText = "No dependency relationships.")]
    public Markout.Graph? DependencyHierarchy
    {
        get
        {
            DependsAssetProjection? projection =
                _data.DependencyHierarchyProjection;
            if (projection is null)
                return null;

            return DependencyHierarchyOutputAdapter.ToGraph(
                projection.Hierarchy,
                projection.HierarchyRows,
                markWindowedFragments: true);
        }
    }

    [MarkoutSection(Name = PackageSections.Manifest)]
    public List<ManifestRow>? Manifest => !HasManifest ? null : GetManifestRows();

    [MarkoutIgnore]
    public bool HasManifest => !string.IsNullOrWhiteSpace(_data.PackageName)
        || !string.IsNullOrWhiteSpace(_data.Version)
        || !string.IsNullOrWhiteSpace(_data.ToolFormat)
        || _data.ToolCommands is { Count: > 0 }
        || _data.RuntimeIdentifierPackages is { Count: > 0 };

    [MarkoutSection(Name = PackageSections.Files)]
    public List<PackageFileRow>? Files => Text.Files?
        .Select(ToFileRow)
        .ToList();

    [MarkoutSection(Name = PackageSections.PackageInfo, FieldOrder = MarkoutFieldOrder.Alphabetical)]
    public List<MarkoutField> Metadata => GetMetadataFields();

    // The manifest is listed as a path row rather than printed, so the section
    // stays a listing like its siblings; --print renders the document.
    [MarkoutSection(Name = PackageSections.FilesNuspec)]
    public List<PackageFileRow>? NuspecFiles => FamilyRows(PackageSections.FilesNuspec);

    // IsReadme is set by PackageFileLister.ListAll on exactly the file that
    // ResolvePackageReadme selected, so this goes through the same family predicate as
    // its siblings instead of re-deriving the readme from PackageReadmeFile.
    [MarkoutSection(Name = PackageSections.FilesReadme)]
    public List<PackageFileRow>? PackageReadme => FamilyRows(PackageSections.FilesReadme);

    [MarkoutSection(Name = PackageSections.FilesLicenses)]
    public List<PackageFileRow>? LicenseFiles => FamilyRows(PackageSections.FilesLicenses);

    [MarkoutSection(Name = PackageSections.FilesSkills)]
    public List<PackageFileRow>? SkillFiles => FamilyRows(PackageSections.FilesSkills);

    [MarkoutSection(Name = PackageSections.RuntimeDependencies)]
    public List<PackageDependencyRow>? RuntimeDependencies => Text.RuntimeDependencies?
        .Select(ToDependencyRow)
        .ToList();

    [MarkoutIgnore]
    public bool HasAuditSignals => _data.AuditSignals is { Count: > 0 };

    [MarkoutSection(Name = PackageSections.Signals, ShowWhenProperty = nameof(HasAuditSignals))]
    public List<PackageAuditSignalRow>? SignalsSection => Text.AuditSignals?
        .Select(signal => new PackageAuditSignalRow(
            signal.Area,
            signal.Signal,
            signal.Value,
            signal.Evidence))
        .ToList();

    [MarkoutIgnore]
    public bool HasArtifactTextConcerns => Text.ConcernCases.Count > 0;

    [MarkoutSection(
        Name = PackageSections.AuditArtifactText,
        ShowWhenProperty = nameof(HasArtifactTextConcerns))]
    public List<PackageTextConcernRow> ArtifactTextConcerns => Text.ConcernCases
        .Select(value => new PackageTextConcernRow(
            new InertString(TextPolicy.Field, value.Location),
            new InertString(TextPolicy.Field, TextConcernDisplay.Describe(value.Concerns))))
        .ToList();

    [MarkoutIgnore]
    public bool HasAuditFindings =>
        _data.PackageContentAudit?.Findings.Count > 0;

    [MarkoutSection(
        Name = PackageSections.AuditFindings,
        ShowWhenProperty = nameof(HasAuditFindings))]
    public List<PackageAuditFindingRow> AuditFindings =>
        _data.PackageContentAudit?.Findings
            .Select(value => new PackageAuditFindingRow(
                new InertString(TextPolicy.Field, value.Path),
                PackageContentFindingKindText(value),
                value.EncodedText.EnsurePermitted(TextPolicy.Field)))
            .ToList()
        ?? [];

    private static InertString PackageContentFindingKindText(
        PackageContentAuditFinding finding)
        => new(
            TextPolicy.Field,
            finding.Kind switch
            {
                PackageContentFindingKind.NonGraphicText =>
                    TextConcernDisplay.Describe(finding.Concerns),
                PackageContentFindingKind.NonGraphicSourceLinkText =>
                    $"SourceLink {TextConcernDisplay.Describe(finding.Concerns)}",
                PackageContentFindingKind.SourceLinkParentPathSegment =>
                    "SourceLink parent path segment",
                PackageContentFindingKind.InvalidSourceLinkMap =>
                    "invalid SourceLink map",
                PackageContentFindingKind.RejectedSourceLinkMapping =>
                    "rejected SourceLink mapping",
                PackageContentFindingKind.RestoreSourcesCleared =>
                    "restore sources cleared",
                PackageContentFindingKind.PackageSourceDeclared =>
                    "package source declared",
                PackageContentFindingKind.InvalidTextEncoding =>
                    "invalid text encoding",
                PackageContentFindingKind.InvalidNuGetConfiguration =>
                    "invalid NuGet configuration",
                PackageContentFindingKind.ScanLimit =>
                    "scan limit",
                PackageContentFindingKind.ReadFailure =>
                    "read failure",
                _ => "unknown",
            });

    [MarkoutIgnore]
    public bool HasIdentifierConfusion =>
        IdentifierConfusionAudit.InspectPackage(_data).Count > 0;

    [MarkoutSection(
        Name = PackageSections.AuditIdentifierConfusion,
        ShowWhenProperty = nameof(HasIdentifierConfusion))]
    public List<IdentifierConfusionRow> IdentifierConfusion =>
        IdentifierConfusionRows.Create(IdentifierConfusionAudit.InspectPackage(_data));

    [MarkoutSection(Name = PackageSections.Signature)]
    public SigningSection? SigningSectionData => Text.SignatureResult is { } signature
        ? new SigningSection(
            signature.AuthorVerified
                ? "Yes"
                : signature.RepositoryVerified || signature.IsUnsigned ? "No" : null,
            signature.Publisher is { IsEmpty: false } publisher
                ? InertString.Format(
                    TextPolicy.Field,
                    $"{publisher}{(signature.AuthorVerified ? " (Verified)" : "")}")
                : null,
            signature.Repository,
            signature.RepositoryVerified ? "Yes" : null,
            _data.Signed == true ? "Yes" : signature.IsUnsigned ? "No" : "Unknown",
            signature.StatusMessage)
        : null;

    [MarkoutSection(Name = PackageSections.SourceLinkAvailability)]
    public PackageSourceAvailabilitySection? SourceAvailability =>
        Text.SourceAvailability is { } availability
            ? new PackageSourceAvailabilitySection(
                new InertString(
                    TextPolicy.Field,
                    $"{availability.AuditedLibraries}/{availability.TotalLibraries} audited"),
                availability.EmbeddedSourceFiles,
                availability.FailedLibraries,
                availability.MissingFiles?.Count ?? 0,
                new InertString(
                    TextPolicy.Field,
                    $"{availability.AccessibleSourceFiles}/{availability.TotalSourceFiles} available"),
                new InertString(TextPolicy.Field, AvailabilityStatus(availability)),
                availability.UnavailableLibraries)
            : null;

    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutSection(Name = PackageSections.SourceLinkFiles, EmptyText = "No SourceLink source files found for this package.")]
    public List<PackageSourceFileRow>? SourceFiles => Text.SourceFiles?
        .Select(row => new PackageSourceFileRow(row.Library, row.Type, row.Url))
        .ToList();

    [MarkoutSection(Name = PackageSections.SourceLinkIntegrity)]
    public PackageSourceIntegritySection? SourceIntegrity =>
        Text.SourceIntegrity is { } integrity
            ? new PackageSourceIntegritySection(
                new InertString(
                    TextPolicy.Field,
                    $"{integrity.CheckedLibraries}/{integrity.TotalLibraries} checked"),
                integrity.LineEndingNormalized > 0
                    ? new InertString(
                        TextPolicy.Field,
                        $"{integrity.LineEndingNormalized} normalized")
                    : null,
                integrity.FailedLibraries,
                integrity.Mismatched,
                integrity.MismatchedFiles,
                new InertString(TextPolicy.Field, IntegrityStatus(integrity)),
                integrity.UnavailableLibraries,
                integrity.Unverifiable,
                integrity.Verified)
            : null;

    [MarkoutSection(Name = PackageSections.SourceLinkMissingFiles)]
    public List<PackageSourceLinkFileRow>? MissingSourceFiles =>
        Text.SourceAvailability is { } availability
            ? MissingSourceRows(availability)
            : null;

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
    public List<TargetFrameworkRow>? TargetFrameworkRows => Text.OrderedTargetFrameworks is { } tfms
        ? tfms
            .Select(tfm => new TargetFrameworkRow(tfm))
            .ToList()
        : null;

    [MarkoutSection(Name = PackageSections.Vulnerabilities)]
    [MarkoutIgnoreInTable]
    public List<PackageVulnerabilityRow>? Vulnerabilities => Text.Vulnerabilities?
        .Select(value => new PackageVulnerabilityRow(
            value.Severity,
            value.CveId,
            value.Summary,
            value.AdvisoryUrl,
            value.GhsaId))
        .ToList();

    /// <inheritdoc cref="PackageViewText"/>
    public string? Authors => PackageViewText.Render(Text.Authors);
    /// <inheritdoc cref="PackageViewText"/>
    public string? License => PackageViewText.Render(Text.License);
    /// <inheritdoc cref="PackageViewText"/>
    public string? LicenseUrl => PackageViewText.Render(Text.LicenseUrl);
    /// <inheritdoc cref="PackageViewText"/>
    public string? Repository => PackageViewText.Render(Text.Repository);
    /// <inheritdoc cref="PackageViewText"/>
    public string? RepositoryType => PackageViewText.Render(Text.RepositoryType);
    /// <inheritdoc cref="PackageViewText"/>
    public string? RepositoryCommit => PackageViewText.Render(Text.RepositoryCommit);

    [MarkoutFormat("yyyy-MM-dd")]
    [MarkoutPropertyName("Built")]
    public DateTimeOffset? BuiltDate => _data.BuiltDate;

    public bool? IsVerified => _data.IsVerified;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Owners")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? Owners => PackageViewText.Render(Text.Owners);

    [MarkoutIgnoreInTable]
    public PackageDeprecationRow? Deprecation => Text.Deprecation is { } deprecation
        ? new PackageDeprecationRow(
            PackageViewText.Render(deprecation.Reasons),
            deprecation.Message,
            deprecation.AlternatePackageId,
            deprecation.Summary)
        : null;

    [MarkoutPropertyName("Vulnerabilities")]
    public string? VulnerabilitiesDisplay
    {
        get
        {
            if (Text.Vulnerabilities is not { Count: > 0 } vulnerabilities)
                return null;

            InertString severities = InertString.Join(
                ", ",
                TextPolicy.Field,
                vulnerabilities.Select(value => value.Severity).Distinct());
            return InertString.Format(
                TextPolicy.Field,
                $"{vulnerabilities.Count} known ({severities})").ToString();
        }
    }

    [MarkoutSkipDefault]
    public bool HasReadme => _data.HasReadme;

    [MarkoutPropertyName("Readme")]
    public string? ReadmeFile => _data.HasReadme
        ? PackageViewText.Render(Text.PackageReadmeFile ?? Text.ReadmeFile)
            ?? "README.md"
        : PackageViewText.Render(Text.ReadmeFile);

    [MarkoutSkipDefault]
    public bool IsToolPackage => _data.IsToolPackage;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Package Types")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? PackageTypes => PackageViewText.Render(Text.PackageTypes);

    [MarkoutPropertyName("Package Type")]
    public string PackageType => _data.ToolFormat?.Contains("Version=\"2\"") == true
        ? "Tool v2"
        : _data.IsToolPackage ? "Tool" : "Library";

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Content")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? ContentDirectories => PackageViewText.Render(Text.ContentDirectories);

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Target Frameworks")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? TargetFrameworks => PackageViewText.Render(Text.TargetFrameworks);

    [MarkoutPropertyName("TFM Count")]
    public int TargetFrameworkCount => _data.TargetFrameworks?.Count ?? 0;

    [MarkoutPropertyName("Highest TFM")]
    /// <inheritdoc cref="PackageViewText"/>
    public string? HighestTfm => PackageViewText.Render(Text.HighestTfm);

    [MarkoutIgnore]
    private PackageInfoMeasurements? PackageMeasurements =>
        _data.PackageInfoMeasurementInspection?.Content;

    [MarkoutIgnore]
    private long? PackageSizeBytes =>
        PackageMeasurements?.CompressedPackageBytes
        ?? _data.PackageSize;

    [MarkoutIgnore]
    private string? PackageMeasurementStatus =>
        PackageMeasurements is { HasSelectedSlice: false } measurements
            ? measurements.Detail?.ToString()
                ?? measurements.Status.ToString()
            : null;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Supported RIDs")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? SupportedRids => PackageViewText.Render(Text.SupportedRids);

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
    /// <inheritdoc cref="PackageViewText"/>
    public string? ToolFormat => PackageViewText.Render(Text.ToolFormat);

    [MarkoutPropertyName("RID Pointer Package")]
    [MarkoutSkipDefault]
    public bool IsRidSpecificPointerPackage => _data.IsRidSpecificPointerPackage;

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Tool Commands")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? ToolCommands => PackageViewText.Render(Text.ToolCommands);

    [MarkoutPropertyName("Runtime Target RID")]
    /// <inheritdoc cref="PackageViewText"/>
    public string? RuntimeTargetRid => PackageViewText.Render(Text.RuntimeTargetRid);

    [MarkoutJoin(", ")]
    [MarkoutPropertyName("Native Files")]
    /// <inheritdoc cref="PackageViewText"/>
    public List<string>? NativeFiles => PackageViewText.Render(Text.NativeFiles);

    private static PackageFileRow ToFileRow(PackageFileText file)
        => new(file.Path, file.Size);

    private List<PackageFileRow>? FamilyRows(string section)
        => PackageFileFamily.PredicateFor(section) is { } predicate
            ? Text.SelectPackageFiles(predicate)?
                .Select(ToFileRow)
                .ToList()
            : null;

    private static PackageDependencyRow ToDependencyRow(PackageDependencyText dependency)
        => new(dependency.Id, dependency.Version);

    private static List<PackageSourceLinkFileRow> MissingSourceRows(
        PackageSourceAvailabilityText availability)
    {
        List<PackageSourceLinkFileRow> rows = [];
        if (availability.MissingFiles is { } missing)
        {
            rows.AddRange(missing.Select(static file =>
                new PackageSourceLinkFileRow(
                    file.Library,
                    file.Path,
                    new InertString(TextPolicy.Field, "Missing"))));
        }
        if (availability.UnavailableLibraries is { } unavailable)
        {
            rows.AddRange(unavailable.Select(static issue =>
                new PackageSourceLinkFileRow(
                    issue.Library,
                    new InertString(TextPolicy.Field, ""),
                    InertString.Format(TextPolicy.Field, $"Unavailable: {issue.Reason}"))));
        }
        if (availability.FailedLibraries is { } failed)
        {
            rows.AddRange(failed.Select(static issue =>
                new PackageSourceLinkFileRow(
                    issue.Library,
                    new InertString(TextPolicy.Field, ""),
                    InertString.Format(TextPolicy.Field, $"Failed: {issue.Reason}"))));
        }

        return rows
            .OrderBy(static row => row.LibraryText.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.FileText.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string AvailabilityStatus(PackageSourceAvailabilityText availability)
    {
        if (availability.TotalLibraries == 0)
            return "Unavailable";
        if (availability.TotalSourceFiles == 0)
            return "Partial";
        if (availability.AuditedLibraries == 0
            && availability.FailedLibraries is { Count: > 0 })
        {
            return "Failed";
        }

        return availability.AuditedLibraries < availability.TotalLibraries
               || availability.MissingFiles is { Count: > 0 }
            ? "Partial"
            : "Complete";
    }

    private static string IntegrityStatus(PackageSourceIntegrityText integrity)
    {
        if (integrity.TotalLibraries == 0)
            return "Unavailable";
        if (integrity.CheckedLibraries == 0
            && integrity.FailedLibraries is { Count: > 0 })
        {
            return "Failed";
        }
        if (integrity.Mismatched > 0)
            return "Mismatch";
        return integrity.CheckedLibraries < integrity.TotalLibraries
               || integrity.Unverifiable > 0
            ? "Partial"
            : "Verified";
    }

    private List<MarkoutField> GetCompactFields()
    {
        List<MarkoutField> fields = [];

        fields.Add(new("Version", Version));
        fields.Add(new("Type", PackageType));

        if (PackageMeasurements?.SelectedTargetFramework
                is { } selectedTfm
            && selectedTfm.ToString() is { Length: > 0 } selectedTfmText)
        {
            fields.Add(new("Selected TFM", selectedTfmText));
        }
        if (PackageMeasurements?.AvailableTargetFrameworks
            is { } targetFrameworks)
        {
            fields.Add(new(
                "TFMs",
                targetFrameworks.Count == 0
                    ? "None"
                    : InertString.Join(
                        ", ",
                        TextPolicy.Field,
                        targetFrameworks).ToString()));
        }
        if (_data.BuiltDate.HasValue)
            fields.Add(new("Built", _data.BuiltDate.Value.ToString("yyyy-MM-dd")));
        else if (_data.Published.HasValue)
            fields.Add(new("Published", _data.Published.Value.ToString("yyyy-MM-dd")));
        if (Text.Source is { } source)
            fields.Add(new("Source", source.ToString()));
        if (_data.Deprecation != null)
            fields.Add(new("Deprecated", "Yes"));
        if (_data.Vulnerabilities is { Count: > 0 })
            fields.Add(new("Vulnerabilities", _data.Vulnerabilities.Count.ToString()));

        return fields;
    }

    private List<MarkoutField> GetMetadataFields()
    {
        List<MarkoutField> fields = [];
        foreach (PackageInfoFieldDefinition definition in PackageInfoFields)
        {
            if (definition.Resolve(this) is { } value)
                fields.Add(new(definition.Name, value));
        }
        return fields;
    }

    private EcosystemDependencyRecognitionDocument? RecognitionDocument =>
        _data.EcosystemDependencyRecognitionInspection?.Content switch
        {
            EcosystemDependencyRecognitionOutcome.Complete complete =>
                complete.Document,
            EcosystemDependencyRecognitionOutcome.Incomplete incomplete =>
                incomplete.Document,
            _ => null,
        };

    private string? EcosystemDependenciesDisplay =>
        _data.EcosystemDependencyRecognitionInspection?.Content
            is EcosystemDependencyRecognitionOutcome.Complete complete
        && !complete.Document.Classification.RecognizedEcosystems.IsEmpty
            ? string.Join(
                ", ",
                complete.Document.Classification.RecognizedEcosystems
                    .Select(static ecosystem => ecosystem.Title))
            : null;

    private string? EcosystemDependencyStatus =>
        _data.EcosystemDependencyRecognitionInspection?.Content switch
        {
            EcosystemDependencyRecognitionOutcome.Incomplete incomplete =>
                $"Incomplete ({incomplete.Document.InputIssues.Length} "
                + (incomplete.Document.InputIssues.Length == 1
                    ? "issue"
                    : "issues")
                + ")",
            EcosystemDependencyRecognitionOutcome.Unavailable unavailable =>
                $"Unavailable ({unavailable.InputIssues.Length} "
                + (unavailable.InputIssues.Length == 1
                    ? "issue"
                    : "issues")
                + ")",
            _ => null,
        };

    private List<ManifestRow> GetManifestRows()
    {
        List<ManifestRow> rows = [];

        InertString info = new(TextPolicy.Field, "Info");
        InertString notApplicable = new(TextPolicy.Field, "n/a");
        if (Text.ManifestVersion is { } manifestVersion
            && !string.IsNullOrWhiteSpace(manifestVersion.ToString()))
            rows.Add(new(info, new(TextPolicy.Field, "Manifest Version"), manifestVersion, notApplicable));
        if (!string.IsNullOrWhiteSpace(Text.PackageName.ToString()))
            rows.Add(new(info, new(TextPolicy.Field, "Package"), Text.PackageName, notApplicable));
        if (!string.IsNullOrWhiteSpace(Text.Version.ToString()))
            rows.Add(new(info, new(TextPolicy.Field, "Version"), Text.Version, notApplicable));
        rows.Add(new(
            info,
            new(TextPolicy.Field, "Type"),
            new InertString(TextPolicy.Field, PackageType),
            notApplicable));
        if (Text.ToolCommands is { Count: > 0 } commands)
            rows.Add(new(
                info,
                new(TextPolicy.Field, "Commands"),
                InertString.Join(", ", TextPolicy.Field, commands),
                notApplicable));
        if (Text.RuntimeIdentifierPackages is { Count: > 0 } runtimePackages)
        {
            rows.AddRange(runtimePackages.Select(reference =>
                new ManifestRow(
                    new(TextPolicy.Field, "RID Package"),
                    reference.RuntimeIdentifier,
                    reference.PackageId,
                    new InertString(TextPolicy.Field, reference.AvailableDisplay))));
        }

        return rows;
    }

}

public class SigningSection
{
    private InertString? _publisher;
    private InertString? _repository;
    private InertString? _status;

    internal static readonly string[] FieldNames =
    [
        "Author Verified",
        "Publisher",
        "Repository",
        "Repository Verified",
        "Signed",
        "Status",
    ];

    public SigningSection()
    {
    }

    public SigningSection(
        string? authorVerified,
        InertString? publisher,
        InertString? repository,
        string? repositoryVerified,
        string signed,
        InertString? status)
    {
        AuthorVerified = authorVerified;
        _publisher = publisher;
        _repository = repository;
        RepositoryVerified = repositoryVerified;
        Signed = signed;
        _status = status;
    }

    [MarkoutPropertyName("Author Verified")]
    public string? AuthorVerified { get; init; }
    public string? Publisher
    {
        get => PackageViewText.Render(_publisher);
        init => _publisher = value is null
            ? null
            : new InertString(TextPolicy.Field, value);
    }
    public string? Repository
    {
        get => PackageViewText.Render(_repository);
        init => _repository = value is null
            ? null
            : new InertString(TextPolicy.Field, value);
    }
    [MarkoutPropertyName("Repository Verified")]
    public string? RepositoryVerified { get; init; }
    public string Signed { get; init; } = "Unknown";
    public string? Status
    {
        get => PackageViewText.Render(_status);
        init => _status = value is null
            ? null
            : new InertString(TextPolicy.Field, value);
    }

    internal IEnumerable<MarkoutField> ToMarkoutFields()
    {
        if (AuthorVerified is not null)
            yield return new("Author Verified", AuthorVerified);
        if (Publisher is not null)
            yield return new("Publisher", Publisher);
        if (Repository is not null)
            yield return new("Repository", Repository);
        if (RepositoryVerified is not null)
            yield return new("Repository Verified", RepositoryVerified);
        yield return new("Signed", Signed);
        if (Status is not null)
            yield return new("Status", Status);
    }
}

/// <inheritdoc cref="PackageViewText"/>
[MarkoutSerializable]
public record ManifestRow(
    [property: MarkoutIgnore] InertString KindText,
    [property: MarkoutIgnore] InertString NameText,
    [property: MarkoutIgnore] InertString ValueText,
    [property: MarkoutIgnore] InertString? AvailableText)
{
    public string Kind => KindText.ToString();
    public string Name => NameText.ToString();
    public string Value => ValueText.ToString();
    public string? Available => PackageViewText.Render(AvailableText);
}

/// <inheritdoc cref="PackageViewText"/>
[MarkoutSerializable]
public record TargetFrameworkRow([property: MarkoutIgnore] InertString TfmText)
{
    [MarkoutPropertyName("TFM")]
    public string Tfm => TfmText.ToString();
}

/// <summary>
/// Containment for text rendered by the package views.
/// </summary>
/// <remarks>
/// A .nupkg is untrusted input: its ZIP entry names and nuspec text are chosen
/// by whoever built it. Text carrying a line terminator, ANSI escape, or bidi
/// override breaks out of its Markdown table cell and injects text that reads
/// as genuine tool output (issue #3319). These rows are presentation-only --
/// path filtering runs against <c>PackageFile</c>, the model, not against these
/// rows -- so containment here cannot affect matching.
/// </remarks>
internal static class PackageViewText
{
    [return: NotNullIfNotNull(nameof(value))]
    public static string? Render(InertString? value)
        => value?.ToString();

    [return: NotNullIfNotNull(nameof(values))]
    public static List<string>? Render(List<InertString>? values)
        => values?.Select(value => value.ToString()).ToList();

    public static string? RenderIssues(List<PackageSourceLinkIssueText>? issues)
        => issues is { Count: > 0 }
            ? string.Join(
                ", ",
                issues.Select(
                    static issue =>
                        $"{MarkoutInline.Code(issue.Library)} ({issue.Reason})"))
            : null;

    public static string? RenderSourceFiles(List<PackageSourceLinkFileText>? files)
        => files is { Count: > 0 }
            ? string.Join(
                ", ",
                files.Select(
                    static file =>
                        MarkoutInline.Code(
                            InertString.Format(
                                TextPolicy.Field,
                                $"{file.Library}: {file.Path}"))))
            : null;

    /// <summary>
    /// Renders contained prose as a Markdown quotation.
    /// </summary>
    /// <remarks>
    /// The value reaches this sink as an <see cref="InertString"/>, so visual containment is
    /// already a property of the object model. The quotation is the separate structural
    /// containment Markdown requires: package-authored headings and tables remain inside a
    /// visibly quoted block instead of becoming peer structures in tool output.
    /// </remarks>
    [return: NotNullIfNotNull(nameof(value))]
    public static string? QuoteProse(InertString? value)
    {
        if (value is not { } prose || prose.IsEmpty)
            return value?.ToString();

        // Markout's DescriptionProperty writes a paragraph verbatim. Prefixing every line
        // makes the package's prose a quotation, so headings, tables, and other block syntax
        // remain visibly package-authored instead of becoming peer sections in tool output.
        string text = prose.ToString().ReplaceLineEndings("\n");
        return string.Join(
            "\n",
            text.Split('\n').Select(static line => line.Length == 0 ? ">" : $"> {line}"));
    }
}

[MarkoutSerializable]
public record PackageFileRow(
    [property: MarkoutIgnore] InertString PathText,
    long Size)
{
    /// <inheritdoc cref="PackageViewText"/>
    [MarkoutPropertyName("Path")]
    public string Path => PathText.ToString();

    [MarkoutPropertyName("Size")]
    public long Size { get; } = Size;
}

[MarkoutSerializable]
public record PackageSourceFileRow(
    [property: MarkoutIgnore] InertString LibraryText,
    [property: MarkoutIgnore] InertString TypeText,
    [property: MarkoutIgnore] InertString? UrlText)
{
    /// <inheritdoc cref="PackageViewText"/>
    public string Library => LibraryText.ToString();

    /// <inheritdoc cref="PackageViewText"/>
    public string Type => TypeText.ToString();

    /// <inheritdoc cref="PackageViewText"/>
    [MarkoutSkipNull]
    public string? Url => PackageViewText.Render(UrlText);
}

[MarkoutSerializable]
public sealed record PackageDependencyGroupRow(
    [property: MarkoutIgnore] InertString TargetFrameworkText,
    List<PackageDependencyRow> Dependencies)
{
    public string TargetFramework => TargetFrameworkText.ToString();
}

[MarkoutSerializable]
public sealed class PackageEcosystemDependencyRow
{
    public PackageEcosystemDependencyRow(
        string ecosystem,
        string kind,
        string dependency,
        string declaredBy,
        string? coverage,
        string matchingBases,
        string? versionOrRange,
        int occurrence,
        string? requestedTargetFramework,
        string? selectedTargetFramework,
        int? selectedGroup)
    {
        EcosystemText = Field(ecosystem);
        KindText = Field(kind);
        DependencyText = Field(dependency);
        DeclaredByText = Field(declaredBy);
        CoverageText = OptionalField(coverage);
        MatchingBasesText = Field(matchingBases);
        VersionOrRangeText = OptionalField(versionOrRange);
        Occurrence = occurrence;
        RequestedTargetFrameworkText =
            OptionalField(requestedTargetFramework);
        SelectedTargetFrameworkText =
            OptionalField(selectedTargetFramework);
        SelectedGroup = selectedGroup;
    }

    [MarkoutIgnore]
    public InertString EcosystemText { get; }

    public string Ecosystem => EcosystemText.ToString();

    [MarkoutIgnore]
    public InertString KindText { get; }

    public string Kind => KindText.ToString();

    [MarkoutIgnore]
    public InertString DependencyText { get; }

    public string Dependency => DependencyText.ToString();

    [MarkoutIgnore]
    public InertString DeclaredByText { get; }

    [MarkoutPropertyName("Declared By")]
    public string DeclaredBy => DeclaredByText.ToString();

    [MarkoutIgnore]
    public InertString? CoverageText { get; }

    public string? Coverage => CoverageText?.ToString();

    [MarkoutIgnore]
    public InertString MatchingBasesText { get; }

    [MarkoutPropertyName("Matching Bases")]
    public string MatchingBases => MatchingBasesText.ToString();

    [MarkoutIgnore]
    public InertString? VersionOrRangeText { get; }

    [MarkoutPropertyName("Version/Range")]
    public string? VersionOrRange => VersionOrRangeText?.ToString();

    public int Occurrence { get; }

    [MarkoutIgnore]
    public InertString? RequestedTargetFrameworkText { get; }

    [MarkoutPropertyName("Requested TFM")]
    public string? RequestedTargetFramework =>
        RequestedTargetFrameworkText?.ToString();

    [MarkoutIgnore]
    public InertString? SelectedTargetFrameworkText { get; }

    [MarkoutPropertyName("Selected TFM")]
    public string? SelectedTargetFramework =>
        SelectedTargetFrameworkText?.ToString();

    [MarkoutPropertyName("Selected Group")]
    public int? SelectedGroup { get; }

    internal static PackageEcosystemDependencyRow Create(
        EcosystemDependencyRecognitionEntry entry,
        EcosystemDependencyRecognitionDocument document)
    {
        PackageDependencyGroups? groups =
            document.InputContext
                is EcosystemDependencyInputContext.Package packageContext
            && packageContext.DependencyGroup
                is EcosystemDependencyInputComponent<
                    PackageDependencyGroups>.Available available
                ? available.Value
                : null;
        string matchingBases = string.Join(
            ", ",
            entry.MatchingAssociations.Select(DescribeAssociation));
        return entry.Observation switch
        {
            EcosystemDependencyObservation.PackageDeclaration package =>
                new(
                    entry.Ecosystem.Title,
                    "Package declaration",
                    $"{package.Dependency.Id} "
                        + package.Dependency.VersionRange,
                    $"{package.DeclaringPackage.PackageId}@"
                        + package.DeclaringPackage.Version,
                    CoverageValue(document),
                    matchingBases,
                    package.Dependency.VersionRange,
                    package.Identity.Value,
                    groups?.RequestedTargetFramework,
                    groups?.SelectedTargetFramework,
                    groups?.SelectedGroupIndex is int selectedGroup
                        ? selectedGroup + 1
                        : null),
            EcosystemDependencyObservation.AssemblyReference assembly =>
                new(
                    entry.Ecosystem.Title,
                    "Assembly reference",
                    DescribeAssembly(assembly.Reference),
                    DescribeLibrary(assembly.DeclaringLibrary),
                    CoverageValue(document),
                    matchingBases,
                    assembly.Reference.Version?.ToString(),
                    assembly.Identity.Value,
                    groups?.RequestedTargetFramework,
                    groups?.SelectedTargetFramework,
                    groups?.SelectedGroupIndex is int selectedGroup
                        ? selectedGroup + 1
                        : null),
            _ => throw new InvalidOperationException(
                "Unknown ecosystem dependency observation."),
        };
    }

    private static string? CoverageValue(
        EcosystemDependencyRecognitionDocument document) =>
        document.Coverage
            == EcosystemDependencyRecognitionCoverage.Complete
                ? null
                : "Incomplete";

    private static string DescribeAssociation(
        EcosystemDependencyAssociation association) =>
        $"{association.Domain switch
        {
            EcosystemDependencyIdentityDomain.PackageId =>
                "Package ID",
            EcosystemDependencyIdentityDomain.AssemblyName =>
                "Assembly name",
            _ => throw new InvalidOperationException(
                "Unknown ecosystem dependency identity domain."),
        }} {association.Kind.ToString().ToLowerInvariant()}: "
        + association.Value;

    private static string DescribeAssembly(
        AssemblyReferenceIdentity identity) =>
        $"{identity.Name}, Version={identity.Version}, "
        + $"Culture={identity.Culture ?? "neutral"}, "
        + $"PublicKeyToken={identity.PublicKeyToken ?? "null"}";

    private static string DescribeLibrary(
        PortableLibraryIdentity identity) =>
        $"{identity.Name}, Version={identity.Version}, "
        + $"Culture={identity.Culture ?? "neutral"}, "
        + $"PublicKeyToken={identity.PublicKeyToken ?? "null"}";

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);

    private static InertString? OptionalField(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Field(value);
}

[MarkoutSerializable]
public sealed record PackageDependencyRow(
    [property: MarkoutIgnore] InertString IdText,
    [property: MarkoutIgnore] InertString VersionText)
{
    public string Id => IdText.ToString();
    public string Version => VersionText.ToString();
}

[MarkoutSerializable]
public sealed record PackageDeprecationRow(
    List<string>? Reasons,
    [property: MarkoutIgnore] InertString? MessageText,
    [property: MarkoutIgnore] InertString? AlternatePackageIdText,
    [property: MarkoutIgnore] InertString SummaryText)
{
    public string? Message => PackageViewText.Render(MessageText);
    public string? AlternatePackageId => PackageViewText.Render(AlternatePackageIdText);
    public string Summary => SummaryText.ToString();
}

[MarkoutSerializable]
public sealed record PackageVulnerabilityRow(
    [property: MarkoutIgnore] InertString SeverityText,
    [property: MarkoutIgnore] InertString? CveIdText,
    [property: MarkoutIgnore] InertString? SummaryText,
    [property: MarkoutIgnore] InertString? AdvisoryUrlText,
    [property: MarkoutIgnore] InertString? GhsaIdText)
{
    public string Severity => SeverityText.ToString();
    public string? CveId => PackageViewText.Render(CveIdText);
    public string? Summary => PackageViewText.Render(SummaryText);
    public string? AdvisoryUrl => PackageViewText.Render(AdvisoryUrlText);
    public string? GhsaId => PackageViewText.Render(GhsaIdText);
}

[MarkoutSerializable]
public sealed record PackageAuditSignalRow(
    [property: MarkoutIgnore] InertString AreaText,
    [property: MarkoutIgnore] InertString SignalText,
    [property: MarkoutIgnore] InertString ValueText,
    [property: MarkoutIgnore] InertString EvidenceText)
{
    public string Area => AreaText.ToString();
    public string Signal => SignalText.ToString();
    public string Value => ValueText.ToString();
    public string Evidence => EvidenceText.ToString();
}

[MarkoutSerializable]
public sealed record PackageTextConcernRow(
    [property: MarkoutIgnore] InertString LocationText,
    [property: MarkoutIgnore] InertString ConcernsText)
{
    public string Location => LocationText.ToString();
    public string Concerns => ConcernsText.ToString();
}

[MarkoutSerializable]
public sealed record PackageAuditFindingRow(
    [property: MarkoutIgnore] InertString PathText,
    [property: MarkoutIgnore] InertString KindText,
    [property: MarkoutIgnore] InertString EncodedTextValue)
{
    public string Path => PathText.ToString();
    public string Kind => KindText.ToString();

    [MarkoutPropertyName("Encoded Text")]
    public string EncodedText => EncodedTextValue.ToString();
}

[MarkoutSerializable]
public record PackageSourceLinkFileRow(
    [property: MarkoutIgnore] InertString LibraryText,
    [property: MarkoutIgnore] InertString FileText,
    [property: MarkoutIgnore] InertString StatusText)
{
    public string Library => LibraryText.ToString();
    public string File => FileText.ToString();
    public string Status => StatusText.ToString();
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public sealed record PackageSourceAvailabilitySection(
    InertString AuditedLibrariesText = default,
    int Embedded = 0,
    List<PackageSourceLinkIssueText>? FailedLibraryIssues = null,
    int Missing = 0,
    InertString SourceFilesText = default,
    InertString StatusText = default,
    List<PackageSourceLinkIssueText>? UnavailableLibraryIssues = null)
{
    [MarkoutIgnore]
    public InertString AuditedLibrariesText { get; init; } = AuditedLibrariesText;
    public string AuditedLibraries => AuditedLibrariesText.ToString();

    public int Embedded { get; init; } = Embedded;

    [MarkoutIgnore]
    public List<PackageSourceLinkIssueText>? FailedLibraryIssues { get; init; } = FailedLibraryIssues;
    public string? FailedLibraries => PackageViewText.RenderIssues(FailedLibraryIssues);

    public int Missing { get; init; } = Missing;

    [MarkoutIgnore]
    public InertString SourceFilesText { get; init; } = SourceFilesText;
    public string SourceFiles => SourceFilesText.ToString();

    [MarkoutIgnore]
    public InertString StatusText { get; init; } = StatusText;
    public string Status => StatusText.ToString();

    [MarkoutIgnore]
    public List<PackageSourceLinkIssueText>? UnavailableLibraryIssues { get; init; } = UnavailableLibraryIssues;
    public string? UnavailableLibraries => PackageViewText.RenderIssues(UnavailableLibraryIssues);
}

[MarkoutSerializable(NamingPolicy = NamingPolicy.PascalCaseWords, FieldLayout = FieldLayout.Table)]
[MarkoutSkipNull]
public sealed record PackageSourceIntegritySection(
    InertString CheckedLibrariesText = default,
    InertString? CrlfMismatchText = null,
    List<PackageSourceLinkIssueText>? FailedLibraryIssues = null,
    int Mismatched = 0,
    List<PackageSourceLinkFileText>? MismatchedSourceFiles = null,
    InertString StatusText = default,
    List<PackageSourceLinkIssueText>? UnavailableLibraryIssues = null,
    int Unverifiable = 0,
    int Verified = 0)
{
    [MarkoutIgnore]
    public InertString CheckedLibrariesText { get; init; } = CheckedLibrariesText;
    public string CheckedLibraries => CheckedLibrariesText.ToString();

    [MarkoutIgnore]
    public InertString? CrlfMismatchText { get; init; } = CrlfMismatchText;
    [MarkoutPropertyName("CR/LF Mismatch")]
    public string? CrlfMismatch => PackageViewText.Render(CrlfMismatchText);

    [MarkoutIgnore]
    public List<PackageSourceLinkIssueText>? FailedLibraryIssues { get; init; } = FailedLibraryIssues;
    public string? FailedLibraries => PackageViewText.RenderIssues(FailedLibraryIssues);

    public int Mismatched { get; init; } = Mismatched;

    [MarkoutIgnore]
    public List<PackageSourceLinkFileText>? MismatchedSourceFiles { get; init; } = MismatchedSourceFiles;
    public string? MismatchedFiles => PackageViewText.RenderSourceFiles(MismatchedSourceFiles);

    [MarkoutIgnore]
    public InertString StatusText { get; init; } = StatusText;
    public string Status => StatusText.ToString();

    [MarkoutIgnore]
    public List<PackageSourceLinkIssueText>? UnavailableLibraryIssues { get; init; } = UnavailableLibraryIssues;
    public string? UnavailableLibraries => PackageViewText.RenderIssues(UnavailableLibraryIssues);

    public int Unverifiable { get; init; } = Unverifiable;

    public int Verified { get; init; } = Verified;
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(InspectionResultView))]
[MarkoutContext(typeof(LibraryInspectionView))]
[MarkoutContext(typeof(ReferenceRow))]
[MarkoutContext(typeof(ExtensionMethodRow))]
[MarkoutContext(typeof(ClassifiedMethodRow))]
[MarkoutContext(typeof(PInvokeMethodRow))]
[MarkoutContext(typeof(ResourceRow))]
[MarkoutContext(typeof(ResourceTriageRow))]
[MarkoutContext(typeof(PerformanceRow))]
[MarkoutContext(typeof(PerformanceGroupRow))]
[MarkoutContext(typeof(PerformanceGroupView))]
[MarkoutContext(typeof(ReadyToRunImageRow))]
[MarkoutContext(typeof(ReadyToRunSectionRow))]
[MarkoutContext(typeof(BodyShapeRow))]
[MarkoutContext(typeof(BodyShapeSummaryRow))]
[MarkoutContext(typeof(CustomAttributeRow))]
[MarkoutContext(typeof(TypeForwarderRow))]
[MarkoutContext(typeof(AuditSignalRow))]
[MarkoutContext(typeof(PackageAuditSignalRow))]
[MarkoutContext(typeof(PackageTextConcernRow))]
[MarkoutContext(typeof(PackageAuditFindingRow))]
[MarkoutContext(typeof(IdentifierConfusionRow))]
[MarkoutContext(typeof(InspectionFailureRow))]
[MarkoutContext(typeof(SwitchRow))]
[MarkoutContext(typeof(IntegrationOpportunityRow))]
[MarkoutContext(typeof(IntegrationSignalRow))]
[MarkoutContext(typeof(PackageDependencyGroupRow))]
[MarkoutContext(typeof(PackageDependencyRow))]
[MarkoutContext(typeof(PackageDeprecationRow))]
[MarkoutContext(typeof(PackageVulnerabilityRow))]
[MarkoutContext(typeof(FlatDependency))]
[MarkoutContext(typeof(TargetFrameworkRow))]
[MarkoutContext(typeof(PackageFileRow))]
[MarkoutContext(typeof(PackageSourceFileRow))]
[MarkoutContext(typeof(PackageSourceLinkFileRow))]
[MarkoutContext(typeof(PackageSourceAvailabilitySection))]
[MarkoutContext(typeof(PackageSourceIntegritySection))]
[MarkoutContext(typeof(ILOffsetSection))]
[MarkoutContext(typeof(ILOffsetMemberContextSection))]
[MarkoutContext(typeof(ILOffsetInstructionContextSection))]
[MarkoutContext(typeof(ILOffsetExceptionContextRow))]
[MarkoutContext(typeof(ILOffsetCallsiteContextSection))]
[MarkoutContext(typeof(ILOffsetReturnAddressContextSection))]
[MarkoutContext(typeof(ManifestRow))]
[MarkoutContext(typeof(RidPackageReferenceView))]
[MarkoutContext(typeof(AggregatedSectionDocument))]
public partial class InspectionContext : MarkoutSerializerContext
{
}
