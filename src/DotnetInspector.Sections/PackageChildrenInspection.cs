using System.Collections.Immutable;
using System.Text.Json.Serialization;

using InertText;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public enum PackageChildrenKind
{
    Libraries,
    RuntimeIdentifierPackages,
    NoManagedLibraries,
}

public enum PackageChildrenStatus
{
    Available,
    SelectedEmpty,
    NoCompileAssets,
    NoApplicableTarget,
    InvalidSelection,
    Unavailable,
}

public enum PackageLibraryChildRole
{
    Compile,
    ToolEntryPoint,
    ToolLibrary,
}

public sealed record PackageChildrenSubject
{
    public PackageChildrenSubject(
        string packageId,
        string packageVersion,
        string? targetFramework = null,
        string? source = null)
        : this(
            new InertString(TextPolicy.Field, packageId),
            new InertString(TextPolicy.Field, packageVersion),
            targetFramework is null
                ? null
                : new InertString(TextPolicy.Field, targetFramework),
            source is null
                ? null
                : new InertString(TextPolicy.Field, source))
    {
    }

    [JsonConstructor]
    public PackageChildrenSubject(
        InertString packageId,
        InertString packageVersion,
        InertString? targetFramework,
        InertString? source = null)
    {
        if (packageId.IsEmpty)
            throw new ArgumentException(
                "A Package children subject requires a Package ID.",
                nameof(packageId));
        if (packageVersion.IsEmpty)
            throw new ArgumentException(
                "A Package children subject requires a Package version.",
                nameof(packageVersion));

        PackageId = packageId;
        PackageVersion = packageVersion;
        TargetFramework = targetFramework;
        Source = source;
    }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString PackageId { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString PackageVersion { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? TargetFramework { get; }

    /// <summary>
    /// The acquisition source the children were selected from, such as a
    /// NuGet feed or a local file, when the composition knows it.
    /// </summary>
    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Source { get; }
}

/// <summary>
/// The owner-issued <see cref="ResultProperty"/> set of a Package children
/// result, per <c>docs/design/section-shapes.md#properties</c>: the source,
/// the selected target, and the asset root(s) of the displayed children, in
/// that order, each present only when the document carries it. Renderers print
/// these values and nothing else beside the subject identity.
/// </summary>
public static class PackageChildrenProperties
{
    public const string SourceName = "source";
    public const string TargetName = "target";
    public const string RootName = "root";

    public static ImmutableArray<ResultProperty> For(
        PackageChildrenDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var properties = ImmutableArray.CreateBuilder<ResultProperty>(3);
        if (document.Subject.Source is { IsEmpty: false } source)
            properties.Add(new ResultProperty(SourceName, source));
        if (document.Subject.TargetFramework is { IsEmpty: false } target)
            properties.Add(new ResultProperty(TargetName, target));
        string roots = string.Join(
            ", ",
            document.Libraries
                .Select(static library => AssetRoot(library.AssetPath.ToString()))
                .Where(static root => root.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        if (roots.Length > 0)
            properties.Add(new ResultProperty(RootName, roots));
        return properties.ToImmutable();
    }

    private static string AssetRoot(string assetPath)
    {
        int separator = assetPath.IndexOfAny(['/', '\\']);
        return separator > 0 ? assetPath[..separator] : "";
    }
}

public sealed record PackageLibraryChild(
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString AssetId,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString AssetPath,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString AssemblyName,
    PackageLibraryChildRole Role);

public sealed record PackageRuntimeIdentifierChild
{
    public PackageRuntimeIdentifierChild(
        string runtimeIdentifier,
        string packageId)
        : this(
            new InertString(TextPolicy.Field, runtimeIdentifier),
            new InertString(TextPolicy.Field, packageId))
    {
    }

    [JsonConstructor]
    public PackageRuntimeIdentifierChild(
        InertString runtimeIdentifier,
        InertString packageId)
    {
        if (runtimeIdentifier.IsEmpty)
        {
            throw new ArgumentException(
                "A RID Package child requires a runtime identifier.",
                nameof(runtimeIdentifier));
        }
        if (packageId.IsEmpty)
        {
            throw new ArgumentException(
                "A RID Package child requires a Package ID.",
                nameof(packageId));
        }

        RuntimeIdentifier = runtimeIdentifier;
        PackageId = packageId;
    }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString RuntimeIdentifier { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString PackageId { get; }
}

public sealed record PackageChildrenDocument
{
    [JsonConstructor]
    public PackageChildrenDocument(
        PackageChildrenSubject subject,
        PackageChildrenKind kind,
        PackageChildrenStatus status,
        ImmutableArray<PackageLibraryChild> libraries,
        ImmutableArray<PackageRuntimeIdentifierChild> runtimeIdentifierPackages,
        InertString? detail,
        bool isComplete)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (libraries.IsDefault)
            throw new ArgumentException(
                "Package Library children must be initialized.",
                nameof(libraries));
        if (runtimeIdentifierPackages.IsDefault)
        {
            throw new ArgumentException(
                "Package RID children must be initialized.",
                nameof(runtimeIdentifierPackages));
        }
        if (kind != PackageChildrenKind.Libraries
            && !libraries.IsEmpty)
        {
            throw new ArgumentException(
                "Only a Library population can carry Library rows.",
                nameof(libraries));
        }
        if (kind != PackageChildrenKind.RuntimeIdentifierPackages
            && !runtimeIdentifierPackages.IsEmpty)
        {
            throw new ArgumentException(
                "Only a RID Package population can carry RID rows.",
                nameof(runtimeIdentifierPackages));
        }

        Subject = subject;
        Kind = kind;
        Status = status;
        Libraries = libraries;
        RuntimeIdentifierPackages = runtimeIdentifierPackages;
        Detail = detail;
        IsComplete = isComplete;
    }

    public PackageChildrenSubject Subject { get; }
    public PackageChildrenKind Kind { get; }
    public PackageChildrenStatus Status { get; }
    public ImmutableArray<PackageLibraryChild> Libraries { get; }
    public ImmutableArray<PackageRuntimeIdentifierChild>
        RuntimeIdentifierPackages
    { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? Detail { get; }

    public bool IsComplete { get; }

    public static PackageChildrenDocument FromRuntimeIdentifierPackages(
        PackageChildrenSubject subject,
        IEnumerable<PackageRuntimeIdentifierChild> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ImmutableArray<PackageRuntimeIdentifierChild> rows =
        [
            .. packages.OrderBy(
                static package => package.RuntimeIdentifier.ToString(),
                StringComparer.Ordinal),
        ];
        return new(
            subject,
            PackageChildrenKind.RuntimeIdentifierPackages,
            rows.IsEmpty
                ? PackageChildrenStatus.SelectedEmpty
                : PackageChildrenStatus.Available,
            [],
            rows,
            detail: null,
            isComplete: true);
    }

    public static PackageChildrenDocument NoManagedLibraries(
        PackageChildrenSubject subject,
        string detail) =>
        new(
            subject,
            PackageChildrenKind.NoManagedLibraries,
            PackageChildrenStatus.Available,
            [],
            [],
            new InertString(TextPolicy.Field, detail),
            isComplete: true);

    public static PackageChildrenDocument UnavailableLibraries(
        PackageChildrenSubject subject,
        PackageChildrenStatus status,
        string detail) =>
        LibrariesWithoutRows(
            subject,
            status,
            detail,
            isComplete: false);

    public static PackageChildrenDocument LibrariesWithoutRows(
        PackageChildrenSubject subject,
        PackageChildrenStatus status,
        string? detail,
        bool isComplete)
    {
        if (status is PackageChildrenStatus.Available)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new(
            subject,
            PackageChildrenKind.Libraries,
            status,
            [],
            [],
            detail is null
                ? null
                : new InertString(TextPolicy.Field, detail),
            isComplete);
    }
}

public sealed record PackageLibraryChildCandidate(
    string AssetId,
    string AssetPath,
    string AssemblyName,
    string? TargetFramework,
    PackageLibraryChildRole Role);

public static class PackageChildrenInspection
{
    public static InspectionEnvelope<PackageChildrenDocument> Execute(
            PackageChildrenSubject subject,
            IReadOnlyList<PackageLibraryChildCandidate> candidates,
            PackageChildrenCapabilityPlan capabilityPlan)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(candidates);
        ValidateRowsPlan(
            capabilityPlan,
            candidates.Count);
        ImmutableArray<PackageLibraryChild> rows =
        [
            .. candidates
                .Skip(capabilityPlan.SelectedStart)
                .Take(capabilityPlan.SelectedCount)
                .Select(
                    static candidate =>
                        new PackageLibraryChild(
                            new InertString(
                                TextPolicy.Field,
                                candidate.AssetId),
                            new InertString(
                                TextPolicy.Field,
                                candidate.AssetPath),
                            new InertString(
                                TextPolicy.Field,
                                candidate.AssemblyName),
                            candidate.Role)),
        ];

        return Envelope(
            new(
                subject,
                PackageChildrenKind.Libraries,
                rows.IsEmpty
                    ? PackageChildrenStatus.SelectedEmpty
                    : PackageChildrenStatus.Available,
                rows,
                [],
                detail: null,
                isComplete: true),
            []);
    }

    static void ValidateRowsPlan(
        PackageChildrenCapabilityPlan capabilityPlan,
        int sourceCount)
    {
        ArgumentNullException.ThrowIfNull(capabilityPlan);
        if (capabilityPlan.Terminal
            is not QuerySpaceTerminalRequirement.Rows
            || capabilityPlan.RowProvision
                is not PackageChildrenRowProvision.SelectedRows)
        {
            throw new ArgumentException(
                "Package Library inspection requires an accepted "
                    + "selected-Rows capability plan.",
                nameof(capabilityPlan));
        }
        if (capabilityPlan.SourceCount != sourceCount)
        {
            throw new ArgumentException(
                "The Package child source population does not match "
                    + "the accepted capability plan.",
                nameof(capabilityPlan));
        }
    }

    private static InspectionEnvelope<PackageChildrenDocument> Envelope(
        PackageChildrenDocument document,
        ImmutableArray<InspectionDiagnostic> diagnostics) =>
        new(
            document,
            new InspectionShare.NonProjectable(
                "package-children/share",
                "Package children do not yet have a canonical Workspace "
                    + "Share projection."),
            diagnostics);
}
