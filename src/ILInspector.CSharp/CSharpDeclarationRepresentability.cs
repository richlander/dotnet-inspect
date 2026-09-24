using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using CSharpText;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.CSharp;

public enum CSharpLanguageVersion
{
    CSharp10 = 1000,
    CSharp11 = 1100,
    CSharp12 = 1200,
    CSharp13 = 1300,
    CSharp14 = 1400,
    Preview = int.MaxValue,
}

public sealed record CSharpLanguageProfile
{
    public CSharpLanguageProfile(CSharpLanguageVersion version)
    {
        if (!Enum.IsDefined(version))
            throw new ArgumentOutOfRangeException(nameof(version));

        Version = version;
    }

    public CSharpLanguageVersion Version { get; }
}

public sealed record CSharpMethodImplementationPost
{
    internal CSharpMethodImplementationPost(
        MetadataMethodImplementationCertificate relationship,
        MetadataTypeDeclarationResult? declarationOwner,
        MetadataInterfaceImplementationRequest? interfaceRequest,
        MetadataInterfaceImplementationResult? interfaceResult)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        Relationship = relationship;
        DeclarationOwner = declarationOwner;
        InterfaceRequest = interfaceRequest;
        InterfaceResult = interfaceResult;
    }

    public MetadataMethodImplementationCertificate Relationship
        { get; internal init; }

    public MetadataTypeDeclarationResult? DeclarationOwner
        { get; internal init; }

    public MetadataInterfaceImplementationRequest? InterfaceRequest
        { get; internal init; }

    public MetadataInterfaceImplementationResult? InterfaceResult
        { get; internal init; }
}

public sealed record CSharpMethodDeclarationPost
{
    internal CSharpMethodDeclarationPost(
        MetadataMethodDeclarationRequest request,
        MetadataMethodDeclarationResult method,
        MetadataTypeDeclarationResult containingType,
        MetadataMethodImplementationResult implementations,
        ImmutableArray<CSharpMethodImplementationPost>
            implementationOccurrences)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(containingType);
        ArgumentNullException.ThrowIfNull(implementations);
        if (implementationOccurrences.IsDefault)
        {
            throw new ArgumentException(
                "Implementation occurrences must be initialized.",
                nameof(implementationOccurrences));
        }

        Request = request;
        Method = method;
        ContainingType = containingType;
        Implementations = implementations;
        ImplementationOccurrences = implementationOccurrences;
    }

    public MetadataMethodDeclarationRequest Request { get; }

    public MetadataMethodDeclarationResult Method { get; }

    public MetadataTypeDeclarationResult ContainingType { get; }

    public MetadataMethodImplementationResult Implementations { get; }

    public ImmutableArray<CSharpMethodImplementationPost>
        ImplementationOccurrences { get; internal init; }

    public static CSharpMethodDeclarationPost Capture(
        MetadataDeclarationSession session,
        MetadataTypeDefinitionAddress type,
        MetadataMethodAddress method,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        token.ThrowIfCancellationRequested();

        MetadataMethodDeclarationResult methodResult =
            session.PostMethodDeclaration(type, method, token);
        MetadataTypeDeclarationResult typeResult =
            session.PostTypeDeclaration(type, token);
        MetadataMethodImplementationResult implementations =
            session.Relate(type, method, token);
        var occurrences =
            ImmutableArray.CreateBuilder<CSharpMethodImplementationPost>();

        if (implementations is
            MetadataMethodImplementationResult.Related related)
        {
            foreach (MetadataMethodImplementationCertificate relationship
                in related.Relationships)
            {
                token.ThrowIfCancellationRequested();
                MetadataTypeDeclarationResult? owner =
                    relationship.Definition is
                        MetadataDeclarationDefinitionDisposition.LocalResolved
                            local
                        ? session.PostTypeDeclaration(local.Owner, token)
                        : null;
                MetadataInterfaceImplementationRequest? interfaceRequest =
                    null;
                MetadataInterfaceImplementationResult? interfaceResult =
                    null;
                if (owner is MetadataTypeDeclarationResult.Posted
                    {
                        Evidence.Category:
                            MetadataTypeDeclarationCategory.Interface,
                    })
                {
                    interfaceRequest =
                        new MetadataInterfaceImplementationRequest(
                            relationship.Type,
                            relationship.DeclarationOwner);
                    interfaceResult = session.Relate(
                        interfaceRequest.Value.Type,
                        interfaceRequest.Value.Interface,
                        token);
                }
                occurrences.Add(
                    new(
                        relationship,
                        owner,
                        interfaceRequest,
                        interfaceResult));
            }
        }

        return new(
            new(type, method),
            methodResult,
            typeResult,
            implementations,
            occurrences.ToImmutable());
    }
}

public enum CSharpDeclarationKind
{
    ExplicitInterfaceOperator,
}

public enum CSharpDeclarationRefusalReason
{
    UnsupportedLanguageProfile,
    MultipleMethodImplementations,
    MultipleInterfaceImplementations,
    UnsupportedOperatorSignature,
    MissingMethodBody,
    NonStaticOperator,
}

public enum CSharpDeclarationUnavailableReason
{
    MethodDeclarationRejected,
    ContainingTypeRejected,
    MethodImplementationRejected,
    RequestMismatch,
    OccurrencePairingMismatch,
    RelationshipRequestMismatch,
    InterfaceRequestMismatch,
    MethodSignatureMismatch,
    UnresolvedDeclarationOwner,
    DeclarationOwnerRejected,
    DeclarationOwnerIdentityMismatch,
    InterfaceImplementationRejected,
    InterfaceImplementationAbsent,
    InterfaceImplementationMismatch,
    SpecialNameUnavailable,
    InterfaceImplementationNotPosted,
    ParameterEvidenceIncomplete,
    OutsideInitialBoundary,
}

public sealed record CSharpTypeSpelling
{
    internal CSharpTypeSpelling(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        Source = source;
    }

    public string Source { get; }

    public override string ToString() => Source;
}

public sealed record CSharpParameterSpelling
{
    internal CSharpParameterSpelling(
        CSharpTypeSpelling type,
        string name)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrEmpty(name);
        Type = type;
        Name = name;
    }

    public CSharpTypeSpelling Type { get; }

    public string Name { get; }
}

public sealed record CSharpAcceptedDeclarationRequest
{
    internal CSharpAcceptedDeclarationRequest(
        CSharpDeclarationKind kind,
        CSharpLanguageProfile profile,
        MetadataTypeDefinitionAddress containingType,
        MetadataMethodAddress body,
        MetadataTypeIdentity containingTypeIdentity,
        MetadataTypeIdentity explicitInterfaceIdentity,
        MetadataMethodSignatureIdentity signature,
        CSharpTypeSpelling explicitInterface,
        CSharpTypeSpelling returnType,
        string operatorDeclaration,
        ImmutableArray<CSharpParameterSpelling> parameters)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(containingTypeIdentity);
        ArgumentNullException.ThrowIfNull(explicitInterfaceIdentity);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(explicitInterface);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentException.ThrowIfNullOrEmpty(operatorDeclaration);
        if (parameters.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "An operator request requires parameters.",
                nameof(parameters));
        }

        Kind = kind;
        Profile = profile;
        ContainingType = containingType;
        Body = body;
        ContainingTypeIdentity = containingTypeIdentity;
        ExplicitInterfaceIdentity = explicitInterfaceIdentity;
        Signature = signature;
        ExplicitInterface = explicitInterface;
        ReturnType = returnType;
        OperatorDeclaration = operatorDeclaration;
        Parameters = parameters;
    }

    public CSharpDeclarationKind Kind { get; }

    public CSharpLanguageProfile Profile { get; }

    public MetadataTypeDefinitionAddress ContainingType { get; }

    public MetadataMethodAddress Body { get; }

    public MetadataTypeIdentity ContainingTypeIdentity { get; }

    public MetadataTypeIdentity ExplicitInterfaceIdentity { get; }

    public MetadataMethodSignatureIdentity Signature { get; }

    public CSharpTypeSpelling ExplicitInterface { get; }

    public CSharpTypeSpelling ReturnType { get; }

    public string OperatorDeclaration { get; }

    public ImmutableArray<CSharpParameterSpelling> Parameters { get; }
}

public abstract record CSharpDeclarationRepresentabilityResult
{
    private protected CSharpDeclarationRepresentabilityResult()
    {
    }

    public sealed record Representable(
        CSharpAcceptedDeclarationRequest Request)
        : CSharpDeclarationRepresentabilityResult;

    public sealed record Unrepresentable(
        MetadataMethodDeclarationRequest Coordinate,
        CSharpLanguageProfile Profile,
        CSharpDeclarationRefusalReason Reason)
        : CSharpDeclarationRepresentabilityResult;

    public sealed record Unavailable(
        MetadataMethodDeclarationRequest Coordinate,
        CSharpLanguageProfile Profile,
        CSharpDeclarationUnavailableReason Reason,
        int? RelationshipOccurrence = null)
        : CSharpDeclarationRepresentabilityResult;
}

public static class CSharpDeclarationRepresentability
{
    public static CSharpDeclarationRepresentabilityResult Decide(
        CSharpMethodDeclarationPost post,
        CSharpLanguageProfile profile)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(profile);

        CSharpDeclarationRepresentabilityResult.Unrepresentable Refuse(
            CSharpMethodDeclarationPost _,
            CSharpDeclarationRefusalReason reason) =>
            new(post.Request, profile, reason);
        CSharpDeclarationRepresentabilityResult.Unavailable Unavailable(
            CSharpMethodDeclarationPost _,
            CSharpDeclarationUnavailableReason reason,
            int? relationshipOccurrence = null) =>
            new(
                post.Request,
                profile,
                reason,
                relationshipOccurrence);

        if (post.Method is not MetadataMethodDeclarationResult.Posted method)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .MethodDeclarationRejected);
        }

        if (post.ContainingType is not
            MetadataTypeDeclarationResult.Posted containingType)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason.ContainingTypeRejected);
        }

        if (method.Evidence.Type != post.Request.Type
            || method.Evidence.Method != post.Request.Method
            || containingType.Evidence.Type != post.Request.Type)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason.RequestMismatch);
        }

        if (method.Evidence.Parameters.Length !=
            method.Evidence.Signature.ParameterTypes.Length)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .MethodSignatureMismatch);
        }

        if (!HasCompleteParameterEvidence(
                method.Evidence.ReturnParameter)
            || method.Evidence.Parameters.Any(parameter =>
                !HasCompleteParameterEvidence(parameter)))
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .ParameterEvidenceIncomplete);
        }

        if (post.Implementations is
            MetadataMethodImplementationResult.Rejected)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .MethodImplementationRejected);
        }

        if (post.Implementations is not
            MetadataMethodImplementationResult.Related related)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        if (related.Relationships.Length !=
            post.ImplementationOccurrences.Length
            || !related.Relationships.SequenceEqual(
                post.ImplementationOccurrences.Select(
                    occurrence => occurrence.Relationship)))
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OccurrencePairingMismatch);
        }

        MetadataInterfaceImplementationResult.Related?
            selectedInterfaceResult = null;
        bool allOwnersAreInterfaces = true;
        for (int index = 0;
            index < post.ImplementationOccurrences.Length;
            index++)
        {
            CSharpDeclarationRepresentabilityResult? failure =
                ValidateOccurrence(
                    post,
                    profile,
                    method.Evidence,
                    post.ImplementationOccurrences[index],
                    index,
                    out MetadataInterfaceImplementationResult.Related
                        interfaceResult,
                    out bool ownerIsInterface);
            if (failure is not null)
                return failure;
            allOwnersAreInterfaces &= ownerIsInterface;
            if (index == 0)
                selectedInterfaceResult = interfaceResult;
        }

        if (!allOwnersAreInterfaces)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        if (related.Relationships.Length != 1)
        {
            return Refuse(
                post,
                CSharpDeclarationRefusalReason
                    .MultipleMethodImplementations);
        }

        if (selectedInterfaceResult!.Relationships.Length != 1)
        {
            return Refuse(
                post,
                CSharpDeclarationRefusalReason
                    .MultipleInterfaceImplementations);
        }

        CSharpMethodImplementationPost occurrence =
            post.ImplementationOccurrences[0];
        MetadataMethodImplementationCertificate relationship =
            occurrence.Relationship;
        var local = (MetadataDeclarationDefinitionDisposition.LocalResolved)
            relationship.Definition;

        if (relationship.SpecialName is not
            MetadataSpecialNameEvidence.KnownTrue)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary,
                relationshipOccurrence: 0);
        }

        string metadataName =
            MetadataDeclarationText.RenderDeclarationName(relationship);
        if (metadataName != "op_Addition")
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary,
                relationshipOccurrence: 0);
        }

        MetadataMethodDeclarationEvidence declaration = method.Evidence;
        const MethodAttributes BodyAttributes =
            MethodAttributes.Private
            | MethodAttributes.Static
            | MethodAttributes.HideBySig;
        const MethodAttributes DeclarationAttributes =
            MethodAttributes.Public
            | MethodAttributes.Static
            | MethodAttributes.Abstract
            | MethodAttributes.Virtual
            | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName;
        if (!declaration.Attributes.HasFlag(MethodAttributes.Static)
            || !local.Attributes.HasFlag(MethodAttributes.Static))
        {
            return Refuse(
                post,
                CSharpDeclarationRefusalReason.NonStaticOperator);
        }

        if (declaration.Attributes != BodyAttributes
            || declaration.ImplementationAttributes !=
                MethodImplAttributes.IL
            || local.Attributes != DeclarationAttributes
            || containingType.Evidence.Category is not
                (MetadataTypeDeclarationCategory.Class
                or MetadataTypeDeclarationCategory.Struct)
            || containingType.Evidence.Category
                    == MetadataTypeDeclarationCategory.Class
                && containingType.Evidence.Attributes.HasFlag(
                    TypeAttributes.Abstract)
                && containingType.Evidence.Attributes.HasFlag(
                    TypeAttributes.Sealed))
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        if (!declaration.HasBodyRva)
        {
            return Refuse(
                post,
                CSharpDeclarationRefusalReason.MissingMethodBody);
        }

        if (!declaration.TypeParameters.IsEmpty
            || !declaration.MethodParameters.IsEmpty
            || declaration.Signature.GenericParameterCount != 0)
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        if (!HasPlainParameterEvidence(
                declaration.ReturnParameter)
            || declaration.Parameters.Any(parameter =>
                !HasPlainParameterEvidence(parameter)))
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        MetadataTypeIdentity containingIdentity =
            containingType.Evidence.PrimitiveAlias
            ?? containingType.Evidence.OpenSelfIdentity;
        if (!IsAdditionSignatureShape(declaration.Signature))
        {
            return Refuse(
                post,
                CSharpDeclarationRefusalReason
                    .UnsupportedOperatorSignature);
        }

        if (!declaration.Signature.ParameterTypes.Contains(
                containingIdentity))
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        if (!TrySpellType(
                relationship.DeclarationOwner,
                out CSharpTypeSpelling? explicitInterface)
            || !TrySpellType(
                declaration.Signature.ReturnType,
                out CSharpTypeSpelling? returnType))
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        var parameters =
            ImmutableArray.CreateBuilder<CSharpParameterSpelling>(
                declaration.Parameters.Length);
        ImmutableArray<string> parameterNames =
            SpellParameterNames(declaration.Parameters);
        for (int index = 0;
            index < declaration.Parameters.Length;
            index++)
        {
            if (!TrySpellType(
                    declaration.Signature.ParameterTypes[index],
                    out CSharpTypeSpelling? parameterType))
            {
                return Unavailable(
                    post,
                    CSharpDeclarationUnavailableReason
                        .OutsideInitialBoundary);
            }

            parameters.Add(
                new(
                    parameterType,
                    parameterNames[index]));
        }

        if (!NamedTypeSpellingsAreUnambiguous(
                containingIdentity,
                relationship.DeclarationOwner,
                declaration.Signature))
        {
            return Unavailable(
                post,
                CSharpDeclarationUnavailableReason
                    .OutsideInitialBoundary);
        }

        CSharpLanguageVersion minimumVersion =
            containingType.Evidence.IsByRefLike
                ? CSharpLanguageVersion.CSharp13
                : CSharpLanguageVersion.CSharp11;
        if (profile.Version < minimumVersion)
        {
            return Refuse(
                post,
                CSharpDeclarationRefusalReason
                    .UnsupportedLanguageProfile);
        }

        return new CSharpDeclarationRepresentabilityResult.Representable(
            new(
                CSharpDeclarationKind.ExplicitInterfaceOperator,
                profile,
                post.Request.Type,
                post.Request.Method,
                containingIdentity,
                relationship.DeclarationOwner,
                declaration.Signature,
                explicitInterface,
                returnType,
                OperatorNames.FormatDisplayNameUntreated(metadataName),
                parameters.MoveToImmutable()));
    }

    static CSharpDeclarationRepresentabilityResult.Unavailable Unavailable(
        CSharpMethodDeclarationPost post,
        CSharpLanguageProfile profile,
        CSharpDeclarationUnavailableReason reason,
        int? relationshipOccurrence = null) =>
        new(post.Request, profile, reason, relationshipOccurrence);

    static CSharpDeclarationRepresentabilityResult? ValidateOccurrence(
        CSharpMethodDeclarationPost post,
        CSharpLanguageProfile profile,
        MetadataMethodDeclarationEvidence declaration,
        CSharpMethodImplementationPost occurrence,
        int index,
        out MetadataInterfaceImplementationResult.Related interfaceResult,
        out bool ownerIsInterface)
    {
        interfaceResult = null!;
        ownerIsInterface = false;
        MetadataMethodImplementationCertificate relationship =
            occurrence.Relationship;
        if (relationship.Type != post.Request.Type
            || relationship.Body != post.Request.Method)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .RelationshipRequestMismatch,
                index);
        }

        if (relationship.Definition is not
            MetadataDeclarationDefinitionDisposition.LocalResolved local)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .UnresolvedDeclarationOwner,
                index);
        }

        if (occurrence.DeclarationOwner is not
            MetadataTypeDeclarationResult.Posted owner)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .DeclarationOwnerRejected,
                index);
        }

        MetadataNamedTypeIdentity? ownerDefinition =
            relationship.DeclarationOwner switch
            {
                MetadataTypeIdentity.Named named => named.Definition,
                MetadataTypeIdentity.GenericInstance generic =>
                    generic.Definition,
                _ => null,
            };
        if (owner.Evidence.Type != local.Owner
            || ownerDefinition is null
            || owner.Evidence.DefinitionIdentity != ownerDefinition)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .DeclarationOwnerIdentityMismatch,
                index);
        }

        if (relationship.SpecialName is
            MetadataSpecialNameEvidence.Unknown)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .SpecialNameUnavailable,
                index);
        }

        ownerIsInterface = owner.Evidence.Category ==
            MetadataTypeDeclarationCategory.Interface;
        if (!ownerIsInterface)
        {
            if (occurrence.InterfaceRequest is not null
                || occurrence.InterfaceResult is not null)
            {
                return Unavailable(
                    post,
                    profile,
                    CSharpDeclarationUnavailableReason
                        .InterfaceImplementationMismatch,
                    index);
            }
            return null;
        }

        if (occurrence.InterfaceRequest is not
                MetadataInterfaceImplementationRequest request
            || occurrence.InterfaceResult is null)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .InterfaceImplementationNotPosted,
                index);
        }

        if (request.Type != relationship.Type
            || request.Interface !=
                relationship.DeclarationOwner)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .InterfaceRequestMismatch,
                index);
        }

        if (occurrence.InterfaceResult is
            MetadataInterfaceImplementationResult.Rejected)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .InterfaceImplementationRejected,
                index);
        }

        if (occurrence.InterfaceResult is
            MetadataInterfaceImplementationResult.Absent)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .InterfaceImplementationAbsent,
                index);
        }

        if (occurrence.InterfaceResult is not
            MetadataInterfaceImplementationResult.Related related
            || related.Relationships.Any(certificate =>
                certificate.Type != relationship.Type
                || certificate.Interface !=
                    relationship.DeclarationOwner))
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .InterfaceImplementationMismatch,
                index);
        }

        // Metadata authenticated generic substitution when it issued this
        // relationship. Its declaration signature intentionally remains open,
        // so CSharp checks structural cardinality rather than false equality
        // with the substituted body signature.
        if (declaration.Signature.ParameterTypes.Length
                != relationship.DeclarationSignature.ParameterTypes.Length
            || declaration.Signature.GenericParameterCount
                != relationship.DeclarationSignature.GenericParameterCount)
        {
            return Unavailable(
                post,
                profile,
                CSharpDeclarationUnavailableReason
                    .MethodSignatureMismatch,
                index);
        }

        interfaceResult = related;
        return null;
    }

    internal static ImmutableArray<string> SpellParameterNames(
        ImmutableArray<MetadataParameterDeclarationEvidence> parameters)
    {
        if (parameters.IsDefault)
            throw new ArgumentException(
                "Parameter evidence must be initialized.",
                nameof(parameters));

        var result = ImmutableArray.CreateBuilder<string>(
            parameters.Length);
        string?[] preservedNames = new string?[parameters.Length];
        var reservedNames = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < parameters.Length; index++)
        {
            string? name =
                MetadataDeclarationText.RenderParameterName(
                    parameters[index]);
            if (name is not null
                && IsCompilerPreservedIdentifier(name))
            {
                string preserved = CSharpIdentifier.Escape(name);
                preservedNames[index] = preserved;
                reservedNames.Add(preserved);
            }
        }

        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < parameters.Length; index++)
        {
            string? preserved = preservedNames[index];
            if (preserved is not null && usedNames.Add(preserved))
            {
                result.Add(preserved);
                continue;
            }

            string candidate = $"arg{index}";
            while (reservedNames.Contains(candidate)
                || !usedNames.Add(candidate))
            {
                candidate = "_" + candidate;
            }
            result.Add(candidate);
        }
        return result.MoveToImmutable();
    }

    static bool IsCompilerPreservedIdentifier(string name) =>
        CSharpIdentifier.IsIdentifierLike(name)
        && !name.Any(character =>
            char.IsSurrogate(character)
            || char.GetUnicodeCategory(character) ==
                UnicodeCategory.Format);

    static bool HasCompleteParameterEvidence(
        MetadataParameterDeclarationEvidence parameter) =>
        parameter.Markers.IsComplete;

    static bool HasPlainParameterEvidence(
        MetadataParameterDeclarationEvidence parameter) =>
        parameter.Attributes == ParameterAttributes.None
        && parameter.Markers.IsReadOnlyCount == 0
        && parameter.Markers.RequiresLocationCount == 0
        && parameter.Markers.ParamArrayCount == 0
        && parameter.Markers.ParamCollectionCount == 0
        && parameter.Markers.ScopedRefCount == 0
        && parameter.Markers.UnscopedRefCount == 0;

    static bool IsVoid(MetadataTypeIdentity identity) =>
        identity is MetadataTypeIdentity.Primitive primitive
        && MetadataDeclarationText.RenderPrimitiveName(primitive) == "void";

    internal static bool IsAdditionSignatureShape(
        MetadataMethodSignatureIdentity signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        var header = new SignatureHeader(signature.Header);
        return OperatorNames.GetDeclarationParameterCount(
                "op_Addition") == signature.ParameterTypes.Length
            && header.CallingConvention ==
                SignatureCallingConvention.Default
            && !header.IsInstance
            && !header.HasExplicitThis
            && !header.IsGeneric
            && signature.RequiredParameterCount ==
                signature.ParameterTypes.Length
            && !IsVoid(signature.ReturnType)
            && !signature.ParameterTypes.Any(IsVoid);
    }

    internal static bool TrySpellType(
        MetadataTypeIdentity identity,
        [NotNullWhen(true)]
        out CSharpTypeSpelling? spelling)
    {
        string? source = identity switch
        {
            MetadataTypeIdentity.Primitive primitive =>
                SpellPrimitive(primitive),
            MetadataTypeIdentity.Named named =>
                SpellNamed(
                    named.Definition,
                    ImmutableArray<MetadataTypeIdentity>.Empty),
            MetadataTypeIdentity.GenericInstance generic =>
                SpellNamed(generic.Definition, generic.Arguments),
            MetadataTypeIdentity.SzArray array =>
                TrySpellType(array.Element, out CSharpTypeSpelling? element)
                    ? element.Source + "[]"
                    : null,
            _ => null,
        };

        spelling = source is null ? null : new(source);
        return spelling is not null;
    }

    internal static bool NamedTypeSpellingsAreUnambiguous(
        MetadataTypeIdentity containingType,
        MetadataTypeIdentity explicitInterface,
        MetadataMethodSignatureIdentity signature)
    {
        ArgumentNullException.ThrowIfNull(containingType);
        ArgumentNullException.ThrowIfNull(explicitInterface);
        ArgumentNullException.ThrowIfNull(signature);

        var definitions =
            new Dictionary<string, MetadataNamedTypeIdentity?>(
                StringComparer.Ordinal);
        return AddNamedDefinitions(containingType, definitions)
            && AddNamedDefinitions(explicitInterface, definitions)
            && AddNamedDefinitions(signature.ReturnType, definitions)
            && signature.ParameterTypes.All(
                type => AddNamedDefinitions(type, definitions));
    }

    static bool AddNamedDefinitions(
        MetadataTypeIdentity identity,
        Dictionary<string, MetadataNamedTypeIdentity?> definitions)
    {
        switch (identity)
        {
            case MetadataTypeIdentity.Named named:
                return AddNamedDefinition(
                    named.Definition,
                    definitions);
            case MetadataTypeIdentity.GenericInstance generic:
                return AddNamedDefinition(
                        generic.Definition,
                        definitions)
                    && generic.Arguments.All(
                        argument => AddNamedDefinitions(
                            argument,
                            definitions));
            case MetadataTypeIdentity.SzArray array:
                return AddNamedDefinitions(
                    array.Element,
                    definitions);
            default:
                return true;
        }
    }

    static bool AddNamedDefinition(
        MetadataNamedTypeIdentity definition,
        Dictionary<string, MetadataNamedTypeIdentity?> definitions)
    {
        var builder = new System.Text.StringBuilder();
        string namespaceName =
            MetadataDeclarationText.RenderNamespace(definition);
        if (namespaceName.Length > 0)
        {
            foreach (string segment in namespaceName.Split('.'))
            {
                AppendSpellingComponent(
                    builder,
                    CSharpIdentifier.Escape(segment),
                    genericArity: 0);
                if (!AddSpellingTarget(
                        builder.ToString(),
                        definition: null,
                        definitions))
                {
                    return false;
                }
            }
        }

        int segmentCount =
            MetadataDeclarationText.GetSegmentCount(definition);
        for (int index = 0; index < segmentCount; index++)
        {
            string metadataName =
                MetadataDeclarationText.RenderSegment(
                    definition,
                    index);
            int introduced =
                definition.IntroducedGenericParameterCounts[index];
            string suffix = $"`{introduced}";
            string identifier = introduced > 0
                && metadataName.EndsWith(
                    suffix,
                    StringComparison.Ordinal)
                    ? metadataName[..^suffix.Length]
                    : metadataName;
            AppendSpellingComponent(
                builder,
                CSharpIdentifier.Escape(identifier),
                introduced);
            if (!AddSpellingTarget(
                    builder.ToString(),
                    definition.GetDefinitionPrefix(index + 1),
                    definitions))
            {
                return false;
            }
        }

        return true;
    }

    static bool AddSpellingTarget(
        string key,
        MetadataNamedTypeIdentity? definition,
        Dictionary<string, MetadataNamedTypeIdentity?> definitions)
    {
        if (definitions.TryGetValue(
                key,
                out MetadataNamedTypeIdentity? existing))
        {
            return existing == definition;
        }

        definitions.Add(key, definition);
        return true;
    }

    static void AppendSpellingComponent(
        System.Text.StringBuilder builder,
        string identifier,
        int genericArity)
    {
        builder.Append('|');
        builder.Append(identifier.Length);
        builder.Append(':');
        builder.Append(identifier);
        builder.Append('#');
        builder.Append(genericArity);
    }

    static string? SpellPrimitive(
        MetadataTypeIdentity.Primitive primitive)
    {
        string name =
            MetadataDeclarationText.RenderPrimitiveName(primitive);
        return name != "void"
            && PrimitiveTypeNames.TryToClrFullName(name, out _)
            ? name
            : null;
    }

    static string? SpellNamed(
        MetadataNamedTypeIdentity definition,
        ImmutableArray<MetadataTypeIdentity> arguments)
    {
        int segmentCount =
            MetadataDeclarationText.GetSegmentCount(definition);
        if (segmentCount == 0
            || definition.IntroducedGenericParameterCounts.IsDefault
            || segmentCount !=
                definition.IntroducedGenericParameterCounts.Length
            || definition.IntroducedGenericParameterCounts.Any(
                count => count < 0)
            || definition.IntroducedGenericParameterCounts.Sum()
                != arguments.Length)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder("global::");
        string namespaceName =
            MetadataDeclarationText.RenderNamespace(definition);
        if (namespaceName.Length > 0)
        {
            string[] namespaceSegments = namespaceName.Split('.');
            if (namespaceSegments.Any(segment =>
                    !IsCompilerPreservedIdentifier(segment)))
            {
                return null;
            }

            builder.AppendJoin(
                '.',
                namespaceSegments.Select(CSharpIdentifier.Escape));
            builder.Append('.');
        }

        int argumentIndex = 0;
        for (int segmentIndex = 0;
            segmentIndex < segmentCount;
            segmentIndex++)
        {
            if (segmentIndex > 0)
                builder.Append('.');

            string metadataName =
                MetadataDeclarationText.RenderSegment(
                    definition,
                    segmentIndex);
            int tick = metadataName.LastIndexOf('`');
            int introduced =
                definition.IntroducedGenericParameterCounts[segmentIndex];
            string expectedSuffix = $"`{introduced}";
            if (introduced == 0 && tick >= 0
                || introduced > 0
                    && !metadataName.EndsWith(
                        expectedSuffix,
                        StringComparison.Ordinal))
            {
                return null;
            }

            string identifier = introduced > 0
                ? metadataName[..^expectedSuffix.Length]
                : metadataName;
            if (!IsCompilerPreservedIdentifier(identifier))
                return null;

            builder.Append(CSharpIdentifier.Escape(identifier));
            if (introduced == 0)
                continue;

            builder.Append('<');
            for (int offset = 0; offset < introduced; offset++)
            {
                if (offset > 0)
                    builder.Append(", ");

                if (!TrySpellType(
                        arguments[argumentIndex++],
                        out CSharpTypeSpelling? argument))
                {
                    return null;
                }
                builder.Append(argument.Source);
            }
            builder.Append('>');
        }

        string source = builder.ToString();
        return source is
            "global::System.Void"
            or "global::System.TypedReference"
            or "global::System.ArgIterator"
            or "global::System.RuntimeArgumentHandle"
            ? null
            : source;
    }
}

public static class CSharpAcceptedDeclarationRenderer
{
    public static string RenderStub(
        CSharpAcceptedDeclarationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind switch
        {
            CSharpDeclarationKind.ExplicitInterfaceOperator =>
                $"static {request.ReturnType.Source} "
                + $"{request.ExplicitInterface.Source}."
                + $"{request.OperatorDeclaration}("
                + string.Join(
                    ", ",
                    request.Parameters.Select(parameter =>
                        $"{parameter.Type.Source} {parameter.Name}"))
                + ") => throw null;",
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Kind,
                "The accepted declaration kind is not supported."),
        };
    }
}
