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
/// gates exact role selection and
/// <c>StateMachineRelationshipIndex_PropagatesTypedBudgetFailure</c> and
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
        Relationships = relationships;
        _globalFailure = globalFailure;
    }

    public ImmutableArray<StateMachineRelationship> Relationships { get; }

    public static StateMachineRelationshipIndex Create(
        MetadataReader reader)
        => Create(
            reader,
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars);

    internal static StateMachineRelationshipIndex Create(
        MetadataReader reader,
        int relationshipBudget,
        int methodRowBudget =
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        int nameWorkBudget =
            MetadataSafetyPolicy.MaxStructuralSignatureWorkChars)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            relationshipBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            methodRowBudget);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            nameWorkBudget);

        try
        {
            return new Builder(
                reader,
                relationshipBudget,
                methodRowBudget,
                nameWorkBudget).Build();
        }
        catch (RelationshipBudgetException)
        {
            return Failed(
                reader,
                StateMachineRelationshipFailureKind.BudgetExceeded,
                "State-machine relationship discovery exceeded its work budget.");
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or InvalidOperationException
                or OverflowException)
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
            moduleVersionId =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
            methodRows =
                reader.GetTableRowCount(TableIndex.MethodDef);
            typeRows =
                reader.GetTableRowCount(TableIndex.TypeDef);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException)
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
        readonly Dictionary<int, StateMachineRelationshipResult> _byKickoff =
            [];
        readonly Dictionary<int, StateMachineRelationshipResult>
            _byStateMachine = [];
        readonly Dictionary<int, StateMachineRelationshipResult>
            _byImplementation = [];
        readonly List<StateMachineRelationship> _relationships = [];
        readonly List<Claim> _claims = [];
        long _remainingNameWork;
        int _work;

        internal Builder(
            MetadataReader reader,
            int relationshipBudget,
            int methodRowBudget,
            int nameWorkBudget)
        {
            _reader = reader;
            _relationshipBudget = relationshipBudget;
            _methodRowBudget = methodRowBudget;
            _remainingNameWork = nameWorkBudget;
            _moduleVersionId =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
            _typeDefinitions =
                MetadataTypeDefinitionIndex.Create(reader);
            _signatures = new(
                reader,
                ChargeNameWork);
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

            return new(
                _moduleVersionId,
                _reader.GetTableRowCount(TableIndex.MethodDef),
                _reader.GetTableRowCount(TableIndex.TypeDef),
                _byKickoff,
                _byStateMachine,
                _byImplementation,
                [.. _relationships],
                globalFailure: null);
        }

        void ReadClaims(MethodDefinitionHandle kickoff)
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(kickoff);
            var candidates = new List<ClaimCandidate>();
            foreach (CustomAttributeHandle attributeHandle
                in method.GetCustomAttributes())
            {
                Charge();
                CustomAttribute attribute =
                    _reader.GetCustomAttribute(attributeHandle);
                if (!_attributeConstructors.TryGetValue(
                        attribute.Constructor,
                        out AttributeConstructorClassification
                            classification))
                {
                    classification =
                        _signatures.ClassifyAttributeConstructor(
                            attribute.Constructor);
                    _attributeConstructors.Add(
                        attribute.Constructor,
                        classification);
                }

                if (classification.Status
                    == AttributeConstructorStatus.NotTrusted)
                {
                    continue;
                }

                if (candidates.Count
                    == MetadataSafetyPolicy.MaxRelationshipNodes)
                {
                    _byKickoff[MetadataTokens.GetToken(kickoff)] =
                        Rejected(
                            StateMachineRelationshipFailureKind.BudgetExceeded,
                            "One kickoff method exceeds the state-machine claim budget.",
                            [Address(kickoff)]);
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
                _byKickoff[MetadataTokens.GetToken(kickoff)] =
                    Rejected(
                        candidate.Failure.Value,
                        candidate.Detail!,
                        [Address(kickoff)]);
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
            if (SerializedTypeArgumentExceedsByteBudget(attribute))
            {
                return ClaimCandidate.Rejected(
                    kind,
                    StateMachineRelationshipFailureKind.Malformed,
                    "The state-machine type name exceeds its encoded byte budget.");
            }

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

        bool SerializedTypeArgumentExceedsByteBudget(
            CustomAttribute attribute)
        {
            try
            {
                BlobReader value =
                    _reader.GetBlobReader(attribute.Value);
                if (value.RemainingBytes < 3
                    || value.ReadUInt16() != 1)
                {
                    return false;
                }

                int offset = value.Offset;
                if (value.ReadByte() == 0xFF)
                    return false;
                value.Offset = offset;
                int byteCount = value.ReadCompressedInteger();
                return byteCount
                    > MetadataTypeNameBudget.MaxEncodedBytes;
            }
            catch (Exception ex) when (
                ex is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                return false;
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

        bool AssemblyQualificationMatches(
            AssemblyNameInfo qualification)
        {
            if (!_reader.IsAssembly)
                return false;

            AssemblyReferenceIdentity assembly =
                AssemblyReferenceIdentity.FromAssemblyDefinition(
                    _reader);
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
            if (!string.IsNullOrEmpty(qualification.CultureName)
                && !string.Equals(
                    qualification.CultureName,
                    assembly.Culture,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if ((qualification.Flags
                    & AssemblyNameFlags.PublicKey) != 0)
            {
                return false;
            }
            if (!qualification.PublicKeyOrToken.IsDefaultOrEmpty
                && !string.Equals(
                    Convert.ToHexString(
                        qualification.PublicKeyOrToken.AsSpan()),
                    assembly.PublicKeyToken,
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
            var stateMachines =
                ImmutableArray.CreateBuilder<
                    MetadataTypeDefinitionAddress>();
            foreach (MetadataTypeDefinitionName claimedType
                in claimedTypes)
            {
                if (_typeDefinitions.TryGetDefinitions(
                        claimedType,
                        out ImmutableArray<TypeDefinitionHandle>
                            matchingTypes,
                        out _))
                {
                    foreach (TypeDefinitionHandle stateMachine
                        in matchingTypes)
                    {
                        stateMachines.Add(
                            TypeAddress(stateMachine));
                    }
                }
            }

            StateMachineRelationshipResult.Rejected rejection =
                Rejected(
                    kind,
                    detail,
                    [Address(kickoff)],
                    stateMachines.ToImmutable(),
                    claimedTypes);
            _byKickoff[MetadataTokens.GetToken(kickoff)] =
                rejection;
            foreach (MetadataTypeDefinitionAddress stateMachine
                in stateMachines)
            {
                _byStateMachine[
                    stateMachine.Definition.Value] = rejection;
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
                    out StateMachineRelationshipResult?
                        priorRejection))
            {
                StateMachineRelationshipResult merged =
                    MergePriorRejection(
                        priorRejection,
                        kickoff,
                        stateMachineAddress,
                        claim.StateMachineName);
                _byKickoff[kickoff.Token] = merged;
                _byStateMachine[
                    stateMachineAddress.Definition.Value] = merged;
                return;
            }
            if (!HasManagedIlBody(
                    _reader.GetMethodDefinition(claim.Kickoff)))
            {
                StateMachineRelationshipResult.Rejected rejection =
                    Rejected(
                        StateMachineRelationshipFailureKind.Malformed,
                        "The claimed kickoff method does not have a managed IL body.",
                        [kickoff],
                        [stateMachineAddress],
                        [claim.StateMachineName]);
                _byKickoff[MetadataTokens.GetToken(claim.Kickoff)] =
                    rejection;
                _byStateMachine[
                    MetadataTokens.GetToken(stateMachine)] =
                        rejection;
                return;
            }

            RoleSpec[] roles = claim.Kind switch
            {
                StateMachineClaimKind.ClassicAsync =>
                [
                    RoleSpec.AsyncMoveNext,
                    RoleSpec.SetStateMachine,
                ],
                StateMachineClaimKind.AsyncIterator =>
                [
                    RoleSpec.AsyncMoveNext,
                    RoleSpec.SetStateMachine,
                    RoleSpec.MoveNextAsync,
                    RoleSpec.DisposeAsync,
                ],
                StateMachineClaimKind.Iterator =>
                [
                    RoleSpec.IteratorMoveNext,
                    RoleSpec.Dispose,
                ],
                _ => throw new InvalidOperationException(
                    "Unknown state-machine claim kind."),
            };

            var methods =
                ImmutableArray.CreateBuilder<
                    StateMachineMethodRelationship>(
                        roles.Length);
            foreach (RoleSpec role in roles)
            {
                RoleResolution resolution =
                    ResolveRole(stateMachine, role);
                if (resolution.Method.IsNil)
                {
                    StateMachineRelationshipResult.Rejected rejection =
                        Rejected(
                            resolution.Failure,
                            resolution.Detail,
                            [kickoff],
                            [stateMachineAddress],
                            [claim.StateMachineName]);
                    _byKickoff[MetadataTokens.GetToken(claim.Kickoff)] =
                        rejection;
                    _byStateMachine[
                        MetadataTokens.GetToken(stateMachine)] =
                            rejection;
                    return;
                }

                methods.Add(
                    new(
                        role.Role,
                        Address(resolution.Method)));
            }

            var relationship =
                new StateMachineRelationship(
                    kickoff,
                    stateMachineAddress,
                    claim.StateMachineName,
                    claim.Kind,
                    methods.ToImmutable());
            var resolved =
                new StateMachineRelationshipResult.Resolved(
                    relationship);

            var implementationTokens = new HashSet<int>();
            foreach (StateMachineMethodRelationship method
                in relationship.Methods)
            {
                if (!implementationTokens.Add(method.Method.Token))
                {
                    StateMachineRelationshipResult.Rejected rejection =
                        Rejected(
                            StateMachineRelationshipFailureKind.Ambiguous,
                            "One MethodDef implements multiple required state-machine roles.",
                            [relationship.Kickoff],
                            [relationship.StateMachineType],
                            [relationship.StateMachineName]);
                    _byKickoff[relationship.Kickoff.Token] = rejection;
                    _byStateMachine[
                        relationship.StateMachineType.Definition.Value] =
                            rejection;
                    return;
                }

                if (!_byImplementation.TryGetValue(
                        method.Method.Token,
                        out StateMachineRelationshipResult? existing))
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
            _byKickoff[MetadataTokens.GetToken(claim.Kickoff)] =
                resolved;
            _byStateMachine[MetadataTokens.GetToken(stateMachine)] =
                resolved;
            foreach (StateMachineMethodRelationship method
                in relationship.Methods)
            {
                int token = method.Method.Token;
                _byImplementation[token] = resolved;
            }
        }

        void RejectImplementationCollision(
            StateMachineRelationship relationship,
            StateMachineRelationshipResult existing,
            int implementationToken)
        {
            if (existing
                is not StateMachineRelationshipResult.Resolved prior)
            {
                _byKickoff[relationship.Kickoff.Token] = existing;
                _byStateMachine[
                    relationship.StateMachineType.Definition.Value] = existing;
                return;
            }

            StateMachineRelationship previous = prior.Relationship;
            var rejection = Rejected(
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
                ]);
            _relationships.Remove(previous);
            _byKickoff[previous.Kickoff.Token] = rejection;
            _byKickoff[relationship.Kickoff.Token] = rejection;
            _byStateMachine[
                previous.StateMachineType.Definition.Value] = rejection;
            _byStateMachine[
                relationship.StateMachineType.Definition.Value] = rejection;
            foreach (StateMachineMethodRelationship method
                in previous.Methods)
            {
                _byImplementation[method.Method.Token] = rejection;
            }
            _byImplementation[implementationToken] = rejection;
        }

        RoleResolution ResolveRole(
            TypeDefinitionHandle stateMachine,
            RoleSpec role)
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
                return RoleResolution.Resolved(explicitMethod);

            MethodDefinitionHandle implicitMethod = default;
            foreach (MethodDefinitionHandle handle in type.GetMethods())
            {
                Charge();
                MethodDefinition method =
                    _reader.GetMethodDefinition(handle);
                if (!_reader.StringComparer.Equals(
                        method.Name,
                        role.Name)
                    || !IsImplementationCandidate(
                        handle,
                        stateMachine,
                        role,
                        requireImplicitVisibility: true))
                {
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

            return implicitMethod.IsNil
                ? RoleResolution.Rejected(
                    StateMachineRelationshipFailureKind.Unresolved,
                    "A required state-machine interface role could not be resolved.")
                : RoleResolution.Resolved(implicitMethod);
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
            StateMachineRelationshipResult.Rejected rejection =
                Rejected(
                    kind,
                    detail,
                    kickoffs,
                    stateMachineAddresses,
                    [.. claims
                        .Select(claim => claim.StateMachineName)
                        .Distinct()]);
            foreach (Claim claim in claims)
            {
                _byKickoff[MetadataTokens.GetToken(claim.Kickoff)] =
                    rejection;
            }
            foreach (TypeDefinitionHandle stateMachine
                in stateMachines.IsDefault
                    ? []
                    : stateMachines)
            {
                _byStateMachine[
                    MetadataTokens.GetToken(stateMachine)] =
                        rejection;
            }
        }

        StateMachineRelationshipResult.Rejected MergePriorRejection(
            StateMachineRelationshipResult prior,
            MetadataMethodAddress kickoff,
            MetadataTypeDefinitionAddress stateMachine,
            MetadataTypeDefinitionName claimedType)
        {
            if (prior
                is not StateMachineRelationshipResult.Rejected rejected)
            {
                return Rejected(
                    StateMachineRelationshipFailureKind.Ambiguous,
                    "The state-machine type has conflicting relationships.",
                    [kickoff],
                    [stateMachine],
                    [claimedType]);
            }

            StateMachineRelationshipFailure failure =
                rejected.Failure;
            var merged = Rejected(
                failure.Kind,
                failure.Detail,
                failure.KickoffCandidates
                    .Append(kickoff)
                    .Distinct()
                    .ToImmutableArray(),
                failure.StateMachineCandidates
                    .Append(stateMachine)
                    .Distinct()
                    .ToImmutableArray(),
                failure.ClaimedTypes
                    .Append(claimedType)
                    .Distinct()
                    .ToImmutableArray());
            ReplaceRejection(_byKickoff, rejected, merged);
            ReplaceRejection(_byStateMachine, rejected, merged);
            ReplaceRejection(_byImplementation, rejected, merged);
            return merged;
        }

        static void ReplaceRejection(
            Dictionary<int, StateMachineRelationshipResult> index,
            StateMachineRelationshipResult.Rejected previous,
            StateMachineRelationshipResult.Rejected replacement)
        {
            int[] keys =
                [.. index
                    .Where(pair =>
                        ReferenceEquals(pair.Value, previous))
                    .Select(pair => pair.Key)];
            foreach (int key in keys)
                index[key] = replacement;
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
    }

    sealed class StateMachineSignatureProvider :
        ISignatureTypeProvider<SignatureType, object?>
    {
        const byte ValueTypeCode = 0x11;
        const byte ClassTypeCode = 0x12;

        readonly MetadataReader _reader;
        readonly bool _currentAssemblyIsCoreLibrary;
        readonly Action<int> _beforeMaterialize;

        internal StateMachineSignatureProvider(
            MetadataReader reader,
            Action<int> beforeMaterialize)
        {
            _reader = reader;
            _beforeMaterialize = beforeMaterialize;
            _currentAssemblyIsCoreLibrary =
                CoreLibraryRootAuthentication
                    .DeclaresUniqueTopLevelCoreLibraryRoot(reader);
        }

        internal AttributeConstructorClassification
            ClassifyAttributeConstructor(
            EntityHandle constructor)
        {
            MethodSignature<SignatureType>? signature;
            EntityHandle declaringType;
            StringHandle methodName;
            if (constructor.Kind == HandleKind.MemberReference)
            {
                MemberReference member =
                    _reader.GetMemberReference(
                        (MemberReferenceHandle)constructor);
                declaringType = member.Parent;
                methodName = member.Name;
                signature = Decode(member);
            }
            else if (constructor.Kind
                == HandleKind.MethodDefinition)
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)constructor);
                declaringType = method.GetDeclaringType();
                methodName = method.Name;
                signature = Decode(method);
            }
            else
            {
                return new(
                    StateMachineClaimKind.ClassicAsync,
                    AttributeConstructorStatus.NotTrusted);
            }

            SignatureType attributeType =
                DecodeType(declaringType, ClassTypeCode);
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

            if (!_reader.StringComparer.Equals(
                    methodName,
                    ".ctor")
                || signature is not { } value
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
                    GuardedProviderDecode.TypeSpec(
                        _reader,
                        (TypeSpecificationHandle)handle,
                        this,
                        context: null,
                        SignatureType.Unknown),
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
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        reader,
                        handle,
                        chain,
                        out _,
                        out EntityHandle terminal,
                        out _)
                || terminal.Kind
                    != HandleKind.AssemblyReference
                || !PlatformKeys.IsPlatform(
                    AssemblyReferenceIdentity.From(
                        reader,
                        (AssemblyReferenceHandle)terminal)
                        .PublicKeyToken))
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
            => GuardedProviderDecode.TypeSpec(
                reader,
                handle,
                this,
                context,
                SignatureType.Unknown);

        public SignatureType GetSZArrayType(
            SignatureType elementType) =>
            SignatureType.Unknown;

        public SignatureType GetArrayType(
            SignatureType elementType,
            ArrayShape shape) =>
            SignatureType.Unknown;

        public SignatureType GetByReferenceType(
            SignatureType elementType) =>
            SignatureType.Unknown;

        public SignatureType GetPointerType(
            SignatureType elementType) =>
            SignatureType.Unknown;

        public SignatureType GetPinnedType(
            SignatureType elementType) =>
            SignatureType.Unknown;

        public SignatureType GetGenericInstantiation(
            SignatureType genericType,
            ImmutableArray<SignatureType> typeArguments)
        {
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
            MethodSignature<SignatureType> signature) =>
            SignatureType.Unknown;

        public SignatureType GetModifiedType(
            SignatureType modifier,
            SignatureType unmodifiedType,
            bool isRequired) =>
            unmodifiedType with
            {
                Modified = true,
            };

        static SignatureType ReadKnownType(
            MetadataTypeDefinitionNameReadResult result,
            byte rawTypeKind)
            => result is MetadataTypeDefinitionNameReadResult.Read read
                ? SignatureType.Known(
                    KnownType(read.Name),
                    ReferenceKind(rawTypeKind))
                : SignatureType.Unknown;

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
        bool Modified)
    {
        internal static SignatureType Unknown =>
            new(
                KnownStateMachineType.Unknown,
                SignatureReferenceKind.Unknown,
                Modified: false);

        internal static SignatureType Known(
            KnownStateMachineType type,
            SignatureReferenceKind referenceKind) =>
            new(type, referenceKind, Modified: false);

        internal bool Is(KnownStateMachineType type)
            => !Modified
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
        Malformed,
        Valid,
    }

    readonly record struct AttributeConstructorClassification(
        StateMachineClaimKind Kind,
        AttributeConstructorStatus Status);

    sealed record RoleSpec(
        StateMachineMethodRole Role,
        KnownStateMachineType Interface,
        string Name,
        KnownStateMachineType Return,
        ImmutableArray<KnownStateMachineType> Parameters)
    {
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

    readonly record struct RoleResolution(
        MethodDefinitionHandle Method,
        StateMachineRelationshipFailureKind Failure,
        string Detail)
    {
        internal static RoleResolution Resolved(
            MethodDefinitionHandle method) =>
            new(
                method,
                StateMachineRelationshipFailureKind.Unresolved,
                "");

        internal static RoleResolution Rejected(
            StateMachineRelationshipFailureKind failure,
            string detail) =>
            new(default, failure, detail);
    }

    sealed class RelationshipBudgetException : Exception;
}
