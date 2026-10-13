using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record TypeOverviewDocumentInspectionPlan
{
    public TypeOverviewDocumentInspectionPlan(
        MetadataTypeDefinitionName type,
        TypeMemberGroupRowsRequest rows,
        ApiSurfaceExtractionBounds bounds,
        TypeMemberGroupSpelling spelling =
            TypeMemberGroupSpelling.CSharp,
        TypeMemberGroupAccessibilityFilter accessibility =
            TypeMemberGroupAccessibilityFilter.Public,
        TypeMemberGroupReceiverFilter receiver =
            TypeMemberGroupReceiverFilter.All,
        bool includeHidden = false,
        InspectionHierarchyRequest<TypeOverviewHierarchyTopology>?
            hierarchy = null,
        bool includeComposition = false,
        bool includeSelectorCounts = false)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        ArgumentNullException.ThrowIfNull(rows);
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        Members = new(
            count: null,
            rows: rows,
            composition: includeComposition ? new() : null,
            selectorCounts: includeSelectorCounts ? new() : null,
            spelling: spelling,
            accessibility: accessibility,
            receiver: receiver,
            includeHidden: includeHidden);
        if (hierarchy is not null)
        {
            TypeOverviewHierarchyProjection.ValidateRequest(
                hierarchy,
                Members,
                nameof(hierarchy));
        }

        Hierarchy = hierarchy;
        SourcePlan = new(
            type,
            bounds,
            Members);
    }

    public MetadataTypeDefinitionName Type { get; }
    public TypeMemberGroupPopulationRequest Members { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public InspectionHierarchyRequest<TypeOverviewHierarchyTopology>?
        Hierarchy { get; }
    internal TypeSubjectInspectionPlan SourcePlan { get; }
}

public sealed record TypeOverviewDocumentInspectionRequest
{
    public TypeOverviewDocumentInspectionRequest(
        LibraryReference library,
        TypeOverviewDocumentInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeOverviewDocumentInspectionPlan Plan { get; }
}

public enum TypeOverviewDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    IncompatibleContinuation,
    StaleContinuation,
    ContinuationOutOfRange,
    TypeNotFound,
    TypeAmbiguous,
}

public enum TypeOverviewDocumentInspectionBound
{
    MetadataRows,
    Members,
    RetainedTextCharacters,
}

public enum TypeOverviewDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
    PopulationResultMismatch,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Failed),
    "failed")]
public abstract record TypeOverviewDocumentInspectionOutcome
{
    private protected TypeOverviewDocumentInspectionOutcome()
    {
    }

    public sealed record Available(TypeOverviewDocument Document)
        : TypeOverviewDocumentInspectionOutcome;

    public sealed record Rejected(
        TypeOverviewDocumentInspectionRejection Reason)
        : TypeOverviewDocumentInspectionOutcome;

    public sealed record Incomplete(
        TypeOverviewDocumentInspectionBound Bound,
        long Limit,
        long Measured)
        : TypeOverviewDocumentInspectionOutcome;

    public sealed record Failed(
        TypeOverviewDocumentInspectionFailure Reason)
        : TypeOverviewDocumentInspectionOutcome;
}

public static class TypeOverviewDocumentInspectionOperation
{
    public static InspectionEnvelope<TypeOverviewDocumentInspectionOutcome>
        Execute(
            TypeOverviewDocumentInspectionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (request is null)
        {
            lease.Dispose();
            throw new ArgumentNullException(nameof(request));
        }

        InspectionEnvelope<TypeSubjectInspectionOutcome> source =
            TypeSubjectInspectionOperation.Execute(
                new(
                    request.Library,
                    request.Plan.SourcePlan),
                lease,
                cancellationToken);
        return new(
            Project(source.Content),
            source.Share,
            source.Diagnostics);
    }

    private static TypeOverviewDocumentInspectionOutcome Project(
        TypeSubjectInspectionOutcome source) =>
        source switch
        {
            TypeSubjectInspectionOutcome.Available available =>
                Project(available.Document),
            TypeSubjectInspectionOutcome.Rejected rejected =>
                new TypeOverviewDocumentInspectionOutcome.Rejected(
                    Rejection(rejected.Reason)),
            TypeSubjectInspectionOutcome.Incomplete incomplete =>
                new TypeOverviewDocumentInspectionOutcome.Incomplete(
                    Bound(incomplete.Bound),
                    incomplete.Limit,
                    incomplete.Measured),
            TypeSubjectInspectionOutcome.Failed failed =>
                new TypeOverviewDocumentInspectionOutcome.Failed(
                    Failure(failed.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown Type document inspection outcome."),
        };

    private static TypeOverviewDocumentInspectionOutcome Project(
        TypeSubjectInspectionContent source) =>
        source.Declarations switch
        {
            TypeSubjectMemberGroups.Available available =>
                Project(source, available.Population),
            TypeSubjectMemberGroups.Rejected rejected =>
                new TypeOverviewDocumentInspectionOutcome.Rejected(
                    Rejection(rejected.Reason)),
            TypeSubjectMemberGroups.Incomplete incomplete =>
                new TypeOverviewDocumentInspectionOutcome.Incomplete(
                    Bound(incomplete.Bound),
                    incomplete.Limit,
                    incomplete.Measured),
            TypeSubjectMemberGroups.Failed failed =>
                new TypeOverviewDocumentInspectionOutcome.Failed(
                    Failure(failed.Reason)),
            TypeSubjectMemberGroups.NotRequested =>
                new TypeOverviewDocumentInspectionOutcome.Failed(
                    TypeOverviewDocumentInspectionFailure
                        .PopulationResultMismatch),
            _ => throw new InvalidOperationException(
                "Unknown Type document declaration outcome."),
        };

    private static TypeOverviewDocumentInspectionOutcome Project(
        TypeSubjectInspectionContent source,
        TypeMemberGroupPopulationResult population) =>
        population.Rows switch
        {
            TypeMemberGroupRowsOutcome.Read rows
                when HasOverviewCorrespondence(population, rows) =>
                    new TypeOverviewDocumentInspectionOutcome.Available(
                        new(
                            source.Subject,
                            population,
                            source.AssemblyBytes)),
            TypeMemberGroupRowsOutcome.Read =>
                new TypeOverviewDocumentInspectionOutcome.Failed(
                    TypeOverviewDocumentInspectionFailure
                        .PopulationResultMismatch),
            TypeMemberGroupRowsOutcome.Rejected rejected =>
                new TypeOverviewDocumentInspectionOutcome.Rejected(
                    rejected.Reason switch
                    {
                        TypeMemberGroupRowsRejection
                                .ContinuationOutOfRange =>
                            TypeOverviewDocumentInspectionRejection
                                .ContinuationOutOfRange,
                        _ => throw new InvalidOperationException(
                            "Unknown Type Member-group Rows rejection."),
                    }),
            TypeMemberGroupRowsOutcome.Incomplete incomplete =>
                new TypeOverviewDocumentInspectionOutcome.Incomplete(
                    Bound(incomplete.Bound),
                    incomplete.Limit,
                    incomplete.Measured),
            TypeMemberGroupRowsOutcome.Failed failed =>
                new TypeOverviewDocumentInspectionOutcome.Failed(
                    failed.Reason switch
                    {
                        TypeMemberGroupRowsFailure.MalformedMetadata =>
                            TypeOverviewDocumentInspectionFailure
                                .MalformedMetadata,
                        _ => throw new InvalidOperationException(
                            "Unknown Type Member-group Rows failure."),
                    }),
            null => new TypeOverviewDocumentInspectionOutcome.Failed(
                TypeOverviewDocumentInspectionFailure
                    .PopulationResultMismatch),
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group Rows outcome."),
        };

    private static bool HasOverviewCorrespondence(
        TypeMemberGroupPopulationResult population,
        TypeMemberGroupRowsOutcome.Read rows) =>
        rows.Ordering == population.Binding.Ordering
        && rows.Items.All(row =>
            row.Binding.Population == population.Binding)
        && (rows.Continuation is null
            || rows.Continuation.Binding == population.Binding)
        && TypeOverviewDocument.HasUniformExactMemberCounts(rows);

    private static TypeOverviewDocumentInspectionRejection Rejection(
        TypeSubjectInspectionRejection reason) =>
        reason switch
        {
            TypeSubjectInspectionRejection.LeaseReferenceMismatch =>
                TypeOverviewDocumentInspectionRejection
                    .LeaseReferenceMismatch,
            TypeSubjectInspectionRejection.AssemblyIdentityMismatch =>
                TypeOverviewDocumentInspectionRejection
                    .AssemblyIdentityMismatch,
            TypeSubjectInspectionRejection.TypeNotFound =>
                TypeOverviewDocumentInspectionRejection.TypeNotFound,
            TypeSubjectInspectionRejection.TypeAmbiguous =>
                TypeOverviewDocumentInspectionRejection.TypeAmbiguous,
            _ => throw new InvalidOperationException(
                "Unknown Type document rejection."),
        };

    private static TypeOverviewDocumentInspectionRejection Rejection(
        TypeMemberGroupPopulationInspectionRejection reason) =>
        reason switch
        {
            TypeMemberGroupPopulationInspectionRejection
                    .LeaseReferenceMismatch =>
                TypeOverviewDocumentInspectionRejection
                    .LeaseReferenceMismatch,
            TypeMemberGroupPopulationInspectionRejection
                    .AssemblyIdentityMismatch =>
                TypeOverviewDocumentInspectionRejection
                    .AssemblyIdentityMismatch,
            TypeMemberGroupPopulationInspectionRejection
                    .IncompatibleContinuation =>
                TypeOverviewDocumentInspectionRejection
                    .IncompatibleContinuation,
            TypeMemberGroupPopulationInspectionRejection
                    .StaleContinuation =>
                TypeOverviewDocumentInspectionRejection
                    .StaleContinuation,
            TypeMemberGroupPopulationInspectionRejection.TypeNotFound =>
                TypeOverviewDocumentInspectionRejection.TypeNotFound,
            TypeMemberGroupPopulationInspectionRejection.TypeAmbiguous =>
                TypeOverviewDocumentInspectionRejection.TypeAmbiguous,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population rejection."),
        };

    private static TypeOverviewDocumentInspectionBound Bound(
        TypeSubjectInspectionBound bound) =>
        bound switch
        {
            TypeSubjectInspectionBound.MetadataRows =>
                TypeOverviewDocumentInspectionBound.MetadataRows,
            _ => throw new InvalidOperationException(
                "Unknown Type document inspection bound."),
        };

    private static TypeOverviewDocumentInspectionBound Bound(
        TypeMemberGroupPopulationBound bound) =>
        bound switch
        {
            TypeMemberGroupPopulationBound.MetadataRows =>
                TypeOverviewDocumentInspectionBound.MetadataRows,
            TypeMemberGroupPopulationBound.Members =>
                TypeOverviewDocumentInspectionBound.Members,
            TypeMemberGroupPopulationBound.RetainedTextCharacters =>
                TypeOverviewDocumentInspectionBound
                    .RetainedTextCharacters,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population bound."),
        };

    private static TypeOverviewDocumentInspectionFailure Failure(
        TypeSubjectInspectionFailure reason) =>
        reason switch
        {
            TypeSubjectInspectionFailure.NotManagedAssembly =>
                TypeOverviewDocumentInspectionFailure
                    .NotManagedAssembly,
            TypeSubjectInspectionFailure.ManagedModule =>
                TypeOverviewDocumentInspectionFailure.ManagedModule,
            TypeSubjectInspectionFailure.UnsupportedWindowsMetadata =>
                TypeOverviewDocumentInspectionFailure
                    .UnsupportedWindowsMetadata,
            TypeSubjectInspectionFailure.MalformedMetadata =>
                TypeOverviewDocumentInspectionFailure
                    .MalformedMetadata,
            TypeSubjectInspectionFailure.EmptyModuleVersionId =>
                TypeOverviewDocumentInspectionFailure
                    .EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown Type document inspection failure."),
        };

    private static TypeOverviewDocumentInspectionFailure Failure(
        TypeMemberGroupPopulationInspectionFailure reason) =>
        reason switch
        {
            TypeMemberGroupPopulationInspectionFailure
                    .NotManagedAssembly =>
                TypeOverviewDocumentInspectionFailure
                    .NotManagedAssembly,
            TypeMemberGroupPopulationInspectionFailure.ManagedModule =>
                TypeOverviewDocumentInspectionFailure.ManagedModule,
            TypeMemberGroupPopulationInspectionFailure
                    .UnsupportedWindowsMetadata =>
                TypeOverviewDocumentInspectionFailure
                    .UnsupportedWindowsMetadata,
            TypeMemberGroupPopulationInspectionFailure
                    .MalformedMetadata =>
                TypeOverviewDocumentInspectionFailure
                    .MalformedMetadata,
            TypeMemberGroupPopulationInspectionFailure
                    .EmptyModuleVersionId =>
                TypeOverviewDocumentInspectionFailure
                    .EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population failure."),
        };
}
