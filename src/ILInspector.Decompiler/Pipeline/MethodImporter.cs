using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Imports one method from a live <see cref="MetadataSource"/> into fully
/// materialized data (docs/decompiler-ir.md): symbolic types throughout, no
/// metadata handles in the output, valid after the source is disposed.
/// </summary>
public static class MethodImporter
{
    /// <summary>
    /// Imports a method by metadata type name (<c>System.Collections.Generic.List`1</c>)
    /// and method name. Returns null when the type, method, or IL body does
    /// not exist — callers that need the reason use the harness/diagnostic
    /// paths; this is the mechanical front door.
    /// </summary>
    public static ImportedMethod? Import(MetadataSource source, string typeFullName, string methodName, int overloadIndex = 0)
    {
        var reader = source.Reader;
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetFullTypeName(typeDef) != typeFullName)
                continue;

            // Overload indices count every name match, body or not (parity
            // with legacy selection); a selected overload without a body is
            // null, not silently the next one with a body.
            int seen = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (seen++ != overloadIndex)
                    continue;
                return method.RelativeVirtualAddress == 0 ? null : Import(source, typeDefHandle, methodHandle);
            }
            return null;
        }
        return null;
    }

    internal static ImportedMethod Import(MetadataSource source, TypeDefinitionHandle typeDefHandle, MethodDefinitionHandle methodHandle)
    {
        var reader = source.Reader;
        var typeDef = reader.GetTypeDefinition(typeDefHandle);
        var method = reader.GetMethodDefinition(methodHandle);

        var scope = new GenericScope(
            ParameterNames(reader, typeDef.GetGenericParameters()),
            ParameterNames(reader, method.GetGenericParameters()));

        var declaringType = TypeRefDecoder.Instance.GetTypeFromDefinition(reader, typeDefHandle, 0);
        var decoded = method.DecodeSignature(TypeRefDecoder.Instance, scope);

        var parameters = ImmutableArray.CreateBuilder<Parameter>(decoded.ParameterTypes.Length);
        var namesByIndex = new Dictionary<int, string>();
        var hasDefaultByIndex = new Dictionary<int, bool>();
        foreach (var parameterHandle in method.GetParameters())
        {
            var parameter = reader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber > 0)
            {
                namesByIndex[parameter.SequenceNumber - 1] = reader.GetString(parameter.Name);
                hasDefaultByIndex[parameter.SequenceNumber - 1] = HasDefault(reader, parameter);
            }
        }
        for (int i = 0; i < decoded.ParameterTypes.Length; i++)
            parameters.Add(new Parameter(
                namesByIndex.GetValueOrDefault(i, $"arg{i}"),
                decoded.ParameterTypes[i],
                hasDefaultByIndex.GetValueOrDefault(i)));

        var signature = new MethodSignature(
            decoded.ReturnType,
            parameters.MoveToImmutable(),
            decoded.Header.IsInstance,
            decoded.GenericParameterCount);

        var body = source.Pe.GetMethodBody(method.RelativeVirtualAddress);

        var locals = ImmutableArray<TypeRef>.Empty;
        if (!body.LocalSignature.IsNil)
        {
            var localSignature = reader.GetStandaloneSignature(body.LocalSignature);
            locals = localSignature.DecodeLocalSignature(TypeRefDecoder.Instance, scope);
        }

        var handlers = ImmutableArray.CreateBuilder<HandlerRegion>(body.ExceptionRegions.Length);
        foreach (var region in body.ExceptionRegions)
        {
            handlers.Add(new HandlerRegion(
                region.Kind switch
                {
                    ExceptionRegionKind.Catch => HandlerKind.Catch,
                    ExceptionRegionKind.Filter => HandlerKind.Filter,
                    ExceptionRegionKind.Finally => HandlerKind.Finally,
                    _ => HandlerKind.Fault,
                },
                region.TryOffset,
                region.TryLength,
                region.HandlerOffset,
                region.HandlerLength,
                region.FilterOffset,
                CatchType(reader, region.CatchType, scope)));
        }

        var methodBody = new MethodBody(
            [.. body.GetILBytes() ?? []],
            body.MaxStack,
            locals,
            LocalNames: source.LocalNames(methodHandle, locals.Length),
            handlers.MoveToImmutable(),
            SkipLocalsInit: !body.LocalVariablesInitialized);

        return new ImportedMethod(
            declaringType,
            reader.GetString(method.Name),
            signature,
            methodBody,
            CompilerGenerated: FactState(MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, method.GetCustomAttributes())),
            DeclaringTypeCompilerGenerated: FactState(MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, typeDef.GetCustomAttributes())),
            MetadataToken: MetadataTokens.GetToken(methodHandle));
    }

    static MetadataFactState FactState(bool value) => value ? MetadataFactState.Yes : MetadataFactState.No;

    static bool HasDefault(MetadataReader reader, System.Reflection.Metadata.Parameter parameter)
    {
        if ((parameter.Attributes & System.Reflection.ParameterAttributes.HasDefault) != 0)
            return true;
        foreach (var handle in parameter.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (AttributeTypeFullName(reader, attribute) is
                "System.Runtime.CompilerServices.DecimalConstantAttribute" or
                "System.Runtime.CompilerServices.DateTimeConstantAttribute")
            {
                return true;
            }
        }
        return false;
    }

    static string AttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)member.Parent)),
                    HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent)),
                    _ => "",
                };
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return FullName(reader, reader.GetTypeDefinition(method.GetDeclaringType()));
            default:
                return "";
        }
    }

    static string FullName(MetadataReader reader, TypeReference type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static string FullName(MetadataReader reader, TypeDefinition type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static TypeRef? CatchType(MetadataReader reader, EntityHandle handle, GenericScope scope) => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
        HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
        HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(reader, scope, (TypeSpecificationHandle)handle, 0),
        _ => null,
    };

    static ImmutableArray<string> ParameterNames(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(reader.GetString(reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }
}
