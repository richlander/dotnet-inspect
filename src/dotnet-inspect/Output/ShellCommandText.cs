namespace DotnetInspector.Output;

internal enum ShellCommandDialect
{
    Posix,
    PowerShell,
}

internal static class ShellCommandText
{
    internal static ShellCommandDialect CurrentDialect =>
        OperatingSystem.IsWindows()
            ? ShellCommandDialect.PowerShell
            : ShellCommandDialect.Posix;

    internal static string CurrentDialectName =>
        CurrentDialect == ShellCommandDialect.PowerShell
            ? "PowerShell"
            : "a POSIX shell";

    internal static string Quote(string value)
        => Quote(value, CurrentDialect);

    internal static string Quote(
        string value,
        ShellCommandDialect dialect)
        => dialect switch
        {
            ShellCommandDialect.Posix =>
                $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'",
            ShellCommandDialect.PowerShell =>
                $"'{value.Replace("'", "''", StringComparison.Ordinal)}'",
            _ => throw new ArgumentOutOfRangeException(nameof(dialect)),
        };
}
