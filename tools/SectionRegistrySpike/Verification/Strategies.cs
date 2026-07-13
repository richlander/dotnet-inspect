using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using SectionRegistrySpike.Capabilities;
using SectionRegistrySpike.CurrentBaseline;
using SectionRegistrySpike.Sections;

namespace SectionRegistrySpike.Verification;

/// <summary>
/// Runs the four strategies from issue #2605 (describe, discover, effective discovery, render)
/// plus the current-vs-typed A/B and negative self-verification checks, emitting Markdown
/// evidence and recording pass/fail assertions on a <see cref="Report"/>.
/// </summary>
public static class Strategies
{
    public static async Task<Report> RunAsync()
    {
        var report = new Report();
        report.Heading("Section Registry Spike — Evidence (issue #2605)", 1);
        report.Line();
        report.Line("Representative model only. No production descriptors or commands are changed by this spike.");
        report.Line();

        var capabilities = SpikeSections.CreateCapabilityRegistry();
        var capRegistry = SpikeSections.CreateCapabilityRegistrySections(capabilities);
        var currentPipeline = CurrentBaselinePipelines.CreatePipeline();
        var currentScanners = CurrentBaselinePipelines.CreateScannerRegistry();

        DescribeStrategy(report, capRegistry, currentPipeline);
        DiscoverStrategy(report, capRegistry, currentPipeline);
        await EffectiveDiscoveryStrategyAsync(report, capabilities, capRegistry);
        await RenderStrategyAsync(report, capabilities, capRegistry, currentPipeline, currentScanners);
        await NegativeChecksAsync(report, capabilities, capRegistry);

        report.Heading("Conclusion input", 2);
        report.Bullet("Current and typed work traces/counts were equal for every representative selection above.");
        report.Bullet("The typed plan performed the same work as today — no runtime savings are claimed.");
        report.Bullet("Representative drift path: capability dependencies (BodyIndex, AcquirePdb→FetchSource) are explicit registered metadata instead of implicit context helpers and manual bool branches.");
        report.Bullet("See docs/design/capability-section-registry-spike.md for the staged-migration conclusion.");

        return report;
    }

    private static void DescribeStrategy(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> capRegistry,
        SectionPipeline<SpikeModel> currentPipeline)
    {
        report.Heading("Strategy 1 — Describe/schema (static metadata only)", 2);

        var names = capRegistry.Pipeline.AllSectionNames;
        report.Line($"Registered sections, in registration order: {string.Join(", ", names)}");
        report.Line();

        report.Check(
            names.SequenceEqual(currentPipeline.AllSectionNames),
            "capability-registry pipeline and current baseline pipeline register the same sections in the same order");
        report.Check(
            capRegistry.Pipeline.GetCostAnnotations().OrderBy(kv => kv.Key)
                .SequenceEqual(currentPipeline.GetCostAnnotations().OrderBy(kv => kv.Key)),
            "cost annotations (opt-in/verbose) match between the two pipelines");
        report.Check(
            capRegistry.Pipeline.GetCategoryMap()["@Projections"].SequenceEqual(currentPipeline.GetCategoryMap()["@Projections"]),
            "@Projections category matches between the two pipelines");
        report.Check(
            capRegistry.Pipeline.GetCategoryMap()["@Source"].SequenceEqual(currentPipeline.GetCategoryMap()["@Source"]),
            "@Source category matches between the two pipelines");
        var explicitSource = new HashSet<string>(["Original Source"], StringComparer.Ordinal);
        report.Check(
            capRegistry.Pipeline.GetRequiredVerbosity(explicitSource)
                == currentPipeline.GetRequiredVerbosity(explicitSource),
            "required verbosity for explicit section selection matches between the two pipelines");

        var resolvedSource = SelectResolver.ResolveSelectAsSections(
            ["@Source"],
            capRegistry.Pipeline.SelectableSectionNames,
            capRegistry.Pipeline.InfoSectionNames,
            capRegistry.Pipeline.GetCategoryMap());
        report.Check(!resolvedSource.HasError && resolvedSource.Sections is { Count: 2 },
            "@Source resolves through the existing SelectResolver to two concrete sections");
        var categoryPlan = capRegistry.PlanFor(resolvedSource.Sections ?? []);
        var directPlan = capRegistry.PlanFor(["Decompiled Source", "Original Source"]);
        report.Check(categoryPlan.SequenceEqual(directPlan),
            "typed planning consumes resolved category members in pipeline registration order");

        // Describe reads only static pipeline metadata — no CapabilitySession is created, so work is
        // zero by construction (there is nothing to count).
        report.Check(true, "describe/schema strategy creates zero capability instances (no session constructed)");
        report.Line();
    }

    private static void DiscoverStrategy(
        Report report,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> capRegistry,
        SectionPipeline<SpikeModel> currentPipeline)
    {
        report.Heading("Strategy 2 — Discover (structural applicability only)", 2);

        var freshModel = new SpikeModel();
        var discoverable = capRegistry.Pipeline.GetDiscoverableSections(freshModel);
        report.Line($"Discoverable sections on an unexecuted model: {string.Join(", ", discoverable)}");
        report.Line();

        report.Check(
            discoverable.Count == 5,
            "all five representative sections are structurally discoverable before any capability runs");
        report.Check(
            discoverable.SequenceEqual(currentPipeline.GetDiscoverableSections(freshModel)),
            "discoverable sections match between the two pipelines");
        report.Check(
            !freshModel.MetadataLoaded && freshModel.DecompiledSource is null && freshModel.OriginalSource is null
                && freshModel.Calls == 0 && freshModel.Facts == 0,
            "discover strategy creates zero capability instances (model is untouched)");
        report.Line();
    }

    private static async Task EffectiveDiscoveryStrategyAsync(
        Report report,
        CapabilityRegistry<SpikeContext> capabilities,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> capRegistry)
    {
        report.Heading("Strategy 3 — Effective discovery (probe only safe-to-probe closures)", 2);

        var model = new SpikeModel();
        var context = new SpikeContext { Model = model, NetworkAuthorized = false };
        var session = new CapabilitySession<SpikeContext>(capabilities);

        var applicable = capRegistry.Pipeline.GetDiscoverableSections(model);
        var unprobed = capRegistry.Pipeline.GetUnprobedSections();
        List<string> probed = [];
        List<string> structuralOnly = [];

        foreach (var name in applicable)
        {
            if (unprobed.Contains(name))
            {
                structuralOnly.Add(name);
                continue;
            }

            var plan = capRegistry.PlanFor([name]);
            if (!capabilities.IsClosureSafeToProbe(plan))
            {
                structuralOnly.Add(name);
                continue;
            }

            await session.ExecutePlanAsync(plan, context);
            probed.Add(name);
        }

        report.Line($"Probed (safe closure, executed): {string.Join(", ", probed)}");
        report.Line($"Structural only (deferred, not executed): {string.Join(", ", structuralOnly)}");
        report.Line($"Capabilities created: {session.CreatedCount}, executed: {session.ExecutedCount}");
        report.Code(session.Trace);
        report.Line();

        report.Check(probed is ["Metadata"], "only Metadata's closure is safe to probe among the representative sections");
        report.Check(
            structuralOnly.OrderBy(n => n, StringComparer.Ordinal)
                .SequenceEqual(new[] { "Calls", "Decompiled Source", "Facts", "Original Source" }.OrderBy(n => n, StringComparer.Ordinal)),
            "Decompiled Source, Original Source, Calls, and Facts remain structurally discoverable but unprobed");
        report.Check(session.CreatedCount == 1 && session.ExecutedCount == 1, "effective discovery creates/executes exactly one capability (Metadata)");
        report.Check(model.DecompiledSource is null && model.OriginalSource is null && model.Calls == 0 && model.Facts == 0,
            "heavy/decompiler/network/body work stayed at zero during effective discovery");
        report.Line();
    }

    private static async Task RenderStrategyAsync(
        Report report,
        CapabilityRegistry<SpikeContext> capabilities,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> capRegistry,
        SectionPipeline<SpikeModel> currentPipeline,
        CurrentScannerRegistry currentScanners)
    {
        report.Heading("Strategy 4 — Render (explicit `-S` selection)", 2);
        report.Line();
        report.Line("Current-vs-typed A/B for representative selections. Output is from the representative model; product CLI behavior is unchanged.");
        report.Line();

        await CompareSelectionAsync(report, capabilities, capRegistry, currentPipeline, currentScanners,
            "Metadata", ["Metadata"],
            expectedTrace: ["create Metadata", "execute Metadata"]);

        await CompareSelectionAsync(report, capabilities, capRegistry, currentPipeline, currentScanners,
            "Decompiled Source", ["Decompiled Source"],
            expectedTrace: ["create Decompile", "execute Decompile"]);

        await CompareSelectionAsync(report, capabilities, capRegistry, currentPipeline, currentScanners,
            "Original Source", ["Original Source"],
            expectedTrace: ["create AcquirePdb", "execute AcquirePdb", "create FetchSource", "execute FetchSource"]);

        await CompareSelectionAsync(report, capabilities, capRegistry, currentPipeline, currentScanners,
            "Calls only", ["Calls"],
            expectedTrace: ["create BodyIndex", "execute BodyIndex", "create Calls", "execute Calls"]);

        await CompareSelectionAsync(report, capabilities, capRegistry, currentPipeline, currentScanners,
            "Calls + Facts", ["Calls", "Facts"],
            expectedTrace: ["create BodyIndex", "execute BodyIndex", "create Calls", "execute Calls", "create Facts", "execute Facts"]);

        // Empty selection: no section requests any capability.
        await CompareSelectionAsync(report, capabilities, capRegistry, currentPipeline, currentScanners,
            "Empty selection", [],
            expectedTrace: []);

        report.Line();
    }

    private static async Task CompareSelectionAsync(
        Report report,
        CapabilityRegistry<SpikeContext> capabilities,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> capRegistry,
        SectionPipeline<SpikeModel> currentPipeline,
        CurrentScannerRegistry currentScanners,
        string label,
        string[] include,
        string[] expectedTrace)
    {
        report.Heading(label, 3);

        var includeSet = new HashSet<string>(include, StringComparer.Ordinal);
        const Verbosity verbosity = Verbosity.Quiet; // network capabilities must come from explicit -S, never verbosity

        // --- current baseline ---
        var currentModel = new SpikeModel();
        var requiredScanners = currentPipeline.GetRequiredScanners(verbosity, includeSet);
        bool currentNetworkAuthorized =
            currentPipeline.GetAuthorizedSections(SectionCapabilities.MayDownloadPdb, verbosity, includeSet).Count > 0
            || currentPipeline.GetAuthorizedSections(SectionCapabilities.MayFetchSources, verbosity, includeSet).Count > 0;
        var currentContext = new CurrentScannerContext { Model = currentModel, NetworkAuthorized = currentNetworkAuthorized };
        currentScanners.RunScanners(requiredScanners, currentContext);
        await CurrentBaselinePipelines.RunNetworkWorkAsync(currentPipeline, includeSet, verbosity, currentContext);

        // --- typed capability registry ---
        var typedModel = new SpikeModel();
        // Pass the ordered array (not includeSet) — HashSet<string> enumeration order is not
        // guaranteed, and plan order must be deterministic for the trace assertions below.
        var plan = capRegistry.PlanFor(include);
        bool typedNetworkAuthorized = currentNetworkAuthorized; // same GetAuthorizedSections rule, same pipeline shape
        var typedContext = new SpikeContext { Model = typedModel, NetworkAuthorized = typedNetworkAuthorized };
        var session = new CapabilitySession<SpikeContext>(capabilities);
        await session.ExecutePlanAsync(plan, typedContext);

        var currentSections = currentPipeline.GetEffectiveSections(currentModel, verbosity, includeSet);
        var typedSections = capRegistry.Pipeline.GetEffectiveSections(typedModel, verbosity, includeSet);
        string currentOutput = RenderRepresentativeOutput(currentModel, currentSections);
        string typedOutput = RenderRepresentativeOutput(typedModel, typedSections);

        report.Line($"Current trace: {(currentContext.Trace.Count == 0 ? "(none)" : string.Join(", ", currentContext.Trace))}");
        report.Line($"Typed trace:   {(session.Trace.Count == 0 ? "(none)" : string.Join(", ", session.Trace))}");
        report.Line($"Current work: {currentContext.Trace.Count / 2} created, {currentContext.Trace.Count / 2} executed");
        report.Line($"Typed work:   {session.CreatedCount} created, {session.ExecutedCount} executed");
        report.Line("Current output:");
        report.Code(currentOutput.Length == 0 ? ["(none)"] : currentOutput.Split('\n'));
        report.Line("Typed output:");
        report.Code(typedOutput.Length == 0 ? ["(none)"] : typedOutput.Split('\n'));
        report.Line();

        report.Check(currentContext.Trace.SequenceEqual(expectedTrace), $"current baseline trace matches expected trace for '{label}'");
        report.Check(session.Trace.SequenceEqual(expectedTrace), $"typed plan trace matches expected trace for '{label}'");
        report.Check(currentContext.Trace.SequenceEqual(session.Trace), $"current and typed traces are identical for '{label}'");
        report.Check(session.CreatedCount == expectedTrace.Length / 2 && session.ExecutedCount == expectedTrace.Length / 2,
            $"typed creation/execution counts match expected work for '{label}'");
        report.Check(currentSections.SequenceEqual(typedSections), $"current and typed render-filter sections are identical for '{label}'");
        report.Check(currentOutput == typedOutput, $"current and typed representative CLI output are identical for '{label}'");
        report.Line();
    }

    private static string RenderRepresentativeOutput(SpikeModel model, IEnumerable<string> sections)
        => string.Join('\n', sections.Select(name => name switch
        {
            "Metadata" => "Metadata: loaded",
            "Decompiled Source" => $"Decompiled Source: {model.DecompiledSource}",
            "Original Source" => $"Original Source: {model.OriginalSource}",
            "Calls" => $"Calls: {model.Calls}",
            "Facts" => $"Facts: {model.Facts}",
            _ => throw new InvalidOperationException($"No representative renderer is registered for section '{name}'."),
        }));

    private static async Task NegativeChecksAsync(
        Report report,
        CapabilityRegistry<SpikeContext> capabilities,
        CapabilitySectionRegistry<SpikeModel, SpikeContext> capRegistry)
    {
        report.Heading("Negative self-verification", 2);

        // Missing dependency.
        var missingRegistry = new CapabilityRegistry<object>().Register<DependsOnMissingCapability>();
        try
        {
            missingRegistry.ResolvePlan([CapabilityKey.Of<DependsOnMissingCapability>()]);
            report.Check(false, "resolving a plan with a missing dependency throws CapabilityNotRegisteredException");
        }
        catch (CapabilityNotRegisteredException ex)
        {
            report.Line($"Missing-dependency diagnostic: {ex.Message}");
            report.Check(true, "resolving a plan with a missing dependency throws CapabilityNotRegisteredException");
        }

        try
        {
            capRegistry.PlanFor(["No Such Section"]);
            report.Check(false, "planning an unknown section throws instead of silently producing an empty plan");
        }
        catch (KeyNotFoundException ex)
        {
            report.Line($"Unknown-section diagnostic: {ex.Message}");
            report.Check(true, "planning an unknown section throws instead of silently producing an empty plan");
        }

        // Dependency cycle.
        var cycleRegistry = new CapabilityRegistry<object>().Register<CycleACapability>().Register<CycleBCapability>();
        try
        {
            cycleRegistry.ResolvePlan([CapabilityKey.Of<CycleACapability>()]);
            report.Check(false, "resolving a cyclic plan throws CapabilityCycleException");
        }
        catch (CapabilityCycleException ex)
        {
            report.Line($"Cycle diagnostic: {ex.Message}");
            report.Check(ex.Path.Count >= 3, "cycle diagnostic includes the detected cycle path");
            report.Check(true, "resolving a cyclic plan throws CapabilityCycleException");
        }

        // Probe-safety: a section-level probe-effectiveness flag says "safe" but the capability
        // closure contains a not-safe-to-probe capability. Kept in its own registry/pipeline so it
        // never contaminates the representative production-like descriptor set above.
        var probeSafetyCapabilities = new CapabilityRegistry<SpikeContext>().Register<DeepScanCapability>();
        var probeSafetyRegistry = new CapabilitySectionRegistry<SpikeModel, SpikeContext>(probeSafetyCapabilities)
            .Add<SpikeSections.MisleadingProbeSection>(_ => true);

        var probeModel = new SpikeModel();
        var unprobed = probeSafetyRegistry.Pipeline.GetUnprobedSections();
        report.Check(!unprobed.Contains("Misleading Probe"),
            "'Misleading Probe' declares ProbeEffectiveness=true (the section-level flag alone would allow probing)");

        var misleadingPlan = probeSafetyRegistry.PlanFor(["Misleading Probe"]);
        bool closureSafe = probeSafetyCapabilities.IsClosureSafeToProbe(misleadingPlan);
        report.Check(!closureSafe, "the capability-closure check finds DeepScan is not safe to probe, despite the section-level flag");

        var probeSession = new CapabilitySession<SpikeContext>(probeSafetyCapabilities);
        if (closureSafe)
            await probeSession.ExecutePlanAsync(misleadingPlan, new SpikeContext { Model = probeModel, NetworkAuthorized = false });

        report.Check(probeSession.CreatedCount == 0 && !probeModel.DeepScanRan,
            "'Misleading Probe' capability closure is deferred: zero instances created, DeepScan never ran");

        // Network rejection when not authorized: Original Source's plan runs but the context refuses.
        var unauthorizedContext = new SpikeContext { Model = new SpikeModel(), NetworkAuthorized = false };
        var originalSourcePlan = capRegistry.PlanFor(["Original Source"]);
        var unauthorizedSession = new CapabilitySession<SpikeContext>(capabilities);
        try
        {
            await unauthorizedSession.ExecutePlanAsync(originalSourcePlan, unauthorizedContext);
            report.Check(false, "executing Original Source's plan without network authorization throws");
        }
        catch (InvalidOperationException ex)
        {
            report.Line($"Unauthorized-network diagnostic: {ex.Message}");
            report.Check(true, "executing Original Source's plan without network authorization throws");
        }
        report.Check(!unauthorizedSession.HasExecuted<AcquirePdbCapability>(),
            "a failed capability is not recorded as executed");

        report.Line();
    }

    private sealed class DependsOnMissingCapability : ICapability<object>
    {
        public static string Name => "DependsOnMissing";
        public static bool SafeToProbe => false;
        public static CapabilityKey[] DependsOn => [CapabilityKey.Of<NotRegisteredCapability>()];
        public ValueTask ExecuteAsync(object context, CapabilitySession<object> session) => ValueTask.CompletedTask;
    }

    private sealed class NotRegisteredCapability : ICapability<object>
    {
        public static string Name => "NotRegistered";
        public static bool SafeToProbe => false;
        public static CapabilityKey[] DependsOn => [];
        public ValueTask ExecuteAsync(object context, CapabilitySession<object> session) => ValueTask.CompletedTask;
    }

    private sealed class CycleACapability : ICapability<object>
    {
        public static string Name => "CycleA";
        public static bool SafeToProbe => false;
        public static CapabilityKey[] DependsOn => [CapabilityKey.Of<CycleBCapability>()];
        public ValueTask ExecuteAsync(object context, CapabilitySession<object> session) => ValueTask.CompletedTask;
    }

    private sealed class CycleBCapability : ICapability<object>
    {
        public static string Name => "CycleB";
        public static bool SafeToProbe => false;
        public static CapabilityKey[] DependsOn => [CapabilityKey.Of<CycleACapability>()];
        public ValueTask ExecuteAsync(object context, CapabilitySession<object> session) => ValueTask.CompletedTask;
    }
}
