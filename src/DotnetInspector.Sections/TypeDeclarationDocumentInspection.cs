using System.Reflection;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspector.Sections;

public sealed record TypeDocumentInspectionPlan
{
    public TypeDocumentInspectionPlan(
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

public sealed record TypeDocumentSubject(
    LibraryAssemblyIdentity DefiningAssembly,
    Guid ModuleVersionId,
    MetadataTypeDefinitionName Type,
    int TypeDefinitionToken,
    [property: JsonConverter(typeof(InertStringJsonConverter))]
    InertString DisplaySignature,
    MetadataTypeDeclarationCategory Category,
    TypeAttributes Attributes,
    bool IsByRefLike,
    bool DefinesCoreLibraryRoot,
    int? DeclaringTypeDefinitionToken);

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

public sealed record TypeDocument(
    TypeDocumentSubject Subject,
    TypeDocumentDeclarations Declarations,
    int AssemblyBytes);

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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
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

    public sealed record Available(TypeDocument Document)
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
