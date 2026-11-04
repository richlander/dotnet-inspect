using System.CommandLine;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public class OutputItemBindingTests
{
    private sealed record TypeInventory(string Library, int Total, IReadOnlyList<string> Types);

    private static readonly OutputItemBinding<TypeInventory> Types =
        OutputItemBinding<TypeInventory>.Create<string>(
            "Types",
            static model => model.Types,
            static (model, rows) => model with { Types = rows });

    [Fact]
    public void SelectionPreservesProducerContextAndOrderedItems()
    {
        var source = new TypeInventory("World.dll", 3, ["World.Blue.Foo", "World.Blue.Bar", "World.Green.Node"]);
        var intent = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Tail(2)]);

        Assert.True(Types.TrySelect(source, intent, out var selected));
        Assert.Equal(source.Library, selected.Library);
        Assert.Equal(source.Total, selected.Total);
        Assert.Equal(["World.Blue.Bar", "World.Green.Node"], selected.Types);
        Assert.Equal(3, source.Types.Count);
    }

    [Fact]
    public async Task StrictWindowFailureDoesNotRebindPartialItems()
    {
        var source = new TypeInventory("World.dll", 2, ["World.Blue.Foo", "World.Blue.Bar"]);
        var intent = RowSelectionIntent<string>.Create(
            [RowSelectionIntentOperation<string>.Window(1, 3)]);
        var (_, error) = await ConsoleCapture.RunAsync(() =>
        {
            Assert.False(Types.TrySelect(source, intent, out var selected));
            Assert.Same(source, selected);
        });
        Assert.Contains("requires item 3, but only 2 items are available", error);
    }

    [Fact]
    public void EffectiveProjectionChoosesItemsOrLinesWithoutChangingFormatIntent()
    {
        var root = new RootCommand();
        var command = new Command("inspect");
        root.Subcommands.Add(command);
        var options = new SharedOptions();
        foreach (Option option in new Option[]
        {
            options.Limit, options.Rows, options.Head, options.Tail, options.Lines,
            options.TailLines, options.Print, options.Markdown, options.Json,
        })
        {
            command.Options.Add(option);
        }
        CliOutputItemSelection.Register(
            command,
            new(options.Limit, options.Rows, null, null, options.Head, options.Tail,
                options.Lines, options.TailLines),
            static _ => Types,
            result => CliOutputItemSelection.ResolveProjection(result.GetValue(options.Print)),
            result => result.GetValue(options.Json));

        foreach (string[] projection in new[] { Array.Empty<string>(), ["--print"], ["--lines"] })
        {
            string[] arguments = ["inspect", "-n", "2", .. projection];
            var parsed = root.Parse(arguments);
            var prepared = CliRowSelectionCommandRegistry.Prepare(parsed, arguments);
            Assert.Null(prepared.Error);
            Assert.False(options.IsFormatFlagExplicitlySet(prepared.ParseResult));
            if (projection.Length == 0)
            {
                Assert.Single(prepared.Lowering!.SemanticIntent.Operations);
                Assert.Null(prepared.Lowering.LineIntent);
            }
            else
            {
                Assert.Empty(prepared.Lowering!.SemanticIntent.Operations);
                Assert.Equal(2, prepared.Lowering.LineIntent!.Count);
            }
        }
    }
}
