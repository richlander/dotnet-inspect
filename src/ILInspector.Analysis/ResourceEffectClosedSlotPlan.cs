using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

// Unlike member correspondence, application identity never opens the declaring
// type or discards its substituted signature. Method parameters remain symbolic.
internal sealed class ResourceEffectClosedSlotPlan
{
    readonly MemberRef _member;
    readonly PlannedType _declaring;
    readonly PlannedType _return;
    readonly ImmutableArray<PlannedType> _parameters;
    readonly ImmutableArray<MemberCorrespondenceFailure> _failures;
    readonly ImmutableArray<ResolvedResourceEffectGenericScope?> _declaringScopes;
    readonly ImmutableArray<ResolvedResourceEffectGenericScope?> _parameterScopes;
    readonly ImmutableArray<ResolvedResourceEffectGenericScope?> _returnScopes;

    internal ResourceEffectClosedSlotPlan(
        ResolvedAssemblyReference source,
        MemberRef member,
        IReadOnlyDictionary<TypeRef, ResolvedAssemblyReference>? origins = null,
        DirectCallGenericScopeOwners? genericScopes = null)
    {
        _member = member;
        var failures = ImmutableArray.CreateBuilder<MemberCorrespondenceFailure>();
        var planner = new CatalogMemberCorrespondencePlanner(source, failures, origins);
        _declaring = planner.Plan(member.DeclaringType, 0);
        _return = planner.Plan(member.ReturnType, 0);
        _parameters = [.. member.ParameterTypes.Select(type => planner.Plan(type, 0))];
        Requests = [.. planner.Requests];
        _failures = failures.ToImmutable();
        _declaringScopes = Scopes([member.DeclaringType]);
        _parameterScopes = Scopes(member.ParameterTypes);
        _returnScopes = Scopes([member.ReturnType]);

        ImmutableArray<ResolvedResourceEffectGenericScope?> Scopes(IEnumerable<TypeRef> types)
        {
            var result = ImmutableArray.CreateBuilder<ResolvedResourceEffectGenericScope?>();
            foreach (TypeRef type in types)
                Visit(type);
            return result.ToImmutable();

            void Visit(TypeRef type)
            {
                if (type.Kind is TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter)
                {
                    bool method = type.Kind == TypeRefKind.MethodGenericParameter;
                    result.Add(origins?.ContainsKey(type) == true && genericScopes is not null
                        ? new ResolvedResourceEffectGenericScope(
                            method ? ResourceEffectGenericVariableKind.Method : ResourceEffectGenericVariableKind.Type,
                            method ? genericScopes.Method : genericScopes.Type)
                        : null);
                }
                if (type.ElementType is { } element)
                    Visit(element);
                foreach (TypeRef argument in type.TypeArguments)
                    Visit(argument);
                if (type.ModifierType is { } modifier)
                    Visit(modifier);
                if (type.UnmodifiedType is { } unmodified)
                    Visit(unmodified);
                if (type.FunctionPointerSignature is { } signature)
                {
                    Visit(signature.ReturnType);
                    foreach (TypeRef parameter in signature.ParameterTypes)
                        Visit(parameter);
                }
            }
        }
    }

    internal ImmutableArray<TypeResolutionRequest> Requests { get; }

    internal ClosedSlotProjection Project(TypeResolutionContext context)
    {
        if (!_failures.IsEmpty)
            return new ClosedSlotProjection.Incomplete(_failures);
        var projector = new CatalogMemberJoinProjector(
            context, [.. Requests.Select(context.Resolve)]);
        CatalogTypeShape? declaring = projector.Project(_declaring);
        CatalogTypeShape? result = projector.Project(_return);
        var parameters = _parameters.Select(projector.Project).ToArray();
        if (declaring is null || result is null
            || parameters.Any(type => type is null)
            || projector.Failures.Count > 0)
        {
            return new ClosedSlotProjection.Incomplete(projector.Failures.ToImmutable());
        }
        return new ClosedSlotProjection.Issued(new ClosedSlotKey(
            projector.Evidence.Count == 0
                ? CatalogMemberCorrespondenceKind.Exact
                : CatalogMemberCorrespondenceKind.Indeterminate,
            declaring, _member.Name, _member.Kind, _member.GenericArity,
            _member.HasThis, _member.SignatureHeader,
            _member.RequiredParameterCount,
            [.. parameters.Select(type => type!)], result,
            _declaringScopes, _parameterScopes, _returnScopes));
    }
}

internal sealed record ClosedSlotKey(
    CatalogMemberCorrespondenceKind Kind,
    CatalogTypeShape DeclaringType,
    string Name,
    MemberKind MemberKind,
    int GenericArity,
    bool HasThis,
    byte SignatureHeader,
    int RequiredParameterCount,
    ImmutableArray<CatalogTypeShape> ParameterTypes,
    CatalogTypeShape ReturnType,
    ImmutableArray<ResolvedResourceEffectGenericScope?> DeclaringScopes,
    ImmutableArray<ResolvedResourceEffectGenericScope?> ParameterScopes,
    ImmutableArray<ResolvedResourceEffectGenericScope?> ReturnScopes);

internal abstract record ClosedSlotProjection
{
    internal sealed record Issued(ClosedSlotKey Key) : ClosedSlotProjection;
    internal sealed record Incomplete(
        ImmutableArray<MemberCorrespondenceFailure> Failures) : ClosedSlotProjection;
}
