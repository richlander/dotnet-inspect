using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using ILInspector.Metadata;
using InertText;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

public enum TypeMemberGroupCategory
{
    Constructor,
    Method,
    Operator,
    ExplicitInterfaceImplementation,
    Property,
    Field,
    Event,
}

public enum TypeMemberGroupRole
{
    Declared,
    AttachedExtension,
}

public enum TypeMemberReceiver
{
    Static,
    This,
    Extension,
}

[Flags]
public enum TypeMemberReceiverKinds
{
    None = 0,
    Static = 1,
    This = 2,
    Extension = 4,
    All = Static | This | Extension,
}

public sealed record TypeMemberGroupTypeIdentity(
    LibraryContentReference ApiContent,
    AssemblyReferenceIdentity Assembly,
    Guid ModuleVersionId,
    MetadataTypeDefinitionName Definition,
    MetadataTypeDefinitionAddress Address);

public sealed record TypeMemberGroupIdentity(
    TypeMemberGroupTypeIdentity Type,
    InertString Name,
    TypeMemberGroupCategory Category,
    TypeMemberGroupRole Role,
    TypeMemberGroupTypeIdentity? AttachedDeclaringType);

public sealed record ExactMemberPopulationBinding(
    TypeMemberGroupIdentity Group,
    TypeMemberReceiverKinds Receivers);

public abstract record ExactMemberPopulationCountOutcome
{
    private protected ExactMemberPopulationCountOutcome()
    {
    }

    public sealed record Counted(int Value)
        : ExactMemberPopulationCountOutcome;
}

public sealed record TypeMemberGroupRow(
    TypeMemberGroupIdentity Identity,
    TypeMemberReceiverKinds ReceiverKinds,
    ExactMemberPopulationBinding ExactMembers,
    ExactMemberPopulationCountOutcome? ExactMemberCount);

public sealed record TypeMemberGroupPopulationBinding(
    TypeMemberGroupTypeIdentity Type,
    TypeMemberGroupCategory Category,
    TypeMemberReceiverKinds Receivers,
    PortableQueryIntent RowIntent);

public sealed record TypeMemberGroupPopulationWork(
    int CandidateMembersVisited,
    int GroupsAggregated,
    int RowsMaterialized,
    int ExactMemberRowsMaterialized,
    int SignaturesDecoded,
    int FormattedSignaturesMaterialized);

public enum TypeMemberGroupPopulationUnavailableReason
{
    TypeNotFound,
    TypeAmbiguous,
    TypeNotDefined,
}

public enum TypeMemberGroupPopulationIncompleteReason
{
    AssemblyBytes,
    MetadataRows,
    TypeResolution,
    RetainedGroups,
    NameWorkBytes,
    RetainedTextCharacters,
}

public enum TypeMemberGroupPopulationFailureReason
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum TypeMemberGroupPopulationRejectionReason
{
    Query,
    Library,
    SelectionOutOfRange,
}

public enum TypeMemberGroupPopulationLibraryRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
}

public sealed record TypeMemberGroupSelectionFailure(
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

public abstract record TypeMemberGroupPopulationOutcome
{
    private protected TypeMemberGroupPopulationOutcome()
    {
    }

    public sealed record Counted(
        TypeMemberGroupPopulationBinding Binding,
        int Value,
        TypeMemberGroupPopulationWork Work)
        : TypeMemberGroupPopulationOutcome;

    public sealed record Rows(
        TypeMemberGroupPopulationBinding Binding,
        ImmutableArray<TypeMemberGroupRow> Items,
        TypeMemberGroupPopulationWork Work)
        : TypeMemberGroupPopulationOutcome;

    public sealed record Unavailable(
        TypeMemberGroupPopulationUnavailableReason Reason)
        : TypeMemberGroupPopulationOutcome;

    public sealed record Incomplete(
        TypeMemberGroupPopulationIncompleteReason Reason,
        long Measured,
        string? Detail = null)
        : TypeMemberGroupPopulationOutcome;

    public sealed record Rejected(
        TypeMemberGroupPopulationRejectionReason Reason,
        TypeMemberGroupsQueryRequestRejectionKind? QueryRequest = null,
        TypeMemberGroupsQueryIntentRejection? QueryIntent = null,
        TypeMemberGroupPopulationLibraryRejection? Library = null,
        TypeMemberGroupSelectionFailure? SelectionFailure = null)
        : TypeMemberGroupPopulationOutcome;

    public sealed record Failed(
        TypeMemberGroupPopulationFailureReason Reason)
        : TypeMemberGroupPopulationOutcome;
}

public sealed class TypeMemberGroupPopulationQueryBounds
{
    public TypeMemberGroupPopulationQueryBounds(
        int maximumAssemblyBytes,
        int maximumMetadataRows,
        int maximumRetainedGroups,
        long maximumNameWorkBytes,
        int maximumRetainedTextCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumAssemblyBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumMetadataRows);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedGroups);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumNameWorkBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedTextCharacters);

        MaximumAssemblyBytes = maximumAssemblyBytes;
        MaximumMetadataRows = maximumMetadataRows;
        MaximumRetainedGroups = maximumRetainedGroups;
        MaximumNameWorkBytes = maximumNameWorkBytes;
        MaximumRetainedTextCharacters =
            maximumRetainedTextCharacters;
    }

    public int MaximumAssemblyBytes { get; }
    public int MaximumMetadataRows { get; }
    public int MaximumRetainedGroups { get; }
    public long MaximumNameWorkBytes { get; }
    public int MaximumRetainedTextCharacters { get; }
}

public sealed class TypeMemberGroupPopulationQueryRequest
{
    public TypeMemberGroupPopulationQueryRequest(
        LibraryReference library,
        MetadataTypeDefinitionName type,
        QuerySpaceRequest query,
        TypeMemberGroupPopulationQueryBounds bounds,
        bool includeExactMemberCount = false)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Type = type
            ?? throw new ArgumentNullException(nameof(type));
        Query = query
            ?? throw new ArgumentNullException(nameof(query));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        if (query.Terminal == QuerySpaceTerminalRequirement.Count
            && includeExactMemberCount)
        {
            throw new ArgumentException(
                "A MemberGroup Count cannot request exact-Member Counts.",
                nameof(includeExactMemberCount));
        }

        IncludeExactMemberCount = includeExactMemberCount;
    }

    public LibraryReference Library { get; }
    public MetadataTypeDefinitionName Type { get; }
    public QuerySpaceRequest Query { get; }
    public TypeMemberGroupPopulationQueryBounds Bounds { get; }
    public bool IncludeExactMemberCount { get; }
}

public sealed record TypeMemberGroupsQueryPlan(
    QuerySpaceRequest Request,
    PortableQueryIntent RowIntent,
    TypeMemberGroupCategory Category,
    TypeMemberReceiverKinds Receivers,
    ImmutableArray<AssemblyTypeMemberGroupSelectionStage> Selection);

public enum TypeMemberGroupsQueryRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentAssociationMismatch,
    QueryShapeMismatch,
    TerminalMismatch,
    ResultContractMismatch,
}

public sealed record TypeMemberGroupsQueryIntentRejection(
    RowQueryOperationKind OperationKind,
    int OperationPosition,
    int? TermPosition,
    int? SemanticStageNumber,
    RowQueryFailureReason Reason)
{
    internal static TypeMemberGroupsQueryIntentRejection From(
        RowQueryFailure failure) =>
        new(
            failure.OperationKind,
            failure.OperationPosition,
            failure.TermPosition,
            failure.SemanticStageNumber,
            failure.Reason);
}

public abstract record TypeMemberGroupsQueryRequestResult
{
    private protected TypeMemberGroupsQueryRequestResult()
    {
    }

    public sealed record Accepted(TypeMemberGroupsQueryPlan Plan)
        : TypeMemberGroupsQueryRequestResult;

    public sealed record Rejected(
        TypeMemberGroupsQueryRequestRejectionKind Kind)
        : TypeMemberGroupsQueryRequestResult;

    public sealed record IntentRejected(
        TypeMemberGroupsQueryIntentRejection Failure)
        : TypeMemberGroupsQueryRequestResult;
}

public static class TypeMemberGroupsQuery
{
    public const string OperationIdentity = "type-member-groups";
    public const string OperationRouteIdentity =
        "type-member-groups/default";
    public const string OperationSubjectRole = "exact-type";
    public const string OperationResultGrain = "member-group";
    public const string OperationProfileIdentity = "default";
    public const string OperationVocabularyIdentity =
        "type-member-groups/operation/v1";
    public const string QuerySpaceIdentity =
        "type-member-groups/query-space/v1";
    public const string RowVocabularyIdentity =
        "type-member-groups/rows/v1";
    public const string ReceiverFacetIdentity =
        "type-member-groups/facet/receiver";
    public const string ReceiverTermKey = "receiver";
    public const string ReceiverValueVocabularyIdentity =
        "type-member-groups/receiver/v1";
    public const string RowsResultContractIdentity =
        "type-member-groups/rows-result/v1";
    public const string CountResultContractIdentity =
        "type-member-groups/count-result/v1";

    public const string ConstructorsRowSet = "constructors";
    public const string MethodsRowSet = "methods";
    public const string OperatorsRowSet = "operators";
    public const string ExplicitImplementationsRowSet =
        "explicit-interface-implementations";
    public const string PropertiesRowSet = "properties";
    public const string FieldsRowSet = "fields";
    public const string EventsRowSet = "events";

    public const string StaticReceiverValue = "static";
    public const string ThisReceiverValue = "this";
    public const string ExtensionReceiverValue = "extension";

    private static readonly string[] RowSets =
    [
        ConstructorsRowSet,
        MethodsRowSet,
        OperatorsRowSet,
        ExplicitImplementationsRowSet,
        PropertiesRowSet,
        FieldsRowSet,
        EventsRowSet,
    ];

    private static readonly RowQueryVocabulary<TypeMemberGroupRow>
        RowVocabulary =
            RowQueryVocabulary<TypeMemberGroupRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [ReceiverKey()],
                []);

    private static readonly QuerySpaceRowScopeBinding<TypeMemberGroupRow>
        MemberGroupsRowScope = CreateRowScope();

    private static readonly Lazy<QuerySpaceBinding> QuerySpaceValue =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => QuerySpaceValue.Value;

    public static QuerySpaceRowScopeBinding<TypeMemberGroupRow> RowScope(
        TypeMemberGroupCategory category)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        return MemberGroupsRowScope;
    }

    public static PortableQueryIntent CreateRowIntent(
        TypeMemberReceiver? receiver = null,
        bool exclude = false,
        RowSelectionIntent<string>? selection = null)
    {
        if (receiver is not null && !Enum.IsDefined(receiver.Value))
            throw new ArgumentOutOfRangeException(nameof(receiver));
        if (receiver is null && exclude)
        {
            throw new ArgumentException(
                "Receiver exclusion requires one receiver value.",
                nameof(exclude));
        }

        return PortableQueryIntent.Create(
            receiver is null
                ? []
                :
                [
                    new(
                        ReceiverTermKey,
                        exclude
                            ? PortableQueryOperator.NotEqual
                            : PortableQueryOperator.Equal,
                        ReceiverValue(receiver.Value)),
                ],
            [],
            PortableQueryRowSelection.ToStages(selection),
            []);
    }

    public static QuerySpaceRequest CreateRequest(
        TypeMemberGroupCategory category,
        QuerySpaceTerminalRequirement terminal,
        PortableQueryIntent? rowIntent = null)
    {
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if (terminal is not QuerySpaceTerminalRequirement.Rows
            and not QuerySpaceTerminalRequirement.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(terminal));
        }

        string rowSet = RowSet(category);
        QuerySpaceRowScopeBinding<TypeMemberGroupRow> scope =
            RowScope(category);
        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            EmptyIntent(),
            [rowSet],
            [
                new(
                    scope.Descriptor.Identity,
                    rowIntent ?? EmptyIntent(),
                    [rowSet]),
            ],
            terminal);
    }

    public static TypeMemberGroupsQueryRequestResult ResolveRequest(
        QuerySpaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                request.QuerySpace,
                QuerySpaceIdentity,
                StringComparison.Ordinal))
        {
            return Rejected(
                TypeMemberGroupsQueryRequestRejectionKind
                    .QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !TryCategory(
                request.ParticipatingRowSets[0],
                out TypeMemberGroupCategory category))
        {
            return Rejected(
                TypeMemberGroupsQueryRequestRejectionKind
                    .ParticipatingRowSetsMismatch);
        }

        QuerySpaceRowScopeBinding<TypeMemberGroupRow> scope =
            RowScope(category);
        if (request.RowIntents.Count != 1
            || !string.Equals(
                request.RowIntents[0].Scope,
                scope.Descriptor.Identity,
                StringComparison.Ordinal)
            || request.RowIntents[0].RowSets.Count != 1
            || !string.Equals(
                request.RowIntents[0].RowSets[0],
                RowSet(category),
                StringComparison.Ordinal))
        {
            return Rejected(
                TypeMemberGroupsQueryRequestRejectionKind
                    .RowIntentAssociationMismatch);
        }
        if (request.Terminal is not QuerySpaceTerminalRequirement.Rows
            and not QuerySpaceTerminalRequirement.Count)
        {
            return Rejected(
                TypeMemberGroupsQueryRequestRejectionKind
                    .TerminalMismatch);
        }
        string expectedContract =
            request.Terminal == QuerySpaceTerminalRequirement.Rows
                ? RowsResultContractIdentity
                : CountResultContractIdentity;
        if (!string.Equals(
                request.ResultContract,
                expectedContract,
                StringComparison.Ordinal))
        {
            return Rejected(
                TypeMemberGroupsQueryRequestRejectionKind
                    .ResultContractMismatch);
        }

        QuerySpaceRowIntentAssociation association =
            request.RowIntents[0];
        if (!IsEmpty(request.Operation)
            || association.Intent.Bounds.Count != 0
            || association.Intent.Order.Count != 0
            || association.Intent.Terms.Any(
                static term =>
                    !string.Equals(
                        term.Key,
                        ReceiverTermKey,
                        StringComparison.Ordinal)
                    || term.Operator is not PortableQueryOperator.Equal
                        and not PortableQueryOperator.NotEqual)
            || association.Intent.Stages.Any(
                static stage =>
                    stage.Kind is not RowSelectionStageKind.Head
                        and not RowSelectionStageKind.Tail
                        and not RowSelectionStageKind.Window))
        {
            return Rejected(
                TypeMemberGroupsQueryRequestRejectionKind
                    .QueryShapeMismatch);
        }

        PortableQueryResolution<OperationPlan> operationResolution =
            OperationRegistration.Route.Resolve(
                request.Operation,
                cancellationToken);
        if (!operationResolution.IsResolved)
        {
            throw new InvalidOperationException(
                "A structurally valid MemberGroup operation did not resolve.");
        }

        RowQueryResolutionResult<TypeMemberGroupRow> rowResolution =
            scope.Resolve(association.Intent);
        if (!rowResolution.IsSuccess)
        {
            return new TypeMemberGroupsQueryRequestResult.IntentRejected(
                TypeMemberGroupsQueryIntentRejection.From(
                    rowResolution.Failure
                        ?? throw new InvalidOperationException(
                            "A failed row resolution requires a failure.")));
        }

        TypeMemberReceiverKinds receivers =
            ResolveReceivers(association.Intent.Terms);
        ImmutableArray<AssemblyTypeMemberGroupSelectionStage> selection =
            [
                .. rowResolution.Plan!.SelectionPlan.Stages.Select(
                    static stage =>
                        stage.Kind switch
                        {
                            RowSelectionStageKind.Head =>
                                AssemblyTypeMemberGroupSelectionStage.Head(
                                    stage.Count),
                            RowSelectionStageKind.Tail =>
                                AssemblyTypeMemberGroupSelectionStage.Tail(
                                    stage.Count),
                            RowSelectionStageKind.Window =>
                                AssemblyTypeMemberGroupSelectionStage.Window(
                                    stage.Start,
                                    stage.End),
                            _ => throw new InvalidOperationException(
                                "Unsupported MemberGroup row stage."),
                        }),
            ];
        return new TypeMemberGroupsQueryRequestResult.Accepted(
            new(
                request,
                association.Intent,
                category,
                receivers,
                selection));
    }

    public static TypeMemberGroupPopulationOutcome Execute(
        TypeMemberGroupPopulationQueryRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lease);
        cancellationToken.ThrowIfCancellationRequested();

        TypeMemberGroupsQueryRequestResult resolution =
            ResolveRequest(request.Query, cancellationToken);
        if (resolution
            is TypeMemberGroupsQueryRequestResult.Rejected rejected)
        {
            return new TypeMemberGroupPopulationOutcome.Rejected(
                TypeMemberGroupPopulationRejectionReason.Query,
                QueryRequest: rejected.Kind);
        }
        if (resolution
            is TypeMemberGroupsQueryRequestResult.IntentRejected
                intentRejected)
        {
            return new TypeMemberGroupPopulationOutcome.Rejected(
                TypeMemberGroupPopulationRejectionReason.Query,
                QueryIntent: intentRejected.Failure);
        }
        TypeMemberGroupsQueryPlan plan =
            ((TypeMemberGroupsQueryRequestResult.Accepted)resolution)
                .Plan;

        LibraryTypeMemberGroupPopulationInspectionOutcome source =
            LibraryTypeMemberGroupPopulationInspection.Execute(
                new(
                    request.Library,
                    request.Type,
                    new(
                        Map(plan.Category),
                        Map(plan.Receivers),
                        request.Query.Terminal
                            == QuerySpaceTerminalRequirement.Rows
                                ? AssemblyTypeMemberGroupTerminal.Rows
                                : AssemblyTypeMemberGroupTerminal.Count,
                        plan.Selection,
                        request.IncludeExactMemberCount),
                    new(
                        request.Bounds.MaximumAssemblyBytes,
                        request.Bounds.MaximumMetadataRows,
                        request.Bounds.MaximumRetainedGroups,
                        request.Bounds.MaximumNameWorkBytes,
                        request.Bounds.MaximumRetainedTextCharacters)),
                lease,
                cancellationToken);
        return Project(source, plan);
    }

    private static TypeMemberGroupPopulationOutcome Project(
        LibraryTypeMemberGroupPopulationInspectionOutcome source,
        TypeMemberGroupsQueryPlan plan) =>
        source switch
        {
            LibraryTypeMemberGroupPopulationInspectionOutcome.Completed
                completed =>
                Project(completed.Correspondence, plan),
            LibraryTypeMemberGroupPopulationInspectionOutcome.Unavailable
                unavailable =>
                new TypeMemberGroupPopulationOutcome.Unavailable(
                    unavailable.Reason switch
                    {
                        LibraryTypeMemberGroupPopulationUnavailableReason
                                .TypeNotFound =>
                            TypeMemberGroupPopulationUnavailableReason
                                .TypeNotFound,
                        LibraryTypeMemberGroupPopulationUnavailableReason
                                .TypeAmbiguous =>
                            TypeMemberGroupPopulationUnavailableReason
                                .TypeAmbiguous,
                        LibraryTypeMemberGroupPopulationUnavailableReason
                                .TypeNotDefined =>
                            TypeMemberGroupPopulationUnavailableReason
                                .TypeNotDefined,
                        _ => throw new InvalidOperationException(
                            "Unknown MemberGroup unavailability."),
                    }),
            LibraryTypeMemberGroupPopulationInspectionOutcome.Incomplete
                incomplete =>
                new TypeMemberGroupPopulationOutcome.Incomplete(
                    incomplete.Bound switch
                    {
                        LibraryTypeMemberGroupPopulationInspectionBound
                                .AssemblyBytes =>
                            TypeMemberGroupPopulationIncompleteReason
                                .AssemblyBytes,
                        LibraryTypeMemberGroupPopulationInspectionBound
                                .MetadataRows =>
                            TypeMemberGroupPopulationIncompleteReason
                                .MetadataRows,
                        LibraryTypeMemberGroupPopulationInspectionBound
                                .TypeResolution =>
                            TypeMemberGroupPopulationIncompleteReason
                                .TypeResolution,
                        LibraryTypeMemberGroupPopulationInspectionBound
                                .RetainedGroups =>
                            TypeMemberGroupPopulationIncompleteReason
                                .RetainedGroups,
                        LibraryTypeMemberGroupPopulationInspectionBound
                                .NameWorkBytes =>
                            TypeMemberGroupPopulationIncompleteReason
                                .NameWorkBytes,
                        LibraryTypeMemberGroupPopulationInspectionBound
                                .RetainedTextCharacters =>
                            TypeMemberGroupPopulationIncompleteReason
                                .RetainedTextCharacters,
                        _ => throw new InvalidOperationException(
                            "Unknown MemberGroup incomplete reason."),
                    },
                    incomplete.Measured,
                    incomplete.Detail),
            LibraryTypeMemberGroupPopulationInspectionOutcome.Rejected
                rejected =>
                new TypeMemberGroupPopulationOutcome.Rejected(
                    TypeMemberGroupPopulationRejectionReason.Library,
                    Library: rejected.Kind switch
                    {
                        LibraryTypeMemberGroupPopulationInspectionRejectionKind
                                .LeaseReferenceMismatch =>
                            TypeMemberGroupPopulationLibraryRejection
                                .LeaseReferenceMismatch,
                        LibraryTypeMemberGroupPopulationInspectionRejectionKind
                                .AssemblyIdentityMismatch =>
                            TypeMemberGroupPopulationLibraryRejection
                                .AssemblyIdentityMismatch,
                        _ => throw new InvalidOperationException(
                            "Unknown MemberGroup Library rejection."),
                    }),
            LibraryTypeMemberGroupPopulationInspectionOutcome.Failed
                failed =>
                new TypeMemberGroupPopulationOutcome.Failed(
                    failed.Kind switch
                    {
                        LibraryTypeMemberGroupPopulationInspectionFailureKind
                                .NotManagedAssembly =>
                            TypeMemberGroupPopulationFailureReason
                                .NotManagedAssembly,
                        LibraryTypeMemberGroupPopulationInspectionFailureKind
                                .ManagedModule =>
                            TypeMemberGroupPopulationFailureReason
                                .ManagedModule,
                        LibraryTypeMemberGroupPopulationInspectionFailureKind
                                .UnsupportedWindowsMetadata =>
                            TypeMemberGroupPopulationFailureReason
                                .UnsupportedWindowsMetadata,
                        LibraryTypeMemberGroupPopulationInspectionFailureKind
                                .MalformedMetadata =>
                            TypeMemberGroupPopulationFailureReason
                                .MalformedMetadata,
                        LibraryTypeMemberGroupPopulationInspectionFailureKind
                                .EmptyModuleVersionId =>
                            TypeMemberGroupPopulationFailureReason
                                .EmptyModuleVersionId,
                        _ => throw new InvalidOperationException(
                            "Unknown MemberGroup failure."),
                    }),
            _ => throw new InvalidOperationException(
                "Unknown MemberGroup source outcome."),
        };

    private static TypeMemberGroupPopulationOutcome Project(
        LibraryTypeMemberGroupPopulationCorrespondence source,
        TypeMemberGroupsQueryPlan plan)
    {
        TypeMemberGroupTypeIdentity type = new(
            source.Subject.ApiContent,
            source.Subject.AssemblyIdentity,
            source.Subject.ModuleVersionId,
            source.Subject.Type,
            source.Subject.TypeAddress);
        TypeMemberGroupPopulationBinding binding = new(
            type,
            plan.Category,
            plan.Receivers,
            plan.RowIntent);
        return source.Population switch
        {
            AssemblyTypeMemberGroupPopulationOutcome.Counted counted =>
                new TypeMemberGroupPopulationOutcome.Counted(
                    binding,
                    counted.Value,
                    Map(counted.Work)),
            AssemblyTypeMemberGroupPopulationOutcome.Read read =>
                new TypeMemberGroupPopulationOutcome.Rows(
                    binding,
                    [
                        .. read.Rows.Select(row =>
                            Project(type, plan.Receivers, row)),
                    ],
                    Map(read.Work)),
            AssemblyTypeMemberGroupPopulationOutcome.Rejected rejected =>
                new TypeMemberGroupPopulationOutcome.Rejected(
                    rejected.Reason
                        == AssemblyTypeMemberGroupPopulationRejection
                            .SelectionOutOfRange
                            ? TypeMemberGroupPopulationRejectionReason
                                .SelectionOutOfRange
                            : TypeMemberGroupPopulationRejectionReason.Library,
                    SelectionFailure: rejected.SelectionFailure is null
                        ? null
                        : new(
                            rejected.SelectionFailure.StageNumber,
                            rejected.SelectionFailure.RequiredPosition,
                            rejected.SelectionFailure.AvailableCount)),
            _ => throw new InvalidOperationException(
                "The LibraryMetadata boundary must project incomplete and "
                    + "failed source outcomes before query composition."),
        };
    }

    private static TypeMemberGroupRow Project(
        TypeMemberGroupTypeIdentity subject,
        TypeMemberReceiverKinds selectedReceivers,
        AssemblyTypeMemberGroupRow row)
    {
        TypeMemberGroupTypeIdentity? declaringType =
            row.AttachedDeclaringType is null
                ? null
                : new(
                    subject.ApiContent,
                    subject.Assembly,
                    subject.ModuleVersionId,
                    row.AttachedDeclaringType,
                    row.AttachedDeclaringTypeAddress
                        ?? throw new InvalidOperationException(
                            "An attached group requires a declaring address."));
        var identity = new TypeMemberGroupIdentity(
            subject,
            row.Name,
            Map(row.Category),
            row.Role == AssemblyTypeMemberGroupRole.Declared
                ? TypeMemberGroupRole.Declared
                : TypeMemberGroupRole.AttachedExtension,
            declaringType);
        var exactMembers = new ExactMemberPopulationBinding(
            identity,
            selectedReceivers);
        return new(
            identity,
            Map(row.ReceiverKinds),
            exactMembers,
            row.ExactMemberCount is { } count
                ? new ExactMemberPopulationCountOutcome.Counted(count)
                : null);
    }

    private static TypeMemberGroupPopulationWork Map(
        AssemblyTypeMemberGroupPopulationWork work) =>
        new(
            work.CandidateMembersVisited,
            work.GroupsAggregated,
            work.RowsMaterialized,
            work.ExactMemberRowsMaterialized,
            work.SignaturesDecoded,
            work.FormattedSignaturesMaterialized);

    private static AssemblyTypeMemberGroupCategory Map(
        TypeMemberGroupCategory category) =>
        category switch
        {
            TypeMemberGroupCategory.Constructor =>
                AssemblyTypeMemberGroupCategory.Constructor,
            TypeMemberGroupCategory.Method =>
                AssemblyTypeMemberGroupCategory.Method,
            TypeMemberGroupCategory.Operator =>
                AssemblyTypeMemberGroupCategory.Operator,
            TypeMemberGroupCategory.ExplicitInterfaceImplementation =>
                AssemblyTypeMemberGroupCategory
                    .ExplicitInterfaceImplementation,
            TypeMemberGroupCategory.Property =>
                AssemblyTypeMemberGroupCategory.Property,
            TypeMemberGroupCategory.Field =>
                AssemblyTypeMemberGroupCategory.Field,
            TypeMemberGroupCategory.Event =>
                AssemblyTypeMemberGroupCategory.Event,
            _ => throw new InvalidOperationException(
                "Unknown MemberGroup category."),
        };

    private static TypeMemberGroupCategory Map(
        AssemblyTypeMemberGroupCategory category) =>
        category switch
        {
            AssemblyTypeMemberGroupCategory.Constructor =>
                TypeMemberGroupCategory.Constructor,
            AssemblyTypeMemberGroupCategory.Method =>
                TypeMemberGroupCategory.Method,
            AssemblyTypeMemberGroupCategory.Operator =>
                TypeMemberGroupCategory.Operator,
            AssemblyTypeMemberGroupCategory
                    .ExplicitInterfaceImplementation =>
                TypeMemberGroupCategory.ExplicitInterfaceImplementation,
            AssemblyTypeMemberGroupCategory.Property =>
                TypeMemberGroupCategory.Property,
            AssemblyTypeMemberGroupCategory.Field =>
                TypeMemberGroupCategory.Field,
            AssemblyTypeMemberGroupCategory.Event =>
                TypeMemberGroupCategory.Event,
            _ => throw new InvalidOperationException(
                "Unknown MemberGroup category."),
        };

    private static AssemblyTypeMemberReceiverKinds Map(
        TypeMemberReceiverKinds receivers)
    {
        AssemblyTypeMemberReceiverKinds result =
            AssemblyTypeMemberReceiverKinds.None;
        if ((receivers & TypeMemberReceiverKinds.Static) != 0)
            result |= AssemblyTypeMemberReceiverKinds.Static;
        if ((receivers & TypeMemberReceiverKinds.This) != 0)
            result |= AssemblyTypeMemberReceiverKinds.This;
        if ((receivers & TypeMemberReceiverKinds.Extension) != 0)
            result |= AssemblyTypeMemberReceiverKinds.Extension;
        return result;
    }

    private static TypeMemberReceiverKinds Map(
        AssemblyTypeMemberReceiverKinds receivers)
    {
        TypeMemberReceiverKinds result = TypeMemberReceiverKinds.None;
        if ((receivers & AssemblyTypeMemberReceiverKinds.Static) != 0)
            result |= TypeMemberReceiverKinds.Static;
        if ((receivers & AssemblyTypeMemberReceiverKinds.This) != 0)
            result |= TypeMemberReceiverKinds.This;
        if ((receivers & AssemblyTypeMemberReceiverKinds.Extension) != 0)
            result |= TypeMemberReceiverKinds.Extension;
        return result;
    }

    private static TypeMemberReceiverKinds ResolveReceivers(
        IReadOnlyList<PortableQueryTerm> terms)
    {
        TypeMemberReceiverKinds receivers = TypeMemberReceiverKinds.All;
        foreach (PortableQueryTerm term in terms)
        {
            TypeMemberReceiverKinds selected =
                term.Value switch
                {
                    StaticReceiverValue =>
                        TypeMemberReceiverKinds.Static,
                    ThisReceiverValue =>
                        TypeMemberReceiverKinds.This,
                    ExtensionReceiverValue =>
                        TypeMemberReceiverKinds.Extension,
                    _ => throw new InvalidOperationException(
                        "A resolved receiver term has an unknown value."),
                };
            receivers = term.Operator switch
            {
                PortableQueryOperator.Equal =>
                    receivers & selected,
                PortableQueryOperator.NotEqual =>
                    receivers & ~selected,
                _ => throw new InvalidOperationException(
                    "A resolved receiver term has an unknown operator."),
            };
        }
        return receivers;
    }

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRoute,
            [MemberGroupsRowScope],
            [
                QuerySpaceTerminalRequirement.Rows,
                QuerySpaceTerminalRequirement.Count,
            ],
            acceptsContinuation: false,
            [
                new(
                    QuerySpaceTerminalRequirement.Rows,
                    RowsResultContractIdentity),
                new(
                    QuerySpaceTerminalRequirement.Count,
                    CountResultContractIdentity),
            ]);

    private static QuerySpaceRowScopeBinding<TypeMemberGroupRow>
        CreateRowScope() =>
        new(
            new QuerySpaceRowScopeDescriptor(
                "type-member-groups/member-groups/v1",
                RowVocabularyIdentity,
                RowSets,
                [
                    new(
                        ReceiverFacetIdentity,
                        ReceiverTermKey,
                        [
                            PortableQueryOperator.Equal,
                            PortableQueryOperator.NotEqual,
                        ],
                        "exact Member receiver classification",
                        ReceiverValueVocabularyIdentity,
                        "receiver",
                        [
                            StaticReceiverValue,
                            ThisReceiverValue,
                            ExtensionReceiverValue,
                        ],
                        "Filters exact declarations before MemberGroup "
                            + "formation.",
                        supportsOrdering: false),
                ],
                [],
                [
                    RowSelectionStageKind.Head,
                    RowSelectionStageKind.Tail,
                    RowSelectionStageKind.Window,
                ]),
            RowVocabulary);

    private static RowQueryKey<TypeMemberGroupRow> ReceiverKey() =>
        RowQueryKey<TypeMemberGroupRow>.Create(
            RowQueryKeyIdentity.Create(),
            ReceiverTermKey,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            static row =>
                RowQueryValue<TypeMemberReceiverKinds>.Present(
                    row.ReceiverKinds),
            BindReceiver);

    private static Predicate<TypeMemberReceiverKinds>? BindReceiver(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!TryParseReceiver(
                token.Text,
                out TypeMemberReceiverKinds receiver))
        {
            return null;
        }

        return operation switch
        {
            RowQueryOperator.Equals =>
                value => (value & receiver) != 0,
            RowQueryOperator.NotEquals =>
                value => (value & receiver) == 0,
            _ => null,
        };
    }

    private static bool TryParseReceiver(
        string value,
        out TypeMemberReceiverKinds receiver)
    {
        receiver = value switch
        {
            StaticReceiverValue => TypeMemberReceiverKinds.Static,
            ThisReceiverValue => TypeMemberReceiverKinds.This,
            ExtensionReceiverValue => TypeMemberReceiverKinds.Extension,
            _ => TypeMemberReceiverKinds.None,
        };
        return receiver != TypeMemberReceiverKinds.None;
    }

    private static string ReceiverValue(TypeMemberReceiver receiver) =>
        receiver switch
        {
            TypeMemberReceiver.Static => StaticReceiverValue,
            TypeMemberReceiver.This => ThisReceiverValue,
            TypeMemberReceiver.Extension => ExtensionReceiverValue,
            _ => throw new InvalidOperationException(
                "Unknown Member receiver."),
        };

    private static bool IsEmpty(PortableQueryIntent intent) =>
        intent.Terms.Count == 0
        && intent.Bounds.Count == 0
        && intent.Stages.Count == 0
        && intent.Order.Count == 0;

    private static PortableQueryIntent EmptyIntent() =>
        PortableQueryIntent.Create([], [], [], []);

    private static string RowSet(TypeMemberGroupCategory category) =>
        category switch
        {
            TypeMemberGroupCategory.Constructor => ConstructorsRowSet,
            TypeMemberGroupCategory.Method => MethodsRowSet,
            TypeMemberGroupCategory.Operator => OperatorsRowSet,
            TypeMemberGroupCategory.ExplicitInterfaceImplementation =>
                ExplicitImplementationsRowSet,
            TypeMemberGroupCategory.Property => PropertiesRowSet,
            TypeMemberGroupCategory.Field => FieldsRowSet,
            TypeMemberGroupCategory.Event => EventsRowSet,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };

    private static bool TryCategory(
        string rowSet,
        out TypeMemberGroupCategory category)
    {
        category = rowSet switch
        {
            ConstructorsRowSet => TypeMemberGroupCategory.Constructor,
            MethodsRowSet => TypeMemberGroupCategory.Method,
            OperatorsRowSet => TypeMemberGroupCategory.Operator,
            ExplicitImplementationsRowSet =>
                TypeMemberGroupCategory.ExplicitInterfaceImplementation,
            PropertiesRowSet => TypeMemberGroupCategory.Property,
            FieldsRowSet => TypeMemberGroupCategory.Field,
            EventsRowSet => TypeMemberGroupCategory.Event,
            _ => default,
        };
        return RowSets.Contains(rowSet, StringComparer.Ordinal);
    }

    private static TypeMemberGroupsQueryRequestResult Rejected(
        TypeMemberGroupsQueryRequestRejectionKind kind) =>
        new TypeMemberGroupsQueryRequestResult.Rejected(kind);

    private sealed record OperationPredicate;
    private sealed record OperationPlan;

    private sealed class OperationVocabulary :
        PortableQueryVocabulary<OperationPredicate, OperationPlan>
    {
        public override string Identity => OperationVocabularyIdentity;

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<OperationPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool TryGetDimension(
            string dimension,
            [NotNullWhen(true)]
            out PortableQueryDimensionDeclaration<OperationPredicate>?
                declaration)
        {
            declaration = null;
            return false;
        }

        public override bool AdmitsStageKind(
            RowSelectionStageKind kind) =>
            false;

        public override bool TryGetNamedOrder(
            string reference,
            out PortableQueryOrderPurpose purpose)
        {
            purpose = default;
            return false;
        }

        public override bool IsOrderable(string key) => false;

        public override OperationPlan CreatePlan(
            PortableQueryResolvedIntent<OperationPredicate> resolved) =>
            new();
    }

    private static class OperationRegistration
    {
        private static readonly OperationVocabulary Vocabulary = new();

        internal static readonly QueryOperationDefinition<
            OperationPredicate,
            OperationPlan> Definition =
                QueryOperationDefinition<
                    OperationPredicate,
                    OperationPlan>.Create(
                        OperationIdentity,
                        Vocabulary,
                        [OperationSubjectRole],
                        [OperationResultGrain],
                        RowSets,
                        [],
                        [],
                        [
                            new(
                                OperationProfileIdentity,
                                [],
                                []),
                        ]);

        internal static readonly QueryOperationRoute<
            OperationPredicate,
            OperationPlan> Route =
                QueryOperationRoute<
                    OperationPredicate,
                    OperationPlan>.Create(
                        OperationRouteIdentity,
                        Definition,
                        OperationSubjectRole,
                        OperationResultGrain,
                        RowSets,
                        OperationProfileIdentity,
                        [],
                        []);
    }
}
