using System.Globalization;
using DotnetInspector.Core;

namespace DotnetInspector;

internal static class HttpTimeoutConfiguration
{
    internal const string Flag = "--http-timeout";
    internal const string EnvironmentVariable = "DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS";

    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(1);

    internal static (string[] RemainingArgs, string? ExplicitValue, bool HasDuplicate) Extract(string[] args)
    {
        var remaining = new List<string>(args.Length);
        string? explicitValue = null;
        bool found = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--")
            {
                for (; i < args.Length; i++)
                    remaining.Add(args[i]);
                break;
            }

            if (arg == Flag)
            {
                if (found)
                    return (args, null, true);

                found = true;
                explicitValue = string.Empty;
                if (i + 1 < args.Length && args[i + 1] != "--" && !IsTimeoutOption(args[i + 1]))
                    explicitValue = args[++i];
                continue;
            }

            if (TryGetInlineValue(arg, out string? inlineValue))
            {
                if (found)
                    return (args, null, true);

                found = true;
                explicitValue = inlineValue;
                continue;
            }

            remaining.Add(arg);
        }

        return ([.. remaining], explicitValue, false);
    }

    internal static TimeSpan ResolveEnvironmentDefault(string? value) =>
        TryParseSeconds(value, out TimeSpan timeout)
            ? timeout
            : HttpClientFactoryOptions.BuiltInDefaultTimeout;

    internal static bool TryParseSeconds(string? value, out TimeSpan timeout)
    {
        timeout = default;
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
            return false;

        var requested = TimeSpan.FromSeconds(seconds);
        if (requested < TimeSpan.FromSeconds(1) || requested > MaximumTimeout)
            return false;

        timeout = requested;
        return true;
    }

    private static bool IsTimeoutOption(string arg) =>
        arg == Flag || TryGetInlineValue(arg, out _);

    private static bool TryGetInlineValue(string arg, out string? value)
    {
        int prefixLength = Flag.Length + 1;
        if (arg.Length >= prefixLength
            && arg.StartsWith(Flag, StringComparison.Ordinal)
            && arg[Flag.Length] is '=' or ':')
        {
            value = arg[prefixLength..];
            return true;
        }

        value = null;
        return false;
    }
}
