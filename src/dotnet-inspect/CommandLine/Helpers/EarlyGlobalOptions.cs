namespace DotnetInspector.CommandLine;

/// <summary>
/// Handles options that must be consumed before the command graph is invoked.
/// </summary>
internal static class EarlyGlobalOptions
{
    internal static bool ContainsBeforeEndOfOptions(string[] arguments, string option) =>
        IndexOfBeforeEndOfOptions(arguments, option) >= 0;

    internal static int IndexOfBeforeEndOfOptions(string[] arguments, string option)
    {
        var boundary = GetEndOfOptionsIndex(arguments);
        return Array.IndexOf(arguments, option, 0, boundary);
    }

    internal static string[] RemoveAllBeforeEndOfOptions(string[] arguments, string option)
    {
        var boundary = GetEndOfOptionsIndex(arguments);
        return
        [
            .. arguments[..boundary].Where(argument => argument != option),
            .. arguments[boundary..],
        ];
    }

    internal static string[] InsertBeforeEndOfOptions(string[] arguments, params string[] values)
    {
        var boundary = GetEndOfOptionsIndex(arguments);
        return [.. arguments[..boundary], .. values, .. arguments[boundary..]];
    }

    internal static int GetEndOfOptionsIndex(string[] arguments)
    {
        var index = Array.IndexOf(arguments, "--");
        return index < 0 ? arguments.Length : index;
    }
}
