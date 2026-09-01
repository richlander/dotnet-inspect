using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

/// <summary>
/// Indexes structural state-machine relationships from one metadata reader.
/// Consumers retain ownership of attribution, reconstruction, and presentation
/// policy.
/// </summary>
/// <remarks>
/// <c>StateMachineRelationshipIndex_ResolvesExactInterfaceImplementations</c>
/// gates exact role selection,
/// <c>StateMachineRelationshipIndex_ResolvesClassicAsyncWithAbsentSupportRole</c>
/// gates the admitted classic support-role absence, and
/// <c>StateMachineRelationshipIndex_PropagatesTypedBudgetFailure</c>,
/// <c>StateMachineRelationshipIndex_MergesEveryOverlappingRejection</c>, and
/// <c>StateMachineRelationshipIndex_RejectsInvalidImplementationShapes</c>
/// gate typed rejection.
/// </remarks>
public sealed class StateMachineRelationshipIndex
{
    static readonly StateMachineRelationshipResult s_absent =
        new StateMachineRelationshipResult.Absent();

    readonly Guid _moduleVersionId;
    readonly int _methodRowCount;
    readonly int _typeRowCount;
    readonly IReadOnlyDictionary<int, StateMachineRelationshipResult>
        _byKickoff;
    readonly IReadOnlyDictionary<int, StateMachineRelationshipResult>
        _byStateMachine;
    readonly IReadOnlyDictionary<int, StateMachineRelationshipResult>
        _byImplementation;
    readonly StateMachineRelationshipResult.Rejected? _globalFailure;

    StateMachineRelationshipIndex(
        Guid moduleVersionId,
        int methodRowCount,
        int typeRowCount,
        IReadOnlyDictionary<int, StateMachineRelationshipResult> byKickoff,
        IReadOnlyDictionary<int, StateMachineRelationshipResult> byStateMachine,
        IReadOnlyDictionary<int, StateMachineRelationshipResult> byImplementation,
        ImmutableArray<StateMachineRelationship> relationships,
        StateMachineRelationshipResult.Rejected? globalFailure)
    {
        _moduleVersionId = moduleVersionId;
        _methodRowCount = methodRowCount;
        _typeRowCount = typeRowCount;
        _byKickoff = byKickoff;
        _byStateMachine = byStateMachine;
        _byImplementation = byImplementation;
        _globalFailure = globalFailure;
        Relationships = globalFailure is null
            ? new StateMachineRelationshipsResult.Available(
                relationships)
            : new StateMachineRelationshipsResult.Rejected(
                globalFailure.Failure);
    }

    public StateMachineRelationshipsResult Relationships { get; }

    public static StateMachineRelationshipIndex Create(
        MetadataReader reader)
        => Create(
            reader,
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars,
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars);

    internal static StateMachineRelationshipIndex Create(
        MetadataReader reader,
        int relationshipBudget,
        int methodRowBudget =
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        int nameWorkBudget =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars,
        int signatureWorkBudget =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars,
        Action? rejectionWorkObserved = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            relationshipBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            methodRowBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            nameWorkBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            signatureWorkBudget);

        try
        {
            return new Builder(
                reader,
                relationshipBudget,
                methodRowBudget,
                nameWorkBudget,
                signatureWorkBudget,
                rejectionWorkObserved).Build();
        }
        catch (RelationshipBudgetException)
        {
            return Failed(
                reader,
                StateMachineRelationshipFailureKind.BudgetExceeded,
                "State-machine relationship discovery exceeded its work budget.");
        }
        catch (MetadataTypeDefinitionIndexBudgetException)
        {
            return Failed(
                reader,
                StateMachineRelationshipFailureKind.BudgetExceeded,
                "State-machine relationship discovery exceeded its TypeDef name budget.");
        }
        catch (Exception ex) when (
            IsRecoverableMetadataFailure(ex))
        {
            return Failed(
                reader,
                StateMachineRelationshipFailureKind.Malformed,
                "State-machine relationship metadata could not be read.");
        }
    }

    public StateMachineRelationshipResult GetByKickoff(
        MethodDefinitionHandle kickoff)
        => GetMethodResult(kickoff, _byKickoff);

    public StateMachineRelationshipResult GetByStateMachine(
        TypeDefinitionHandle stateMachineType)
    {
        if (!IsValidTypeHandle(stateMachineType))
            return MalformedHandle("The state-machine TypeDef handle is invalid.");

        return _globalFailure
            ?? _byStateMachine.GetValueOrDefault(
                MetadataTokens.GetToken(stateMachineType),
                s_absent);
    }

    public StateMachineRelationshipResult GetByImplementation(
        MethodDefinitionHandle implementation)
        => GetMethodResult(implementation, _byImplementation);

    StateMachineRelationshipResult GetMethodResult(
        MethodDefinitionHandle handle,
        IReadOnlyDictionary<int, StateMachineRelationshipResult> index)
    {
        if (!IsValidMethodHandle(handle))
            return MalformedHandle("The MethodDef handle is invalid.");

        return _globalFailure
            ?? index.GetValueOrDefault(
                MetadataTokens.GetToken(handle),
                s_absent);
    }

    bool IsValidMethodHandle(MethodDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0 && row <= _methodRowCount;
    }

    bool IsValidTypeHandle(TypeDefinitionHandle handle)
    {
        int row = MetadataTokens.GetRowNumber(handle);
        return row > 0 && row <= _typeRowCount;
    }

    static StateMachineRelationshipResult.Rejected MalformedHandle(
        string detail) =>
        Rejected(
            StateMachineRelationshipFailureKind.Malformed,
            detail);

    static bool IsRecoverableMetadataFailure(Exception exception) =>
        exception is BadImageFormatException
            or ArgumentOutOfRangeException
            or InvalidOperationException
            or OverflowException;

    static Guid ReadModuleVersionId(MetadataReader reader)
    {
        GuidHandle handle = reader.GetModuleDefinition().Mvid;
        int index = MetadataTokens.GetHeapOffset(handle);
        int heapSize = reader.GetHeapSize(HeapIndex.Guid);
        if (handle.IsNil
            || index <= 0
            || (long)index * 16 > heapSize)
        {
            throw new BadImageFormatException(
                "The module MVID does not reference a complete GUID heap entry.");
        }

        return reader.GetGuid(handle);
    }

    static StateMachineRelationshipIndex Failed(
        MetadataReader reader,
        StateMachineRelationshipFailureKind kind,
        string detail)
    {
        Guid moduleVersionId = default;
        int methodRows = 0;
        int typeRows = 0;
        try
        {
            moduleVersionId = ReadModuleVersionId(reader);
        }
        catch (Exception ex) when (
            IsRecoverableMetadataFailure(ex))
        {
        }
        try
        {
            methodRows =
                reader.GetTableRowCount(TableIndex.MethodDef);
        }
        catch (Exception ex) when (
            IsRecoverableMetadataFailure(ex))
        {
        }
        try
        {
            typeRows =
                reader.GetTableRowCount(TableIndex.TypeDef);
        }
        catch (Exception ex) when (
            IsRecoverableMetadataFailure(ex))
        {
        }

        return new(
            moduleVersionId,
            methodRows,
            typeRows,
            new Dictionary<int, StateMachineRelationshipResult>(),
            new Dictionary<int, StateMachineRelationshipResult>(),
            new Dictionary<int, StateMachineRelationshipResult>(),
            [],
            Rejected(kind, detail));
    }

    static StateMachineRelationshipResult.Rejected Rejected(
        StateMachineRelationshipFailureKind kind,
        string detail,
        ImmutableArray<MetadataMethodAddress> kickoffs = default,
        ImmutableArray<MetadataTypeDefinitionAddress> stateMachines = default,
        ImmutableArray<MetadataTypeDefinitionName> claimedTypes = default)
        => new(
            new StateMachineRelationshipFailure(
                kind,
                detail,
                kickoffs,
                stateMachines,
                claimedTypes));

    sealed class Builder
    {
        readonly MetadataReader _reader;
        readonly int _relationshipBudget;
        readonly int _methodRowBudget;
        readonly Guid _moduleVersionId;
        readonly MetadataTypeDefinitionIndex _typeDefinitions;
        readonly StateMachineSignatureProvider _signatures;
        readonly Dictionary<
            EntityHandle,
            AttributeConstructorClassification>
            _attributeConstructors = [];
        readonly Dictionary<int, PendingResult> _byKickoff =
            [];
        readonly Dictionary<int, PendingResult>
            _byStateMachine = [];
        readonly Dictionary<int, PendingResult>
            _byImplementation = [];
        readonly List<StateMachineRelationship> _relationships = [];
        readonly List<Claim> _claims = [];
        readonly List<RejectionComponent> _rejectionComponents = [];
        readonly Dictionary<
            MetadataTypeDefinitionName,
            RejectionComponent> _claimedNameRejections = [];
        readonly Action? _rejectionWorkObserved;
        AssemblyReferenceIdentity? _assemblyDefinition;
        long _remainingNameWork;
        long _remainingSignatureWork;
        int _work;

        internal Builder(
            MetadataReader reader,
            int relationshipBudget,
            int methodRowBudget,
            int nameWorkBudget,
            int signatureWorkBudget,
            Action? rejectionWorkObserved)
        {
            _reader = reader;
            _relationshipBudget = relationshipBudget;
            _methodRowBudget = methodRowBudget;
            _remainingNameWork = nameWorkBudget;
            _remainingSignatureWork = signatureWorkBudget;
            _rejectionWorkObserved = rejectionWorkObserved;
            _moduleVersionId = ReadModuleVersionId(reader);
            _typeDefinitions =
                MetadataTypeDefinitionIndex.Create(
                    reader,
                    definitionVisited: null);
            _signatures = new(
                reader,
                ChargeNameWork,
                ChargeSignatureWork);
        }

        internal StateMachineRelationshipIndex Build()
        {
            if (_reader.GetTableRowCount(TableIndex.MethodDef)
                > _methodRowBudget)
            {
                throw new RelationshipBudgetException();
            }

            foreach (MethodDefinitionHandle kickoff
                in _reader.MethodDefinitions)
            {
                ReadClaims(kickoff);
            }

            foreach (IGrouping<
                MetadataTypeDefinitionName,
                Claim> group in _claims.GroupBy(
                    claim => claim.StateMachineName))
            {
                Claim[] claims = group.ToArray();
                if (!_typeDefinitions.TryGetDefinitions(
                        group.Key,
                        out ImmutableArray<TypeDefinitionHandle>
                            stateMachines,
                        out bool ambiguous))
                {
                    RejectClaims(
                        claims,
                        StateMachineRelationshipFailureKind.Unresolved,
                        "The claimed state-machine type could not be resolved.");
                    continue;
                }
                if (ambiguous)
                {
                    RejectClaims(
                        claims,
                        StateMachineRelationshipFailureKind.Ambiguous,
                        "The claimed state-machine type is ambiguous.",
                        stateMachines);
                    continue;
                }

                TypeDefinitionHandle stateMachine =
                    stateMachines[0];
                if (claims.Length > 1)
                {
                    bool crossKind =
                        claims.Select(claim => claim.Kind)
                            .Distinct()
                            .Skip(1)
                            .Any();
                    RejectClaims(
                        claims,
                        crossKind
                            ? StateMachineRelationshipFailureKind.CrossKind
                            : StateMachineRelationshipFailureKind.Duplicate,
                        crossKind
                            ? "The state-machine type has cross-kind kickoff claims."
                            : "The state-machine type has duplicate kickoff claims.",
                        [stateMachine]);
                    continue;
                }

                Resolve(claims[0], stateMachine);
            }

            IReadOnlyDictionary<
                RejectionComponent,
                StateMachineRelationshipResult.Rejected>
                rejections = FreezeRejections();
            return new(
                _moduleVersionId,
                _reader.GetTableRowCount(TableIndex.MethodDef),
                _reader.GetTableRowCount(TableIndex.TypeDef),
                Freeze(_byKickoff, rejections),
                Freeze(_byStateMachine, rejections),
                Freeze(_byImplementation, rejections),
                [.. _relationships],
                globalFailure: null);
        }

        void ReadClaims(MethodDefinitionHandle kickoff)
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(kickoff);
            var candidates = new List<ClaimCandidate>();
            bool unreadableConstructor = false;
            foreach (CustomAttributeHandle attributeHandle
                in method.GetCustomAttributes())
            {
                Charge();
                CustomAttribute attribute;
                EntityHandle constructor;
                AttributeConstructorClassification classification;
                try
                {
                    attribute =
                        _reader.GetCustomAttribute(attributeHandle);
                    constructor = attribute.Constructor;
                }
                catch (Exception ex) when (
                    IsRecoverableMetadataFailure(ex))
                {
                    unreadableConstructor = true;
                    continue;
                }

                if (!_attributeConstructors.TryGetValue(
                        constructor,
                        out classification))
                {
                    try
                    {
                        classification =
                            _signatures.ClassifyAttributeConstructor(
                                constructor);
                    }
                    catch (Exception ex) when (
                        IsRecoverableMetadataFailure(ex))
                    {
                        classification = new(
                            StateMachineClaimKind.ClassicAsync,
                            AttributeConstructorStatus.Unreadable);
                    }

                    _attributeConstructors.Add(
                        constructor,
                        classification);
                }

                if (classification.Status
                    == AttributeConstructorStatus.NotTrusted)
                {
                    continue;
                }
                if (classification.Status
                    == AttributeConstructorStatus.Unreadable)
                {
                    unreadableConstructor = true;
                    continue;
                }

                if (candidates.Count
                    == MetadataSafetyPolicy.MaxRelationshipNodes)
                {
                    PublishRejection(
                        StateMachineRelationshipFailureKind.BudgetExceeded,
                        "One kickoff method exceeds the state-machine claim budget.",
                        [Address(kickoff)],
                        [],
                        [],
                        [MetadataTokens.GetToken(kickoff)],
                        [],
                        []);
                    return;
                }

                candidates.Add(
                    classification.Status
                        == AttributeConstructorStatus.Valid
                            ? ReadClaimCandidate(
                                classification.Kind,
                                attribute)
                            : ClaimCandidate.Rejected(
                                classification.Kind,
                                StateMachineRelationshipFailureKind.Malformed,
                                "The state-machine attribute constructor is malformed."));
            }

            if (unreadableConstructor)
            {
                const string detail =
                    "A custom-attribute constructor could not be read.";
                if (candidates.Count == 0)
                {
                    PublishRejection(
                        StateMachineRelationshipFailureKind.Malformed,
                        detail,
                        [Address(kickoff)],
                        [],
                        [],
                        [MetadataTokens.GetToken(kickoff)],
                        [],
                        []);
                }
                else
                {
                    RejectKickoffCandidates(
                        kickoff,
                        candidates,
                        StateMachineRelationshipFailureKind.Malformed,
                        detail);
                }
                return;
            }

            if (candidates.Count == 0)
                return;

            if (candidates.Count > 1)
            {
                bool crossKind =
                    candidates.Select(candidate => candidate.Kind)
                        .Distinct()
                        .Skip(1)
                        .Any();
                RejectKickoffCandidates(
                    kickoff,
                    candidates,
                    crossKind
                        ? StateMachineRelationshipFailureKind.CrossKind
                        : StateMachineRelationshipFailureKind.Duplicate,
                    crossKind
                        ? "The kickoff method has cross-kind state-machine claims."
                        : "The kickoff method has duplicate state-machine claims.");
                return;
            }

            ClaimCandidate candidate = candidates[0];
            if (candidate.Failure is not null)
            {
                PublishRejection(
                    candidate.Failure.Value,
                    candidate.Detail!,
                    [Address(kickoff)],
                    [],
                    [],
                    [MetadataTokens.GetToken(kickoff)],
                    [],
                    []);
                return;
            }

            _claims.Add(
                new(
                    kickoff,
                    candidate.Kind,
                    candidate.StateMachineName!));
        }

        ClaimCandidate ReadClaimCandidate(
            StateMachineClaimKind kind,
            CustomAttribute attribute)
        {
            switch (InspectClaimValue(
                attribute,
                out int serializedByteCount))
            {
                case ClaimValueShape.Oversized:
                    return ClaimCandidate.Rejected(
                        kind,
                        StateMachineRelationshipFailureKind.Malformed,
                        "The state-machine type name exceeds its encoded byte budget.");
                case ClaimValueShape.Malformed:
                    return ClaimCandidate.Rejected(
                        kind,
                        StateMachineRelationshipFailureKind.Malformed,
                        "The state-machine attribute value is malformed.");
            }

            ChargeNameWork(serializedByteCount);

            CustomAttributeValue<string>? decoded =
                AttributeDecoder
                    .TryDecodePreservingSerializedTypeNames(
                        _reader,
                        attribute);
            if (decoded is not
                {
                    FixedArguments.Length: 1,
                    NamedArguments.Length: 0,
                }
                || decoded.Value.FixedArguments[0].Value
                    is not string serializedType
                || string.IsNullOrWhiteSpace(serializedType))
            {
                return ClaimCandidate.Rejected(
                    kind,
                    StateMachineRelationshipFailureKind.Malformed,
                    "The state-machine attribute value is malformed.");
            }

            if (!TryGetCurrentAssemblyTypeName(
                    serializedType,
                    out MetadataTypeDefinitionName? stateMachineName,
                    out bool malformed))
            {
                return ClaimCandidate.Rejected(
                    kind,
                    malformed
                        ? StateMachineRelationshipFailureKind.Malformed
                        : StateMachineRelationshipFailureKind.Unresolved,
                    malformed
                        ? "The state-machine type name is malformed."
                        : "The state-machine type is outside the indexed module.");
            }

            return new(
                kind,
                stateMachineName,
                Failure: null,
                Detail: null);
        }

        /// <summary>
        /// Validates the whole attribute value blob before any decode. A trusted
        /// claim constructor takes exactly one <c>System.Type</c> parameter, so a
        /// well-formed value is the prolog, one non-null <c>SerString</c>, and a
        /// zero named-argument count with nothing after it. Checking the tail
        /// here — rather than inspecting the decoded
        /// <c>NamedArguments.Length</c> — keeps SRM from materializing named
        /// argument names and values that the claim contract already forbids;
        /// those bytes are otherwise unbounded and uncharged because many
        /// method definitions can share one value blob.
        /// </summary>
        ClaimValueShape InspectClaimValue(
            CustomAttribute attribute,
            out int byteCount)
        {
            byteCount = 0;
            try
            {
                BlobReader value =
                    _reader.GetBlobReader(attribute.Value);
                if (value.RemainingBytes < 3
                    || value.ReadUInt16() != 1)
                {
                    return ClaimValueShape.Malformed;
                }

                int offset = value.Offset;
                if (value.ReadByte() == 0xFF)
                    return ClaimValueShape.Malformed;
                value.Offset = offset;

                byteCount = value.ReadCompressedInteger();
                if (byteCount < 0)
                    return ClaimValueShape.Malformed;
                if (byteCount
                    > MetadataTypeNameBudget.MaxEncodedBytes)
                {
                    return ClaimValueShape.Oversized;
                }
                if (byteCount > value.RemainingBytes)
                    return ClaimValueShape.Malformed;

                value.Offset += byteCount;
                if (value.RemainingBytes != 2
                    || value.ReadUInt16() != 0)
                {
                    return ClaimValueShape.Malformed;
                }

                return ClaimValueShape.Valid;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                return ClaimValueShape.Malformed;
            }
        }

        bool TryGetCurrentAssemblyTypeName(
            string serializedType,
            out MetadataTypeDefinitionName? name,
            out bool malformed)
        {
            name = null;
            malformed = false;
            if (serializedType.Length
                > MetadataSafetyPolicy.MaxTypeNameCharacters)
            {
                malformed = true;
                return false;
            }

            var options = new TypeNameParseOptions
            {
                MaxNodes =
                    MetadataSafetyPolicy.MaxRelationshipNodes,
            };
            if (!TypeName.TryParse(
                    serializedType,
                    out TypeName? parsed,
                    options))
            {
                malformed = true;
                return false;
            }

            if (parsed.AssemblyName is { } assembly
                && !AssemblyQualificationMatches(assembly))
            {
                return false;
            }

            if (MetadataTypeDefinitionName
                    .FromParsedSerializedName(parsed)
                is not MetadataTypeDefinitionNameResult.Valid valid)
            {
                malformed = true;
                return false;
            }

            name = valid.Name;
            return true;
        }

        /// <summary>
        /// Projects this image's own assembly identity once, charging its
        /// public-key blob against the name-work budget before the key is
        /// copied and hashed. Both are required: the key blob is unbounded
        /// attacker-controlled metadata, and every assembly-qualified claim
        /// consults this identity, so an uncached projection re-copies and
        /// re-hashes the same key once per claim.
        /// </summary>
        AssemblyReferenceIdentity AssemblyDefinitionIdentity()
        {
            if (_assemblyDefinition is { } projected)
                return projected;

            AssemblyDefinition definition =
                _reader.GetAssemblyDefinition();
            BlobHandle key = definition.PublicKey;
            if (!key.IsNil)
                ChargeNameWork(_reader.GetBlobReader(key).Length);

            // The projection decodes the name and culture too. Charging them
            // keeps an unsigned assembly, whose key blob is nil, from reaching
            // this path entirely uncharged.
            if (!definition.Name.IsNil)
            {
                ChargeNameWork(
                    _reader.GetBlobReader(definition.Name).Length);
            }
            if (!definition.Culture.IsNil)
            {
                ChargeNameWork(
                    _reader.GetBlobReader(definition.Culture).Length);
            }

            _assemblyDefinition =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    _reader);
            return _assemblyDefinition;
        }

        bool AssemblyQualificationMatches(
            AssemblyNameInfo qualification)
        {
            if (!_reader.IsAssembly)
                return false;

            AssemblyReferenceIdentity assembly =
                AssemblyDefinitionIdentity();
            if (!string.Equals(
                    qualification.Name,
                    assembly.Name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (qualification.Version is { } version
                && version != assembly.Version)
            {
                return false;
            }
            // A null culture name means the qualifier was omitted; an empty one
            // is an explicit `Culture=neutral` and still has to match.
            if (qualification.CultureName is { } culture
                && !string.Equals(
                    AssemblyReferenceIdentity.NormalizeCulture(culture),
                    AssemblyReferenceIdentity.NormalizeCulture(
                        assembly.Culture),
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if ((qualification.Flags
                    & AssemblyNameFlags.PublicKey) != 0)
            {
                return false;
            }
            // Only a default token means the qualifier was omitted. An empty one
            // is an explicit `PublicKeyToken=null`, which names an unsigned
            // assembly and must not match a signed one.
            if (!qualification.PublicKeyOrToken.IsDefault
                && !string.Equals(
                    Convert.ToHexString(
                        qualification.PublicKeyOrToken.AsSpan()),
                    assembly.PublicKeyToken ?? "",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        void RejectKickoffCandidates(
            MethodDefinitionHandle kickoff,
            IReadOnlyList<ClaimCandidate> candidates,
            StateMachineRelationshipFailureKind kind,
            string detail)
        {
            ImmutableArray<MetadataTypeDefinitionName> claimedTypes =
                [.. candidates
                    .Where(candidate =>
                        candidate.StateMachineName is not null)
                    .Select(candidate =>
                        candidate.StateMachineName!)
                    .Distinct()];
            // Expand each claimed name into its matching type definitions at
            // most once for the whole image. A claimed name can match every
            // duplicate-named type definition, so expanding per kickoff would
            // retain and re-publish that set once per kickoff. Later kickoffs
            // claiming an already-expanded name join the component that owns
            // those reverse-index entries instead, which keeps the merged
            // evidence identical while bounding the work by the type-definition
            // row count rather than by kickoffs times duplicates.
            var stateMachines =
                ImmutableArray.CreateBuilder<
                    MetadataTypeDefinitionAddress>();
            List<MetadataTypeDefinitionName>? expandedNames = null;
            List<RejectionComponent>? priorNameComponents = null;
            foreach (MetadataTypeDefinitionName claimedType
                in claimedTypes)
            {
                if (_claimedNameRejections.TryGetValue(
                        claimedType,
                        out RejectionComponent? prior))
                {
                    (priorNameComponents ??= []).Add(prior);
                    continue;
                }

                if (!_typeDefinitions.TryGetDefinitions(
                        claimedType,
                        out ImmutableArray<TypeDefinitionHandle>
                            matchingTypes,
                        out _))
                {
                    continue;
                }

                foreach (TypeDefinitionHandle stateMachine
                    in matchingTypes)
                {
                    stateMachines.Add(TypeAddress(stateMachine));
                }
                (expandedNames ??= []).Add(claimedType);
            }

            ImmutableArray<MetadataTypeDefinitionAddress>
                stateMachineEvidence = stateMachines.ToImmutable();
            RejectionComponent component = PublishRejection(
                kind,
                detail,
                [Address(kickoff)],
                stateMachineEvidence,
                claimedTypes,
                [MetadataTokens.GetToken(kickoff)],
                [.. stateMachineEvidence.Select(
                    stateMachine => stateMachine.Definition.Value)],
                []);

            if (expandedNames is not null)
            {
                foreach (MetadataTypeDefinitionName name
                    in expandedNames)
                {
                    _claimedNameRejections.Add(name, component);
                }
            }

            if (priorNameComponents is null)
                return;

            foreach (RejectionComponent prior in priorNameComponents)
            {
                ObserveRejectionWork();
                RejectionComponent.Union(component, prior);
            }
        }

        void Resolve(
            Claim claim,
            TypeDefinitionHandle stateMachine)
        {
            MetadataMethodAddress kickoff = Address(claim.Kickoff);
            MetadataTypeDefinitionAddress stateMachineAddress =
                TypeAddress(stateMachine);
            if (_byStateMachine.TryGetValue(
                    stateMachineAddress.Definition.Value,
                    out PendingResult prior))
            {
                if (prior.Rejection is not null)
                {
                    PublishRejection(
                        StateMachineRelationshipFailureKind.Ambiguous,
                        "The state-machine type has conflicting relationships.",
                        [kickoff],
                        [stateMachineAddress],
                        [claim.StateMachineName],
                        [kickoff.Token],
                        [stateMachineAddress.Definition.Value],
                        []);
                    return;
                }

                StateMachineRelationship previous =
                    prior.Resolved!.Relationship;
                RemoveResolved(previous);
                PublishRejection(
                    StateMachineRelationshipFailureKind.Ambiguous,
                    "The state-machine type has conflicting relationships.",
                    [previous.Kickoff, kickoff],
                    [
                        previous.StateMachineType,
                        stateMachineAddress,
                    ],
                    [
                        previous.StateMachineName,
                        claim.StateMachineName,
                    ],
                    [previous.Kickoff.Token, kickoff.Token],
                    [
                        previous.StateMachineType.Definition.Value,
                        stateMachineAddress.Definition.Value,
                    ],
                    [.. PresentRoles(previous).Select(
                        method => method.Method.Token)]);
                return;
            }
            if (!HasManagedIlBody(
                    _reader.GetMethodDefinition(claim.Kickoff)))
            {
                PublishRejection(
                    StateMachineRelationshipFailureKind.Malformed,
                    "The claimed kickoff method does not have a managed IL body.",
                    [kickoff],
                    [stateMachineAddress],
                    [claim.StateMachineName],
                    [kickoff.Token],
                    [stateMachineAddress.Definition.Value],
                    []);
                return;
            }

            ReadOnlySpan<StateMachineMethodRole> roles =
                StateMachineRelationship.RolesFor(claim.Kind);
            var dispositions =
                ImmutableArray.CreateBuilder<
                    StateMachineRoleDisposition>(roles.Length);
            foreach (StateMachineMethodRole roleKind in roles)
            {
                RoleSpec role = RoleSpec.For(claim.Kind, roleKind);
                RoleResolution resolution =
                    ResolveRole(
                        stateMachine,
                        role,
                        StateMachineRelationship.CanBeAbsent(
                            claim.Kind,
                            roleKind));
                if (resolution.Kind == RoleResolutionKind.Rejected)
                {
                    PublishRejection(
                        resolution.Failure,
                        resolution.Detail,
                        [kickoff],
                        [stateMachineAddress],
                        [claim.StateMachineName],
                        [kickoff.Token],
                        [stateMachineAddress.Definition.Value],
                        []);
                    return;
                }

                dispositions.Add(
                    resolution.Kind == RoleResolutionKind.Present
                    ? new StateMachineRoleDisposition.Present(
                        role.Role,
                        Address(resolution.Method))
                    : new StateMachineRoleDisposition.AbsentFromArtifact(
                        role.Role));
            }

            var relationship =
                new StateMachineRelationship(
                    kickoff,
                    stateMachineAddress,
                    claim.StateMachineName,
                    claim.Kind,
                    dispositions.ToImmutable());
            var resolved =
                new StateMachineRelationshipResult.Resolved(
                    relationship);

            var implementationTokens = new HashSet<int>();
            foreach (StateMachineRoleDisposition.Present method
                in PresentRoles(relationship))
            {
                if (!implementationTokens.Add(method.Method.Token))
                {
                    PublishRejection(
                        StateMachineRelationshipFailureKind.Ambiguous,
                        "One MethodDef implements multiple required state-machine roles.",
                        [relationship.Kickoff],
                        [relationship.StateMachineType],
                        [relationship.StateMachineName],
                        [relationship.Kickoff.Token],
                        [relationship.StateMachineType.Definition.Value],
                        [.. implementationTokens]);
                    return;
                }

                if (!_byImplementation.TryGetValue(
                        method.Method.Token,
                        out PendingResult existing))
                {
                    continue;
                }

                RejectImplementationCollision(
                    relationship,
                    existing,
                    method.Method.Token);
                return;
            }

            _relationships.Add(relationship);
            var resolvedEntry = PendingResult.From(resolved);
            _byKickoff[MetadataTokens.GetToken(claim.Kickoff)] =
                resolvedEntry;
            _byStateMachine[MetadataTokens.GetToken(stateMachine)] =
                resolvedEntry;
            foreach (StateMachineRoleDisposition.Present method
                in PresentRoles(relationship))
            {
                int token = method.Method.Token;
                _byImplementation[token] = resolvedEntry;
            }
        }

        void RejectImplementationCollision(
            StateMachineRelationship relationship,
            PendingResult existing,
            int implementationToken)
        {
            ImmutableArray<int> currentImplementations =
                [.. PresentRoles(relationship).Select(
                    method => method.Method.Token)];
            if (existing.Rejection is not null)
            {
                PublishRejection(
                    StateMachineRelationshipFailureKind.Ambiguous,
                    "One implementation MethodDef belongs to multiple state-machine relationships.",
                    [relationship.Kickoff],
                    [relationship.StateMachineType],
                    [relationship.StateMachineName],
                    [relationship.Kickoff.Token],
                    [relationship.StateMachineType.Definition.Value],
                    currentImplementations);
                return;
            }

            StateMachineRelationship previous =
                existing.Resolved!.Relationship;
            RemoveResolved(previous);
            PublishRejection(
                StateMachineRelationshipFailureKind.Ambiguous,
                "One implementation MethodDef belongs to multiple state-machine relationships.",
                [previous.Kickoff, relationship.Kickoff],
                [
                    previous.StateMachineType,
                    relationship.StateMachineType,
                ],
                [
                    previous.StateMachineName,
                    relationship.StateMachineName,
                ],
                [previous.Kickoff.Token, relationship.Kickoff.Token],
                [
                    previous.StateMachineType.Definition.Value,
                    relationship.StateMachineType.Definition.Value,
                ],
                [.. PresentRoles(previous)
                    .Select(method => method.Method.Token)
                    .Concat(currentImplementations)
                    .Append(implementationToken)
                    .Distinct()]);
        }

        void RemoveResolved(StateMachineRelationship relationship)
        {
            _relationships.Remove(relationship);
            _byKickoff.Remove(relationship.Kickoff.Token);
            _byStateMachine.Remove(
                relationship.StateMachineType.Definition.Value);
            foreach (StateMachineRoleDisposition.Present method
                in PresentRoles(relationship))
            {
                _byImplementation.Remove(method.Method.Token);
            }
        }

        static IEnumerable<StateMachineRoleDisposition.Present> PresentRoles(
            StateMachineRelationship relationship) =>
            relationship.Roles.OfType<
                StateMachineRoleDisposition.Present>();

        RoleResolution ResolveRole(
            TypeDefinitionHandle stateMachine,
            RoleSpec role,
            bool allowAbsent)
        {
            TypeDefinition type =
                _reader.GetTypeDefinition(stateMachine);
            EntityHandle implementedInterface =
                FindImplementedInterface(type, role.Interface);
            if (implementedInterface.IsNil)
            {
                return RoleResolution.Rejected(
                    StateMachineRelationshipFailureKind.Unresolved,
                    "The state-machine type does not implement a required interface.");
            }

            MethodDefinitionHandle explicitMethod = default;
            foreach (MethodImplementationHandle handle
                in type.GetMethodImplementations())
            {
                Charge();
                MethodImplementation implementation =
                    _reader.GetMethodImplementation(handle);
                bool isCandidate = allowAbsent
                    && _signatures.IsDeclarationCandidate(
                        implementation.MethodDeclaration,
                        role);
                if (!_signatures.MatchesDeclaration(
                        implementation.MethodDeclaration,
                        role,
                        out EntityHandle declaredInterface)
                    || role.Interface
                            == KnownStateMachineType.IAsyncEnumerator
                        && !SameConstructedInterface(
                            implementedInterface,
                            declaredInterface))
                {
                    if (isCandidate)
                    {
                        return RoleResolution.Rejected(
                            StateMachineRelationshipFailureKind.Unresolved,
                            "A state-machine MethodImpl declaration names an optional role but does not match its signature.");
                    }

                    continue;
                }
                if (!explicitMethod.IsNil
                    || implementation.MethodBody.Kind
                        != HandleKind.MethodDefinition)
                {
                    return RoleResolution.Rejected(
                        StateMachineRelationshipFailureKind.Ambiguous,
                        "A required state-machine role has multiple or invalid MethodImpl bodies.");
                }

                MethodDefinitionHandle body =
                    (MethodDefinitionHandle)implementation.MethodBody;
                if (!IsImplementationCandidate(
                        body,
                        stateMachine,
                        role,
                        requireImplicitVisibility: false))
                {
                    return RoleResolution.Rejected(
                        StateMachineRelationshipFailureKind.Malformed,
                        "A state-machine MethodImpl body does not match its required role.");
                }
                explicitMethod = body;
            }
            if (!explicitMethod.IsNil)
                return RoleResolution.Present(explicitMethod);

            MethodDefinitionHandle implicitMethod = default;
            foreach (MethodDefinitionHandle handle in type.GetMethods())
            {
                Charge();
                MethodDefinition method =
                    _reader.GetMethodDefinition(handle);
                if (!_reader.StringComparer.Equals(method.Name, role.Name))
                    continue;
                if (!IsImplementationCandidate(
                        handle,
                        stateMachine,
                        role,
                        requireImplicitVisibility: true))
                {
                    if (allowAbsent)
                    {
                        return RoleResolution.Rejected(
                            StateMachineRelationshipFailureKind.Unresolved,
                            "A state-machine MethodDef names an optional role but does not match its required shape.");
                    }

                    continue;
                }
                if (!implicitMethod.IsNil)
                {
                    return RoleResolution.Rejected(
                        StateMachineRelationshipFailureKind.Ambiguous,
                        "A required state-machine role has multiple implicit implementations.");
                }
                implicitMethod = handle;
            }

            if (!implicitMethod.IsNil)
                return RoleResolution.Present(implicitMethod);
            if (allowAbsent)
                return RoleResolution.AbsentFromArtifact();

            return RoleResolution.Rejected(
                StateMachineRelationshipFailureKind.Unresolved,
                "A required state-machine interface role could not be resolved.");
        }

        EntityHandle FindImplementedInterface(
            TypeDefinition type,
            KnownStateMachineType required)
        {
            EntityHandle match = default;
            foreach (InterfaceImplementationHandle handle
                in type.GetInterfaceImplementations())
            {
                Charge();
                InterfaceImplementation implementation =
                    _reader.GetInterfaceImplementation(handle);
                if (_signatures.IsKnownType(
                        implementation.Interface,
                        required))
                {
                    if (!match.IsNil)
                        return default;
                    match = implementation.Interface;
                }
            }
            return match;
        }

        bool SameConstructedInterface(
            EntityHandle left,
            EntityHandle right)
        {
            if (left == right)
                return true;
            if (left.Kind != HandleKind.TypeSpecification
                || right.Kind != HandleKind.TypeSpecification)
            {
                return false;
            }

            BlobReader leftSignature =
                _reader.GetBlobReader(
                    _reader.GetTypeSpecification(
                        (TypeSpecificationHandle)left).Signature);
            BlobReader rightSignature =
                _reader.GetBlobReader(
                    _reader.GetTypeSpecification(
                        (TypeSpecificationHandle)right).Signature);
            ChargeSignatureWork(leftSignature.Length);
            ChargeSignatureWork(rightSignature.Length);
            if (leftSignature.Length != rightSignature.Length)
                return false;
            while (leftSignature.RemainingBytes > 0)
            {
                if (leftSignature.ReadByte()
                    != rightSignature.ReadByte())
                {
                    return false;
                }
            }
            return true;
        }

        bool IsImplementationCandidate(
            MethodDefinitionHandle handle,
            TypeDefinitionHandle declaringType,
            RoleSpec role,
            bool requireImplicitVisibility)
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(handle);
            if (method.GetDeclaringType() != declaringType
                || (method.Attributes & MethodAttributes.Static) != 0
                || !HasManagedIlBody(method)
                || requireImplicitVisibility
                    && ((method.Attributes
                            & MethodAttributes.MemberAccessMask)
                                != MethodAttributes.Public
                        || (method.Attributes
                            & MethodAttributes.Virtual) == 0))
            {
                return false;
            }

            return _signatures.MatchesMethod(method, role);
        }

        static bool HasManagedIlBody(MethodDefinition method) =>
            method.RelativeVirtualAddress != 0
                && (method.Attributes
                    & MethodAttributes.PinvokeImpl) == 0
                && (method.ImplAttributes
                    & (MethodImplAttributes.CodeTypeMask
                        | MethodImplAttributes.ManagedMask
                        | MethodImplAttributes.InternalCall))
                    == MethodImplAttributes.IL;

        void RejectClaims(
            IReadOnlyList<Claim> claims,
            StateMachineRelationshipFailureKind kind,
            string detail,
            ImmutableArray<TypeDefinitionHandle> stateMachines = default)
        {
            ImmutableArray<MetadataMethodAddress> kickoffs =
                [.. claims
                    .Select(claim => Address(claim.Kickoff))
                    .Distinct()];
            ImmutableArray<MetadataTypeDefinitionAddress>
                stateMachineAddresses =
                stateMachines.IsDefaultOrEmpty
                    ? []
                    : [.. stateMachines.Select(TypeAddress)];
            PublishRejection(
                kind,
                detail,
                kickoffs,
                stateMachineAddresses,
                [.. claims
                    .Select(claim => claim.StateMachineName)
                    .Distinct()],
                [.. claims.Select(
                    claim => MetadataTokens.GetToken(
                        claim.Kickoff))],
                [.. stateMachineAddresses.Select(
                    stateMachine => stateMachine.Definition.Value)],
                []);
        }

        RejectionComponent PublishRejection(
            StateMachineRelationshipFailureKind kind,
            string detail,
            ImmutableArray<MetadataMethodAddress> kickoffs,
            ImmutableArray<MetadataTypeDefinitionAddress> stateMachines,
            ImmutableArray<MetadataTypeDefinitionName> claimedTypes,
            ImmutableArray<int> kickoffTokens,
            ImmutableArray<int> stateMachineTokens,
            ImmutableArray<int> implementationTokens)
        {
            var component = new RejectionComponent(
                kind,
                detail,
                kickoffs,
                stateMachines,
                claimedTypes);
            ObserveRejectionWork();
            _rejectionComponents.Add(component);
            MergeExisting(
                _byKickoff,
                kickoffTokens,
                component);
            MergeExisting(
                _byStateMachine,
                stateMachineTokens,
                component);
            MergeExisting(
                _byImplementation,
                implementationTokens,
                component);

            PendingResult pending = PendingResult.From(component);
            foreach (int token in kickoffTokens)
                _byKickoff[token] = pending;
            foreach (int token in stateMachineTokens)
                _byStateMachine[token] = pending;
            foreach (int token in implementationTokens)
                _byImplementation[token] = pending;

            return component;
        }

        void MergeExisting(
            IReadOnlyDictionary<int, PendingResult> index,
            ImmutableArray<int> tokens,
            RejectionComponent component)
        {
            foreach (int token in tokens)
            {
                ObserveRejectionWork();
                if (!index.TryGetValue(
                        token,
                        out PendingResult existing))
                {
                    continue;
                }
                if (existing.Rejection is null)
                {
                    throw new InvalidOperationException(
                        "A rejection overlapped a resolved relationship.");
                }
                RejectionComponent.Union(
                    component,
                    existing.Rejection);
            }
        }

        IReadOnlyDictionary<
            RejectionComponent,
            StateMachineRelationshipResult.Rejected>
            FreezeRejections()
        {
            var evidence = new Dictionary<
                RejectionComponent,
                RejectionEvidenceBuilder>();
            foreach (RejectionComponent component
                in _rejectionComponents)
            {
                ObserveRejectionWork();
                RejectionComponent root = component.Find();
                if (!evidence.TryGetValue(
                        root,
                        out RejectionEvidenceBuilder? builder))
                {
                    builder = new(
                        component.Kind,
                        component.Detail);
                    evidence.Add(root, builder);
                }
                builder.Add(component);
            }

            return evidence.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Freeze());
        }

        IReadOnlyDictionary<int, StateMachineRelationshipResult>
            Freeze(
                IReadOnlyDictionary<int, PendingResult> index,
                IReadOnlyDictionary<
                    RejectionComponent,
                    StateMachineRelationshipResult.Rejected>
                    rejections)
        {
            var frozen =
                new Dictionary<int, StateMachineRelationshipResult>(
                    index.Count);
            foreach ((int token, PendingResult pending) in index)
            {
                ObserveRejectionWork();
                frozen.Add(
                    token,
                    pending.Resolved is { } resolved
                        ? resolved
                        : rejections[
                            pending.Rejection!.Find()]);
            }
            return frozen;
        }

        void ObserveRejectionWork() =>
            _rejectionWorkObserved?.Invoke();

        readonly record struct PendingResult(
            StateMachineRelationshipResult.Resolved? Resolved,
            RejectionComponent? Rejection)
        {
            internal static PendingResult From(
                StateMachineRelationshipResult.Resolved resolved) =>
                new(resolved, Rejection: null);

            internal static PendingResult From(
                RejectionComponent rejection) =>
                new(Resolved: null, rejection);
        }

        sealed class RejectionComponent
        {
            RejectionComponent _parent;
            int _rank;

            internal RejectionComponent(
                StateMachineRelationshipFailureKind kind,
                string detail,
                ImmutableArray<MetadataMethodAddress> kickoffs,
                ImmutableArray<MetadataTypeDefinitionAddress> stateMachines,
                ImmutableArray<MetadataTypeDefinitionName> claimedTypes)
            {
                _parent = this;
                Kind = kind;
                Detail = detail;
                Kickoffs = kickoffs;
                StateMachines = stateMachines;
                ClaimedTypes = claimedTypes;
            }

            internal StateMachineRelationshipFailureKind Kind { get; }
            internal string Detail { get; }
            internal ImmutableArray<MetadataMethodAddress> Kickoffs { get; }
            internal ImmutableArray<MetadataTypeDefinitionAddress>
                StateMachines { get; }
            internal ImmutableArray<MetadataTypeDefinitionName>
                ClaimedTypes { get; }

            internal RejectionComponent Find()
            {
                if (!ReferenceEquals(_parent, this))
                    _parent = _parent.Find();
                return _parent;
            }

            internal static void Union(
                RejectionComponent left,
                RejectionComponent right)
            {
                RejectionComponent leftRoot = left.Find();
                RejectionComponent rightRoot = right.Find();
                if (ReferenceEquals(leftRoot, rightRoot))
                    return;

                if (leftRoot._rank < rightRoot._rank)
                {
                    leftRoot._parent = rightRoot;
                    return;
                }

                rightRoot._parent = leftRoot;
                if (leftRoot._rank == rightRoot._rank)
                    leftRoot._rank++;
            }
        }

        sealed class RejectionEvidenceBuilder(
            StateMachineRelationshipFailureKind kind,
            string detail)
        {
            readonly OrderedEvidence<MetadataMethodAddress> _kickoffs =
                new();
            readonly OrderedEvidence<MetadataTypeDefinitionAddress>
                _stateMachines = new();
            readonly OrderedEvidence<MetadataTypeDefinitionName>
                _claimedTypes = new();

            internal void Add(RejectionComponent component)
            {
                _kickoffs.AddRange(component.Kickoffs);
                _stateMachines.AddRange(component.StateMachines);
                _claimedTypes.AddRange(component.ClaimedTypes);
            }

            internal StateMachineRelationshipResult.Rejected Freeze() =>
                Rejected(
                    kind,
                    detail,
                    _kickoffs.Freeze(),
                    _stateMachines.Freeze(),
                    _claimedTypes.Freeze());
        }

        sealed class OrderedEvidence<T>
            where T : notnull
        {
            readonly HashSet<T> _seen = [];
            readonly List<T> _values = [];

            internal void AddRange(ImmutableArray<T> values)
            {
                foreach (T value in values)
                {
                    if (_seen.Add(value))
                        _values.Add(value);
                }
            }

            internal ImmutableArray<T> Freeze() =>
                [.. _values];
        }

        MetadataMethodAddress Address(
            MethodDefinitionHandle method) =>
            new(_moduleVersionId, method);

        MetadataTypeDefinitionAddress TypeAddress(
            TypeDefinitionHandle type) =>
            new(
                _moduleVersionId,
                TypeDefinitionToken.FromHandle(
                    _reader,
                    type));

        void Charge()
        {
            if (++_work > _relationshipBudget)
                throw new RelationshipBudgetException();
        }

        void ChargeNameWork(int characters)
        {
            _remainingNameWork -= Math.Max(characters, 1);
            if (_remainingNameWork < 0)
                throw new RelationshipBudgetException();
        }

        void ChargeSignatureWork(int bytes)
        {
            _remainingSignatureWork -= Math.Max(bytes, 1);
            if (_remainingSignatureWork < 0)
                throw new RelationshipBudgetException();
        }
    }

    sealed class StateMachineSignatureProvider :
        ISignatureTypeProvider<SignatureType, object?>
    {
        const byte ValueTypeCode = 0x11;
        const byte ClassTypeCode = 0x12;

        readonly MetadataReader _reader;
        readonly bool _currentAssemblyIsCoreLibrary;
        readonly Action<int> _beforeMaterialize;
        readonly Action<int> _beforeDecodeSignature;
        readonly AssemblyReferenceProjectionCache _assemblyReferences;
        readonly Dictionary<AssemblyReferenceHandle, bool>
            _platformAssemblies = [];
        readonly HashSet<BlobHandle> _chargedAssemblyKeys = [];

        internal StateMachineSignatureProvider(
            MetadataReader reader,
            Action<int> beforeMaterialize,
            Action<int> beforeDecodeSignature)
        {
            _reader = reader;
            _beforeMaterialize = beforeMaterialize;
            _beforeDecodeSignature = beforeDecodeSignature;
            _assemblyReferences =
                AssemblyReferenceIdentity.RetainedProjection(reader);
            _currentAssemblyIsCoreLibrary =
                CoreLibraryRootAuthentication
                    .DeclaresUniqueTopLevelCoreLibraryRoot(reader);
        }

        /// <summary>
        /// Answers whether a type reference's terminal assembly reference
        /// carries a platform public key. Every distinct assembly-reference row
        /// is projected once, and each distinct public-key blob is charged once
        /// against the name-work budget before it is copied and hashed. Without
        /// both, a type reference shared by many constructor member references
        /// re-copies and re-hashes the same unbounded key blob per constructor.
        /// </summary>
        bool TerminatesInPlatformAssembly(
            AssemblyReferenceHandle handle)
        {
            if (_platformAssemblies.TryGetValue(
                    handle,
                    out bool platform))
            {
                return platform;
            }

            System.Reflection.Metadata.AssemblyReference reference =
                _reader.GetAssemblyReference(handle);
            BlobHandle key = reference.PublicKeyOrToken;
            if (!key.IsNil && _chargedAssemblyKeys.Add(key))
                _beforeMaterialize(_reader.GetBlobReader(key).Length);

            // Projecting the row also decodes its name and culture. Distinct
            // rows can share one oversized name `StringHandle` while differing
            // by version, which defeats row-keyed projection caching, so the
            // decode really does repeat per row and has to be charged per row.
            ChargeAssemblyStrings(reference.Name, reference.Culture);

            platform = PlatformKeys.IsPlatform(
                AssemblyReferenceIdentity.From(
                    handle,
                    _assemblyReferences)
                    .PublicKeyToken);
            _platformAssemblies.Add(handle, platform);
            return platform;
        }

        /// <summary>
        /// Charges the encoded length of an assembly row's name and culture
        /// before either is decoded. <see cref="MetadataReader.GetBlobReader"/>
        /// measures the UTF-8 heap span without materializing a string, so the
        /// charge precedes the allocation it accounts for.
        /// </summary>
        void ChargeAssemblyStrings(
            StringHandle name,
            StringHandle culture)
        {
            if (!name.IsNil)
                _beforeMaterialize(_reader.GetBlobReader(name).Length);
            if (!culture.IsNil)
                _beforeMaterialize(_reader.GetBlobReader(culture).Length);
        }

        internal AttributeConstructorClassification
            ClassifyAttributeConstructor(
            EntityHandle constructor)
        {
            EntityHandle declaringType;
            StringHandle methodName;
            if (constructor.Kind == HandleKind.MemberReference)
            {
                MemberReference member =
                    _reader.GetMemberReference(
                        (MemberReferenceHandle)constructor);
                declaringType = member.Parent;
                methodName = member.Name;
            }
            else if (constructor.Kind
                == HandleKind.MethodDefinition)
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)constructor);
                declaringType = method.GetDeclaringType();
                methodName = method.Name;
            }
            else
            {
                return new(
                    StateMachineClaimKind.ClassicAsync,
                    AttributeConstructorStatus.NotTrusted);
            }

            SignatureType attributeType =
                DecodeType(declaringType, ClassTypeCode);
            if (attributeType.TypeNameFailure is { } typeNameFailure)
            {
                return new(
                    StateMachineClaimKind.ClassicAsync,
                    AttributeConstructorStatus.Unreadable,
                    typeNameFailure);
            }
            StateMachineClaimKind? kind =
                attributeType.Type switch
                {
                    KnownStateMachineType.AsyncStateMachineAttribute =>
                        StateMachineClaimKind.ClassicAsync,
                    KnownStateMachineType
                        .AsyncIteratorStateMachineAttribute =>
                        StateMachineClaimKind.AsyncIterator,
                    KnownStateMachineType.IteratorStateMachineAttribute =>
                        StateMachineClaimKind.Iterator,
                    _ => null,
                };
            if (kind is null
                || !attributeType.Is(
                    AttributeType(kind.Value)))
            {
                return new(
                    StateMachineClaimKind.ClassicAsync,
                    AttributeConstructorStatus.NotTrusted);
            }

            if (!_reader.StringComparer.Equals(methodName, ".ctor"))
            {
                return new(
                    kind.Value,
                    AttributeConstructorStatus.Malformed);
            }

            MethodSignature<SignatureType>? signature =
                constructor.Kind == HandleKind.MemberReference
                    ? Decode(
                        _reader.GetMemberReference(
                            (MemberReferenceHandle)constructor))
                    : Decode(
                        _reader.GetMethodDefinition(
                            (MethodDefinitionHandle)constructor));
            if (signature is not { } value
                || !IsInstanceDefault(value)
                || value.GenericParameterCount != 0
                || value.RequiredParameterCount != 1
                || value.ParameterTypes.Length != 1
                || !value.ReturnType.Is(
                    KnownStateMachineType.Void)
                || !value.ParameterTypes[0].Is(
                    KnownStateMachineType.Type))
            {
                return new(
                    kind.Value,
                    AttributeConstructorStatus.Malformed);
            }

            return new(
                kind.Value,
                AttributeConstructorStatus.Valid);
        }

        internal bool MatchesDeclaration(
            EntityHandle declaration,
            RoleSpec role,
            out EntityHandle declaringType)
        {
            MethodSignature<SignatureType>? signature;
            StringHandle name;
            if (declaration.Kind == HandleKind.MemberReference)
            {
                MemberReference member =
                    _reader.GetMemberReference(
                        (MemberReferenceHandle)declaration);
                declaringType = member.Parent;
                name = member.Name;
                signature = Decode(member);
            }
            else if (declaration.Kind
                == HandleKind.MethodDefinition)
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)declaration);
                declaringType = method.GetDeclaringType();
                name = method.Name;
                signature = Decode(method);
            }
            else
            {
                declaringType = default;
                name = default;
                return false;
            }

            return _reader.StringComparer.Equals(
                    name,
                    role.Name)
                && IsKnownType(declaringType, role.Interface)
                && signature is { } value
                && Matches(value, role);
        }

        internal bool IsDeclarationCandidate(
            EntityHandle declaration,
            RoleSpec role)
        {
            StringHandle name;
            EntityHandle declaringType;
            if (declaration.Kind == HandleKind.MemberReference)
            {
                MemberReference member =
                    _reader.GetMemberReference(
                        (MemberReferenceHandle)declaration);
                declaringType = member.Parent;
                name = member.Name;
            }
            else if (declaration.Kind
                == HandleKind.MethodDefinition)
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)declaration);
                declaringType = method.GetDeclaringType();
                name = method.Name;
            }
            else
            {
                return false;
            }

            return _reader.StringComparer.Equals(name, role.Name)
                && IsKnownType(declaringType, role.Interface);
        }

        internal bool MatchesMethod(
            MethodDefinition method,
            RoleSpec role)
            => Decode(method) is { } signature
                && Matches(signature, role);

        internal bool IsKnownType(
            EntityHandle handle,
            KnownStateMachineType expected)
            => DecodeType(
                handle,
                ClassTypeCode).Is(expected);

        bool Matches(
            MethodSignature<SignatureType> signature,
            RoleSpec role)
        {
            if (!IsInstanceDefault(signature)
                || signature.GenericParameterCount != 0
                || signature.RequiredParameterCount
                    != role.Parameters.Length
                || signature.ParameterTypes.Length
                    != role.Parameters.Length
                || !signature.ReturnType.Is(role.Return))
            {
                return false;
            }

            for (int i = 0; i < role.Parameters.Length; i++)
            {
                if (!signature.ParameterTypes[i].Is(
                        role.Parameters[i]))
                {
                    return false;
                }
            }
            return true;
        }

        static bool IsInstanceDefault(
            MethodSignature<SignatureType> signature)
            => signature.Header.RawValue == 0x20;

        MethodSignature<SignatureType>? Decode(
            MethodDefinition method)
        {
            ChargeSignature(method.Signature);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    method.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                return null;
            }
            return method.DecodeSignature(this, null);
        }

        MethodSignature<SignatureType>? Decode(
            MemberReference member)
        {
            ChargeSignature(member.Signature);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    member.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                return null;
            }
            return member.DecodeMethodSignature(
                this,
                null);
        }

        void ChargeSignature(BlobHandle signature)
        {
            int length = signature.IsNil
                ? 0
                : _reader.GetBlobReader(signature).Length;
            _beforeDecodeSignature(length);
        }

        SignatureType DecodeType(
            EntityHandle handle,
            byte directTypeKind)
            => handle.Kind switch
            {
                HandleKind.TypeDefinition =>
                    GetTypeFromDefinition(
                        _reader,
                        (TypeDefinitionHandle)handle,
                        directTypeKind),
                HandleKind.TypeReference =>
                    GetTypeFromReference(
                        _reader,
                        (TypeReferenceHandle)handle,
                        directTypeKind),
                HandleKind.TypeSpecification =>
                    DecodeTypeSpecification(
                        (TypeSpecificationHandle)handle,
                        context: null),
                _ => SignatureType.Unknown,
            };

        public SignatureType GetPrimitiveType(
            PrimitiveTypeCode typeCode)
            => typeCode switch
            {
                PrimitiveTypeCode.Boolean =>
                    SignatureType.Known(
                        KnownStateMachineType.Boolean,
                        SignatureReferenceKind.Primitive),
                PrimitiveTypeCode.Void =>
                    SignatureType.Known(
                        KnownStateMachineType.Void,
                        SignatureReferenceKind.Primitive),
                _ => SignatureType.Unknown,
            };

        public SignatureType GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            if (!_currentAssemblyIsCoreLibrary)
                return SignatureType.Unknown;
            return ReadKnownType(
                MetadataTypeDefinitionNameReader.Read(
                    reader,
                    handle,
                    _beforeMaterialize),
                rawTypeKind);
        }

        public SignatureType GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            bool walked = MetadataRelationshipTraversal
                .TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    chain,
                    out int consumedNodes,
                    out EntityHandle terminal,
                    out RelationshipTraversalRejection? rejection);

            // The walk itself is attacker-scaled work that happens before any
            // name is materialized, and it is not free: cycle detection
            // rescans the visited prefix at every step, so a `consumedNodes`
            // chain costs `consumedNodes * (consumedNodes - 1) / 2` handle
            // comparisons. Charging only on the platform-terminating path
            // would let a chain aimed at a non-platform assembly reference
            // buy that work for nothing, once per distinct constructor row.
            // Charge the comparisons actually performed, on every exit, so
            // the budget bounds the scan rather than only the allocation the
            // scan leads to.
            _beforeMaterialize(
                consumedNodes
                    + (consumedNodes * (consumedNodes - 1) / 2));

            if (!walked)
            {
                return SignatureType.Rejected(
                    MetadataTypeNameFailure.From(rejection!));
            }

            if (terminal.Kind
                    != HandleKind.AssemblyReference
                || !TerminatesInPlatformAssembly(
                    (AssemblyReferenceHandle)terminal))
            {
                return SignatureType.Unknown;
            }

            return ReadKnownType(
                MetadataTypeDefinitionNameReader.Read(
                    reader,
                    handle,
                    _beforeMaterialize),
                rawTypeKind);
        }

        public SignatureType GetTypeFromSpecification(
            MetadataReader reader,
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
            => DecodeTypeSpecification(handle, context);

        SignatureType DecodeTypeSpecification(
            TypeSpecificationHandle handle,
            object? context)
        {
            BlobHandle signature =
                _reader.GetTypeSpecification(handle).Signature;
            ChargeSignature(signature);
            if (!TypeSpecGuard.TryEnter(
                    _reader,
                    handle,
                    out var scope,
                    out SignatureDecodeRejectionKind rejectionKind))
            {
                return SignatureType.Rejected(
                    MetadataTypeNameFailure.From(
                        new SignatureDecodeRejection(
                            rejectionKind,
                            rejectionKind
                                == SignatureDecodeRejectionKind.UnsafeStructure
                                    ? "The TypeSpec exceeds the structural safety limit."
                                    : "The TypeSpec exceeds the re-entry depth or cumulative-byte budget."),
                        handle));
            }

            using (scope)
            {
                return _reader.GetTypeSpecification(handle)
                    .DecodeSignature(this, context);
            }
        }

        public SignatureType GetSZArrayType(
            SignatureType elementType) =>
            UnknownOrRejected(elementType);

        public SignatureType GetArrayType(
            SignatureType elementType,
            ArrayShape shape) =>
            UnknownOrRejected(elementType);

        public SignatureType GetByReferenceType(
            SignatureType elementType) =>
            UnknownOrRejected(elementType);

        public SignatureType GetPointerType(
            SignatureType elementType) =>
            UnknownOrRejected(elementType);

        public SignatureType GetPinnedType(
            SignatureType elementType) =>
            UnknownOrRejected(elementType);

        public SignatureType GetGenericInstantiation(
            SignatureType genericType,
            ImmutableArray<SignatureType> typeArguments)
        {
            if (FirstFailure(genericType, typeArguments) is { } failure)
                return SignatureType.Rejected(failure);

            if (genericType.Modified
                || typeArguments.Length != 1)
            {
                return SignatureType.Unknown;
            }

            return genericType.Type switch
            {
                KnownStateMachineType.IAsyncEnumeratorDefinition
                    when genericType.ReferenceKind
                        == SignatureReferenceKind.Class =>
                    SignatureType.Known(
                        KnownStateMachineType.IAsyncEnumerator,
                        SignatureReferenceKind.Class),
                KnownStateMachineType.ValueTaskOfTDefinition
                    when genericType.ReferenceKind
                            == SignatureReferenceKind.ValueType
                        && typeArguments is
                    [
                        {
                            Type: KnownStateMachineType.Boolean,
                            Modified: false,
                            ReferenceKind:
                                SignatureReferenceKind.Primitive,
                        },
                    ] =>
                    SignatureType.Known(
                        KnownStateMachineType.ValueTaskOfBoolean,
                        SignatureReferenceKind.ValueType),
                _ => SignatureType.Unknown,
            };
        }

        public SignatureType GetGenericTypeParameter(
            object? context,
            int index) =>
            SignatureType.Unknown;

        public SignatureType GetGenericMethodParameter(
            object? context,
            int index) =>
            SignatureType.Unknown;

        public SignatureType GetFunctionPointerType(
            MethodSignature<SignatureType> signature)
        {
            if (FirstFailure(
                    signature.ReturnType,
                    signature.ParameterTypes) is { } failure)
            {
                return SignatureType.Rejected(failure);
            }

            return SignatureType.Unknown;
        }

        public SignatureType GetModifiedType(
            SignatureType modifier,
            SignatureType unmodifiedType,
            bool isRequired)
        {
            if (modifier.TypeNameFailure is { } modifierFailure)
                return SignatureType.Rejected(modifierFailure);
            if (unmodifiedType.TypeNameFailure is { } unmodifiedFailure)
                return SignatureType.Rejected(unmodifiedFailure);

            return unmodifiedType with
            {
                Modified = true,
            };
        }

        static SignatureType UnknownOrRejected(SignatureType component) =>
            component.TypeNameFailure is { } failure
                ? SignatureType.Rejected(failure)
                : SignatureType.Unknown;

        static MetadataTypeNameFailure? FirstFailure(
            SignatureType first,
            ImmutableArray<SignatureType> remaining)
        {
            if (first.TypeNameFailure is { } failure)
                return failure;

            foreach (SignatureType type in remaining)
            {
                if (type.TypeNameFailure is { } nestedFailure)
                    return nestedFailure;
            }

            return null;
        }

        static SignatureType ReadKnownType(
            MetadataTypeDefinitionNameReadResult result,
            byte rawTypeKind)
            => result switch
            {
                MetadataTypeDefinitionNameReadResult.Read read =>
                    SignatureType.Known(
                        KnownType(read.Name),
                        ReferenceKind(rawTypeKind)),
                MetadataTypeDefinitionNameReadResult.Rejected rejected =>
                    SignatureType.Rejected(rejected.Failure),
                _ => throw new InvalidOperationException(
                    "Unknown metadata type-name read result."),
            };

        static SignatureReferenceKind ReferenceKind(
            byte rawTypeKind)
            => rawTypeKind switch
            {
                ClassTypeCode =>
                    SignatureReferenceKind.Class,
                ValueTypeCode =>
                    SignatureReferenceKind.ValueType,
                _ => SignatureReferenceKind.Unknown,
            };

        static KnownStateMachineType AttributeType(
            StateMachineClaimKind kind)
            => kind switch
            {
                StateMachineClaimKind.ClassicAsync =>
                    KnownStateMachineType.AsyncStateMachineAttribute,
                StateMachineClaimKind.AsyncIterator =>
                    KnownStateMachineType.AsyncIteratorStateMachineAttribute,
                StateMachineClaimKind.Iterator =>
                    KnownStateMachineType.IteratorStateMachineAttribute,
                _ => KnownStateMachineType.Unknown,
            };

        static KnownStateMachineType KnownType(
            MetadataTypeDefinitionName name)
        {
            if (name.Segments.Length != 1)
                return KnownStateMachineType.Unknown;
            string type = name.Segments[0];
            return (name.Namespace, type) switch
            {
                ("System", "Type") =>
                    KnownStateMachineType.Type,
                ("System", "Void") =>
                    KnownStateMachineType.Void,
                ("System", "Boolean") =>
                    KnownStateMachineType.Boolean,
                ("System", "IDisposable") =>
                    KnownStateMachineType.IDisposable,
                ("System.Collections", "IEnumerator") =>
                    KnownStateMachineType.IEnumerator,
                ("System.Collections.Generic", "IAsyncEnumerator`1") =>
                    KnownStateMachineType.IAsyncEnumeratorDefinition,
                ("System.Runtime.CompilerServices",
                    "IAsyncStateMachine") =>
                    KnownStateMachineType.IAsyncStateMachine,
                ("System.Runtime.CompilerServices",
                    "AsyncStateMachineAttribute") =>
                    KnownStateMachineType.AsyncStateMachineAttribute,
                ("System.Runtime.CompilerServices",
                    "AsyncIteratorStateMachineAttribute") =>
                    KnownStateMachineType.AsyncIteratorStateMachineAttribute,
                ("System.Runtime.CompilerServices",
                    "IteratorStateMachineAttribute") =>
                    KnownStateMachineType.IteratorStateMachineAttribute,
                ("System.Threading.Tasks", "ValueTask") =>
                    KnownStateMachineType.ValueTask,
                ("System.Threading.Tasks", "ValueTask`1") =>
                    KnownStateMachineType.ValueTaskOfTDefinition,
                ("System", "IAsyncDisposable") =>
                    KnownStateMachineType.IAsyncDisposable,
                _ => KnownStateMachineType.Unknown,
            };
        }
    }

    readonly record struct SignatureType(
        KnownStateMachineType Type,
        SignatureReferenceKind ReferenceKind,
        bool Modified,
        MetadataTypeNameFailure? TypeNameFailure)
    {
        internal static SignatureType Unknown =>
            new(
                KnownStateMachineType.Unknown,
                SignatureReferenceKind.Unknown,
                Modified: false,
                TypeNameFailure: null);

        internal static SignatureType Known(
            KnownStateMachineType type,
            SignatureReferenceKind referenceKind) =>
            new(
                type,
                referenceKind,
                Modified: false,
                TypeNameFailure: null);

        internal static SignatureType Rejected(
            MetadataTypeNameFailure failure) =>
            new(
                KnownStateMachineType.Unknown,
                SignatureReferenceKind.Unknown,
                Modified: false,
                failure);

        internal bool Is(KnownStateMachineType type)
            => TypeNameFailure is null
                && !Modified
                && Type == type
                && ReferenceKind == ExpectedReferenceKind(type);

        static SignatureReferenceKind ExpectedReferenceKind(
            KnownStateMachineType type)
            => type switch
            {
                KnownStateMachineType.Void
                    or KnownStateMachineType.Boolean =>
                    SignatureReferenceKind.Primitive,
                KnownStateMachineType.ValueTask
                    or KnownStateMachineType.ValueTaskOfTDefinition
                    or KnownStateMachineType.ValueTaskOfBoolean =>
                    SignatureReferenceKind.ValueType,
                KnownStateMachineType.Unknown =>
                    SignatureReferenceKind.Unknown,
                _ => SignatureReferenceKind.Class,
            };
    }

    enum SignatureReferenceKind
    {
        Unknown,
        Primitive,
        Class,
        ValueType,
    }

    enum KnownStateMachineType
    {
        Unknown,
        Void,
        Boolean,
        Type,
        IAsyncStateMachine,
        IEnumerator,
        IAsyncEnumeratorDefinition,
        IAsyncEnumerator,
        IDisposable,
        IAsyncDisposable,
        ValueTask,
        ValueTaskOfTDefinition,
        ValueTaskOfBoolean,
        AsyncStateMachineAttribute,
        AsyncIteratorStateMachineAttribute,
        IteratorStateMachineAttribute,
    }

    enum AttributeConstructorStatus
    {
        NotTrusted,
        Unreadable,
        Malformed,
        Valid,
    }

    readonly record struct AttributeConstructorClassification(
        StateMachineClaimKind Kind,
        AttributeConstructorStatus Status,
        MetadataTypeNameFailure? TypeNameFailure = null);

    sealed record RoleSpec(
        StateMachineMethodRole Role,
        KnownStateMachineType Interface,
        string Name,
        KnownStateMachineType Return,
        ImmutableArray<KnownStateMachineType> Parameters)
    {
        internal static RoleSpec For(
            StateMachineClaimKind kind,
            StateMachineMethodRole role) =>
            (kind, role) switch
            {
                (
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineMethodRole.MoveNext) => AsyncMoveNext,
                (
                    StateMachineClaimKind.ClassicAsync,
                    StateMachineMethodRole.SetStateMachine) => SetStateMachine,
                (
                    StateMachineClaimKind.AsyncIterator,
                    StateMachineMethodRole.MoveNext) => AsyncMoveNext,
                (
                    StateMachineClaimKind.AsyncIterator,
                    StateMachineMethodRole.SetStateMachine) => SetStateMachine,
                (
                    StateMachineClaimKind.AsyncIterator,
                    StateMachineMethodRole.MoveNextAsync) => MoveNextAsync,
                (
                    StateMachineClaimKind.AsyncIterator,
                    StateMachineMethodRole.DisposeAsync) => DisposeAsync,
                (
                    StateMachineClaimKind.Iterator,
                    StateMachineMethodRole.MoveNext) => IteratorMoveNext,
                (
                    StateMachineClaimKind.Iterator,
                    StateMachineMethodRole.Dispose) => Dispose,
                _ => throw new InvalidOperationException(
                    "Unknown state-machine role."),
            };

        internal static RoleSpec AsyncMoveNext { get; } =
            new(
                StateMachineMethodRole.MoveNext,
                KnownStateMachineType.IAsyncStateMachine,
                "MoveNext",
                KnownStateMachineType.Void,
                []);

        internal static RoleSpec SetStateMachine { get; } =
            new(
                StateMachineMethodRole.SetStateMachine,
                KnownStateMachineType.IAsyncStateMachine,
                "SetStateMachine",
                KnownStateMachineType.Void,
                [KnownStateMachineType.IAsyncStateMachine]);

        internal static RoleSpec IteratorMoveNext { get; } =
            new(
                StateMachineMethodRole.MoveNext,
                KnownStateMachineType.IEnumerator,
                "MoveNext",
                KnownStateMachineType.Boolean,
                []);

        internal static RoleSpec MoveNextAsync { get; } =
            new(
                StateMachineMethodRole.MoveNextAsync,
                KnownStateMachineType.IAsyncEnumerator,
                "MoveNextAsync",
                KnownStateMachineType.ValueTaskOfBoolean,
                []);

        internal static RoleSpec Dispose { get; } =
            new(
                StateMachineMethodRole.Dispose,
                KnownStateMachineType.IDisposable,
                "Dispose",
                KnownStateMachineType.Void,
                []);

        internal static RoleSpec DisposeAsync { get; } =
            new(
                StateMachineMethodRole.DisposeAsync,
                KnownStateMachineType.IAsyncDisposable,
                "DisposeAsync",
                KnownStateMachineType.ValueTask,
                []);
    }

    readonly record struct Claim(
        MethodDefinitionHandle Kickoff,
        StateMachineClaimKind Kind,
        MetadataTypeDefinitionName StateMachineName);

    /// <summary>
    /// Outcome of validating a claim attribute's value blob before decode.
    /// </summary>
    enum ClaimValueShape
    {
        Valid,
        Malformed,
        Oversized,
    }

    readonly record struct ClaimCandidate(
        StateMachineClaimKind Kind,
        MetadataTypeDefinitionName? StateMachineName,
        StateMachineRelationshipFailureKind? Failure,
        string? Detail)
    {
        internal static ClaimCandidate Rejected(
            StateMachineClaimKind kind,
            StateMachineRelationshipFailureKind failure,
            string detail) =>
            new(
                kind,
                StateMachineName: null,
                failure,
                detail);
    }

    enum RoleResolutionKind
    {
        Present,
        AbsentFromArtifact,
        Rejected,
    }

    readonly record struct RoleResolution(
        RoleResolutionKind Kind,
        MethodDefinitionHandle Method,
        StateMachineRelationshipFailureKind Failure,
        string Detail)
    {
        internal static RoleResolution Present(
            MethodDefinitionHandle method) =>
            new(
                RoleResolutionKind.Present,
                method,
                StateMachineRelationshipFailureKind.Unresolved,
                "");

        internal static RoleResolution AbsentFromArtifact() =>
            new(
                RoleResolutionKind.AbsentFromArtifact,
                default,
                StateMachineRelationshipFailureKind.Unresolved,
                "");

        internal static RoleResolution Rejected(
            StateMachineRelationshipFailureKind failure,
            string detail) =>
            new(
                RoleResolutionKind.Rejected,
                default,
                failure,
                detail);
    }

    sealed class RelationshipBudgetException : Exception;
}
