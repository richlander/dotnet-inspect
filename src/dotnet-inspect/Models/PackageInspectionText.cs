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
    private readonly bool _requiredContainment;

    public PackageInspectionText(InspectionResult data)
    {
        var collector = new Collector();

        PackageName = collector.Field(data.PackageName);
        ManifestVersion = collector.OptionalField(data.ManifestVersion);
        Version = collector.Field(data.Version);
        Source = collector.OptionalField(data.Source);
        Description = collector.Existing(data.Description);
        Authors = collector.OptionalField(data.Authors);
        License = collector.OptionalField(data.License);
        LicenseUrl = collector.OptionalField(data.LicenseUrl);
        Repository = collector.OptionalField(data.Repository);
        RepositoryType = collector.OptionalField(data.RepositoryType);
        RepositoryCommit = collector.OptionalField(data.RepositoryCommit);
        Owners = collector.Fields(data.Owners);
        Deprecation = data.Deprecation is { } deprecation
            ? CreateDeprecation(deprecation, collector)
            : null;
        Vulnerabilities = data.Vulnerabilities?
            .Select(value => new PackageVulnerabilityText(
                collector.Field(value.Severity),
                collector.OptionalField(value.CveId),
                collector.OptionalField(value.Summary),
                collector.OptionalField(value.AdvisoryUrl),
                collector.OptionalField(value.GhsaId)))
            .ToList();
        ReadmeFile = collector.OptionalField(data.ReadmeFile);
        PackageReadmeFile = collector.OptionalField(data.PackageReadmeFile);
        PackageTypes = collector.Fields(data.PackageTypes);
        ContentDirectories = collector.Fields(data.ContentDirectories);
        TargetFrameworks = collector.Fields(data.TargetFrameworks);
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
        SupportedRids = collector.Fields(data.SupportedRids);
        ToolFormat = collector.OptionalField(data.ToolFormat);
        ToolCommands = collector.Fields(data.ToolCommands);
        RuntimeIdentifierPackages = data.RuntimeIdentifierPackages?
            .Select(value => new RidPackageReferenceText(
                collector.Field(value.RuntimeIdentifier),
                collector.Field(value.PackageId),
                value.Exists))
            .ToList();
        RuntimeTargetRid = collector.OptionalField(data.RuntimeTargetRid);
        NativeFiles = collector.Fields(data.NativeFiles);
        LibraryFiles = collector.Fields(data.LibraryFiles);
        DependencyGroups = data.DependencyGroups?
            .Select(value => CreateDependencyGroup(value, collector))
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
            .Select(value => CreateDependency(value, collector))
            .ToList();
        Files = data.Files?
            .Select(value => new PackageFileText(
                collector.Field(value.Path),
                value.Size,
                value.IsReadme,
                value.IsAgents))
            .ToList();
        _packageFileSource = data.PackageFiles;
        SourceFiles = data.SourceFiles?
            .Select(value => new PackageSourceFileText(
                collector.Field(value.Library),
                collector.Field(value.Type),
                collector.OptionalField(value.Url)))
            .ToList();
        SignatureResult = data.SignatureResult is { } signature
            ? new PackageSignatureText(
                collector.OptionalField(signature.Publisher),
                signature.AuthorVerified,
                signature.RepositoryVerified,
                collector.OptionalField(signature.Repository),
                collector.OptionalField(signature.StatusMessage),
                signature.IsUnsigned)
            : null;
        AuditSignals = data.AuditSignals?
            .Select(value => new PackageAuditSignalText(
                collector.Field(value.Area),
                collector.Field(value.Signal),
                collector.Field(value.Value),
                collector.Field(value.Evidence)))
            .ToList();

        _requiredContainment = collector.RequiredContainment;
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
        : _packageFiles ??= _packageFileSource
            .Select(CreatePackageFileText)
            .ToList();
    public List<PackageSourceFileText>? SourceFiles { get; }
    public PackageSignatureText? SignatureResult { get; }
    public List<PackageAuditSignalText>? AuditSignals { get; }
    public bool RequiredContainment =>
        _requiredContainment
        || PackageFiles?.Any(value => value.Path.RequiredContainment) == true;

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

    private static PackageDeprecationText CreateDeprecation(
        PackageDeprecation value,
        Collector collector)
    {
        List<InertString>? reasons = collector.Fields(value.Reasons);
        InertString? message = collector.OptionalField(value.Message);
        InertString? alternatePackageId = collector.OptionalField(value.AlternatePackageId);
        List<InertString> parts = [];

        if (reasons is { Count: > 0 })
            parts.Add(collector.Compose(InertString.Join(", ", TextPolicy.Field, reasons)));
        if (alternatePackageId is { IsEmpty: false } alternate)
            parts.Add(collector.Compose(InertString.Format(TextPolicy.Field, $"use {alternate}")));
        if (message is { IsEmpty: false } deprecationMessage)
            parts.Add(deprecationMessage);

        InertString summary = parts.Count > 0
            ? collector.Compose(InertString.Join(" - ", TextPolicy.Field, parts))
            : collector.Field("Deprecated");

        return new PackageDeprecationText(reasons, message, alternatePackageId, summary);
    }

    private static PackageDependencyGroupText CreateDependencyGroup(
        DependencyGroup value,
        Collector collector)
        => new(
            collector.Field(value.TargetFramework),
            value.Dependencies.Select(dependency => CreateDependency(dependency, collector)).ToList());

    private static PackageDependencyText CreateDependency(
        PackageDependency value,
        Collector collector)
        => new(collector.Field(value.Id), collector.Field(value.Version));

    internal sealed class Collector
    {
        public bool RequiredContainment { get; private set; }

        public InertString Field(string value)
            => Compose(new InertString(TextPolicy.Field, value));

        public InertString? OptionalField(string? value)
            => value is null ? null : Field(value);

        public InertString? Existing(InertString? value)
            => value is { } existing ? Compose(existing) : null;

        public List<InertString>? Fields(List<string>? values)
            => values?.Select(Field).ToList();

        public InertString Compose(InertString value)
        {
            RequiredContainment |= value.RequiredContainment;
            return value;
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
    List<PackageDependencyText> Dependencies);

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
