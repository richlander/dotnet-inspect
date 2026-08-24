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
    /// inventories, with normalized return type, calling convention, and
    /// parameter-direction semantics as close negatives. Assembly-reference
    /// versions and TypeDef/TypeRef storage roles therefore do not become API
    /// identity.
    /// <c>CommandExecutionTests.Member_PdbSource_CrossImageDependencyVersionUsesStableApiIdentity</c>
    /// gates the assembly-version distinction;
    /// <c>ResolveApiMember_ParameterDirectionMismatchIsAbsent</c>,
    /// <c>ResolveApiMember_ReturnTypeMismatchIsAbsent</c>, and
    /// <c>ResolveApiMember_InstanceMismatchIsAbsent</c> gate the close
    /// negatives.
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
            MethodAnchorInfo sourceAnchor =
                ApiMemberIdentity.CreateMethodAnchorInfo(
                sourceReader,
                sourceTypeHandle,
                sourceMethod,
                IsExtensionMethod(sourceReader, sourceType, sourceMethod));
            MemberAnchor anchor = sourceAnchor.Anchor;
            ApiMethodSemantics sourceSemantics =
                ReadApiMethodSemantics(sourceReader, sourceMethod);

            List<MetadataMethodAddress> candidates = [];
            int targetAnchorWorkRemaining =
                MetadataSafetyPolicy.MaxCorrespondenceAnchorWorkChars;
            TypeDefinitionHandle previousTargetTypeHandle = default;
            MetadataTypeDefinitionNameMatch previousTypeMatch =
                MetadataTypeDefinitionNameMatch.NoMatch;
            foreach (var targetHandle in targetReader.MethodDefinitions)
            {
                var targetMethod =
                    targetReader.GetMethodDefinition(targetHandle);
                TypeDefinitionHandle targetTypeHandle =
                    targetMethod.GetDeclaringType();
                if (targetTypeHandle != previousTargetTypeHandle)
                {
                    previousTargetTypeHandle = targetTypeHandle;
                    previousTypeMatch =
                        MetadataTypeDefinitionNameReader.Matches(
                            targetReader,
                            targetTypeHandle,
                            sourceTypeName,
                            out MetadataTypeNameFailure? typeFailure);
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

                if (!StringComparer.Ordinal.Equals(
                        MetadataSafetyPolicy.ReadStructuralString(
                            targetReader,
                            targetMethod.Name),
                        sourceMetadataName))
                {
                    continue;
                }

                var targetType =
                    targetReader.GetTypeDefinition(targetTypeHandle);
                MethodAnchorInfo targetAnchor;
                try
                {
                    targetAnchor = ApiMemberIdentity.CreateMethodAnchorInfo(
                        targetReader,
                        targetTypeHandle,
                        targetMethod,
                        ref targetAnchorWorkRemaining,
                        IsExtensionMethod(
                            targetReader,
                            targetType,
                            targetMethod));
                }
                catch (BadImageFormatException ex)
                    when (targetAnchorWorkRemaining <= 0
                        || ex.Message.Contains(
                            "classification scan work budget",
                            StringComparison.Ordinal))
                {
                    return Failed(
                        "target methods exceed the correspondence anchor work budget");
                }
                if (targetAnchor.Anchor != anchor
                    || !StringComparer.Ordinal.Equals(
                        targetAnchor.ReturnType,
                        sourceAnchor.ReturnType))
                    continue;
                if (!sourceSemantics.Equals(
                        ReadApiMethodSemantics(
                            targetReader,
                            targetMethod)))
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

    static bool IsExtensionMethod(
        MetadataReader reader,
        TypeDefinition type,
        MethodDefinition method)
        => type.Attributes.HasFlag(TypeAttributes.Abstract)
           && type.Attributes.HasFlag(TypeAttributes.Sealed)
           && method.Attributes.HasFlag(MethodAttributes.Static)
           && AttributeReader.HasExtensionAttribute(reader, type.GetCustomAttributes())
           && AttributeReader.HasExtensionAttribute(reader, method.GetCustomAttributes());

    static ApiMethodSemantics ReadApiMethodSemantics(
        MetadataReader reader,
        MethodDefinition method)
    {
        var signature = reader.GetBlobReader(method.Signature);
        SignatureHeader header = signature.ReadSignatureHeader();
        ParameterHandleCollection parameters = method.GetParameters();
        if (parameters.Count > MetadataSafetyPolicy.MaxSignatureTypeNodes)
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

            const ParameterAttributes DirectionMask =
                ParameterAttributes.In | ParameterAttributes.Out;
            byte direction =
                (byte)(parameter.Attributes & DirectionMask);
            if (direction != 0)
                (directions ??= []).Add(parameter.SequenceNumber, direction);
        }

        return new ApiMethodSemantics(
            header.Kind,
            header.CallingConvention,
            header.IsInstance,
            header.HasExplicitThis,
            header.IsGeneric,
            directions);
    }

    readonly record struct ApiMethodSemantics(
        SignatureKind Kind,
        SignatureCallingConvention CallingConvention,
        bool IsInstance,
        bool HasExplicitThis,
        bool IsGeneric,
        IReadOnlyDictionary<int, byte>? ParameterDirections)
    {
        public bool Equals(ApiMethodSemantics other)
        {
            if (Kind != other.Kind
                || CallingConvention != other.CallingConvention
                || IsInstance != other.IsInstance
                || HasExplicitThis != other.HasExplicitThis
                || IsGeneric != other.IsGeneric)
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
                Kind,
                CallingConvention,
                IsInstance,
                HasExplicitThis,
                IsGeneric);
    }
}
