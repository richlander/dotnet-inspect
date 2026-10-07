using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using CSharpText;
using ILInspector.CSharp;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.ResearchQueries;

public abstract record ReturnToSenderDeclarationSelection
{
    private ReturnToSenderDeclarationSelection()
    {
    }

    public sealed record OrdinaryMethod
        : ReturnToSenderDeclarationSelection;

    public sealed record ExactMethod(
        CSharpAcceptedDeclarationRequest Request)
        : ReturnToSenderDeclarationSelection;

    public sealed record ExactAccessor(
        CSharpAcceptedAccessorDeclarationRequest Request)
        : ReturnToSenderDeclarationSelection;
}

public enum ReturnToSenderDeclarationProducer
{
    OrdinaryTypeArtifact,
    ExactMethodDeclaration,
    ExactAccessorDeclaration,
}

public enum ReturnToSenderTargetExclusionReason
{
    ProductMemberUnavailable,
    OrdinaryDeclarationUnrepresentable,
    ExactDeclarationUnrepresentable,
    ExactDeclarationUnavailable,
    ExactAccessorDeclarationUnrepresentable,
    ExactAccessorDeclarationUnavailable,
    CanonicalSignatureUnavailable,
}

public sealed record ReturnToSenderTarget(
    string AssemblyPath,
    string Type,
    string Method,
    int Overload,
    string Signature,
    MetadataMethodAddress Address,
    ReturnToSenderDeclarationSelection Declaration);

public sealed record ReturnToSenderTargetExclusion(
    string AssemblyPath,
    string Type,
    string Method,
    int Overload,
    string? Signature,
    MetadataMethodAddress Address,
    ReturnToSenderTargetExclusionReason Reason,
    ReturnToSenderDeclarationProducer? Producer = null,
    CSharpDeclarationRepresentabilityResult? ExactOutcome = null,
    CSharpAccessorDeclarationRepresentabilityResult? AccessorOutcome =
        null);

public sealed record ReturnToSenderTargetDecision(
    ReturnToSenderTarget? Target,
    ReturnToSenderTargetExclusion? Exclusion,
    string StableIdentitySuffix);

public sealed record ReturnToSenderTargetSourceCount(
    int EligibleCount,
    int ScannedBodyCount,
    int DeclarationCandidateCount,
    int MaterializedRowCount);

/// <summary>
/// One capped selection and the work it performed. API evidence is
/// materialized only for target Types in the first
/// <paramref name="ApiEvidencePrefixLength"/> ranked candidates.
/// </summary>
public sealed record ReturnToSenderCappedTargetSelection(
    IReadOnlyList<ReturnToSenderTarget> Targets,
    int RankedBodyCount,
    int EvaluatedBodyCount,
    int DeclarationCandidateCount,
    int ExcludedDeclarationCandidateCount,
    int ApiEvidencePrefixLength,
    int ApiTypesMaterialized);

public sealed class ReturnToSenderTargetSourceSession : IDisposable
{
    private readonly string _assemblyIdentity;
    private readonly AssemblyInspectionSession _assembly;
    private readonly MetadataOperationContext _operation;
    private readonly MetadataDeclarationSession _declarations;
    private readonly CSharpLanguageProfile _languageProfile;
    private readonly TargetApiEvidence _targetApiEvidence = new();

    public ReturnToSenderTargetSourceSession(
        string assemblyIdentity,
        AssemblyInspectionSession assembly,
        CSharpLanguageProfile? languageProfile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyIdentity);
        ArgumentNullException.ThrowIfNull(assembly);

        _assemblyIdentity = assemblyIdentity;
        _assembly = assembly;
        _operation = new MetadataOperationContext(
            MetadataOperationPolicy.Unbounded);
        _declarations = assembly.CreateDeclarationSession(_operation);
        _languageProfile =
            languageProfile
            ?? new CSharpLanguageProfile(CSharpLanguageVersion.Preview);
    }

    public ReturnToSenderTargetDecision? Decide(
        IrImporter.StableSampleCandidate candidate,
        string? typeFilter,
        out bool declarationCandidate)
    {
        TargetEvaluation evaluation =
            _assembly.InspectImage(
                pe =>
                {
                    MetadataReader reader = pe.GetMetadataReader();
                    if (IsTargetType(reader, candidate, typeFilter))
                    {
                        _targetApiEvidence.Materialize(
                            pe,
                            [candidate.TypeDefHandle]);
                    }
                    return Evaluate(
                        reader,
                        candidate,
                        typeFilter,
                        materializeDecision: true);
                });
        declarationCandidate = evaluation.DeclarationCandidate;
        return evaluation.Decision;
    }

    public ReturnToSenderTargetSourceCount Count(
        string? typeFilter = null) =>
        _assembly.InspectImage(
            pe => Count(
                pe,
                typeFilter));

    public ReturnToSenderCappedTargetSelection SelectCappedTargets(
        MetadataSource metadata,
        int cap,
        string? typeFilter = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cap);

        IReadOnlyList<IrImporter.StableRankedSampleCandidate>
            ranked =
                IrImporter.GetStableRankedSampleCandidates(
                    metadata,
                    typeFilter is null
                        ? null
                        : candidate =>
                            candidate.TypeName.Contains(
                                typeFilter,
                                StringComparison.Ordinal),
                    includeGenericArity: true);
        return _assembly.InspectImage(
            pe => SelectCappedTargets(
                pe,
                ranked,
                cap,
                typeFilter));
    }

    public void Dispose()
    {
        _declarations.Dispose();
        _operation.Dispose();
    }

    private ReturnToSenderTargetSourceCount Count(
        PEReader pe,
        string? typeFilter)
    {
        MetadataReader reader = pe.GetMetadataReader();
        var targetTypes = new List<TypeDefinitionHandle>();
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            if (IsTargetType(
                    reader,
                    typeHandle,
                    reader.GetFullTypeName(type),
                    typeFilter))
            {
                targetTypes.Add(typeHandle);
            }
        }
        _targetApiEvidence.Materialize(pe, targetTypes);

        int scannedBodyCount = 0;
        int declarationCandidateCount = 0;
        int eligibleCount = 0;
        foreach (TypeDefinitionHandle typeHandle
            in reader.TypeDefinitions)
        {
            TypeDefinition type =
                reader.GetTypeDefinition(typeHandle);
            string typeName = reader.GetFullTypeName(type);
            var overloads = new Dictionary<string, int>();
            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                int overload =
                    overloads.GetValueOrDefault(methodName);
                overloads[methodName] = overload + 1;
                if (method.RelativeVirtualAddress == 0)
                    continue;

                scannedBodyCount++;
                TargetEvaluation evaluation =
                    Evaluate(
                        reader,
                        new(
                            typeName,
                            methodName,
                            overload,
                            typeHandle,
                            methodHandle),
                        typeFilter,
                        materializeDecision: false);
                if (evaluation.DeclarationCandidate)
                    declarationCandidateCount++;
                if (evaluation.Eligible)
                    eligibleCount++;
            }
        }

        return new(
            eligibleCount,
            scannedBodyCount,
            declarationCandidateCount,
            MaterializedRowCount: 0);
    }

    private ReturnToSenderCappedTargetSelection SelectCappedTargets(
        PEReader pe,
        IReadOnlyList<IrImporter.StableRankedSampleCandidate>
            ranked,
        int cap,
        string? typeFilter)
    {
        MetadataReader reader = pe.GetMetadataReader();

        // API evidence is materialized for the ranked prefix the loop can
        // reach: first the cap, then a doubling prefix only when the loop
        // runs past it. Types the target filter rejects are never decoded.
        int preparedPrefix = 0;
        int typesMaterialized = 0;
        void Prepare(int end)
        {
            var types = new List<TypeDefinitionHandle>();
            for (int i = preparedPrefix; i < end; i++)
            {
                IrImporter.StableSampleCandidate candidate =
                    ranked[i].Candidate;
                if (IsTargetType(reader, candidate, typeFilter))
                    types.Add(candidate.TypeDefHandle);
            }
            typesMaterialized += _targetApiEvidence.Materialize(pe, types);
            preparedPrefix = end;
        }
        Prepare(Math.Min(cap, ranked.Count));

        var selected =
            new List<(int Sequence, ReturnToSenderTarget Target)>(
                Math.Min(cap, ranked.Count));
        int evaluatedBodyCount = 0;
        int declarationCandidateCount = 0;
        int excludedDeclarationCandidateCount = 0;
        for (int rank = 0; rank < ranked.Count; rank++)
        {
            if (rank == preparedPrefix)
            {
                Prepare(Math.Min(
                    ranked.Count,
                    Math.Max(preparedPrefix * 2, preparedPrefix + 1)));
            }
            IrImporter.StableRankedSampleCandidate rankedCandidate =
                ranked[rank];
            evaluatedBodyCount++;
            TargetEvaluation evaluation =
                Evaluate(
                    reader,
                    rankedCandidate.Candidate,
                    typeFilter,
                    materializeDecision: true);
            if (evaluation.DeclarationCandidate)
                declarationCandidateCount++;
            if (!evaluation.Eligible)
            {
                if (evaluation.DeclarationCandidate)
                    excludedDeclarationCandidateCount++;
                continue;
            }

            ReturnToSenderTarget target =
                evaluation.Decision?.Target
                ?? throw new InvalidOperationException(
                    "An eligible ranked RTS candidate lost its target.");
            selected.Add(
                (rankedCandidate.MetadataSequence, target));
            if (selected.Count == cap)
                break;
        }

        return new(
            [
                .. selected
                    .OrderBy(item => item.Sequence)
                    .Select(item => item.Target),
            ],
            ranked.Count,
            evaluatedBodyCount,
            declarationCandidateCount,
            excludedDeclarationCandidateCount,
            preparedPrefix,
            typesMaterialized);
    }

    private TargetEvaluation Evaluate(
        MetadataReader reader,
        IrImporter.StableSampleCandidate candidate,
        string? typeFilter,
        bool materializeDecision)
    {
        if (!IsTargetType(reader, candidate, typeFilter))
        {
            return default;
        }

        MethodDefinition method =
            reader.GetMethodDefinition(candidate.MethodHandle);
        int token = MetadataTokens.GetToken(candidate.MethodHandle);
        bool mappedAccessor =
            _targetApiEvidence.AccessorTokens.Contains(token);
        bool hasApiEntry =
            _targetApiEvidence.Index.TryGetValue(
                token,
                out (ApiType Type, ApiMember Member) entry);
        bool requiresMethodSemanticsDecision = mappedAccessor
            || (hasApiEntry
                && entry.Member.MethodSemantics is not
                    ApiMethodSemanticsKind.None);
        MetadataMethodAddress address =
            MetadataMethodAddress.Create(
                reader,
                candidate.MethodHandle);
        CSharpAccessorDeclarationPost? accessorPost = null;
        if (requiresMethodSemanticsDecision
            || (method.Attributes & MethodAttributes.SpecialName) != 0)
        {
            accessorPost = CaptureAccessor(
                reader,
                candidate.TypeDefHandle,
                address);
        }
        bool hasAccessorAssociation =
            accessorPost?.Association is not null
                and not MetadataAccessorAssociationResult.Absent;
        if (!requiresMethodSemanticsDecision
            && !hasAccessorAssociation
            && IsGeneratedMethod(
                reader,
                method,
                candidate.MethodName,
                allowEmbeddedAngleBrackets: true))
        {
            return default;
        }

        string stableIdentitySuffix = "generic-arity:"
            + method.GetGenericParameters().Count.ToString(
                CultureInfo.InvariantCulture);
        MemberSignatureShapeResult signatureShape =
            MetadataMemberSignatureShape.Create(
                reader,
                candidate.MethodHandle);
        string? signature = signatureShape.Shape is { } shape
            ? MemberSignatureShapeCodec.Encode(shape)
            : null;

        TargetEvaluation Exclude(
            ReturnToSenderTargetExclusionReason reason,
            ReturnToSenderDeclarationProducer? producer = null,
            CSharpDeclarationRepresentabilityResult?
                exactOutcome = null,
            CSharpAccessorDeclarationRepresentabilityResult?
                accessorOutcome = null) =>
            new(
                DeclarationCandidate: true,
                Eligible: false,
                materializeDecision
                    ? new(
                        null,
                        new(
                            _assemblyIdentity,
                            candidate.TypeName,
                            candidate.MethodName,
                            candidate.Overload,
                            signature,
                            address,
                            reason,
                            producer,
                            exactOutcome,
                            accessorOutcome),
                        stableIdentitySuffix)
                    : null);

        ReturnToSenderDeclarationSelection declaration;
        CSharpDeclarationRepresentabilityResult? exactResult = null;
        CSharpAccessorDeclarationRepresentabilityResult?
            accessorResult = null;
        if (requiresMethodSemanticsDecision
            || hasAccessorAssociation)
        {
            accessorPost ??= CaptureAccessor(
                reader,
                candidate.TypeDefHandle,
                address);
            accessorResult =
                CSharpAccessorDeclarationRepresentability.Decide(
                    accessorPost,
                    _languageProfile);
            switch (accessorResult)
            {
                case CSharpAccessorDeclarationRepresentabilityResult
                    .Representable represented:
                    declaration =
                        new ReturnToSenderDeclarationSelection
                            .ExactAccessor(
                                represented.Request);
                    break;
                case CSharpAccessorDeclarationRepresentabilityResult
                    .Unrepresentable:
                    return Exclude(
                        ReturnToSenderTargetExclusionReason
                            .ExactAccessorDeclarationUnrepresentable,
                        ReturnToSenderDeclarationProducer
                            .ExactAccessorDeclaration,
                        accessorOutcome: accessorResult);
                case CSharpAccessorDeclarationRepresentabilityResult
                    .Unavailable:
                    return Exclude(
                        ReturnToSenderTargetExclusionReason
                            .ExactAccessorDeclarationUnavailable,
                        ReturnToSenderDeclarationProducer
                            .ExactAccessorDeclaration,
                        accessorOutcome: accessorResult);
                default:
                    throw new InvalidOperationException(
                        "Unknown C# accessor declaration "
                        + "representability result.");
            }
        }
        else
        {
            CSharpMethodDeclarationPost? exactPost = null;
            bool inspectExactDeclaration =
                !hasApiEntry
                || entry.Member.Kind is
                    "explicit-interface-implementation" or "operator";
            bool requiresExactDeclaration =
                hasApiEntry
                && entry.Member.Kind
                    == "explicit-interface-implementation";
            if (inspectExactDeclaration)
            {
                exactPost = CaptureMethod(
                    reader,
                    candidate.TypeDefHandle,
                    address);
                requiresExactDeclaration |=
                    exactPost.Implementations is not
                        MetadataMethodImplementationResult.Absent;
            }

            if (requiresExactDeclaration)
            {
                exactPost ??= CaptureMethod(
                    reader,
                    candidate.TypeDefHandle,
                    address);
                exactResult =
                    CSharpDeclarationRepresentability.Decide(
                        exactPost,
                        _languageProfile);
                switch (exactResult)
                {
                    case CSharpDeclarationRepresentabilityResult
                        .Representable represented:
                        declaration =
                            new ReturnToSenderDeclarationSelection
                                .ExactMethod(
                                    represented.Request);
                        break;
                    case CSharpDeclarationRepresentabilityResult
                        .Unrepresentable:
                        return Exclude(
                            ReturnToSenderTargetExclusionReason
                                .ExactDeclarationUnrepresentable,
                            ReturnToSenderDeclarationProducer
                                .ExactMethodDeclaration,
                            exactResult);
                    case CSharpDeclarationRepresentabilityResult
                        .Unavailable:
                        return Exclude(
                            ReturnToSenderTargetExclusionReason
                                .ExactDeclarationUnavailable,
                            ReturnToSenderDeclarationProducer
                                .ExactMethodDeclaration,
                            exactResult);
                    default:
                        throw new InvalidOperationException(
                            "Unknown C# declaration representability "
                            + "result.");
                }
            }
            else
            {
                if (!hasApiEntry)
                {
                    return Exclude(
                        ReturnToSenderTargetExclusionReason
                            .ProductMemberUnavailable,
                        ReturnToSenderDeclarationProducer
                            .OrdinaryTypeArtifact,
                        exactResult);
                }

                if (!CSharpMemberArtifactEligibility.IsRepresentable(
                        entry.Type,
                        entry.Member))
                {
                    return Exclude(
                        ReturnToSenderTargetExclusionReason
                            .OrdinaryDeclarationUnrepresentable,
                        ReturnToSenderDeclarationProducer
                            .OrdinaryTypeArtifact);
                }

                declaration =
                    new ReturnToSenderDeclarationSelection
                        .OrdinaryMethod();
            }
        }

        if (signature is null)
        {
            return Exclude(
                ReturnToSenderTargetExclusionReason
                    .CanonicalSignatureUnavailable,
                declaration switch
                {
                    ReturnToSenderDeclarationSelection.ExactMethod =>
                        ReturnToSenderDeclarationProducer
                            .ExactMethodDeclaration,
                    ReturnToSenderDeclarationSelection.ExactAccessor =>
                        ReturnToSenderDeclarationProducer
                            .ExactAccessorDeclaration,
                    _ => ReturnToSenderDeclarationProducer
                        .OrdinaryTypeArtifact,
                },
                exactResult,
                accessorOutcome: accessorResult);
        }

        return new(
            DeclarationCandidate: true,
            Eligible: true,
            materializeDecision
                ? new(
                    new(
                        _assemblyIdentity,
                        candidate.TypeName,
                        candidate.MethodName,
                        candidate.Overload,
                        signature,
                        address,
                        declaration),
                    null,
                    stableIdentitySuffix)
                : null);
    }

    private CSharpMethodDeclarationPost CaptureMethod(
        MetadataReader reader,
        TypeDefinitionHandle type,
        MetadataMethodAddress method) =>
        CSharpMethodDeclarationPost.Capture(
            _declarations,
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                type),
            method);

    private readonly record struct TargetEvaluation(
        bool DeclarationCandidate,
        bool Eligible,
        ReturnToSenderTargetDecision? Decision);

    private CSharpAccessorDeclarationPost CaptureAccessor(
        MetadataReader reader,
        TypeDefinitionHandle type,
        MetadataMethodAddress method) =>
        CSharpAccessorDeclarationPost.Capture(
            _declarations,
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                type),
            method);

    private static bool IsTargetType(
        MetadataReader reader,
        IrImporter.StableSampleCandidate candidate,
        string? typeFilter) =>
        IsTargetType(
            reader,
            candidate.TypeDefHandle,
            candidate.TypeName,
            typeFilter);

    // The Types whose methods Evaluate can decide; every other candidate is
    // rejected before it consults API evidence.
    private static bool IsTargetType(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        string typeName,
        string? typeFilter)
    {
        TypeDefinition typeDef = reader.GetTypeDefinition(typeHandle);
        return typeDef.GetDeclaringType().IsNil
            && ShapeOf(reader, typeDef) is TypeKind.Class or TypeKind.Struct
            && (typeFilter is null
                || typeName.Contains(
                    typeFilter,
                    StringComparison.Ordinal))
            && !IsGeneratedType(reader, typeDef, typeName);
    }

    private static bool IsGeneratedType(
        MetadataReader reader,
        TypeDefinition typeDef,
        string fullType) =>
        fullType.Contains('<')
        || TypeFilters.IsCompilerGeneratedNested(
            reader.GetString(typeDef.Name))
        || AttributeReader.HasAttribute(
            reader,
            typeDef.GetCustomAttributes(),
            KnownAttributeNames.CompilerGeneratedAttribute)
        || AttributeReader.HasAttribute(
            reader,
            typeDef.GetCustomAttributes(),
            "System.CodeDom.Compiler.GeneratedCodeAttribute")
        || BaseTypeName(reader, typeDef.BaseType)
            == "System.Text.Json.Serialization.JsonSerializerContext";

    private static bool IsGeneratedMethod(
        MetadataReader reader,
        MethodDefinition method,
        string name,
        bool allowEmbeddedAngleBrackets)
    {
        bool generatedName = allowEmbeddedAngleBrackets
            ? name.StartsWith('<')
            : name.Contains('<');
        if (generatedName
            || name.StartsWith("__", StringComparison.Ordinal)
            || AttributeReader.HasAttribute(
                reader,
                method.GetCustomAttributes(),
                "System.CodeDom.Compiler.GeneratedCodeAttribute"))
        {
            return true;
        }

        if (!AttributeReader.HasAttribute(
                reader,
                method.GetCustomAttributes(),
                KnownAttributeNames.CompilerGeneratedAttribute))
        {
            return false;
        }

        return !IsCompilerGeneratedAccessor(method, name);
    }

    private static bool IsCompilerGeneratedAccessor(
        MethodDefinition method,
        string name) =>
        (method.Attributes & MethodAttributes.SpecialName) != 0
        && (name.StartsWith("get_", StringComparison.Ordinal)
            || name.StartsWith("set_", StringComparison.Ordinal));

    private static TypeKind ShapeOf(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return TypeKind.Interface;
        if (typeDef.BaseType.IsNil)
            return TypeKind.Class;

        return BaseTypeName(reader, typeDef.BaseType) switch
        {
            "System.Enum" => TypeKind.Enum,
            "System.ValueType" => TypeKind.Struct,
            "System.MulticastDelegate" or "System.Delegate" =>
                TypeKind.Delegate,
            _ => TypeKind.Class,
        };
    }

    private static string BaseTypeName(
        MetadataReader reader,
        EntityHandle handle)
    {
        if (handle.IsNil)
            return "";

        return handle.Kind switch
        {
            HandleKind.TypeReference =>
                FullName(
                    reader,
                    reader.GetTypeReference(
                        (TypeReferenceHandle)handle)),
            HandleKind.TypeDefinition =>
                FullName(
                    reader,
                    reader.GetTypeDefinition(
                        (TypeDefinitionHandle)handle)),
            _ => "",
        };
    }

    private static string FullName(
        MetadataReader reader,
        TypeReference type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private static string FullName(
        MetadataReader reader,
        TypeDefinition type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private enum TypeKind
    {
        Class,
        Struct,
        Enum,
        Interface,
        Delegate,
    }

    /// <summary>
    /// API declarations for the target Types materialized so far. A Type's
    /// members index by their own token and by their accessor tokens.
    /// </summary>
    private sealed class TargetApiEvidence
    {
        private readonly Dictionary<int, (ApiType Type, ApiMember Member)>
            index = [];
        private readonly HashSet<int> accessorTokens = [];
        private readonly HashSet<TypeDefinitionHandle> materialized = [];

        public IReadOnlyDictionary<int, (ApiType Type, ApiMember Member)>
            Index => index;

        public IReadOnlySet<int> AccessorTokens => accessorTokens;

        /// <summary>
        /// Decodes the not-yet-materialized Types among
        /// <paramref name="types"/> and returns how many it decoded.
        /// </summary>
        public int Materialize(
            PEReader pe,
            IEnumerable<TypeDefinitionHandle> types)
        {
            var pending = new HashSet<TypeDefinitionHandle>();
            foreach (TypeDefinitionHandle handle in types)
            {
                if (!materialized.Contains(handle))
                    pending.Add(handle);
            }
            if (pending.Count == 0)
                return 0;

            foreach (ApiType type in ApiSurfaceExtractor.ExtractDeclarations(
                pe,
                ApiSurfaceExtractionScope.IncludeAll,
                pending.Contains,
                includeCompilerGenerated: true).Types)
            {
                foreach (ApiMember member in type.Members)
                {
                    if (member.MetadataToken is { } token)
                    {
                        if (member.Kind == "extension-method")
                            index.TryAdd(token, (type, member));
                        else
                            index[token] = (type, member);
                    }
                    if (member.Kind == "property")
                    {
                        if (member.GetterToken is { } getterToken)
                        {
                            accessorTokens.Add(getterToken);
                            if (!member.Name.Contains(
                                    '.',
                                    StringComparison.Ordinal))
                            {
                                index.TryAdd(
                                    getterToken,
                                    (type, member));
                            }
                        }
                        if (member.SetterToken is { } setterToken)
                        {
                            accessorTokens.Add(setterToken);
                            if (!member.Name.Contains(
                                    '.',
                                    StringComparison.Ordinal))
                            {
                                index.TryAdd(
                                    setterToken,
                                    (type, member));
                            }
                        }
                    }
                    if (member.Kind == "event")
                    {
                        if (member.AdderToken is { } adderToken)
                        {
                            accessorTokens.Add(adderToken);
                            index.TryAdd(adderToken, (type, member));
                        }
                        if (member.RemoverToken is { } removerToken)
                        {
                            accessorTokens.Add(removerToken);
                            index.TryAdd(removerToken, (type, member));
                        }
                    }
                }
            }
            materialized.UnionWith(pending);
            return pending.Count;
        }
    }
}
