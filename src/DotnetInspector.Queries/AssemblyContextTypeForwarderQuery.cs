using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Reads the exact Type names one realized participant forwards through its
/// <c>ExportedType</c> table. This is a metadata-table read: it opens no
/// target assembly and binds no forwarding route.
/// </summary>
public static class AssemblyContextTypeForwarderQuery
{
    public static AssemblyContextEntry<ImmutableArray<MetadataTypeDefinitionName>>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            int maximumRetainedDeclarations,
            int maximumRetainedTextCharacters)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedDeclarations);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedTextCharacters);
        if (!group.Participants.Any(candidate => ReferenceEquals(
                candidate.Assembly.Registration,
                participant.Assembly.Registration)))
        {
            throw new ArgumentException(
                "The requested participant is not a member of the assembly context group.",
                nameof(participant));
        }

        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyImageAccessResult<AssemblyTypeDeclarationInventoryOutcome> access =
            group.UseAssemblySession(
                participant.Assembly,
                session => session.TypeDeclarations(
                    maximumRetainedDeclarations,
                    maximumRetainedTextCharacters));
        return access switch
        {
            AssemblyImageAccessResult<AssemblyTypeDeclarationInventoryOutcome>.Available available =>
                available.Value switch
                {
                    AssemblyTypeDeclarationInventoryOutcome.Read read =>
                        new AssemblyContextEntry<ImmutableArray<MetadataTypeDefinitionName>>.Available(
                            subject,
                            read.Inventory.Forwarders),
                    AssemblyTypeDeclarationInventoryOutcome.Rejected rejected =>
                        new AssemblyContextEntry<ImmutableArray<MetadataTypeDefinitionName>>.Rejected(
                            subject,
                            rejected.Failure),
                    AssemblyTypeDeclarationInventoryOutcome.Incomplete incomplete =>
                        new AssemblyContextEntry<ImmutableArray<MetadataTypeDefinitionName>>.Failed(
                            subject,
                            new InvalidOperationException(
                                $"The Type declaration inventory reached its {incomplete.Bound} bound.")),
                    _ => throw new InvalidOperationException(
                        "Unknown Type declaration inventory outcome."),
                },
            AssemblyImageAccessResult<AssemblyTypeDeclarationInventoryOutcome>.Rejected rejected =>
                new AssemblyContextEntry<ImmutableArray<MetadataTypeDefinitionName>>.Rejected(
                    subject,
                    rejected.Failure),
            _ => throw new InvalidOperationException("Unknown assembly image access result."),
        };
    }
}
