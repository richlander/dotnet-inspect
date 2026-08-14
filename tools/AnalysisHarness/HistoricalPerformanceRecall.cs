using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspector.Core;
using DotnetInspector.Packages;

namespace ILInspector.AnalysisHarness;

public sealed record HistoricalPerformanceManifest(
    int MethodologyVersion,
    IReadOnlyList<HistoricalPerformanceReference> References);

public sealed record HistoricalPerformanceReference(
    string Id,
    int PullRequest,
    string Author,
    string Classification,
    string CurrentStatus,
    string BeforeCommit,
    string AfterCommit,
    string Rationale,
    HistoricalPerformanceCell? Before = null,
    HistoricalPerformanceCell? After = null);

public sealed record HistoricalPerformanceCell(
    string Package,
    string Version,
    string AssemblyPath,
    string Type,
    string MethodContains,
    string Shape,
    int ExpectedCount);

public static class HistoricalPerformanceRecall
{
    static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<int> RunAsync(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine(
                $"Historical performance reference not found: {manifestPath}");
            return 2;
        }

        HistoricalPerformanceManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<HistoricalPerformanceManifest>(
                await File.ReadAllTextAsync(manifestPath),
                s_json);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(
                $"Invalid historical performance reference: {ex.Message}");
            return 2;
        }

        if (manifest is null
            || manifest.MethodologyVersion != 1
            || manifest.References.Count == 0)
        {
            Console.Error.WriteLine(
                "Historical performance reference must use methodologyVersion 1 and contain references.");
            return 2;
        }

        bool failed = false;
        int executable = 0;
        CoreCache.Initialize("dotnet-inspect");
        using var client = new HttpClient();
        foreach (var reference in manifest.References)
        {
            if (string.IsNullOrWhiteSpace(reference.Rationale)
                || !IsFullCommit(reference.BeforeCommit)
                || !IsFullCommit(reference.AfterCommit))
            {
                Console.Error.WriteLine(
                    $"{reference.Id}: provenance must contain a rationale and full 40-character commit IDs.");
                failed = true;
                continue;
            }

            if ((reference.Before is null) != (reference.After is null))
            {
                Console.Error.WriteLine(
                    $"{reference.Id}: before and after cells must be supplied together.");
                failed = true;
                continue;
            }

            bool executableStatus = reference.CurrentStatus == "found";
            if (executableStatus != (reference.Before is not null))
            {
                Console.Error.WriteLine(
                    $"{reference.Id}: recovered status and executable cells disagree.");
                failed = true;
                continue;
            }

            if (reference.Before is null)
            {
                Console.WriteLine(
                    $"{reference.Id} PR #{reference.PullRequest}: {reference.CurrentStatus} ({reference.Classification})");
                continue;
            }

            executable++;
            HistoricalPerformanceCell beforeCell = reference.Before;
            HistoricalPerformanceCell afterCell = reference.After!;
            int? before = await CountAsync(client, beforeCell);
            int? after = await CountAsync(client, afterCell);
            bool passed = before == beforeCell.ExpectedCount
                && after == afterCell.ExpectedCount;
            Console.WriteLine(
                $"{reference.Id} PR #{reference.PullRequest}: "
                + $"before {before?.ToString() ?? "error"}/{beforeCell.ExpectedCount}, "
                + $"after {after?.ToString() ?? "error"}/{afterCell.ExpectedCount}"
                + (passed ? "" : " REGRESSION"));
            failed |= !passed;
        }

        if (executable == 0)
        {
            Console.Error.WriteLine(
                "Historical performance reference contains no executable before/after cells.");
            return 2;
        }

        Console.WriteLine(
            $"HISTORICAL PERFORMANCE RECALL: {executable} executable, "
            + $"{manifest.References.Count - executable} classified-only"
            + (failed ? " REGRESSION" : ""));
        return failed ? 1 : 0;
    }

    static bool IsFullCommit(string commit)
        => commit.Length == 40
            && commit.All(Uri.IsHexDigit);

    static async Task<int?> CountAsync(
        HttpClient client,
        HistoricalPerformanceCell cell)
    {
        PackageExtractionOutcome outcome =
            await PackageExtractor.ExtractPackageAsync(
                client,
                cell.Package,
                version: cell.Version);
        if (!outcome.IsSuccess)
        {
            Console.Error.WriteLine(
                $"{cell.Package}@{cell.Version}: {outcome.ErrorMessage}");
            return null;
        }

        string assembly = Path.Combine(
            outcome.Result!.ExtractPath,
            cell.AssemblyPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(assembly))
        {
            Console.Error.WriteLine(
                $"{cell.Package}@{cell.Version}: assembly not found at {cell.AssemblyPath}");
            return null;
        }

        try
        {
            return PrecisionRecall.Candidates(assembly).Count(candidate =>
                candidate.Type == cell.Type
                && candidate.Method.Contains(
                    cell.MethodContains,
                    StringComparison.Ordinal)
                && candidate.Shape == cell.Shape);
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or IOException
            or InvalidOperationException)
        {
            Console.Error.WriteLine(
                $"{cell.Package}@{cell.Version}: analysis failed: {ex.Message}");
            return null;
        }
    }
}
