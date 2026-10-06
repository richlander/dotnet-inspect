using ILInspector.Metadata;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Commands;

/// <summary>
/// The type and member commands' adoption of <c>docs/design/section-shapes.md</c>
/// (slice 2 of #9385 and #9386): a lone explicitly selected section renders in
/// its declared shape's native format when the caller named no format, and a
/// scalar section rejects the row terminals before acquisition. Both go through
/// <see cref="SectionShapeOutputPolicy"/>, the shared CLI policy.
/// </summary>
public partial class ApiCommand
{
    /// <summary>
    /// Applies the native format of a lone selected section when the caller
    /// named no format: a Table streams TSV rows and a Text prints its payload
    /// (no type or member section is a Hierarchy). Explicit intent — a format
    /// flag or environment default, <c>--print</c>, <c>--row</c>, <c>--tree</c>,
    /// <c>--count</c>, a projection, a shape flag, discovery, an envelope, or an
    /// analysis query — leaves the options unchanged. Idempotent: the preamble
    /// runs more than once for a member request, and a second pass sees the
    /// first pass's result as explicit intent.
    /// </summary>
    internal static ApiOptions ApplyNativeShapeOutput(
        ApiOptions options,
        IReadOnlyDictionary<string, SectionShape> shapes,
        IReadOnlyCollection<string>? effectiveSections)
    {
        if (options.SelectDeferredToListing)
            return options;

        // The effective selection includes sections the command will add on
        // the user's behalf (Callers under a caller scope), so a lone -S that
        // becomes a composition never takes a one-section native format.
        SectionNativeOutput? nativeOutput =
            SectionShapeOutputPolicy.ResolveNativeOutput(
                options.SelectionIsExplicit,
                effectiveSections,
                shapes,
                options.HasExplicitOutputIntent || options.NativeTextPayload);

        return nativeOutput switch
        {
            SectionNativeOutput.TabularRows => options with
            {
                Format = OutputFormat.Tsv,
                Tabular = true,
                Tsv = true,
                Jsonl = false,
            },
            SectionNativeOutput.TextPayload => options with
            {
                NativeTextPayload = true,
            },
            SectionNativeOutput.HierarchyTree => options with { Tree = true },
            _ => options,
        };
    }

    /// <summary>
    /// A lone scalar section (a field-set record such as <c>Type Info</c> or
    /// <c>API Info</c>, or a Text payload with no declared inventory) has no
    /// rows under <c>docs/design/section-cardinality.md</c>,
    /// so <c>--count</c> and the <c>--rows</c> window are rejected before
    /// acquisition. A bare <c>-n</c> stays the rendered-line window. Count maps
    /// over several sections keep their per-section meaning.
    /// </summary>
    internal static string? ValidateApiScalarTerminals(
        ApiOptions options,
        IReadOnlyDictionary<string, SectionCardinalityDeclaration> cardinalities,
        IReadOnlyCollection<string>? effectiveSections)
    {
        if (options.SelectDeferredToListing)
            return null;

        SectionTerminalCapability? terminal =
            options.Count
                ? SectionTerminalCapability.Count
                : options.Rows is not null
                    ? SectionTerminalCapability.Rows
                    : null;
        // The same effective selection the native decision uses: a caller scope
        // adds Callers, so "-S IL --bin X --count" is a two-section count map,
        // not a lone scalar.
        return SectionShapeOutputPolicy.ValidateScalarTerminal(
            effectiveSections,
            cardinalities,
            terminal,
            discovery: options.Discover is not null);
    }

    /// <summary>The cardinality declarations for the active catalog.</summary>
    internal static IReadOnlyDictionary<string, SectionCardinalityDeclaration>
        ApiCardinalities(
            bool singleTypeMode,
            SectionPipeline<ApiType> memberPipeline) =>
        singleTypeMode
            ? ApiMemberSectionCardinality.For(memberPipeline.AllSectionNames)
            : ApiTypeSectionCardinality.Declarations;
}
