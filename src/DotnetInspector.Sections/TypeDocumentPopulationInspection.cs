using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record TypeSubject
{
    public TypeSubject(
        LibraryAssemblyIdentity assembly,
        Guid moduleVersionId,
        MetadataTypeDefinitionName type,
        int typeDefinitionToken)
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

        ModuleVersionId = moduleVersionId;
        TypeDefinitionToken = typeDefinitionToken;
    }

    public LibraryAssemblyIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public MetadataTypeDefinitionName Type { get; }
    public int TypeDefinitionToken { get; }
}

/// <summary>
/// A resource-free document for one exact Type and its requested declared
/// Member-group population.
/// </summary>
public sealed record TypeDocument
{
    public TypeDocument(
        TypeSubject subject,
        TypeMemberGroupPopulationResult members)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        Members = members
            ?? throw new ArgumentNullException(nameof(members));

        TypeMemberGroupPopulationBinding binding = members.Binding;
        if (subject.Assembly != binding.Assembly
            || subject.ModuleVersionId != binding.ModuleVersionId
            || subject.Type != binding.Type
            || subject.TypeDefinitionToken
                != binding.TypeDefinitionToken)
        {
            throw new ArgumentException(
                "The Type subject and Member-group population binding must identify the same exact Type.",
                nameof(members));
        }
    }

    public TypeSubject Subject { get; }
    public TypeMemberGroupPopulationResult Members { get; }
}

public sealed record TypeDocumentInspectionRequest
{
    public TypeDocumentInspectionRequest(
        LibraryReference library,
        TypeMemberGroupPopulationInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeMemberGroupPopulationInspectionPlan Plan { get; }
}

public enum TypeDocumentInspectionFailure
{
    NotManagedAssembly,
    ManagedModule,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Available),
    typeDiscriminator: "available")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Rejected),
    typeDiscriminator: "rejected")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Incomplete),
    typeDiscriminator: "incomplete")]
[JsonDerivedType(
    typeof(TypeDocumentInspectionOutcome.Failed),
    typeDiscriminator: "failed")]
public abstract record TypeDocumentInspectionOutcome
{
    private TypeDocumentInspectionOutcome()
    {
    }

    public sealed record Available(TypeDocument Document)
        : TypeDocumentInspectionOutcome;

    public sealed record Rejected(
        TypeMemberGroupPopulationInspectionRejection Reason)
        : TypeDocumentInspectionOutcome;

    public sealed record Incomplete(
        TypeMemberGroupPopulationBound Bound,
        long Limit,
        long Measured)
        : TypeDocumentInspectionOutcome;

    public sealed record Failed(TypeDocumentInspectionFailure Reason)
        : TypeDocumentInspectionOutcome;
}

/// <summary>
/// Composes one exact Type and its compact declared Member-group result into
/// a document without reconstructing population semantics.
/// </summary>
public static class TypeDocumentInspectionOperation
{
    public static InspectionEnvelope<TypeDocumentInspectionOutcome> Execute(
        TypeDocumentInspectionRequest request,
        LibraryOperationLease lease,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Library);
        ArgumentNullException.ThrowIfNull(request.Plan);
        ArgumentNullException.ThrowIfNull(lease);

        InspectionEnvelope<TypeMemberGroupPopulationInspectionOutcome>
            population =
                TypeMemberGroupPopulationInspectionOperation.Execute(
                    new(request.Library, request.Plan),
                    lease,
                    cancellationToken);
        return new(
            Project(population.Content),
            population.Share,
            population.Diagnostics);
    }

    private static TypeDocumentInspectionOutcome Project(
        TypeMemberGroupPopulationInspectionOutcome outcome) =>
        outcome switch
        {
            TypeMemberGroupPopulationInspectionOutcome.Available available =>
                Available(available.Content),
            TypeMemberGroupPopulationInspectionOutcome.Rejected rejected =>
                new TypeDocumentInspectionOutcome.Rejected(
                    rejected.Reason),
            TypeMemberGroupPopulationInspectionOutcome.Incomplete incomplete =>
                new TypeDocumentInspectionOutcome.Incomplete(
                    incomplete.Bound,
                    incomplete.Limit,
                    incomplete.Measured),
            TypeMemberGroupPopulationInspectionOutcome.Failed failed =>
                new TypeDocumentInspectionOutcome.Failed(
                    Map(failed.Reason)),
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population inspection outcome."),
        };

    private static TypeDocumentInspectionOutcome.Available Available(
        TypeMemberGroupPopulationContent content)
    {
        TypeMemberGroupPopulationBinding binding =
            content.Members.Binding;
        return new(
            new(
                new(
                    content.Assembly,
                    binding.ModuleVersionId,
                    content.Type,
                    binding.TypeDefinitionToken),
                content.Members));
    }

    private static TypeDocumentInspectionFailure Map(
        TypeMemberGroupPopulationInspectionFailure failure) =>
        failure switch
        {
            TypeMemberGroupPopulationInspectionFailure.NotManagedAssembly =>
                TypeDocumentInspectionFailure.NotManagedAssembly,
            TypeMemberGroupPopulationInspectionFailure.ManagedModule =>
                TypeDocumentInspectionFailure.ManagedModule,
            TypeMemberGroupPopulationInspectionFailure
                    .UnsupportedWindowsMetadata =>
                TypeDocumentInspectionFailure.UnsupportedWindowsMetadata,
            TypeMemberGroupPopulationInspectionFailure.MalformedMetadata =>
                TypeDocumentInspectionFailure.MalformedMetadata,
            TypeMemberGroupPopulationInspectionFailure
                    .EmptyModuleVersionId =>
                TypeDocumentInspectionFailure.EmptyModuleVersionId,
            _ => throw new InvalidOperationException(
                "Unknown Type Member-group population failure."),
        };
}
