using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Core;
using DotnetInspector.Services;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

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

        var groups = candidates
            .GroupBy(keySelector, StringComparer.Ordinal)
            .Select(group => group.ToArray())
            .ToArray();

        var selected = new List<T>(cap);
        int index = 0;
        while (selected.Count < cap)
        {
            bool progressed = false;
            foreach (var group in groups)
            {
                if (index >= group.Length)
                    continue;

                selected.Add(group[index]);
                progressed = true;
                if (selected.Count >= cap)
                    break;
            }

            if (!progressed)
                break;

            index++;
        }

        return selected;
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
