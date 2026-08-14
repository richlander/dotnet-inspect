using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Information about a method with a notable classification (unsafe, P/Invoke, etc.).
/// </summary>
public record ClassifiedMethodInfo(
    string MethodName,
    string DeclaringType,
    string? Namespace,
    string Signature,
    MethodClassification Classification,
    string? ModuleName = null)
{
    public MemberAnchor? Anchor { get; init; }
    public string? ReturnType { get; init; }

    // Preserve the original six-field record contract. Anchor and ReturnType are
    // derived structured data and intentionally do not participate in equality.
    public virtual bool Equals(ClassifiedMethodInfo? other)
        => ReferenceEquals(this, other)
        || other is not null
        && EqualityContract == other.EqualityContract
        && string.Equals(MethodName, other.MethodName, StringComparison.Ordinal)
        && string.Equals(DeclaringType, other.DeclaringType, StringComparison.Ordinal)
        && string.Equals(Namespace, other.Namespace, StringComparison.Ordinal)
        && string.Equals(Signature, other.Signature, StringComparison.Ordinal)
        && Classification == other.Classification
        && string.Equals(ModuleName, other.ModuleName, StringComparison.Ordinal);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EqualityContract);
        hash.Add(MethodName, StringComparer.Ordinal);
        hash.Add(DeclaringType, StringComparer.Ordinal);
        hash.Add(Namespace, StringComparer.Ordinal);
        hash.Add(Signature, StringComparer.Ordinal);
        hash.Add(Classification);
        hash.Add(ModuleName, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Classification of a method based on its metadata characteristics.
/// </summary>
public enum MethodClassification
{
    Unsafe,
    PInvoke,

    /// <summary>
    /// Runtime async method (.NET 11+): MethodImplAttributes.Async (0x2000) flag set,
    /// suspension handled by the runtime with no compiler-generated state machine.
    /// </summary>
    RuntimeAsync,

    /// <summary>
    /// Classic compiler state-machine async method: carries
    /// AsyncStateMachineAttribute or AsyncIteratorStateMachineAttribute.
    /// </summary>
    StateMachineAsync
}

/// <summary>
/// Scans assemblies for methods with notable classifications: unsafe signatures and P/Invoke imports.
/// </summary>
public static class MethodClassificationScanner
{
    /// <summary>
    /// Finds all unsafe and P/Invoke methods in an assembly.
    /// </summary>
    public static List<ClassifiedMethodInfo> Scan(Stream peStream)
    {
        using var peReader = new PEReader(peStream);
        return Scan(peReader);
    }

    /// <summary>
    /// Finds all unsafe and P/Invoke methods in an assembly.
    /// </summary>
    public static List<ClassifiedMethodInfo> Scan(PEReader peReader)
    {
        List<ClassifiedMethodInfo> results = [];

        if (!peReader.HasMetadata)
            return results;

        var reader = peReader.GetMetadataReader();
        int identityDecodeFailures = 0;
        int scanWorkRemaining =
            MetadataSafetyPolicy.MaxClassificationScanWorkChars;

        foreach (var typeDefHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeDefHandle);

            // Skip compiler-generated types
            string typeName = reader.GetString(typeDef.Name);
            if (typeName.StartsWith("<", StringComparison.Ordinal))
                continue;

            string ns = reader.GetString(typeDef.Namespace);
            string fullTypeName = reader.GetFullTypeName(typeDef);

            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                    continue;

                string methodName = reader.GetString(method.Name);
                MethodAnchorInfo? methodIdentity = null;
                bool identityAttempted = false;

                MethodAnchorInfo? GetMethodIdentity()
                {
                    if (!identityAttempted)
                    {
                        methodIdentity = TryCreateMethodIdentity(
                            reader,
                            typeDefHandle,
                            method,
                            ref identityDecodeFailures,
                            ref scanWorkRemaining);
                        identityAttempted = true;
                    }

                    return methodIdentity;
                }

                // Skip accessors and constructors
                if (methodName.StartsWith("get_", StringComparison.Ordinal) ||
                    methodName.StartsWith("set_", StringComparison.Ordinal) ||
                    methodName.StartsWith("add_", StringComparison.Ordinal) ||
                    methodName.StartsWith("remove_", StringComparison.Ordinal))
                    continue;

                // Check P/Invoke
                if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                {
                    string? moduleName = GetPInvokeModuleName(reader, methodHandle);
                    // Identity first: a hostile signature must not also pay
                    // FormatSignature after CreateMethodAnchorInfo already rejected.
                    MethodAnchorInfo? identity = GetMethodIdentity();
                    string signature = FormatSignatureOrFallback(
                        reader, typeDef, method, methodName, identity);
                    results.Add(new ClassifiedMethodInfo(
                        methodName, fullTypeName, ns, signature,
                        MethodClassification.PInvoke, moduleName)
                    {
                        Anchor = identity?.Anchor,
                        ReturnType = identity?.ReturnType,
                    });
                    continue; // P/Invoke methods are also "unsafe" but classify as P/Invoke
                }

                // Check async (runtime async vs classic state-machine async)
                var asyncClassification = ClassifyAsyncMethod(reader, method);
                if (asyncClassification is { } asyncKind)
                {
                    MethodAnchorInfo? identity = GetMethodIdentity();
                    string signature = FormatSignatureOrFallback(
                        reader, typeDef, method, methodName, identity);
                    results.Add(new ClassifiedMethodInfo(
                        methodName, fullTypeName, ns, signature, asyncKind)
                    {
                        Anchor = identity?.Anchor,
                        ReturnType = identity?.ReturnType,
                    });
                }

                // Check unsafe (pointer types in signature)
                try
                {
                    var context = GenericContext.ForMethod(reader, typeDef, method);
                    var sig = GuardedSignatureText.MethodText(reader, method, context)
                        .GetValueOrThrow();
                    if (HasPointerType(sig))
                    {
                        var signature = SignatureRenderer.RenderDecodedSignature(reader, method, methodName, sig);
                        MethodAnchorInfo? identity = GetMethodIdentity();
                        results.Add(new ClassifiedMethodInfo(
                            methodName, fullTypeName, ns, signature,
                            MethodClassification.Unsafe)
                        {
                            Anchor = identity?.Anchor,
                            ReturnType = identity?.ReturnType,
                        });
                    }
                }
                catch (BadImageFormatException ex)
                {
                    // Pointer-shape probes share the scan failure budget so a
                    // multi-method hostile image cannot decode forever here
                    // either.
                    NoteDecodeFailure(ref identityDecodeFailures, ex);
                }
                catch
                {
                    // Skip methods with unresolvable signatures
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Classifies a method as runtime async or classic state-machine async, or null
    /// if it is not an async method. Runtime async (.NET 11+) is identified by the
    /// MethodImplAttributes.Async (0x2000) flag; classic async by the compiler-emitted
    /// AsyncStateMachineAttribute / AsyncIteratorStateMachineAttribute.
    /// </summary>
    public static MethodClassification? ClassifyAsyncMethod(MetadataReader reader, MethodDefinition method)
    {
        const MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000;
        if ((method.ImplAttributes & AsyncImplFlag) != 0)
            return MethodClassification.RuntimeAsync;

        var attributes = method.GetCustomAttributes();
        if (AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.AsyncStateMachineAttribute)
            || AttributeReader.HasAttribute(reader, attributes, KnownAttributeNames.AsyncIteratorStateMachineAttribute))
            return MethodClassification.StateMachineAsync;

        return null;
    }

    private static bool HasPointerType(MethodSignature<string> signature)
    {
        if (signature.ReturnType.Contains('*'))
            return true;

        foreach (var paramType in signature.ParameterTypes)
        {
            if (paramType.Contains('*'))
                return true;
        }

        return false;
    }

    private static string? GetPInvokeModuleName(MetadataReader reader, MethodDefinitionHandle methodHandle)
    {
        var import = reader.GetMethodDefinition(methodHandle).GetImport();
        if (import.Module.IsNil)
            return null;

        var moduleRef = reader.GetModuleReference(import.Module);
        return reader.GetString(moduleRef.Name);
    }

    private static MethodAnchorInfo? TryCreateMethodIdentity(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        MethodDefinition method,
        ref int identityDecodeFailures,
        ref int scanWorkRemaining)
    {
        try
        {
            return ApiMemberIdentity.CreateMethodAnchorInfo(
                reader,
                typeHandle,
                method,
                ref scanWorkRemaining);
        }
        catch (BadImageFormatException ex)
        {
            // One malformed anchor is skippable (null identity on that row). Many
            // hostile methods each paying the per-anchor reject cost — or many
            // near-limit successes drawing down the scan work budget — are not.
            // Gated by MaxClassificationIdentityDecodeFailures and
            // MaxClassificationScanWorkChars.
            // Exhausted scan-level work (including a single near-limit identity that
            // consumed the shared budget) must fail the scan, not soft-skip.
            if (scanWorkRemaining <= 0
                || ex.Message.Contains(
                    "classification scan work budget",
                    StringComparison.Ordinal))
            {
                throw;
            }

            NoteDecodeFailure(ref identityDecodeFailures, ex);
            return null;
        }
    }

    static void NoteDecodeFailure(
        ref int identityDecodeFailures,
        BadImageFormatException ex)
    {
        identityDecodeFailures++;
        if (identityDecodeFailures
            >= MetadataSafetyPolicy.MaxClassificationIdentityDecodeFailures)
        {
            throw new BadImageFormatException(
                "The assembly exceeds the method-identity decode failure budget during classification scan.",
                ex);
        }
    }

    static string FormatSignatureOrFallback(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        string methodName,
        MethodAnchorInfo? identity)
    {
        // When identity decode already failed, the blob is hostile or malformed —
        // do not decode it again for display text.
        if (identity is null)
            return methodName + "(...)";

        return FormatSignature(reader, typeDef, method, methodName);
    }

    private static string FormatSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        MethodDefinition method,
        string methodName)
    {
        try
        {
            var context = GenericContext.ForMethod(reader, typeDef, method);
            var sig = GuardedSignatureText.MethodText(reader, method, context)
                .GetValueOrThrow();
            return SignatureRenderer.RenderDecodedSignature(reader, method, methodName, sig);
        }
        catch
        {
            return methodName + "(...)";
        }
    }
}
