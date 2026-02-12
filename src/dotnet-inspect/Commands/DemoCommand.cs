using System.CommandLine;
using DotnetInspector.Output;

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
        // api (4)
        new("Shape: INumber<TSelf> — generic math interface", "api",
            ["api", "System.Runtime", "INumber<TSelf>", "--shape"]),

        new("Shape: Int128 — generic math concrete type", "api",
            ["api", "System.Runtime", "Int128", "--shape"]),

        new("API: JsonSerializer members", "api",
            ["api", "System.Text.Json", "JsonSerializer"]),

        new("Code: OptionsFactory.Create — source, lowered C#, and IL", "api",
            ["api", "--package", "Microsoft.Extensions.Options", "OptionsFactory", "Create"]),

        // depends (1)
        new("Depends: IFloatingPointIeee754 interface hierarchy", "depends",
            ["depends", "IFloatingPointIeee754<TSelf>"]),

        // diff (1)
        new("Diff: System.CommandLine breaking changes (beta→stable)", "diff",
            ["diff", "System.CommandLine@2.0.0-beta4.22272.1..2.0.3", "-v:q"]),

        // extensions (1)
        new("Extensions for IServiceCollection", "extensions",
            ["extensions", "IServiceCollection"]),

        // find (3)
        new("Find: Chat* types", "find",
            ["find", "Chat*"]),

        new("Find: Chat*/Converse*/Message* across OpenAI, Azure, AWS, Anthropic", "find",
            ["find", "Chat*,Converse*,Message*", "--package", "OpenAI", "--package", "Azure.AI.OpenAI", "--package", "AWSSDK.BedrockRuntime", "--package", "Anthropic"]),

        new("Find: Chat* across Azure AI packages (prefix search)", "find",
            ["find", "Chat*", "--package-prefix", "Azure.AI"]),

        // implements (1)
        new("Implements Stream", "implements",
            ["implements", "Stream"]),

        // library (1)
        new("Library: Microsoft.Extensions.AI.OpenAI dependency tree", "library",
            ["library", "Microsoft.Extensions.AI.OpenAI", "--dependencies"]),

        // package (2)
        new("Package: System.Text.Json@8.0.0 vulnerabilities", "package",
            ["package", "System.Text.Json@8.0.0", "-s", "Vulnerabilities"]),

        new("Package search: Azure AI ecosystem", "search",
            ["package", "search", "Azure.AI"]),
    ];

    public static async Task<int> ExecuteListAsync()
    {
        Console.WriteLine("# Demo Queries");
        Console.WriteLine();

        for (int i = 0; i < Demos.Length; i++)
        {
            var demo = Demos[i];
            Console.WriteLine($"  {i + 1,2}. [{demo.Category}] {demo.Title}");
            Console.WriteLine($"      dotnet-inspect {demo.CommandLine}");
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
