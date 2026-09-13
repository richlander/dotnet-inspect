using ILInspector.Decompiler;

namespace ILInspector.DecompilerHarness;

static class MemorySafetyModeAdmission
{
    public static int RunAggregateReport(
        IReadOnlyList<string> assemblies,
        Func<int> run)
    {
        bool unavailable = false;
        foreach (string assembly in assemblies)
        {
            if (CompilerFeatureOptions.Resolve(assembly)
                is not CompilerFeatureOptions.Resolution.Unavailable resolution)
            {
                continue;
            }

            unavailable = true;
            HarnessLog.Status(
                $"{DiagnosticIds.MemorySafetyModeUnavailable}: "
                + $"{Path.GetFileName(assembly)}: {resolution.Reason}. "
                + "The aggregate report was not run because its decompiler "
                + "passes require a known memory-safety mode.");
        }

        return unavailable ? 1 : run();
    }
}
