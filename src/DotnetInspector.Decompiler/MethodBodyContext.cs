using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Metadata;

namespace DotnetInspector.Decompiler;

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

    MethodBodyContext(
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
        GenericContext? genericContext = null)
    {
        ILBytes = ilBytes;
        ExceptionRegions = exceptionRegions;
        MaxStack = maxStack;
        LocalTypes = localTypes;
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
    public static MethodBodyContext? Create(PEReader peReader, MetadataReader reader, MethodDefinition method)
    {
        if (method.RelativeVirtualAddress == 0)
            return null;

        MethodBodyBlock body;
        try
        {
            body = peReader.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch
        {
            return null;
        }

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

        return new MethodBodyContext(
            ilBytes,
            body.ExceptionRegions,
            body.MaxStack,
            localTypes,
            reader,
            sig.ParameterTypes.Length,
            !method.Attributes.HasFlag(System.Reflection.MethodAttributes.Static),
            sig.ReturnType != "System.Void" && sig.ReturnType != "void",
            [.. sig.ParameterTypes],
            paramNames,
            sig.ReturnType,
            declaringType,
            genericContext);
    }

    /// <summary>
    /// Convenience: find a method by type/name and create context.
    /// </summary>
    public static MethodBodyContext? Create(PEReader peReader, string typeName, string methodName, int overloadIndex = 0, bool publicOnly = false)
    {
        var reader = peReader.GetMetadataReader();
        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);
            if (reader.GetFullTypeName(typeDef) != typeName)
                continue;

            int matchCount = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (publicOnly && (method.Attributes & System.Reflection.MethodAttributes.Public) == 0)
                    continue;
                if (matchCount == overloadIndex)
                    return Create(peReader, reader, method);
                matchCount++;
            }
        }

        return null;
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
}
