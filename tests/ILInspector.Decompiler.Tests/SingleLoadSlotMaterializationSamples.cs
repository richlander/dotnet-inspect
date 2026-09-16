namespace ILInspector.Decompiler.Tests;

public sealed class SingleLoadStorageSource
{
    public string? Display;
    public string? FullPath;
    public Func<string> Fallback = null!;
}

public static class SingleLoadSlotMaterializationSamples
{
    public static string ReadDisplay(SingleLoadStorageSource source)
        => source.Display ?? source.FullPath ?? source.Fallback();

    public static string CacheDisplay(SingleLoadStorageSource source)
        => source.Display ??= source.Fallback();

    public static int ConditionalConsumer(bool choose, Func<int> first, Func<int> second)
        => Math.Abs(choose ? first() : second());

    public static string CoalesceConsumer(string? value, Func<string> fallback)
        => (value ?? fallback()).Trim();

    public static bool ShortCircuitConsumer(Func<bool> first, Func<bool> second)
        => first() && second();
}
