using DotnetInspector.Options;

namespace DotnetInspector.Commands;

/// <summary>
/// Inspects type members (docs on by default).
/// Delegates to ApiCommand for actual execution.
/// </summary>
public static class MemberCommand
{
    public const string Name = "member";

    public static Task<int> ExecuteAsync(ApiOptions options)
    {
        // MemberCommand shares the same execution logic as ApiCommand
        return ApiCommand.ExecuteAsync(options);
    }
}
