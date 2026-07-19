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
    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples, bool json)
        => RunAsync(assemblies, cap, maxExamples, json).GetAwaiter().GetResult();

    static async Task<int> RunAsync(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples,
        bool json)
    {
        HttpClientFactory.Initialize();
        using var httpClient = HttpClientFactory.CreateNew();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);
        List<ReturnToSenderSourceProbeResult> results = [];

        foreach (string assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;

            IReadOnlyList<ReturnToSender.Result> decompilerResults;
            try
            {
                decompilerResults = ReturnToSender.CompileBackPropertyGetters(
                    assemblyPath,
                    cap - results.Count);
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

            using var source = SourceLinkService.Open(assemblyPath);
            await AuthoredRebuildFidelity.AcquirePdbAsync(source, httpClient);

            foreach (var decompilerResult in decompilerResults)
            {
                if (results.Count >= cap)
                    break;

                results.Add(await ClassifyAsync(source, fetcher, decompilerResult));
            }
        }

        return ReturnToSenderSourceProbe.Report(results, maxExamples, json);
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
