using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Libraries;
using ILInspector.Metadata;

namespace DotnetInspector.Sections;

public sealed record TypeSignatureGenericParameter
{
    public TypeSignatureGenericParameter(
        int definitionSegmentIndex,
        int metadataIndex,
        MetadataUtf8Name name,
        string? variance,
        string? primaryConstraint,
        ImmutableArray<MetadataTypeSignatureConstraintEvidence>
            typeConstraints,
        string? constructorConstraint,
        string? allowsRefStructConstraint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            definitionSegmentIndex);
        ArgumentOutOfRangeException.ThrowIfNegative(metadataIndex);
        if (name.Length == 0)
            throw new ArgumentException(
                "A Type parameter requires a Name.",
                nameof(name));
        if (typeConstraints.IsDefault)
        {
            throw new ArgumentException(
                "A Type parameter requires an explicit Type-constraint population.",
                nameof(typeConstraints));
        }

        DefinitionSegmentIndex = definitionSegmentIndex;
        MetadataIndex = metadataIndex;
        Name = name;
        Variance = variance;
        PrimaryConstraint = primaryConstraint;
        TypeConstraints = typeConstraints;
        ConstructorConstraint = constructorConstraint;
        AllowsRefStructConstraint = allowsRefStructConstraint;
    }

    public int DefinitionSegmentIndex { get; }
    public int MetadataIndex { get; }
    public MetadataUtf8Name Name { get; }
    public string? Variance { get; }
    public string? PrimaryConstraint { get; }
    public ImmutableArray<MetadataTypeSignatureConstraintEvidence>
        TypeConstraints
    { get; }
    public string? ConstructorConstraint { get; }
    public string? AllowsRefStructConstraint { get; }
}

public sealed record TypeSignature
{
    public TypeSignature(
        TypeSubject subject,
        MetadataUtf8TypeDefinitionName subjectName,
        MetadataTypeSignatureTypeEvidence? baseType,
        ImmutableArray<TypeSignatureGenericParameter> typeParameters,
        int interfaceCount,
        bool areEnclosingTypesExternallyReachable,
        int nestedTypeCount)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        SubjectName = subjectName
            ?? throw new ArgumentNullException(nameof(subjectName));
        if (typeParameters.IsDefault)
        {
            throw new ArgumentException(
                "A Type Signature requires an explicit Type-parameter population.",
                nameof(typeParameters));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(interfaceCount);
        ArgumentOutOfRangeException.ThrowIfNegative(nestedTypeCount);

        BaseType = baseType;
        TypeParameters = typeParameters;
        InterfaceCount = interfaceCount;
        AreEnclosingTypesExternallyReachable =
            areEnclosingTypesExternallyReachable;
        NestedTypeCount = nestedTypeCount;
    }

    public TypeSubject Subject { get; }
    public MetadataUtf8TypeDefinitionName SubjectName { get; }
    public MetadataTypeSignatureTypeEvidence? BaseType { get; }
    public ImmutableArray<TypeSignatureGenericParameter>
        TypeParameters
    { get; }
    public int InterfaceCount { get; }
    public bool AreEnclosingTypesExternallyReachable { get; }
    public int NestedTypeCount { get; }
}

public sealed record TypeSignatureInspectionPlan
{
    public TypeSignatureInspectionPlan(
        MetadataTypeDefinitionName type,
        ApiSurfaceExtractionBounds bounds,
        MetadataExactTypeInput? exactInput = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Bounds = bounds
            ?? throw new ArgumentNullException(nameof(bounds));
        if (exactInput is not null && !type.Equals(exactInput.Type))
        {
            throw new ArgumentException(
                "The exact input must describe the planned Type.",
                nameof(exactInput));
        }
        ExactInput = exactInput;
    }

    public MetadataTypeDefinitionName Type { get; }
    public ApiSurfaceExtractionBounds Bounds { get; }
    public MetadataExactTypeInput? ExactInput { get; }

    internal TypeSubjectInspectionPlan ToSubjectPlan() =>
        new(
            Type,
            Bounds,
            exactInput: ExactInput,
            demand:
                TypeSubjectInspectionDemand.CompleteSignature);
}

public sealed record TypeSignatureInspectionRequest
{
    public TypeSignatureInspectionRequest(
        LibraryReference library,
        TypeSignatureInspectionPlan plan)
    {
        Library = library
            ?? throw new ArgumentNullException(nameof(library));
        Plan = plan
            ?? throw new ArgumentNullException(nameof(plan));
    }

    public LibraryReference Library { get; }
    public TypeSignatureInspectionPlan Plan { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "outcome")]
[JsonDerivedType(typeof(TypeSignatureInspectionOutcome.Available), "available")]
[JsonDerivedType(typeof(TypeSignatureInspectionOutcome.Rejected), "rejected")]
[JsonDerivedType(typeof(TypeSignatureInspectionOutcome.Incomplete), "incomplete")]
[JsonDerivedType(typeof(TypeSignatureInspectionOutcome.Failed), "failed")]
public abstract record TypeSignatureInspectionOutcome
{
    private protected TypeSignatureInspectionOutcome()
    {
    }

    public sealed record Available(TypeSignature Signature)
        : TypeSignatureInspectionOutcome;

    public sealed record Rejected(TypeSubjectInspectionRejection Reason)
        : TypeSignatureInspectionOutcome;

    public sealed record Incomplete(
        TypeSubjectInspectionBound Bound,
        long Limit,
        long Measured)
        : TypeSignatureInspectionOutcome;

    public sealed record Failed(TypeSubjectInspectionFailure Reason)
        : TypeSignatureInspectionOutcome;
}

public static class TypeSignatureInspectionOperation
{
    public static InspectionEnvelope<TypeSignatureInspectionOutcome>
        Execute(
            ResolvedAssemblyReference assembly,
            TypeSignatureInspectionPlan plan,
            CancellationToken cancellationToken = default) =>
        Project(
            TypeSubjectInspectionOperation.Execute(
                assembly,
                plan.ToSubjectPlan(),
                cancellationToken));

    public static InspectionEnvelope<TypeSignatureInspectionOutcome>
        Execute(
            TypeSignatureInspectionRequest request,
            LibraryOperationLease lease,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Project(
            TypeSubjectInspectionOperation.Execute(
                new(
                    request.Library,
                    request.Plan.ToSubjectPlan()),
                lease,
                cancellationToken));
    }

    private static InspectionEnvelope<TypeSignatureInspectionOutcome>
        Project(
            InspectionEnvelope<TypeSubjectInspectionOutcome> envelope)
    {
        TypeSignatureInspectionOutcome outcome =
            envelope.Content switch
            {
                TypeSubjectInspectionOutcome.Available available
                    when available.Document.CompleteSignature
                        is { } signature =>
                    new TypeSignatureInspectionOutcome.Available(
                        signature),
                TypeSubjectInspectionOutcome.Available =>
                    new TypeSignatureInspectionOutcome.Failed(
                        TypeSubjectInspectionFailure
                            .UnsupportedSignatureEvidence),
                TypeSubjectInspectionOutcome.Rejected rejected =>
                    new TypeSignatureInspectionOutcome.Rejected(
                        rejected.Reason),
                TypeSubjectInspectionOutcome.Incomplete incomplete =>
                    new TypeSignatureInspectionOutcome.Incomplete(
                        incomplete.Bound,
                        incomplete.Limit,
                        incomplete.Measured),
                TypeSubjectInspectionOutcome.Failed failed =>
                    new TypeSignatureInspectionOutcome.Failed(
                        failed.Reason),
                _ => throw new InvalidOperationException(
                    "Unknown Type subject inspection outcome."),
            };
        return new(
            outcome,
            envelope.Share,
            envelope.Diagnostics);
    }
}
