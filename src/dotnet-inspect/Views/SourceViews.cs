using ILInspector.CSharp;
using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Row for type-to-SourceLink URL sections on library/package/source views.
/// </summary>
[MarkoutSerializable]
/// <summary>
/// A SourceLink source-file row.
/// </summary>
/// <remarks>
/// The URL comes out of the inspected binary's SourceLink document, so it is
/// chosen by whoever built the assembly (issue #3319). Every positional
/// property is redeclared in order, because redeclaring only some of them would
/// reorder the rendered columns.
/// </remarks>
public record SourceFileRow(string Type, string? Url)
{
    public string Type { get; init; } = CSharpIdentifier.ContainRenderedText(Type);
    [MarkoutSkipNull]
    public string? Url { get; init; } = Url is null ? null : CSharpIdentifier.ContainRenderedText(Url);
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(SourceFileRow))]
public partial class SourceViewContext : MarkoutSerializerContext
{
}
