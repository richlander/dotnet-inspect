using ILInspector.Findings;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>One selected library and its bounded API projection.</summary>
public sealed class AssemblyContextApiComparisonEndpoint
{
    internal AssemblyContextApiComparisonEndpoint(
        AssemblyContextSubject subject,
        AssemblyContextApiSurfaceResult projection)
    {
        Subject = subject;
        Projection = projection;
    }

    public AssemblyContextSubject Subject { get; }
    public AssemblyContextApiSurfaceResult Projection { get; }
    public bool IsComplete => CompleteSurface is not null;

    internal ApiSurface? CompleteSurface =>
        Projection.Truncation is null
        && Projection.Assemblies.Assemblies is
            [AssemblyContextEntry<AssemblyApiSurface>.Available available]
        && available.Value.InspectionFailures.IsEmpty
        && available.Value.Surface.Types.All(type =>
            type.Members.All(member => member.SignatureDecodeStatus is null))
            ? available.Value.Surface
            : null;
}

/// <summary>
/// The ordered endpoint evidence and, only for fully projected inputs, their
/// Metadata-owned API comparison.
/// </summary>
public sealed class AssemblyContextApiComparisonResult
{
    internal AssemblyContextApiComparisonResult(
        ApiSurfaceScope scope,
        AssemblyContextApiComparisonEndpoint before,
        AssemblyContextApiComparisonEndpoint after,
        ApiFindingComparison? comparison)
    {
        Scope = scope;
        Before = before;
        After = after;
        Comparison = comparison;
    }

    public ApiSurfaceScope Scope { get; }
    public AssemblyContextApiComparisonEndpoint Before { get; }
    public AssemblyContextApiComparisonEndpoint After { get; }
    public ApiFindingComparison? Comparison { get; }
    public bool IsComplete =>
        Comparison is
        {
            Types.Value: FindingComparison<ApiTypeHandle>.Complete,
            Members.Value: FindingComparison<ApiMemberHandle>.Complete,
        };
    public bool IsExact => IsComplete && Comparison is { IsExact: true };
}

/// <summary>
/// Compares two explicitly selected libraries in retained contexts. Each side
/// receives the full caller-declared projection budget independently.
/// </summary>
public static class AssemblyContextApiComparisonQuery
{
    public static InspectionQuery<AssemblyContextApiComparisonResult> Definition
    { get; } = new(
        "Assembly context API comparison",
        InspectionCost.NetworkFree);

    public static AssemblyContextApiComparisonResult Execute(
        AssemblyContextGroup beforeGroup,
        AssemblyContextParticipant beforeParticipant,
        AssemblyContextGroup afterGroup,
        AssemblyContextParticipant afterParticipant,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits perEndpointLimits)
    {
        ArgumentNullException.ThrowIfNull(beforeGroup);
        ArgumentNullException.ThrowIfNull(beforeParticipant);
        ArgumentNullException.ThrowIfNull(afterGroup);
        ArgumentNullException.ThrowIfNull(afterParticipant);
        ArgumentNullException.ThrowIfNull(perEndpointLimits);
        if (!Enum.IsDefined(scope))
            throw new ArgumentOutOfRangeException(nameof(scope));

        AssemblyContextApiComparisonEndpoint before =
            Project(beforeGroup, beforeParticipant, scope, perEndpointLimits);
        AssemblyContextApiComparisonEndpoint after =
            Project(afterGroup, afterParticipant, scope, perEndpointLimits);

        ApiFindingComparison? comparison =
            before.CompleteSurface is { } beforeSurface
            && after.CompleteSurface is { } afterSurface
                ? ApiComparisonQuery.Execute(beforeSurface, afterSurface)
                : null;

        return new(scope, before, after, comparison);
    }

    static AssemblyContextApiComparisonEndpoint Project(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        ApiSurfaceScope scope,
        ApiSurfaceProjectionLimits limits)
        => new(
            new AssemblyContextSubject(participant.Assembly),
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                group,
                scope,
                limits,
                [participant]));
}
