using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Serialization;

using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record TypeOverviewDocumentInspectionPlan
{
    public TypeOverviewDocumentInspectionPlan(
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        TypeMemberGroupPopulationRequest? declarations = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        Declarations = declarations;
    }

    public MetadataTypeDefinitionName Type { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public TypeMemberGroupPopulationRequest? Declarations { get; }
}

public sealed record TypeOverviewDocumentInspectionRequest
{
    public TypeOverviewDocumentInspectionRequest(
        LibraryReference library,
        TypeOverviewDocumentInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeOverviewDocumentInspectionPlan Plan { get; }
}

public sealed record TypeOverviewDocumentGenericParameter(
    int DefinitionSegmentIndex,
    int MetadataIndex,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
        InertString Name,
    GenericParameterAttributes Attributes);

public sealed record TypeOverviewDocumentDeclarationSignature(
    ImmutableArray<TypeOverviewDocumentGenericParameter> GenericParameters);

public sealed record TypeSubject
{
    public TypeSubject(
        LibraryTypeOverviewDocumentSubjectCorrespondence libraryCorrespondence,
        LibraryAssemblyIdentity assembly,
        Guid moduleVersionId,
        MetadataTypeDefinitionName type,
        int typeDefinitionToken,
        TypeOverviewDocumentDeclarationSignature signature,
        MetadataTypeDeclarationCategory category,
        TypeAttributes attributes,
        bool isByRefLike,
        bool definesCoreLibraryRoot,
        int? declaringTypeDefinitionToken)
    {
        LibraryCorrespondence = libraryCorrespondence
            ?? throw new ArgumentNullException(
                nameof(libraryCorrespondence));
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

        ModuleVersionId = moduleVersionId;
        TypeDefinitionToken = typeDefinitionToken;
        Category = category;
        Attributes = attributes;
        IsByRefLike = isByRefLike;
        DefinesCoreLibraryRoot = definesCoreLibraryRoot;
        DeclaringTypeDefinitionToken = declaringTypeDefinitionToken;
    }

    [JsonIgnore]
    public LibraryTypeOverviewDocumentSubjectCorrespondence LibraryCorrespondence { get; }
    [JsonIgnore]
    public LibraryReference RequestedLibrary =>
        LibraryCorrespondence.RequestedLibrary;
    [JsonIgnore]
    public LibraryReference DefiningLibrary =>
        LibraryCorrespondence.DefiningLibrary;
    [JsonIgnore]
    public LibraryContentReference DefiningApiContent =>
        LibraryCorrespondence.DefiningApiContent;
    public LibraryAssemblyIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public MetadataTypeDefinitionName Type { get; }
    public int TypeDefinitionToken { get; }
    public TypeOverviewDocumentDeclarationSignature Signature { get; }
    public MetadataTypeDeclarationCategory Category { get; }
    public TypeAttributes Attributes { get; }
    public bool IsByRefLike { get; }
    public bool DefinesCoreLibraryRoot { get; }
    public int? DeclaringTypeDefinitionToken { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentDeclarations.NotRequested),
    "not-requested")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentDeclarations.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentDeclarations.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentDeclarations.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentDeclarations.Failed),
    "failed")]
public abstract record TypeOverviewDocumentDeclarations
{
    private protected TypeOverviewDocumentDeclarations()
    {
    }

    public sealed record NotRequested : TypeOverviewDocumentDeclarations;

    public sealed record Available(
        TypeMemberGroupPopulationResult Population)
        : TypeOverviewDocumentDeclarations;

    public sealed record Rejected(
        TypeMemberGroupPopulationInspectionRejection Reason)
        : TypeOverviewDocumentDeclarations;

    public sealed record Incomplete(
        TypeMemberGroupPopulationBound Bound,
        long Limit,
        long Measured)
        : TypeOverviewDocumentDeclarations;

    public sealed record Failed(
        TypeMemberGroupPopulationInspectionFailure Reason)
        : TypeOverviewDocumentDeclarations;
}

public sealed record TypeOverviewDocument
{
    public TypeOverviewDocument(
        TypeSubject subject,
        TypeOverviewDocumentDeclarations declarations,
        int assemblyBytes)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Declarations = declarations
            ?? throw new ArgumentNullException(nameof(declarations));
        ArgumentOutOfRangeException.ThrowIfNegative(assemblyBytes);
        if (declarations
                is TypeOverviewDocumentDeclarations.Available available
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
    public TypeOverviewDocumentDeclarations Declarations { get; }
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

public enum TypeOverviewDocumentInspectionRejection
{
    LeaseReferenceMismatch,
    AssemblyIdentityMismatch,
    TypeNotFound,
    TypeAmbiguous,
}

public enum TypeOverviewDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public enum TypeOverviewDocumentInspectionBound
{
    MetadataRows,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeOverviewDocumentInspectionOutcome.Failed),
    "failed")]
public abstract record TypeOverviewDocumentInspectionOutcome
{
    private protected TypeOverviewDocumentInspectionOutcome()
    {
    }

    public sealed record Available(TypeOverviewDocument Document)
        : TypeOverviewDocumentInspectionOutcome;

    public sealed record Rejected(TypeOverviewDocumentInspectionRejection Reason)
        : TypeOverviewDocumentInspectionOutcome;

    public sealed record Incomplete(
        TypeOverviewDocumentInspectionBound Bound,
        long Limit,
        long Measured)
        : TypeOverviewDocumentInspectionOutcome;

    public sealed record Failed(TypeOverviewDocumentInspectionFailure Reason)
        : TypeOverviewDocumentInspectionOutcome;
}
