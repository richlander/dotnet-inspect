using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;

namespace DotnetInspector.Ecosystems;

/// <summary>The semantic subject whose direct dependencies are recognized.</summary>
public abstract record EcosystemDependencySubject
{
    private protected EcosystemDependencySubject()
    {
    }

    public sealed record Package : EcosystemDependencySubject
    {
        public Package(RealizedMemberCoordinate.Package coordinate) =>
            Coordinate = coordinate
                ?? throw new ArgumentNullException(nameof(coordinate));

        public RealizedMemberCoordinate.Package Coordinate { get; }
    }

    public sealed record Library : EcosystemDependencySubject
    {
        public Library(
            RealizedMemberCoordinate source,
            PortableLibraryIdentity identity)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        }

        public RealizedMemberCoordinate Source { get; }

        public PortableLibraryIdentity Identity { get; }
    }
}

/// <summary>Document-local identity for one recognition input issue.</summary>
public readonly record struct EcosystemDependencyInputIssueIdentity
{
    public EcosystemDependencyInputIssueIdentity(int value)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
        Value = value;
    }

    public int Value { get; }
}

/// <summary>The recognition input role that could not be completed.</summary>
public enum EcosystemDependencyInputRole
{
    EffectiveTargetFrameworkSelection,
    PackageManifestProjection,
    SelectedCompileLibraryEnumeration,
    AssemblyReferenceProjection,
}

/// <summary>A known declaring source associated with one input issue.</summary>
public abstract record EcosystemDependencyInputIssueSource
{
    private protected EcosystemDependencyInputIssueSource()
    {
    }

    public sealed record Package : EcosystemDependencyInputIssueSource
    {
        public Package(RealizedMemberCoordinate.Package coordinate) =>
            Coordinate = coordinate
                ?? throw new ArgumentNullException(nameof(coordinate));

        public RealizedMemberCoordinate.Package Coordinate { get; }
    }

    public sealed record Library : EcosystemDependencyInputIssueSource
    {
        public Library(PortableLibraryIdentity identity) =>
            Identity = identity
                ?? throw new ArgumentNullException(nameof(identity));

        public PortableLibraryIdentity Identity { get; }
    }
}

/// <summary>One visible failure or gap in the recognition input.</summary>
public sealed record EcosystemDependencyInputIssue
{
    public EcosystemDependencyInputIssue(
        EcosystemDependencyInputIssueIdentity identity,
        EcosystemDependencyInputRole role,
        InspectionDiagnostic diagnostic,
        EcosystemDependencyInputIssueSource? source = null)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role), role, null);
        Identity = identity;
        Role = role;
        Diagnostic = diagnostic
            ?? throw new ArgumentNullException(nameof(diagnostic));
        Source = source;
    }

    public EcosystemDependencyInputIssueIdentity Identity { get; }

    public EcosystemDependencyInputRole Role { get; }

    public InspectionDiagnostic Diagnostic { get; }

    public EcosystemDependencyInputIssueSource? Source { get; }
}

/// <summary>
/// Availability of one required owner-issued recognition input component.
/// </summary>
public abstract record EcosystemDependencyInputComponent<T>
    where T : class
{
    private protected EcosystemDependencyInputComponent()
    {
    }

    public sealed record Available :
        EcosystemDependencyInputComponent<T>
    {
        public Available(T value)
            => Value = value
                ?? throw new ArgumentNullException(nameof(value));

        public T Value { get; }
    }

    public sealed record Unavailable(
        EcosystemDependencyInputIssueIdentity Issue) :
        EcosystemDependencyInputComponent<T>;

    public sealed record NotAttempted(
        EcosystemDependencyInputIssueIdentity Issue) :
        EcosystemDependencyInputComponent<T>;
}

/// <summary>Availability of direct assembly-reference projection.</summary>
public abstract record EcosystemDependencyReferenceInput
{
    private protected EcosystemDependencyReferenceInput()
    {
    }

    public sealed record Available : EcosystemDependencyReferenceInput;

    public sealed record Unavailable(
        EcosystemDependencyInputIssueIdentity Issue) :
        EcosystemDependencyReferenceInput;
}

/// <summary>
/// Owner-issued target-framework selection retained for Package recognition.
/// </summary>
public sealed record EcosystemDependencyGroupSelection
{
    public EcosystemDependencyGroupSelection(
        string? requestedTargetFramework,
        PackageDependencyGroupSelectionStatus status,
        string? selectedTargetFramework,
        int? selectedGroupIndex)
    {
        if (requestedTargetFramework is not null
            && string.IsNullOrWhiteSpace(requestedTargetFramework))
        {
            throw new ArgumentException(
                "A requested target framework cannot be blank.",
                nameof(requestedTargetFramework));
        }
        if (selectedTargetFramework is not null
            && string.IsNullOrWhiteSpace(selectedTargetFramework))
        {
            throw new ArgumentException(
                "A selected target framework cannot be blank.",
                nameof(selectedTargetFramework));
        }
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, null);
        if (status == PackageDependencyGroupSelectionStatus.Selected
            && (string.IsNullOrWhiteSpace(selectedTargetFramework)
                || selectedGroupIndex is null or < 0))
        {
            throw new ArgumentException(
                "A selected dependency group requires a selected target framework and nonnegative group index.");
        }
        if (status != PackageDependencyGroupSelectionStatus.Selected
            && (selectedTargetFramework is not null
                || selectedGroupIndex is not null))
        {
            throw new ArgumentException(
                "A non-selected dependency-group outcome cannot retain selected group identity.");
        }

        RequestedTargetFramework = requestedTargetFramework;
        Status = status;
        SelectedTargetFramework = selectedTargetFramework;
        SelectedGroupIndex = selectedGroupIndex;
    }

    public string? RequestedTargetFramework { get; }

    public PackageDependencyGroupSelectionStatus Status { get; }

    public string? SelectedTargetFramework { get; }

    public int? SelectedGroupIndex { get; }
}

/// <summary>Typed recognition inputs retained beside the semantic subject.</summary>
public abstract record EcosystemDependencyInputContext
{
    private protected EcosystemDependencyInputContext()
    {
    }

    public sealed record Package : EcosystemDependencyInputContext
    {
        public Package(
            EcosystemDependencyInputComponent<PackageManifestFacts> manifest,
            EcosystemDependencyInputComponent<EcosystemDependencyGroupSelection>
                dependencyGroup,
            EcosystemDependencyInputComponent<
                PackageCompileAssetSelectionReceipt> compileAssets)
        {
            Manifest = manifest
                ?? throw new ArgumentNullException(nameof(manifest));
            DependencyGroup = dependencyGroup
                ?? throw new ArgumentNullException(nameof(dependencyGroup));
            CompileAssets = compileAssets
                ?? throw new ArgumentNullException(nameof(compileAssets));
        }

        public EcosystemDependencyInputComponent<PackageManifestFacts> Manifest
            { get; }

        public EcosystemDependencyInputComponent<
            EcosystemDependencyGroupSelection> DependencyGroup { get; }

        public EcosystemDependencyInputComponent<
            PackageCompileAssetSelectionReceipt> CompileAssets { get; }
    }

    public sealed record Library : EcosystemDependencyInputContext
    {
        public Library(EcosystemDependencyReferenceInput directReferences) =>
            DirectReferences = directReferences
                ?? throw new ArgumentNullException(nameof(directReferences));

        public EcosystemDependencyReferenceInput DirectReferences { get; }
    }
}

/// <summary>Coverage represented by one recognition Document.</summary>
public enum EcosystemDependencyRecognitionCoverage
{
    Complete,
    Incomplete,
}

/// <summary>
/// One Package or Library observation batch before product recognition.
/// </summary>
public abstract record EcosystemDependencyObservationBatch
{
    private protected EcosystemDependencyObservationBatch(
        EcosystemDependencySubject subject,
        EcosystemDependencyInputContext context,
        IEnumerable<EcosystemDependencyObservation>? observations,
        IEnumerable<EcosystemDependencyInputIssue>? issues)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Observations = [.. observations ?? []];
        Issues = [.. issues ?? []];
        ValidateCommon();
    }

    public EcosystemDependencySubject Subject { get; }

    public EcosystemDependencyInputContext Context { get; }

    public ImmutableArray<EcosystemDependencyObservation> Observations
        { get; }

    public ImmutableArray<EcosystemDependencyInputIssue> Issues { get; }

    internal abstract EcosystemDependencyBatchState State { get; }

    public sealed record Available : EcosystemDependencyObservationBatch
    {
        public Available(
            EcosystemDependencySubject subject,
            EcosystemDependencyInputContext context,
            IEnumerable<EcosystemDependencyObservation>? observations = null)
            : base(subject, context, observations, issues: null)
        {
            ValidateAvailable();
        }

        internal override EcosystemDependencyBatchState State =>
            EcosystemDependencyBatchState.Available;
    }

    public sealed record Incomplete : EcosystemDependencyObservationBatch
    {
        public Incomplete(
            EcosystemDependencySubject subject,
            EcosystemDependencyInputContext context,
            IEnumerable<EcosystemDependencyObservation> observations,
            IEnumerable<EcosystemDependencyInputIssue> issues)
            : base(subject, context, observations, issues)
        {
            if (Issues.IsEmpty)
            {
                throw new ArgumentException(
                    "An incomplete recognition batch requires at least one input issue.",
                    nameof(issues));
            }
        }

        internal override EcosystemDependencyBatchState State =>
            EcosystemDependencyBatchState.Incomplete;
    }

    public sealed record Unavailable : EcosystemDependencyObservationBatch
    {
        public Unavailable(
            EcosystemDependencySubject subject,
            EcosystemDependencyInputContext context,
            IEnumerable<EcosystemDependencyInputIssue> issues)
            : base(subject, context, observations: null, issues)
        {
            if (Issues.IsEmpty)
            {
                throw new ArgumentException(
                    "An unavailable recognition batch requires at least one input issue.",
                    nameof(issues));
            }
        }

        internal override EcosystemDependencyBatchState State =>
            EcosystemDependencyBatchState.Unavailable;
    }

    private void ValidateCommon()
    {
        if (Subject is EcosystemDependencySubject.Package
            && Context is not EcosystemDependencyInputContext.Package
            || Subject is EcosystemDependencySubject.Library
                && Context is not EcosystemDependencyInputContext.Library)
        {
            throw new ArgumentException(
                "The recognition subject and input context kinds must match.");
        }
        if (Observations.Any(static observation => observation is null))
        {
            throw new ArgumentException(
                "A recognition batch cannot contain null observations.",
                nameof(Observations));
        }
        if (Issues.Any(static issue => issue is null))
        {
            throw new ArgumentException(
                "A recognition batch cannot contain null input issues.",
                nameof(Issues));
        }

        var issuesById =
            new Dictionary<
                EcosystemDependencyInputIssueIdentity,
                EcosystemDependencyInputIssue>();
        foreach (EcosystemDependencyInputIssue issue in Issues)
        {
            if (!issuesById.TryAdd(issue.Identity, issue))
            {
                throw new ArgumentException(
                    $"Input issue identity '{issue.Identity.Value}' occurs more than once.",
                    nameof(Issues));
            }
        }

        ValidateContextIssueReferences(Context, issuesById);
        ValidateSubjectCorrespondence();
    }

    private void ValidateAvailable()
    {
        if (!Issues.IsEmpty)
            throw new InvalidOperationException("An available batch cannot contain issues.");

        bool complete = Context switch
        {
            EcosystemDependencyInputContext.Package package =>
                package.Manifest is
                    EcosystemDependencyInputComponent<
                        PackageManifestFacts>.Available
                && package.DependencyGroup is
                    EcosystemDependencyInputComponent<
                        EcosystemDependencyGroupSelection>.Available
                && package.CompileAssets is
                    EcosystemDependencyInputComponent<
                        PackageCompileAssetSelectionReceipt>.Available,
            EcosystemDependencyInputContext.Library library =>
                library.DirectReferences is
                    EcosystemDependencyReferenceInput.Available,
            _ => false,
        };
        if (!complete)
        {
            throw new ArgumentException(
                "An available recognition batch requires every input component.");
        }
    }

    private void ValidateSubjectCorrespondence()
    {
        switch (Subject, Context)
        {
            case (
                EcosystemDependencySubject.Package packageSubject,
                EcosystemDependencyInputContext.Package packageContext):
                ValidatePackageContext(packageSubject, packageContext);
                foreach (EcosystemDependencyObservation observation in
                         Observations)
                {
                    switch (observation)
                    {
                        case EcosystemDependencyObservation.PackageDeclaration
                            packageObservation:
                            if (packageObservation.DeclaringPackage
                                != packageSubject.Coordinate)
                            {
                                throw new ArgumentException(
                                    "A Package observation must retain the exact Package subject.",
                                    nameof(Observations));
                            }
                            if (packageContext.Manifest
                                    is not EcosystemDependencyInputComponent<
                                        PackageManifestFacts>.Available
                                || packageContext.DependencyGroup
                                    is not EcosystemDependencyInputComponent<
                                        EcosystemDependencyGroupSelection>.Available)
                            {
                                throw new ArgumentException(
                                    "A Package declaration requires available manifest and dependency-group input.",
                                    nameof(Observations));
                            }
                            break;

                        case EcosystemDependencyObservation.AssemblyReference
                            assemblyObservation:
                            if (packageContext.CompileAssets
                                    is not EcosystemDependencyInputComponent<
                                        PackageCompileAssetSelectionReceipt>.Available
                                        compile
                                || !compile.Value.Selection.Assets.Any(asset =>
                                    asset.AssemblyName.Equals(
                                        assemblyObservation.DeclaringLibrary.Name,
                                        StringComparison.OrdinalIgnoreCase)))
                            {
                                throw new ArgumentException(
                                    "A Package assembly-reference observation must join to one selected compile Library.",
                                    nameof(Observations));
                            }
                            break;
                    }
                }
                break;

            case (
                EcosystemDependencySubject.Library librarySubject,
                EcosystemDependencyInputContext.Library):
                foreach (EcosystemDependencyObservation observation in
                         Observations)
                {
                    if (observation
                            is not EcosystemDependencyObservation.AssemblyReference
                                assemblyObservation
                        || assemblyObservation.DeclaringLibrary
                            != librarySubject.Identity)
                    {
                        throw new ArgumentException(
                            "A Library recognition batch can contain only direct references declared by its exact Library subject.",
                            nameof(Observations));
                    }
                }
                break;
        }
    }

    private static void ValidatePackageContext(
        EcosystemDependencySubject.Package subject,
        EcosystemDependencyInputContext.Package context)
    {
        if (context.Manifest
                is EcosystemDependencyInputComponent<
                    PackageManifestFacts>.Available manifest
            && (!manifest.Value.Coordinate.PackageId.Equals(
                    subject.Coordinate.PackageId,
                    StringComparison.OrdinalIgnoreCase)
                || !manifest.Value.Coordinate.Version.Equals(
                    subject.Coordinate.Version,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException(
                "The Package manifest does not correspond to the semantic subject.",
                nameof(context));
        }
        if (context.CompileAssets
                is EcosystemDependencyInputComponent<
                    PackageCompileAssetSelectionReceipt>.Available compile
            && !compile.Value.PackageId.Equals(
                subject.Coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The compile-asset selection does not correspond to the semantic subject.",
                nameof(context));
        }
        if (context.DependencyGroup
                is EcosystemDependencyInputComponent<
                    EcosystemDependencyGroupSelection>.Available dependency
            && context.CompileAssets
                is EcosystemDependencyInputComponent<
                    PackageCompileAssetSelectionReceipt>.Available assets
            && dependency.Value.SelectedTargetFramework is { } dependencyTarget
            && assets.Value.Selection.TargetFramework is { } compileTarget
            && !dependencyTarget.Equals(
                compileTarget,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Dependency-group and compile-asset selection must describe one target-framework slice.",
                nameof(context));
        }
    }

    private static void ValidateContextIssueReferences(
        EcosystemDependencyInputContext context,
        IReadOnlyDictionary<
            EcosystemDependencyInputIssueIdentity,
            EcosystemDependencyInputIssue> issues)
    {
        switch (context)
        {
            case EcosystemDependencyInputContext.Package package:
                ValidateComponentIssue(
                    package.Manifest,
                    EcosystemDependencyInputRole.PackageManifestProjection,
                    issues);
                ValidateComponentIssue(
                    package.DependencyGroup,
                    EcosystemDependencyInputRole
                        .EffectiveTargetFrameworkSelection,
                    issues);
                ValidateComponentIssue(
                    package.CompileAssets,
                    EcosystemDependencyInputRole
                        .SelectedCompileLibraryEnumeration,
                    issues);
                break;

            case EcosystemDependencyInputContext.Library
            {
                DirectReferences:
                    EcosystemDependencyReferenceInput.Unavailable unavailable,
            }:
                RequireIssue(
                    unavailable.Issue,
                    EcosystemDependencyInputRole.AssemblyReferenceProjection,
                    requireRoleMatch: true,
                    issues);
                break;
        }
    }

    private static void ValidateComponentIssue<T>(
        EcosystemDependencyInputComponent<T> component,
        EcosystemDependencyInputRole unavailableRole,
        IReadOnlyDictionary<
            EcosystemDependencyInputIssueIdentity,
            EcosystemDependencyInputIssue> issues)
        where T : class
    {
        switch (component)
        {
            case EcosystemDependencyInputComponent<T>.Unavailable unavailable:
                RequireIssue(
                    unavailable.Issue,
                    unavailableRole,
                    requireRoleMatch: true,
                    issues);
                break;
            case EcosystemDependencyInputComponent<T>.NotAttempted notAttempted:
                RequireIssue(
                    notAttempted.Issue,
                    unavailableRole,
                    requireRoleMatch: false,
                    issues);
                break;
        }
    }

    private static void RequireIssue(
        EcosystemDependencyInputIssueIdentity reference,
        EcosystemDependencyInputRole expectedRole,
        bool requireRoleMatch,
        IReadOnlyDictionary<
            EcosystemDependencyInputIssueIdentity,
            EcosystemDependencyInputIssue> issues)
    {
        if (!issues.TryGetValue(
                reference,
                out EcosystemDependencyInputIssue? issue))
        {
            throw new ArgumentException(
                $"Input component references missing issue '{reference.Value}'.");
        }
        if (requireRoleMatch && issue.Role != expectedRole)
        {
            throw new ArgumentException(
                $"Input component issue '{reference.Value}' has role"
                + $" '{issue.Role}' instead of '{expectedRole}'.");
        }
    }
}

internal enum EcosystemDependencyBatchState
{
    Available,
    Incomplete,
    Unavailable,
}

/// <summary>
/// Subject-bound recognition content for Package and Library inspection.
/// </summary>
public sealed record EcosystemDependencyRecognitionDocument
{
    internal EcosystemDependencyRecognitionDocument(
        EcosystemDependencySubject subject,
        EcosystemDependencyInputContext inputContext,
        EcosystemDependencyClassification classification,
        EcosystemDependencyRecognitionCoverage coverage,
        ImmutableArray<EcosystemDependencyInputIssue> inputIssues)
    {
        Subject = subject;
        InputContext = inputContext;
        Classification = classification;
        Coverage = coverage;
        InputIssues = inputIssues;
    }

    public EcosystemDependencySubject Subject { get; }

    public EcosystemDependencyInputContext InputContext { get; }

    public EcosystemDependencyClassification Classification { get; }

    public EcosystemDependencyRecognitionCoverage Coverage { get; }

    public ImmutableArray<EcosystemDependencyInputIssue> InputIssues { get; }

    public bool Equals(EcosystemDependencyRecognitionDocument? other) =>
        ReferenceEquals(this, other)
        || other is not null
            && Subject == other.Subject
            && InputContext == other.InputContext
            && Classification == other.Classification
            && Coverage == other.Coverage
            && InputIssues.SequenceEqual(other.InputIssues);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Subject);
        hash.Add(InputContext);
        hash.Add(Classification);
        hash.Add(Coverage);
        foreach (EcosystemDependencyInputIssue issue in InputIssues)
            hash.Add(issue);
        return hash.ToHashCode();
    }
}

/// <summary>The completed Package or Library recognition outcome.</summary>
public abstract record EcosystemDependencyRecognitionOutcome
{
    private protected EcosystemDependencyRecognitionOutcome()
    {
    }

    public sealed record Complete : EcosystemDependencyRecognitionOutcome
    {
        internal Complete(EcosystemDependencyRecognitionDocument document) =>
            Document = document
                ?? throw new ArgumentNullException(nameof(document));

        public EcosystemDependencyRecognitionDocument Document { get; }
    }

    public sealed record Incomplete : EcosystemDependencyRecognitionOutcome
    {
        internal Incomplete(EcosystemDependencyRecognitionDocument document) =>
            Document = document
                ?? throw new ArgumentNullException(nameof(document));

        public EcosystemDependencyRecognitionDocument Document { get; }
    }

    public sealed record Unavailable : EcosystemDependencyRecognitionOutcome
    {
        internal Unavailable(
            EcosystemDependencySubject subject,
            EcosystemDependencyInputContext inputContext,
            ImmutableArray<EcosystemDependencyInputIssue> inputIssues)
        {
            Subject = subject;
            InputContext = inputContext;
            InputIssues = inputIssues;
        }

        public EcosystemDependencySubject Subject { get; }

        public EcosystemDependencyInputContext InputContext { get; }

        public ImmutableArray<EcosystemDependencyInputIssue> InputIssues
            { get; }

        public bool Equals(Unavailable? other) =>
            ReferenceEquals(this, other)
            || other is not null
                && Subject == other.Subject
                && InputContext == other.InputContext
                && InputIssues.SequenceEqual(other.InputIssues);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Subject);
            hash.Add(InputContext);
            foreach (EcosystemDependencyInputIssue issue in InputIssues)
                hash.Add(issue);
            return hash.ToHashCode();
        }
    }
}

/// <summary>
/// Completes Package or Library recognition over one already-issued input
/// batch.
/// </summary>
public static class EcosystemDependencyRecognizer
{
    public static EcosystemDependencyRecognitionOutcome Recognize(
        EcosystemDependencyRecognitionProfile profile,
        EcosystemDependencyObservationBatch batch)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.State == EcosystemDependencyBatchState.Unavailable)
        {
            return new EcosystemDependencyRecognitionOutcome.Unavailable(
                batch.Subject,
                batch.Context,
                batch.Issues);
        }

        EcosystemDependencyClassification classification =
            EcosystemDependencyClassifier.Classify(
                profile,
                batch.Observations);
        EcosystemDependencyRecognitionCoverage coverage =
            batch.State == EcosystemDependencyBatchState.Available
                ? EcosystemDependencyRecognitionCoverage.Complete
                : EcosystemDependencyRecognitionCoverage.Incomplete;
        var document = new EcosystemDependencyRecognitionDocument(
            batch.Subject,
            batch.Context,
            classification,
            coverage,
            batch.Issues);
        return coverage == EcosystemDependencyRecognitionCoverage.Complete
            ? new EcosystemDependencyRecognitionOutcome.Complete(document)
            : new EcosystemDependencyRecognitionOutcome.Incomplete(document);
    }

    public static InspectionEnvelope<EcosystemDependencyRecognitionOutcome>
        Recognize(
            EcosystemDependencyRecognitionProfile profile,
            EcosystemDependencyObservationBatch batch,
            InspectionShare share,
            IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            Recognize(profile, batch),
            share,
            diagnostics);
}
