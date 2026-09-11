namespace ExtensionInstanceFixture;

public sealed class Widget;

public static class WidgetExtensions
{
    public static int Measure(this Widget value, int count) => count;
}
