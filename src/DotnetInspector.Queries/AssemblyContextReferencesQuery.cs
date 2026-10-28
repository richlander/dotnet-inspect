using System.Collections.Immutable;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One detached direct assembly-reference row.</summary>
public sealed record AssemblyReferenceRow(
    string Name,
    string Version,
    string? Culture,
    string? PublicKeyToken);

/// <summary>
/// One exact direct-reference request and the completed selection from its
/// admitted assembly context.
/// </summary>
public sealed record AssemblyContextReferenceBindingEvidence
{
    internal AssemblyContextReferenceBindingEvidence(
        AssemblyReferenceRow reference,
        AssemblyBindingRequest request,
        AssemblyBindingSelectionSnapshot referencingContext)
    {
        Reference = reference
            ?? throw new ArgumentNullException(nameof(reference));
        Request = request
            ?? throw new ArgumentNullException(nameof(request));
        ReferencingContext = referencingContext
            ?? throw new ArgumentNullException(nameof(referencingContext));
    }

    public AssemblyReferenceRow Reference { get; }

    public AssemblyBindingRequest Request { get; }

    public AssemblyBindingSelectionSnapshot ReferencingContext { get; }
}

/// <summary>Why one participant could not issue detached assembly-reference rows.</summary>
public enum AssemblyReferenceRowsRejectionKind
{
    Unreadable,
    InvalidImage,
    ResourceBudget,
    UnsupportedMetadataFormat,
}

/// <summary>One detached assembly-reference row rejection.</summary>
public readonly record struct AssemblyReferenceRowsRejection(
    AssemblyReferenceRowsRejectionKind Kind,
    string Detail);

/// <summary>
/// Reads direct assembly references from every participant in one binding-consistent context.
/// </summary>
public static class AssemblyContextReferencesQuery
{
    public static InspectionQuery<
        AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>>>
        Definition { get; } =
        new("Assembly context references", InspectionCost.Unbounded);

    public static AssemblyContextResult<ImmutableArray<AssemblyReferenceIdentity>> Execute(
        AssemblyContextGroup group)
        => AssemblyContextQueryExecutor.Execute(
            group,
            AssemblyReferencesQuery.Read);

    /// <summary>Reads one participant without releasing it from a reusable group.</summary>
    public static AssemblyContextEntry<ImmutableArray<AssemblyReferenceIdentity>>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
        => AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            AssemblyReferencesQuery.Read);

    /// <summary>
    /// Reads one participant and detaches its direct references from Metadata identity types.
    /// </summary>
    public static AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>
        ExecuteParticipantRows(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
        => AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            AssemblyReferencesQuery.ReadRows);

    /// <summary>
    /// Reads one participant's direct references, creates their exact binding
    /// requests, and completes the participant's current-context selection.
    /// </summary>
    public static AssemblyContextEntry<
        ImmutableArray<AssemblyContextReferenceBindingEvidence>>
        ExecuteParticipantBindingEvidence(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        AssemblyBindingPolicyVersion expectedVersion =
            group.BindingPolicyVersion;

        return AssemblyContextQueryExecutor.ExecuteParticipant(
            group,
            participant,
            session =>
            {
                if (!ReferenceEquals(
                        participant.BindingPolicy.Version,
                        expectedVersion))
                {
                    throw new InvalidOperationException(
                        "The participant binding policy changed after its context was admitted.");
                }

                ImmutableArray<AssemblyReferenceIdentity> references =
                    AssemblyReferencesQuery.Read(session);
                var evidence = ImmutableArray.CreateBuilder<
                    AssemblyContextReferenceBindingEvidence>(
                        references.Length);
                foreach (AssemblyReferenceIdentity reference in references)
                {
                    var request = new AssemblyBindingRequest(
                        AssemblyBindingTarget.Reference(reference),
                        AssemblyBindingOrigin.FromAssembly(
                            participant.Assembly),
                        AssemblyResolutionScope.Any);
                    AssemblyBindingSelectionSnapshot selection =
                        participant.BindingPolicy.Select(request);
                    if (selection is null
                        || !ReferenceEquals(
                            selection.Version,
                            expectedVersion)
                        || !ReferenceEquals(
                            participant.BindingPolicy.Version,
                            expectedVersion))
                    {
                        throw new InvalidOperationException(
                            "The participant binding policy changed while direct references were selected.");
                    }

                    AssemblyBindingSelection validated =
                        AssemblyBindingSelection
                            .ValidateForMetadataRequest(
                                request,
                                selection.Selection);
                    evidence.Add(
                        new(
                            AssemblyReferencesQuery.ToRow(reference),
                            request,
                            ReferenceEquals(
                                validated,
                                selection.Selection)
                                ? selection
                                : new(
                                    selection.Version,
                                    validated)));
                }
                return evidence.ToImmutable();
            });
    }

    /// <summary>Detaches one participant rejection from Metadata failure types.</summary>
    public static AssemblyReferenceRowsRejection GetRowsRejection(
        AssemblyContextEntry<ImmutableArray<AssemblyReferenceRow>>.Rejected rejected)
    {
        ArgumentNullException.ThrowIfNull(rejected);
        return new(
            rejected.Failure.Kind switch
            {
                CandidateOpenFailureKind.Unreadable =>
                    AssemblyReferenceRowsRejectionKind.Unreadable,
                CandidateOpenFailureKind.InvalidImage =>
                    AssemblyReferenceRowsRejectionKind.InvalidImage,
                CandidateOpenFailureKind.ResourceBudget =>
                    AssemblyReferenceRowsRejectionKind.ResourceBudget,
                CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                    AssemblyReferenceRowsRejectionKind.UnsupportedMetadataFormat,
                _ => throw new InvalidOperationException(
                    "Unknown assembly-context candidate failure."),
            },
            rejected.Failure.Detail);
    }
}
