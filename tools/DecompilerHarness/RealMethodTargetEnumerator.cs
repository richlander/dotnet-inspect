using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Decompiler.Pipeline;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Enumerates the "real" method targets of an assembly: body-bearing, named
/// methods that are neither compiler-generated nor <c>SpecialName</c>
/// accessors/operators/constructors. Property and event accessors, static and
/// instance constructors, and compiler-synthesized members are intentionally
/// excluded so the authored-source corpus is biased toward hand-written method
/// bodies rather than trivial generated shapes.
///
/// Each target carries the same identity coordinates that
/// <see cref="ReturnToSender.RequestedTarget"/> is matched against — the full
/// type name, method name, and 0-based overload ordinal computed exactly as
/// <c>ReturnToSender.TryFindMethod</c> computes it (counting every same-named
/// method in <see cref="TypeDefinition.GetMethods"/> order, regardless of RVA)
/// — plus a uniquely round-tripping signature when one exists and the raw
/// metadata token for direct SourceLink acquisition.
/// </summary>
static class RealMethodTargetEnumerator
{
    /// <summary>
    /// A single enumerated real-method target.
    /// </summary>
    /// <param name="Type">Full type name as produced by <c>GetFullTypeName</c>;
    /// the key <see cref="ReturnToSender.RequestedTarget.Type"/> is matched on.</param>
    /// <param name="Method">Method name.</param>
    /// <param name="Overload">0-based ordinal among same-named methods in
    /// <see cref="TypeDefinition.GetMethods"/> order.</param>
    /// <param name="Signature">Normalized signature that resolves uniquely among
    /// body-bearing same-named methods, or <c>null</c> when no such signature
    /// exists (fall back to the ordinal).</param>
    /// <param name="MetadataToken">Method-definition metadata token for direct
    /// SourceLink acquisition.</param>
    /// <param name="ParameterCount">Parameter count, used when extracting the
    /// authored member body.</param>
    /// <param name="IlSize">Method body IL byte length, used by difficulty
    /// scoring for the hard-IL corpus.</param>
    /// <param name="Difficulty">Structural IL difficulty profile plus composite
    /// score used to rank the hard-IL corpus.</param>
    internal sealed record RealMethodTarget(
        string Type,
        string Method,
        int Overload,
        string? Signature,
        int MetadataToken,
        int ParameterCount,
        int IlSize,
        IlDifficulty Difficulty)
    {
        public ReturnToSender.RequestedTarget ToRequestedTarget()
            => new(Type, Method, Overload, Signature);
    }

    /// <summary>
    /// Enumerates every real-method target in <paramref name="assemblyPath"/>.
    /// Types that are not classes or structs, the <c>&lt;Module&gt;</c> type, and
    /// compiler-generated types (names containing <c>'&lt;'</c>) are skipped so the
    /// results match what <c>ReturnToSender.CompileBackTargets</c> can resolve.
    /// </summary>
    public static IReadOnlyList<RealMethodTarget> Enumerate(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);

        var targets = new List<RealMethodTarget>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            throw new InvalidOperationException("Assembly has no metadata.");

        var reader = pe.GetMetadataReader();
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            string typeName = reader.GetString(typeDef.Name);
            if (typeName == "<Module>"
                || typeName.Contains('<', StringComparison.Ordinal)
                || source.ClassifyType((EntityHandle)typeHandle)
                    is not (TypeShapeKind.Class or TypeShapeKind.Struct))
            {
                continue;
            }

            string fullType = reader.GetFullTypeName(typeDef);
            if (fullType.Length == 0)
                continue;

            var overloadCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);

                // Overload ordinal counts every same-named method in metadata
                // order, exactly as ReturnToSender.TryFindMethod does, so a
                // skipped accessor still advances the ordinal for later real
                // overloads.
                int overload = overloadCounts.TryGetValue(methodName, out int seen) ? seen : 0;
                overloadCounts[methodName] = overload + 1;

                if (!IsRealMethod(method, methodName))
                    continue;

                int parameterCount = ParameterCount(reader, typeDef, method);
                string? signature = ResolveUniqueSignature(reader, typeDef, methodName, methodHandle);
                IlDifficulty difficulty = ComputeDifficulty(pe, reader, typeDef, method);

                targets.Add(new RealMethodTarget(
                    fullType,
                    methodName,
                    overload,
                    signature,
                    MetadataTokens.GetToken(methodHandle),
                    parameterCount,
                    difficulty.IlSize,
                    difficulty));
            }
        }

        return targets;
    }

    static bool IsRealMethod(MethodDefinition method, string methodName)
    {
        if (method.RelativeVirtualAddress == 0)
            return false;

        // Exclude accessors, operators, constructors, and other special-name
        // members: the authored-source corpus targets hand-written method
        // bodies, not generated or ceremonial shapes.
        if ((method.Attributes & (MethodAttributes.SpecialName | MethodAttributes.RTSpecialName)) != 0)
            return false;

        // Exclude compiler-generated methods (e.g. <Foo>b__0, lambda/local
        // function bodies, iterator/async MoveNext) whose names carry '<'.
        if (methodName.Contains('<', StringComparison.Ordinal))
            return false;

        // Exclude P/Invoke and other bodyless declarations defensively; these
        // already fail the RVA check above but the intent is explicit.
        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            return false;

        return true;
    }

    static int ParameterCount(MetadataReader reader, TypeDefinition typeDef, MethodDefinition method)
    {
        try
        {
            return method.DecodeSignature(
                    SignatureDecoder.Instance,
                    GenericContext.ForMethod(reader, typeDef, method))
                .ParameterTypes.Length;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return method.GetParameters().Count;
        }
    }

    static string? ResolveUniqueSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        string methodName,
        MethodDefinitionHandle methodHandle)
    {
        string? signature;
        try
        {
            signature = SignatureIdentity.ForMetadataMethod(reader, typeDef, methodHandle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        if (signature is null)
            return null;

        return ReturnToSender.ResolvesUniquelyBySignature(reader, typeDef, methodName, signature, methodHandle)
            ? signature
            : null;
    }

    static IlDifficulty ComputeDifficulty(
        PEReader pe,
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method)
    {
        if (method.RelativeVirtualAddress == 0)
            return IlDifficulty.Empty;

        try
        {
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            int ilSize = body.GetILBytes()?.Length ?? 0;
            int localCount = LocalCount(reader, typeDef, method, body);
            var decoded = MethodInstructions.Decode(body);
            return IlDifficultyScorer.Score(decoded, ilSize, localCount, body.MaxStack);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return IlDifficulty.Empty;
        }
    }

    static int LocalCount(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        MethodBodyBlock body)
    {
        if (body.LocalSignature.IsNil)
            return 0;

        try
        {
            return reader.GetStandaloneSignature(body.LocalSignature)
                .DecodeLocalSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method))
                .Length;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return 0;
        }
    }
}
