using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Serialization;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record TypeDocumentInspectionPlan
{
    public TypeDocumentInspectionPlan(
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        TypeMemberGroupPopulationRequest? declarations = null,
        InspectionHierarchyRequest? hierarchy = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        if (hierarchy is not null && declarations?.Rows is null)
        {
            throw new ArgumentException(
                "A Type document hierarchy requires a member-group Rows request.",
                nameof(hierarchy));
        }

        Declarations = declarations;
        Hierarchy = hierarchy;
    }

    public MetadataTypeDefinitionName Type { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public TypeMemberGroupPopulationRequest? Declarations { get; }
    public InspectionHierarchyRequest? Hierarchy { get; }
}

public sealed record TypeDocumentInspectionRequest
{
    public TypeDocumentInspectionRequest(
        LibraryReference library,
        TypeDocumentInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeDocumentInspectionPlan Plan { get; }
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
        GenericParameters { get; }
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
    public int TypeDefinitionToken { get; }
    public TypeDocumentDeclarationSignature Signature { get; }
    public MetadataTypeDeclarationCategory Category { get; }
    public TypeAttributes Attributes { get; }
    public bool IsByRefLike { get; }
    public bool IsReadOnly { get; }
    public bool DefinesCoreLibraryRoot { get; }
    public int? DeclaringTypeDefinitionToken { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(TypeDocumentDeclarations.NotRequested),
    "not-requested")]
[JsonDerivedType(
    typeof(TypeDocumentDeclarations.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeDocumentDeclarations.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeDocumentDeclarations.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeDocumentDeclarations.Failed),
    "failed")]
public abstract record TypeDocumentDeclarations
{
    private protected TypeDocumentDeclarations()
    {
    }

    public sealed record NotRequested : TypeDocumentDeclarations;

    public sealed record Available(
        TypeMemberGroupPopulationResult Population)
        : TypeDocumentDeclarations;

    public sealed record Rejected(
        TypeMemberGroupPopulationInspectionRejection Reason)
        : TypeDocumentDeclarations;

    public sealed record Incomplete(
        TypeMemberGroupPopulationBound Bound,
        long Limit,
        long Measured)
        : TypeDocumentDeclarations;

    public sealed record Failed(
        TypeMemberGroupPopulationInspectionFailure Reason)
        : TypeDocumentDeclarations;
}

public sealed record TypeDocumentInspectionContent
{
    public TypeDocumentInspectionContent(
        TypeSubject subject,
        TypeDocumentDeclarations declarations,
        int assemblyBytes)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Declarations = declarations
            ?? throw new ArgumentNullException(nameof(declarations));
        ArgumentOutOfRangeException.ThrowIfNegative(assemblyBytes);
        if (declarations
                is TypeDocumentDeclarations.Available available
            && !Matches(
                subject,
                available.Population.Binding))
        {
            throw new ArgumentException(
                "The Type subject and Member-group population binding must identify the same exact Type.",
                nameof(declarations));
        }

        AssemblyBytes = assemblyBytes;
    }

    public TypeSubject Subject { get; }
    public TypeDocumentDeclarations Declarations { get; }
    public int AssemblyBytes { get; }

    private static bool Matches(
        TypeSubject subject,
        TypeMemberGroupPopulationBinding binding) =>
        subject.Assembly == binding.Assembly
        && subject.ModuleVersionId == binding.ModuleVersionId
        && subject.Type == binding.Type
        && subject.TypeDefinitionToken
            == binding.TypeDefinitionToken;
}

public enum TypeDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
}

public enum TypeDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum TypeDocumentInspectionBound
{
    MetadataRows,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Failed),
    "failed")]
public abstract record TypeDocumentInspectionOutcome
{
    private protected TypeDocumentInspectionOutcome()
    {
    }

    public sealed record Available(TypeDocumentInspectionContent Document)
        : TypeDocumentInspectionOutcome;

    public sealed record Rejected(TypeDocumentInspectionRejection Reason)
        : TypeDocumentInspectionOutcome;

    public sealed record Incomplete(
        TypeDocumentInspectionBound Bound,
        long Limit,
        long Measured)
        : TypeDocumentInspectionOutcome;

    public sealed record Failed(TypeDocumentInspectionFailure Reason)
        : TypeDocumentInspectionOutcome;
}
