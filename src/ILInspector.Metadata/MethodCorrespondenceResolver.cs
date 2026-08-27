using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public enum MethodCorrespondenceStatus
{
    Exact,
    Absent,
    Ambiguous,
    Failed,
}

/// <summary>
/// Total cross-reader correspondence for one metadata method definition. The
/// resolver operation defines whether exactness means strict structural
/// definition identity or stable API-member identity; metadata row numbers are
/// never used as cross-module identity.
/// </summary>
public sealed record MethodCorrespondenceResult(
    MethodCorrespondenceStatus Status,
    MemberAnchor? Anchor,
    MetadataMethodAddress? Target,
    IReadOnlyList<MetadataMethodAddress> Candidates,
    string? Failure)
{
    public bool IsExact => Status == MethodCorrespondenceStatus.Exact;
}

public static class MethodCorrespondenceResolver
{
    /// <summary>
    /// Resolves by the stable API-member anchor used by selectors and API
    /// inventories, with normalized signature shape, required custom
    /// modifiers, named-type CLASS/VALUETYPE encodings, nested function-pointer
    /// conventions, defining scope, positional generic parameters, encoded
    /// arity, and parameter-direction semantics as close negatives.
    /// Assembly-reference versions, generic parameter names, optional modifiers
    /// outside function pointers, and equivalent current-scope TypeDef/TypeRef
    /// storage roles therefore do not become API identity.
    /// <c>CommandExecutionTests.Member_PdbSource_CrossImageDependencyVersionUsesStableApiIdentity</c>
    /// gates the assembly-version distinction;
    /// <c>ResolveApiMember_ParameterDirectionMismatchIsAbsent</c>,
    /// <c>ResolveApiMember_ReturnTypeMismatchIsAbsent</c>,
    /// <c>ResolveApiMember_DifferentDefiningAssemblyIsAbsent</c>,
    /// <c>ResolveApiMember_ClassAndValueTypeSignaturesAreAbsentInEitherDirection</c>,
    /// <c>ResolveApiMember_RenamedMethodGenericParameterRemainsExact</c>,
    /// <c>ResolveApiMember_RequiredReturnModifierMismatchIsAbsent</c>,
    /// <c>ResolveApiMember_FunctionPointerCallingConventionMismatchIsAbsent</c>,
    /// <c>ResolveApiMember_InstanceMismatchIsAbsent</c>,
    /// <c>ResolveApiMember_MaximumTypeArityWithSignatureReferenceMatchesItself</c>,
    /// <c>ResolveApiMember_MaximumNestedTypeArityWithSignatureReferenceMatchesItself</c>,
    /// <c>ResolveApiMember_NestedTypeRawContextUsesCumulativeRows</c>,
    /// <c>ResolveApiMember_HiddenMaximumMethodArityFailsInEitherDirection</c>,
    /// <c>ResolveApiMember_EncodedGenericArityMismatchOnNonmatchingOverloadDoesNotPoisonExactCandidate</c>,
    /// and
    /// <c>ResolveApiMember_MaximumMethodArityWithSignatureReferenceMatchesItself</c>
    /// gate the close negatives and maximum encoded arity.
    /// </summary>
    public static MethodCorrespondenceResult ResolveApiMember(
        MetadataReader sourceReader,
        MetadataMethodAddress source,
        MetadataReader targetReader)
    {
        try
        {
            if (!source.BelongsTo(sourceReader))
                return Failed("source method address belongs to a different metadata module");
            if (!IsValid(sourceReader, source.Handle))
                return Failed("source method handle is outside its metadata module");
            if (targetReader.MethodDefinitions.Count
                > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
            {
                return Failed(
                    "target method table exceeds the correspondence safety limit");
            }

            var sourceMethod = sourceReader.GetMethodDefinition(source.Handle);
            var sourceTypeHandle = sourceMethod.GetDeclaringType();
            MetadataTypeDefinitionNameReadResult sourceTypeNameResult =
                MetadataTypeDefinitionNameReader.Read(
                    sourceReader,
                    sourceTypeHandle);
            if (sourceTypeNameResult
                is not MetadataTypeDefinitionNameReadResult.Read sourceTypeNameRead)
            {
                var failure =
                    ((MetadataTypeDefinitionNameReadResult.Rejected)
                        sourceTypeNameResult).Failure;
                return Failed(failure.Detail);
            }
            MetadataTypeDefinitionName sourceTypeName =
                sourceTypeNameRead.Name;
            var sourceType = sourceReader.GetTypeDefinition(sourceTypeHandle);
            string sourceMetadataName =
                MetadataSafetyPolicy.ReadStructuralString(
                    sourceReader,
                    sourceMethod.Name);
            int typeComparisonWork =
                GetTypeComparisonWork(sourceTypeName);
            int correspondenceWorkRemaining =
                MetadataSafetyPolicy.MaxCorrespondenceAnchorWorkChars;
            var correspondenceContext =
                new ApiMemberIdentity.MethodCorrespondenceContext();
            var targetTypeNameComparisons =
                new Dictionary<
                    (StringHandle Handle, string Expected),
                    bool>();
            var targetMethodNameComparisons =
                new Dictionary<StringHandle, bool>();
            bool? sourceTypeHasExtensionAttribute = null;
            bool sourceIsExtensionMethod =
                IsExtensionMethod(
                    sourceReader,
                    sourceType,
                    sourceMethod,
                    ref correspondenceWorkRemaining,
                    ref sourceTypeHasExtensionAttribute);
            MethodAnchorDeclaringTypeContext sourceDeclaringType =
                ApiMemberIdentity
                    .CreateMethodAnchorDeclaringTypeContext(
                        sourceReader,
                        sourceTypeHandle,
                        ref correspondenceWorkRemaining,
                        correspondenceContext);
            MethodCorrespondenceAnchorInfo sourceAnchor =
                ApiMemberIdentity
                .CreateMethodCorrespondenceAnchorInfo(
                    sourceReader,
                    sourceTypeHandle,
                    source.Handle,
                    sourceDeclaringType,
                    sourceMetadataName,
                    ref correspondenceWorkRemaining,
                    sourceIsExtensionMethod,
                    correspondenceContext);
            MemberAnchor anchor =
                sourceAnchor.AnchorInfo.Anchor;
            ApiMethodSemantics sourceSemantics =
                ReadApiMethodSemantics(
                    sourceReader,
                    sourceMethod,
                    sourceAnchor);

            List<MetadataMethodAddress> candidates = [];
            TypeDefinitionHandle previousTargetTypeHandle = default;
            MetadataTypeDefinitionNameMatch previousTypeMatch =
                MetadataTypeDefinitionNameMatch.NoMatch;
            MethodAnchorDeclaringTypeContext?
                targetDeclaringType = null;
            bool? targetTypeHasExtensionAttribute = null;
            foreach (var targetHandle in targetReader.MethodDefinitions)
            {
                var targetMethod =
                    targetReader.GetMethodDefinition(targetHandle);
                TypeDefinitionHandle targetTypeHandle =
                    targetMethod.GetDeclaringType();
                if (targetTypeHandle != previousTargetTypeHandle)
                {
                    if (!TryCharge(
                            ref correspondenceWorkRemaining,
                            typeComparisonWork))
                    {
                        return Failed(
                            "target methods exceed the correspondence anchor work budget");
                    }
                    previousTargetTypeHandle = targetTypeHandle;
                    targetDeclaringType = null;
                    targetTypeHasExtensionAttribute = null;
                    previousTypeMatch =
                        MetadataTypeDefinitionNameReader.Matches(
                            targetReader,
                            targetTypeHandle,
                            sourceTypeName,
                            out MetadataTypeNameFailure? typeFailure,
                            CompareTargetTypeName);
                    if (previousTypeMatch
                        == MetadataTypeDefinitionNameMatch.Rejected)
                    {
                        return Failed(
                            typeFailure?.Detail
                            ?? "target declaring type name was rejected without failure detail");
                    }
                }
                if (previousTypeMatch == MetadataTypeDefinitionNameMatch.NoMatch)
                    continue;

                if (!TryCompareTargetMethodName(
                        targetMethod.Name,
                        out bool methodNameMatches))
                {
                    return Failed(
                        "target methods exceed the correspondence anchor work budget");
                }
                if (!methodNameMatches)
                    continue;

                var targetType =
                    targetReader.GetTypeDefinition(targetTypeHandle);
                MethodCorrespondenceAnchorInfo targetAnchor;
                try
                {
                    targetDeclaringType ??=
                        ApiMemberIdentity
                            .CreateMethodAnchorDeclaringTypeContext(
                                targetReader,
                                targetTypeHandle,
                                ref correspondenceWorkRemaining,
                                correspondenceContext);
                    bool targetIsExtensionMethod =
                        IsExtensionMethod(
                            targetReader,
                            targetType,
                            targetMethod,
                            ref correspondenceWorkRemaining,
                            ref targetTypeHasExtensionAttribute);
                    targetAnchor =
                        ApiMemberIdentity
                            .CreateMethodCorrespondenceAnchorInfo(
                                targetReader,
                                targetTypeHandle,
                                targetHandle,
                                targetDeclaringType,
                                sourceMetadataName,
                                ref correspondenceWorkRemaining,
                                targetIsExtensionMethod,
                                correspondenceContext);
                }
                catch (BadImageFormatException ex)
                    when (correspondenceWorkRemaining <= 0
                        || ex.Message.Contains(
                            "classification scan work budget",
                            StringComparison.Ordinal))
                {
                    return Failed(
                        "target methods exceed the correspondence anchor work budget");
                }
                if (targetAnchor.IsExtensionMethod
                        != sourceAnchor.IsExtensionMethod
                    || !targetAnchor.CorrespondenceReturnType
                        .CorrespondsTo(
                            sourceAnchor.CorrespondenceReturnType)
                    || !CorrespondenceParameterTypesEqual(
                        targetAnchor.CorrespondenceParameterTypes,
                        sourceAnchor.CorrespondenceParameterTypes))
                    continue;
                if (!sourceSemantics.Equals(
                        ReadApiMethodSemantics(
                            targetReader,
                            targetMethod,
                            targetAnchor)))
                {
                    continue;
                }
                if (candidates.Count
                    == MetadataSafetyPolicy.MaxCorrespondenceCandidates)
                {
                    return Failed(
                        "matching target methods exceed the correspondence safety limit");
                }
                candidates.Add(
                    MetadataMethodAddress.Create(
                        targetReader,
                        targetHandle));
            }

            return candidates.Count switch
            {
                0 => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Absent,
                    anchor,
                    Target: null,
                    Candidates: [],
                    Failure: "no target method has the same normalized API member identity"),
                1 => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Exact,
                    anchor,
                    candidates[0],
                    candidates,
                    Failure: null),
                _ => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Ambiguous,
                    anchor,
                    Target: null,
                    candidates,
                    Failure: $"{candidates.Count} target methods have the same normalized API member identity"),
            };

            bool CompareTargetTypeName(
                StringHandle handle,
                string expected)
            {
                var key = (handle, expected);
                if (targetTypeNameComparisons.TryGetValue(
                        key,
                        out bool matches))
                {
                    return matches;
                }
                int encodedLength =
                    targetReader.GetBlobReader(handle).Length;
                if (!TryCharge(
                        ref correspondenceWorkRemaining,
                        Math.Max(encodedLength, 64)))
                {
                    throw new BadImageFormatException(
                        "Target type names exceed the correspondence anchor work budget.");
                }
                matches =
                    targetReader.StringComparer.Equals(handle, expected);
                targetTypeNameComparisons.Add(key, matches);
                return matches;
            }

            bool TryCompareTargetMethodName(
                StringHandle handle,
                out bool matches)
            {
                if (targetMethodNameComparisons.TryGetValue(
                        handle,
                        out matches))
                {
                    return true;
                }
                int encodedLength =
                    targetReader.GetBlobReader(handle).Length;
                if (!TryCharge(
                        ref correspondenceWorkRemaining,
                        Math.Max(encodedLength, 64)))
                {
                    matches = false;
                    return false;
                }
                matches =
                    targetReader.StringComparer.Equals(
                        handle,
                        sourceMetadataName);
                targetMethodNameComparisons.Add(handle, matches);
                return true;
            }
        }
        catch (Exception ex)
            when (ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            return Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        static MethodCorrespondenceResult Failed(string failure)
            => new(
                MethodCorrespondenceStatus.Failed,
                Anchor: null,
                Target: null,
                Candidates: [],
                failure);
    }

    static bool CorrespondenceParameterTypesEqual(
        ImmutableArray<ApiMemberIdentity.MethodTypeCorrespondence> left,
        ImmutableArray<ApiMemberIdentity.MethodTypeCorrespondence> right)
    {
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (!left[i].CorrespondsTo(right[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Resolves by the strict cross-module definition key, including structured
    /// type-reference scope and generic-constraint identity.
    /// </summary>
    public static MethodCorrespondenceResult Resolve(
        MetadataReader sourceReader,
        MetadataMethodAddress source,
        MetadataReader targetReader)
    {
        try
        {
            if (!source.BelongsTo(sourceReader))
                return Failed("source method address belongs to a different metadata module");
            if (!IsValid(sourceReader, source.Handle))
                return Failed("source method handle is outside its metadata module");
            if (targetReader.MethodDefinitions.Count
                > MetadataSafetyPolicy.MaxCorrespondenceMethodRows)
            {
                return Failed(
                    "target method table exceeds the correspondence safety limit");
            }

            var sourceMethod = sourceReader.GetMethodDefinition(source.Handle);
            var sourceTypeHandle = sourceMethod.GetDeclaringType();
            var sourceType = sourceReader.GetTypeDefinition(sourceTypeHandle);

            // This is definition correspondence, not ECMA MemberRef lookup: the key
            // carries generic constraints in addition to the structural signature,
            // while representing generic parameters positionally so renaming one
            // cannot break a real cross-build match.
            var sourceSignatures =
                new StructuralSignatureBuilder(sourceReader);
            StructuralMethodKey sourceKey =
                sourceSignatures.BuildMethodKey(sourceMethod);
            var anchor = ApiMemberIdentity.CreateMethodAnchor(
                sourceReader,
                sourceTypeHandle,
                sourceMethod,
                IsExtensionMethod(sourceReader, sourceType, sourceMethod));

            List<MetadataMethodAddress> candidates = [];
            var targetSignatures =
                new StructuralSignatureBuilder(targetReader);
            var matchingDeclaringTypes =
                new Dictionary<TypeDefinitionHandle, bool>();
            var matchingSignatures =
                new Dictionary<StructuralEncodedSignature, bool>(
                    ReferenceEqualityComparer.Instance);
            foreach (var targetHandle in targetReader.MethodDefinitions)
            {
                var targetMethod = targetReader.GetMethodDefinition(targetHandle);
                TypeDefinitionHandle declaringType =
                    targetMethod.GetDeclaringType();
                if (!matchingDeclaringTypes.TryGetValue(
                        declaringType,
                        out bool declaringTypeMatches))
                {
                    declaringTypeMatches = sourceKey.DeclaringType.Equals(
                        targetSignatures.BuildTypeKey(declaringType));
                    matchingDeclaringTypes.Add(
                        declaringType,
                        declaringTypeMatches);
                }
                if (!declaringTypeMatches)
                    continue;

                StructuralMethodKey targetKey =
                    targetSignatures.BuildMethodKey(targetMethod);
                if (!matchingSignatures.TryGetValue(
                        targetKey.Component.Signature,
                        out bool signatureMatches))
                {
                    signatureMatches = sourceKey.Component.Signature.Equals(
                        targetKey.Component.Signature);
                    matchingSignatures.Add(
                        targetKey.Component.Signature,
                        signatureMatches);
                }
                if (signatureMatches
                    && sourceKey.Component.LocalKey.Equals(
                        targetKey.Component.LocalKey))
                {
                    if (candidates.Count
                        == MetadataSafetyPolicy.MaxCorrespondenceCandidates)
                    {
                        return Failed(
                            "matching target methods exceed the correspondence safety limit");
                    }
                    candidates.Add(MetadataMethodAddress.Create(targetReader, targetHandle));
                }
            }

            return candidates.Count switch
            {
                0 => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Absent,
                    anchor,
                    Target: null,
                    Candidates: [],
                    Failure: "no target method has the same structural identity"),
                1 => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Exact,
                    anchor,
                    candidates[0],
                    candidates,
                    Failure: null),
                _ => new MethodCorrespondenceResult(
                    MethodCorrespondenceStatus.Ambiguous,
                    anchor,
                    Target: null,
                    candidates,
                    Failure: $"{candidates.Count} target methods have the same structural identity"),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return Failed($"{ex.GetType().Name}: {ex.Message}");
        }

        static MethodCorrespondenceResult Failed(string failure)
            => new(
                MethodCorrespondenceStatus.Failed,
                Anchor: null,
                Target: null,
                Candidates: [],
                failure);
    }

    static bool IsValid(MetadataReader reader, MethodDefinitionHandle handle)
    {
        if (handle.IsNil)
            return false;
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0 && row <= reader.GetTableRowCount(TableIndex.MethodDef);
    }

    static int GetTypeComparisonWork(
        MetadataTypeDefinitionName name)
    {
        int work =
            MetadataSafetyPolicy.MaxRelationshipNodes;
        try
        {
            work = checked(work + name.Namespace.Length);
            foreach (string segment in name.Segments)
                work = checked(work + segment.Length);
            return work;
        }
        catch (OverflowException ex)
        {
            throw new BadImageFormatException(
                "The source declaring type exceeds the correspondence work budget.",
                ex);
        }
    }

    static bool TryCharge(
        ref int remaining,
        int work)
    {
        if (work < 0 || work > remaining)
        {
            remaining = 0;
            return false;
        }
        remaining -= work;
        return true;
    }

    static bool IsExtensionMethod(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method)
        => type.Attributes.HasFlag(TypeAttributes.Abstract)
           && type.Attributes.HasFlag(TypeAttributes.Sealed)
           && method.Attributes.HasFlag(MethodAttributes.Static)
           && AttributeReader.HasExtensionAttribute(reader, type.GetCustomAttributes())
           && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());

    static bool IsExtensionMethod(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method,
        ref int correspondenceWorkRemaining,
        ref bool? typeHasExtensionAttribute)
    {
        if (!type.Attributes.HasFlag(TypeAttributes.Abstract)
            || !type.Attributes.HasFlag(TypeAttributes.Sealed)
            || !method.Attributes.HasFlag(MethodAttributes.Static))
        {
            return false;
        }

        typeHasExtensionAttribute ??=
            HasExtensionAttribute(
                reader,
                type.GetCustomAttributes(),
                ref correspondenceWorkRemaining);
        return typeHasExtensionAttribute.Value
            && HasExtensionAttribute(
                reader,
                method.GetCustomAttributes(),
                ref correspondenceWorkRemaining);
    }

    static bool HasExtensionAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection attributes,
        ref int correspondenceWorkRemaining)
    {
        int rowWork;
        try
        {
            rowWork = checked(
                attributes.Count * AttributeRowWorkUnits);
        }
        catch (OverflowException)
        {
            correspondenceWorkRemaining = 0;
            throw CorrespondenceWorkBudgetExceeded();
        }
        if (!TryCharge(
                ref correspondenceWorkRemaining,
                rowWork))
        {
            throw CorrespondenceWorkBudgetExceeded();
        }

        var materializationBudget =
            new CorrespondenceMaterializationBudget(
                correspondenceWorkRemaining);
        try
        {
            return AttributeReader.HasExtensionAttribute(
                reader,
                attributes,
                materializationBudget.Charge);
        }
        finally
        {
            correspondenceWorkRemaining =
                materializationBudget.Remaining;
        }
    }

    static BadImageFormatException
        CorrespondenceWorkBudgetExceeded()
        => new(
            "The assembly exceeds the classification scan work budget.");

    sealed class CorrespondenceMaterializationBudget(int remaining)
    {
        int _remaining = remaining;

        internal int Remaining => _remaining;

        internal void Charge(int work)
        {
            if (work < 0 || work > _remaining)
            {
                _remaining = 0;
                throw CorrespondenceWorkBudgetExceeded();
            }
            _remaining -= work;
        }
    }

    const int AttributeRowWorkUnits = 64;

    static ApiMethodSemantics ReadApiMethodSemantics(
        MetadataReader reader,
        MethodDefinition method,
        MethodCorrespondenceAnchorInfo anchor)
    {
        if (anchor.MetadataGenericParameterCount
            != anchor.GenericParameterCount)
        {
            throw new BadImageFormatException(
                "Method generic-parameter rows do not match the encoded generic arity.");
        }

        ParameterHandleCollection parameters = method.GetParameters();
        if (parameters.Count
            > anchor.ParameterCount + 1
            || parameters.Count
                > MetadataSafetyPolicy.MaxSignatureTypeNodes)
        {
            throw new BadImageFormatException(
                "Method parameter rows exceed the correspondence safety limit.");
        }

        Dictionary<int, byte>? directions = null;
        HashSet<int>? sequences = null;
        foreach (ParameterHandle handle in parameters)
        {
            Parameter parameter = reader.GetParameter(handle);
            if (!(sequences ??= []).Add(parameter.SequenceNumber))
            {
                throw new BadImageFormatException(
                    "Method parameter rows contain a duplicate sequence number.");
            }
            if (parameter.SequenceNumber > anchor.ParameterCount)
            {
                throw new BadImageFormatException(
                    "Method parameter row sequence exceeds the encoded parameter count.");
            }

            const ParameterAttributes DirectionMask =
                ParameterAttributes.In | ParameterAttributes.Out;
            byte direction =
                (byte)(parameter.Attributes & DirectionMask);
            if (parameter.SequenceNumber != 0
                && direction != 0)
                (directions ??= []).Add(parameter.SequenceNumber, direction);
        }

        return new ApiMethodSemantics(
            anchor.SignatureHeader,
            anchor.GenericParameterCount,
            anchor.RequiredParameterCount,
            anchor.ParameterCount,
            directions);
    }

    readonly record struct ApiMethodSemantics(
        byte SignatureHeader,
        int GenericParameterCount,
        int RequiredParameterCount,
        int ParameterCount,
        IReadOnlyDictionary<int, byte>? ParameterDirections)
    {
        public bool Equals(ApiMethodSemantics other)
        {
            if (SignatureHeader != other.SignatureHeader
                || GenericParameterCount
                    != other.GenericParameterCount
                || RequiredParameterCount
                    != other.RequiredParameterCount
                || ParameterCount != other.ParameterCount)
            {
                return false;
            }

            if (ParameterDirections is null)
                return other.ParameterDirections is null;
            if (other.ParameterDirections is null
                || ParameterDirections.Count
                    != other.ParameterDirections.Count)
            {
                return false;
            }

            foreach ((int sequence, byte direction) in ParameterDirections)
            {
                if (!other.ParameterDirections.TryGetValue(
                        sequence,
                        out byte otherDirection)
                    || otherDirection != direction)
                {
                    return false;
                }
            }
            return true;
        }

        public override int GetHashCode()
            => HashCode.Combine(
                SignatureHeader,
                GenericParameterCount,
                RequiredParameterCount,
                ParameterCount);
    }
}
