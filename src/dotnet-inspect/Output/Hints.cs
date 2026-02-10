using DotnetInspector.Options;

namespace DotnetInspector.Output;

public record Tip(string Subcommand, string Args, string Comment)
{
    public string CommandText =>
        string.IsNullOrEmpty(Args)
            ? Subcommand
            : $"{Subcommand} {Args}";
}

public static class Hints
{
    public static void WriteTips(TipLevel level, params Tip[] tips)
    {
        if (level == TipLevel.Quiet || tips.Length == 0) return;
        int max = level == TipLevel.Minimal ? 3 : 6;
        var visible = tips.Take(max).ToList();
        int commentColumn = visible.Max(t => t.CommandText.Length) + 3;
        Console.Out.Flush();
        Console.Error.WriteLine();
        Console.Error.WriteLine("Tips:");
        foreach (var tip in visible)
            Console.Error.WriteLine($"{tip.CommandText.PadRight(commentColumn)}# {tip.Comment}");
    }
}
