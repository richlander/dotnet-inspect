using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable]
public record SampleRow(string Type, string Description, string Url);
