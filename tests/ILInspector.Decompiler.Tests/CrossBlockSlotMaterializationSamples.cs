namespace ILInspector.Decompiler.Tests;

public static class CrossBlockSlotMaterializationSamples
{
    public static string AccessorName(string name, bool getter, bool alternateSetter)
        => (getter ? "get_" : alternateSetter ? "put_" : "set_") + name;

    public static int SelectedNumber(int value, bool first, bool second)
        => (first ? 10 : second ? 20 : 30) + value;

    public static void ValidateName(string? value, string? name)
    {
        if (value is null)
            throw new ArgumentNullException(name ?? "parameter");
        if (value.Length == 0)
            throw new ArgumentException((name ?? "parameter") + " is empty.");
    }

    public static bool RepeatedConditional(bool first, bool second, Func<bool> observe)
    {
        bool result = first && observe();
        if (result)
            result = second && observe();
        return result;
    }
}
