using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Ordered participant outcomes scoped to one explicit scanner selection.</summary>
public sealed record AssemblyContextIntegrationScanResult(
    EcosystemIntegrationScannerBinding Binding,
    ImmutableArray<AssemblyIntegrationsEntry> Assemblies)
{
    public bool IsComplete =>
        Assemblies.All(static entry => entry is AssemblyIntegrationsEntry.Selected);
}

/// <summary>
/// Executes one selected scanner over realized participants. This operation is
/// uncached and does not use the full-scan query definition.
/// </summary>
public static class AssemblyContextIntegrationScanQuery
{
    public static AssemblyContextIntegrationScanResult Execute(
        AssemblyContextGroup group,
        EcosystemIntegrationScannerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(binding);

        var entries = ImmutableArray.CreateBuilder<AssemblyIntegrationsEntry>(
            group.Participants.Length);
        foreach (AssemblyContextParticipant participant in group.Participants)
            entries.Add(ExecuteParticipantCore(group, participant, binding));
        return new AssemblyContextIntegrationScanResult(
            binding,
            entries.MoveToImmutable());
    }

    public static AssemblyContextIntegrationScanResult Execute(
        PackageAssemblyContextRoleProjection role,
        EcosystemIntegrationScannerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(binding);
        return role.Use(group => Execute(group, binding));
    }

    public static AssemblyIntegrationsEntry ExecuteParticipant(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        EcosystemIntegrationScannerBinding binding)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        ArgumentNullException.ThrowIfNull(binding);
        if (!group.Participants.Any(candidate => ReferenceEquals(
                candidate.Assembly.Registration,
                participant.Assembly.Registration)))
        {
            throw new ArgumentException(
                "The requested participant is not a member of the assembly context group.",
                nameof(participant));
        }

        return ExecuteParticipantCore(group, participant, binding);
    }

    static AssemblyIntegrationsEntry ExecuteParticipantCore(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        EcosystemIntegrationScannerBinding binding)
    {
        var subject = new AssemblyContextSubject(participant.Assembly);
        AssemblyImageAccessResult<AssemblyIntegrationsEntry> access =
            group.UseAssemblySession(
                participant.Assembly,
                session => Inspect(subject, session, binding));
        return access switch
        {
            AssemblyImageAccessResult<AssemblyIntegrationsEntry>.Available available =>
                available.Value,
            AssemblyImageAccessResult<AssemblyIntegrationsEntry>.Rejected rejected =>
                new AssemblyIntegrationsEntry.Rejected(subject, rejected.Failure),
            _ => throw new InvalidOperationException("Unknown assembly image access result."),
        };
    }

    static AssemblyIntegrationsEntry Inspect(
        AssemblyContextSubject subject,
        AssemblyInspectionSession session,
        EcosystemIntegrationScannerBinding binding)
    {
        EcosystemIntegrationObservationContext context;
        try
        {
            context = session.EcosystemIntegrationObservations();
        }
        catch (BadImageFormatException ex)
        {
            return new AssemblyIntegrationsEntry.Failed(subject, ex);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return new AssemblyIntegrationsEntry.Failed(
                subject,
                new BadImageFormatException(
                    "The selected image contains invalid metadata.",
                    ex));
        }

        // Callback and projection faults are not malformed inspected metadata.
        return new AssemblyIntegrationsEntry.Selected(
            subject,
            EcosystemIntegrationScanner.Scan(context, binding).ToImmutableArray());
    }
}
