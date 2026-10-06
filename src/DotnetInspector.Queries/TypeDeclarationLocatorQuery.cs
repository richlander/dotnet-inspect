using System.Collections.Immutable;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Locates declarations without source acquisition, fallback searches, or candidate selection.
/// </summary>
public static class TypeDeclarationLocatorQuery
{
    public static InspectionQuery<TypeDeclarationLocatorResult> Definition { get; } =
        new("Type declaration locator", InspectionCost.Unbounded);

    /// <summary>Evaluates every request against the exact selected population.</summary>
    /// <param name="population">Workspace-issued live input with a detached, immutable receipt.</param>
    /// <param name="requests">Nonempty requests in the consumer's intended answer order.</param>
    /// <param name="includeAll">Whether to include Metadata's non-public declarations.</param>
    /// <param name="maxInventoryReads">
    /// Optional bound on attempted whole-image inventory reads, in population order.
    /// It does not limit rows, requests, or the Metadata work within one image.
    /// </param>
    /// <param name="cancellationToken">Caller cancellation, never a successful empty result.</param>
    public static TypeDeclarationLocatorResult Execute(
        WorkspaceDeclarationPopulation population,
        ImmutableArray<TypeDeclarationLocatorRequest> requests,
        bool includeAll = false,
        int? maxInventoryReads = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(population);
        return Execute(population, requests, includeAll, maxInventoryReads,
            population.ReadDeclarations, cancellationToken);
    }

    internal static TypeDeclarationLocatorResult.Rejected? ValidateRequests(
        ImmutableArray<TypeDeclarationLocatorRequest> requests,
        int? maxInventoryReads,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requests.IsDefaultOrEmpty)
            return new TypeDeclarationLocatorResult.Rejected(TypeDeclarationLocatorRejectionKind.EmptyRequests);
        for (int index = 0; index < requests.Length; index++)
        {
            bool valid = requests[index] switch
            {
                TypeDeclarationLocatorRequest.Exact exact => exact.Name is not null,
                TypeDeclarationLocatorRequest.Pattern pattern => !string.IsNullOrWhiteSpace(pattern.Text),
                TypeDeclarationLocatorRequest.Namespace @namespace =>
                    !string.IsNullOrWhiteSpace(@namespace.Name)
                    && Enum.IsDefined(@namespace.Match)
                    && (@namespace.Match
                            != MetadataNamespaceMatch.Suffix
                        || (@namespace.Name.Length > 1
                            && @namespace.Name[0] == '.')),
                _ => false,
            };
            if (!valid)
            {
                return new TypeDeclarationLocatorResult.Rejected(
                    TypeDeclarationLocatorRejectionKind.InvalidRequest, index);
            }
        }
        if (maxInventoryReads < 0)
        {
            return new TypeDeclarationLocatorResult.Rejected(
                TypeDeclarationLocatorRejectionKind.InvalidInventoryReadLimit);
        }
        return null;
    }

    internal static TypeDeclarationLocatorResult Execute(
        WorkspaceDeclarationPopulation population,
        ImmutableArray<TypeDeclarationLocatorRequest> requests,
        bool includeAll,
        int? maxInventoryReads,
        Func<WorkspaceDeclarationOccurrence, CancellationToken, WorkspaceDeclarationInventoryOutcome> readInventory,
        CancellationToken cancellationToken)
    {
        if (ValidateRequests(requests, maxInventoryReads, cancellationToken) is { } rejectedRequest)
            return rejectedRequest;
        if (population.Availability() is { } unavailable)
        {
            return new TypeDeclarationLocatorResult.Rejected(
                TypeDeclarationLocatorRejectionKind.PopulationUnavailable,
                populationFailure: unavailable);
        }

        var candidates = requests.Select(static _ => new List<TypeDeclarationLocatorCandidate>()).ToArray();
        var outcomes = ImmutableArray.CreateBuilder<TypeDeclarationLocatorMemberOutcome>(
            population.Receipt.Members.Length);
        int reads = 0;
        foreach (WorkspaceDeclarationMember member in population.Receipt.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member.Coordinate is not { } coordinate)
            {
                outcomes.Add(new TypeDeclarationLocatorMemberOutcome.CoordinateUnavailable(member));
                continue;
            }
            if (maxInventoryReads is { } limit && reads >= limit)
            {
                outcomes.Add(new TypeDeclarationLocatorMemberOutcome.NotEvaluated(member));
                continue;
            }

            reads++;
            WorkspaceDeclarationInventoryOutcome access =
                readInventory(member.Occurrence, cancellationToken);
            switch (access)
            {
                case WorkspaceDeclarationInventoryOutcome.Inspected
                    inspected
                    when inspected.Outcome
                        is AssemblyTypeDeclarationInventoryOutcome.Read read:
                    Evaluate(
                        read.Inventory,
                        inspected.ModuleVersionId);
                    break;
                case WorkspaceDeclarationInventoryOutcome.Inspected
                    inspected
                    when inspected.Outcome
                        is AssemblyTypeDeclarationInventoryOutcome.Rejected
                            rejected:
                    outcomes.Add(
                        new TypeDeclarationLocatorMemberOutcome
                            .InventoryRejected(
                                member,
                                rejected.Failure));
                    break;
                case WorkspaceDeclarationInventoryOutcome.LibraryInspected
                    inspected
                    when inspected.Outcome
                        is LibraryTypeDeclarationInventoryInspectionOutcome
                            .Completed completed:
                    Evaluate(
                        completed.Correspondence.Inventory,
                        completed.Correspondence.ModuleVersionId);
                    break;
                case WorkspaceDeclarationInventoryOutcome.LibraryInspected
                    inspected:
                    outcomes.Add(
                        new TypeDeclarationLocatorMemberOutcome
                            .InventoryRejected(
                                member,
                                LibraryFailure(inspected.Outcome)));
                    break;
                case WorkspaceDeclarationInventoryOutcome
                    .InventoryRejected rejected:
                    outcomes.Add(
                        new TypeDeclarationLocatorMemberOutcome
                            .InventoryRejected(
                                member,
                                rejected.Failure));
                    break;
                case WorkspaceDeclarationInventoryOutcome.AcquisitionRejected rejected:
                    outcomes.Add(new TypeDeclarationLocatorMemberOutcome.AccessRejected(member, rejected.Failure));
                    break;
                case WorkspaceDeclarationInventoryOutcome.Unavailable rejected:
                    outcomes.Add(new TypeDeclarationLocatorMemberOutcome.Unavailable(member, rejected.Failure));
                    break;
                case WorkspaceDeclarationInventoryOutcome.NotEvaluated stopped:
                    outcomes.Add(new TypeDeclarationLocatorMemberOutcome.NotEvaluated(member, stopped.Bound));
                    break;
                default:
                    throw new InspectionQueryException("Unknown declaration inventory outcome.");
            }

            void Evaluate(
                AssemblyTypeDeclarationInventory inventory,
                Guid moduleVersionId)
            {
                    var unsupported = ImmutableArray.CreateBuilder<AssemblyTypeDeclaration>();
                    int declarationOrder = 0;
                    foreach (AssemblyTypeDeclaration declaration
                        in inventory.GetDeclarations(includeAll))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int currentDeclarationOrder = declarationOrder++;
                        if (declaration.Kind is not (AssemblyTypeDeclarationKind.Definition
                            or AssemblyTypeDeclarationKind.Forwarder))
                        {
                            unsupported.Add(declaration);
                            continue;
                        }
                        for (int index = 0; index < requests.Length; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            bool matches = requests[index] switch
                            {
                                TypeDeclarationLocatorRequest.Exact exact => exact.Name.Equals(declaration.Name),
                                TypeDeclarationLocatorRequest.Pattern pattern =>
                                    TypeMatcher.MatchesTypeFilter(declaration.Name, pattern.Text),
                                TypeDeclarationLocatorRequest.Namespace
                                    @namespace =>
                                    declaration.Name.IsInNamespace(
                                        @namespace.Name,
                                        @namespace.Match),
                                _ => throw new InspectionQueryException("Unknown admitted locator request."),
                            };
                            if (matches)
                            {
                                candidates[index].Add(
                                    new(
                                        coordinate,
                                        declaration,
                                        moduleVersionId,
                                        currentDeclarationOrder,
                                        member));
                            }
                        }
                    }
                    outcomes.Add(new TypeDeclarationLocatorMemberOutcome.Searched(member, unsupported.ToImmutable()));
            }
        }

        ImmutableArray<TypeDeclarationLocatorMemberOutcome> members = outcomes.MoveToImmutable();
        bool evaluationComplete = members.All(static member => member.IsComplete);
        var answers = ImmutableArray.CreateBuilder<TypeDeclarationLocatorAnswer>(requests.Length);
        for (int index = 0; index < requests.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            candidates[index].Sort(CompareCandidates);
            answers.Add(new(requests[index], [.. candidates[index]],
                population.Receipt.IsRealizationComplete, evaluationComplete));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new TypeDeclarationLocatorResult.Evaluated(
            population.Receipt, includeAll, maxInventoryReads, members, answers.MoveToImmutable());
    }

    static int CompareCandidates(TypeDeclarationLocatorCandidate left, TypeDeclarationLocatorCandidate right)
    {
        int comparison = StringComparer.Ordinal.Compare(left.Name.Namespace, right.Name.Namespace);
        if (comparison != 0)
            return comparison;
        for (int index = 0; index < Math.Min(left.Name.Segments.Length, right.Name.Segments.Length); index++)
        {
            comparison = StringComparer.Ordinal.Compare(left.Name.Segments[index], right.Name.Segments[index]);
            if (comparison != 0)
                return comparison;
        }
        comparison = left.Name.Segments.Length.CompareTo(right.Name.Segments.Length);
        if (comparison != 0)
            return comparison;
        comparison = SourceOrder(left.Coordinate).CompareTo(SourceOrder(right.Coordinate));
        if (comparison != 0)
            return comparison;
        if (left.Coordinate is ExactLibrarySourceCoordinate.Package leftPackage
            && right.Coordinate is ExactLibrarySourceCoordinate.Package rightPackage)
        {
            comparison = StringComparer.Ordinal.Compare(
                leftPackage.PackageCoordinate.PackageId, rightPackage.PackageCoordinate.PackageId);
            if (comparison != 0)
                return comparison;
            comparison = StringComparer.Ordinal.Compare(
                leftPackage.PackageCoordinate.Version, rightPackage.PackageCoordinate.Version);
        }
        else if (left.Coordinate is ExactLibrarySourceCoordinate.Platform leftPlatform
            && right.Coordinate is ExactLibrarySourceCoordinate.Platform rightPlatform)
        {
            comparison = leftPlatform.Population.Family.CompareTo(rightPlatform.Population.Family);
        }
        if (comparison != 0)
            return comparison;
        comparison = CompareIdentities(left.Coordinate.LibraryIdentity.Identity, right.Coordinate.LibraryIdentity.Identity);
        if (comparison != 0)
            return comparison;
        comparison = left.Kind.CompareTo(right.Kind);
        if (comparison != 0)
            return comparison;
        comparison = left.Observation.Occurrence.ContextOrder.CompareTo(right.Observation.Occurrence.ContextOrder);
        return comparison != 0 ? comparison
            : left.Observation.Occurrence.MemberOrder.CompareTo(right.Observation.Occurrence.MemberOrder);
    }

    static int SourceOrder(ExactLibrarySourceCoordinate coordinate) => coordinate switch
    {
        ExactLibrarySourceCoordinate.Package => 0,
        ExactLibrarySourceCoordinate.Platform => 1,
        ExactLibrarySourceCoordinate.Project => 2,
        ExactLibrarySourceCoordinate.Local => 3,
        _ => throw new InspectionQueryException("The source coordinate has no admitted locator ordering."),
    };

    static int CompareIdentities(AssemblyReferenceIdentity left, AssemblyReferenceIdentity right)
    {
        int comparison = StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
        if (comparison != 0)
            return comparison;
        comparison = Comparer<Version>.Default.Compare(left.Version, right.Version);
        if (comparison != 0)
            return comparison;
        comparison = StringComparer.OrdinalIgnoreCase.Compare(
            AssemblyReferenceIdentity.NormalizeCulture(left.Culture),
            AssemblyReferenceIdentity.NormalizeCulture(right.Culture));
        return comparison != 0 ? comparison
            : StringComparer.OrdinalIgnoreCase.Compare(left.PublicKeyToken ?? "", right.PublicKeyToken ?? "");
    }

    static CandidateOpenFailure LibraryFailure(
        LibraryTypeDeclarationInventoryInspectionOutcome outcome) =>
        outcome switch
        {
            LibraryTypeDeclarationInventoryInspectionOutcome
                .Incomplete incomplete =>
                    new(
                        CandidateOpenFailureKind.ResourceBudget,
                        $"Library declaration inspection reached "
                            + $"{incomplete.Bound}."),
            LibraryTypeDeclarationInventoryInspectionOutcome
                .Rejected rejected =>
                    new(
                        CandidateOpenFailureKind.InvalidImage,
                        $"Library declaration inspection was rejected as "
                            + $"{rejected.Kind}."),
            LibraryTypeDeclarationInventoryInspectionOutcome
                .Failed failed =>
                    new(
                        failed.Kind
                            is LibraryTypeDeclarationInventoryInspectionFailureKind
                                .UnsupportedWindowsMetadata
                            ? CandidateOpenFailureKind
                                .UnsupportedMetadataFormat
                            : CandidateOpenFailureKind.InvalidImage,
                        $"Library declaration inspection failed as "
                            + $"{failed.Kind}."),
            _ => throw new InspectionQueryException(
                "A completed Library declaration inventory was not evaluated."),
        };
}
