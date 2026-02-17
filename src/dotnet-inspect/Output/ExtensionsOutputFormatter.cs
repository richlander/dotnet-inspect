using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Views;

namespace DotnetInspector.Output;

/// <summary>
/// Builds view models for extension method search results.
/// </summary>
public static class ExtensionsOutputFormatter
{
    public static ExtensionsResultView BuildView(string targetType, List<ExtensionMethodResult> results,
        Verbosity verbosity = Verbosity.Normal)
    {
        if (results.Count == 0)
        {
            return new ExtensionsResultView
            {
                Title = $"Extension Methods for {targetType}",
                Description = "No extension methods found."
            };
        }

        var directExtensions = results.Where(r => r.ReachablePath == null).ToList();
        var reachableExtensions = results.Where(r => r.ReachablePath != null)
            .GroupBy(r => r.ReachablePath)
            .ToList();

        if (verbosity == Verbosity.Quiet)
        {
            return BuildQuietView(targetType, directExtensions, reachableExtensions);
        }

        return BuildNormalView(targetType, results);
    }

    private static ExtensionsResultView BuildQuietView(string targetType,
        List<ExtensionMethodResult> directExtensions,
        List<IGrouping<string?, ExtensionMethodResult>> reachableExtensions)
    {
        List<ExtensionCountRow> counts = [new(targetType, directExtensions.Count.ToString(), "")];

        foreach (var group in reachableExtensions)
        {
            var reachableType = group.First().ReachableFromType ?? "";
            counts.Add(new ExtensionCountRow(reachableType, group.Count().ToString(), group.Key ?? ""));
        }

        return new ExtensionsResultView
        {
            Title = $"Extension Methods for {targetType}",
            Counts = counts
        };
    }

    private static ExtensionsResultView BuildNormalView(string targetType, List<ExtensionMethodResult> results)
    {
        var rows = results
            .OrderBy(r => r.ReachablePath ?? "")
            .ThenBy(r => r.ExtensionClass)
            .ThenBy(r => r.MethodName)
            .Select(r =>
            {
                var sourceDisplay = r.SourceVersion != null
                    ? $"{r.Source}@{r.SourceVersion}"
                    : r.Source ?? "";
                var name = r.Overloads > 1
                    ? $"{r.MethodName} ({r.Overloads} overloads)"
                    : r.MethodName;
                var type = r.ReachableFromType ?? targetType;
                var via = r.ReachablePath ?? "";
                return new ExtensionRow(name, r.Kind, r.ExtensionClass ?? "", r.Assembly ?? "", sourceDisplay, type, via);
            })
            .ToList();

        return new ExtensionsResultView
        {
            Title = $"Extension Methods for {targetType}",
            Extensions = rows
        };
    }
}
