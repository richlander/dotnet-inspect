using ILInspector.Research;

namespace DotnetInspector.Queries;

/// <summary>
/// Builds the Research-owned settled document for one exact assembly pair.
/// </summary>
public static class ImplementationDiffDocumentQuery
{
    public static ImplementationDiffDocument Execute(
        ImplementationComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.OldAssemblies);
        ArgumentNullException.ThrowIfNull(input.NewAssemblies);

        if (input.OldAssemblies.Count != 1
            || input.NewAssemblies.Count != 1)
        {
            throw new ArgumentException(
                "Implementation Diff document queries require exactly one "
                    + "assembly on each endpoint.",
                nameof(input));
        }

        return ImplementationDiff.CompareExactPair(
            input.OldAssemblies[0],
            input.NewAssemblies[0],
            new ImplementationDiffOptions(
                TypeFilters: input.TypeFilters,
                MemberTargetIdentities: input.MemberTargetIdentities));
    }
}
