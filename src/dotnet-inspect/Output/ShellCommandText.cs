namespace DotnetInspector.Output;

internal static class ShellCommandText
{
    internal static string Quote(string value)
        => $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
}
