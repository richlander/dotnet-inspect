using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public class FindCommand
{
    public const string Name = "find";
    public static async Task<int> ExecuteAsync(FindOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;

        try
        {
            // Discovery mode: -D/--discover lists schema
            if (options.Discover != null)
            {
                var schema = options.Members
                    ? new DocumentSchema()
                        .Add("Members", "column", "Pattern", "Member", "Kind", "Type", "Signature", "Library", "Source")
                    : new DocumentSchema()
                        .Add("Results", "column", "Pattern", "Type", "Namespace", "Kind", "Library", "Source", "Match", "Sim");
                return DiscoverOutput.Execute(options.Discover, schema,
                    tree: options.Tree, json: options.JsonOutput, tsv: options.Tsv, jsonl: options.Jsonl,
                    projection: options);
            }

            var patterns = options.Pattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                CommandError.Write("No pattern specified.");
                return 1;
            }

            // Fail closed, like the type/member paths (#3386): --fields/--columns select table
            // columns, but find's --json emits the full per-result objects and has no column-
            // slicing facility, so the combination silently dropped the column filter. Reject it
            // rather than emitting an unfiltered document. --count reduces to a scalar and is
            // handled below, so it is excluded. Placed after the -D discovery branch, which honors
            // projection itself. The row-oriented formats (--tsv/--jsonl/--table) project columns.
            if (options.JsonOutput && !options.Count && IsColumnProjectionRequested(options))
                return RejectColumnProjectionUnderJson();

            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            if (options.Members)
            {
                return await ExecuteMemberSearchAsync(options, patterns, logger, context.HttpClient);
            }

            var results = await TypeSearchService.FindTypesAsync(options, patterns, logger, context.HttpClient);
            var title = patterns.Length == 1 ? $"Find: {patterns[0]}" : "Find Results";

            // --count reduces the payload, so it is resolved before the format flags that
            // render it. Ordering these the other way lets --json answer a count request
            // with the full unprojected result set.
            if (options.Count)
            {
                WriteCount(results, title);
            }
            else if (options.JsonOutput)
            {
                var writer = new FindJsonWriter();
                writer.Write(results, new WriterOptions(), Console.Out);
            }
            else
            {
                WriteOutput(results, title, options);
            }

            return 0;
        }
        catch (Exception ex)
        {
            CommandError.Write(ex);
            return 1;
        }
    }

    private static async Task<int> ExecuteMemberSearchAsync(
        FindOptions options,
        string[] patterns,
        VerboseLogger logger,
        HttpClient httpClient)
    {
        // Strip the leading '.' sentinel from each segment so ".Serialize" and "Serialize" both search
        // the member named "Serialize". ".ctor"/".cctor" are preserved (they are real member names).
        var memberPatterns = patterns
            .Select(MemberPatternSentinel.Strip)
            .Where(p => p.Length > 0)
            .ToArray();

        if (memberPatterns.Length == 0)
        {
            CommandError.Write("No member pattern specified.");
            return 1;
        }

        var results = await MemberSearchService.FindMembersAsync(options, memberPatterns, logger, httpClient);
        var title = memberPatterns.Length == 1 ? $"Find member: {memberPatterns[0]}" : "Find Members";

        if (options.Count)
        {
            WriteMemberCount(results, title);
        }
        else if (options.JsonOutput)
        {
            var writer = new MemberFindJsonWriter();
            writer.Write(results, new WriterOptions(), Console.Out);
        }
        else
        {
            WriteMemberOutput(results, title, options);
        }

        return 0;
    }

    private static bool IsColumnProjectionRequested(FindOptions options)
        => options.Fields is { Length: > 0 } || options.Columns is { Length: > 0 };

    private static int RejectColumnProjectionUnderJson()
    {
        CommandError.Write(
            "--fields/--columns select table columns and cannot be combined with --json, "
            + "which emits the full result objects. Use --tsv, --jsonl, or --table to project columns.");
        return 1;
    }

    private static void WriteOutput(List<TypeFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                options.Rows);
        }
        else
        {
            OutputFormatter.WriteLimitedMarkdown(Console.Out,
                MarkoutSerializer.Serialize(view, SearchViewContext.Default), options.Rows);
        }
    }

    private static void WriteCount(List<TypeFindResult> rawData, string title)
    {
        var view = FindOutputFormatter.BuildView(rawData, title);
        CountOutput.WriteCount(view.Results?.Count ?? 0);
    }

    private static void WriteMemberOutput(List<MemberFindResult> rawData, string title, FindOptions options)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);

        if (view.Results == null && view.Description != null)
        {
            CommandError.WriteLine(view.Description);
            return;
        }

        if (options.Tabular)
        {
            OutputFormatter.WriteProjectedTable(Console.Out, !options.NoHeader, options.Tsv, options.Jsonl,
                options.Columns, options.Fields,
                (writer, formatter, writerOptions) =>
                    MarkoutSerializer.Serialize(view, writer, formatter, SearchViewContext.Default, writerOptions),
                options.Rows);
        }
        else
        {
            OutputFormatter.WriteLimitedMarkdown(Console.Out,
                MarkoutSerializer.Serialize(view, SearchViewContext.Default), options.Rows);
        }
    }

    private static void WriteMemberCount(List<MemberFindResult> rawData, string title)
    {
        var view = FindOutputFormatter.BuildMemberView(rawData, title);
        CountOutput.WriteCount(view.Results?.Count ?? 0);
    }
}

/// <summary>
/// Represents a type found during search.
/// </summary>
public record class TypeSearchResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("library")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }
}
