using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal static class GeneratedFrameworkTypeAnalysis
{
    internal static IReadOnlySet<string> Collect(
        ImmutableArray<DirectCall> directCalls,
        ImmutableArray<MethodIdentity> methods)
    {
        var generated = new HashSet<string>(StringComparer.Ordinal);

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
            {
                generated.Add(
                    call.Caller.DeclaringType.ToQualifiedDisplayString());
            }
        }

        var typesCallingGrpcCore =
            new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in directCalls)
        {
            if (call.Callee.Kind == MemberKind.Unsupported)
                continue;
            if (IsGrpcCoreNamespace(
                    NamedDefinition(call.Callee.DeclaringType).Namespace))
            {
                typesCallingGrpcCore.Add(
                    call.Caller.DeclaringType.ToQualifiedDisplayString());
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
                var typeName =
                    method.DeclaringType.ToQualifiedDisplayString();
                if (typesCallingGrpcCore.Contains(typeName))
                    generated.Add(typeName);
            }
        }

        return generated;
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

    // Only a canonical trailing `N is an arity suffix; MetadataNameArity owns that
    // rule, so a literal backtick does not make an unrelated name match a
    // framework one.
    static string StripGenericArity(string name)
        => MetadataNameArity.StripFromNestedName(name);
}
