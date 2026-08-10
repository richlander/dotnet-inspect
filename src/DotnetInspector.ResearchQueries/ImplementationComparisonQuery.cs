using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Content-shaped inputs for comparing C# and IL implementation evidence
/// across two already-acquired assembly versions.
/// </summary>
public sealed record ImplementationComparisonInput(
    IReadOnlyList<ImplementationAssemblyInput> OldAssemblies,
    IReadOnlyList<ImplementationAssemblyInput> NewAssemblies,
    IReadOnlySet<string>? TypeFilters = null,
    IReadOnlySet<string>? MemberTargetIdentities = null);

/// <summary>
/// Compares implementation evidence while retaining the Research-owned result
/// and Finding correspondence.
/// </summary>
public static class ImplementationComparisonQuery
{
    public static InspectionQuery<ImplementationDiffResult> Definition { get; } =
        new("Implementation comparison", InspectionCost.Unbounded);

    public static ImplementationDiffResult Execute(
        ImplementationComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.OldAssemblies);
        ArgumentNullException.ThrowIfNull(input.NewAssemblies);

        return ImplementationDiff.Compare(
            input.OldAssemblies,
            input.NewAssemblies,
            new ImplementationDiffOptions(
                TypeFilters: input.TypeFilters,
                MemberTargetIdentities: input.MemberTargetIdentities));
    }
}
