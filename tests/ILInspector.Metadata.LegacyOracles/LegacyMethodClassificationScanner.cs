using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.LegacyOracles;

/// <summary>
/// The retired <c>MethodClassificationScanner.Scan</c>, frozen on 2026-09-28
/// when the method classification analyzers replaced it (#8773). It is the
/// equivalence oracle for those analyzers: one metadata-order pass that
/// classifies every public method as P/Invoke, async, or pointer signature and
/// projects its identity. Do not change its behavior.
/// </summary>
public static class LegacyMethodClassificationScanner
{
    /// <summary>
    /// Finds all unsafe and P/Invoke methods in an assembly.
    /// </summary>
    public static List<ClassifiedMethodInfo> Scan(PEReader peReader)
    {
        List<ClassifiedMethodInfo> results = [];

        if (!MetadataFormatAdmission.AdmitImage(peReader))
            return results;

        var reader = MetadataFormatAdmission.GetMetadataReader(peReader);
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
            string fullTypeName = MethodRowProjection.FormatDeclaringTypeName(
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
                        methodIdentity = MethodRowProjection.TryCreateMethodIdentity(
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
                    string? moduleName = MethodRowProjection.GetPInvokeModuleName(reader, methodHandle);
                    // Identity first: a hostile signature must not also pay
                    // FormatSignature after CreateMethodAnchorInfo already rejected.
                    MethodAnchorInfo? identity = GetMethodIdentity();
                    string signature = MethodRowProjection.FormatSignatureOrFallback(
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
                var asyncClassification = MethodClassificationScanner.ClassifyAsyncMethod(reader, method);
                if (asyncClassification is { } asyncKind)
                {
                    MethodAnchorInfo? identity = GetMethodIdentity();
                    string signature = MethodRowProjection.FormatSignatureOrFallback(
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
                        string signature = MethodRowProjection.FormatSignatureOrFallback(
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

                    MethodRowProjection.NoteDecodeFailure(ref identityDecodeFailures, ex);
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
}
