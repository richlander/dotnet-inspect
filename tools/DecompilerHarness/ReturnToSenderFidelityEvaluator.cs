namespace ILInspector.DecompilerHarness;

static class ReturnToSenderFidelityEvaluator
{
    internal sealed record Evaluation(
        IReadOnlyList<FidelityCheck.CompileBackResult> Results,
        int CompileBackFloorAppliedMethods);

    internal static Task<Evaluation> EvaluateAsync(
        string assemblyPath,
        IReadOnlyList<FidelityCheck.CompileBackTarget> selectedTargets,
        string captureDetail,
        FidelityCheck.CaptureMode capture = FidelityCheck.CaptureMode.WholeModule)
        => EvaluateAsync(
            assemblyPath,
            selectedTargets,
            captureDetail,
            capture,
            () => ReturnToSender.CompileBackTargets(
                assemblyPath,
                selectedTargets
                    .Select(target => new ReturnToSender.RequestedTarget(
                        target.Type,
                        target.Method,
                        target.Overload,
                        target.Signature,
                        target.Address))
                    .ToArray(),
                applyCompileBackFloor: false));

    internal static async Task<Evaluation> EvaluateAsync(
        string assemblyPath,
        IReadOnlyList<FidelityCheck.CompileBackTarget> selectedTargets,
        string captureDetail,
        FidelityCheck.CaptureMode capture,
        Func<Task<IReadOnlyList<ReturnToSender.Result>>> evaluate)
    {
        try
        {
            var returnToSenderResults = (await evaluate()).ToArray();
            return new Evaluation(
                Align(selectedTargets, returnToSenderResults, captureDetail, capture),
                returnToSenderResults.Count(result => result.UsedCompileBackFloor));
        }
        catch (Exception ex) when (
            ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            HarnessLog.Status($"RTS unavailable {PortablePath(assemblyPath)}: {ex.Message}");
            return new Evaluation(
                selectedTargets
                    .Select(target => new FidelityCheck.CompileBackResult(
                        target.Type,
                        target.Method,
                        target.Overload,
                        target.Signature,
                        FidelityCheck.CompileBackStatus.ContextFail,
                        "",
                        "",
                        $"return-to-sender-context-unavailable: {ex.Message}",
                        capture,
                        captureDetail))
                    .ToArray(),
                CompileBackFloorAppliedMethods: 0);
        }
    }

    internal static IReadOnlyList<FidelityCheck.CompileBackResult> Align(
        IReadOnlyList<FidelityCheck.CompileBackTarget> selectedTargets,
        IReadOnlyList<ReturnToSender.Result> returnToSenderResults,
        string captureDetail,
        FidelityCheck.CaptureMode capture = FidelityCheck.CaptureMode.WholeModule)
    {
        var resultsByTarget = returnToSenderResults
            .GroupBy(ReturnToSenderKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var results = new FidelityCheck.CompileBackResult[selectedTargets.Count];
        for (int index = 0; index < selectedTargets.Count; index++)
        {
            var target = selectedTargets[index];
            if (!resultsByTarget.TryGetValue(CompileBackTargetKey(target), out var result))
            {
                results[index] = new FidelityCheck.CompileBackResult(
                    target.Type,
                    target.Method,
                    target.Overload,
                    target.Signature,
                    FidelityCheck.CompileBackStatus.ContextFail,
                    "",
                    "",
                    "return-to-sender-target-unavailable",
                    capture,
                    captureDetail);
                continue;
            }

            results[index] = new FidelityCheck.CompileBackResult(
                target.Type,
                target.Method,
                target.Overload,
                target.Signature,
                result.Status,
                result.OriginalOpcodes,
                result.RecompiledOpcodes,
                result.Detail,
                result.CompileBackFloor?.Capture ?? capture,
                result.UsedCompileBackFloor ? $"{captureDetail}; compile-back-floor" : captureDetail,
                result.FidelityDiff);
        }

        return results;
    }

    static string CompileBackTargetKey(FidelityCheck.CompileBackTarget target)
        => target.Address is { } address
            ? $"address:{address.ModuleVersionId:D}:{address.Token:X8}"
            : $"identity:{target.Type}::{target.Method}::{target.Overload}::{target.Signature}";

    static string ReturnToSenderKey(ReturnToSender.Result result)
        => result.TargetAddress is { } address
            ? $"address:{address.ModuleVersionId:D}:{address.Token:X8}"
            : $"identity:{result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}::{result.Plan.TargetMethod.Overload}::{result.Plan.TargetMethod.Signature}";

    static string PortablePath(string path)
        => Path.GetFullPath(path).Replace('\\', '/');
}
