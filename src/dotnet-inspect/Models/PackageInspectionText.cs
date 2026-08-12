using DotnetInspector.Packages;
using DotnetInspector.Services;
using InertText;

namespace DotnetInspector.Models;

/// <summary>
/// The package inspection's artifact-derived text, contained at the shared
/// presentation boundary.
/// </summary>
internal sealed class PackageInspectionText
{
    private readonly List<PackageFile>? _packageFileSource;
    private List<PackageFileText>? _packageFiles;
    private List<PackageTextConcernCase>? _packageFileConcernCases;
    private readonly TextConcern _concerns;
    private readonly IReadOnlyList<PackageTextConcernCase> _concernCases;

    public PackageInspectionText(InspectionResult data)
    {
        var collector = new Collector();

        PackageName = collector.Field(data.PackageName, nameof(data.PackageName));
        ManifestVersion = collector.OptionalField(data.ManifestVersion, nameof(data.ManifestVersion));
        Version = collector.Field(data.Version, nameof(data.Version));
        Source = collector.OptionalField(data.Source, nameof(data.Source));
        Description = collector.Existing(data.Description, nameof(data.Description));
        Authors = collector.OptionalField(data.Authors, nameof(data.Authors));
        License = collector.OptionalField(data.License, nameof(data.License));
        LicenseUrl = collector.OptionalField(data.LicenseUrl, nameof(data.LicenseUrl));
        Repository = collector.OptionalField(data.Repository, nameof(data.Repository));
        RepositoryType = collector.OptionalField(data.RepositoryType, nameof(data.RepositoryType));
        RepositoryCommit = collector.OptionalField(data.RepositoryCommit, nameof(data.RepositoryCommit));
        Owners = collector.Fields(data.Owners, nameof(data.Owners));
        Deprecation = data.Deprecation is { } deprecation
            ? CreateDeprecation(deprecation, collector, nameof(data.Deprecation))
            : null;
        Vulnerabilities = data.Vulnerabilities?
            .Select((value, index) =>
            {
                string location = $"{nameof(data.Vulnerabilities)}[{index}]";
                return new PackageVulnerabilityText(
                    collector.Field(value.Severity, $"{location}.{nameof(value.Severity)}"),
                    collector.OptionalField(value.CveId, $"{location}.{nameof(value.CveId)}"),
                    collector.OptionalField(value.Summary, $"{location}.{nameof(value.Summary)}"),
                    collector.OptionalField(value.AdvisoryUrl, $"{location}.{nameof(value.AdvisoryUrl)}"),
                    collector.OptionalField(value.GhsaId, $"{location}.{nameof(value.GhsaId)}"));
            })
            .ToList();
        ReadmeFile = collector.OptionalField(data.ReadmeFile, nameof(data.ReadmeFile));
        PackageReadmeFile = collector.OptionalField(data.PackageReadmeFile, nameof(data.PackageReadmeFile));
        PackageTypes = collector.Fields(data.PackageTypes, nameof(data.PackageTypes));
        ContentDirectories = collector.Fields(data.ContentDirectories, nameof(data.ContentDirectories));
        TargetFrameworks = collector.Fields(data.TargetFrameworks, nameof(data.TargetFrameworks));
        OrderedTargetFrameworks = data.TargetFrameworks is { } targetFrameworks
            && TargetFrameworks is { } containedTargetFrameworks
            ? TfmSelector.OrderByTfmPriorityDescending(
                    Enumerable.Range(0, targetFrameworks.Count),
                    index => targetFrameworks[index])
                .Select(index => containedTargetFrameworks[index])
                .ToList()
            : null;
        HighestTfm = OrderedTargetFrameworks is { Count: > 0 } orderedTargetFrameworks
            ? orderedTargetFrameworks[0]
            : null;
        SupportedRids = collector.Fields(data.SupportedRids, nameof(data.SupportedRids));
        ToolFormat = collector.OptionalField(data.ToolFormat, nameof(data.ToolFormat));
        ToolCommands = collector.Fields(data.ToolCommands, nameof(data.ToolCommands));
        RuntimeIdentifierPackages = data.RuntimeIdentifierPackages?
            .Select((value, index) =>
            {
                string location = $"{nameof(data.RuntimeIdentifierPackages)}[{index}]";
                return new RidPackageReferenceText(
                    collector.Field(value.RuntimeIdentifier, $"{location}.{nameof(value.RuntimeIdentifier)}"),
                    collector.Field(value.PackageId, $"{location}.{nameof(value.PackageId)}"),
                    value.Exists);
            })
            .ToList();
        RuntimeTargetRid = collector.OptionalField(data.RuntimeTargetRid, nameof(data.RuntimeTargetRid));
        NativeFiles = collector.Fields(data.NativeFiles, nameof(data.NativeFiles));
        LibraryFiles = collector.Fields(data.LibraryFiles, nameof(data.LibraryFiles));
        DependencyGroups = data.DependencyGroups?
            .Select((value, index) => CreateDependencyGroup(
                value,
                collector,
                $"{nameof(data.DependencyGroups)}[{index}]"))
            .ToList();
        FlatDependencies = data.DependencyGroups is { } groups
            && DependencyGroups is { } containedGroups
            ? TfmSelector.OrderByTfmPriorityDescending(
                    Enumerable.Range(0, groups.Count),
                    index => groups[index].TargetFramework)
                .ThenBy(index => groups[index].TargetFramework)
                .SelectMany(groupIndex => groups[groupIndex].Dependencies
                    .Select((dependency, dependencyIndex) => (dependency.Id, dependencyIndex))
                    .OrderBy(value => value.Id)
                    .Select(value => new FlatPackageDependencyText(
                        containedGroups[groupIndex].TargetFramework,
                        containedGroups[groupIndex].Dependencies[value.dependencyIndex].Id,
                        containedGroups[groupIndex].Dependencies[value.dependencyIndex].Version)))
                .ToList()
            : null;
        RuntimeDependencies = data.RuntimeDependencies?
            .Select((value, index) => CreateDependency(
                value,
                collector,
                $"{nameof(data.RuntimeDependencies)}[{index}]"))
            .ToList();
        Files = data.Files?
            .Select((value, index) => new PackageFileText(
                collector.Field(value.Path, $"{nameof(data.Files)}[{index}].{nameof(value.Path)}"),
                value.Size,
                value.IsReadme,
                value.IsAgents))
            .ToList();
        _packageFileSource = data.PackageFiles;
        SourceFiles = data.SourceFiles?
            .Select((value, index) =>
            {
                string location = $"{nameof(data.SourceFiles)}[{index}]";
                return new PackageSourceFileText(
                    collector.Field(value.Library, $"{location}.{nameof(value.Library)}"),
                    collector.Field(value.Type, $"{location}.{nameof(value.Type)}"),
                    collector.OptionalField(value.Url, $"{location}.{nameof(value.Url)}"));
            })
            .ToList();
        SourceAvailability = data.SourceAvailability is { } availability
            ? new PackageSourceAvailabilityText(
                availability.TotalLibraries,
                availability.AuditedLibraries,
                availability.TotalSourceFiles,
                availability.AccessibleSourceFiles,
                availability.EmbeddedSourceFiles,
                availability.MissingFiles?
                    .Select((value, index) => CreateSourceLinkFile(
                        value,
                        collector,
                        $"{nameof(data.SourceAvailability)}.{nameof(availability.MissingFiles)}[{index}]"))
                    .ToList(),
                availability.UnavailableLibraries?
                    .Select((value, index) => CreateSourceLinkIssue(
                        value,
                        collector,
                        $"{nameof(data.SourceAvailability)}.{nameof(availability.UnavailableLibraries)}[{index}]"))
                    .ToList(),
                availability.FailedLibraries?
                    .Select((value, index) => CreateSourceLinkIssue(
                        value,
                        collector,
                        $"{nameof(data.SourceAvailability)}.{nameof(availability.FailedLibraries)}[{index}]"))
                    .ToList())
            : null;
        SourceIntegrity = data.SourceIntegrity is { } integrity
            ? new PackageSourceIntegrityText(
                integrity.TotalLibraries,
                integrity.CheckedLibraries,
                integrity.Verified,
                integrity.Mismatched,
                integrity.LineEndingNormalized,
                integrity.Unverifiable,
                integrity.MismatchedFiles?
                    .Select((value, index) => CreateSourceLinkFile(
                        value,
                        collector,
                        $"{nameof(data.SourceIntegrity)}.{nameof(integrity.MismatchedFiles)}[{index}]"))
                    .ToList(),
                integrity.UnavailableLibraries?
                    .Select((value, index) => CreateSourceLinkIssue(
                        value,
                        collector,
                        $"{nameof(data.SourceIntegrity)}.{nameof(integrity.UnavailableLibraries)}[{index}]"))
                    .ToList(),
                integrity.FailedLibraries?
                    .Select((value, index) => CreateSourceLinkIssue(
                        value,
                        collector,
                        $"{nameof(data.SourceIntegrity)}.{nameof(integrity.FailedLibraries)}[{index}]"))
                    .ToList())
            : null;
        SignatureResult = data.SignatureResult is { } signature
            ? new PackageSignatureText(
                collector.OptionalField(
                    signature.Publisher,
                    $"{nameof(data.SignatureResult)}.{nameof(signature.Publisher)}"),
                signature.AuthorVerified,
                signature.RepositoryVerified,
                collector.OptionalField(
                    signature.Repository,
                    $"{nameof(data.SignatureResult)}.{nameof(signature.Repository)}"),
                collector.OptionalField(
                    signature.StatusMessage,
                    $"{nameof(data.SignatureResult)}.{nameof(signature.StatusMessage)}"),
                signature.IsUnsigned)
            : null;
        AuditSignals = data.AuditSignals?
            .Select((value, index) =>
            {
                string location = $"{nameof(data.AuditSignals)}[{index}]";
                return new PackageAuditSignalText(
                    collector.Field(value.Area, $"{location}.{nameof(value.Area)}"),
                    collector.Field(value.Signal, $"{location}.{nameof(value.Signal)}"),
                    collector.Field(value.Value, $"{location}.{nameof(value.Value)}"),
                    collector.Field(value.Evidence, $"{location}.{nameof(value.Evidence)}"));
            })
            .ToList();

        _concerns = collector.Concerns;
        _concernCases = collector.ConcernCases;
    }

    public InertString PackageName { get; }
    public InertString? ManifestVersion { get; }
    public InertString Version { get; }
    public InertString? Source { get; }
    public InertString? Description { get; }
    public InertString? Authors { get; }
    public InertString? License { get; }
    public InertString? LicenseUrl { get; }
    public InertString? Repository { get; }
    public InertString? RepositoryType { get; }
    public InertString? RepositoryCommit { get; }
    public List<InertString>? Owners { get; }
    public PackageDeprecationText? Deprecation { get; }
    public List<PackageVulnerabilityText>? Vulnerabilities { get; }
    public InertString? ReadmeFile { get; }
    public InertString? PackageReadmeFile { get; }
    public List<InertString>? PackageTypes { get; }
    public List<InertString>? ContentDirectories { get; }
    public List<InertString>? TargetFrameworks { get; }
    public List<InertString>? OrderedTargetFrameworks { get; }
    public InertString? HighestTfm { get; }
    public List<InertString>? SupportedRids { get; }
    public InertString? ToolFormat { get; }
    public List<InertString>? ToolCommands { get; }
    public List<RidPackageReferenceText>? RuntimeIdentifierPackages { get; }
    public InertString? RuntimeTargetRid { get; }
    public List<InertString>? NativeFiles { get; }
    public List<InertString>? LibraryFiles { get; }
    public List<PackageDependencyGroupText>? DependencyGroups { get; }
    public List<FlatPackageDependencyText>? FlatDependencies { get; }
    public List<PackageDependencyText>? RuntimeDependencies { get; }
    public List<PackageFileText>? Files { get; }
    public List<PackageFileText>? PackageFiles => _packageFileSource is null
        ? null
        : _packageFiles ??= ProjectPackageFiles();
    public List<PackageSourceFileText>? SourceFiles { get; }
    public PackageSourceAvailabilityText? SourceAvailability { get; }
    public PackageSourceIntegrityText? SourceIntegrity { get; }
    public PackageSignatureText? SignatureResult { get; }
    public List<PackageAuditSignalText>? AuditSignals { get; }
    public TextConcern Concerns =>
        _concerns
        | (PackageFiles?.Aggregate(
            TextConcern.None,
            static (concerns, value) => concerns | value.Path.Concerns)
            ?? TextConcern.None);
    public bool RequiredContainment => Concerns != TextConcern.None;
    public IReadOnlyList<PackageTextConcernCase> ConcernCases
    {
        get
        {
            _ = PackageFiles;
            return _packageFileConcernCases is { Count: > 0 } packageFileCases
                ? _concernCases.Concat(packageFileCases).ToArray()
                : _concernCases;
        }
    }

    public List<PackageFileText>? SelectPackageFiles(Func<PackageFile, bool> predicate)
    {
        if (_packageFileSource is null)
            return null;

        if (_packageFiles is { } projected)
        {
            return _packageFileSource
                .Select((file, index) => (File: file, Text: projected[index]))
                .Where(value => predicate(value.File))
                .Select(value => value.Text)
                .ToList();
        }

        return _packageFileSource
            .Where(predicate)
            .Select(CreatePackageFileText)
            .ToList();
    }

    private static PackageFileText CreatePackageFileText(PackageFile value)
        => new(
            new InertString(TextPolicy.Field, value.Path),
            value.Size,
            value.IsReadme,
            value.IsAgents);

    private List<PackageFileText> ProjectPackageFiles()
    {
        List<PackageFileText> files = new(_packageFileSource!.Count);
        List<PackageTextConcernCase> cases = [];
        for (int index = 0; index < _packageFileSource.Count; index++)
        {
            PackageFileText file = CreatePackageFileText(_packageFileSource[index]);
            files.Add(file);
            if (file.Path.Concerns != TextConcern.None)
            {
                cases.Add(new PackageTextConcernCase(
                    $"{nameof(InspectionResult.PackageFiles)}[{index}].{nameof(PackageFile.Path)}",
                    file.Path.Concerns));
            }
        }

        _packageFileConcernCases = cases;
        return files;
    }

    private static PackageDeprecationText CreateDeprecation(
        PackageDeprecation value,
        Collector collector,
        string location)
    {
        List<InertString>? reasons = collector.Fields(
            value.Reasons,
            $"{location}.{nameof(value.Reasons)}");
        InertString? message = collector.OptionalField(
            value.Message,
            $"{location}.{nameof(value.Message)}");
        InertString? alternatePackageId = collector.OptionalField(
            value.AlternatePackageId,
            $"{location}.{nameof(value.AlternatePackageId)}");
        List<InertString> parts = [];

        if (reasons is { Count: > 0 })
            parts.Add(collector.Compose(InertString.Join(", ", TextPolicy.Field, reasons)));
        if (alternatePackageId is { IsEmpty: false } alternate)
            parts.Add(collector.Compose(InertString.Format(TextPolicy.Field, $"use {alternate}")));
        if (message is { IsEmpty: false } deprecationMessage)
            parts.Add(deprecationMessage);

        InertString summary = parts.Count > 0
            ? collector.Compose(InertString.Join(" - ", TextPolicy.Field, parts))
            : new InertString(TextPolicy.Field, "Deprecated");

        return new PackageDeprecationText(reasons, message, alternatePackageId, summary);
    }

    private static PackageDependencyGroupText CreateDependencyGroup(
        DependencyGroup value,
        Collector collector,
        string location)
        => new(
            collector.Field(
                value.TargetFramework,
                $"{location}.{nameof(value.TargetFramework)}"),
            value.Dependencies.Select((dependency, index) => CreateDependency(
                dependency,
                collector,
                $"{location}.{nameof(value.Dependencies)}[{index}]")).ToList(),
            value.IsImplicitManifestGroup);

    private static PackageDependencyText CreateDependency(
        PackageDependency value,
        Collector collector,
        string location)
        => new(
            collector.Field(value.Id, $"{location}.{nameof(value.Id)}"),
            collector.Field(value.Version, $"{location}.{nameof(value.Version)}"));

    private static PackageSourceLinkFileText CreateSourceLinkFile(
        PackageSourceLinkFile value,
        Collector collector,
        string location)
        => new(
            collector.Field(value.Library, $"{location}.{nameof(value.Library)}"),
            collector.Field(value.Path, $"{location}.{nameof(value.Path)}"));

    private static PackageSourceLinkIssueText CreateSourceLinkIssue(
        PackageSourceLinkIssue value,
        Collector collector,
        string location)
        => new(
            collector.Field(value.Library, $"{location}.{nameof(value.Library)}"),
            collector.Field(value.Reason, $"{location}.{nameof(value.Reason)}"));

    internal sealed class Collector
    {
        private readonly List<PackageTextConcernCase> _concernCases = [];

        public TextConcern Concerns { get; private set; }
        public IReadOnlyList<PackageTextConcernCase> ConcernCases => _concernCases;

        public InertString Field(string value, string location)
            => Track(new InertString(TextPolicy.Field, value), location);

        public InertString? OptionalField(string? value, string location)
            => value is null ? null : Field(value, location);

        public InertString? Existing(InertString? value, string location)
            => value is { } existing ? Track(existing, location) : null;

        public List<InertString>? Fields(List<string>? values, string location)
            => values?
                .Select((value, index) => Field(value, $"{location}[{index}]"))
                .ToList();

        private InertString Track(InertString value, string location)
        {
            Compose(value);
            if (value.Concerns != TextConcern.None)
                _concernCases.Add(new PackageTextConcernCase(location, value.Concerns));
            return value;
        }

        public InertString Compose(InertString value)
        {
            Concerns |= value.Concerns;
            return value;
        }
    }
}

internal readonly record struct PackageTextConcernCase(
    string Location,
    TextConcern Concerns);

internal static class TextConcernDisplay
{
    public static string Describe(TextConcern concerns)
    {
        if (concerns == TextConcern.None)
            return "no concerning scalars found";

        List<string> kinds = [];
        AddKind(TextConcern.Control, "control (Cc)");
        AddKind(TextConcern.Format, "format/bidi (Cf)");
        AddKind(TextConcern.Surrogate, "unpaired surrogate (Cs)");
        AddKind(TextConcern.LineSeparator, "line separator (Zl)");
        AddKind(TextConcern.ParagraphSeparator, "paragraph separator (Zp)");
        return string.Join(", ", kinds);

        void AddKind(TextConcern kind, string description)
        {
            if ((concerns & kind) != TextConcern.None)
                kinds.Add(description);
        }
    }
}

internal sealed record PackageDeprecationText(
    List<InertString>? Reasons,
    InertString? Message,
    InertString? AlternatePackageId,
    InertString Summary);

internal readonly record struct PackageVulnerabilityText(
    InertString Severity,
    InertString? CveId,
    InertString? Summary,
    InertString? AdvisoryUrl,
    InertString? GhsaId);

internal readonly record struct RidPackageReferenceText(
    InertString RuntimeIdentifier,
    InertString PackageId,
    bool? Exists)
{
    public string AvailableDisplay => Exists switch
    {
        true => "yes",
        false => "no",
        null => "unknown"
    };
}

internal sealed record PackageDependencyGroupText(
    InertString TargetFramework,
    List<PackageDependencyText> Dependencies,
    bool IsImplicitManifestGroup);

internal readonly record struct PackageDependencyText(
    InertString Id,
    InertString Version);

internal readonly record struct FlatPackageDependencyText(
    InertString TargetFramework,
    InertString Id,
    InertString Version);

internal readonly record struct PackageFileText(
    InertString Path,
    long Size,
    bool IsReadme,
    bool IsAgents);

internal readonly record struct PackageSourceFileText(
    InertString Library,
    InertString Type,
    InertString? Url);

public readonly record struct PackageSourceLinkIssueText(
    InertString Library,
    InertString Reason);

public readonly record struct PackageSourceLinkFileText(
    InertString Library,
    InertString Path);

internal sealed record PackageSourceAvailabilityText(
    int TotalLibraries,
    int AuditedLibraries,
    int TotalSourceFiles,
    int AccessibleSourceFiles,
    int EmbeddedSourceFiles,
    List<PackageSourceLinkFileText>? MissingFiles,
    List<PackageSourceLinkIssueText>? UnavailableLibraries,
    List<PackageSourceLinkIssueText>? FailedLibraries);

internal sealed record PackageSourceIntegrityText(
    int TotalLibraries,
    int CheckedLibraries,
    int Verified,
    int Mismatched,
    int LineEndingNormalized,
    int Unverifiable,
    List<PackageSourceLinkFileText>? MismatchedFiles,
    List<PackageSourceLinkIssueText>? UnavailableLibraries,
    List<PackageSourceLinkIssueText>? FailedLibraries);

internal readonly record struct PackageSignatureText(
    InertString? Publisher,
    bool AuthorVerified,
    bool RepositoryVerified,
    InertString? Repository,
    InertString? StatusMessage,
    bool IsUnsigned);

internal readonly record struct PackageAuditSignalText(
    InertString Area,
    InertString Signal,
    InertString Value,
    InertString Evidence);

internal sealed class PackageFileJsonRow(InertString path, long size)
{
    private InertString PathText { get; } = path;

    public string Path => PathText.ToString();
    public long Size { get; } = size;
}

internal sealed class PackageFileMultiJsonRow(
    InertString package,
    InertString version,
    InertString path,
    long? size)
{
    private InertString PackageText { get; } = package;
    private InertString VersionText { get; } = version;
    private InertString PathText { get; } = path;

    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Path => PathText.ToString();
    public long? Size { get; } = size;
}

internal sealed class PackageFileContentText
{
    private PackageFileContentText(PackageFileContent value)
    {
        PackageText = new InertString(TextPolicy.Field, value.Package);
        VersionText = new InertString(TextPolicy.Field, value.Version);
        PathText = new InertString(TextPolicy.Field, value.Path);
        Size = value.Size;
        Found = value.Found;
        Content = value.Content;
    }

    public static PackageFileContentText Create(PackageFileContent value) => new(value);

    internal InertString PackageText { get; }
    internal InertString VersionText { get; }
    internal InertString PathText { get; }

    public string Package => PackageText.ToString();
    public string Version => VersionText.ToString();
    public string Path => PathText.ToString();
    public long Size { get; }
    public bool Found { get; }
    public string Content { get; }
}
