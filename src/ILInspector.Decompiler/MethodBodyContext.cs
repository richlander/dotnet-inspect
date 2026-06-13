using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.Decompiler;

/// <summary>
/// Bundles a method body's IL bytes, exception regions, local variable types,
/// and metadata context for analysis. Thin wrapper around <see cref="MethodBodyBlock"/>
/// and <see cref="MetadataReader"/> providing the interface the ported runtime
/// algorithms need.
/// </summary>
public sealed class MethodBodyContext
{
    public byte[] ILBytes { get; }
    public ImmutableArray<ExceptionRegion> ExceptionRegions { get; }
    public int MaxStack { get; }
    public IReadOnlyList<string> LocalTypes { get; }

    /// <summary>
    /// Local variable names from PDB debug info. Entries may be null for unnamed slots.
    /// </summary>
    public IReadOnlyList<string?> LocalNames { get; }

    public MetadataReader Reader { get; }

    /// <summary>
    /// Method signature parameter count (excluding return type).
    /// </summary>
    public int ParameterCount { get; }

    /// <summary>
    /// True if the method is an instance method (has implicit 'this' parameter).
    /// </summary>
    public bool HasThis { get; }

    /// <summary>
    /// True if the method has a non-void return type.
    /// </summary>
    public bool HasReturnValue { get; }

    /// <summary>
    /// The decoded type names of the method parameters (excluding 'this').
    /// </summary>
    public IReadOnlyList<string> ParameterTypes { get; }

    /// <summary>
    /// The parameter names from metadata (may be empty if stripped).
    /// </summary>
    public IReadOnlyList<string> ParameterNames { get; }

    /// <summary>
    /// The decoded return type name.
    /// </summary>
    public string ReturnType { get; }

    /// <summary>
    /// The declaring type name (for 'this' resolution in instance methods).
    /// </summary>
    public string? DeclaringType { get; }

    /// <summary>
    /// Generic context for resolving type/method generic parameters in signatures.
    /// </summary>
    public GenericContext? GenericContext { get; }

    /// <summary>
    /// The PE reader the context was created from, when available. Enables
    /// recursive decompilation (lambda targets); the caller owns its lifetime.
    /// </summary>
    public System.Reflection.PortableExecutable.PEReader? PE { get; init; }

    internal MethodBodyContext(
        byte[] ilBytes,
        ImmutableArray<ExceptionRegion> exceptionRegions,
        int maxStack,
        IReadOnlyList<string> localTypes,
        MetadataReader reader,
        int parameterCount,
        bool hasThis,
        bool hasReturnValue,
        IReadOnlyList<string> parameterTypes,
        IReadOnlyList<string> parameterNames,
        string returnType,
        string? declaringType = null,
        GenericContext? genericContext = null,
        IReadOnlyList<string?>? localNames = null)
    {
        ILBytes = ilBytes;
        ExceptionRegions = exceptionRegions;
        MaxStack = maxStack;
        LocalTypes = localTypes;
        LocalNames = localNames ?? [];
        Reader = reader;
        ParameterCount = parameterCount;
        HasThis = hasThis;
        HasReturnValue = hasReturnValue;
        ParameterTypes = parameterTypes;
        ParameterNames = parameterNames;
        ReturnType = returnType;
        DeclaringType = declaringType;
        GenericContext = genericContext;
    }

    /// <summary>
    /// Creates a <see cref="MethodBodyContext"/> from a PE reader and method definition.
    /// Returns null if the method has no IL body.
    /// </summary>
    public static MethodBodyContext? Create(PEReader peReader, MetadataReader reader, MethodDefinition method,
        MetadataReader? pdbReader = null, MethodDefinitionHandle methodHandle = default)
    {
        if (method.RelativeVirtualAddress == 0)
            return null;

        // A method that claims a body but whose body cannot be read is
        // corrupt input, not "no body" — let it throw so callers surface a
        // diagnostic instead of silently dropping sections.
        var body = peReader.GetMethodBody(method.RelativeVirtualAddress);

        var ilBytes = body.GetILContent().ToArray();

        // Build generic context from declaring type and method generic parameters
        GenericContext? genericContext = null;
        string? declaringType = null;
        try
        {
            var declTypeHandle = method.GetDeclaringType();
            if (!declTypeHandle.IsNil)
            {
                var typeDef = reader.GetTypeDefinition(declTypeHandle);
                declaringType = reader.GetFullTypeName(typeDef);
                genericContext = GenericContext.ForMethod(reader, typeDef, method);
            }
        }
        // Graceful fallback when generic context cannot be built from declaring type
        catch { }

        var localTypes = DecodeLocalTypes(reader, body.LocalSignature, genericContext);
        var sig = method.DecodeSignature(SignatureDecoder.Instance, genericContext);
        var paramNames = ReadParameterNames(reader, method);

        // Runtime async (.NET 11+ "async v2"): a method carrying MethodImplAttributes.Async
        // (0x2000) has no compiler state machine; its IL `ret` yields the *unwrapped* awaited
        // value, not the Task/ValueTask declared in metadata. Map the effective IL return type
        // accordingly (Task/ValueTask -> void; Task<T>/ValueTask<T> -> T) so stack simulation
        // and emitted `return` statements stay balanced.
        const System.Reflection.MethodImplAttributes AsyncImplFlag = (System.Reflection.MethodImplAttributes)0x2000;
        string effectiveReturnType = (method.ImplAttributes & AsyncImplFlag) != 0
            ? UnwrapRuntimeAsyncReturnType(sig.ReturnType)
            : sig.ReturnType;

        // Read PDB local variable names if available
        IReadOnlyList<string?>? localNames = null;
        if (pdbReader != null && !methodHandle.IsNil)
            localNames = ReadLocalNamesFromPdb(pdbReader, methodHandle, localTypes.Count);

        return new MethodBodyContext(
            ilBytes,
            body.ExceptionRegions,
            body.MaxStack,
            localTypes,
            reader,
            sig.ParameterTypes.Length,
            !method.Attributes.HasFlag(System.Reflection.MethodAttributes.Static),
            effectiveReturnType != "System.Void" && effectiveReturnType != "void",
            [.. sig.ParameterTypes],
            paramNames,
            effectiveReturnType,
            declaringType,
            genericContext,
            localNames)
        {
            PE = peReader,
        };
    }

    /// <summary>
    /// Unwraps a runtime-async method's declared return type to the type its IL actually
    /// returns: <c>Task</c>/<c>ValueTask</c> become <c>void</c>, and <c>Task&lt;T&gt;</c>/
    /// <c>ValueTask&lt;T&gt;</c> become <c>T</c>. Other return types are returned unchanged.
    /// </summary>
    internal static string UnwrapRuntimeAsyncReturnType(string returnType)
    {
        if (returnType is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
            return "System.Void";

        foreach (var wrapper in new[] { "System.Threading.Tasks.Task<", "System.Threading.Tasks.ValueTask<" })
        {
            if (returnType.StartsWith(wrapper, StringComparison.Ordinal)
                && returnType.EndsWith(">", StringComparison.Ordinal))
                return returnType[wrapper.Length..^1];
        }

        return returnType;
    }

    /// <summary>
    /// Convenience: find a method by type/name and create context.
    /// Reads local variable names from the embedded PDB when present; otherwise, when an
    /// <paramref name="externalPdbPath"/> to a portable PDB is supplied (e.g. one the CLI
    /// downloaded from a symbol server for platform assemblies), reads local names from there.
    /// </summary>
    public static MethodBodyContext? Create(PEReader peReader, string typeName, string methodName, int overloadIndex = 0, bool publicOnly = false, string? externalPdbPath = null)
    {
        var reader = peReader.GetMetadataReader();

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetFullTypeName(typeDef) != typeName)
                continue;

            return Create(peReader, reader, typeDefHandle, methodName, overloadIndex, publicOnly, externalPdbPath);
        }

        return null;
    }

    /// <summary>
    /// Overload for callers that have already resolved the declaring type handle, avoiding a repeated
    /// TypeDefinitions scan per method.
    /// </summary>
    public static MethodBodyContext? Create(
        PEReader peReader, MetadataReader reader, TypeDefinitionHandle typeHandle,
        string methodName, int overloadIndex = 0, bool publicOnly = false, string? externalPdbPath = null)
    {
        MetadataReader? pdbReader = TryGetEmbeddedPdbReader(peReader);

        // Fall back to an external portable PDB (e.g. acquired from a symbol server) when the
        // assembly has no embedded PDB. Local names are materialized eagerly inside Create, so the
        // provider/stream can be disposed as soon as the matching context has been built.
        FileStream? pdbStream = null;
        MetadataReaderProvider? externalProvider = null;
        try
        {
            if (pdbReader is null && !string.IsNullOrEmpty(externalPdbPath) && File.Exists(externalPdbPath))
            {
                try
                {
                    pdbStream = File.OpenRead(externalPdbPath);
                    externalProvider = MetadataReaderProvider.FromPortablePdbStream(pdbStream);
                    pdbReader = externalProvider.GetMetadataReader();
                }
                catch
                {
                    externalProvider?.Dispose();
                    externalProvider = null;
                    pdbStream?.Dispose();
                    pdbStream = null;
                    pdbReader = null;
                }
            }

            var typeDef = reader.GetTypeDefinition(typeHandle);
            int matchCount = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (publicOnly && (method.Attributes & System.Reflection.MethodAttributes.Public) == 0)
                    continue;
                if (matchCount == overloadIndex)
                    return Create(peReader, reader, method, pdbReader, methodHandle);
                matchCount++;
            }

            return null;
        }
        finally
        {
            externalProvider?.Dispose();
            pdbStream?.Dispose();
        }
    }

    static IReadOnlyList<string> ReadParameterNames(MetadataReader reader, MethodDefinition method)
    {
        var names = new List<string>();

        foreach (var paramHandle in method.GetParameters())
        {
            var param = reader.GetParameter(paramHandle);
            // Sequence 0 = return value parameter; skip it
            if (param.SequenceNumber == 0)
                continue;

            var name = reader.GetString(param.Name);
            names.Add(string.IsNullOrEmpty(name) ? $"P_{param.SequenceNumber - 1}" : name);
        }

        return names;
    }

    static List<string> DecodeLocalTypes(MetadataReader reader, StandaloneSignatureHandle sigHandle, GenericContext? genericContext)
    {
        if (sigHandle.IsNil)
            return [];

        try
        {
            var sig = reader.GetStandaloneSignature(sigHandle);
            var types = sig.DecodeLocalSignature(SignatureDecoder.Instance, genericContext);
            return [.. types];
        }
        catch
        {
            return [];
        }
    }

    static IReadOnlyList<string?> ReadLocalNamesFromPdb(MetadataReader pdbReader, MethodDefinitionHandle methodHandle, int localCount)
    {
        var names = new string?[localCount];
        try
        {
            foreach (var scopeHandle in pdbReader.GetLocalScopes(methodHandle))
            {
                var scope = pdbReader.GetLocalScope(scopeHandle);
                foreach (var varHandle in scope.GetLocalVariables())
                {
                    var variable = pdbReader.GetLocalVariable(varHandle);
                    if (variable.Index >= 0 && variable.Index < localCount)
                        names[variable.Index] = pdbReader.GetString(variable.Name);
                }
            }
        }
        catch { }
        return names;
    }

    /// <summary>
    /// Try to get a MetadataReader from an embedded portable PDB.
    /// The provider is not disposed — the PEReader owns the lifetime.
    /// </summary>
    static MetadataReader? TryGetEmbeddedPdbReader(PEReader peReader)
    {
        try
        {
            foreach (var entry in peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                    return peReader.ReadEmbeddedPortablePdbDebugDirectoryData(entry).GetMetadataReader();
            }
        }
        catch { }
        return null;
    }
}
