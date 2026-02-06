namespace DotnetInspector.Output;

public static class Hints
{
    public static void WriteHint(string hint)
    {
        Console.Out.Flush();
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Tip: {hint}");
    }
}
