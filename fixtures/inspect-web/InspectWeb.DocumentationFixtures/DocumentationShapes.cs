namespace InspectWeb.DocumentationFixtures;

/// <summary>A receiver for projected extension methods.</summary>
public sealed class Widget;

/// <summary>Methods that extend documentation fixture types.</summary>
public static class WidgetExtensions
{
    /// <summary>Measures a widget through its declaring extension member.</summary>
    /// <param name="value">The widget to measure.</param>
    /// <param name="count">The measurement count.</param>
    /// <returns>The supplied measurement count.</returns>
    public static int Measure(this Widget value, int count) => count;
}

/// <summary>A non-public type retained by the browser accessibility surface.</summary>
internal sealed class HiddenDocumentedType
{
    /// <summary>Reads documentation from a non-public type.</summary>
    /// <returns>The fixture value.</returns>
    public int Read() => 42;
}
