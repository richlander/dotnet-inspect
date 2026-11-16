namespace CiChangeDetection;

internal static class DeepInspectHealthTestSuite
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaximumAge = TimeSpan.FromHours(36);

    internal static void Run()
    {
        AssertHealthyRunPasses();
        AssertIrrelevantScheduleDoesNotHideHealth();
        AssertNewestFailureWins();
        AssertCancellationFails();
        AssertMissingJobFails();
        AssertStaleRunFails();
        AssertMissingRunFails();
        AssertManualRecoveryClearsFailure();
        AssertFutureCompletionFails();
    }

    private static void AssertHealthyRunPasses()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(1, "schedule", Now.AddHours(-4), "success", "success"));
        AssertOutcome(decision, true, DeepInspectHealthOutcome.Healthy);
    }

    private static void AssertIrrelevantScheduleDoesNotHideHealth()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(2, "schedule", Now.AddHours(-1), "skipped", "skipped"),
            Run(1, "schedule", Now.AddHours(-6), "success", "success"));
        AssertOutcome(decision, true, DeepInspectHealthOutcome.Healthy);
    }

    private static void AssertNewestFailureWins()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(2, "schedule", Now.AddHours(-1), "failure", "success"),
            Run(1, "schedule", Now.AddHours(-6), "success", "success"));
        AssertOutcome(decision, false, DeepInspectHealthOutcome.NonGreen);
    }

    private static void AssertCancellationFails()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(1, "schedule", Now.AddHours(-4), "success", "cancelled"));
        AssertOutcome(decision, false, DeepInspectHealthOutcome.NonGreen);
    }

    private static void AssertMissingJobFails()
    {
        DeepInspectHealthRun run = Run(
            1,
            "schedule",
            Now.AddHours(-4),
            "success",
            "success");
        DeepInspectHealthDecision decision = Evaluate(
            run with { Jobs = [run.Jobs[0]] });
        AssertOutcome(decision, false, DeepInspectHealthOutcome.Incomplete);
    }

    private static void AssertStaleRunFails()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(1, "schedule", Now.AddHours(-37), "success", "success"));
        AssertOutcome(decision, false, DeepInspectHealthOutcome.Stale);
    }

    private static void AssertMissingRunFails()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(1, "schedule", Now.AddHours(-1), "skipped", "skipped"));
        AssertOutcome(decision, false, DeepInspectHealthOutcome.Missing);
    }

    private static void AssertManualRecoveryClearsFailure()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(
                2,
                "workflow_dispatch",
                Now.AddMinutes(-30),
                "success",
                "success"),
            Run(1, "schedule", Now.AddHours(-4), "failure", "failure"));
        AssertOutcome(decision, true, DeepInspectHealthOutcome.Healthy);
    }

    private static void AssertFutureCompletionFails()
    {
        DeepInspectHealthDecision decision = Evaluate(
            Run(1, "schedule", Now.AddMinutes(6), "success", "success"));
        AssertOutcome(decision, false, DeepInspectHealthOutcome.Future);
    }

    private static DeepInspectHealthDecision Evaluate(
        params DeepInspectHealthRun[] runs) =>
        DeepInspectHealthPolicy.Evaluate(runs, Now, MaximumAge);

    private static DeepInspectHealthRun Run(
        long id,
        string eventName,
        DateTimeOffset completedAt,
        string testConclusion,
        string decompilerConclusion) =>
        new(
            id,
            eventName,
            "main",
            $"{id:x40}",
            completedAt,
            $"https://github.test/actions/runs/{id}",
            [
                new DeepInspectHealthJob("Test lane", testConclusion),
                new DeepInspectHealthJob(
                    "Decompiler test lane",
                    decompilerConclusion),
            ]);

    private static void AssertOutcome(
        DeepInspectHealthDecision decision,
        bool admitted,
        DeepInspectHealthOutcome outcome)
    {
        if (decision.Admitted != admitted || decision.Outcome != outcome)
        {
            throw new InvalidOperationException(
                $"Expected admitted={admitted}, outcome={outcome}; got "
                + $"admitted={decision.Admitted}, outcome={decision.Outcome}: "
                + decision.Message);
        }
    }
}
