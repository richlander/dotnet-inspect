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

public abstract class TypeDeclaredMethodPopulationPreparedInspection
    : IDisposable
{
    private protected TypeDeclaredMethodPopulationPreparedInspection(
        MetadataTypeDefinitionName type)
    {
        Type = type;
    }

    public MetadataTypeDefinitionName Type { get; }

    public virtual void Dispose()
    {
    }

    public sealed class Ready
        : TypeDeclaredMethodPopulationPreparedInspection
    {
        internal Ready(
            MetadataTypeDefinitionName type,
            AssemblyContextDeclaredMethodPopulationExecution execution,
            TypeDeclaredMethodPopulationSubject subject,
            TypeDeclaredMethodPopulationBinding binding)
            : base(type)
        {
            Execution = execution;
            Subject = subject;
            Binding = binding;
        }

        internal AssemblyContextDeclaredMethodPopulationExecution Execution
        {
            get;
        }

        internal TypeDeclaredMethodPopulationSubject Subject { get; }

        internal TypeDeclaredMethodPopulationBinding Binding { get; }

        public override void Dispose() => Execution.Dispose();
    }

    public sealed class Rejected
        : TypeDeclaredMethodPopulationPreparedInspection
    {
        internal Rejected(
            MetadataTypeDefinitionName type,
            TypeDeclaredMethodPopulationOutcome.Rejected outcome)
            : base(type)
        {
            Outcome = outcome;
        }

        internal TypeDeclaredMethodPopulationOutcome.Rejected Outcome
        {
            get;
        }
    }

    public sealed class Failed
        : TypeDeclaredMethodPopulationPreparedInspection
    {
        internal Failed(
            MetadataTypeDefinitionName type,
            TypeDeclaredMethodPopulationOutcome.Failed outcome)
            : base(type)
        {
            Outcome = outcome;
        }

        internal TypeDeclaredMethodPopulationOutcome.Failed Outcome
        {
            get;
        }
    }
}

public static class TypeDeclaredMethodPopulationInspectionOperation
{
    private const string SharePath =
        "type-declared-method-population/share";
    private const string ShareReason =
        "Raw declared MethodDefs do not yet have a portable Workspace scenario.";
    private static readonly InspectionShare.NonProjectable s_share =
        new(
            SharePath,
            ShareReason);

    public static TypeDeclaredMethodPopulationPreparedInspection
        Prepare(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            MetadataTypeDefinitionName type,
            MetadataTypeDefinitionBinding binding,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(binding);
        cancellationToken.ThrowIfCancellationRequested();

        return Prepare(
            type,
            AssemblyContextDeclaredMethodPopulationQuery
                .PrepareParticipant(
                    group,
                    participant,
                    binding,
                    cancellationToken));
    }

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

        using TypeDeclaredMethodPopulationPreparedInspection prepared =
            Prepare(
                request.Group,
                request.Participant,
                request.Type,
                request.Binding,
                cancellationToken);
        return Execute(prepared, accepted, request.MaximumRows);
    }

    public static InspectionEnvelope<TypeDeclaredMethodPopulationOutcome>
        Execute(
            TypeDeclaredMethodPopulationPreparedInspection prepared,
            QuerySpaceRequest query,
            int maximumRows = int.MaxValue,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRows);
        cancellationToken.ThrowIfCancellationRequested();

        TypeDeclaredMethodPopulationQueryResult resolved =
            TypeDeclaredMethodPopulationQuery.ResolveRequest(
                query,
                cancellationToken);
        return resolved
            is TypeDeclaredMethodPopulationQueryResult.Accepted accepted
                ? Execute(prepared, accepted, maximumRows)
                : Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Rejected(
                        null,
                        prepared.Type,
                        TypeDeclaredMethodPopulationRejection
                            .QueryRejected));
    }

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Execute(
            TypeDeclaredMethodPopulationPreparedInspection prepared,
            TypeDeclaredMethodPopulationQueryResult.Accepted accepted,
            int maximumRows) =>
        prepared switch
        {
            TypeDeclaredMethodPopulationPreparedInspection.Ready
                ready =>
                    Project(
                        ready,
                        accepted.Terminal
                            == QuerySpaceTerminalRequirement.Count
                                ? ready.Execution.Count()
                                : ready.Execution.Rows(maximumRows)),
            TypeDeclaredMethodPopulationPreparedInspection.Rejected
                rejected =>
                    Envelope(rejected.Outcome),
            TypeDeclaredMethodPopulationPreparedInspection.Failed
                failed =>
                    Envelope(failed.Outcome),
            _ => throw new InvalidOperationException(
                "Unknown declared-method prepared inspection."),
        };

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Project(
            TypeDeclaredMethodPopulationPreparedInspection.Ready prepared,
            MetadataDeclaredMethodPopulationResult result) =>
        result.Kind switch
        {
            MetadataDeclaredMethodPopulationResultKind.Counted =>
                Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Counted(
                        prepared.Subject,
                        prepared.Type,
                        prepared.Binding,
                        result.Count,
                        result.Receipt)),
            MetadataDeclaredMethodPopulationResultKind.Read =>
                Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Read(
                        prepared.Subject,
                        prepared.Type,
                        prepared.Binding,
                        result.Count,
                        result.Rows,
                        result.Receipt)),
            MetadataDeclaredMethodPopulationResultKind.Incomplete =>
                Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Incomplete(
                        prepared.Subject,
                        prepared.Type,
                        prepared.Binding,
                        result.Count,
                        result.MaximumRows,
                        result.Receipt)),
            MetadataDeclaredMethodPopulationResultKind.Failed =>
                Envelope(
                    new TypeDeclaredMethodPopulationOutcome.Failed(
                        prepared.Subject,
                        prepared.Type,
                        Field(result.Detail!))),
            _ => throw new InvalidOperationException(
                "Unknown prepared declared-method result."),
        };

    private static TypeDeclaredMethodPopulationPreparedInspection
        Prepare(
            MetadataTypeDefinitionName type,
            AssemblyContextDeclaredMethodPopulationPreparation
                preparation) =>
        preparation switch
        {
            AssemblyContextDeclaredMethodPopulationPreparation.Ready
                ready =>
                    new TypeDeclaredMethodPopulationPreparedInspection
                        .Ready(
                            type,
                            ready.OpenExecution(),
                            Subject(ready.Subject),
                            Binding(ready.Binding)),
            AssemblyContextDeclaredMethodPopulationPreparation
                .ParticipantRejected rejected =>
                    new TypeDeclaredMethodPopulationPreparedInspection
                        .Rejected(
                            type,
                            new TypeDeclaredMethodPopulationOutcome
                                .Rejected(
                                    Subject(rejected.Subject),
                                    type,
                                    TypeDeclaredMethodPopulationRejection
                                        .ParticipantRejected)),
            AssemblyContextDeclaredMethodPopulationPreparation
                .BindingRejected rejected =>
                    new TypeDeclaredMethodPopulationPreparedInspection
                        .Rejected(
                            type,
                            new TypeDeclaredMethodPopulationOutcome
                                .Rejected(
                                    Subject(rejected.Subject),
                                    type,
                                    TypeDeclaredMethodPopulationRejection
                                        .BindingRejected)),
            AssemblyContextDeclaredMethodPopulationPreparation.Failed
                failed =>
                    new TypeDeclaredMethodPopulationPreparedInspection
                        .Failed(
                            type,
                            new TypeDeclaredMethodPopulationOutcome.Failed(
                                Subject(failed.Subject),
                                type,
                                Field(failed.Detail))),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context declared-method preparation."),
        };

    private static InspectionEnvelope<
        TypeDeclaredMethodPopulationOutcome> Envelope(
            TypeDeclaredMethodPopulationOutcome outcome) =>
        new(
            outcome,
            s_share);

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
