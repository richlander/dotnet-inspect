using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using SectionRegistrySpike.Capabilities;
using SectionRegistrySpike.CurrentBaseline;
using SectionRegistrySpike.Sections;

namespace SectionRegistrySpike.Verification;

public static class Strategies
{
    public static async Task<Report> RunAsync()
    {
        var report = new Report();
        report.Heading("Section Registry Spike - Evidence (issue #2605)", 1);
        report.Line();

        var typed = SpikeSections.Registry;
        var currentPipeline = CurrentBaselinePipelines.CreatePipeline();
        var currentScanners = CurrentBaselinePipelines.CreateScannerRegistry();

        Describe(report, typed, currentPipeline);
        Discover(report, typed, currentPipeline);
        await EffectiveDiscoveryAsync(report, typed);
        await RenderAsync(report, typed, currentPipeline, currentScanners);
        await NegativeChecksAsync(report, typed);

        report.Heading("Code-quality result", 2);
        report.Bullet("Each operation has one noncapturing execution lambda in the static table.");
        report.Bullet("Probe safety and operation-specific authorization are derived from the compiled plan.");
        report.Bullet("One static table holds noncapturing execution, applicability, and rendering lambdas.");
        report.Bullet("Dependency order plus single-section/category plans are precompiled by selection mask.");
        report.Bullet("The real SectionPipeline still owns names, categories, verbosity, -D/-S, and render filtering.");
        report.Line();

        return report;
    }

    private static void Describe(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> typed,
        SectionPipeline<SpikeModel> current)
    {
        report.Heading("Strategy 1 - Describe/schema", 2);
        report.Check(typed.Pipeline.AllSectionNames.SequenceEqual(current.AllSectionNames),
            "registration order matches the current pipeline");
        report.Check(
            typed.Pipeline.GetCostAnnotations().OrderBy(pair => pair.Key)
                .SequenceEqual(current.GetCostAnnotations().OrderBy(pair => pair.Key)),
            "cost annotations match the current pipeline");
        report.Check(typed.Pipeline.GetCategoryMap()["@Source"].SequenceEqual(current.GetCategoryMap()["@Source"]),
            "categories remain representable");

        var resolved = SelectResolver.ResolveSelectAsSections(
            ["@Source"],
            typed.Pipeline.SelectableSectionNames,
            typed.Pipeline.InfoSectionNames,
            typed.Pipeline.GetCategoryMap());
        report.Check(!resolved.HasError && resolved.Sections is { Count: 2 },
            "existing SelectResolver expands categories before capability planning");
        report.Check(typed.PlanFor(resolved.Sections!).HasSameEntries(
                typed.PlanFor(["Decompiled Source", "Original Source"])),
            "category and direct selection compile to the same ordered plan");

        var mergeRegistry = new CapabilitySectionRegistry<SpikeModel, SpikeContext>(
            [
                new("Later", false, true, false, static _ => true, static _ => true,
                    new CapabilityPlan<SpikeContext>(
                        new CapabilityPlanEntry<SpikeContext>(
                            2, "Later", CapabilityExecutionModes.All, static _ => ValueTask.CompletedTask))),
                new("Earlier", false, true, false, static _ => true, static _ => true,
                    new CapabilityPlan<SpikeContext>(
                        new CapabilityPlanEntry<SpikeContext>(
                            1, "Earlier", CapabilityExecutionModes.All, static _ => ValueTask.CompletedTask))),
            ],
            [],
            static _ => null);
        report.Check(
            mergeRegistry.PlanFor(["Later", "Earlier"]).HasSameEntries(
                new CapabilityPlan<SpikeContext>(
                    new CapabilityPlanEntry<SpikeContext>(
                        1, "Earlier", CapabilityExecutionModes.All, static _ => ValueTask.CompletedTask),
                    new CapabilityPlanEntry<SpikeContext>(
                        2, "Later", CapabilityExecutionModes.All, static _ => ValueTask.CompletedTask))),
            "cold arbitrary plans preserve generated topological order");
        report.Line();
    }

    private static void Discover(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> typed,
        SectionPipeline<SpikeModel> current)
    {
        report.Heading("Strategy 2 - Structural discovery", 2);
        var model = new SpikeModel();
        var names = typed.Pipeline.GetDiscoverableSections(model);
        report.Check(names.SequenceEqual(current.GetDiscoverableSections(model)),
            "discovery matches current output on an unexecuted model");
        report.Check(!model.MetadataLoaded && model.OriginalSource is null && model.Calls == 0,
            "structural discovery executes no work");
        report.Line();
    }

    private static async Task EffectiveDiscoveryAsync(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> typed)
    {
        report.Heading("Strategy 3 - Effective discovery", 2);
        var model = new SpikeModel();
        var context = new SpikeContext { Model = model };
        List<string> trace = [];
        List<string> probed = [];
        List<string> deferred = [];

        foreach (var name in typed.Pipeline.GetDiscoverableSections(model))
        {
            var plan = typed.PlanFor([name]);
            if (!plan.CanExecute(CapabilityExecutionModes.Probe))
            {
                deferred.Add(name);
                continue;
            }

            await plan.ExecuteAsync(context, CapabilityExecutionModes.Probe, trace.Add);
            probed.Add(name);
        }

        report.Check(probed is ["Metadata"], "only the cheap metadata closure is probe-authorized");
        report.Check(deferred.Count == 4, "decompiler, network, and body-index work remains deferred");
        report.Check(context.WorkCount == 1 && trace is ["execute Metadata"],
            "effective discovery executes exactly one static capability");
        report.Line();
    }

    private static async Task RenderAsync(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> typed,
        SectionPipeline<SpikeModel> currentPipeline,
        CurrentScannerRegistry currentScanners)
    {
        report.Heading("Strategy 4 - Render A/B", 2);

        await CompareAsync(report, typed, currentPipeline, currentScanners,
            "Metadata", ["Metadata"], ["execute Metadata"]);
        await CompareAsync(report, typed, currentPipeline, currentScanners,
            "Decompiled Source", ["Decompiled Source"], ["execute Decompile"]);
        await CompareAsync(report, typed, currentPipeline, currentScanners,
            "Original Source", ["Original Source"], ["execute AcquirePdb", "execute FetchSource"]);
        await CompareAsync(report, typed, currentPipeline, currentScanners,
            "Calls", ["Calls"], ["execute BodyIndex", "execute Calls"]);
        await CompareAsync(report, typed, currentPipeline, currentScanners,
            "Calls + Facts", ["Calls", "Facts"],
            ["execute BodyIndex", "execute Calls", "execute Facts"]);
        await CompareAsync(report, typed, currentPipeline, currentScanners,
            "Metadata + Facts", ["Metadata", "Facts"],
            ["execute Metadata", "execute BodyIndex", "execute Facts"]);
        await CompareAsync(report, typed, currentPipeline, currentScanners,
            "Empty", [], []);
        report.Line();
    }

    private static async Task CompareAsync(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> typed,
        SectionPipeline<SpikeModel> currentPipeline,
        CurrentScannerRegistry currentScanners,
        string label,
        string[] selected,
        string[] expectedTrace)
    {
        var include = new HashSet<string>(selected, StringComparer.Ordinal);
        const Verbosity verbosity = Verbosity.Quiet;

        var currentModel = new SpikeModel();
        List<string> currentTrace = [];
        var currentContext = new CurrentScannerContext { Model = currentModel, Trace = currentTrace.Add };
        currentScanners.RunScanners(currentPipeline.GetRequiredScanners(verbosity, include), currentContext);
        await CurrentBaselinePipelines.RunNetworkWorkAsync(
            currentPipeline, include, verbosity, currentContext);

        var typedModel = new SpikeModel();
        List<string> typedTrace = [];
        var typedContext = new SpikeContext { Model = typedModel };
        var plan = typed.PlanFor(selected);
        await plan.ExecuteAsync(typedContext, CapabilityExecutionModes.Explicit, typedTrace.Add);

        var currentSections = currentPipeline.GetEffectiveSections(currentModel, verbosity, include);
        var typedSections = typed.Pipeline.GetEffectiveSections(typedModel, verbosity, include);
        string currentOutput = Render(currentModel, currentSections);
        string typedOutput = Render(typedModel, typedSections);

        report.Check(currentTrace.SequenceEqual(expectedTrace), $"{label}: current trace is expected");
        report.Check(typedTrace.SequenceEqual(expectedTrace), $"{label}: static-plan trace is expected");
        report.Check(currentContext.WorkCount == typedContext.WorkCount,
            $"{label}: actual work counts match ({typedContext.WorkCount})");
        report.Check(currentOutput == typedOutput, $"{label}: representative output matches");
    }

    private static string Render(SpikeModel model, IEnumerable<string> sections)
        => string.Join('\n', sections.Select(name => name switch
        {
            "Metadata" => "Metadata: loaded",
            "Decompiled Source" => $"Decompiled Source: {model.DecompiledSource}",
            "Original Source" => $"Original Source: {model.OriginalSource}",
            "Calls" => $"Calls: {model.Calls}",
            "Facts" => $"Facts: {model.Facts}",
            _ => throw new InvalidOperationException($"No renderer for '{name}'."),
        }));

    private static async Task NegativeChecksAsync(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> typed)
    {
        report.Heading("Negative verification", 2);

        var duplicateEntry = new CapabilityPlanEntry<object>(
            0,
            "Duplicate",
            CapabilityExecutionModes.Explicit,
            static _ => ValueTask.CompletedTask);
        try
        {
            _ = new CapabilityPlan<object>(duplicateEntry, duplicateEntry);
            report.Check(false, "duplicate static plan entries are rejected");
        }
        catch (InvalidOperationException)
        {
            report.Check(true, "duplicate static plan entries are rejected");
        }

        try
        {
            _ = new CapabilitySectionRegistry<SpikeModel, SpikeContext>(
                [
                    new("Duplicate", false, false, false, static _ => true, static _ => true,
                        new CapabilityPlan<SpikeContext>()),
                    new("Duplicate", false, false, false, static _ => true, static _ => true,
                        new CapabilityPlan<SpikeContext>()),
                ],
                [],
                static _ => null);
            report.Check(false, "duplicate static section names are rejected");
        }
        catch (InvalidOperationException)
        {
            report.Check(true, "duplicate static section names are rejected");
        }

        var probeRegistry = SpikeSections.CreateProbeTestRegistry();
        report.Check(probeRegistry.Pipeline.GetUnprobedSections().Contains("Misleading Probe"),
            "SectionPipeline probe metadata is derived from the capability plan");

        var originalSource = typed.PlanFor(["Original Source"]);
        var unauthorized = new SpikeContext { Model = new SpikeModel() };
        try
        {
            await originalSource.ExecuteAsync(unauthorized, CapabilityExecutionModes.Detailed);
            report.Check(false, "source-body work is rejected outside explicit selection");
        }
        catch (CapabilityNotAuthorizedException)
        {
            report.Check(!unauthorized.Model.PdbAcquired && unauthorized.WorkCount == 0,
                "authorization is preflighted before a prerequisite mutates context");
        }

        var detailed = new SpikeContext { Model = new SpikeModel() };
        await SpikeSections.PdbPlan.ExecuteAsync(detailed, CapabilityExecutionModes.Detailed);
        report.Check(detailed.Model.PdbAcquired,
            "PDB acquisition remains authorized at detailed verbosity without authorizing source bodies");

        var retryPlan = new CapabilityPlan<FailureContext>(
            new CapabilityPlanEntry<FailureContext>(
                0,
                "FailOnce",
                CapabilityExecutionModes.Explicit,
                static context =>
                {
                    context.Attempts++;
                    if (context.Attempts == 1)
                        throw new InvalidOperationException("representative partial failure");
                    return ValueTask.CompletedTask;
                }));
        var retryContext = new FailureContext();
        try
        {
            await retryPlan.ExecuteAsync(retryContext, CapabilityExecutionModes.Explicit);
        }
        catch (InvalidOperationException)
        {
            // Expected first attempt.
        }
        await retryPlan.ExecuteAsync(retryContext, CapabilityExecutionModes.Explicit);
        report.Check(retryContext.Attempts == 2,
            "failed execution leaves no cached capability instance or poisoned session state");
        report.Line();
    }

    private sealed class FailureContext
    {
        public int Attempts { get; set; }
    }
}
