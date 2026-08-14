using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class CatalogMemberJoinProjector
{
    readonly TypeResolutionContext _context;
    readonly TypeResolutionOutcome[] _outcomes;
    readonly HashSet<int> _failedRequests = [];
    readonly HashSet<DefinitionJoinToken> _duplicateTokens = [];
    readonly HashSet<(
        UnresolvedBindingKey Binding,
        MetadataTypeDefinitionName Type)> _unresolvedBindings = [];

    internal CatalogMemberJoinProjector(
        TypeResolutionContext context,
        TypeResolutionOutcome[] outcomes)
    {
        _context = context;
        _outcomes = outcomes;
    }

    internal ImmutableArray<MemberCorrespondenceFailure>.Builder Failures
        { get; } =
            ImmutableArray.CreateBuilder<MemberCorrespondenceFailure>();
    internal ImmutableArray<MemberCorrespondenceEvidence>.Builder Evidence
        { get; } =
            ImmutableArray.CreateBuilder<MemberCorrespondenceEvidence>();

    internal CatalogTypeShape? Project(PlannedType planned)
    {
        switch (planned.Kind)
        {
            case PlannedTypeKind.Invalid:
                return null;
            case PlannedTypeKind.Named:
                return ProjectNamed(
                    planned.RequestIndex,
                    planned.TypeName!);
            case PlannedTypeKind.GenericInstance:
            {
                CatalogTypeShape? definition =
                    Project(planned.ElementType!);
                ImmutableArray<CatalogTypeShape>? components =
                    ProjectMany(planned.Components);
                return definition is null || components is null
                    ? null
                    : CatalogTypeShape.GenericInstance(
                        definition,
                        components.Value);
            }
            case PlannedTypeKind.SzArray:
            case PlannedTypeKind.Array:
            case PlannedTypeKind.ByRef:
            case PlannedTypeKind.Pointer:
            case PlannedTypeKind.Pinned:
            {
                CatalogTypeShape? element =
                    Project(planned.ElementType!);
                return element is null
                    ? null
                    : CatalogTypeShape.Unary(
                        ToCatalogKind(planned.Kind),
                        element,
                        planned.Rank);
            }
            case PlannedTypeKind.Modified:
            {
                CatalogTypeShape? modifier =
                    Project(planned.Components[0]);
                CatalogTypeShape? unmodified =
                    Project(planned.ElementType!);
                return modifier is null || unmodified is null
                    ? null
                    : CatalogTypeShape.Modified(
                        modifier,
                        unmodified,
                        planned.IsRequiredModifier);
            }
            case PlannedTypeKind.FunctionPointer:
            {
                CatalogTypeShape? returnType =
                    Project(planned.ElementType!);
                ImmutableArray<CatalogTypeShape>? parameters =
                    ProjectMany(planned.Components);
                return returnType is null || parameters is null
                    ? null
                    : CatalogTypeShape.FunctionPointer(
                        planned.SignatureHeader,
                        planned.GenericArity,
                        planned.RequiredParameterCount,
                        returnType,
                        parameters.Value);
            }
            case PlannedTypeKind.GenericParameter:
            case PlannedTypeKind.MethodGenericParameter:
                return CatalogTypeShape.GenericParameter(
                    planned.Kind
                        == PlannedTypeKind.MethodGenericParameter,
                    planned.GenericParameterIndex);
            default:
                throw new InvalidOperationException(
                    "Unknown planned type shape.");
        }
    }

    CatalogTypeShape? ProjectNamed(
        int requestIndex,
        MetadataTypeDefinitionName type)
    {
        TypeResolutionOutcome outcome = _outcomes[requestIndex];
        switch (outcome)
        {
            case TypeResolutionOutcome.Resolved resolved:
                return ProjectResolved(
                    requestIndex,
                    type,
                    resolved);
            case TypeResolutionOutcome.UnboundBinding unbound:
                return ProjectUnresolved(
                    requestIndex,
                    type,
                    unbound.Binding,
                    outcome);
            case TypeResolutionOutcome.Unavailable unavailable:
                return ProjectUnresolved(
                    requestIndex,
                    type,
                    unavailable.Binding,
                    outcome);
            case TypeResolutionOutcome.Rejected
                {
                    Failure:
                        TypeResolutionFailure.PlanExpansionRequired
                        expansion
                }:
                AddFailure(
                    requestIndex,
                    new MemberCorrespondenceFailure.ExpansionRequired(
                        expansion.Request));
                return null;
            default:
                AddFailure(
                    requestIndex,
                    new MemberCorrespondenceFailure.Resolution(outcome));
                return null;
        }
    }

    CatalogTypeShape? ProjectResolved(
        int requestIndex,
        MetadataTypeDefinitionName type,
        TypeResolutionOutcome.Resolved resolved)
    {
        switch (_context.ProjectDefinitionJoinToken(
            resolved.Definition.Key))
        {
            case DefinitionJoinTokenProjection.Issued issued:
                if (issued.Token.Kind
                        == DefinitionJoinKind
                            .IndeterminateDuplicateArtifact
                    && _duplicateTokens.Add(issued.Token))
                {
                    Evidence.Add(
                        new MemberCorrespondenceEvidence
                            .DuplicateArtifact(
                                type,
                                issued.Token.Evidence
                                ?? throw new InvalidOperationException(
                                    "Duplicate join token has no evidence.")));
                }
                return CatalogTypeShape.Resolved(issued.Token);
            case DefinitionJoinTokenProjection.IncomparableCatalogs:
                throw new InvalidOperationException(
                    "A context resolved currency from another catalog.");
            case DefinitionJoinTokenProjection.StaleGeneration stale:
                AddFailure(
                    requestIndex,
                    new MemberCorrespondenceFailure.StaleGeneration(
                        stale.DefinitionGeneration,
                        stale.CurrentGeneration));
                return null;
            default:
                throw new InvalidOperationException(
                    "Unknown definition-token projection.");
        }
    }

    CatalogTypeShape? ProjectUnresolved(
        int requestIndex,
        MetadataTypeDefinitionName type,
        UnresolvedBindingReference binding,
        TypeResolutionOutcome outcome)
    {
        switch (_context.ProjectUnresolvedBindingKey(binding))
        {
            case UnresolvedBindingKeyProjection.Issued issued:
                if (_unresolvedBindings.Add((issued.Key, type)))
                {
                    Evidence.Add(
                        new MemberCorrespondenceEvidence
                            .UnresolvedBinding(type, outcome));
                }
                return CatalogTypeShape.Degraded(
                    issued.Key,
                    type);
            case UnresolvedBindingKeyProjection.IncomparableCatalogs:
                throw new InvalidOperationException(
                    "A context resolved currency from another catalog.");
            case UnresolvedBindingKeyProjection.StaleGeneration stale:
                AddFailure(
                    requestIndex,
                    new MemberCorrespondenceFailure.StaleGeneration(
                        stale.BindingGeneration,
                        stale.CurrentGeneration));
                return null;
            default:
                throw new InvalidOperationException(
                    "Unknown unresolved-binding projection.");
        }
    }

    ImmutableArray<CatalogTypeShape>? ProjectMany(
        ImmutableArray<PlannedType> planned)
    {
        var builder =
            ImmutableArray.CreateBuilder<CatalogTypeShape>(
                planned.Length);
        foreach (PlannedType item in planned)
        {
            CatalogTypeShape? projected = Project(item);
            if (projected is not null)
                builder.Add(projected);
        }
        return builder.Count == planned.Length
            ? builder.MoveToImmutable()
            : null;
    }

    void AddFailure(
        int requestIndex,
        MemberCorrespondenceFailure failure)
    {
        if (_failedRequests.Add(requestIndex))
            Failures.Add(failure);
    }

    static CatalogTypeShapeKind ToCatalogKind(
        PlannedTypeKind kind) =>
        kind switch
        {
            PlannedTypeKind.SzArray =>
                CatalogTypeShapeKind.SzArray,
            PlannedTypeKind.Array =>
                CatalogTypeShapeKind.Array,
            PlannedTypeKind.ByRef =>
                CatalogTypeShapeKind.ByRef,
            PlannedTypeKind.Pointer =>
                CatalogTypeShapeKind.Pointer,
            PlannedTypeKind.Pinned =>
                CatalogTypeShapeKind.Pinned,
            _ => throw new InvalidOperationException(
                "Planned type is not unary."),
        };
}
