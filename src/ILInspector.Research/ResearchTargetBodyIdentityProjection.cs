using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

static class ResearchTargetBodyIdentityProjection
{
    internal static bool TryCreate(
        MethodIdentity method,
        ResolvedMemberTarget target,
        ResearchTargetRelationshipRole role,
        out MethodBodyIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(target);

        ImmutableArray<TypeRef> parameterTypes = method.ParameterTypes;
        TypeRef returnType = method.ReturnType;
        string name = method.Name;
        if (role
            is ResearchTargetRelationshipRole.Getter
                or ResearchTargetRelationshipRole.Setter
                or ResearchTargetRelationshipRole.Adder
                or ResearchTargetRelationshipRole.Remover)
        {
            name = target.ApiMember.Member.Name;
        }
        if (role == ResearchTargetRelationshipRole.Setter)
        {
            if (parameterTypes.IsEmpty)
            {
                identity = null;
                return false;
            }
            returnType = parameterTypes[^1];
            parameterTypes = parameterTypes.RemoveAt(parameterTypes.Length - 1);
        }

        return MethodBodyIdentityFactory.TryCreate(
            method.DeclaringType,
            name,
            method.GenericArity,
            parameterTypes,
            returnType,
            method.IsExtension,
            out identity);
    }
}
