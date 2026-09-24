using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using QuerySpace;
using QuerySpace.Composition;
using QuerySpace.Operations;
using QuerySpace.Rows;

namespace DotnetInspector.Queries;

/// <summary>How one Type Member receives its target.</summary>
public enum TypeMemberReceiver
{
    Static,
    This,
    Extension,
}

/// <summary>How one Member participates in the inspected Type population.</summary>
public enum TypeMemberRowKind
{
    Declared,
    AttachedExtension,
}

/// <summary>Exact assembly-and-definition identity for one Type.</summary>
public sealed class TypeInspectionTypeIdentity :
    IEquatable<TypeInspectionTypeIdentity>
{
    public TypeInspectionTypeIdentity(
        ExactTypeAssemblyIdentity assembly,
        ExactTypeDefinitionIdentity definition)
    {
        Assembly = assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        Definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
    }

    public ExactTypeAssemblyIdentity Assembly { get; }

    public ExactTypeDefinitionIdentity Definition { get; }

    public bool Equals(TypeInspectionTypeIdentity? other) =>
        other is not null
        && Assembly == other.Assembly
        && DefinitionIdentityEquals(Definition, other.Definition);

    public override bool Equals(object? obj) =>
        Equals(obj as TypeInspectionTypeIdentity);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Assembly);
        AddDefinitionIdentity(ref hash, Definition);
        return hash.ToHashCode();
    }

    internal static bool DefinitionIdentityEquals(
        ExactTypeDefinitionIdentity left,
        ExactTypeDefinitionIdentity right) =>
        string.Equals(
            left.Namespace,
            right.Namespace,
            StringComparison.Ordinal)
        && left.Segments.SequenceEqual(
            right.Segments,
            StringComparer.Ordinal);

    internal static void AddDefinitionIdentity(
        ref HashCode hash,
        ExactTypeDefinitionIdentity type)
    {
        hash.Add(type.Namespace, StringComparer.Ordinal);
        foreach (string segment in type.Segments)
            hash.Add(segment, StringComparer.Ordinal);
    }
}

/// <summary>Exact declaration identity for one Type Member row.</summary>
public sealed class TypeMemberDeclarationIdentity :
    IEquatable<TypeMemberDeclarationIdentity>
{
    public TypeMemberDeclarationIdentity(
        TypeInspectionTypeIdentity declaringType,
        MemberAnchor member)
    {
        DeclaringType = declaringType
            ?? throw new ArgumentNullException(nameof(declaringType));
        Member = member
            ?? throw new ArgumentNullException(nameof(member));
    }

    public TypeInspectionTypeIdentity DeclaringType { get; }

    public MemberAnchor Member { get; }

    public bool Equals(TypeMemberDeclarationIdentity? other) =>
        other is not null
        && DeclaringType.Equals(other.DeclaringType)
        && Member == other.Member;

    public override bool Equals(object? obj) =>
        Equals(obj as TypeMemberDeclarationIdentity);

    public override int GetHashCode() =>
        HashCode.Combine(DeclaringType, Member);
}

/// <summary>One exact declaration in a Type's canonical Member population.</summary>
public sealed record TypeMemberRow
{
    public TypeMemberRow(
        TypeMemberRowKind rowKind,
        TypeMemberReceiver receiver,
        TypeMemberDeclarationIdentity declaration,
        string name,
        string declarationKind,
        string? signature,
        TypeInspectionTypeIdentity? attachedReceiver = null)
    {
        if (!Enum.IsDefined(rowKind))
            throw new ArgumentOutOfRangeException(nameof(rowKind));
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(declarationKind);
        if (rowKind is TypeMemberRowKind.AttachedExtension
            && (receiver is not TypeMemberReceiver.Extension
                || attachedReceiver is null))
        {
            throw new ArgumentException(
                "An attached extension row requires extension receiver "
                    + "classification and one exact attached receiver Type.",
                nameof(attachedReceiver));
        }
        if (rowKind is TypeMemberRowKind.Declared
            && attachedReceiver is not null)
        {
            throw new ArgumentException(
                "A declared Member row cannot carry an attached receiver Type.",
                nameof(attachedReceiver));
        }

        RowKind = rowKind;
        Receiver = receiver;
        Declaration = declaration;
        Name = name;
        DeclarationKind = declarationKind;
        Signature = signature;
        AttachedReceiver = attachedReceiver;
    }

    public TypeMemberRowKind RowKind { get; }

    public TypeMemberReceiver Receiver { get; }

    public TypeMemberDeclarationIdentity Declaration { get; }

    public string Name { get; }

    public string DeclarationKind { get; }

    public string? Signature { get; }

    public TypeInspectionTypeIdentity? AttachedReceiver { get; }
}

/// <summary>Immutable identity of one canonical Type Member population.</summary>
public sealed class TypeMemberPopulationBinding :
    IEquatable<TypeMemberPopulationBinding>
{
    public TypeMemberPopulationBinding(
        ExactTypeAssemblyIdentity assembly,
        ExactTypeDefinitionIdentity type,
        ApiSurfaceScope scope)
        : this(
            new TypeInspectionTypeIdentity(
                assembly,
                type),
            scope)
    {
    }

    public TypeMemberPopulationBinding(
        TypeInspectionTypeIdentity subject,
        ApiSurfaceScope scope)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        Scope = scope;
    }

    public TypeInspectionTypeIdentity Subject { get; }

    public ApiSurfaceScope Scope { get; }

    public bool Equals(TypeMemberPopulationBinding? other) =>
        other is not null
        && Scope == other.Scope
        && Subject.Equals(other.Subject);

    public override bool Equals(object? obj) =>
        Equals(obj as TypeMemberPopulationBinding);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Subject);
        hash.Add(Scope);
        return hash.ToHashCode();
    }
}

public enum TypeMembersUnavailableReason
{
    SubjectUnavailable,
}

public enum TypeMembersIncompleteReason
{
    SubjectInspectionIncomplete,
}

public enum TypeMembersFailure
{
    SubjectIdentityUnavailable,
    DeclaringTypeUnavailable,
    DeclaringMemberUnavailable,
}

/// <summary>Typed semantic-selection rejection from QuerySpace execution.</summary>
public sealed record TypeMembersSelectionFailure(
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

/// <summary>The terminal result for one requested Type Member population.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeMembersOutcome.NotRequested), "not-requested")]
[JsonDerivedType(typeof(TypeMembersOutcome.Rows), "rows")]
[JsonDerivedType(typeof(TypeMembersOutcome.Count), "count")]
[JsonDerivedType(typeof(TypeMembersOutcome.Unavailable), "unavailable")]
[JsonDerivedType(typeof(TypeMembersOutcome.Incomplete), "incomplete")]
[JsonDerivedType(typeof(TypeMembersOutcome.Rejected), "rejected")]
[JsonDerivedType(typeof(TypeMembersOutcome.Failed), "failed")]
public abstract record TypeMembersOutcome
{
    private protected TypeMembersOutcome()
    {
    }

    public sealed record NotRequested : TypeMembersOutcome;

    public sealed record Rows : TypeMembersOutcome
    {
        public Rows(
            TypeMemberPopulationBinding binding,
            ImmutableArray<TypeMemberRow> items)
        {
            Binding = binding
                ?? throw new ArgumentNullException(nameof(binding));
            if (items.IsDefault
                || items.Any(static item => item is null))
            {
                throw new ArgumentException(
                    "Type Member Rows require initialized non-null items.",
                    nameof(items));
            }

            Items = items;
        }

        public TypeMemberPopulationBinding Binding { get; }

        public ImmutableArray<TypeMemberRow> Items { get; }
    }

    public sealed record Count : TypeMembersOutcome
    {
        public Count(
            TypeMemberPopulationBinding binding,
            int value)
        {
            Binding = binding
                ?? throw new ArgumentNullException(nameof(binding));
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public TypeMemberPopulationBinding Binding { get; }

        public int Value { get; }
    }

    public sealed record Unavailable(TypeMembersUnavailableReason Reason)
        : TypeMembersOutcome;

    public sealed record Incomplete(TypeMembersIncompleteReason Reason)
        : TypeMembersOutcome;

    public sealed record Rejected(TypeMembersSelectionFailure Failure)
        : TypeMembersOutcome;

    public sealed record Failed(TypeMembersFailure Reason)
        : TypeMembersOutcome;
}

/// <summary>One QuerySpace-resolved Type Members request.</summary>
public sealed class TypeMembersQueryPlan
{
    internal TypeMembersQueryPlan(
        QuerySpaceRequest request,
        PortableQueryIntent rowIntent,
        ResolvedRowQueryPlan<TypeMemberRow> rowPlan)
    {
        Request = request;
        RowIntent = rowIntent;
        RowPlan = rowPlan;
    }

    public QuerySpaceRequest Request { get; }

    public PortableQueryIntent RowIntent { get; }

    internal ResolvedRowQueryPlan<TypeMemberRow> RowPlan { get; }
}

public enum TypeMembersQueryRequestRejectionKind
{
    QuerySpaceMismatch,
    ParticipatingRowSetsMismatch,
    RowIntentAssociationMismatch,
    QueryShapeMismatch,
    TerminalMismatch,
    ResultContractMismatch,
}

/// <summary>Resource-free row-intent failure for one Type Members request.</summary>
public sealed record TypeMembersQueryIntentRejection(
    RowQueryOperationKind OperationKind,
    int OperationPosition,
    int? TermPosition,
    int? SemanticStageNumber,
    RowQueryFailureReason Reason)
{
    internal static TypeMembersQueryIntentRejection From(
        RowQueryFailure failure) =>
        new(
            failure.OperationKind,
            failure.OperationPosition,
            failure.TermPosition,
            failure.SemanticStageNumber,
            failure.Reason);
}

/// <summary>The result of validating and resolving one Members request.</summary>
public abstract record TypeMembersQueryRequestResult
{
    private TypeMembersQueryRequestResult()
    {
    }

    public sealed record Accepted(TypeMembersQueryPlan Plan)
        : TypeMembersQueryRequestResult;

    public sealed record Rejected(
        TypeMembersQueryRequestRejectionKind Kind)
        : TypeMembersQueryRequestResult;

    public sealed record IntentRejected(
        TypeMembersQueryIntentRejection Failure)
        : TypeMembersQueryRequestResult;
}

/// <summary>
/// QuerySpace registration and executable row vocabulary for a Type's
/// canonical Member population.
/// </summary>
public static class TypeInspectionMembersQuery
{
    public const string OperationIdentity = "type-inspection";
    public const string OperationRouteIdentity =
        "type-inspection/type";
    public const string OperationSubjectRole = "exact-type";
    public const string OperationResultGrain = "type-member";
    public const string OperationProfileIdentity = "members";
    public const string OperationVocabularyIdentity =
        "type-inspection/operation/v1";
    public const string MembersRowSet = "members";
    public const string QuerySpaceIdentity =
        "type-inspection/members/query-space/v1";
    public const string MembersRowScopeIdentity =
        "type-inspection/members/rows/v1";
    public const string MembersRowVocabularyIdentity =
        "type-inspection/members/row-vocabulary/v1";
    public const string ReceiverFacetIdentity =
        "type-inspection/members/facet/receiver";
    public const string ReceiverTermKey = "receiver";
    public const string ReceiverValueVocabularyIdentity =
        "type-inspection/members/receiver/v1";
    public const string RowsResultContractIdentity =
        "type-inspection/members/rows-result/v1";
    public const string CountResultContractIdentity =
        "type-inspection/members/count-result/v1";

    public const string StaticReceiverValue = "static";
    public const string ThisReceiverValue = "this";
    public const string ExtensionReceiverValue = "extension";

    private static readonly RowQueryVocabulary<TypeMemberRow>
        MembersVocabulary =
            RowQueryVocabulary<TypeMemberRow>.Create(
                RowQueryVocabularyIdentity.Create(),
                [ReceiverKey()],
                []);

    private static readonly Lazy<QuerySpaceBinding> QuerySpaceValue =
        new(CreateQuerySpace);

    public static IQueryOperationRoute OperationRoute =>
        OperationRegistration.Route;

    public static QuerySpaceBinding QuerySpace => QuerySpaceValue.Value;

    public static QuerySpaceRowScopeBinding<TypeMemberRow>
        MembersRowScope { get; } =
            new(
                new QuerySpaceRowScopeDescriptor(
                    MembersRowScopeIdentity,
                    MembersRowVocabularyIdentity,
                    [MembersRowSet],
                    [
                        new(
                            ReceiverFacetIdentity,
                            ReceiverTermKey,
                            [
                                PortableQueryOperator.Equal,
                                PortableQueryOperator.NotEqual,
                            ],
                            "Type Member receiver classification",
                            ReceiverValueVocabularyIdentity,
                            "receiver",
                            [
                                StaticReceiverValue,
                                ThisReceiverValue,
                                ExtensionReceiverValue,
                            ],
                            "Selects ordinary static, instance, or "
                                + "extension Member declarations.",
                            supportsOrdering: false),
                    ],
                    [],
                    [
                        RowSelectionStageKind.Head,
                        RowSelectionStageKind.Tail,
                        RowSelectionStageKind.Window,
                    ]),
                MembersVocabulary);

    public static PortableQueryIntent CreateRowIntent(
        TypeMemberReceiver receiver,
        bool exclude = false)
    {
        if (!Enum.IsDefined(receiver))
            throw new ArgumentOutOfRangeException(nameof(receiver));

        return PortableQueryIntent.Create(
            [
                new(
                    ReceiverTermKey,
                    exclude
                        ? PortableQueryOperator.NotEqual
                        : PortableQueryOperator.Equal,
                    ReceiverValue(receiver)),
            ],
            [],
            [],
            []);
    }

    public static QuerySpaceRequest CreateRequest(
        QuerySpaceTerminalRequirement terminal,
        PortableQueryIntent? rowIntent = null)
    {
        if (terminal is not QuerySpaceTerminalRequirement.Rows
            and not QuerySpaceTerminalRequirement.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(terminal));
        }

        return QuerySpaceRequest.Create(
            QuerySpace.Descriptor,
            EmptyOperationIntent(),
            [MembersRowSet],
            [
                new(
                    MembersRowScopeIdentity,
                    rowIntent ?? EmptyRowIntent(),
                    [MembersRowSet]),
            ],
            terminal);
    }

    public static TypeMembersQueryRequestResult ResolveRequest(
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
            return new TypeMembersQueryRequestResult.Rejected(
                TypeMembersQueryRequestRejectionKind.QuerySpaceMismatch);
        }
        if (request.ParticipatingRowSets.Count != 1
            || !string.Equals(
                request.ParticipatingRowSets[0],
                MembersRowSet,
                StringComparison.Ordinal))
        {
            return new TypeMembersQueryRequestResult.Rejected(
                TypeMembersQueryRequestRejectionKind
                    .ParticipatingRowSetsMismatch);
        }
        if (request.RowIntents.Count != 1
            || !string.Equals(
                request.RowIntents[0].Scope,
                MembersRowScopeIdentity,
                StringComparison.Ordinal)
            || request.RowIntents[0].RowSets.Count != 1
            || !string.Equals(
                request.RowIntents[0].RowSets[0],
                MembersRowSet,
                StringComparison.Ordinal))
        {
            return new TypeMembersQueryRequestResult.Rejected(
                TypeMembersQueryRequestRejectionKind
                    .RowIntentAssociationMismatch);
        }
        if (request.Terminal is not QuerySpaceTerminalRequirement.Rows
            and not QuerySpaceTerminalRequirement.Count)
        {
            return new TypeMembersQueryRequestResult.Rejected(
                TypeMembersQueryRequestRejectionKind.TerminalMismatch);
        }
        string expectedResultContract =
            request.Terminal is QuerySpaceTerminalRequirement.Rows
                ? RowsResultContractIdentity
                : CountResultContractIdentity;
        if (!string.Equals(
                request.ResultContract,
                expectedResultContract,
                StringComparison.Ordinal))
        {
            return new TypeMembersQueryRequestResult.Rejected(
                TypeMembersQueryRequestRejectionKind
                    .ResultContractMismatch);
        }
        QuerySpaceRowIntentAssociation association =
            request.RowIntents[0];
        if (request.Operation.Terms.Count != 0
            || request.Operation.Bounds.Count != 0
            || request.Operation.Order.Count != 0
            || request.Operation.Stages.Count != 0
            || association.Intent.Terms.Any(
                static term =>
                    !string.Equals(
                        term.Key,
                        ReceiverTermKey,
                        StringComparison.Ordinal)
                    || term.Operator is not PortableQueryOperator.Equal
                        and not PortableQueryOperator.NotEqual)
            || association.Intent.Order.Count != 0
            || association.Intent.Stages.Any(
                static stage =>
                    stage.Kind is not RowSelectionStageKind.Head
                        and not RowSelectionStageKind.Tail
                        and not RowSelectionStageKind.Window))
        {
            return new TypeMembersQueryRequestResult.Rejected(
                TypeMembersQueryRequestRejectionKind.QueryShapeMismatch);
        }

        PortableQueryResolution<OperationPlan> operationResolution =
            OperationRegistration.Route.Resolve(
                request.Operation,
                cancellationToken);
        if (!operationResolution.IsResolved)
        {
            throw new InvalidOperationException(
                "A structurally valid Type Members request did not resolve "
                    + "through its empty operation vocabulary.");
        }

        RowQueryResolutionResult<TypeMemberRow> rowResolution =
            MembersRowScope.Resolve(association.Intent);
        return rowResolution.IsSuccess
            ? new TypeMembersQueryRequestResult.Accepted(
                new(
                    request,
                    association.Intent,
                    rowResolution.Plan
                        ?? throw new InvalidOperationException(
                            "A successful Type Members row resolution "
                                + "requires one plan.")))
            : new TypeMembersQueryRequestResult.IntentRejected(
                TypeMembersQueryIntentRejection.From(
                    rowResolution.Failure
                        ?? throw new InvalidOperationException(
                            "A failed Type Members row resolution requires "
                                + "one failure.")));
    }

    private static QuerySpaceBinding CreateQuerySpace() =>
        QuerySpaceBinding.Create(
            QuerySpaceIdentity,
            OperationRoute,
            [MembersRowScope],
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

    private static RowQueryKey<TypeMemberRow> ReceiverKey() =>
        RowQueryKey<TypeMemberRow>.Create(
            RowQueryKeyIdentity.Create(),
            ReceiverTermKey,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            static row =>
                RowQueryValue<TypeMemberReceiver>.Present(row.Receiver),
            BindReceiver);

    private static Predicate<TypeMemberReceiver>? BindReceiver(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        if (!TryParseReceiver(token.Text, out TypeMemberReceiver receiver))
            return null;

        return operation switch
        {
            RowQueryOperator.Equals => value => value == receiver,
            RowQueryOperator.NotEquals => value => value != receiver,
            _ => null,
        };
    }

    private static bool TryParseReceiver(
        string value,
        out TypeMemberReceiver receiver)
    {
        receiver = value switch
        {
            StaticReceiverValue => TypeMemberReceiver.Static,
            ThisReceiverValue => TypeMemberReceiver.This,
            ExtensionReceiverValue => TypeMemberReceiver.Extension,
            _ => default,
        };
        return value is StaticReceiverValue
            or ThisReceiverValue
            or ExtensionReceiverValue;
    }

    private static string ReceiverValue(TypeMemberReceiver receiver) =>
        receiver switch
        {
            TypeMemberReceiver.Static => StaticReceiverValue,
            TypeMemberReceiver.This => ThisReceiverValue,
            TypeMemberReceiver.Extension => ExtensionReceiverValue,
            _ => throw new InvalidOperationException(
                "Unknown Type Member receiver."),
        };

    private static PortableQueryIntent EmptyOperationIntent() =>
        PortableQueryIntent.Create([], [], [], []);

    private static PortableQueryIntent EmptyRowIntent() =>
        PortableQueryIntent.Create([], [], [], []);

    private sealed record OperationPredicate;

    private sealed record OperationPlan;

    private sealed class OperationVocabulary :
        PortableQueryVocabulary<OperationPredicate, OperationPlan>
    {
        public override string Identity => OperationVocabularyIdentity;

        public override bool TryGetKey(
            string key,
            [NotNullWhen(true)]
            out PortableQueryKeyDeclaration<OperationPredicate>? declaration)
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
                        [MembersRowSet],
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
                        [MembersRowSet],
                        OperationProfileIdentity,
                        [],
                        []);
    }
}
