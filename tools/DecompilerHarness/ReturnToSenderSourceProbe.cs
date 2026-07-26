using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

using DotnetInspector.Fixtures;
using DotnetInspector.HarnessReports;
using ILInspector.Decompiler;
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
    ReturnToSender.FaultIsolationMethod? FaultIsolationMethod = null)
{
    public bool Passed => Outcome == ReturnToSenderSourceOutcome.ValidMatch;
    public bool Different => Outcome == ReturnToSenderSourceOutcome.ValidDifferent;
    public bool Failed => Outcome == ReturnToSenderSourceOutcome.Invalid;
    public bool Skipped => Outcome is ReturnToSenderSourceOutcome.SourceUnavailable or ReturnToSenderSourceOutcome.UnsupportedTarget;
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

        return result.FaultIsolationKind switch
        {
            ReturnToSender.FaultIsolationKind.BodyDefect => ReturnToSenderInvalidKind.ProductBodyDefect,
            ReturnToSender.FaultIsolationKind.ShellOrClosureDefect => ReturnToSenderInvalidKind.HarnessShellReconstruction,
            _ when HasClosureStopDetail(result.Detail) => ReturnToSenderInvalidKind.HarnessShellReconstruction,
            _ => ReturnToSenderInvalidKind.Unclassified,
        };
    }

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
    internal static readonly HarnessReportDescriptor Descriptor = new("return-to-sender.source-correspondence", 1);

    internal sealed record ProbeTarget(ReturnToSender.RequestedTarget Target, IReadOnlyList<string> ExpectedFragments);

    public static int Run(
        IReadOnlyList<string> assemblies,
        int cap,
        int maxExamples,
        bool json,
        string? emitHarnessReport = null)
    {
        var results = Evaluate(assemblies, cap);
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
            return failed == 0 ? 0 : 1;
        }

        Console.WriteLine($"RETURNTOSENDER SOURCE PROBE over {results.Count} target(s)");
        Console.WriteLine();
        Console.WriteLine($"  ValidMatch       : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch)}");
        Console.WriteLine($"  ValidDifferent   : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent)}");
        Console.WriteLine($"  Invalid          : {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid)}");
        Console.WriteLine($"  SourceUnavailable: {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable)}");
        Console.WriteLine($"  UnsupportedTarget: {results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget)}");
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

        return failed == 0 ? 0 : 1;
    }

    internal static DecompilerHarnessReport<IReadOnlyList<ReturnToSenderSourceProbeResult>> BuildReport(
        IReadOnlyList<ReturnToSenderSourceProbeResult> results)
    {
        string population = HarnessPopulationKey.Create(
            "return-to-sender.source-correspondence",
            results.Select(result =>
                $"{result.Target.Type}|{result.Target.Method}|{result.Target.Overload}|{result.Target.Signature}"));
        int total = results.Count;

        return new DecompilerHarnessReport<IReadOnlyList<ReturnToSenderSourceProbeResult>>(
            Descriptor,
            results,
            new HarnessComparisonProjection(
                "RTS compile-back and authored-source correspondence remain independent outcome lanes.",
                population,
                [
                    new("valid-match", "Valid match", MetricGoal.Higher, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidMatch), total), population),
                    new("valid-different", "Valid different", MetricGoal.Context, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.ValidDifferent), total), population),
                    new("invalid", "Invalid / RTS compile-back failed", MetricGoal.Lower, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.Invalid), total), population),
                    new("source-unavailable", "Source unavailable", MetricGoal.Context, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.SourceUnavailable), total), population),
                    new("unsupported-target", "Unsupported target", MetricGoal.Lower, new MetricValue(results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget), total), population),
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
                results.Add(new ReturnToSenderSourceProbeResult(
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
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.Invalid,
                    result.Status,
                    FailureReason(result),
                    result.Detail,
                    SourcePath: sourceMember?.SourcePath,
                    ExpectedBody: sourceMember?.Body,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor,
                    FaultIsolationKind: result.FaultIsolation?.Kind,
                    FaultIsolationMethod: result.FaultIsolation?.Method));
                continue;
            }

            if (result.Status is not (
                FidelityCheck.CompileBackStatus.Exact
                or FidelityCheck.CompileBackStatus.OpcodeDiff
                or FidelityCheck.CompileBackStatus.OperandDiff))
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    FailureReason(result),
                    result.Detail,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor,
                    FaultIsolationKind: result.FaultIsolation?.Kind,
                    FaultIsolationMethod: result.FaultIsolation?.Method));
                continue;
            }

            if (sourceIndex is null)
            {
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    "fixture-source-unavailable",
                    sourceUnavailableDetail,
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor,
                    FaultIsolationKind: result.FaultIsolation?.Kind,
                    FaultIsolationMethod: result.FaultIsolation?.Method));
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

                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.SourceUnavailable,
                    result.Status,
                    "source-slice-unavailable",
                    $"no source member matched {target.Type}::{target.Method}#{target.Overload}",
                    SourcePath: null,
                    ExpectedBody: null,
                    ActualBody: result.TargetBody,
                    MemberAnchor: result.MemberAnchor,
                    FaultIsolationKind: result.FaultIsolation?.Kind,
                    FaultIsolationMethod: result.FaultIsolation?.Method));
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
                results.Add(new ReturnToSenderSourceProbeResult(
                    target,
                    ReturnToSenderSourceOutcome.ValidMatch,
                    result.Status,
                    "valid_match",
                    Detail: null,
                    sourceMember.SourcePath,
                    expected,
                    actual,
                    MemberAnchor: result.MemberAnchor,
                    FaultIsolationKind: result.FaultIsolation?.Kind,
                    FaultIsolationMethod: result.FaultIsolation?.Method));
                continue;
            }

            string reason = ClassifyValidDifference(
                expected,
                actual,
                result.Status,
                result.Decisions ?? [],
                out var classificationDetail);
            var fidelityEvidence = FidelityEvidence(result);
            results.Add(new ReturnToSenderSourceProbeResult(
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
                FaultIsolationKind: result.FaultIsolation?.Kind,
                    FaultIsolationMethod: result.FaultIsolation?.Method));
        }

        return results;
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
                unsupported_target = results.Count(result => result.Outcome == ReturnToSenderSourceOutcome.UnsupportedTarget),
                passed,
                different,
                failed,
                skipped,
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
                original_opcodes = result.OriginalOpcodes,
                recompiled_opcodes = result.RecompiledOpcodes,
                il_diff = result.IlDiffLines,
            }),
        };
        Console.WriteLine(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void AddBodylessSourceResult(
        List<ReturnToSenderSourceProbeResult> results,
        ReturnToSender.RequestedTarget target,
        ReturnToSender.Result result,
        string sourcePath)
    {
        var exact = result.Status == FidelityCheck.CompileBackStatus.Exact;
        results.Add(new ReturnToSenderSourceProbeResult(
            target,
            exact ? ReturnToSenderSourceOutcome.ValidMatch : ReturnToSenderSourceOutcome.Invalid,
            result.Status,
            exact ? "valid_match.source_bodyless" : "invalid.source_bodyless_non_exact",
            $"source member matched {target.Type}::{target.Method}#{target.Overload}, but it has no explicit source body; compile-back status is {result.Status}",
            sourcePath,
            ExpectedBody: null,
            ActualBody: result.TargetBody,
            MemberAnchor: result.MemberAnchor,
            FaultIsolationKind: result.FaultIsolation?.Kind,
                    FaultIsolationMethod: result.FaultIsolation?.Method));
    }

}

internal sealed record ReturnToSenderSourceMember(string Type, string Method, int Overload, string Signature, string SourcePath, string? Body);

internal sealed class ReturnToSenderSourceIndex
{
    readonly Dictionary<string, ReturnToSenderSourceMember> _members;
    readonly Dictionary<string, ReturnToSenderSourceMember> _membersBySignature;
    readonly HashSet<string> _ambiguousSignatures;
    readonly Dictionary<string, RecordSourceInfo> _recordSources;

    ReturnToSenderSourceIndex(
        Dictionary<string, ReturnToSenderSourceMember> members,
        Dictionary<string, ReturnToSenderSourceMember> membersBySignature,
        HashSet<string> ambiguousSignatures,
        Dictionary<string, RecordSourceInfo> recordSources)
    {
        _members = members;
        _membersBySignature = membersBySignature;
        _ambiguousSignatures = ambiguousSignatures;
        _recordSources = recordSources;
    }

    public static ReturnToSenderSourceIndex? TryCreate(IReadOnlyList<string> sourcePaths)
    {
        var members = new Dictionary<string, ReturnToSenderSourceMember>(StringComparer.Ordinal);
        var membersBySignature = new Dictionary<string, ReturnToSenderSourceMember>(StringComparer.Ordinal);
        var ambiguousSignatures = new HashSet<string>(StringComparer.Ordinal);
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
            AddSourceFile(members, membersBySignature, ambiguousSignatures, recordSources, overloads, sourceFile.Path, sourceFile.Root, sourceIdentity);

        return new ReturnToSenderSourceIndex(members, membersBySignature, ambiguousSignatures, recordSources);
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
    /// Builds an index directly from pre-snapshotted authored members (the vendored
    /// authored-source corpus). Keys are derived exactly as <see cref="AddSourceFile"/>
    /// does, so lookups from a <see cref="ReturnToSender.RequestedTarget"/> resolve
    /// by signature when unambiguous and otherwise by (type, method, overload).
    /// Members with no signature are indexed by overload key only.
    /// </summary>
    public static ReturnToSenderSourceIndex FromMembers(IEnumerable<ReturnToSenderSourceMember> sourceMembers)
    {
        var members = new Dictionary<string, ReturnToSenderSourceMember>(StringComparer.Ordinal);
        var membersBySignature = new Dictionary<string, ReturnToSenderSourceMember>(StringComparer.Ordinal);
        var ambiguousSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in sourceMembers)
        {
            members.TryAdd(Key(member.Type, member.Method, member.Overload), member);

            if (string.IsNullOrEmpty(member.Signature))
                continue;

            var signatureKey = SigKey(member.Type, member.Method, member.Signature);
            if (ambiguousSignatures.Contains(signatureKey))
                continue;
            if (!membersBySignature.TryAdd(signatureKey, member))
            {
                membersBySignature.Remove(signatureKey);
                ambiguousSignatures.Add(signatureKey);
            }
        }

        return new ReturnToSenderSourceIndex(
            members,
            membersBySignature,
            ambiguousSignatures,
            new Dictionary<string, RecordSourceInfo>(StringComparer.Ordinal));
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
        if (target.Signature is { } signature)
        {
            var signatureKey = SigKey(target.Type, target.Method, signature);
            if (!_ambiguousSignatures.Contains(signatureKey)
                && _membersBySignature.TryGetValue(signatureKey, out member!))
            {
                return true;
            }
        }

        return _members.TryGetValue(Key(target.Type, target.Method, target.Overload), out member!);
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
        Dictionary<string, ReturnToSenderSourceMember> membersBySignature,
        HashSet<string> ambiguousSignatures,
        Dictionary<string, RecordSourceInfo> recordSources,
        Dictionary<string, Dictionary<string, int>> overloads,
        string sourcePath,
        CompilationUnitSyntax root,
        CSharpSourceIdentityContext sourceIdentity)
    {
        foreach (var member in SourceMembers(root, sourcePath, recordSources, overloads, sourceIdentity))
        {
            members.TryAdd(Key(member.Type, member.Method, member.Overload), member);

            var signatureKey = SigKey(member.Type, member.Method, member.Signature);
            if (ambiguousSignatures.Contains(signatureKey))
                continue;
            if (!membersBySignature.TryAdd(signatureKey, member))
            {
                membersBySignature.Remove(signatureKey);
                ambiguousSignatures.Add(signatureKey);
            }
        }
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static string SigKey(string type, string method, string signature) => $"{type}::{method}{signature}";

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
                yield return new ReturnToSenderSourceMember(fullType, sourceMember.MetadataName, overload, sourceMember.Signature, path, sourceMember.Body);
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
                targets.Add(new ProbeTarget(new ReturnToSender.RequestedTarget(fullType, methodName, overload, signature), fragments.Distinct(StringComparer.Ordinal).ToArray()));
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
                targets.Add(new ProbeTarget(new ReturnToSender.RequestedTarget(fullType, methodName, overload, signature), fragments.Distinct(StringComparer.Ordinal).ToArray()));
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

    static int OverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string methodName)
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

    static string SigKey(string type, string method, string signature) => $"{type}::{method}{signature}";

    // Only carry a signature identity that unambiguously round-trips to this exact
    // metadata member. A lossy or ambiguous normalized signature is dropped so both
    // metadata resolution and source correlation fall back to the ordinal.
    static string? UniqueTargetSignature(
        MetadataReader reader,
        TypeDefinition typeDef,
        string methodName,
        MethodDefinitionHandle handle)
        => SignatureIdentity.ForMetadataMethod(reader, typeDef, handle) is { } signature
            && ReturnToSender.ResolvesUniquelyBySignature(reader, typeDef, methodName, signature, handle)
                ? signature
                : null;

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
