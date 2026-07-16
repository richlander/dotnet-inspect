namespace DotnetInspector.Fixtures;

public static class AppContextSwitchFixture
{
    public static bool IsEnabled()
        => AppContext.TryGetSwitch(
            "DotnetInspector.Fixtures.AppContextOnly",
            out var enabled)
            && enabled;
}
