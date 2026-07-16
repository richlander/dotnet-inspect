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

    internal static bool? GetBooleanValueBeforeEndOfOptions(string[] arguments, string option)
    {
        bool? value = null;
        var boundary = GetEndOfOptionsIndex(arguments);
        for (var index = 0; index < boundary; index++)
        {
            if (arguments[index] == option)
            {
                value = true;
                continue;
            }

            var prefix = $"{option}=";
            if (arguments[index].StartsWith(prefix, StringComparison.Ordinal)
                && bool.TryParse(arguments[index][prefix.Length..], out var inlineValue))
            {
                value = inlineValue;
            }
        }

        return value;
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

    internal static string[] RemoveBooleanBeforeEndOfOptions(string[] arguments, string option)
    {
        var boundary = GetEndOfOptionsIndex(arguments);
        return
        [
            .. arguments[..boundary].Where(argument => !IsBooleanOption(argument, option)),
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

    private static bool IsBooleanOption(string argument, string option)
    {
        if (argument == option)
            return true;

        var prefix = $"{option}=";
        return argument.StartsWith(prefix, StringComparison.Ordinal)
            && bool.TryParse(argument[prefix.Length..], out _);
    }
}
