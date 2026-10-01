using DotnetInspect.Cli;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Views;
using Markout;

using ILInspector.CSharp;

namespace DotnetInspect.Cli.Output;

public record Tip(string Subcommand, string Args, string Comment)
{
    public string CommandText =>
        string.IsNullOrEmpty(Args)
            ? Subcommand
            : $"{Subcommand} {Args}";
}

public static class Hints
{
    public static void WriteTips(TipLevel level, Func<Tip[]> createTips)
    {
        if (level == TipLevel.Quiet) return;

        Console.Out.Flush();

        Tip[] tips = createTips();
        if (tips.Length == 0) return;

        var visible = tips.Take(3).ToList();

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
            // This write hands the stream to a serializer, which no rule can
            // inspect, so the site itself is accounted for: it carries a
            // justified RS0030 suppression, and
            // CommandErrorOwnershipTests.CompiledIl_ReachesStderrOnlyWhereAccountedFor
            // counts it in the shipped assembly. A second sink here fails that
            // count.
            Commands = visible.Select(t => new TipRow(
                CSharpIdentifier.ContainRenderedText(t.CommandText),
                CSharpIdentifier.ContainRenderedText(t.Comment))).ToList()
        };

        CommandError.WriteBlankLine();
        #pragma warning disable RS0030 // An accounted stderr sink: every field of the view was contained above (issue #3319).
        MarkoutSerializer.Serialize(view, Console.Error, new PlainTextFormatter(), TipsViewContext.Default);
        #pragma warning restore RS0030
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
        #pragma warning disable RS0030 // An accounted stderr sink: every field of the view was contained above (issue #3319).
        MarkoutSerializer.Serialize(view, Console.Error, new PlainTextFormatter(), TipsViewContext.Default);
        #pragma warning restore RS0030
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
