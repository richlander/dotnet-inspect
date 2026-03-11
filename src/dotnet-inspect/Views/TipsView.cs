using Markout;

namespace DotnetInspector.Views;

[MarkoutSerializable]
public class TipsView
{
    [MarkoutIgnore]
    public List<TipRow> Commands { get; set; } = [];

    [MarkoutPropertyName("Tips")]
    [MarkoutIgnoreInTable]
    public List<string> FormattedCommands
    {
        get
        {
            if (Commands.Count == 0) return [];
            const int MinCommandColumn = 19;
            int padWidth = Math.Max(Commands.Max(t => t.Command.Length), MinCommandColumn);
            return Commands.Select(t =>
                $"{t.Command.PadRight(padWidth)}  # {t.Description}").ToList();
        }
    }
}

public record TipRow(string Command, string Description);

[MarkoutSerializable]
public class LegendView
{
    [MarkoutIgnore]
    public List<LegendEntry> Entries { get; set; } = [];

    [MarkoutPropertyName("Legend")]
    [MarkoutIgnoreInTable]
    public List<string> FormattedEntries
    {
        get
        {
            if (Entries.Count == 0) return [];
            int padWidth = Entries.Max(e => e.Symbol.Length);
            return Entries.Select(e =>
                $"{e.Symbol.PadRight(padWidth)}  {e.Description}").ToList();
        }
    }
}

public record LegendEntry(string Symbol, string Description);

[MarkoutContext(typeof(TipsView))]
[MarkoutContext(typeof(LegendView))]
public partial class TipsViewContext : MarkoutSerializerContext
{
}
