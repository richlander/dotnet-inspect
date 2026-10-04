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

        ImplementationComparisonResult comparison =
            ImplementationComparisonQuery.Execute(input);
        if (comparison is not ImplementationComparisonResult.Compared compared)
        {
            throw new InspectionQueryException(
                comparison is ImplementationComparisonResult.TargetFailed failed
                    ? failed.Summary
                    : $"Implementation Diff comparison did not complete "
                        + $"({comparison.GetType().Name}).");
        }

        return ImplementationDiff.CreateExactPairDocument(
            input.OldAssemblies[0],
            input.NewAssemblies[0],
            compared.Comparison,
            new ImplementationDiffOptions(
                TypeFilters: input.TypeFilters),
            input.MemberSelections is null
                ? null
                : [.. input.MemberSelections.Select(selection =>
                    new ImplementationDiffDocumentMemberSelection(
                        selection.DeclaringType.ToEscapedFullName(),
                        selection.Selector.RequestedText))]);
    }
}
