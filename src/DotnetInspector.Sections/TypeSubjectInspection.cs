using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Serialization;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public enum TypeSubjectInspectionDemand
{
    Declaration,
    CompleteSignature,
}

public sealed record TypeSubjectInspectionPlan
{
    public TypeSubjectInspectionPlan(
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        TypeMemberGroupPopulationRequest? declarations = null,
        MetadataExactTypeInput? exactInput = null,
        TypeSubjectInspectionDemand demand =
            TypeSubjectInspectionDemand.Declaration)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        if (exactInput is not null && !type.Equals(exactInput.Type))
        {
            throw new ArgumentException(
                "The exact input must describe the planned Type.",
                nameof(exactInput));
        }
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        if (!Enum.IsDefined(demand))
            throw new ArgumentOutOfRangeException(nameof(demand));

        Declarations = declarations;
        ExactInput = exactInput;
        Demand = demand;
    }

    public MetadataTypeDefinitionName Type { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public TypeMemberGroupPopulationRequest? Declarations { get; }
    public MetadataExactTypeInput? ExactInput { get; }
    public TypeSubjectInspectionDemand Demand { get; }
}

public sealed record TypeSubjectInspectionRequest
{
    public TypeSubjectInspectionRequest(
        LibraryReference library,
        TypeSubjectInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeSubjectInspectionPlan Plan { get; }
}

public sealed record TypeDocumentGenericParameter(
    int DefinitionSegmentIndex,
    int MetadataIndex,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
        InertString Name,
    GenericParameterAttributes Attributes);

public sealed record TypeDocumentDeclarationSignature
{
    [JsonConstructor]
    public TypeDocumentDeclarationSignature(
        ImmutableArray<TypeDocumentGenericParameter> genericParameters)
    {
        if (genericParameters.IsDefault)
        {
            throw new ArgumentException(
                "A Type declaration signature requires an explicit generic-parameter population.",
                nameof(genericParameters));
        }

        GenericParameters = genericParameters;
    }

    public ImmutableArray<TypeDocumentGenericParameter>
        GenericParameters
    { get; }
}

public sealed record TypeSubject
{
    [JsonConstructor]
    public TypeSubject(
        LibraryAssemblyIdentity assembly,
        Guid moduleVersionId,
        MetadataTypeDefinitionName type,
        int typeDefinitionToken,
        TypeDocumentDeclarationSignature signature,
        MetadataTypeDeclarationCategory category,
        TypeAttributes attributes,
        bool isHidden,
        bool isCompilerGenerated,
        bool isByRefLike,
        bool isReadOnly,
        bool definesCoreLibraryRoot,
        int? declaringTypeDefinitionToken)
    {
        Assembly = assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An exact Type subject requires a module version identifier.",
                nameof(moduleVersionId));
        }
        Type = type ?? throw new ArgumentNullException(nameof(type));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            typeDefinitionToken);
        Signature = signature
            ?? throw new ArgumentNullException(nameof(signature));
        if (!Enum.IsDefined(category))
            throw new ArgumentOutOfRangeException(nameof(category));
        if (declaringTypeDefinitionToken is { } declaringToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                declaringToken);
        }

        ModuleVersionId = moduleVersionId;
        TypeDefinitionToken = typeDefinitionToken;
        Category = category;
        Attributes = attributes;
        IsHidden = isHidden;
        IsCompilerGenerated = isCompilerGenerated;
        IsByRefLike = isByRefLike;
        IsReadOnly = isReadOnly;
        DefinesCoreLibraryRoot = definesCoreLibraryRoot;
        DeclaringTypeDefinitionToken = declaringTypeDefinitionToken;
    }

    public TypeSubject(
        LibraryTypeDocumentSubjectCorrespondence libraryCorrespondence,
        LibraryAssemblyIdentity assembly,
        Guid moduleVersionId,
        MetadataTypeDefinitionName type,
        int typeDefinitionToken,
        TypeDocumentDeclarationSignature signature,
        MetadataTypeDeclarationCategory category,
        TypeAttributes attributes,
        bool isHidden,
        bool isCompilerGenerated,
        bool isByRefLike,
        bool isReadOnly,
        bool definesCoreLibraryRoot,
        int? declaringTypeDefinitionToken)
        : this(
            assembly,
            moduleVersionId,
            type,
            typeDefinitionToken,
            signature,
            category,
            attributes,
            isHidden,
            isCompilerGenerated,
            isByRefLike,
            isReadOnly,
            definesCoreLibraryRoot,
            declaringTypeDefinitionToken)
    {
        LibraryCorrespondence = libraryCorrespondence
            ?? throw new ArgumentNullException(
                nameof(libraryCorrespondence));
    }

    [JsonIgnore]
    public LibraryTypeDocumentSubjectCorrespondence? LibraryCorrespondence
    {
        get;
    }
    [JsonIgnore]
    public LibraryReference? RequestedLibrary =>
        LibraryCorrespondence?.RequestedLibrary;
    [JsonIgnore]
    public LibraryReference? DefiningLibrary =>
        LibraryCorrespondence?.DefiningLibrary;
    [JsonIgnore]
    public LibraryContentReference? DefiningApiContent =>
        LibraryCorrespondence?.DefiningApiContent;
    public LibraryAssemblyIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public MetadataTypeDefinitionName Type { get; }
    /// <summary>
    /// The matched byte-native name contained for presentation, created on first read.
    /// </summary>
    /// <remarks>
    /// Most Type outputs never present this name as bytes, so containment waits for the sink
    /// that does. Absent for a deserialized subject, which carries no byte-native identity.
    /// </remarks>
    [JsonIgnore]
    public MetadataContainedTypeDefinitionName? ContainedType =>
        _matchedName?.Contained;

    internal MetadataUtf8TypeDefinitionName? MatchedName
    {
        get => _matchedName?.Name;
        init => _matchedName = value is null ? null : new(value);
    }

    // A reference holder rather than a cache field on the record: record equality compares
    // fields, so a field filled on first read would make otherwise equal copies differ.
    private readonly LazyContainedName? _matchedName;

    /// <summary>
    /// The byte-native generic-parameter declarations behind <see cref="Signature"/>.
    /// </summary>
    /// <remarks>
    /// Absent for a deserialized subject, which carries only the contained
    /// <see cref="Signature"/> names.
    /// </remarks>
    [JsonIgnore]
    public MetadataTypeDeclarationSignature? DeclarationSignature { get; internal init; }

    /// <summary>
    /// Contains the matched name for display, without each segment's canonical arity suffix.
    /// </summary>
    /// <returns>
    /// The display name, or <see langword="null"/> when the subject carries no byte-native
    /// identity or declarations.
    /// </returns>
    public MetadataContainedTypeDefinitionName? ContainDisplayType() =>
        _matchedName is { } matched
            && DeclarationSignature is { } signature
            ? matched.Name.ContainDisplay(TextPolicy.Field, signature)
            : null;

    public int TypeDefinitionToken { get; }
    public TypeDocumentDeclarationSignature Signature { get; }
    public MetadataTypeDeclarationCategory Category { get; }
    public TypeAttributes Attributes { get; }
    public bool IsHidden { get; }
    public bool IsCompilerGenerated { get; }
    public bool IsByRefLike { get; }
    public bool IsReadOnly { get; }
    public bool DefinesCoreLibraryRoot { get; }
    public int? DeclaringTypeDefinitionToken { get; }

    private sealed class LazyContainedName(MetadataUtf8TypeDefinitionName name)
    {
        private MetadataContainedTypeDefinitionName? _contained;

        internal MetadataUtf8TypeDefinitionName Name => name;

        internal MetadataContainedTypeDefinitionName Contained =>
            _contained ??= name.Contain(TextPolicy.Field);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(TypeSubjectMemberGroups.NotRequested),
    "not-requested")]
[JsonDerivedType(
    typeof(TypeSubjectMemberGroups.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeSubjectMemberGroups.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeSubjectMemberGroups.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeSubjectMemberGroups.Failed),
    "failed")]
public abstract record TypeSubjectMemberGroups
{
    private protected TypeSubjectMemberGroups()
    {
    }

    public sealed record NotRequested : TypeSubjectMemberGroups;

    public sealed record Available(
        TypeMemberGroupPopulationResult Population)
        : TypeSubjectMemberGroups;

    public sealed record Rejected(
        TypeMemberGroupPopulationInspectionRejection Reason)
        : TypeSubjectMemberGroups;

    public sealed record Incomplete(
        TypeMemberGroupPopulationBound Bound,
        long Limit,
        long Measured)
        : TypeSubjectMemberGroups;

    public sealed record Failed(
        TypeMemberGroupPopulationInspectionFailure Reason)
        : TypeSubjectMemberGroups;
}

public sealed record TypeSubjectInspectionContent
{
    public TypeSubjectInspectionContent(
        TypeSubject subject,
        TypeSubjectMemberGroups declarations,
        int assemblyBytes,
        MetadataTypeDeclarationBaseKind baseKind,
        int interfaceCount,
        TypeSignature? completeSignature = null)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Declarations = declarations
            ?? throw new ArgumentNullException(nameof(declarations));
        ArgumentOutOfRangeException.ThrowIfNegative(assemblyBytes);
        if (!Enum.IsDefined(baseKind))
            throw new ArgumentOutOfRangeException(nameof(baseKind));
        ArgumentOutOfRangeException.ThrowIfNegative(interfaceCount);
        if (declarations
                is TypeSubjectMemberGroups.Available available
            && !Matches(
                subject,
                available.Population.Binding))
        {
            throw new ArgumentException(
                "The Type subject and Member-group population binding must identify the same exact Type.",
                nameof(declarations));
        }

        AssemblyBytes = assemblyBytes;
        BaseKind = baseKind;
        InterfaceCount = interfaceCount;
        CompleteSignature = completeSignature;
    }

    public TypeSubject Subject { get; }
    public TypeSubjectMemberGroups Declarations { get; }
    public int AssemblyBytes { get; }
    public MetadataTypeDeclarationBaseKind BaseKind { get; }
    public int InterfaceCount { get; }
    [JsonIgnore]
    public TypeSignature? CompleteSignature { get; }

    private static bool Matches(
        TypeSubject subject,
        TypeMemberGroupPopulationBinding binding) =>
        subject.Assembly == binding.Assembly
        && subject.ModuleVersionId == binding.ModuleVersionId
        && subject.Type == binding.Type
        && subject.TypeDefinitionToken
            == binding.TypeDefinitionToken;
}

public enum TypeSubjectInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
}

public enum TypeSubjectInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
    UnsupportedSignatureEvidence,
}

public enum TypeSubjectInspectionBound
{
    MetadataRows,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(TypeSubjectInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeSubjectInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeSubjectInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeSubjectInspectionOutcome.Failed),
    "failed")]
public abstract record TypeSubjectInspectionOutcome
{
    private protected TypeSubjectInspectionOutcome()
    {
    }

    public sealed record Available(TypeSubjectInspectionContent Document)
        : TypeSubjectInspectionOutcome;

    public sealed record Rejected(TypeSubjectInspectionRejection Reason)
        : TypeSubjectInspectionOutcome;

    public sealed record Incomplete(
        TypeSubjectInspectionBound Bound,
        long Limit,
        long Measured)
        : TypeSubjectInspectionOutcome;

    public sealed record Failed(TypeSubjectInspectionFailure Reason)
        : TypeSubjectInspectionOutcome;
}
