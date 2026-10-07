using DotnetInspect.Cli.Options;
using DotnetInspector.Presentation;

namespace DotnetInspect.Cli.Planning;

internal abstract record LibraryCommandPlan
{
    private LibraryCommandPlan()
    {
    }

    internal sealed record Standard : LibraryCommandPlan;

    /// <summary>
    /// The Library subject's native Tree: one exact Library, its namespaces,
    /// and its public-surface Type declarations by Name.
    /// </summary>
    internal sealed record TypeHierarchy(
        LibraryTypeHierarchyPresentationFormat Format,
        bool Explicit)
        : LibraryCommandPlan;
}

internal abstract record LibraryCommandPlanningResult
{
    private LibraryCommandPlanningResult()
    {
    }

    internal sealed record Planned(LibraryCommandPlan Plan)
        : LibraryCommandPlanningResult;

    internal sealed record Rejected(string Error)
        : LibraryCommandPlanningResult;
}

internal static class LibraryCommandPlanner
{
    internal const string TreeAndMermaidError =
        "--tree and --mermaid select different Library hierarchy formats; choose one.";

    internal const string OneLibraryError =
        "The Library Type hierarchy requires one exact Library. Name the assembly within the package.";

    /// <summary>
    /// Plans a <c>library</c> invocation. The Type hierarchy is the default
    /// only when every explicit option selects the Library source, the
    /// hierarchy format, minimal verbosity, or diagnostic logging; any other
    /// demand keeps the standard path.
    /// </summary>
    internal static LibraryCommandPlanningResult Plan(
        LibraryOptions options,
        bool onlyHierarchyOptionsExplicitlySet,
        bool mermaidExplicitlySet)
    {
        ArgumentNullException.ThrowIfNull(options);

        bool tree = options.Tree;
        if (!onlyHierarchyOptionsExplicitlySet
            || options.Verbosity != Verbosity.Minimal
            || string.Equals(
                options.Tfm,
                "all",
                StringComparison.OrdinalIgnoreCase))
        {
            return new LibraryCommandPlanningResult.Planned(
                new LibraryCommandPlan.Standard());
        }
        if (tree && mermaidExplicitlySet)
        {
            return new LibraryCommandPlanningResult.Rejected(
                TreeAndMermaidError);
        }

        return new LibraryCommandPlanningResult.Planned(
            new LibraryCommandPlan.TypeHierarchy(
                mermaidExplicitlySet
                    ? LibraryTypeHierarchyPresentationFormat.Mermaid
                    : LibraryTypeHierarchyPresentationFormat.Tree,
                Explicit: tree || mermaidExplicitlySet));
    }
}
