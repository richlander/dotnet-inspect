using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;

using DotnetInspector.Core;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// One locked member in an <see cref="AuthoredSourceCensusRoster"/>: enough
/// identity to re-target the same member with
/// <see cref="ReturnToSender.CompileBackTargets(string, IReadOnlyList{ReturnToSender.RequestedTarget})"/>
/// in a later, separate run.
/// </summary>
internal sealed record AuthoredSourceCensusRosterMember(string Type, string Method, int Overload, string? Signature);

/// <summary>
/// The locked members captured for one assembly during roster generation, in
/// the diversified order they were confirmed to have available authored
/// source. Correlated back to a run's resolved assembly path by file name.
/// </summary>
internal sealed record AuthoredSourceCensusRosterAssembly(
    string AssemblyFileName,
    IReadOnlyList<AuthoredSourceCensusRosterMember> Members);

/// <summary>
/// A locked, generation-time snapshot of members confirmed to have authored
/// source available (<c>Outcome</c> not <c>SourceUnavailable</c>/
/// <c>UnsupportedTarget</c> at generation time), diversified by declaring
/// type. Replaying this roster at increasing <c>--cap</c> values samples
/// nested prefixes of the same locked population, so cap=100 and cap=1000
/// runs are directly comparable to each other and to earlier runs over the
/// same roster -- unlike plain live discovery, which can select a different
/// population every time <c>--cap</c> changes.
/// </summary>
internal sealed record AuthoredSourceCensusRoster(
    int SchemaVersion,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<AuthoredSourceCensusRosterAssembly> Assemblies);

/// <summary>
/// Runs the Slice-2 source-correspondence bucket classifier
/// (<see cref="ReturnToSenderSourceProbe"/>) over real, non-fixture corpus
/// assemblies using real SourceLink acquisition (the same acquisition lane as
/// <see cref="AuthoredRebuildFidelity"/>). This closes the "assembly-wide
/// source-only census: not added" gap left open by the fixture-only
/// <c>--source-correspondence-census</c> mode, which selects zero targets for
/// any assembly that is not registered in <c>FixtureCatalog</c>.
///
/// This is a peer of the RTS compile-back oracle and the authored-rebuild
/// fidelity oracle, not a replacement for either: it reports its own
/// <c>Absent</c>/<c>Failed</c>/<c>SourceUnavailable</c> outcomes distinctly and
/// never blends its verdict into theirs.
/// </summary>
static class AuthoredSourceCensus
{
    /// <summary>
    /// How many candidate targets to sample per requested slot before
    /// diversifying by declaring type. Real assemblies commonly contain one
    /// dominant type (e.g. a generated resource-string holder such as
    /// <c>SR</c>) with far more property getters than any other type; without
    /// this pool, a plain first-N-in-token-order selection can degenerate into
    /// a corpus that is almost entirely one uncheckable type, which makes the
    /// resulting quality card unrepresentative rather than a real benchmark.
    /// </summary>
    const int DiversityPoolMultiplier = 5;

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples, bool json, bool qualityCard = false)
        => RunAsync(assemblies, cap, maxExamples, json, qualityCard).GetAwaiter().GetResult();

    static async Task<int> RunAsync(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples,
        bool json,
        bool qualityCard)
    {
        HttpClientFactory.Initialize();
        using var httpClient = HttpClientFactory.CreateNew();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);
        List<ReturnToSenderSourceProbeResult> results = [];
        int assemblyCount = 0;

        foreach (string assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;

            int remainingBudget = cap - results.Count;
            int candidatePoolCap = remainingBudget >= int.MaxValue / DiversityPoolMultiplier
                ? int.MaxValue
                : remainingBudget * DiversityPoolMultiplier;

            IReadOnlyList<ReturnToSender.Result> decompilerResults;
            try
            {
                var candidates = ReturnToSender.CompileBackPropertyGetters(assemblyPath, candidatePoolCap);
                decompilerResults = DiversifyByKey(candidates, remainingBudget, result => result.Plan.TargetMethod.Type);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException)
            {
                Console.Error.WriteLine(
                    $"Warning: authored source census skipped '{assemblyPath}' "
                    + $"({ex.GetType().Name}: {ex.Message}).");
                continue;
            }

            assemblyCount++;
            using var source = SourceLinkService.Open(assemblyPath);
            await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);

            foreach (var decompilerResult in decompilerResults)
            {
                if (results.Count >= cap)
                    break;

                results.Add(await ClassifyAsync(source, fetcher, decompilerResult));
            }
        }

        string corpusLabel = $"{assemblyCount} real assembly/assemblies (SourceLink-acquired authored source)";
        return ReturnToSenderSourceProbe.Report(results, maxExamples, json, qualityCard, corpusLabel);
    }

    /// <summary>
    /// Selects up to <paramref name="cap"/> items from <paramref name="candidates"/>,
    /// round-robining across the distinct keys produced by
    /// <paramref name="keySelector"/> so that no single key (e.g. a declaring
    /// type dominated by generated resource-string properties) can crowd out
    /// the rest of the sample. Within-key order is preserved. Pure and
    /// network-free so it can be unit tested directly.
    /// </summary>
    internal static IReadOnlyList<T> DiversifyByKey<T>(IReadOnlyList<T> candidates, int cap, Func<T, string> keySelector)
    {
        if (cap <= 0)
            return [];
        if (candidates.Count <= cap)
            return candidates;

        return RoundRobinByKey(candidates, keySelector).Take(cap).ToList();
    }

    /// <summary>
    /// Reorders (without truncating) <paramref name="items"/> into round-robin
    /// order across the distinct keys produced by <paramref name="keySelector"/>,
    /// preserving within-key order. Used directly by roster generation, which
    /// needs a diversified iteration order over an entire candidate pool
    /// (filtering some out along the way) rather than a fixed-size selection.
    /// </summary>
    internal static IReadOnlyList<T> RoundRobinByKey<T>(IReadOnlyList<T> items, Func<T, string> keySelector)
    {
        var groups = items
            .GroupBy(keySelector, StringComparer.Ordinal)
            .Select(group => group.ToArray())
            .ToArray();

        var ordered = new List<T>(items.Count);
        int index = 0;
        while (ordered.Count < items.Count)
        {
            bool progressed = false;
            foreach (var group in groups)
            {
                if (index >= group.Length)
                    continue;

                ordered.Add(group[index]);
                progressed = true;
            }

            if (!progressed)
                break;

            index++;
        }

        return ordered;
    }

    static JsonSerializerOptions RosterJsonOptions()
        => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// One-time generation phase: samples a diversified candidate pool per
    /// assembly, classifies each candidate (real RTS compile-back + real
    /// SourceLink acquisition), and locks in up to <paramref name="cap"/>
    /// members per assembly whose authored source was actually available
    /// (<c>Outcome</c> not <c>SourceUnavailable</c>/<c>UnsupportedTarget</c>) —
    /// independent of whether RTS's compile-back succeeded, since that verdict
    /// is exactly what later replay runs want to re-measure fresh. Writes the
    /// locked roster to <paramref name="rosterPath"/> for repeated,
    /// apples-to-apples replay via <see cref="RunFromRoster"/>.
    /// </summary>
    public static int GenerateRoster(IReadOnlyList<string> assemblies, int cap, string rosterPath)
        => GenerateRosterAsync(assemblies, cap, rosterPath).GetAwaiter().GetResult();

    static async Task<int> GenerateRosterAsync(IReadOnlyList<string> assemblies, int cap, string rosterPath)
    {
        HttpClientFactory.Initialize();
        using var httpClient = HttpClientFactory.CreateNew();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);
        var rosterAssemblies = new List<AuthoredSourceCensusRosterAssembly>();
        int totalLocked = 0;

        foreach (string assemblyPath in assemblies)
        {
            if (totalLocked >= cap)
                break;

            int remainingBudget = cap - totalLocked;
            int candidatePoolCap = remainingBudget >= int.MaxValue / DiversityPoolMultiplier
                ? int.MaxValue
                : remainingBudget * DiversityPoolMultiplier;

            IReadOnlyList<ReturnToSender.Result> orderedCandidates;
            try
            {
                var pool = ReturnToSender.CompileBackPropertyGetters(assemblyPath, candidatePoolCap);
                orderedCandidates = RoundRobinByKey(pool, result => result.Plan.TargetMethod.Type);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException)
            {
                Console.Error.WriteLine(
                    $"Warning: roster generation skipped '{assemblyPath}' "
                    + $"({ex.GetType().Name}: {ex.Message}).");
                continue;
            }

            using var source = SourceLinkService.Open(assemblyPath);
            await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);

            var lockedMembers = new List<AuthoredSourceCensusRosterMember>();
            foreach (var candidate in orderedCandidates)
            {
                if (lockedMembers.Count >= remainingBudget)
                    break;

                var classified = await ClassifyAsync(source, fetcher, candidate);
                if (classified.Outcome is ReturnToSenderSourceOutcome.SourceUnavailable
                    or ReturnToSenderSourceOutcome.UnsupportedTarget)
                {
                    continue;
                }

                var identity = candidate.Plan.TargetMethod;
                lockedMembers.Add(new AuthoredSourceCensusRosterMember(
                    identity.Type, identity.Method, identity.Overload, identity.Signature));
            }

            totalLocked += lockedMembers.Count;
            rosterAssemblies.Add(new AuthoredSourceCensusRosterAssembly(Path.GetFileName(assemblyPath), lockedMembers));
        }

        var roster = new AuthoredSourceCensusRoster(SchemaVersion: 1, DateTimeOffset.UtcNow, rosterAssemblies);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(rosterPath)) ?? ".");
        File.WriteAllText(rosterPath, JsonSerializer.Serialize(roster, RosterJsonOptions()));
        Console.Error.WriteLine(
            $"Wrote authored-source census roster with {totalLocked} locked member(s) "
            + $"across {rosterAssemblies.Count(assembly => assembly.Members.Count > 0)} assembly/assemblies to {rosterPath}");
        return 0;
    }

    /// <summary>
    /// Replays a locked roster written by <see cref="GenerateRoster"/>: takes
    /// the first <paramref name="cap"/> members (a nested prefix, in the same
    /// locked/diversified order captured at generation time) per matched
    /// assembly, re-targets them with <see cref="ReturnToSender.CompileBackTargets(string, IReadOnlyList{ReturnToSender.RequestedTarget})"/>,
    /// and classifies each fresh (real RTS compile-back + real SourceLink
    /// acquisition). Unlike live discovery, the sampled population only grows
    /// as <paramref name="cap"/> grows -- it never changes -- so runs at
    /// different caps, or runs repeated over time, are directly comparable.
    /// </summary>
    public static int RunFromRoster(
        IReadOnlyList<string> assemblies,
        string rosterPath,
        int cap,
        int maxExamples,
        bool json,
        bool qualityCard = false)
        => RunFromRosterAsync(assemblies, rosterPath, cap, maxExamples, json, qualityCard).GetAwaiter().GetResult();

    static async Task<int> RunFromRosterAsync(
        IReadOnlyList<string> assemblies,
        string rosterPath,
        int cap,
        int maxExamples,
        bool json,
        bool qualityCard)
    {
        var roster = JsonSerializer.Deserialize<AuthoredSourceCensusRoster>(File.ReadAllText(rosterPath), RosterJsonOptions())
            ?? throw new InvalidOperationException($"Could not read authored-source census roster '{rosterPath}'.");
        var rosterByFileName = roster.Assemblies.ToDictionary(
            assembly => assembly.AssemblyFileName, StringComparer.OrdinalIgnoreCase);

        HttpClientFactory.Initialize();
        using var httpClient = HttpClientFactory.CreateNew();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);
        List<ReturnToSenderSourceProbeResult> results = [];
        int assemblyCount = 0;
        int rosterTotal = roster.Assemblies.Sum(assembly => assembly.Members.Count);

        foreach (string assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;

            string fileName = Path.GetFileName(assemblyPath);
            if (!rosterByFileName.TryGetValue(fileName, out var rosterAssembly))
            {
                Console.Error.WriteLine(
                    $"Warning: roster '{rosterPath}' has no locked members for '{fileName}'; skipping.");
                continue;
            }

            int remainingBudget = cap - results.Count;
            var replayMembers = rosterAssembly.Members.Take(remainingBudget).ToArray();
            if (replayMembers.Length == 0)
                continue;

            var targets = replayMembers
                .Select(member => new ReturnToSender.RequestedTarget(member.Type, member.Method, member.Overload, member.Signature))
                .ToArray();

            IReadOnlyList<ReturnToSender.Result> decompilerResults;
            try
            {
                decompilerResults = ReturnToSender.CompileBackTargets(assemblyPath, targets);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException)
            {
                Console.Error.WriteLine(
                    $"Warning: authored source census roster replay skipped '{assemblyPath}' "
                    + $"({ex.GetType().Name}: {ex.Message}).");
                continue;
            }

            assemblyCount++;
            using var source = SourceLinkService.Open(assemblyPath);
            await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);

            foreach (var decompilerResult in decompilerResults)
            {
                if (results.Count >= cap)
                    break;

                results.Add(await ClassifyAsync(source, fetcher, decompilerResult));
            }
        }

        string corpusLabel =
            $"{results.Count}/{rosterTotal} locked roster member(s) across {assemblyCount} assembly/assemblies "
            + $"(roster: {rosterPath})";
        return ReturnToSenderSourceProbe.Report(results, maxExamples, json, qualityCard, corpusLabel);
    }

    internal static async Task<ReturnToSenderSourceProbeResult> ClassifyAsync(
        SourceLinkService source,
        SourceFetcher fetcher,
        ReturnToSender.Result decompilerResult)
    {
        var identity = decompilerResult.Plan.TargetMethod;
        var target = new ReturnToSender.RequestedTarget(
            identity.Type,
            identity.Method,
            identity.Overload,
            identity.Signature);

        if (decompilerResult.FinalRequest is not { } request)
        {
            return new ReturnToSenderSourceProbeResult(
                target,
                ReturnToSenderSourceOutcome.UnsupportedTarget,
                CompileBackStatus: decompilerResult.Status,
                "unsupported-rts-target",
                Detail: "RTS did not produce a final artifact request.",
                SourcePath: null,
                ExpectedBody: null,
                ActualBody: decompilerResult.TargetBody,
                MemberAnchor: decompilerResult.MemberAnchor);
        }

        if (decompilerResult.Status is FidelityCheck.CompileBackStatus.RecompileFail
            or FidelityCheck.CompileBackStatus.ContextFail)
        {
            return new ReturnToSenderSourceProbeResult(
                target,
                ReturnToSenderSourceOutcome.Invalid,
                decompilerResult.Status,
                ReturnToSenderSourceProbe.FailureReason(decompilerResult),
                decompilerResult.Detail,
                SourcePath: null,
                ExpectedBody: null,
                ActualBody: decompilerResult.TargetBody,
                MemberAnchor: decompilerResult.MemberAnchor);
        }

        if (decompilerResult.Status is not (FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff))
        {
            return new ReturnToSenderSourceProbeResult(
                target,
                ReturnToSenderSourceOutcome.SourceUnavailable,
                decompilerResult.Status,
                ReturnToSenderSourceProbe.FailureReason(decompilerResult),
                decompilerResult.Detail,
                SourcePath: null,
                ExpectedBody: null,
                ActualBody: decompilerResult.TargetBody,
                MemberAnchor: decompilerResult.MemberAnchor);
        }

        var subject = new FindingSubject(
            decompilerResult.MemberAnchor?.StableSelector
                ?? $"{request.FullType}.{request.MethodName}",
            $"{request.FullType}.{request.MethodName}");
        var authored = await AuthoredSourceAcquisition.AcquireMemberAsync(
            source,
            MetadataTokens.GetToken(request.TargetMethod),
            request.MethodName,
            subject,
            fetcher);

        if (authored.Lines.Value is FindingInspection<string>.Absent absent)
        {
            return SourceUnavailable(target, decompilerResult, "source-absent", absent.Detail);
        }
        if (authored.Lines.Value is FindingInspection<string>.Failed failed)
        {
            return SourceUnavailable(target, decompilerResult, "source-fetch-failed", failed.Error.Reason);
        }
        if (authored.Text is not { } authoredBody)
        {
            return SourceUnavailable(
                target,
                decompilerResult,
                "source-fetch-failed",
                "Authored-source acquisition completed without body text.");
        }

        if (!AuthoredRebuildFidelity.TryExtractTargetBody(
            authoredBody,
            request.MethodName,
            request.Function.Signature.Parameters.Length,
            out string expected))
        {
            return SourceUnavailable(
                target,
                decompilerResult,
                "source-slice-unavailable",
                "Checksum-verified authored member source did not contain the target body.");
        }

        string? sourcePath = authored.Document?.ResolvedUrl ?? authored.Mapping?.CanonicalPath;
        return ClassifyExpectedBody(target, decompilerResult, sourcePath, expected);
    }

    /// <summary>
    /// Compares a checksum-verified authored body against the decompiled body for
    /// the same target and classifies the outcome using the same
    /// valid_match/valid_different bucket taxonomy as the fixture-based probe.
    /// Kept separate from acquisition so it can be exercised with a synthetic
    /// authored body against a real RTS result without any network access.
    /// </summary>
    internal static ReturnToSenderSourceProbeResult ClassifyExpectedBody(
        ReturnToSender.RequestedTarget target,
        ReturnToSender.Result decompilerResult,
        string? sourcePath,
        string expected)
    {
        string actual = decompilerResult.TargetBody;
        if (ReturnToSenderSourceProbe.NormalizeBody(expected) == ReturnToSenderSourceProbe.NormalizeBody(actual))
        {
            return new ReturnToSenderSourceProbeResult(
                target,
                ReturnToSenderSourceOutcome.ValidMatch,
                decompilerResult.Status,
                "valid_match",
                Detail: null,
                sourcePath,
                expected,
                actual,
                MemberAnchor: decompilerResult.MemberAnchor);
        }

        string reason = ReturnToSenderSourceProbe.ClassifyValidDifference(
            expected,
            actual,
            decompilerResult.Status,
            decompilerResult.Decisions ?? [],
            out var classificationDetail);
        var opcodeEvidence = ReturnToSenderSourceProbe.OpcodeEvidence(decompilerResult);
        return new ReturnToSenderSourceProbeResult(
            target,
            ReturnToSenderSourceOutcome.ValidDifferent,
            decompilerResult.Status,
            reason,
            Detail: classificationDetail,
            sourcePath,
            expected,
            actual,
            OriginalOpcodes: opcodeEvidence?.OriginalOpcodes,
            RecompiledOpcodes: opcodeEvidence?.RecompiledOpcodes,
            IlDiffLines: opcodeEvidence?.IlDiffLines,
            MemberAnchor: decompilerResult.MemberAnchor);
    }

    static ReturnToSenderSourceProbeResult SourceUnavailable(
        ReturnToSender.RequestedTarget target,
        ReturnToSender.Result decompilerResult,
        string reason,
        string? detail)
        => new(
            target,
            ReturnToSenderSourceOutcome.SourceUnavailable,
            decompilerResult.Status,
            reason,
            detail,
            SourcePath: null,
            ExpectedBody: null,
            ActualBody: decompilerResult.TargetBody,
            MemberAnchor: decompilerResult.MemberAnchor);
}
