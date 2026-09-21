using System.Collections.Immutable;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

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
            ExactLibrarySourceCoordinate source,
            PortableLibraryIdentity identity)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (!AssemblyReferenceIdentity.EquivalentComparer.Equals(
                    source.LibraryIdentity.Identity,
                    new AssemblyReferenceIdentity(
                        identity.Name,
                        Version.Parse(identity.Version),
                        identity.Culture,
                        identity.PublicKeyToken)))
            {
                throw new ArgumentException(
                    "The portable Library identity must match the exact source coordinate.",
                    nameof(identity));
            }
        }

        public ExactLibrarySourceCoordinate Source { get; }

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
/// One selected compile asset and the exact portable Library realized from it.
/// </summary>
public sealed record EcosystemDependencySelectedLibrary
{
    public EcosystemDependencySelectedLibrary(
        PackageCompileAsset asset,
        PortableLibraryIdentity identity)
    {
        Asset = asset ?? throw new ArgumentNullException(nameof(asset));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public PackageCompileAsset Asset { get; }

    public PortableLibraryIdentity Identity { get; }
}

/// <summary>
/// Compile selection plus explicit selected-asset-to-Library correspondence.
/// </summary>
public sealed class EcosystemDependencyPackageCompileSelection
{
    public EcosystemDependencyPackageCompileSelection(
        PackageCompileAssetSelectionReceipt receipt,
        IEnumerable<EcosystemDependencySelectedLibrary>? selectedLibraries = null)
    {
        Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt));
        SelectedLibraries = [.. selectedLibraries ?? []];
        Validate();
    }

    public PackageCompileAssetSelectionReceipt Receipt { get; }

    public ImmutableArray<EcosystemDependencySelectedLibrary> SelectedLibraries
        { get; }

    internal bool HasCompleteLibraryCorrespondence =>
        Receipt.Selection.Status switch
        {
            PackageCompileAssetSelectionStatus.Selected =>
                SelectedLibraries.Length == Receipt.Selection.Assets.Count,
            PackageCompileAssetSelectionStatus.NoCompileAssets
                or PackageCompileAssetSelectionStatus.EmptyCompileGroup =>
                SelectedLibraries.IsEmpty,
            _ => false,
        };

    private void Validate()
    {
        if (SelectedLibraries.Any(static library => library is null))
        {
            throw new ArgumentException(
                "Selected compile Libraries cannot contain null entries.",
                nameof(SelectedLibraries));
        }

        PackageCompileAssetSelection selection = Receipt.Selection;
        if (selection.Status is not (
                PackageCompileAssetSelectionStatus.Selected
                or PackageCompileAssetSelectionStatus.NoCompileAssets
                or PackageCompileAssetSelectionStatus.EmptyCompileGroup))
        {
            throw new ArgumentException(
                "A failed compile-asset selection cannot be retained as available recognition input.",
                nameof(Receipt));
        }
        if (selection.Status != PackageCompileAssetSelectionStatus.Selected
            && !SelectedLibraries.IsEmpty)
        {
            throw new ArgumentException(
                "An empty compile selection cannot retain selected Libraries.",
                nameof(SelectedLibraries));
        }

        var assets = new HashSet<PackageCompileAsset>(
            selection.Assets,
            ReferenceEqualityComparer.Instance);
        var selectedAssets = new HashSet<PackageCompileAsset>(
            ReferenceEqualityComparer.Instance);
        foreach (EcosystemDependencySelectedLibrary library
                 in SelectedLibraries)
        {
            if (!assets.Contains(library.Asset))
            {
                throw new ArgumentException(
                    "A selected Library must retain an asset from the compile selection receipt.",
                    nameof(SelectedLibraries));
            }
            if (!selectedAssets.Add(library.Asset))
            {
                throw new ArgumentException(
                    "A compile asset cannot correspond to more than one selected Library.",
                    nameof(SelectedLibraries));
            }
        }
    }
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
            EcosystemDependencyInputComponent<PackageDependencyGroups>
                dependencyGroup,
            EcosystemDependencyInputComponent<
                EcosystemDependencyPackageCompileSelection> compileAssets)
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
            PackageDependencyGroups> DependencyGroup { get; }

        public EcosystemDependencyInputComponent<
            EcosystemDependencyPackageCompileSelection> CompileAssets { get; }
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
                        PackageDependencyGroups>.Available
                && package.CompileAssets is
                    EcosystemDependencyInputComponent<
                        EcosystemDependencyPackageCompileSelection>.Available
                        compile
                && compile.Value.HasCompleteLibraryCorrespondence,
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
                                        PackageDependencyGroups>.Available
                                        dependency
                                || dependency.Value.SelectedGroup is null
                                || !dependency.Value.SelectedGroup.Dependencies
                                    .Any(candidate => ReferenceEquals(
                                        candidate,
                                        packageObservation.Dependency)))
                            {
                                throw new ArgumentException(
                                    "A Package declaration must retain one exact declaration from the selected logical dependency group.",
                                    nameof(Observations));
                            }
                            break;

                        case EcosystemDependencyObservation.AssemblyReference
                            assemblyObservation:
                            if (packageContext.CompileAssets
                                    is not EcosystemDependencyInputComponent<
                                        EcosystemDependencyPackageCompileSelection>.Available
                                        compile
                                || !compile.Value.SelectedLibraries.Any(
                                    library =>
                                        library.Identity
                                        == assemblyObservation.DeclaringLibrary))
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
                    EcosystemDependencyPackageCompileSelection>.Available compile
            && !compile.Value.Receipt.PackageId.Equals(
                subject.Coordinate.PackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The compile-asset selection does not correspond to the semantic subject.",
                nameof(context));
        }
        if (context.Manifest
                is EcosystemDependencyInputComponent<
                    PackageManifestFacts>.Available availableManifest
            && context.DependencyGroup
                is EcosystemDependencyInputComponent<
                    PackageDependencyGroups>.Available dependency)
        {
            ValidateDependencySelection(
                availableManifest.Value,
                dependency.Value);
        }
        if (context.DependencyGroup
                is EcosystemDependencyInputComponent<
                    PackageDependencyGroups>.Available availableDependency
            && context.CompileAssets
                is EcosystemDependencyInputComponent<
                    EcosystemDependencyPackageCompileSelection>.Available assets)
        {
            ValidateTargetFrameworkCorrespondence(
                subject,
                availableDependency.Value,
                assets.Value.Receipt);
        }
    }

    private static void ValidateDependencySelection(
        PackageManifestFacts manifest,
        PackageDependencyGroups selection)
    {
        if (!manifest.DependencyGroups
                .Zip(selection.Groups)
                .All(static pair => GroupsEqual(pair.First, pair.Second))
            || manifest.DependencyGroups.Length != selection.Groups.Length)
        {
            throw new ArgumentException(
                "The dependency-group selection does not belong to the available Package manifest.",
                nameof(selection));
        }

        switch (selection.SelectionStatus)
        {
            case PackageDependencyGroupSelectionStatus.Selected:
                if (selection.SelectedGroup is null
                    || selection.SelectedGroupIndex is not int selectedIndex
                    || selectedIndex < 0
                    || selectedIndex >= selection.Groups.Length
                    || !SelectionTargetCorresponds(
                        selection.SelectedTargetFramework,
                        selection.SelectedGroup)
                    || !SelectedGroupCorresponds(selection, selectedIndex))
                {
                    throw new ArgumentException(
                        "The selected logical dependency group does not correspond to the Package manifest.",
                        nameof(selection));
                }
                break;

            case PackageDependencyGroupSelectionStatus.NoDependencyGroups:
                if (!selection.Groups.IsEmpty
                    || selection.SelectedGroup is not null
                    || selection.SelectedGroupIndex is not null
                    || selection.SelectedTargetFramework is not null)
                {
                    throw new ArgumentException(
                        "A no-dependency-groups selection cannot retain a selected logical group.",
                        nameof(selection));
                }
                break;

            case PackageDependencyGroupSelectionStatus.NoMatchingTargetFramework:
                throw new ArgumentException(
                    "A failed dependency-group selection cannot be retained as available recognition input.",
                    nameof(selection));

            default:
                throw new InvalidOperationException(
                    "Unknown dependency-group selection status.");
        }
    }

    private static bool SelectedGroupCorresponds(
        PackageDependencyGroups selection,
        int selectedIndex)
    {
        DeclaredPackageDependencyGroup selected = selection.SelectedGroup!;
        if (GroupsEqual(selected, selection.Groups[selectedIndex]))
            return true;

        if (!selected.IsImplicitManifestGroup)
            return false;

        if (!selection.Groups[selectedIndex].IsImplicitManifestGroup
            || selection.Groups.Take(selectedIndex).Any(
                static group => group.IsImplicitManifestGroup)
            || !IsUniversalGroup(selected))
        {
            return false;
        }

        return selected.Dependencies.SequenceEqual(
            selection.Groups
                .Where(static group => group.IsImplicitManifestGroup)
                .SelectMany(static group => group.Dependencies));
    }

    private static bool SelectionTargetCorresponds(
        string? selectedTargetFramework,
        DeclaredPackageDependencyGroup selectedGroup)
    {
        if (IsUniversalGroup(selectedGroup))
        {
            return selectedTargetFramework is not null
                && (string.IsNullOrWhiteSpace(selectedTargetFramework)
                || selectedTargetFramework.Equals(
                    "any",
                    StringComparison.OrdinalIgnoreCase));
        }
        if (string.IsNullOrWhiteSpace(selectedTargetFramework))
            return false;

        return TfmSelector.NormalizeTfm(selectedTargetFramework).Equals(
            TfmSelector.NormalizeTfm(selectedGroup.TargetFramework),
            StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateTargetFrameworkCorrespondence(
        EcosystemDependencySubject.Package subject,
        PackageDependencyGroups dependencies,
        PackageCompileAssetSelectionReceipt compile)
    {
        string? dependencyBasis = dependencies.RequestedTargetFramework
            ?? (dependencies.SelectedGroup is { } selectedGroup
                && !IsUniversalGroup(selectedGroup)
                    ? dependencies.SelectedTargetFramework
                    : null);
        string? compileBasis = compile.RequestedTargetFramework
            ?? compile.Selection.TargetFramework;
        string? canonical = null;
        foreach (string target in new[]
                 {
                     subject.Coordinate.Framework,
                     dependencyBasis,
                     compileBasis,
                 }.Where(static target => !string.IsNullOrWhiteSpace(target))!)
        {
            string current = TfmSelector.NormalizeTfm(target);
            if (canonical is null)
            {
                canonical = current;
            }
            else if (!canonical.Equals(
                         current,
                         StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "Dependency-group and compile-asset selection must describe one effective target-framework request.");
            }
        }

    }

    private static bool IsUniversalGroup(
        DeclaredPackageDependencyGroup group) =>
        string.IsNullOrWhiteSpace(group.TargetFramework)
        || group.TargetFramework.Equals(
            "any",
            StringComparison.OrdinalIgnoreCase);

    private static bool GroupsEqual(
        DeclaredPackageDependencyGroup left,
        DeclaredPackageDependencyGroup right) =>
        left.TargetFramework.Equals(
            right.TargetFramework,
            StringComparison.OrdinalIgnoreCase)
        && left.IsImplicitManifestGroup == right.IsImplicitManifestGroup
        && left.Dependencies.SequenceEqual(right.Dependencies);

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
/// Portable projection bound to the semantic subject of one recognition plan.
/// </summary>
public sealed record EcosystemDependencyRecognitionPortableProjection
{
    public EcosystemDependencyRecognitionPortableProjection(
        EcosystemDependencySubject subject,
        InspectionPortableProjection portableProjection)
    {
        Subject = subject ?? throw new ArgumentNullException(nameof(subject));
        PortableProjection = portableProjection
            ?? throw new ArgumentNullException(nameof(portableProjection));
    }

    public EcosystemDependencySubject Subject { get; }

    public InspectionPortableProjection PortableProjection { get; }
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
            EcosystemDependencyRecognitionPortableProjection
                portableProjection,
            IEnumerable<InspectionDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(portableProjection);
        if (portableProjection.Subject != batch.Subject)
        {
            throw new ArgumentException(
                "The portable projection does not correspond to the recognition subject.",
                nameof(portableProjection));
        }

        return new(
            InspectionContentKind.Outcome,
            Recognize(profile, batch),
            portableProjection.PortableProjection,
            diagnostics);
    }
}
