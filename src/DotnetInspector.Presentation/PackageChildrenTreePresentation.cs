using DotnetInspector.Sections;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Presentation;

public enum PackageChildrenTreePresentationFormat
{
    Markdown,
    PlainText,
    Mermaid,
}

public sealed record PackageChildrenTreePresentationPlan
{
    public PackageChildrenTreePresentationPlan(
        PackageChildrenTreePresentationFormat format,
        bool suppressNodes,
        bool collapseToolDependencies,
        IReadOnlySet<string> duplicateLibraryNames)
    {
        if (!Enum.IsDefined(format))
            throw new ArgumentOutOfRangeException(nameof(format));

        Format = format;
        SuppressNodes = suppressNodes;
        CollapseToolDependencies = collapseToolDependencies;
        DuplicateLibraryNames = duplicateLibraryNames
            ?? throw new ArgumentNullException(
                nameof(duplicateLibraryNames));
    }

    public PackageChildrenTreePresentationFormat Format { get; }
    public bool SuppressNodes { get; }
    public bool CollapseToolDependencies { get; }
    public IReadOnlySet<string> DuplicateLibraryNames { get; }
}

public static class PackageChildrenTreePresentation
{
    public static void Write(
        PackageChildrenDocument document,
        PackageChildrenTreePresentationPlan plan,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(output);

        string title = ResultTitle.Compose(
            $"{document.Subject.PackageId} "
                + $"{document.Subject.PackageVersion}",
            PackageChildrenProperties.For(document));
        IMarkoutFormatter formatter = plan.Format switch
        {
            PackageChildrenTreePresentationFormat.Markdown =>
                new MarkdownFormatter(),
            PackageChildrenTreePresentationFormat.PlainText =>
                new PlainTextFormatter(),
            PackageChildrenTreePresentationFormat.Mermaid =>
                new MermaidFormatter(),
            _ => throw new InvalidOperationException(
                "Unknown Package children presentation format."),
        };

        if (plan.Format
            != PackageChildrenTreePresentationFormat.Mermaid)
        {
            output.WriteLine(title);
        }

        var writer = new MarkoutWriter(output, formatter);
        writer.WriteTree(tree =>
        {
            if (plan.Format
                == PackageChildrenTreePresentationFormat.Mermaid)
            {
                tree.WriteNode(
                    title,
                    isLastSibling: true,
                    (Document: document, Plan: plan),
                    static (children, state) =>
                    {
                        if (!state.Plan.SuppressNodes)
                        {
                            WriteNodes(
                                children,
                                state.Document,
                                state.Plan);
                        }
                    });
            }
            else if (!plan.SuppressNodes)
            {
                WriteNodes(tree, document, plan);
            }
        });
        writer.Flush();
    }

    private static void WriteNodes(
        StreamingTreeWriter writer,
        PackageChildrenDocument document,
        PackageChildrenTreePresentationPlan plan)
    {
        switch (document.Kind)
        {
            case PackageChildrenKind.Libraries:
                WriteLibraries(writer, document, plan);
                break;
            case PackageChildrenKind.RuntimeIdentifierPackages:
                writer.WriteNode(
                    $"RID packages "
                        + $"({document.RuntimeIdentifierPackages.Length})",
                    isLastSibling: true,
                    document.RuntimeIdentifierPackages,
                    static (children, packages) =>
                    {
                        for (int index = 0;
                             index < packages.Length;
                             index++)
                        {
                            PackageRuntimeIdentifierChild package =
                                packages[index];
                            children.WriteNode(
                                $"{package.RuntimeIdentifier}: "
                                    + $"{package.PackageId}",
                                isLastSibling:
                                    index == packages.Length - 1);
                        }
                    });
                break;
            case PackageChildrenKind.NoManagedLibraries:
                writer.WriteNode(
                    "No managed Libraries"
                        + (document.Detail is { } detail
                            ? $" ({detail})"
                            : ""),
                    isLastSibling: true);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown Package children kind.");
        }
    }

    private static void WriteLibraries(
        StreamingTreeWriter writer,
        PackageChildrenDocument document,
        PackageChildrenTreePresentationPlan plan)
    {
        if (document.Libraries.IsEmpty)
        {
            writer.WriteNode(
                document.Status switch
                {
                    PackageChildrenStatus.SelectedEmpty =>
                        "No Libraries in the selected compile group",
                    PackageChildrenStatus.NoCompileAssets =>
                        "No compile Libraries",
                    PackageChildrenStatus.NoApplicableTarget =>
                        "No applicable Library target",
                    PackageChildrenStatus.InvalidSelection =>
                        "Invalid Library selection",
                    PackageChildrenStatus.Unavailable =>
                        "Library population unavailable",
                    _ => "No Libraries",
                }
                + (document.Detail is { } detail
                    ? $" ({detail})"
                    : ""),
                isLastSibling: true);
            return;
        }

        int entryPointCount = document.Libraries.Count(
            static library =>
                library.Role
                    == PackageLibraryChildRole.ToolEntryPoint);
        int dependencyCount =
            document.Libraries.Length - entryPointCount;
        bool collapseDependencies =
            plan.CollapseToolDependencies
            && entryPointCount > 0
            && dependencyCount > 8;
        int nodeCount =
            entryPointCount
            + (collapseDependencies ? 1 : dependencyCount);
        int nodeIndex = 0;

        foreach (PackageLibraryChild library in document.Libraries)
        {
            if (library.Role
                != PackageLibraryChildRole.ToolEntryPoint)
            {
                continue;
            }

            writer.WriteNode(
                FormatLibrary(
                    library,
                    plan.DuplicateLibraryNames),
                isLastSibling: ++nodeIndex == nodeCount);
        }

        if (collapseDependencies)
        {
            writer.WriteNode(
                $"Dependencies ({dependencyCount} Libraries; "
                    + "use -v:n for full inventory)",
                isLastSibling: true);
            return;
        }

        foreach (PackageLibraryChild library in document.Libraries)
        {
            if (library.Role
                == PackageLibraryChildRole.ToolEntryPoint)
            {
                continue;
            }

            writer.WriteNode(
                FormatLibrary(
                    library,
                    plan.DuplicateLibraryNames),
                isLastSibling: ++nodeIndex == nodeCount);
        }
    }

    private static string FormatLibrary(
        PackageLibraryChild library,
        IReadOnlySet<string> duplicateNames)
    {
        string assemblyName = library.AssemblyName.ToString();
        string label = duplicateNames.Contains(assemblyName)
            ? library.AssetPath.ToString()
            : assemblyName;
        return library.Role
            == PackageLibraryChildRole.ToolEntryPoint
                ? label + " (entry point)"
                : label;
    }
}
