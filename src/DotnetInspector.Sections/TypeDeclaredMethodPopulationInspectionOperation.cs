using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.Queries;
using ILInspector.Metadata;
using InertText;
using QuerySpace.Composition;

namespace DotnetInspector.Sections;

public enum TypeDeclaredMethodPopulationRejection
{
    QueryRejected,
    ParticipantRejected,
    BindingRejected,
}

public sealed record TypeDeclaredMethodPopulationSubject(
    AssemblyReferenceIdentity Identity,
    AssemblyResolutionProvenance Provenance);

public sealed record TypeDeclaredMethodPopulationBinding
{
    public TypeDeclaredMethodPopulationBinding(
        Guid moduleVersionId,
        int typeDefinitionToken)
    {
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A declared-method binding requires a module version identifier.",
                nameof(moduleVersionId));
        }
        if (typeDefinitionToken <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(typeDefinitionToken));
        }

        ModuleVersionId = moduleVersionId;
        TypeDefinitionToken = typeDefinitionToken;
    }

    public Guid ModuleVersionId { get; }

    public int TypeDefinitionToken { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "status")]
[JsonDerivedType(
    typeof(TypeDeclaredMethodPopulationOutcome.Counted),
    "counted")]
[JsonDerivedType(
    typeof(TypeDeclaredMethodPopulationOutcome.Read),
    "read")]
[JsonDerivedType(
    typeof(TypeDeclaredMethodPopulationOutcome.Incomplete),
    "incomplete")]
[JsonDerivedType(
    typeof(TypeDeclaredMethodPopulationOutcome.Rejected),
    "rejected")]
[JsonDerivedType(
    typeof(TypeDeclaredMethodPopulationOutcome.Failed),
    "failed")]
public abstract record TypeDeclaredMethodPopulationOutcome(
    TypeDeclaredMethodPopulationSubject? Subject,
    MetadataTypeDefinitionName Type)
{
    public sealed record Counted(
        TypeDeclaredMethodPopulationSubject Subject,
        MetadataTypeDefinitionName Type,
        TypeDeclaredMethodPopulationBinding Binding,
        int Count,
        MetadataDeclaredMethodPopulationReceipt Receipt)
        : TypeDeclaredMethodPopulationOutcome(Subject, Type);

    public sealed record Read(
        TypeDeclaredMethodPopulationSubject Subject,
        MetadataTypeDefinitionName Type,
        TypeDeclaredMethodPopulationBinding Binding,
        int Count,
        ImmutableArray<int> Rows,
        MetadataDeclaredMethodPopulationReceipt Receipt)
        : TypeDeclaredMethodPopulationOutcome(Subject, Type);

    public sealed record Incomplete(
        TypeDeclaredMethodPopulationSubject Subject,
        MetadataTypeDefinitionName Type,
        TypeDeclaredMethodPopulationBinding Binding,
        int Count,
        long Limit,
        MetadataDeclaredMethodPopulationReceipt Receipt)
        : TypeDeclaredMethodPopulationOutcome(Subject, Type);

    public sealed record Rejected(
        TypeDeclaredMethodPopulationSubject? Subject,
        MetadataTypeDefinitionName Type,
        TypeDeclaredMethodPopulationRejection Reason)
        : TypeDeclaredMethodPopulationOutcome(Subject, Type);

    public sealed record Failed(
        TypeDeclaredMethodPopulationSubject Subject,
        MetadataTypeDefinitionName Type,
        InertString Detail)
        : TypeDeclaredMethodPopulationOutcome(Subject, Type);
}

public sealed record TypeDeclaredMethodPopulationInspectionRequest
{
    public TypeDeclaredMethodPopulationInspectionRequest(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        MetadataTypeDefinitionName type,
        MetadataTypeDefinitionBinding binding,
        QuerySpaceRequest query,
        int maximumRows = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);

        Group = group;
        Participant = participant;
        Type = type;
        Binding = binding;
        Query = query;
        MaximumRows = maximumRows;
    }

    public AssemblyContextGroup Group { get; }

    public AssemblyContextParticipant Participant { get; }

    public MetadataTypeDefinitionName Type { get; }

    public MetadataTypeDefinitionBinding Binding { get; }

    public QuerySpaceRequest Query { get; }

    public int MaximumRows { get; }
}

public static class TypeDeclaredMethodPopulationInspectionOperation
{
    private const string SharePath =
        "type-declared-method-population/share";
    private const string ShareReason =
        "Raw declared MethodDefs do not yet have a portable Workspace scenario.";

    public static InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
        Execute(
            TypeDeclaredMethodPopulationInspectionRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        TypeDeclaredMethodPopulationQueryResult query =
            TypeDeclaredMethodPopulationQuery.ResolveRequest(
                request.Query,
                cancellationToken);
        if (query is not
            TypeDeclaredMethodPopulationQueryResult.Accepted accepted)
        {
            return Envelope(
                new TypeDeclaredMethodPopulationOutcome.Rejected(
                    null,
                    request.Type,
                    TypeDeclaredMethodPopulationRejection.QueryRejected));
        }

        MetadataDeclaredMethodPopulationTerminal terminal =
            accepted.Terminal == QuerySpaceTerminalRequirement.Count
                ? MetadataDeclaredMethodPopulationTerminal.Count
                : MetadataDeclaredMethodPopulationTerminal.Rows;
        AssemblyContextEntry<
            MetadataDeclaredMethodPopulationOutcome> entry =
                AssemblyContextDeclaredMethodPopulationQuery
                    .ExecuteParticipant(
                        request.Group,
                        request.Participant,
                        request.Binding,
                        terminal,
                        request.MaximumRows,
                        cancellationToken);
        return entry switch
        {
            AssemblyContextEntry<
                MetadataDeclaredMethodPopulationOutcome>
                .Available available =>
                    Project(
                        Subject(available.Subject),
                        request.Type,
                        Binding(request.Binding),
                        available.Value),
            AssemblyContextEntry<
                MetadataDeclaredMethodPopulationOutcome>
                .Rejected rejected =>
                    Envelope(
                        new TypeDeclaredMethodPopulationOutcome.Rejected(
                            Subject(rejected.Subject),
                            request.Type,
                            TypeDeclaredMethodPopulationRejection
                                .ParticipantRejected)),
            AssemblyContextEntry<
                MetadataDeclaredMethodPopulationOutcome>
                .Failed failed =>
                    Envelope(
                        new TypeDeclaredMethodPopulationOutcome.Failed(
                            Subject(failed.Subject),
                            request.Type,
                            Field(failed.Error.Message))),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context declared-method outcome."),
        };
    }

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Project(
            TypeDeclaredMethodPopulationSubject subject,
            MetadataTypeDefinitionName type,
            TypeDeclaredMethodPopulationBinding binding,
            MetadataDeclaredMethodPopulationOutcome population) =>
        population switch
        {
            MetadataDeclaredMethodPopulationOutcome.Counted counted =>
                Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Counted(
                        subject,
                        type,
                        binding,
                        counted.Count,
                        counted.Receipt)),
            MetadataDeclaredMethodPopulationOutcome.Read read =>
                Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Read(
                        subject,
                        type,
                        binding,
                        read.Count,
                        read.Rows,
                        read.Receipt)),
            MetadataDeclaredMethodPopulationOutcome.Incomplete
                incomplete =>
                    Envelope(
                        new TypeDeclaredMethodPopulationOutcome.Incomplete(
                            subject,
                            type,
                            binding,
                            incomplete.Count,
                            incomplete.MaximumRows,
                            incomplete.Receipt)),
            MetadataDeclaredMethodPopulationOutcome.Rejected =>
                Rejected(
                    subject,
                    type,
                    TypeDeclaredMethodPopulationRejection.BindingRejected),
            MetadataDeclaredMethodPopulationOutcome.Failed failed =>
                Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Failed(
                        subject,
                        type,
                        Field(failed.Detail))),
            _ => throw new InvalidOperationException(
                "Unknown Metadata declared-method outcome."),
        };

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Rejected(
            TypeDeclaredMethodPopulationSubject subject,
            MetadataTypeDefinitionName type,
            TypeDeclaredMethodPopulationRejection reason) =>
        Envelope(
            new TypeDeclaredMethodPopulationOutcome.Rejected(
                subject,
                type,
                reason));

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Envelope(
            TypeDeclaredMethodPopulationOutcome outcome) =>
        new(
            outcome,
            new InspectionShare.NonProjectable(
                SharePath,
                ShareReason));

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);

    private static TypeDeclaredMethodPopulationSubject Subject(
        AssemblyContextSubject subject) =>
        new(subject.Identity, subject.Provenance);

    private static TypeDeclaredMethodPopulationBinding Binding(
        MetadataTypeDefinitionBinding binding) =>
        new(
            binding.ModuleVersionId,
            binding.Definition.Value);
}
