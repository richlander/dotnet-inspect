namespace DotnetInspector.Sections;

internal static class SectionContractSnapshot
{
    public static IReadOnlyList<T> Empty<T>() =>
        Array.AsReadOnly(Array.Empty<T>());

    public static IReadOnlyList<T> Copy<T>(
        IReadOnlyList<T> values)
    {
        var copy = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
            copy[index] = values[index];
        return Own(copy);
    }

    public static IReadOnlyList<T> Own<T>(T[] values) =>
        Array.AsReadOnly(values);
}
