using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

using DotnetInspector.Core;
using DotnetInspector.Fixtures;
using DotnetInspector.HarnessReports;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ILInspector.DecompilerHarness;

enum ReturnToSenderSourceOutcome
{
    ValidMatch,
    ValidDifferent,
    Invalid,
    SourceUnavailable,
    UnsupportedTarget,
}

enum PrinterExactOutcome
{
    NotRecorded,
    Exact,
    Different,
}

enum SourceAcquisitionOutcome
{
    NotAttempted,
    Complete,
    Absent,
    Failed,
}

sealed record ReturnToSenderSourceProbeResult(
    ReturnToSender.RequestedTarget Target,
    ReturnToSenderSourceOutcome Outcome,
    FidelityCheck.CompileBackStatus? CompileBackStatus,
    string Reason,
    string? Detail,
    string? SourcePath,
    string? ExpectedBody,
    string? ActualBody,
    string? OriginalOpcodes = null,
    string? RecompiledOpcodes = null,
    IReadOnlyList<string>? IlDiffLines = null,
    MemberAnchor? MemberAnchor = null,
    ReturnToSender.FaultIsolationKind? FaultIsolationKind = null,
    ReturnToSender.FaultIsolationMethod? FaultIsolationMethod = null,
    bool UsedCompileBackFloor = false,
    ReturnToSender.FaultIsolationKind? SupersededFaultIsolationKind = null,
    ReturnToSender.FaultIsolationMethod? SupersededFaultIsolationMethod = null,
    PrinterExactOutcome PrinterExact = PrinterExactOutcome.NotRecorded,
    SourceAcquisitionOutcome SourceAcquisition = SourceAcquisitionOutcome.NotAttempted,
    string? SourceAcquisitionDetail = null)
{
    public bool Passed => Outcome == ReturnToSenderSourceOutcome.ValidMatch;
    public bool Different => Outcome == ReturnToSenderSourceOutcome.ValidDifferent;
    public bool Failed => Outcome == ReturnToSenderSourceOutcome.Invalid;
    public bool Skipped => Outcome is ReturnToSenderSourceOutcome.SourceUnavailable
        or ReturnToSenderSourceOutcome.UnsupportedTarget;
}

enum ReturnToSenderInvalidKind
{
    ProductBodyDefect,
    HarnessShellReconstruction,
    Unclassified,
}

static class ReturnToSenderInvalidClassifier
{
    public static ReturnToSenderInvalidKind? Classify(ReturnToSenderSourceProbeResult result)
    {
        if (result.Outcome != ReturnToSenderSourceOutcome.Invalid)
            return null;

        return ClassifyKind(result.FaultIsolationKind, result.Detail);
    }

    internal static ReturnToSenderInvalidKind ClassifyKind(
        ReturnToSender.FaultIsolationKind? faultIsolation,
        string? detail)
        => faultIsolation switch
        {
            ReturnToSender.FaultIsolationKind.BodyDefect
                => ReturnToSenderInvalidKind.ProductBodyDefect,
            ReturnToSender.FaultIsolationKind.ShellOrClosureDefect
                => ReturnToSenderInvalidKind.HarnessShellReconstruction,
            _ when HasClosureStopDetail(detail)
                => ReturnToSenderInvalidKind.HarnessShellReconstruction,
            _ => ReturnToSenderInvalidKind.Unclassified,
        };

    static bool HasClosureStopDetail(string? detail)
        => detail is not null
            && (detail.StartsWith("closure-stalled", StringComparison.Ordinal)
                || detail.StartsWith("closure-root-budget", StringComparison.Ordinal));
}

sealed record SourceCorrespondenceFinding(
    string FindingId,
    string DescriptorId,
    string Category,
    string SubjectId,
    string Display,
    string Outcome,
    string? CompileBackStatus,
    string Reason,
    string? Detail,
    string? SourceFile,
    bool HasFidelityDiffEvidence);

static partial class ReturnToSenderSourceProbe
{
    internal static readonly HarnessReportDescriptor Descriptor = new("return-to-sender.source-correspondence", 2);

    internal sealed record ProbeTarget(
        ReturnToSender.RequestedTarget Target,
        IReadOnlyList<string> ExpectedFragments,
        int MetadataToken,
        int ParameterCount);

    public static int Run(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples,
        bool json,
        string? emitHarnessReport = null)
        => WriteResults(
            Evaluate(assemblies, cap),
            maxExamples,
            json,
            emitHarnessReport,
            "RETURNTOSENDER SOURCE PROBE");

    public static int RunSourceCorrespondenceCensus(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples,
        bool json,
        IReadOnlyList<string>? repositoryPaths = null,
        IReadOnlyDictionary<string, NuGetPackageCoordinate>? packageCoordinates = null,
        string? emitHarnessReport = null)
        => RunSourceCorrespondenceCensusAsync(
            assemblies,
            cap,
            maxExamples,
            json,
            repositoryPaths,
            packageCoordinates,
            emitHarnessReport).GetAwaiter().GetResult();

    static async Task<int> RunSourceCorrespondenceCensusAsync(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples,
        bool json,
        IReadOnlyList<string>? repositoryPaths,
        IReadOnlyDictionary<string, NuGetPackageCoordinate>? packageCoordinates,
        string? emitHarnessReport)
    {
        HttpClientFactory.Initialize(new HttpClientFactoryOptions());
        NuGetCache.Initialize("dotnet-inspect");
        using var httpClient = HttpClientFactory.CreateClient();
        var fetcher = new SourceFetcher(HttpClientFactory.SharedUntrustedFetch);
        var results = await EvaluateSourceCorrespondenceAsync(
            assemblies,
            cap,
            httpClient,
            fetcher,
            repositoryPaths,
            packageCoordinates);
        return WriteResults(
            results,
            maxExamples,
            json,
            emitHarnessReport,
            "SOURCE CORRESPONDENCE CENSUS",
            failOnInvalid: false);
    }

    static int WriteResults(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        int maxExamples,
        bool json,
        string? emitHarnessReport,
        string title,
        bool failOnInvalid = true)
    {
        var report = BuildReport(results);
        int passed = results.Count(result => result.Passed);
        int different = results.Count(result => result.Different);
        int failed = results.Count(result => result.Failed);
        int skipped = results.Count(result => result.Skipped);
        var buckets = results
            .GroupBy(result => result.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
        var findings = BuildFindings(results);

        if (emitHarnessReport is not null)
        {
            HarnessReportStorage.Write(emitHarnessReport, report);
            HarnessLog.Status($"Wrote harness report: {emitHarnessReport}");
        }

        if (json)
        {
            WriteJson(results, buckets, findings, passed, different, failed, skipped);
            return HasCommandFailure(results, failOnInvalid) ? 1 : 0;
        }

        Console.WriteLine($"{title} over {results.Count} target(s)");
        Console.WriteLine();
        Console.WriteLine($"  ValidMatch       : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch)}");
        Console.WriteLine($"  ValidDifferent   : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent)}");
        Console.WriteLine($"  Invalid          : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid)}");
        Console.WriteLine($"  SourceUnavailable: {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable)}");
        Console.WriteLine($"  UnsupportedTarget: {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget)}");
        if (results.Any(result => result.SourceAcquisition != SourceAcquisitionOutcome.NotAttempted))
        {
            Console.WriteLine();
            Console.WriteLine("Source acquisition:");
            Console.WriteLine($"  Complete         : {results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Complete)}");
            Console.WriteLine($"  Absent           : {results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Absent)}");
            Console.WriteLine($"  Failed           : {results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Failed)}");
        }
        Console.WriteLine();
        Console.WriteLine($"  Passed   : {passed}");
        Console.WriteLine($"  Different: {different}");
        Console.WriteLine($"  Failed   : {failed}");
        Console.WriteLine($"  Skipped  : {skipped}");
        if (buckets.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Buckets:");
            foreach (var bucket in buckets)
                Console.WriteLine($"  {bucket.Count()}: {bucket.Key}");
        }
        if (findings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Source-correspondence findings:");
            foreach (var bucket in findings
                .GroupBy(finding => finding.Category, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {bucket.Count()}: {bucket.Key}");
            }
        }
        var examples = results
            .Where(result => result.Outcome != ReturnToSenderSourceOutcome.ValidMatch)
            .Take(maxExamples)
            .ToArray();
        if (examples.Length > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Examples (first {examples.Length}):");
            foreach (var example in examples)
            {
                Console.WriteLine($"  {example.Target.Type}::{example.Target.Method}#{example.Target.Overload}  outcome={OutcomeId(example.Outcome)}  rts={example.CompileBackStatus?.ToString() ?? "missing"}  bucket={example.Reason}");
                if (!string.IsNullOrWhiteSpace(example.Detail))
                    Console.WriteLine($"      detail: {example.Detail}");
                if (example.SourceAcquisition != SourceAcquisitionOutcome.NotAttempted)
                    Console.WriteLine($"      source-acquisition: {SourceAcquisitionId(example.SourceAcquisition)}");
                if (!string.IsNullOrWhiteSpace(example.SourceAcquisitionDetail)
                    && !string.Equals(example.SourceAcquisitionDetail, example.Detail, StringComparison.Ordinal))
                {
                    Console.WriteLine($"      source-acquisition-detail: {example.SourceAcquisitionDetail}");
                }
                if (!string.IsNullOrWhiteSpace(example.SourcePath))
                    Console.WriteLine($"      source: {example.SourcePath}");
                if (example.CompileBackStatus is
                    FidelityCheck.CompileBackStatus.OpcodeDiff
                    or FidelityCheck.CompileBackStatus.OperandDiff)
                {
                    if (example.IlDiffLines is { Count: > 0 } diffLines)
                    {
                        Console.WriteLine("      il-diff:");
                        foreach (var line in diffLines.Take(3))
                            Console.WriteLine($"        {line}");
                    }

                    if (!string.IsNullOrWhiteSpace(example.OriginalOpcodes))
                        Console.WriteLine($"      original-opcodes  : {example.OriginalOpcodes}");
                    if (!string.IsNullOrWhiteSpace(example.RecompiledOpcodes))
                        Console.WriteLine($"      recompiled-opcodes: {example.RecompiledOpcodes}");
                }
            }
        }

        return HasCommandFailure(results, failOnInvalid) ? 1 : 0;
    }

    internal static bool HasCommandFailure(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        bool failOnInvalid)
        => results.Any(result =>
            result.SourceAcquisition == SourceAcquisitionOutcome.Failed
            || (failOnInvalid
                && result.Outcome == ReturnToSenderSourceOutcome.Invalid));

    internal static DecompilerHarnessReport<IReadOnlyList<ReturnToSenderSourceProbeResult>> BuildReport(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results)
    {
        string sourceRegime = results.Any(result =>
            result.SourceAcquisition != SourceAcquisitionOutcome.NotAttempted)
                ? "pdb-acquired"
                : "provided";
        string population = HarnessPopulationKey.Create(
            $"return-to-sender.source-correspondence.{sourceRegime}",
            results.Select(result =>
                $"{result.Target.Type}|{result.Target.Method}|{result.Target.Overload}|{result.Target.Signature}"));
        int total = results.Count;
        int bodyless = results.Count(result =>
            result.Reason is "valid_match.source_bodyless"
                or "invalid.source_bodyless_non_exact");

        return new DecompilerHarnessReport<IReadOnlyList<ReturnToSenderSourceProbeResult>>(
            Descriptor,
            results,
            new HarnessComparisonProjection(
                "RTS compile-back and authored-source correspondence remain independent outcome lanes.",
                population,
                [
                    new("valid-match", "Valid authored-body match", MetricGoal.Higher, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch && result.ExpectedBody is not null), total), population),
                    new("valid-different", "Valid different", MetricGoal.Context, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent), total), population),
                    new("invalid", "Invalid / RTS compile-back failed", MetricGoal.Lower, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid), total), population),
                    new("source-unavailable", "Source unavailable", MetricGoal.Context, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable), total), population),
                    new("source-bodyless", "Authored declaration has no body", MetricGoal.Context, new MetricValue(bodyless, total), population),
                    new("unsupported-target", "Unsupported target", MetricGoal.Lower, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget), total), population),
                    new("source-acquisition-complete", "Source acquisition complete", MetricGoal.Higher, new MetricValue(results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Complete), total), population),
                    new("source-acquisition-absent", "Source acquisition absent", MetricGoal.Context, new MetricValue(results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Absent), total), population),
                    new("source-acquisition-failed", "Source acquisition failed", MetricGoal.Lower, new MetricValue(results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Failed), total), population),
                ]));
    }

    public static IReadOnlyList<ReturnToSenderSourceProbeResult> Evaluate(IReadOnlyList<string> assemblies, int cap)
    {
        var results = new List<ReturnToSenderSourceProbeResult>();
        foreach (var assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;

            var targets = DiscoverTargets(assemblyPath, cap - results.Count);
            results.AddRange(EvaluateTargets(assemblyPath, targets.Select(target => target.Target).ToArray()));
        }

        return results.Count > cap ? results.Take(cap).ToArray() : results;
    }

    internal static async Task<IReadOnlyList<ReturnToSenderSourceProbeResult>>
        EvaluateSourceCorrespondenceAsync(
            IReadOnlyList<string> assemblies,
            int cap,
            HttpClient httpClient,
            SourceFetcher fetcher,
            IReadOnlyList<string>? repositoryPaths = null,
            IReadOnlyDictionary<string, NuGetPackageCoordinate>? packageCoordinates = null,
            IPdbStore? pdbStore = null)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(fetcher);

        var results = new List<ReturnToSenderSourceProbeResult>();
        foreach (string assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;

            var targets = DiscoverTargets(assemblyPath, cap - results.Count);
            if (targets.Count == 0)
                continue;

            var acquisitions = new Dictionary<string, SourceAcquisitionAttempt>(StringComparer.Ordinal);
            var sourceMembers = new List<ReturnToSenderSourceMember>();
            NuGetPackageCoordinate? package =
                packageCoordinates?.TryGetValue(assemblyPath, out var suppliedPackage) == true
                    ? suppliedPackage
                    : TryGetNuGetPackageCoordinate(assemblyPath);

            SourceLinkService? source = null;
            SourceAcquisitionAttempt? assemblyAcquisition = null;
            try
            {
                source = SourceLinkService.Open(assemblyPath);
                await AuthoredRebuildFidelity.AcquirePdbAsync(
                    source,
                    httpClient,
                    package?.Id,
                    package?.Version,
                    pdbStore);
            }
            catch (Exception ex) when (
                AuthoredRebuildFidelity.IsPdbAcquisitionFailure(ex))
            {
                assemblyAcquisition = new SourceAcquisitionAttempt(
                    SourceAcquisitionOutcome.Failed,
                    $"Portable PDB acquisition failed: {ex.Message}",
                    SourcePath: null,
                    Member: null);
            }

            if (assemblyAcquisition is null
                && source is not null
                && source.Context.NeedsPdb)
            {
                assemblyAcquisition = new SourceAcquisitionAttempt(
                    SourceAcquisitionOutcome.Absent,
                    source.Context.WindowsPdbDetected
                        ? "A Windows PDB was found, but portable-PDB source mapping is unavailable."
                        : "No matching portable PDB is available.",
                    SourcePath: null,
                    Member: null);
            }

            using (source)
            {
                foreach (var target in targets)
                {
                    SourceAcquisitionAttempt acquisition;
                    if (assemblyAcquisition is not null)
                    {
                        acquisition = assemblyAcquisition;
                    }
                    else
                    {
                        if (source is null)
                        {
                            throw new InvalidOperationException(
                                "Source acquisition completed without a source context or failure.");
                        }

                        acquisition = await AcquireSourceAsync(
                            source,
                            fetcher,
                            target,
                            repositoryPaths);
                    }

                    acquisitions.Add(Key(target.Target), acquisition);
                    if (acquisition.Member is { } member)
                        sourceMembers.Add(member);
                }
            }

            ReturnToSenderSourceIndex sourceIndex =
                ReturnToSenderSourceIndex.FromPdbMappedMembers(sourceMembers);
            IReadOnlyList<ReturnToSenderSourceProbeResult> evaluated = EvaluateTargets(
                assemblyPath,
                targets.Select(target => target.Target).ToArray(),
                sourceIndex,
                "checksum-verified PDB source is unavailable for the target");
            results.AddRange(evaluated.Select(result =>
                AddSourceAcquisition(result, acquisitions[Key(result.Target)])));
        }

        return results.Count > cap ? results.Take(cap).ToArray() : results;
    }

    internal static async Task<SourceAcquisitionAttempt> AcquireSourceAsync(
        SourceLinkService source,
        SourceFetcher fetcher,
        ProbeTarget target,
        IReadOnlyList<string>? repositoryPaths = null)
    {
        var subject = new FindingSubject(
            TargetId(target.Target),
            TargetDisplay(target.Target));
        PdbMemberSourceInspection authored = await PdbSourceAcquisition.AcquireMemberAsync(
            source,
            target.MetadataToken,
            target.Target.Method,
            subject,
            fetcher,
            repositoryPaths);
        return CreateSourceAcquisition(target, authored);
    }

    internal static SourceAcquisitionAttempt CreateSourceAcquisition(
        ProbeTarget target,
        PdbMemberSourceInspection authored)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(authored);

        string? sourcePath = authored.Document?.CanonicalPath
            ?? authored.Mapping?.CanonicalPath;

        if (authored.Lines.Value is FindingInspection<string>.Absent absent)
        {
            return new SourceAcquisitionAttempt(
                SourceAcquisitionOutcome.Absent,
                absent.Detail,
                sourcePath,
                Member: null);
        }
        if (authored.Lines.Value is FindingInspection<string>.Failed failed)
        {
            return new SourceAcquisitionAttempt(
                SourceAcquisitionOutcome.Failed,
                failed.Error.Reason,
                sourcePath,
                Member: null);
        }
        if (authored.Text is not { Length: > 0 } memberSource)
        {
            return new SourceAcquisitionAttempt(
                SourceAcquisitionOutcome.Failed,
                "Authored-source acquisition completed without member text.",
                sourcePath,
                Member: null);
        }
        bool extracted = AuthoredRebuildFidelity.TryExtractTargetBodies(
                memberSource,
                target.Target.Method,
                target.ParameterCount,
                out string body,
                out string? printerBody);
        if (!extracted
            && !AuthoredRebuildFidelity.IsBodylessTarget(
                memberSource,
                target.Target.Method,
                target.ParameterCount))
        {
            return new SourceAcquisitionAttempt(
                SourceAcquisitionOutcome.Failed,
                "Checksum-verified authored member source did not contain the target body.",
                sourcePath,
                Member: null);
        }

        return new SourceAcquisitionAttempt(
            SourceAcquisitionOutcome.Complete,
            authored.ChecksumVerification?.ToString(),
            sourcePath,
            new ReturnToSenderSourceMember(
                target.Target.Type,
                target.Target.Method,
                target.Target.Overload,
                target.Target.Signature ?? "",
                sourcePath ?? authored.Mapping?.OriginalPath ?? "",
                extracted ? body : null,
                MetadataToken: target.MetadataToken,
                PrinterBody: extracted ? printerBody : null));
    }

    internal static ReturnToSenderSourceProbeResult AddSourceAcquisition(
        ReturnToSenderSourceProbeResult result,
        SourceAcquisitionAttempt acquisition)
    {
        bool sourceUnavailable = string.Equals(
            result.Reason,
            "source-slice-unavailable",
            StringComparison.Ordinal);
        return result with
        {
            Reason = sourceUnavailable
                ? acquisition.Outcome switch
                {
                    SourceAcquisitionOutcome.Absent => "source_absent",
                    SourceAcquisitionOutcome.Failed => "source_failed",
                    _ => result.Reason,
                }
                : result.Reason,
            Detail = sourceUnavailable
                && acquisition.Outcome is SourceAcquisitionOutcome.Absent
                    or SourceAcquisitionOutcome.Failed
                        ? acquisition.Detail ?? result.Detail
                        : result.Detail,
            SourcePath = result.SourcePath ?? acquisition.SourcePath,
            SourceAcquisition = acquisition.Outcome,
            SourceAcquisitionDetail = acquisition.Detail,
        };
    }

    internal sealed record SourceAcquisitionAttempt(
        SourceAcquisitionOutcome Outcome,
        string? Detail,
        string? SourcePath,
        ReturnToSenderSourceMember? Member);

    internal static NuGetPackageCoordinate? TryGetNuGetPackageCoordinate(
        string assemblyPath)
    {
        string packageRoot = Path.GetFullPath(NuGetCache.GetNuGetCachePath());
        string relative = Path.GetRelativePath(
            packageRoot,
            Path.GetFullPath(assemblyPath));
        if (Path.IsPathRooted(relative))
            return null;
        string[] segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || segments[0] == ".."
            || segments[1] == "..")
        {
            return null;
        }

        return new NuGetPackageCoordinate(segments[0], segments[1]);
    }

    internal sealed record NuGetPackageCoordinate(string Id, string Version);

    public static IReadOnlyList<ReturnToSenderSourceProbeResult> EvaluateTargets(
        string assemblyPath,
        IReadOnlyList<ReturnToSender.RequestedTarget> targets)
    {
        if (targets.Count == 0)
            return [];

        var sourceIndex = ReturnToSenderSourceIndex.TryCreate(assemblyPath);
        return EvaluateTargets(assemblyPath, targets, sourceIndex);
    }

    public static IReadOnlyList<ReturnToSenderSourceProbeResult> EvaluateTargets(
        string assemblyPath,
        IReadOnlyList<ReturnToSender.RequestedTarget> targets,
        IReadOnlyList<string> sourcePaths)
        => EvaluateTargets(
            assemblyPath,
            targets,
            ReturnToSenderSourceIndex.TryCreate(sourcePaths),
            "source index could not be built from the supplied source paths");

    public static IReadOnlyList<ReturnToSenderSourceProbeResult> EvaluateWithIndex(
        string assemblyPath,
        IReadOnlyList<ReturnToSender.RequestedTarget> targets,
        ReturnToSenderSourceIndex sourceIndex)
        => EvaluateTargets(
            assemblyPath,
            targets,
            sourceIndex,
            "authored-source corpus row missing for target");

    static IReadOnlyList<ReturnToSenderSourceProbeResult> EvaluateTargets(
        string assemblyPath,
        IReadOnlyList<ReturnToSender.RequestedTarget> targets,
        ReturnToSenderSourceIndex? sourceIndex,
        string sourceUnavailableDetail = "assembly is not registered in FixtureCatalog")
    {
        if (targets.Count == 0)
            return [];

        var rtsResults = ReturnToSender.CompileBackTargets(assemblyPath, targets.Distinct().ToArray(), sourceIndex)
            .ToDictionary(
                result => Key(
                    result.Plan.TargetMethod.Type,
                    result.Plan.TargetMethod.Method,
                    result.Plan.TargetMethod.Overload),
                StringComparer.Ordinal);

        var results = new List<ReturnToSenderSourceProbeResult>();
        foreach (var target in targets)
        {
            if (!rtsResults.TryGetValue(Key(target.Type, target.Method, target.Overload), out var result))
            {
                AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.UnsupportedTarget,
                    CompileBackStatus: null,
                    "unsupported-rts-target",
                    Detail: null,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: null));
                continue;
            }

            ReturnToSenderSourceMember? sourceMember = null;
            bool sourceFound = sourceIndex?.TryFind(target, out sourceMember) == true;
            if (result.Status is FidelityCheck.CompileBackStatus.RecompileFail
                or FidelityCheck.CompileBackStatus.ContextFail)
            {
                AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.Invalid,
                    result.Status,
                    FailureReason(result),
                    result.Detail,
                    SourcePath: sourceMember?.SourcePath,
                    ExpectedBody: sourceMember?.Body,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor));
                continue;
            }

            if (result.Status is not (
                FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff))
            {
                AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    FailureReason(result),
                    result.Detail,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor));
                continue;
            }

            if (sourceIndex is null)
            {
                AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    "fixture-source-unavailable",
                    sourceUnavailableDetail,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor));
                continue;
            }

            if (!sourceFound || sourceMember is null)
            {
                if (sourceIndex is not null
                    && sourceIndex.TryFindRecordSynthesizedMember(target, out var recordSourcePath))
                {
                    AddBodylessSourceResult(results, target, result, recordSourcePath);
                    continue;
                }

                AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    "source-slice-unavailable",
                    $"no source member matched {target.Type}::{target.Method}#{target.Overload}",
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor));
                continue;
            }

            if (sourceMember.Body is not { } expected)
            {
                AddBodylessSourceResult(results, target, result, sourceMember.SourcePath);
                continue;
            }

            string actual = result.TargetBody;
            if (NormalizeBody(expected) == NormalizeBody(actual))
            {
                var printerExact = ComparePrinterText(sourceMember.PrinterBody, actual);
                AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.ValidMatch,
                    result.Status,
                    "valid_match",
                    Detail: null,
                    sourceMember.SourcePath,
                    expected,
                    actual,
                    MemberAnchor: result.MemberAnchor,
                    PrinterExact: printerExact));
                continue;
            }

            string reason = ClassifyValidDifference(
                expected,
                actual,
                result.Status,
                result.Decisions ?? [],
                out var classificationDetail);
            var fidelityEvidence = FidelityEvidence(result);
            AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
                target,
                ReturnToSenderSourceOutcome.ValidDifferent,
                result.Status,
                reason,
                Detail: classificationDetail,
                sourceMember.SourcePath,
                expected,
                actual,
                OriginalOpcodes: fidelityEvidence?.OriginalOpcodes,
                RecompiledOpcodes: fidelityEvidence?.RecompiledOpcodes,
                IlDiffLines: fidelityEvidence?.IlDiffLines,
                MemberAnchor: result.MemberAnchor,
                PrinterExact: sourceMember.PrinterBody is null
                    ? PrinterExactOutcome.NotRecorded
                    : PrinterExactOutcome.Different));
        }

        return results;
    }

    internal static PrinterExactOutcome ComparePrinterText(string? expected, string actual)
    {
        if (expected is null)
            return PrinterExactOutcome.NotRecorded;

        static string MechanicalEnvelope(string text)
        {
            string normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            return normalized.EndsWith('\n')
                ? normalized[..^1]
                : normalized;
        }

        return string.Equals(
            MechanicalEnvelope(expected),
            MechanicalEnvelope(actual),
            StringComparison.Ordinal)
                ? PrinterExactOutcome.Exact
                : PrinterExactOutcome.Different;
    }

    internal static IReadOnlyList<SourceCorrespondenceFinding> BuildFindings(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results)
        => [.. results
            .Where(result => result.Outcome is ReturnToSenderSourceOutcome.ValidDifferent or ReturnToSenderSourceOutcome.Invalid)
            .Select(SourceCorrespondenceFindingFor)];

    static SourceCorrespondenceFinding SourceCorrespondenceFindingFor(ReturnToSenderSourceProbeResult result)
    {
        string descriptorId = $"source.correspondence.{result.Reason}";
        string subjectId = result.MemberAnchor?.StableSelector ?? TargetId(result.Target);
        string display = result.MemberAnchor is { } anchor
            ? $"{anchor.TypeFullName}.{anchor.MemberName}"
            : TargetDisplay(result.Target);
        return new SourceCorrespondenceFinding(
            $"{descriptorId}|{subjectId}",
            descriptorId,
            SourceCorrespondenceCategory(result),
            subjectId,
            display,
            OutcomeId(result.Outcome),
            result.CompileBackStatus?.ToString(),
            result.Reason,
            result.Detail,
            SourceFileName(result.SourcePath),
            result.IlDiffLines is { Count: > 0 }
                || !string.IsNullOrWhiteSpace(result.OriginalOpcodes)
                || !string.IsNullOrWhiteSpace(result.RecompiledOpcodes));
    }

    internal static string? SourceFileName(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        return Path.GetFileName(sourcePath.Replace('\\', '/'));
    }

    static string SourceCorrespondenceCategory(ReturnToSenderSourceProbeResult result)
    {
        if (result.Outcome == ReturnToSenderSourceOutcome.Invalid)
            return "invalid";

        string reason = result.Reason;
        if (reason.StartsWith("valid_different.known_taste", StringComparison.Ordinal)
            || reason.StartsWith("valid_different.known_compiler_option", StringComparison.Ordinal))
            return "ignorable";
        if (reason.StartsWith("valid_different.semantic_opcode_diff", StringComparison.Ordinal))
            return "semantic-opcode-diff";
        if (reason.StartsWith("valid_different.semantic_operand_diff", StringComparison.Ordinal))
            return "semantic-operand-diff";
        if (reason.Contains(".compiler_lowering.", StringComparison.Ordinal))
            return "not-yet-raised-sugar";
        if (reason.StartsWith("valid_different.source_shape_frontier", StringComparison.Ordinal)
            && reason.Contains("residual", StringComparison.Ordinal))
            return "structuring-residue";
        if (reason.StartsWith("valid_different.source_shape_frontier", StringComparison.Ordinal))
            return "not-yet-raised-sugar";

        return "unclassified";
    }

    static string TargetId(ReturnToSender.RequestedTarget target)
        => $"{target.Type}::{target.Method}#{target.Overload}";

    static string TargetDisplay(ReturnToSender.RequestedTarget target)
        => $"{target.Type}.{target.Method}#{target.Overload}";

    static FidelityDiffEvidence? FidelityEvidence(ReturnToSender.Result result)
    {
        if (result.Status is not (
            FidelityCheck.CompileBackStatus.OpcodeDiff
            or FidelityCheck.CompileBackStatus.OperandDiff))
        {
            return null;
        }

        IReadOnlyList<string> diffLines = result.Status == FidelityCheck.CompileBackStatus.OperandDiff
            && result.FidelityDiff is not null
                ? IlDiffPrinter.ToUnifiedLines(result.FidelityDiff)
                : result.IlDiffDiagnostic is null
                    ? Array.Empty<string>()
                    : IlDiffPrinter.ToUnifiedLines(result.IlDiffDiagnostic);
        return new FidelityDiffEvidence(
            NullIfWhiteSpace(result.OriginalOpcodes),
            NullIfWhiteSpace(result.RecompiledOpcodes),
            diffLines);
    }

    static string? NullIfWhiteSpace(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    sealed record FidelityDiffEvidence(
        string? OriginalOpcodes,
        string? RecompiledOpcodes,
        IReadOnlyList<string> IlDiffLines);

    static void WriteJson(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results,
        IReadOnlyList<IGrouping<string, ReturnToSenderSourceProbeResult>> buckets,
        IReadOnlyList<SourceCorrespondenceFinding> findings,
        int passed,
        int different,
        int failed,
        int skipped)
    {
        var payload = new
        {
            summary = new
            {
                total = results.Count,
                valid_match = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch),
                valid_different = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent),
                invalid = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid),
                source_unavailable = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable),
                source_bodyless = results.Count(result =>
                    result.Reason is "valid_match.source_bodyless"
                        or "invalid.source_bodyless_non_exact"),
                unsupported_target = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget),
                passed,
                different,
                failed,
                skipped,
            },
            source_acquisition = new
            {
                not_attempted = results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.NotAttempted),
                complete = results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Complete),
                absent = results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Absent),
                failed = results.Count(result => result.SourceAcquisition == SourceAcquisitionOutcome.Failed),
            },
            source_correspondence = new
            {
                findings = findings.Count,
                categories = findings
                    .GroupBy(finding => finding.Category, StringComparer.Ordinal)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => new
                    {
                        category = group.Key,
                        count = group.Count(),
                    }),
            },
            buckets = buckets.Select(bucket => new
            {
                reason = bucket.Key,
                count = bucket.Count(),
            }),
            source_correspondence_findings = findings.Select(finding => new
            {
                finding_id = finding.FindingId,
                descriptor_id = finding.DescriptorId,
                category = finding.Category,
                subject_id = finding.SubjectId,
                display = finding.Display,
                outcome = finding.Outcome,
                compile_back_status = finding.CompileBackStatus,
                reason = finding.Reason,
                detail = finding.Detail,
                source_file = finding.SourceFile,
                has_fidelity_diff_evidence = finding.HasFidelityDiffEvidence,
            }),
            results = results.Select(result => new
            {
                target = new
                {
                    type = result.Target.Type,
                    method = result.Target.Method,
                    overload = result.Target.Overload,
                },
                outcome = OutcomeId(result.Outcome),
                compile_back_status = result.CompileBackStatus?.ToString(),
                reason = result.Reason,
                detail = result.Detail,
                source_path = result.SourcePath,
                expected_body = result.ExpectedBody,
                actual_body = result.ActualBody,
                fault_isolation = result.FaultIsolationKind?.ToString(),
                fault_isolation_method = result.FaultIsolationMethod?.ToString(),
                used_compile_back_floor = result.UsedCompileBackFloor,
                superseded_fault_isolation = result.SupersededFaultIsolationKind?.ToString(),
                superseded_fault_isolation_method = result.SupersededFaultIsolationMethod?.ToString(),
                source_acquisition = SourceAcquisitionId(result.SourceAcquisition),
                source_acquisition_detail = result.SourceAcquisitionDetail,
                original_opcodes = result.OriginalOpcodes,
                recompiled_opcodes = result.RecompiledOpcodes,
                il_diff = result.IlDiffLines,
            }),
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static void AddBodylessSourceResult(
        List<ReturnToSenderSourceProbeResult> results,
        ReturnToSender.RequestedTarget target,
        ReturnToSender.Result result,
        string sourcePath)
    {
        var exact = result.Status == FidelityCheck.CompileBackStatus.Exact;
        AddProbeResult(results, result, new ReturnToSenderSourceProbeResult(
            target,
            exact ? ReturnToSenderSourceOutcome.ValidMatch : ReturnToSenderSourceOutcome.Invalid,
            result.Status,
            exact ? "valid_match.source_bodyless" : "invalid.source_bodyless_non_exact",
            $"source member matched {target.Type}::{target.Method}#{target.Overload}, but it has no explicit source body; compile-back status is {result.Status}",
            sourcePath,
            ExpectedBody: null,
            ActualBody: result.TargetBody,
            MemberAnchor: result.MemberAnchor));
    }

    /// <summary>
    /// Adds a probe result, stamping every fact derived from the RTS
    /// <see cref="ReturnToSender.Result"/>'s fault-isolation state.
    /// </summary>
    /// <remarks>
    /// This is the single projection point for those fields: no call site supplies
    /// them, so they cannot diverge per path (#3814). A compile-back floor
    /// supersedes the RTS compile and clears
    /// <see cref="ReturnToSender.Result.FaultIsolation"/> (#3783), so without
    /// <see cref="ReturnToSenderSourceProbeResult.UsedCompileBackFloor"/> a
    /// floor-rescued row is indistinguishable from one RTS handled unaided — which
    /// is exactly the inventory of where RTS cannot yet stand alone.
    /// <paramref name="result"/> is null only for an unsupported target, where RTS
    /// produced no compile at all and every field below is correctly absent.
    /// <para>
    /// The projection is gated by
    /// <c>CorpusFloorProvenanceTests.TheProbeProjectsFloorProvenanceOntoTheRow</c>,
    /// and the separate bodyless producer by
    /// <c>TheBodylessProducerAlsoCarriesFloorProvenance</c>. That every emission
    /// path routes through here remains a structural property of this file rather
    /// than a tested one: removing a call reverts to a plain <c>results.Add</c>,
    /// which still compiles.
    /// </para>
    /// </remarks>
    internal static void AddProbeResult(
        List<ReturnToSenderSourceProbeResult> results,
        ReturnToSender.Result? result,
        ReturnToSenderSourceProbeResult probe)
        => results.Add(probe with
        {
            FaultIsolationKind = result?.FaultIsolation?.Kind,
            FaultIsolationMethod = result?.FaultIsolation?.Method,
            UsedCompileBackFloor = result?.UsedCompileBackFloor ?? false,
            SupersededFaultIsolationKind = result?.SupersededFaultIsolation?.Kind,
            SupersededFaultIsolationMethod = result?.SupersededFaultIsolation?.Method,
        });

}

internal sealed record ReturnToSenderSourceMember(
    string Type,
    string Method,
    int Overload,
    string Signature,
    string SourcePath,
    string? Body,
    int? MetadataToken = null,
    Guid? ModuleVersionId = null,
    string? SignatureUnavailableReason = null,
    string? PrinterBody = null);

internal sealed class ReturnToSenderSourceIndex
{
    readonly Dictionary<string, ReturnToSenderSourceMember> _members;
    readonly Dictionary<string, RecordSourceInfo> _recordSources;
    readonly Dictionary<int, ReturnToSenderSourceMember> _correlatedMembersByToken;

    ReturnToSenderSourceIndex(
        Dictionary<string, ReturnToSenderSourceMember> members,
        Dictionary<string, RecordSourceInfo> recordSources,
        Dictionary<int, ReturnToSenderSourceMember>? correlatedMembersByToken = null)
    {
        _members = members;
        _recordSources = recordSources;
        _correlatedMembersByToken = correlatedMembersByToken ?? [];
    }

    public static ReturnToSenderSourceIndex? TryCreate(IReadOnlyList<string> sourcePaths)
    {
        var members = new Dictionary<string, ReturnToSenderSourceMember>(StringComparer.Ordinal);
        var recordSources = new Dictionary<string, RecordSourceInfo>(StringComparer.Ordinal);
        var overloads = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        var sourceFiles = new List<(string Path, CompilationUnitSyntax Root)>();
        foreach (var sourcePath in sourcePaths)
        {
            if (!TryReadSourceFile(sourcePath, out var root))
                return null;
            sourceFiles.Add((sourcePath, root));
        }

        var sourceIdentity = CSharpSourceIdentityContext.Create(sourceFiles.Select(file => file.Root));
        foreach (var sourceFile in sourceFiles)
            AddSourceFile(members, recordSources, overloads, sourceFile.Path, sourceFile.Root, sourceIdentity);

        return new ReturnToSenderSourceIndex(members, recordSources);
    }

    public static ReturnToSenderSourceIndex? TryCreate(string assemblyPath)
    {
        foreach (var fixture in FixtureCatalog.All)
        {
            if (!TryGetFixtureAssemblyPath(fixture, out var fixtureAssemblyPath))
                continue;

            if (!string.Equals(
                Path.GetFullPath(fixtureAssemblyPath),
                Path.GetFullPath(assemblyPath),
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryGetSourcePaths(fixture, out var sourcePaths))
                return null;

            return TryCreate(sourcePaths);
        }

        return null;
    }

    /// <summary>
    /// Builds an index from authored members already correlated to exact metadata
    /// method definitions, as in the vendored authored-source corpus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each member's module version ID and metadata token must identify the same
    /// logical module and exact method whose checksum-verified body was snapshotted.
    /// This typed identity is the only source correspondence strong enough for
    /// fault attribution. The module version ID is a non-hostile corpus identity,
    /// not a cryptographic byte digest.
    /// </para>
    /// <para>
    /// Raw syntax indexes created by <see cref="TryCreate(IReadOnlyList{string})"/>
    /// retain normal source-probe lookup behavior, but cannot support attribution:
    /// they lack the original build configuration and semantic identity.
    /// <c>TryIsolateRecompileFailure_DeclinesRawSourceIndex</c> gates that boundary.
    /// PDB method spans cannot supply the missing provenance because an unsupplied
    /// input can map its sequence points into another document. No PDB-specific
    /// token map is constructed here; that broader absence has no dedicated gate.
    /// #3835 remains blocked on an independently trusted complete-source manifest
    /// or stronger per-method provenance.
    /// </para>
    /// </remarks>
    public static ReturnToSenderSourceIndex FromCorrelatedMembers(
        IEnumerable<ReturnToSenderSourceMember> sourceMembers,
        MetadataReader reader)
    {
        var members = new Dictionary<string, ReturnToSenderSourceMember>(StringComparer.Ordinal);
        var correlatedMembersByToken = new Dictionary<int, ReturnToSenderSourceMember>();
        Guid moduleVersionId = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        if (moduleVersionId == Guid.Empty)
            throw new InvalidDataException("The benchmark module has an empty module version ID.");

        foreach (var member in sourceMembers)
        {
            ValidateCorrelatedMember(reader, moduleVersionId, member);
            if (!members.TryAdd(Key(member.Type, member.Method, member.Overload), member))
            {
                throw new InvalidDataException(
                    $"Duplicate correlated target {member.Type}::{member.Method}#{member.Overload}.");
            }
            int metadataToken = member.MetadataToken!.Value;
            if (!correlatedMembersByToken.TryAdd(metadataToken, member))
                throw new InvalidDataException($"Duplicate correlated MethodDef token 0x{metadataToken:x8}.");
        }

        return new ReturnToSenderSourceIndex(
            members,
            new Dictionary<string, RecordSourceInfo>(StringComparer.Ordinal),
            correlatedMembersByToken);
    }

    /// <summary>
    /// Builds a comparison-only index from checksum-verified PDB source members.
    /// </summary>
    /// <remarks>
    /// PDB sequence points locate source for correspondence, but do not establish
    /// the complete-source provenance required for RTS fault attribution. This
    /// factory therefore never populates the metadata-token attribution map.
    /// <c>PdbMappedSourceIndex_IsIneligibleForFaultAttribution</c> gates that boundary.
    /// </remarks>
    public static ReturnToSenderSourceIndex FromPdbMappedMembers(
        IEnumerable<ReturnToSenderSourceMember> sourceMembers)
    {
        var members = new Dictionary<string, ReturnToSenderSourceMember>(StringComparer.Ordinal);
        foreach (var member in sourceMembers)
        {
            if (!members.TryAdd(Key(member.Type, member.Method, member.Overload), member))
            {
                throw new InvalidDataException(
                    $"Duplicate PDB-mapped target {member.Type}::{member.Method}#{member.Overload}.");
            }
        }

        return new ReturnToSenderSourceIndex(
            members,
            new Dictionary<string, RecordSourceInfo>(StringComparer.Ordinal));
    }

    static void ValidateCorrelatedMember(
        MetadataReader reader,
        Guid moduleVersionId,
        ReturnToSenderSourceMember member)
    {
        if (member.ModuleVersionId is not { } memberModuleVersionId
            || memberModuleVersionId == Guid.Empty
            || memberModuleVersionId != moduleVersionId)
        {
            throw new InvalidDataException(
                $"Correlated member {member.Type}::{member.Method}#{member.Overload} "
                + "does not identify the benchmark module.");
        }

        if (member.MetadataToken is not { } metadataToken
            || (metadataToken & unchecked((int)0xff000000)) != 0x06000000)
        {
            throw new InvalidDataException(
                $"Correlated member {member.Type}::{member.Method}#{member.Overload} "
                + "does not carry a MethodDef token.");
        }

        int rowNumber = metadataToken & 0x00ffffff;
        if (rowNumber == 0 || rowNumber > reader.MethodDefinitions.Count)
            throw new InvalidDataException($"Correlated MethodDef token 0x{metadataToken:x8} is out of range.");

        var methodHandle = MetadataTokens.MethodDefinitionHandle(rowNumber);
        var method = reader.GetMethodDefinition(methodHandle);
        var type = reader.GetTypeDefinition(method.GetDeclaringType());
        string methodName = reader.GetString(method.Name);
        string fullType = reader.GetFullTypeName(type);
        int overload = ReturnToSenderSourceProbe.OverloadIndex(
            reader,
            type,
            methodHandle,
            methodName);
        string? signature = ReturnToSenderSourceProbe.UniqueTargetSignature(
            reader,
            type,
            methodName,
            methodHandle);
        bool signatureMatches;
        if (string.IsNullOrEmpty(member.Signature))
        {
            signatureMatches = signature is null;
        }
        else
        {
            MemberSignatureShapeResult memberShape =
                MemberSignatureShapeCodec.Normalize(member.Signature, out string? canonical);
            signatureMatches = signature is not null
                && memberShape.Shape is not null
                && (member.Signature.StartsWith("mss1:", StringComparison.Ordinal)
                    ? string.Equals(canonical, signature, StringComparison.Ordinal)
                    : MetadataMemberSignatureShape.LegacyShapeCanDescribe(
                        reader,
                        methodHandle,
                        memberShape.Shape));
        }
        if (!string.Equals(member.Type, fullType, StringComparison.Ordinal)
            || !string.Equals(member.Method, methodName, StringComparison.Ordinal)
            || member.Overload != overload
            || !signatureMatches)
        {
            throw new InvalidDataException(
                $"Correlated MethodDef token 0x{metadataToken:x8} does not match "
                + $"{member.Type}::{member.Method}#{member.Overload}.");
        }
    }

    static bool TryGetFixtureAssemblyPath(FixtureDefinition fixture, out string path)
    {
        try
        {
            path = fixture.AssemblyPath();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            path = "";
            return false;
        }
    }

    static bool TryGetSourcePaths(FixtureDefinition fixture, out IReadOnlyList<string> paths)
    {
        try
        {
            paths = fixture.SourcePaths();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            paths = [];
            return false;
        }
    }

    public bool TryFind(ReturnToSender.RequestedTarget target, out ReturnToSenderSourceMember member)
    {
        MemberSignatureCorrespondence<ReturnToSenderSourceMember> correspondence =
            ResolveBySignature(target);
        if (correspondence.Kind == MemberSignatureCorrespondenceKind.Unique)
        {
            member = correspondence.Match!;
            return true;
        }

        return _members.TryGetValue(Key(target.Type, target.Method, target.Overload), out member!);
    }

    internal MemberSignatureCorrespondence<ReturnToSenderSourceMember> ResolveBySignature(
        ReturnToSender.RequestedTarget target)
    {
        if (target.Signature is null)
        {
            return MemberSignatureCorrespondence<ReturnToSenderSourceMember>.Unavailable(
                "The target carries no signature shape.");
        }
        if (!target.Signature.StartsWith("mss1:", StringComparison.Ordinal))
        {
            return MemberSignatureCorrespondence<ReturnToSenderSourceMember>.Unavailable(
                "Only canonical signature shapes may select source candidates.");
        }

        MemberSignatureShapeResult targetShape =
            MemberSignatureShapeCodec.Decode(target.Signature);
        // Preserve the complete same-type, same-name sibling set. The shape matcher treats
        // every unavailable sibling as evidence that uniqueness cannot be established.
        var candidates = _members.Values
            .Where(member =>
                string.Equals(member.Type, target.Type, StringComparison.Ordinal)
                && string.Equals(member.Method, target.Method, StringComparison.Ordinal))
            .Select(member => (
                Candidate: member,
                Shape: string.IsNullOrEmpty(member.Signature)
                    ? MemberSignatureShapeResult.Unavailable(
                        member.SignatureUnavailableReason
                        ?? "The source signature shape is unavailable.")
                    : MemberSignatureShapeCodec.Decode(member.Signature)))
            .ToArray();
        return MemberSignatureShapeMatcher.Match(targetShape, candidates);
    }

    /// <summary>
    /// Resolves a source member for fault attribution by its exact metadata token.
    /// </summary>
    /// <remarks>
    /// The redundant type, method, and overload check catches a malformed correlated
    /// record rather than trusting a token from the wrong target. Raw syntax indexes
    /// have no token map and therefore fail closed.
    /// </remarks>
    public bool TryFindForAttribution(
        ReturnToSender.RequestedTarget target,
        int metadataToken,
        out ReturnToSenderSourceMember member)
    {
        member = null!;
        if (!_correlatedMembersByToken.TryGetValue(metadataToken, out var correlated)
            || !string.Equals(correlated.Type, target.Type, StringComparison.Ordinal)
            || !string.Equals(correlated.Method, target.Method, StringComparison.Ordinal)
            || correlated.Overload != target.Overload)
        {
            return false;
        }

        member = correlated;
        return true;
    }

    public bool TryFindRecordSynthesizedMember(ReturnToSender.RequestedTarget target, out string sourcePath)
    {
        if (_recordSources.TryGetValue(target.Type, out var source)
            && source.SynthesizedMembers.Contains(target.Method))
        {
            sourcePath = source.SourcePath;
            return true;
        }

        sourcePath = "";
        return false;
    }

    static bool TryReadSourceFile(string sourcePath, out CompilationUnitSyntax root)
    {
        string source;
        try
        {
            source = File.ReadAllText(sourcePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            root = null!;
            return false;
        }

        var tree = CSharpSyntaxTree.ParseText(source, path: sourcePath);
        root = tree.GetCompilationUnitRoot();
        return true;
    }

    static void AddSourceFile(
        Dictionary<string, ReturnToSenderSourceMember> members,
        Dictionary<string, RecordSourceInfo> recordSources,
        Dictionary<string, Dictionary<string, int>> overloads,
        string sourcePath,
        CompilationUnitSyntax root,
        CSharpSourceIdentityContext sourceIdentity)
    {
        foreach (var member in SourceMembers(root, sourcePath, recordSources, overloads, sourceIdentity))
            members.TryAdd(Key(member.Type, member.Method, member.Overload), member);
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static IReadOnlySet<string> RecordSynthesizedMembers(RecordDeclarationSyntax record)
    {
        var members = new HashSet<string>(StringComparer.Ordinal)
        {
            "ToString",
            "GetHashCode",
            "Equals",
            "op_Equality",
            "op_Inequality",
        };
        if (record.ParameterList is { Parameters.Count: > 0 } parameters)
        {
            members.Add("Deconstruct");
            foreach (var parameter in parameters.Parameters)
                members.Add($"get_{parameter.Identifier.ValueText}");
        }

        return members;
    }

    static IEnumerable<ReturnToSenderSourceMember> SourceMembers(
        CompilationUnitSyntax root,
        string sourcePath,
        Dictionary<string, RecordSourceInfo> recordSources,
        Dictionary<string, Dictionary<string, int>> overloads,
        CSharpSourceIdentityContext sourceIdentity)
    {
        foreach (var member in SourceMembers(root.Members, namespaceName: "", containingTypes: [], sourcePath, recordSources, overloads, sourceIdentity))
            yield return member;
    }

    static IEnumerable<ReturnToSenderSourceMember> SourceMembers(
        SyntaxList<MemberDeclarationSyntax> declarations,
        string namespaceName,
        IReadOnlyList<string> containingTypes,
        string sourcePath,
        Dictionary<string, RecordSourceInfo> recordSources,
        Dictionary<string, Dictionary<string, int>> overloads,
        CSharpSourceIdentityContext sourceIdentity)
    {
        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case BaseNamespaceDeclarationSyntax ns:
                    {
                        string nextNamespace = namespaceName.Length == 0
                            ? ns.Name.ToString()
                            : $"{namespaceName}.{ns.Name}";
                        foreach (var member in SourceMembers(ns.Members, nextNamespace, containingTypes, sourcePath, recordSources, overloads, sourceIdentity))
                            yield return member;
                        break;
                    }
                case TypeDeclarationSyntax type:
                    {
                        string typeName = CSharpSourceIdentityContext.TypeMetadataName(type);
                        var typeStack = containingTypes.Concat([typeName]).ToArray();
                        string fullType = namespaceName.Length == 0
                            ? string.Join(".", typeStack)
                            : $"{namespaceName}.{string.Join(".", typeStack)}";
                        if (type is RecordDeclarationSyntax record)
                            recordSources.TryAdd(fullType, new RecordSourceInfo(sourcePath, RecordSynthesizedMembers(record)));

                        foreach (var member in TypeMembers(type, fullType, sourcePath, overloads, sourceIdentity))
                            yield return member;
                        foreach (var member in SourceMembers(type.Members, namespaceName, typeStack, sourcePath, recordSources, overloads, sourceIdentity))
                            yield return member;
                        break;
                    }
            }
        }

        static IEnumerable<ReturnToSenderSourceMember> TypeMembers(
            TypeDeclarationSyntax type,
            string fullType,
            string path,
            Dictionary<string, Dictionary<string, int>> overloadsByType,
            CSharpSourceIdentityContext sourceIdentity)
        {
            if (!overloadsByType.TryGetValue(fullType, out var overloads))
                overloadsByType[fullType] = overloads = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var sourceMember in sourceIdentity.TypeMembers(type, fullType))
            {
                int overload = NextOverload(overloads, sourceMember.MetadataName);
                string signature = sourceMember.SignatureShape.Shape is { } shape
                    ? MemberSignatureShapeCodec.Encode(shape)
                    : "";
                yield return new ReturnToSenderSourceMember(
                    fullType,
                    sourceMember.MetadataName,
                    overload,
                    signature,
                    path,
                    sourceMember.Body,
                    SignatureUnavailableReason: sourceMember.SignatureShape.UnavailableReason);
            }
        }

        static int NextOverload(Dictionary<string, int> overloads, string methodName)
        {
            int overload = overloads.GetValueOrDefault(methodName);
            overloads[methodName] = overload + 1;
            return overload;
        }
    }
}

internal sealed record RecordSourceInfo(string SourcePath, IReadOnlySet<string> SynthesizedMembers);

static partial class ReturnToSenderSourceProbe
{
    internal static IReadOnlyList<ProbeTarget> DiscoverTargets(string assemblyPath, int cap)
    {
        var targets = new List<ProbeTarget>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return targets;

        var reader = pe.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (targets.Count >= cap)
                break;

            var type = reader.GetTypeDefinition(typeHandle);
            var typeNamespace = reader.GetString(type.Namespace);
            if (!type.GetDeclaringType().IsNil
                || !type.IsPublic
                || typeNamespace == "System"
                || typeNamespace.StartsWith("System.", StringComparison.Ordinal)
                || AttributeReader.HasAttribute(reader, type.GetCustomAttributes(), "System.CodeDom.Compiler.GeneratedCodeAttribute")
                || AttributeReader.HasAttribute(reader, type.GetCustomAttributes(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                || reader.GetString(type.Name) == "<Module>"
                || reader.GetString(type.Name).Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            var fullType = reader.GetFullTypeName(type);
            var typeFragments = AttributeReader.RenderAttributes(reader, type.GetCustomAttributes(), qualifyNames: true)
                .Select(attribute => $"[{attribute}]")
                .ToArray();

            foreach (var methodHandle in type.GetMethods())
            {
                if (targets.Count >= cap)
                    break;

                var method = reader.GetMethodDefinition(methodHandle);
                var methodName = reader.GetString(method.Name);

                if (method.RelativeVirtualAddress == 0
                    || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                    || methodName is ".ctor" or ".cctor"
                    || methodName.StartsWith("get_", StringComparison.Ordinal)
                    || methodName.StartsWith("set_", StringComparison.Ordinal)
                    || methodName.StartsWith("add_", StringComparison.Ordinal)
                    || methodName.StartsWith("remove_", StringComparison.Ordinal)
                    || methodName.Contains('<', StringComparison.Ordinal))
                {
                    continue;
                }

                var overload = OverloadIndex(reader, type, methodHandle, methodName);
                var signature = UniqueTargetSignature(reader, type, methodName, methodHandle);
                var fragments = new List<string>();
                fragments.AddRange(typeFragments);
                fragments.AddRange(AttributeReader.RenderAttributes(reader, method.GetCustomAttributes(), qualifyNames: true)
                    .Select(attribute => $"[{attribute}]"));
                AddReturnAndParameterFragments(reader, method.GetParameters(), fragments);
                targets.Add(new ProbeTarget(
                    new ReturnToSender.RequestedTarget(fullType, methodName, overload, signature),
                    fragments.Distinct(StringComparer.Ordinal).ToArray(),
                    MetadataTokens.GetToken(methodHandle),
                    RealMethodTargetEnumerator.ParameterCount(reader, type, method)));
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                if (targets.Count >= cap)
                    break;

                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (accessors.Getter.IsNil)
                    continue;

                var getter = reader.GetMethodDefinition(accessors.Getter);
                if (getter.RelativeVirtualAddress == 0
                    || (getter.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }

                var methodName = reader.GetString(getter.Name);
                var overload = OverloadIndex(reader, type, accessors.Getter, methodName);
                var signature = UniqueTargetSignature(reader, type, methodName, accessors.Getter);
                var fragments = new List<string>();
                fragments.AddRange(typeFragments);
                fragments.AddRange(AttributeReader.RenderAttributes(reader, property.GetCustomAttributes(), qualifyNames: true)
                    .Select(attribute => $"[{attribute}]"));
                AddReturnAndParameterFragments(reader, getter.GetParameters(), fragments);
                targets.Add(new ProbeTarget(
                    new ReturnToSender.RequestedTarget(fullType, methodName, overload, signature),
                    fragments.Distinct(StringComparer.Ordinal).ToArray(),
                    MetadataTokens.GetToken(accessors.Getter),
                    RealMethodTargetEnumerator.ParameterCount(reader, type, getter)));
            }
        }

        return targets;
    }

    static void AddReturnAndParameterFragments(MetadataReader reader, ParameterHandleCollection parameters, List<string> fragments)
    {
        foreach (var parameterHandle in parameters)
        {
            var parameter = reader.GetParameter(parameterHandle);
            var attributes = AttributeReader.RenderParameterAttributes(reader, parameterHandle)
                .Select(attribute => parameter.SequenceNumber == 0 ? $"[return: {attribute}]" : $"[{attribute}]");
            fragments.AddRange(attributes);
        }
    }

    internal static int OverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string methodName)
    {
        int overload = 0;
        foreach (var handle in typeDef.GetMethods())
        {
            if (handle == target)
                return overload;
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName)
                overload++;
        }
        return overload;
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static string Key(ReturnToSender.RequestedTarget target)
        => Key(target.Type, target.Method, target.Overload);

    // Only carry a shape that unambiguously round-trips to this exact metadata member.
    internal static string? UniqueTargetSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        string methodName,
        MethodDefinitionHandle handle)
    {
        MemberSignatureShapeResult result =
            MetadataMemberSignatureShape.Create(reader, handle);
        if (result.Shape is null)
            return null;

        string signature = MemberSignatureShapeCodec.Encode(result.Shape);
        return ReturnToSender.ResolvesUniquelyBySignature(
            reader,
            typeDef,
            methodName,
            signature,
            handle)
            ? signature
            : null;
    }

    static string OutcomeId(ReturnToSenderSourceOutcome outcome)
        => outcome switch
        {
            ReturnToSenderSourceOutcome.ValidMatch => "valid_match",
            ReturnToSenderSourceOutcome.ValidDifferent => "valid_different",
            ReturnToSenderSourceOutcome.Invalid => "invalid",
            ReturnToSenderSourceOutcome.SourceUnavailable => "source_unavailable",
            ReturnToSenderSourceOutcome.UnsupportedTarget => "unsupported_target",
            _ => outcome.ToString(),
        };

    static string SourceAcquisitionId(SourceAcquisitionOutcome outcome)
        => outcome switch
        {
            SourceAcquisitionOutcome.NotAttempted => "not_attempted",
            SourceAcquisitionOutcome.Complete => "complete",
            SourceAcquisitionOutcome.Absent => "absent",
            SourceAcquisitionOutcome.Failed => "failed",
            _ => outcome.ToString(),
        };

    static string ExpressionBodyText(TypeSyntax returnType, ExpressionSyntax expression)
        => returnType is PredefinedTypeSyntax predefined
            && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword)
            ? $"{expression};"
            : $"return {expression};";

    static string StatementsText(BlockSyntax body)
        => string.Join(Environment.NewLine, body.Statements.Select(statement => statement.ToString()));

    static string NormalizeBody(string text)
        => Regex.Replace(text, @"\s+", "");

    static bool TryKnownTasteDifference(
        string expected,
        string actual,
        IReadOnlyList<DecompilerDecision> decisions,
        out string? detail)
    {
        var rewrites = decisions
            .Where(decision => decision is { Category: "taste", RuleId: "type-name.framework-imported" })
            .Select(decision =>
            {
                string oldValue = decision.OldValue ?? decision.Subject.Replace('+', '.');
                int lastDot = oldValue.LastIndexOf('.');
                return lastDot < 0 || lastDot == oldValue.Length - 1
                    ? null
                    : new FrameworkTypeRewrite(
                        oldValue,
                        decision.NewValue ?? oldValue[(lastDot + 1)..],
                        decision);
            })
            .Where(rewrite => rewrite is not null)
            .Select(rewrite => rewrite!)
            .ToArray();
        if (rewrites.Length == 0)
        {
            detail = null;
            return false;
        }

        string wrapped = "{" + Environment.NewLine + expected + Environment.NewLine + "}";
        var tree = CSharpSyntaxTree.ParseText(wrapped);
        var root = tree.GetCompilationUnitRoot();
        var rewriter = new FrameworkTypeNameRewriter(rewrites);
        var rewrittenRoot = rewriter.Visit(root);
        if (rewrittenRoot is null || rewriter.Applied.Count == 0)
        {
            detail = null;
            return false;
        }

        string rewrittenExpected = rewrittenRoot.ToFullString();
        int openBrace = rewrittenExpected.IndexOf('{');
        int closeBrace = rewrittenExpected.LastIndexOf('}');
        if (openBrace >= 0 && closeBrace > openBrace)
            rewrittenExpected = rewrittenExpected[(openBrace + 1)..closeBrace];

        if (NormalizeBody(rewrittenExpected) == NormalizeBody(actual))
        {
            detail = string.Join("; ", rewriter.Applied.Distinct().Select(decision => decision.Detail));
            return true;
        }

        detail = null;
        return false;
    }

    internal static string ClassifyValidDifference(
        string expected,
        string actual,
        FidelityCheck.CompileBackStatus status,
        IReadOnlyList<DecompilerDecision> decisions,
        out string detail)
    {
        if (TryKnownTasteDifference(expected, actual, decisions, out var tasteDetail))
        {
            detail = tasteDetail ?? "documented product taste decision accounts for the source delta";
            return "valid_different.known_taste";
        }

        var shape = SourceDifferenceShape(expected, actual);
        if (status == FidelityCheck.CompileBackStatus.Exact
            && TryKnownExactDifference(expected, actual, shape, out var knownReason, out var knownDetail))
        {
            detail = knownDetail;
            return knownReason;
        }

        string statusId = status switch
        {
            FidelityCheck.CompileBackStatus.OpcodeDiff => "opcode_diff",
            FidelityCheck.CompileBackStatus.OperandDiff => "operand_diff",
            FidelityCheck.CompileBackStatus.Exact => "exact",
            _ => status.ToString().ToLowerInvariant(),
        };
        if (status == FidelityCheck.CompileBackStatus.OpcodeDiff
            && AllowsDynamicCallSiteClassification(shape)
            && IsDynamicCallSiteLowering(actual))
        {
            detail = "decompiled body is Roslyn-valid and compile-back opcode-different; classification=compiler_lowering.dynamic_callsite; compile-back=OpcodeDiff";
            return "valid_different.compiler_lowering.dynamic_callsite.opcode_diff";
        }

        string reason = shape.StartsWith("compiler_lowering.", StringComparison.Ordinal)
            ? $"valid_different.{shape}.{statusId}"
            : status == FidelityCheck.CompileBackStatus.OpcodeDiff
                ? $"valid_different.semantic_opcode_diff.{ShapeLeaf(shape)}"
                : status == FidelityCheck.CompileBackStatus.OperandDiff
                    ? $"valid_different.semantic_operand_diff.{ShapeLeaf(shape)}"
                    : $"valid_different.{shape}.{statusId}";
        detail = $"decompiled body is Roslyn-valid but differs from the fixture source slice; classification={shape}; compile-back={status}";
        return reason;
    }

    static bool TryKnownExactDifference(string expected, string actual, string shape, out string reason, out string detail)
    {
        switch (shape)
        {
            case "source_shape_frontier.checked_context" when !ContainsCommentTrivia(expected)
                && !ContainsCommentTrivia(actual)
                && ContextStrippedBodiesMatch(expected, actual):
                reason = "valid_different.known_compiler_option.checked_context";
                detail = "decompiled body is Roslyn-valid and compile-back exact; the source delta is an intentional checked-context spelling caused by standalone compile-back losing the fixture project's checked arithmetic option";
                return true;
            default:
                reason = "";
                detail = "";
                return false;
        }
    }

    static bool ContainsCommentTrivia(string body)
    {
        var tree = CSharpSyntaxTree.ParseText("class __Probe { void __M() {" + Environment.NewLine + body + Environment.NewLine + "} }");
        return tree.GetCompilationUnitRoot()
            .DescendantTrivia(descendIntoTrivia: true)
            .Any(trivia =>
                trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
    }

    static bool ContextStrippedBodiesMatch(string expected, string actual)
        => NormalizeBody(CheckedContextStrippedBody(expected)) == NormalizeBody(CheckedContextStrippedBody(actual));

    static string CheckedContextStrippedBody(string body)
    {
        var tree = CSharpSyntaxTree.ParseText("class __Probe { void __M() {" + Environment.NewLine + body + Environment.NewLine + "} }");
        var method = tree.GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault();
        if (method?.Body is not { } methodBody)
            return body;

        var rewrittenBody = new CheckedContextRemover().Visit(methodBody) as BlockSyntax;
        return rewrittenBody is null
            ? body
            : string.Join(Environment.NewLine, rewrittenBody.Statements.Select(statement => statement.ToFullString()));
    }

    sealed class CheckedContextRemover : CSharpSyntaxRewriter
    {
        static readonly SyntaxAnnotation s_unwrappedCheckedBlock = new("unwrapped-checked-block");

        public override SyntaxNode? VisitBlock(BlockSyntax node)
        {
            var visited = (BlockSyntax)base.VisitBlock(node)!;
            var statements = new List<StatementSyntax>(visited.Statements.Count);
            foreach (var statement in visited.Statements)
            {
                if (statement is BlockSyntax block && block.HasAnnotation(s_unwrappedCheckedBlock))
                    statements.AddRange(block.Statements);
                else
                    statements.Add(statement);
            }

            return visited.WithStatements(SyntaxFactory.List(statements));
        }

        public override SyntaxNode? VisitCheckedStatement(CheckedStatementSyntax node)
        {
            var visited = (BlockSyntax)Visit(node.Block)!;
            return visited.WithAdditionalAnnotations(s_unwrappedCheckedBlock);
        }

        public override SyntaxNode? VisitCheckedExpression(CheckedExpressionSyntax node)
            => Visit(node.Expression) ?? node.Expression;
    }

    static string SourceDifferenceShape(string expected, string actual)
    {
        var expectedNodes = ParseBodyNodes(expected);
        var actualNodes = ParseBodyNodes(actual);

        if (expectedNodes.Any(node => node is YieldStatementSyntax))
            return "compiler_lowering.iterator";
        if (expectedNodes.Any(node => node is AwaitExpressionSyntax))
            return "compiler_lowering.async";
        if (ContainsCheckedContext(expectedNodes, actualNodes))
            return "source_shape_frontier.checked_context";
        if (ContainsUnsafeResidual(expectedNodes, actualNodes))
            return "source_shape_frontier.unsafe_residual";
        if (expectedNodes.Any(node => node is WithExpressionSyntax) || actualNodes.Any(node => node is WithExpressionSyntax))
            return "source_shape_frontier.record_with";
        if (expectedNodes.Any(node => node is AnonymousFunctionExpressionSyntax))
            return "source_shape_frontier.closure";
        if (ContainsDynamic(expectedNodes) || ContainsDynamic(actualNodes))
            return "source_shape_frontier.dynamic";
        if (actual.Contains("/*", StringComparison.Ordinal))
            return "source_shape_frontier.residual";
        return "source_shape_frontier.syntax";
    }

    static IReadOnlyList<SyntaxNode> ParseBodyNodes(string body)
    {
        var tree = CSharpSyntaxTree.ParseText("class __Probe { void __M() {" + Environment.NewLine + body + Environment.NewLine + "} }");
        return tree.GetCompilationUnitRoot().DescendantNodes().ToArray();
    }

    static bool ContainsCheckedContext(IReadOnlyList<SyntaxNode> expectedNodes, IReadOnlyList<SyntaxNode> actualNodes)
        => expectedNodes.Any(node => node is CheckedExpressionSyntax or CheckedStatementSyntax)
            || actualNodes.Any(node => node is CheckedExpressionSyntax or CheckedStatementSyntax);

    static bool ContainsUnsafeResidual(IReadOnlyList<SyntaxNode> expectedNodes, IReadOnlyList<SyntaxNode> actualNodes)
        => expectedNodes.Concat(actualNodes).Any(node =>
            node is FixedStatementSyntax
                or PointerTypeSyntax
                or StackAllocArrayCreationExpressionSyntax
                or ImplicitStackAllocArrayCreationExpressionSyntax
                or FunctionPointerTypeSyntax
                or FunctionPointerParameterSyntax
                or FunctionPointerParameterListSyntax
                or FunctionPointerCallingConventionSyntax
                or FunctionPointerUnmanagedCallingConventionListSyntax);

    static bool ContainsDynamic(IReadOnlyList<SyntaxNode> nodes)
        => nodes.Any(node => node is IdentifierNameSyntax identifier && identifier.Identifier.ValueText == "dynamic");

    static bool IsDynamicCallSiteLowering(string actual)
        => actual.Contains("CallSite<", StringComparison.Ordinal)
            && actual.Contains("Binder.", StringComparison.Ordinal)
            && actual.Contains("CSharpArgumentInfo", StringComparison.Ordinal);

    static bool AllowsDynamicCallSiteClassification(string shape)
        => shape is
            "source_shape_frontier.syntax" or
            "source_shape_frontier.checked_context" or
            "source_shape_frontier.dynamic";

    static string ShapeLeaf(string shape)
    {
        const string prefix = "source_shape_frontier.";
        return shape.StartsWith(prefix, StringComparison.Ordinal)
            ? shape[prefix.Length..]
            : shape.Replace('.', '_');
    }

    sealed record FrameworkTypeRewrite(string FullName, string SimpleName, DecompilerDecision Decision);

    sealed class FrameworkTypeNameRewriter(IReadOnlyList<FrameworkTypeRewrite> rewrites) : CSharpSyntaxRewriter
    {
        public List<DecompilerDecision> Applied { get; } = [];

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            var rewrite = FindRewrite(node);
            var visited = (QualifiedNameSyntax)base.VisitQualifiedName(node)!;
            return rewrite is not null
                ? ApplyRewrite(visited, rewrite)
                : RewriteName(visited) ?? visited;
        }

        public override SyntaxNode? VisitAliasQualifiedName(AliasQualifiedNameSyntax node)
        {
            var rewrite = FindRewrite(node);
            var visited = (AliasQualifiedNameSyntax)base.VisitAliasQualifiedName(node)!;
            return rewrite is not null
                ? ApplyRewrite(visited, rewrite)
                : RewriteName(visited) ?? visited;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var rewrite = FindRewrite(node);
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            return rewrite is not null
                ? ApplyRewrite(visited, rewrite)
                : RewriteName(visited) ?? visited;
        }

        SyntaxNode? RewriteName(SyntaxNode node)
            => FindRewrite(node) is { } rewrite ? ApplyRewrite(node, rewrite) : null;

        FrameworkTypeRewrite? FindRewrite(SyntaxNode node)
        {
            string canonical = CanonicalNameText(node);
            return rewrites
                .Where(rewrite => canonical == rewrite.FullName)
                .OrderByDescending(rewrite => rewrite.FullName.Length)
                .FirstOrDefault();
        }

        SyntaxNode ApplyRewrite(SyntaxNode node, FrameworkTypeRewrite rewrite)
        {
            Applied.Add(rewrite.Decision);
            return ReplacementName(node, rewrite.SimpleName).WithTriviaFrom(node);
        }

        static string CanonicalNameText(SyntaxNode node)
            => node switch
            {
                GenericNameSyntax generic => generic.Identifier.ValueText,
                QualifiedNameSyntax qualified => $"{CanonicalNameText(qualified.Left)}.{CanonicalNameText(qualified.Right)}",
                AliasQualifiedNameSyntax aliasQualified => $"{aliasQualified.Alias.Identifier.ValueText}::{CanonicalNameText(aliasQualified.Name)}",
                MemberAccessExpressionSyntax memberAccess => $"{CanonicalNameText(memberAccess.Expression)}.{CanonicalNameText(memberAccess.Name)}",
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
                NameSyntax name => name.ToString(),
                _ => node.ToString(),
            };

        static SimpleNameSyntax ReplacementName(SyntaxNode original, string simpleName)
            => original switch
            {
                GenericNameSyntax generic => SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(simpleName),
                    generic.TypeArgumentList),
                QualifiedNameSyntax { Right: GenericNameSyntax generic } => SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(simpleName),
                    generic.TypeArgumentList),
                MemberAccessExpressionSyntax { Name: GenericNameSyntax generic } => SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier(simpleName),
                    generic.TypeArgumentList),
                _ => SyntaxFactory.IdentifierName(simpleName),
            };
    }



    static string DiagnosticCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "recompile-fail";
        var match = Regex.Match(detail, @"CS\d{4}");
        return match.Success ? match.Value : "recompile-fail";
    }

    static string FailureReason(ReturnToSender.Result result)
        => result.Status switch
        {
            FidelityCheck.CompileBackStatus.RecompileFail => DiagnosticCode(result.Detail),
            FidelityCheck.CompileBackStatus.ContextFail => string.IsNullOrWhiteSpace(result.Detail) ? "context-fail" : result.Detail,
            FidelityCheck.CompileBackStatus.OpcodeDiff => "opcode-diff",
            FidelityCheck.CompileBackStatus.OperandDiff => "operand-diff",
            FidelityCheck.CompileBackStatus.FidelityUnavailable => "fidelity-unavailable",
            _ => result.Status.ToString(),
        };
}
