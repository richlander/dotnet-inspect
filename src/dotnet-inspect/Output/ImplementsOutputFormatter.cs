using DotnetInspector.Commands;
using DotnetInspector.Views;

namespace DotnetInspector.Output;

/// <summary>
/// Builds the view model for implementer search results.
/// </summary>
public static class ImplementsOutputFormatter
{
    public static ImplementsResultView BuildView(string targetType, List<ImplementerResult> results)
    {
        return new ImplementsResultView
        {
            Title = $"Types Implementing {targetType}",
            Matches = results.Count,
            Description = results.Count == 0 ? "No implementing types found." : null,
            Rows = results.Count == 0 ? null : results
                .OrderBy(r => r.TypeName)
                .Select(r => new ImplementerRow(
                    r.TypeName, r.Kind, r.Relationship,
                    r.Assembly ?? "", SourceColumn.Format(r.Source, r.SourceVersion)))
                .ToList()
        };
    }
}
