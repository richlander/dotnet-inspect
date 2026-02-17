using DotnetInspector.Options;

namespace DotnetInspector.Commands;

/// <summary>
/// Discovers types in a package or library (terse, no docs by default).
/// Delegates to ApiCommand for actual execution.
/// </summary>
public static class TypeCommand
{
    public const string Name = "type";

    public static Task<int> ExecuteAsync(ApiOptions options)
    {
        // TypeCommand shares the same execution logic as ApiCommand
        return ApiCommand.ExecuteAsync(options);
    }
}
