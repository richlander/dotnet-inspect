using DotnetInspector.Options;
using DotnetInspector.Views;
using Markout;

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
        WriteTips(level, tips, randomize: false);
    }

    public static void WriteTips(TipLevel level, Tip[] tips, bool randomize)
    {
        if (level == TipLevel.Quiet || tips.Length == 0) return;
        int max = level == TipLevel.Minimal ? 3 : 6;

        var visible = randomize
            ? tips.OrderBy(_ => Random.Shared.Next()).Take(max).ToList()
            : tips.Take(max).ToList();

        var view = new TipsView
        {
            Commands = visible.Select(t => new TipRow(t.CommandText, t.Comment)).ToList()
        };

        Console.Out.Flush();
        Console.Error.WriteLine();
        MarkoutSerializer.Serialize(view, Console.Error, TipsViewContext.Default);
    }
}
