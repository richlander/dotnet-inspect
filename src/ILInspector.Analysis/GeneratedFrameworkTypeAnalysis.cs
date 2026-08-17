using System.Collections.Immutable;

namespace ILInspector.Analysis;

internal static class GeneratedFrameworkTypeAnalysis
{
    internal static IReadOnlySet<TypeRef> Collect(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<MethodIdentity> methods)
    {
        var generated = new HashSet<TypeRef>();

        foreach (var call in directCalls)
        {
            var callee = call.Callee;
            if (callee.Kind == MemberKind.Unsupported)
                continue;
            bool protobufBootstrap =
                (IsProtobufType(
                        callee.DeclaringType,
                        "Google.Protobuf.Reflection",
                        "FileDescriptor")
                    && callee.Name == "FromGeneratedCode")
                || (IsProtobufType(
                        callee.DeclaringType,
                        "Google.Protobuf.Reflection",
                        "GeneratedClrTypeInfo")
                    && callee.Name == ".ctor")
                || (IsProtobufType(
                        callee.DeclaringType,
                        "Google.Protobuf",
                        "MessageParser")
                    && callee.Name == ".ctor");
            if (protobufBootstrap)
                generated.Add(NamedDefinition(call.Caller.DeclaringType));
        }

        var typesCallingGrpcCore = new HashSet<TypeRef>();
        foreach (var call in directCalls)
        {
            if (call.Callee.Kind == MemberKind.Unsupported)
                continue;
            if (IsGrpcCoreNamespace(
                    NamedDefinition(call.Callee.DeclaringType).Namespace))
            {
                typesCallingGrpcCore.Add(
                    NamedDefinition(call.Caller.DeclaringType));
            }
        }

        foreach (var method in methods)
        {
            if (method.Name == "__ServiceName"
                || method.Name.StartsWith("__Helper_", StringComparison.Ordinal)
                || method.Name.StartsWith(
                    "__Marshaller_",
                    StringComparison.Ordinal)
                || method.Name.StartsWith("__Method_", StringComparison.Ordinal))
            {
                var declaring = NamedDefinition(method.DeclaringType);
                if (typesCallingGrpcCore.Contains(declaring))
                    generated.Add(declaring);
            }
        }

        return generated;
    }

    /// <summary>
    /// True when <paramref name="type"/> is a classified generated-framework
    /// type, or a metadata nested type of one. Walks <c>+</c> containing-type
    /// names; does not parse qualified display text.
    /// </summary>
    internal static bool Contains(IReadOnlySet<TypeRef> generated, TypeRef type)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentNullException.ThrowIfNull(type);

        var current = NamedDefinition(type);
        if (generated.Contains(current))
            return true;

        if (current.Kind != TypeRefKind.Definition)
            return false;

        string name = current.Name;
        while (true)
        {
            int plus = name.LastIndexOf('+');
            if (plus <= 0)
                return false;

            name = name[..plus];
            if (generated.Contains(
                    TypeRef.Definition(current.Assembly, current.Namespace, name)))
            {
                return true;
            }
        }
    }

    static TypeRef NamedDefinition(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is { } element
                ? element
                : type;

    static bool IsGrpcCoreNamespace(string? ns)
        => ns is not null
            && (ns == "Grpc.Core"
                || ns.StartsWith("Grpc.Core.", StringComparison.Ordinal));

    static bool IsProtobufType(TypeRef type, string ns, string name)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType
            : type;
        return definition is not null
            && definition.TrustedProtobufAssembly
            && definition.Assembly == "Google.Protobuf"
            && definition.Namespace == ns
            && StripGenericArity(definition.Name) == name;
    }

    static string StripGenericArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }
}
