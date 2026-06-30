using Markout;

namespace DotnetInspector.Views;

/// <summary>
/// Row for type-to-SourceLink URL sections on library/package/source views.
/// </summary>
[MarkoutSerializable]
public record SourceFileRow(string Type, [property: MarkoutSkipNull] string? Url);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(SourceFileRow))]
public partial class SourceViewContext : MarkoutSerializerContext
{
}
