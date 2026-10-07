using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The library command's native lowering under
/// <c>docs/design/section-shapes.md</c> (slice 2 of #9511): a lone explicitly
/// selected section renders in its declared shape's native format when the
/// caller named no format, through <see cref="SectionShapeOutputPolicy"/>,
/// the shared CLI policy.
/// </summary>
public partial class LibraryCommand
{
    /// <summary>
    /// A lone selected Table streams TSV rows and the lone Hierarchy,
    /// <c>Reference Hierarchy</c>, renders its tree; the library catalog has no
    /// Text, and the Graph <c>Dependency Structure</c> declares no shape, so it
    /// keeps its current default. Explicit intent — a format flag or
    /// environment default, <c>--print</c>, <c>--row</c>, <c>--value</c>,
    /// <c>--urls</c>, <c>--paths</c>, <c>--tree</c>, <c>--count</c>, a
    /// projection, an envelope, discovery, or an analysis query — leaves the
    /// options unchanged. A selection that named several sections stays a
    /// composition even after its scalar records leave a Rows execution.
    /// </summary>
    private static LibraryOptions ApplyNativeShapeFormat(
        LibraryOptions options)
    {
        bool hasExplicitOutputIntent =
            options.FormatExplicitlySet
            || options.JsonOutput
            || options.Markdown
            || options.PlainText
            || options.Tabular
            || options.Tsv
            || options.Jsonl
            || options.NoHeader
            || options.Print
            || options.ProjectionRow is not null
            || options.Value
            || options.Urls
            || options.Paths
            || options.Count
            || options.Tree
            || options.Discover is not null
            || options.Effective
            || options.Schema
            || options.Fields is { Length: > 0 }
            || options.Columns is { Length: > 0 }
            || options.EnvelopeOutput
            || options.JsonArray
            || options.ExtractResources is not null
            || options.ScalarSectionsOmitted;
        SectionNativeOutput? nativeOutput =
            SectionShapeOutputPolicy.ResolveNativeOutput(
                options.SelectExplicitlySet,
                options.IncludeSections,
                LibrarySections.SectionCatalog.SectionShapes,
                hasExplicitOutputIntent);

        return nativeOutput switch
        {
            SectionNativeOutput.TabularRows => options with
            {
                Format = OutputFormat.Tsv,
                Tabular = true,
                Tsv = true,
                Jsonl = false,
            },
            SectionNativeOutput.HierarchyTree => options with { Tree = true },
            _ => options,
        };
    }
}
