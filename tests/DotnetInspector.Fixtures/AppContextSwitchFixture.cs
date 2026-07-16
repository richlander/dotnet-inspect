namespace DotnetInspector.Fixtures;

public static class AppContextSwitchFixture
{
    public static bool IsEnabled()
        => AppContext.TryGetSwitch(
            "DotnetInspector.Fixtures.AppContextOnly",
            out var enabled)
            && enabled;

    public static bool HasLiteralEscape()
        => AppContext.TryGetSwitch(
            @"DotnetInspector.Fixtures.Literal\nSwitch",
            out var enabled)
            && enabled;

    public static bool IsDuplicateEnabled()
    {
        AppContext.TryGetSwitch("DotnetInspector.Fixtures.Duplicate", out var first);
        AppContext.TryGetSwitch("DotnetInspector.Fixtures.Duplicate", out var second);
        return first || second;
    }

    public static bool CallsLookalike()
        => AppContextSwitchLookalike.TryGetSwitch(
            "DotnetInspector.Fixtures.Lookalike",
            out var enabled)
            && enabled;
}

static class AppContextSwitchLookalike
{
    public static bool TryGetSwitch(string switchName, out bool enabled)
    {
        enabled = switchName.Length > 0;
        return true;
    }
}
