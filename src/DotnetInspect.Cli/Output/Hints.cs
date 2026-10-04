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
    public static void WriteTips(
        CompanionOutput companionOutput,
        Func<Tip[]> createTips)
    {
        if (companionOutput == CompanionOutput.None) return;

        Console.Out.Flush();

        TipsView? view = CreateTipsView(createTips());
        if (view is null) return;

        CommandError.WriteBlankLine();
        #pragma warning disable RS0030 // An accounted stderr sink: every field of the view was contained above (issue #3319).
        MarkoutSerializer.Serialize(view, Console.Error, new PlainTextFormatter(), TipsViewContext.Default);
        #pragma warning restore RS0030
    }

    public static int WritePrimaryTips(Func<Tip[]> createTips)
    {
        TipsView? view = CreateTipsView(createTips());
        if (view is null)
        {
            CommandError.Write(
                "No contextual tips are available for this request.");
            return 1;
        }

        MarkoutSerializer.Serialize(
            view,
            Console.Out,
            new PlainTextFormatter(),
            TipsViewContext.Default);
        return 0;
    }

    private static TipsView? CreateTipsView(Tip[] tips)
    {
        if (tips.Length == 0) return null;

        return new()
        {
            // A tip echoes type and member names that came from untrusted
            // metadata. Containing at this single choke point covers every
            // command's tips, so a new tip cannot reopen the hole (issue #3319).
            Commands = tips.Take(3).Select(t => new TipRow(
                CSharpIdentifier.ContainRenderedText(t.CommandText),
                CSharpIdentifier.ContainRenderedText(t.Comment))).ToList()
        };
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
