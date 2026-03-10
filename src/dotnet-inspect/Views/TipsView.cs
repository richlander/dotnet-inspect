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

[MarkoutContext(typeof(TipsView))]
public partial class TipsViewContext : MarkoutSerializerContext
{
}
