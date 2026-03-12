using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable]
public record SampleRow(string Type, string Description, string Url);
