using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Analysis;

internal static class MemberResolver
{
    public static MemberRef ResolveMethod(MetadataReader reader, EntityHandle handle, GenericScope callerScope)
    {
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
            {
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var declaringType = reader.GetTypeDefinition(method.GetDeclaringType());
                var declaring = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, method.GetDeclaringType(), 0);
                var typeScope = new GenericScope(
                    GenericParameterNames(reader, declaringType.GetGenericParameters()),
                    GenericParameterNames(reader, method.GetGenericParameters()));
                var signature = method.DecodeSignature(TypeRefDecoder.Instance, typeScope);
                string name = reader.GetString(method.Name);
                return new MemberRef(declaring, name, signature.ParameterTypes, signature.ReturnType, KindFor(name))
                {
                    OpenParameterTypes = signature.ParameterTypes,
                    OpenReturnType = signature.ReturnType,
                    HasThis = signature.Header.IsInstance,
                };
            }
            case HandleKind.MemberReference:
            {
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                var declaring = ResolveParentType(reader, member.Parent, callerScope);
                var signature = member.DecodeMethodSignature(TypeRefDecoder.Instance, GenericScope.Empty);
                var typeArguments = declaring.Kind == TypeRefKind.GenericInstance ? declaring.TypeArguments : [];
                string name = reader.GetString(member.Name);
                return new MemberRef(
                    declaring,
                    name,
                    [.. signature.ParameterTypes.Select(p => p.Instantiate(typeArguments, []))],
                    signature.ReturnType.Instantiate(typeArguments, []),
                    KindFor(name))
                {
                    // Raw signature with VAR/MVAR markers, before instantiation (#1731).
                    OpenParameterTypes = signature.ParameterTypes,
                    OpenReturnType = signature.ReturnType,
                    HasThis = signature.Header.IsInstance,
                };
            }
            case HandleKind.MethodSpecification:
            {
                var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                var generic = ResolveMethod(reader, spec.Method, callerScope);
                var methodArguments = spec.DecodeSignature(TypeRefDecoder.Instance, callerScope);
                return generic with
                {
                    TypeArguments = methodArguments,
                    ReturnType = generic.ReturnType.Instantiate([], methodArguments),
                    ParameterTypes = [.. generic.ParameterTypes.Select(p => p.Instantiate([], methodArguments))],
                    // OpenParameterTypes / OpenReturnType are inherited from `generic` (raw
                    // markers), not instantiated, so the open generic-method shape is
                    // preserved (#1731, #1741).
                };
            }
            default:
                return MemberRef.Unsupported($"callee handle kind {handle.Kind}");
        }
    }

    static MemberKind KindFor(string name) => name is ".ctor" or ".cctor" ? MemberKind.Constructor : MemberKind.Method;

    static TypeRef ResolveParentType(MetadataReader reader, EntityHandle parent, GenericScope callerScope) => parent.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)parent, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)parent, 0),
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, callerScope, (TypeSpecificationHandle)parent, 0),
        _ => TypeRef.Unsupported($"member parent kind {parent.Kind}"),
    };

    static ImmutableArray<string> GenericParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }
}
