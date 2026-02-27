using System.CommandLine;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Pre-canned demo invocations that showcase the tool's capabilities.
/// </summary>
public class DemoCommand
{
    /// <summary>
    /// A single demo entry with a description and the args to invoke.
    /// </summary>
    public record DemoEntry(string Title, string Category, string[] Args)
    {
        public string CommandLine => string.Join(" ", Args.Select(a => a.Contains(' ') || a.Contains('*') || a.Contains('<') ? $"\"{a}\"" : a));
    }

    /// <summary>
    /// The curated set of demos — one dozen picks.
    /// </summary>
    public static readonly DemoEntry[] Demos =
    [
        // api (5)
        new("What does the generic math interface hierarchy look like?", "insight",
            ["api", "System.Runtime", "INumber<TSelf>", "--shape"]),

        new("How many interfaces does a 128-bit integer implement?", "insight",
            ["api", "System.Runtime", "Int128", "--shape"]),

        new("How does the runtime build tuples of any length?", "insight",
            ["api", "System.Runtime", "ValueTuple", "--shape"]),

        new("What can JsonSerializer do?", "discovery",
            ["api", "System.Text.Json", "JsonSerializer"]),

        new("What does the options pattern look like under the hood?", "insight",
            ["api", "--package", "Microsoft.Extensions.Options", "OptionsFactory", "Create"]),

        // depends (1)
        new("What does IFloatingPointIeee754 depend on?", "insight",
            ["depends", "IFloatingPointIeee754<TSelf>"]),

        // diff (2)
        new("What broke between System.CommandLine beta and stable?", "migration",
            ["diff", "System.CommandLine@2.0.0-beta4.22272.1..2.0.3", "-v:q"]),

        new("How has System.Text.Json evolved across two major versions?", "migration",
            ["diff", "System.Text.Json@8.0.0..10.0.3", "--oneline"]),

        // extensions (1)
        new("What can I register with dependency injection?", "discovery",
            ["extensions", "IServiceCollection"]),

        // find (3)
        new("Where are the AI chat types in .NET?", "discovery",
            ["find", "Chat*"]),

        new("What are the entry points for OpenAI, Azure, AWS, and Anthropic SDKs?", "discovery",
            ["find", "Chat*,Converse*,Message*", "--package", "OpenAI", "--package", "Azure.AI.OpenAI", "--package", "AWSSDK.BedrockRuntime", "--package", "Anthropic"]),

        new("What chat types exist across all Azure AI packages?", "discovery",
            ["find", "Chat*", "--package-prefix", "Azure.AI"]),

        // implements (1)
        new("What types extend Stream?", "discovery",
            ["implements", "Stream"]),

        // library (1)
        new("What does the OpenAI integration library depend on?", "insight",
            ["library", "Microsoft.Extensions.AI.OpenAI", "--dependencies"]),

        // package (2)
        new("Is my version of System.Text.Json vulnerable?", "security",
            ["package", "System.Text.Json@8.0.0", "-s", "Vulnerabilities"]),

        new("What Azure AI packages are available on NuGet?", "discovery",
            ["package", "search", "Azure.AI"]),

        // select / section (3)
        new("What fields and columns can I select for a package?", "discovery",
            ["package", "System.Text.Json", "-S"]),

        new("What types are in System.Text.Json — just the names?", "discovery",
            ["type", "--package", "System.Text.Json", "-S", "Type"]),

        new("What changed in System.Text.Json — types and changes only?", "migration",
            ["diff", "System.Text.Json@8.0.0..10.0.3", "--oneline", "-S", "Type,Change"]),
    ];

    public static async Task<int> ExecuteListAsync()
    {
        var writer = new MarkdownFormatter(Console.Out);
        writer.WriteHeading(1, "Demo Queries");

        for (int i = 0; i < Demos.Length; i++)
        {
            var demo = Demos[i];
            var label = $"{i + 1}. {char.ToUpper(demo.Category[0])}{demo.Category[1..]}";
            writer.WriteField(label, demo.Title);
        }

        Console.WriteLine();
        Console.Error.WriteLine("Tips:");
        Console.Error.WriteLine("demo <index>            # run a specific demo");
        Console.Error.WriteLine("demo --feeling-lucky    # pick one at random");

        return await Task.FromResult(0);
    }

    public static async Task<int> ExecuteInvokeAsync(int index, RootCommand rootCommand)
    {
        if (index < 1 || index > Demos.Length)
        {
            Console.Error.WriteLine($"Error: Index must be between 1 and {Demos.Length}. Use 'demo list' to see available demos.");
            return 1;
        }

        var demo = Demos[index - 1];
        return await RunDemoAsync(demo, rootCommand);
    }

    public static async Task<int> ExecuteFeelingLuckyAsync(RootCommand rootCommand)
    {
        var index = Random.Shared.Next(Demos.Length);
        var demo = Demos[index];
        return await RunDemoAsync(demo, rootCommand);
    }

    private static async Task<int> RunDemoAsync(DemoEntry demo, RootCommand rootCommand)
    {
        // Print the invocation so users can repeat/modify it
        Console.Error.WriteLine($"$ dotnet-inspect {demo.CommandLine}");
        Console.Error.WriteLine();

        // Invoke the root command with the demo's args
        return await rootCommand.Parse(demo.Args).InvokeAsync();
    }
}
