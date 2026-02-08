using DotnetInspector.Options;

namespace DotnetInspector.Output;

public record Tip(string Subcommand, string Args, string Comment)
{
    public override string ToString() =>
        string.IsNullOrEmpty(Args)
            ? $"{VersionInfo.ToolName} {Subcommand}   # {Comment}"
            : $"{VersionInfo.ToolName} {Subcommand} {Args}   # {Comment}";
}

public static class Hints
{
    public static void WriteTips(TipLevel level, params Tip[] tips)
    {
        if (level == TipLevel.Quiet || tips.Length == 0) return;
        int max = level == TipLevel.Minimal ? 3 : 6;
        Console.Out.Flush();
        Console.Error.WriteLine();
        foreach (var tip in tips.Take(max))
            Console.Error.WriteLine($"Tip: {tip}");
    }
}
