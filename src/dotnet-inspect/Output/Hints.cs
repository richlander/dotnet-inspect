using DotnetInspector.Options;
using DotnetInspector.Views;
using Markout;

using ILInspector.CSharp;

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
            // A tip echoes type and member names that came from untrusted
            // metadata. Containing at this single choke point covers every
            // command's tips, so a new tip cannot reopen the hole (issue #3319).
            // Both fields, not just the one that carries untrusted text today.
            // Every dynamic value currently reaches CommandText and every
            // comment is a literal, but that is a fact about the seven current
            // tips rather than a property of the type, and containing a literal
            // costs nothing.
            //
            // This write hands the stream to a serializer, which the stream
            // rule cannot inspect, so the site itself is pinned by
            // CommandErrorOwnershipTests.StderrSinks_AreStillTheOnesAccountedFor:
            // adding a sink here changes that test's per-file tally and fails.
            Commands = visible.Select(t => new TipRow(
                CSharpIdentifier.ContainRenderedText(t.CommandText),
                CSharpIdentifier.ContainRenderedText(t.Comment))).ToList()
        };

        Console.Out.Flush();
        CommandError.WriteBlankLine();
        MarkoutSerializer.Serialize(view, Console.Error, new PlainTextFormatter(), TipsViewContext.Default);
    }

    public static void WriteLegend(params LegendEntry[] entries)
    {
        if (entries.Length == 0) return;

        var view = new LegendView
        {
            Entries = [.. entries.Select(e => new LegendEntry(
                CSharpIdentifier.ContainRenderedText(e.Symbol),
                CSharpIdentifier.ContainRenderedText(e.Description)))],
        };

        Console.Out.Flush();
        CommandError.WriteBlankLine();
        MarkoutSerializer.Serialize(view, Console.Error, new PlainTextFormatter(), TipsViewContext.Default);
    }

    public static void WriteDiffLegend()
    {
        WriteLegend(
            new("+", "added type"),
            new("~", "modified (non-breaking)"),
            new("x", "modified (breaking)"),
            new("-", "removed type"));
    }
}
