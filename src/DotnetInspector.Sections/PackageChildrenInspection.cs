using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;

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

public enum PackageLibraryChildUnavailableReason
{
    AssemblyUnavailable,
    NotManagedAssembly,
    MaterializationFailed,
    OperationLeaseUnavailable,
    InspectionRejected,
    InspectionFailed,
    CountUnavailable,
    CleanupFailed,
}

public sealed record PackageChildrenSubject
{
    public PackageChildrenSubject(
        string packageId,
        string packageVersion,
        string? targetFramework = null)
        : this(
            new InertString(TextPolicy.Field, packageId),
            new InertString(TextPolicy.Field, packageVersion),
            targetFramework is null
                ? null
                : new InertString(TextPolicy.Field, targetFramework))
    {
    }

    [JsonConstructor]
    public PackageChildrenSubject(
        InertString packageId,
        InertString packageVersion,
        InertString? targetFramework)
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
    }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString PackageId { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString PackageVersion { get; }

    [JsonConverter(typeof(InertStringJsonConverter))]
    public InertString? TargetFramework { get; }
}

public sealed record PackageLibraryChildUnavailable(
    PackageLibraryChildUnavailableReason Reason,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString Detail);

public sealed record PackageLibraryChild(
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString AssetId,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString AssetPath,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString AssemblyName,
    PackageLibraryChildRole Role,
    LibraryTypePopulationCountOutcome? PublicTypeDeclarations,
    PackageLibraryChildUnavailable? Unavailable)
{
    public bool IsAvailable =>
        PublicTypeDeclarations
            is LibraryTypePopulationCountOutcome.Counted
        && Unavailable is null;
}

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

public sealed record PackageChildrenInspectionLimits
{
    public static PackageChildrenInspectionLimits Default { get; } =
        new();

    public int MaxLibraries { get; init; } = 256;

    public AssemblyContextLibraryMaterializationLimits Materialization
    {
        get;
        init;
    } = new(
        maxCapturedImageBytes: 64L * 1024 * 1024,
        maxRetainedArtifactBytes: 64L * 1024 * 1024);

    public ApiSurfaceExtractionBounds Extraction { get; init; } =
        new(
            maxTypes: 5_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 20_000_000);

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxLibraries, 1);
        ArgumentNullException.ThrowIfNull(Materialization);
        ArgumentNullException.ThrowIfNull(Extraction);
    }
}

public sealed class PackageLibraryInspectionTarget
{
    private PackageLibraryInspectionTarget(
        InertString assetId,
        InertString assetPath,
        PackageLibraryChildRole role,
        InertString assemblyName,
        PackageLibraryChildUnavailable unavailable)
    {
        AssetId = assetId;
        AssetPath = assetPath;
        Role = role;
        AssemblyName = assemblyName;
        Unavailable = unavailable;
    }

    public PackageLibraryInspectionTarget(
        string assetId,
        string assetPath,
        PackageLibraryChildRole role,
        AssemblyContextGroup group,
        AssemblyContextParticipant participant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        AssetId = new InertString(TextPolicy.Field, assetId);
        AssetPath = new InertString(TextPolicy.Field, assetPath);
        Role = role;
        Group = group ?? throw new ArgumentNullException(nameof(group));
        Participant =
            participant
            ?? throw new ArgumentNullException(nameof(participant));
        if (!group.Participants.Any(
                candidate => ReferenceEquals(candidate, participant)))
        {
            throw new ArgumentException(
                "A Package Library target must belong to its supplied assembly context group.",
                nameof(participant));
        }
        AssemblyName = new InertString(
            TextPolicy.Field,
            participant.Assembly.Identity.Name);
    }

    public InertString AssetId { get; }
    public InertString AssetPath { get; }
    public PackageLibraryChildRole Role { get; }
    public InertString AssemblyName { get; }
    public AssemblyContextGroup? Group { get; }
    public AssemblyContextParticipant? Participant { get; }
    public PackageLibraryChildUnavailable? Unavailable { get; }

    public static PackageLibraryInspectionTarget CreateUnavailable(
        string assetId,
        string assetPath,
        string assemblyName,
        PackageLibraryChildRole role,
        PackageLibraryChildUnavailableReason reason,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));

        return new(
            new InertString(TextPolicy.Field, assetId),
            new InertString(TextPolicy.Field, assetPath),
            role,
            new InertString(TextPolicy.Field, assemblyName),
            new(
                reason,
                new InertString(TextPolicy.Field, detail)));
    }
}

public sealed record PackageLibraryInspectionCandidate(
    string AssetId,
    string AssetPath,
    string AssemblyName,
    string? TargetFramework,
    PackageLibraryChildRole Role);

public static class PackageChildrenInspection
{
    public static async ValueTask<InspectionEnvelope<PackageChildrenDocument>>
        ExecutePackageEntriesAsync(
            PackageChildrenSubject subject,
            PackageInspectionInput input,
            IReadOnlyList<PackageLibraryInspectionCandidate> candidates,
            PackageAssemblyContextRealizationOptions? realizationOptions = null,
            PackageChildrenInspectionLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return await ExecuteLibrariesAsync(
                    subject,
                    [],
                    limits,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        PackageInspectionSelection selection =
            input.SelectAssemblies(
                candidates.Select(
                    static candidate =>
                        new PackageInspectionAssembly(
                            candidate.AssetPath,
                            candidate.TargetFramework,
                            candidate.TargetFramework)));
        await using var workspace = new InspectionWorkspace();
        using PackageInspectionAssemblyContext realization =
            await workspace.RealizePackageInspectionAsync(
                    selection,
                    options: realizationOptions,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        var outcomes = realization.Assemblies.ToDictionary(
            static outcome => outcome.Selection.Path,
            StringComparer.Ordinal);
        PackageLibraryInspectionTarget[] targets =
        [
            .. candidates.Select(candidate =>
                CreateTarget(
                    candidate,
                    outcomes[candidate.AssetPath])),
        ];
        return await ExecuteLibrariesAsync(
                subject,
                targets,
                limits,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask<InspectionEnvelope<PackageChildrenDocument>>
        ExecuteLibrariesAsync(
            PackageChildrenSubject subject,
            IReadOnlyList<PackageLibraryInspectionTarget> targets,
            PackageChildrenInspectionLimits? limits = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(targets);
        limits ??= PackageChildrenInspectionLimits.Default;
        limits.Validate();
        if (targets.Count > limits.MaxLibraries)
        {
            return Envelope(
                PackageChildrenDocument.UnavailableLibraries(
                    subject,
                    PackageChildrenStatus.Unavailable,
                    $"The selected Package has {targets.Count} Libraries, "
                        + $"which exceeds the inspection limit of "
                        + $"{limits.MaxLibraries}."),
                [
                    new(
                        "package-children.library-limit",
                        InspectionDiagnosticSeverity.Error,
                        "The selected Package Library population exceeds "
                            + "the inspection limit."),
                ]);
        }
        if (targets.Count == 0)
        {
            return Envelope(
                new(
                    subject,
                    PackageChildrenKind.Libraries,
                    PackageChildrenStatus.SelectedEmpty,
                    [],
                    [],
                    detail: null,
                    isComplete: true),
                []);
        }

        var rows = ImmutableArray.CreateBuilder<PackageLibraryChild>(
            targets.Count);
        var diagnostics =
            ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        bool complete = true;
        foreach (PackageLibraryInspectionTarget target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageLibraryChild row;
            if (target.Unavailable is { } unavailable)
            {
                row = new(
                    target.AssetId,
                    target.AssetPath,
                    target.AssemblyName,
                    target.Role,
                    PublicTypeDeclarations: null,
                    unavailable);
                diagnostics.Add(
                    new(
                        "package-children.library-unavailable",
                        InspectionDiagnosticSeverity.Error,
                        unavailable.Detail,
                        target.AssetPath));
            }
            else
            {
                row = await InspectAsync(
                        target,
                        limits,
                        diagnostics,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            rows.Add(row);
            complete &= row.IsAvailable;
        }

        return Envelope(
            new(
                subject,
                PackageChildrenKind.Libraries,
                PackageChildrenStatus.Available,
                rows.ToImmutable(),
                [],
                detail: null,
                complete),
            diagnostics.ToImmutable());
    }

    private static PackageLibraryInspectionTarget CreateTarget(
        PackageLibraryInspectionCandidate candidate,
        PackageInspectionAssemblyOutcome outcome) =>
        outcome switch
        {
            PackageInspectionAssemblyOutcome.Available available =>
                new(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.Role,
                    available.Group,
                    available.Participant),
            PackageInspectionAssemblyOutcome.WithoutAssembly =>
                PackageLibraryInspectionTarget.CreateUnavailable(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.Role,
                    PackageLibraryChildUnavailableReason.NotManagedAssembly,
                    "The selected Package Library is not a managed assembly."),
            PackageInspectionAssemblyOutcome.Unavailable unavailable =>
                PackageLibraryInspectionTarget.CreateUnavailable(
                    candidate.AssetId,
                    candidate.AssetPath,
                    candidate.AssemblyName,
                    candidate.Role,
                    PackageLibraryChildUnavailableReason.AssemblyUnavailable,
                    unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Package inspection assembly outcome."),
        };

    private static async ValueTask<PackageLibraryChild> InspectAsync(
        PackageLibraryInspectionTarget target,
        PackageChildrenInspectionLimits limits,
        ImmutableArray<InspectionDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken)
    {
        var cleanupFailures = new List<string>();
        AssemblyContextGroup group =
            target.Group
            ?? throw new InvalidOperationException(
                "An available Package Library target requires an assembly context group.");
        AssemblyContextParticipant participant =
            target.Participant
            ?? throw new InvalidOperationException(
                "An available Package Library target requires an assembly context participant.");
        AssemblyContextLibraryInspectionRun<
            InspectionEnvelope<LibraryInspectionOutcome>> run =
                await AssemblyContextLibraryInspection.ExecuteAsync(
                        AssemblyContextLibraryAdapter.MaterializeAsync(
                            group,
                            participant,
                            AssemblyContextLibraryRole.ApiOnly,
                            limits.Materialization,
                            cancellationToken),
                        (reference, owner) =>
                            AssemblyContextLibraryInspection.ExecuteOperation(
                                reference,
                                owner,
                                lease =>
                                    LibraryInspectionOperation.Execute(
                                        new(
                                            reference,
                                            new(
                                                new(
                                                    LibraryTypeAccessibility
                                                        .Public,
                                                    new(),
                                                    rows: null,
                                                    LibraryTypeDeclarationSelection
                                                        .DefinitionsAndForwarders),
                                                limits.Extraction)),
                                        lease,
                                        cancellationToken)),
                        cleanupFailures)
                    .ConfigureAwait(false);

        string correspondence = target.AssetPath.ToString();
        foreach (string cleanupFailure in cleanupFailures)
        {
            diagnostics.Add(
                new(
                    "package-children.cleanup",
                    InspectionDiagnosticSeverity.Error,
                    cleanupFailure,
                    correspondence));
        }
        if (cleanupFailures.Count > 0)
        {
            return Unavailable(
                target,
                PackageLibraryChildUnavailableReason.CleanupFailed,
                "The exact Library inspection could not retire all resources.");
        }
        if (run.Result is null)
        {
            string detail =
                run.Failure
                ?? "The exact Library owner did not issue an inspection operation.";
            diagnostics.Add(
                new(
                    "package-children.materialization",
                    InspectionDiagnosticSeverity.Error,
                    detail,
                    correspondence));
            return Unavailable(
                target,
                run.Failure is null
                    ? PackageLibraryChildUnavailableReason
                        .OperationLeaseUnavailable
                    : PackageLibraryChildUnavailableReason
                        .MaterializationFailed,
                detail);
        }

        foreach (InspectionDiagnostic diagnostic in run.Result.Diagnostics)
        {
            diagnostics.Add(
                new(
                    diagnostic.Code,
                    diagnostic.Severity,
                    diagnostic.Summary,
                    new InertString(
                        TextPolicy.Field,
                        correspondence)));
        }

        return run.Result.Content switch
        {
            LibraryInspectionOutcome.Available
            {
                Document.Types.Count: { } count,
            } =>
                FromCount(target, count),
            LibraryInspectionOutcome.Available =>
                Unavailable(
                    target,
                    PackageLibraryChildUnavailableReason.CountUnavailable,
                    "The exact Library inspection returned no declaration Count."),
            LibraryInspectionOutcome.Rejected rejected =>
                Unavailable(
                    target,
                    PackageLibraryChildUnavailableReason.InspectionRejected,
                    $"The exact Library inspection was rejected "
                        + $"({rejected.Reason})."),
            LibraryInspectionOutcome.Failed failed =>
                Unavailable(
                    target,
                    PackageLibraryChildUnavailableReason.InspectionFailed,
                    $"The exact Library inspection failed "
                        + $"({failed.Reason})."),
            _ => throw new InvalidOperationException(
                "Unknown exact Library inspection outcome."),
        };
    }

    private static PackageLibraryChild FromCount(
        PackageLibraryInspectionTarget target,
        LibraryTypePopulationCountOutcome count) =>
        new(
            target.AssetId,
            target.AssetPath,
            target.AssemblyName,
            target.Role,
            count,
            count is LibraryTypePopulationCountOutcome.Counted
                ? null
                : new(
                    PackageLibraryChildUnavailableReason.CountUnavailable,
                    new InertString(
                        TextPolicy.Field,
                        Describe(count))));

    private static PackageLibraryChild Unavailable(
        PackageLibraryInspectionTarget target,
        PackageLibraryChildUnavailableReason reason,
        string detail) =>
        new(
            target.AssetId,
            target.AssetPath,
            target.AssemblyName,
            target.Role,
            PublicTypeDeclarations: null,
            new(
                reason,
                new InertString(TextPolicy.Field, detail)));

    private static string Describe(
        LibraryTypePopulationCountOutcome outcome) =>
        outcome switch
        {
            LibraryTypePopulationCountOutcome.Unavailable unavailable =>
                $"The declaration Count is unavailable "
                    + $"({unavailable.Reason}).",
            LibraryTypePopulationCountOutcome.Incomplete incomplete =>
                $"The declaration Count exceeded {incomplete.Bound} "
                    + $"({incomplete.Measured} > {incomplete.Limit}).",
            LibraryTypePopulationCountOutcome.Counted =>
                "The declaration Count is available.",
            _ => throw new InvalidOperationException(
                "Unknown Library Type declaration Count outcome."),
        };

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
