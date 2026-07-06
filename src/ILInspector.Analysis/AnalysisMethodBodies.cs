using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Analysis;

/// <summary>One method body, with everything a body-level correctness analyzer needs: the
/// method identity, its raw IL, its exception regions, and a metadata-token resolver.</summary>
internal readonly record struct AnalysisMethodBody(
    MethodIdentity Method,
    byte[] Il,
    ImmutableArray<ExceptionRegion> ExceptionRegions,
    Func<int, MemberRef> ResolveMethod);

/// <summary>
/// Shared, fail-closed SRM plumbing that walks an assembly's method bodies. Both the
/// <see cref="LeakTriageAnalyzer"/> finding path and the <see cref="ResourceLifecycleCensus"/>
/// measurement path enumerate exactly the same bodies with the same identities and the same
/// token resolver, so a body one path sees and the other misses can never be an enumeration
/// artifact. Per-method metadata failures are swallowed (a body that cannot be read is simply
/// not yielded); body-level analysis owns its own recoverable-failure handling.
/// </summary>
internal static class AnalysisMethodBodies
{
    public static IEnumerable<AnalysisMethodBody> Enumerate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var assembly = reader.GetAssemblyDefinition();
        var assemblyName = reader.GetString(assembly.Name);
        var mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var methodDef = reader.GetMethodDefinition(methodHandle);
                if (methodDef.RelativeVirtualAddress == 0)
                    continue;

                AnalysisMethodBody? body;
                try
                {
                    var scope = CreateScope(reader, typeDef, methodDef);
                    var signature = methodDef.DecodeSignature(TypeRefDecoder.Instance, scope);
                    var method = new MethodIdentity(
                        assemblyName,
                        mvid,
                        TypeRefDecoder.Instance.GetTypeFromDefinition(reader, typeHandle, 0),
                        reader.GetString(methodDef.Name),
                        signature.ParameterTypes,
                        signature.ReturnType,
                        MetadataTokens.GetToken(methodHandle),
                        (methodDef.Attributes & MethodAttributes.Static) != 0);
                    var methodBody = peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                    body = new AnalysisMethodBody(
                        method,
                        methodBody.GetILBytes() ?? [],
                        methodBody.ExceptionRegions,
                        token => MemberResolver.ResolveMethod(reader, MetadataTokens.EntityHandle(token), scope));
                }
                catch (Exception ex) when (IsRecoverable(ex))
                {
                    // Fail-closed: malformed or unsupported method metadata yields no body.
                    body = null;
                }

                if (body is { } value)
                    yield return value;
            }
        }
    }

    public static int ArgumentSlotCount(MethodIdentity method)
        => method.ParameterTypes.Length + (method.IsStatic ? 0 : 1);

    public static bool IsRecoverable(Exception ex)
        => ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException;

    static GenericScope CreateScope(MetadataReader reader, TypeDefinition typeDef, MethodDefinition methodDef)
        => new(GenericParameterNames(reader, typeDef.GetGenericParameters()), GenericParameterNames(reader, methodDef.GetGenericParameters()));

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
