using System.Text.Json;

using DotnetInspector.Core;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Drift verification for the vendored authored-source correspondence corpus.
/// Every corpus row snapshots a checksum-verified authored member body captured
/// at harvest time. This mode re-acquires each row's source <em>today</em> — from
/// a local git clone (<c>--repo</c>, checksum-arbitrated) and/or SourceLink — and
/// reduces it to the same member-body slice the harvester used
/// (<see cref="AuthoredRebuildFidelity.TryExtractTargetBody"/>), then compares it
/// against the stored body. It answers "does the vendored snapshot still
/// correspond to the authoritative source at its pinned commit?" without running
/// the decompiler: a byte-for-byte mismatch is corpus drift; an acquisition or
/// slice miss is an unresolved row.
///
/// This is a standalone integrity check, not a benchmark gate: it does not run
/// the decompiler and never affects <see cref="AuthoredCorpusBenchmark"/>. It is
/// report-only by default — a run's own integrity (corpus present, every corpus
/// assembly supplied, at least one row evaluated) still governs the exit code, so
/// a partial or empty run never masquerades as success. Pass <c>--fail-on-drift</c>
/// to additionally fail the run when any row has drifted, for a periodic
/// (non-PR) gate.
/// </summary>
static class AuthoredCorpusDrift
{
    enum Outcome
    {
        // Re-acquired body matches the stored snapshot.
        Verified,
        // Re-acquired body differs from the stored snapshot (source rot).
        Drifted,
        // Source could not be re-acquired or sliced (offline, no --repo, commit
        // gone, checksum mismatch, or an extraction regression) — unverifiable.
        Unavailable,
    }

    sealed record RowResult(
        AuthoredSourceHarvest.CorpusRecord Record,
        Outcome Outcome,
        string? Detail);

    public static int Run(
        IReadOnlyList<string> assemblies,
        string corpusPath,
        bool json,
        bool failOnDrift,
        IReadOnlyList<string>? repositoryPaths)
        => RunAsync(assemblies, corpusPath, json, failOnDrift, repositoryPaths).GetAwaiter().GetResult();

    static async Task<int> RunAsync(
        IReadOnlyList<string> assemblies,
        string corpusPath,
        bool json,
        bool failOnDrift,
        IReadOnlyList<string>? repositoryPaths)
    {
        if (!File.Exists(corpusPath))
        {
            Console.Error.WriteLine($"Corpus file not found: {corpusPath}");
            return 1;
        }

        var records = ReadCorpus(corpusPath);
        if (records.Count == 0)
        {
            Console.Error.WriteLine($"Corpus is empty or unparseable: {corpusPath}");
            return 1;
        }

        var byAssembly = records
            .GroupBy(record => record.Assembly, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AuthoredSourceHarvest.CorpusRecord>)group.ToArray(),
                StringComparer.Ordinal);

        HttpClientFactory.Initialize();
        using var httpClient = HttpClientFactory.CreateNew();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);

        var results = new List<RowResult>();
        var matchedGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assemblyPath in assemblies)
        {
            if (!File.Exists(assemblyPath))
                continue;

            string name = AuthoredSourceHarvest.ReadAssemblyIdentity(assemblyPath).Name;
            if (!byAssembly.TryGetValue(name, out var group) || !matchedGroups.Add(name))
                continue;

            SourceLinkService? source = null;
            try
            {
                source = SourceLinkService.Open(assemblyPath);
                await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);
                foreach (var record in group)
                    results.Add(await EvaluateRowAsync(source, record, fetcher, repositoryPaths));
            }
            catch (Exception ex) when (ex is IOException
                or InvalidOperationException
                or BadImageFormatException
                or HttpRequestException
                or TaskCanceledException)
            {
                // The assembly's SourceLink PDB could not be opened or acquired, so
                // no row for it can be verified. Surface every row as Unavailable
                // (never silently drop it) rather than pretending it was checked.
                Console.Error.WriteLine(
                    $"Warning: drift check could not open '{assemblyPath}' ({ex.GetType().Name}: {ex.Message}).");
                foreach (var record in group)
                    results.Add(new RowResult(record, Outcome.Unavailable, $"assembly-open: {ex.GetType().Name}"));
            }
            finally
            {
                source?.Dispose();
            }
        }

        int unmatchedRows = byAssembly
            .Where(entry => !matchedGroups.Contains(entry.Key))
            .Sum(entry => entry.Value.Count);

        return json
            ? WriteJson(results, records.Count, matchedGroups.Count, byAssembly.Count, unmatchedRows, failOnDrift)
            : WriteCard(results, records.Count, matchedGroups.Count, byAssembly.Count, unmatchedRows, failOnDrift);
    }

    static async Task<RowResult> EvaluateRowAsync(
        SourceLinkService source,
        AuthoredSourceHarvest.CorpusRecord record,
        SourceFetcher fetcher,
        IReadOnlyList<string>? repositoryPaths)
    {
        var subject = new FindingSubject(
            $"{record.Type}::{record.Method}#{record.Overload}",
            $"{record.Type}.{record.Method}");

        AuthoredMemberSourceInspection authored;
        try
        {
            authored = await AuthoredSourceAcquisition.AcquireMemberAsync(
                source,
                record.MetadataToken,
                record.Method,
                subject,
                fetcher,
                repositoryPaths);
        }
        catch (Exception ex) when (ex is IOException
            or InvalidOperationException
            or HttpRequestException
            or TaskCanceledException)
        {
            return new RowResult(record, Outcome.Unavailable, $"acquire: {ex.GetType().Name}");
        }

        if (authored.Text is not { } memberSource || memberSource.Length == 0)
            return new RowResult(record, Outcome.Unavailable, "acquire: no source text");

        // Reduce the PDB line-span slice to the same disambiguated member body the
        // harvester stored, so the comparison is like-for-like.
        if (!AuthoredRebuildFidelity.TryExtractTargetBody(
                memberSource,
                record.Method,
                record.ParameterCount,
                out string body)
            || body.Length == 0)
        {
            return new RowResult(record, Outcome.Unavailable, "extract: body slice failed");
        }

        // Compare on normalized newlines: the platform that re-acquires the blob
        // must not register as source drift (git cat-file and raw HTTP both return
        // the committed bytes, but the stored corpus body may have been harvested
        // with a different newline convention).
        return NormalizeNewlines(body).Equals(NormalizeNewlines(record.AuthoredBody), StringComparison.Ordinal)
            ? new RowResult(record, Outcome.Verified, authored.Document?.ResolvedUrl)
            : new RowResult(record, Outcome.Drifted, DescribeDrift(record.AuthoredBody, body));
    }

    static string NormalizeNewlines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    static string DescribeDrift(string stored, string acquired)
    {
        string[] storedLines = NormalizeNewlines(stored).Split('\n');
        string[] acquiredLines = NormalizeNewlines(acquired).Split('\n');
        int firstDiff = 0;
        int shared = Math.Min(storedLines.Length, acquiredLines.Length);
        while (firstDiff < shared
            && string.Equals(storedLines[firstDiff], acquiredLines[firstDiff], StringComparison.Ordinal))
        {
            firstDiff++;
        }

        return $"stored {storedLines.Length} line(s), acquired {acquiredLines.Length} line(s); "
            + $"first diff at line {firstDiff + 1}";
    }

    static List<AuthoredSourceHarvest.CorpusRecord> ReadCorpus(string corpusPath)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var records = new List<AuthoredSourceHarvest.CorpusRecord>();
        foreach (var line in File.ReadLines(corpusPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                if (JsonSerializer.Deserialize<AuthoredSourceHarvest.CorpusRecord>(line, options) is { } record)
                    records.Add(record);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Skipping malformed corpus row: {ex.Message}");
            }
        }

        return records;
    }

    static int WriteCard(
        IReadOnlyList<RowResult> results,
        int corpusRows,
        int matchedAssemblies,
        int corpusAssemblies,
        int unmatchedRows,
        bool failOnDrift)
    {
        int verified = results.Count(result => result.Outcome == Outcome.Verified);
        int drifted = results.Count(result => result.Outcome == Outcome.Drifted);
        int unavailable = results.Count(result => result.Outcome == Outcome.Unavailable);
        int evaluated = results.Count;

        Console.WriteLine("AUTHORED-SOURCE CORPUS DRIFT");
        Console.WriteLine();
        Console.WriteLine($"  corpus rows        : {corpusRows}");
        Console.WriteLine($"  assemblies matched : {matchedAssemblies} / {corpusAssemblies}");
        if (unmatchedRows > 0)
            Console.WriteLine($"  rows without asm   : {unmatchedRows} (BLOCKER: no local assembly supplied)");
        Console.WriteLine($"  rows evaluated     : {evaluated}");
        if (evaluated == 0)
            Console.WriteLine("  (BLOCKER: no rows evaluated — nothing was checked)");
        Console.WriteLine();
        Console.WriteLine($"  Verified   (matches vendored snapshot) : {verified}");
        Console.WriteLine($"  Drifted    (source changed)            : {drifted}");
        Console.WriteLine($"  Unavailable(could not re-acquire)      : {unavailable}");

        WriteRows("Drifted rows", results, Outcome.Drifted);
        WriteRows("Unavailable rows", results, Outcome.Unavailable);

        // Honest-exit contract: a run only counts if every corpus assembly was
        // supplied (unmatchedRows == 0) and at least one row was evaluated. On top
        // of that, --fail-on-drift fails when any row drifted. Unavailable rows are
        // surfaced but non-fatal: running offline without --repo legitimately
        // cannot re-acquire, and that is a "could not verify", not a drift.
        bool honest = unmatchedRows == 0 && evaluated > 0;
        return honest && !(failOnDrift && drifted > 0) ? 0 : 1;
    }

    static void WriteRows(string title, IReadOnlyList<RowResult> results, Outcome outcome)
    {
        var rows = results.Where(result => result.Outcome == outcome).ToArray();
        if (rows.Length == 0)
            return;

        Console.WriteLine();
        Console.WriteLine($"  {title}:");
        foreach (var row in rows.Take(50))
        {
            Console.WriteLine($"    {row.Record.Assembly}!{row.Record.Type}::{row.Record.Method}#{row.Record.Overload}");
            if (!string.IsNullOrEmpty(row.Detail))
                Console.WriteLine($"        {row.Detail}");
        }

        if (rows.Length > 50)
            Console.WriteLine($"    … {rows.Length - 50} more");
    }

    static int WriteJson(
        IReadOnlyList<RowResult> results,
        int corpusRows,
        int matchedAssemblies,
        int corpusAssemblies,
        int unmatchedRows,
        bool failOnDrift)
    {
        int verified = results.Count(result => result.Outcome == Outcome.Verified);
        int drifted = results.Count(result => result.Outcome == Outcome.Drifted);
        int unavailable = results.Count(result => result.Outcome == Outcome.Unavailable);
        int evaluated = results.Count;
        bool honest = unmatchedRows == 0 && evaluated > 0;

        var payload = new
        {
            corpusRows,
            matchedAssemblies,
            corpusAssemblies,
            unmatchedRows,
            rowsEvaluated = evaluated,
            verified,
            drifted,
            unavailable,
            honest,
            failOnDrift,
            rows = results.Select(result => new
            {
                assembly = result.Record.Assembly,
                type = result.Record.Type,
                method = result.Record.Method,
                overload = result.Record.Overload,
                outcome = result.Outcome.ToString(),
                detail = result.Detail,
                sourceUrl = result.Record.SourceUrl,
            }),
        };

        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return honest && !(failOnDrift && drifted > 0) ? 0 : 1;
    }
}
