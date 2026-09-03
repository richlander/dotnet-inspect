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
            string fullTypeName = FormatDeclaringTypeName(
                reader,
                typeDefHandle);

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

                // Check unsafe (pointer types in signature). Use a budgeted pointer
                // detector — string MethodText materializes discarded modopt trees
                // (~570 MiB/method). Even the allocation-light PointerDetector still
                // expands wide GENERICINST TypeSpecs; charge structural visits
                // against the shared scan work budget (R1 Opus plain-hostile).
                try
                {
                    if (scanWorkRemaining <= 0)
                    {
                        throw new BadImageFormatException(
                            "The assembly exceeds the classification scan work budget.");
                    }

                    var pointerProbe = new BudgetedPointerDetector(scanWorkRemaining);
                    try
                    {
                        var decoded = GuardedProviderDecode.MethodResult(
                            reader,
                            method,
                            pointerProbe,
                            (object?)null,
                            PointerDetection.Degraded);
                        var detection = PointerDetection.Combine(
                            decoded.Value.ReturnType,
                            decoded.Value.ParameterTypes);
                        if (!detection.HasPointer)
                            continue;

                        MethodAnchorInfo? identity = GetMethodIdentity();
                        string signature = FormatSignatureOrFallback(
                            reader, typeDef, method, methodName, identity);
                        results.Add(new ClassifiedMethodInfo(
                            methodName, fullTypeName, ns, signature,
                            MethodClassification.Unsafe)
                        {
                            Anchor = identity?.Anchor,
                            ReturnType = identity?.ReturnType,
                        });
                    }
                    finally
                    {
                        scanWorkRemaining = pointerProbe.Remaining;
                    }
                }
                catch (BadImageFormatException ex)
                {
                    // Pointer-shape probes share the scan failure / work budgets so a
                    // multi-method hostile image cannot decode forever here either.
                    if (scanWorkRemaining <= 0
                        || ex.Message.Contains(
                            "classification scan work budget",
                            StringComparison.Ordinal))
                    {
                        throw;
                    }

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

    static string FormatDeclaringTypeName(
        MetadataReader reader,
        TypeDefinitionHandle handle)
    {
        if (MetadataTypeDefinitionNameReader.Read(reader, handle)
            is MetadataTypeDefinitionNameReadResult.Read read)
        {
            string displayName =
                TypeResolver.FormatDisplayName(read.Name.Segments);
            return read.Name.Namespace.Length == 0
                ? displayName
                : $"{read.Name.Namespace}.{displayName}";
        }

        return reader.GetFullTypeName(reader.GetTypeDefinition(handle));
    }

    /// <summary>
    /// Pointer detection that draws from the classification-scan work budget on
    /// composite / TypeSpec visits so wide successful shapes cannot multiply
    /// across MethodDefs without charging.
    /// </summary>
    sealed class BudgetedPointerDetector : ISignatureTypeProvider<PointerDetection, object?>
    {
        int _remaining;

        public BudgetedPointerDetector(int remaining) => _remaining = remaining;

        public int Remaining => _remaining;

        void Charge(int units)
        {
            if (units < 0)
                units = 0;
            if (units > _remaining)
            {
                _remaining = 0;
                throw new BadImageFormatException(
                    "The assembly exceeds the classification scan work budget.");
            }

            _remaining -= units;
        }

        public PointerDetection GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            Charge(1);
            return default;
        }

        public PointerDetection GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            // Match anchor leaf floor so wide TypeSpec arg lists draw down the
            // shared scan budget before ImmutableArrays accumulate.
            Charge(64);
            return default;
        }

        public PointerDetection GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            Charge(64);
            return default;
        }

        public PointerDetection GetTypeFromSpecification(
            MetadataReader reader,
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            Charge(64);
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return PointerDetection.Degraded;
            using (scope)
            {
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            }
        }

        public PointerDetection GetSZArrayType(PointerDetection elementType) => elementType;

        public PointerDetection GetArrayType(PointerDetection elementType, ArrayShape shape)
            => elementType;

        public PointerDetection GetByReferenceType(PointerDetection elementType) => elementType;

        public PointerDetection GetPointerType(PointerDetection elementType)
        {
            Charge(64);
            return new(HasPointer: true, elementType.IsDegraded);
        }

        public PointerDetection GetGenericInstantiation(
            PointerDetection genericType,
            System.Collections.Immutable.ImmutableArray<PointerDetection> typeArguments)
        {
            Charge(64);
            return PointerDetection.Combine(genericType, typeArguments);
        }

        public PointerDetection GetGenericMethodParameter(object? context, int index)
            => default;

        public PointerDetection GetGenericTypeParameter(object? context, int index)
            => default;

        public PointerDetection GetFunctionPointerType(
            MethodSignature<PointerDetection> signature)
        {
            Charge(64);
            return new(
                HasPointer: true,
                signature.ReturnType.IsDegraded
                    || signature.ParameterTypes.Any(static type => type.IsDegraded));
        }

        public PointerDetection GetModifiedType(
            PointerDetection modifier,
            PointerDetection unmodifiedType,
            bool isRequired)
        {
            Charge(64);
            return new(
                modifier.HasPointer || unmodifiedType.HasPointer,
                modifier.IsDegraded || unmodifiedType.IsDegraded);
        }

        public PointerDetection GetPinnedType(PointerDetection elementType) => elementType;
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
            return SignatureRenderer.RenderDecodedSignature(
                reader,
                method,
                methodName,
                sig,
                context);
        }
        catch
        {
            return methodName + "(...)";
        }
    }
}
