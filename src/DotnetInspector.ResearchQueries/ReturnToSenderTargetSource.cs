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

public sealed class ReturnToSenderTargetSourceSession : IDisposable
{
    private readonly string _assemblyIdentity;
    private readonly AssemblyInspectionSession _assembly;
    private readonly MetadataOperationContext _operation;
    private readonly MetadataDeclarationSession _declarations;
    private readonly CSharpLanguageProfile _languageProfile;
    private readonly TargetApiEvidence _targetApiEvidence;

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
        _targetApiEvidence = assembly.InspectImage(
            static pe => CreateTargetApiEvidence(pe));
    }

    public ReturnToSenderTargetDecision? Decide(
        IrImporter.StableSampleCandidate candidate,
        string? typeFilter,
        out bool declarationCandidate)
    {
        TargetEvaluation evaluation =
            _assembly.InspectImage(
                pe => Evaluate(
                    pe.GetMetadataReader(),
                    candidate,
                    typeFilter,
                    materializeDecision: true));
        declarationCandidate = evaluation.DeclarationCandidate;
        return evaluation.Decision;
    }

    public ReturnToSenderTargetSourceCount Count(
        string? typeFilter = null) =>
        _assembly.InspectImage(
            pe => Count(
                pe.GetMetadataReader(),
                typeFilter));

    public void Dispose()
    {
        _declarations.Dispose();
        _operation.Dispose();
    }

    private ReturnToSenderTargetSourceCount Count(
        MetadataReader reader,
        string? typeFilter)
    {
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

    private TargetEvaluation Evaluate(
        MetadataReader reader,
        IrImporter.StableSampleCandidate candidate,
        string? typeFilter,
        bool materializeDecision)
    {
        TypeDefinition typeDef =
            reader.GetTypeDefinition(candidate.TypeDefHandle);
        if (!typeDef.GetDeclaringType().IsNil
            || ShapeOf(reader, typeDef) is not (
                TypeKind.Class or TypeKind.Struct)
            || (typeFilter is not null
                && !candidate.TypeName.Contains(
                    typeFilter,
                    StringComparison.Ordinal))
            || IsGeneratedType(
                reader,
                typeDef,
                candidate.TypeName))
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

    private static TargetApiEvidence CreateTargetApiEvidence(
        PEReader pe)
    {
        var index =
            new Dictionary<int, (ApiType Type, ApiMember Member)>();
        var accessorTokens = new HashSet<int>();
        foreach (ApiType type in ApiSurfaceExtractor.Extract(
            pe,
            includeAll: true,
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

        return new(index, accessorTokens);
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

    private sealed record TargetApiEvidence(
        IReadOnlyDictionary<
            int,
            (ApiType Type, ApiMember Member)> Index,
        IReadOnlySet<int> AccessorTokens);
}
