namespace ILInspector.Analysis;

[Flags]
internal enum ImplementationMetricKind
{
    None = 0,
    BodySize = 1 << 0,
    InstructionShape = 1 << 1,
    ControlFlow = 1 << 2,
    ExceptionRegions = 1 << 3,
    Locals = 1 << 4,
    DirectCalls = 1 << 5,
    SiblingOverloadRelationships = 1 << 6,
    AllocationCount = 1 << 7,
    AllocationOccurrences = 1 << 8,
    ThrowCount = 1 << 9,
    UnsafePresence = 1 << 10,
    DirectReflectionCalls = 1 << 11,
    Async = 1 << 12,
    All = BodySize
        | InstructionShape
        | ControlFlow
        | ExceptionRegions
        | Locals
        | DirectCalls
        | SiblingOverloadRelationships
        | AllocationCount
        | AllocationOccurrences
        | ThrowCount
        | UnsafePresence
        | DirectReflectionCalls
        | Async,
}

[Flags]
internal enum ImplementationMetricFactKind
{
    None = 0,
    SourceAttribution = 1 << 0,
    ManagedBody = 1 << 1,
    LocalSignature = 1 << 2,
    CanonicalMethodContext = 1 << 3,
    DirectCalls = 1 << 4,
    AllocationSignals = 1 << 5,
    AllocationOccurrences = 1 << 6,
    BodySignals = 1 << 7,
    Safety = 1 << 8,
    All = SourceAttribution
        | ManagedBody
        | LocalSignature
        | CanonicalMethodContext
        | DirectCalls
        | AllocationSignals
        | AllocationOccurrences
        | BodySignals
        | Safety,
}

[Flags]
internal enum ImplementationMetricWorkStage
{
    None = 0,
    SourceAttribution = 1 << 0,
    SourceAttributionBodyProbe = 1 << 1,
    ManagedBodyAcquisition = 1 << 2,
    LocalSignatureDecode = 1 << 3,
    CanonicalMethodContext = 1 << 4,
    DirectCallCollection = 1 << 5,
    AllocationSignalCollection = 1 << 6,
    AllocationOccurrenceCollection = 1 << 7,
    BodySignalCollection = 1 << 8,
    SafetyCollection = 1 << 9,
    SiblingRelationshipProjection = 1 << 10,
}

internal enum ImplementationMetricRequestOrigin
{
    Explicit,
    CompleteProfileCompatibility,
    LegacyFeatureCompatibility,
}

internal sealed record ImplementationMetricWorkLimits
{
    internal ImplementationMetricWorkLimits(
        int maximumPhysicalBodies,
        long maximumEncodedIlBytes,
        int maximumAttributionProbeBodies,
        long maximumAttributionProbeIlBytes)
        : this(
            maximumPhysicalBodies,
            maximumEncodedIlBytes,
            maximumAttributionProbeBodies,
            maximumAttributionProbeIlBytes,
            isLegacyUnbounded: false)
    {
    }

    private ImplementationMetricWorkLimits(
        int maximumPhysicalBodies,
        long maximumEncodedIlBytes,
        int maximumAttributionProbeBodies,
        long maximumAttributionProbeIlBytes,
        bool isLegacyUnbounded)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumPhysicalBodies,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumEncodedIlBytes,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumAttributionProbeBodies,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            maximumAttributionProbeIlBytes,
            1);

        MaximumPhysicalBodies = maximumPhysicalBodies;
        MaximumEncodedIlBytes = maximumEncodedIlBytes;
        MaximumAttributionProbeBodies =
            maximumAttributionProbeBodies;
        MaximumAttributionProbeIlBytes =
            maximumAttributionProbeIlBytes;
        IsLegacyUnbounded = isLegacyUnbounded;
    }

    internal int MaximumPhysicalBodies { get; }

    internal long MaximumEncodedIlBytes { get; }

    internal int MaximumAttributionProbeBodies { get; }

    internal long MaximumAttributionProbeIlBytes { get; }

    internal bool IsLegacyUnbounded { get; }

    internal static ImplementationMetricWorkLimits LegacyUnbounded
    { get; } = new(
        int.MaxValue,
        long.MaxValue,
        int.MaxValue,
        long.MaxValue,
        isLegacyUnbounded: true);
}

internal sealed record ImplementationMetricAnalysisRequest(
    ImplementationMetricKind RequestedMetrics,
    ImplementationMetricWorkLimits Limits,
    ImplementationMetricRequestOrigin Origin)
{
    internal static ImplementationMetricAnalysisRequest
        CompleteProfileCompatibility() =>
        CompleteProfile(
            ImplementationMetricWorkLimits.LegacyUnbounded,
            ImplementationMetricRequestOrigin
                .CompleteProfileCompatibility);

    internal static ImplementationMetricAnalysisRequest
        LegacyFeatureCompatibility() =>
        CompleteProfile(
            ImplementationMetricWorkLimits.LegacyUnbounded,
            ImplementationMetricRequestOrigin
                .LegacyFeatureCompatibility);

    internal static ImplementationMetricAnalysisRequest CompleteProfile(
        ImplementationMetricWorkLimits limits,
        ImplementationMetricRequestOrigin origin =
            ImplementationMetricRequestOrigin.Explicit)
    {
        ArgumentNullException.ThrowIfNull(limits);
        return new(
            CompleteProfileV1,
            limits,
            origin);
    }

    internal static ImplementationMetricKind CompleteProfileV1 =>
        ImplementationMetricKind.BodySize
        | ImplementationMetricKind.InstructionShape
        | ImplementationMetricKind.ControlFlow
        | ImplementationMetricKind.ExceptionRegions
        | ImplementationMetricKind.Locals
        | ImplementationMetricKind.DirectCalls
        | ImplementationMetricKind.SiblingOverloadRelationships
        | ImplementationMetricKind.AllocationCount
        | ImplementationMetricKind.ThrowCount
        | ImplementationMetricKind.UnsafePresence
        | ImplementationMetricKind.DirectReflectionCalls
        | ImplementationMetricKind.Async;
}

internal sealed record ImplementationMetricAnalysisPlan(
    ImplementationMetricKind RequestedMetrics,
    ImplementationMetricFactKind RequiredFacts,
    ImplementationMetricWorkStage WorkStages,
    ImplementationMetricWorkLimits Limits,
    ImplementationMetricRequestOrigin Origin)
{
    internal bool UsesFocusedExecution =>
        (RequestedMetrics & ~FocusedMetrics)
            == ImplementationMetricKind.None;

    internal bool IncludesHeaderMetrics =>
        (RequestedMetrics & HeaderMetrics)
            != ImplementationMetricKind.None;

    internal bool IncludesLocalMetric =>
        RequestedMetrics.HasFlag(
            ImplementationMetricKind.Locals);

    internal bool IncludesInstructionShapeMetric =>
        RequestedMetrics.HasFlag(
            ImplementationMetricKind.InstructionShape);

    internal bool IncludesControlFlowMetric =>
        RequestedMetrics.HasFlag(
            ImplementationMetricKind.ControlFlow);

    internal bool IncludesDirectCallMetric =>
        RequestedMetrics.HasFlag(
            ImplementationMetricKind.DirectCalls);

    internal bool RequiresDirectCallFacts =>
        RequiredFacts.HasFlag(
            ImplementationMetricFactKind.DirectCalls);

    internal bool IncludesFocusedContextMetrics =>
        (RequestedMetrics & FocusedContextMetrics)
            != ImplementationMetricKind.None;

    internal bool RequiresCanonicalContext =>
        RequiredFacts.HasFlag(
            ImplementationMetricFactKind.CanonicalMethodContext);

    internal bool RequiresLocalSignatureDecode =>
        RequiredFacts.HasFlag(
            ImplementationMetricFactKind.LocalSignature);

    internal ImplementationMetricKind MetricCausesFor(
        ImplementationMetricWorkStage stage)
    {
        ImplementationMetricKind causes =
            stage switch
            {
                ImplementationMetricWorkStage.SourceAttribution
                    or ImplementationMetricWorkStage
                        .SourceAttributionBodyProbe =>
                    ImplementationMetricKind.All,
                ImplementationMetricWorkStage.ManagedBodyAcquisition =>
                    MetricsRequiringManagedBody,
                ImplementationMetricWorkStage.LocalSignatureDecode =>
                    MetricsRequiringLocalSignature,
                ImplementationMetricWorkStage.CanonicalMethodContext =>
                    MetricsRequiringContext,
                ImplementationMetricWorkStage.DirectCallCollection =>
                    MetricsRequiringCalls,
                ImplementationMetricWorkStage
                    .AllocationSignalCollection =>
                    ImplementationMetricKind.AllocationCount,
                ImplementationMetricWorkStage
                    .AllocationOccurrenceCollection =>
                    ImplementationMetricKind.AllocationOccurrences,
                ImplementationMetricWorkStage.BodySignalCollection =>
                    ImplementationMetricKind.ThrowCount,
                ImplementationMetricWorkStage.SafetyCollection =>
                    ImplementationMetricKind.UnsafePresence,
                ImplementationMetricWorkStage
                    .SiblingRelationshipProjection =>
                    ImplementationMetricKind
                        .SiblingOverloadRelationships,
                _ => ImplementationMetricKind.None,
            };
        return RequestedMetrics & causes;
    }

    internal static ImplementationMetricAnalysisPlan Create(
        ImplementationMetricAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ImplementationMetricKind requested =
            request.RequestedMetrics;
        if (requested == ImplementationMetricKind.None)
        {
            throw new ArgumentException(
                "Implementation metric request cannot be empty.",
                nameof(request));
        }
        if ((requested & ~ImplementationMetricKind.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Implementation metric request contains an unknown kind.");
        }
        if (request.Origin
                == ImplementationMetricRequestOrigin.Explicit
            && request.Limits.IsLegacyUnbounded)
        {
            throw new ArgumentException(
                "Explicit implementation metric requests require finite work limits.",
                nameof(request));
        }

        ImplementationMetricFactKind requiredFacts =
            NormalizeRequiredFacts(requested);
        return new(
            requested,
            requiredFacts,
            ComputeWorkStages(requested, requiredFacts),
            request.Limits,
            request.Origin);
    }

    static ImplementationMetricFactKind NormalizeRequiredFacts(
        ImplementationMetricKind metrics)
    {
        ImplementationMetricFactKind facts =
            ImplementationMetricFactKind.SourceAttribution;
        if ((metrics & MetricsRequiringManagedBody)
            != ImplementationMetricKind.None)
        {
            facts |= ImplementationMetricFactKind.ManagedBody;
        }
        if ((metrics & MetricsRequiringLocalSignature)
            != ImplementationMetricKind.None)
        {
            facts |=
                ImplementationMetricFactKind.LocalSignature;
        }
        if ((metrics & MetricsRequiringContext)
            != ImplementationMetricKind.None)
        {
            facts |=
                ImplementationMetricFactKind
                    .CanonicalMethodContext;
        }
        if ((metrics & MetricsRequiringCalls)
            != ImplementationMetricKind.None)
        {
            facts |= ImplementationMetricFactKind.DirectCalls;
        }
        if (metrics.HasFlag(
            ImplementationMetricKind.AllocationCount))
        {
            facts |=
                ImplementationMetricFactKind.AllocationSignals;
        }
        if (metrics.HasFlag(
            ImplementationMetricKind.AllocationOccurrences))
        {
            facts |=
                ImplementationMetricFactKind
                    .AllocationOccurrences;
        }
        if (metrics.HasFlag(
            ImplementationMetricKind.ThrowCount))
        {
            facts |= ImplementationMetricFactKind.BodySignals;
        }
        if (metrics.HasFlag(
            ImplementationMetricKind.UnsafePresence))
        {
            facts |= ImplementationMetricFactKind.Safety;
        }
        return facts;
    }

    static ImplementationMetricWorkStage ComputeWorkStages(
        ImplementationMetricKind metrics,
        ImplementationMetricFactKind facts)
    {
        ImplementationMetricWorkStage stages =
            ImplementationMetricWorkStage.SourceAttribution
            | ImplementationMetricWorkStage
                .SourceAttributionBodyProbe;

        if (facts.HasFlag(
            ImplementationMetricFactKind.ManagedBody))
        {
            stages |=
                ImplementationMetricWorkStage
                    .ManagedBodyAcquisition;
        }
        if (facts.HasFlag(
            ImplementationMetricFactKind.LocalSignature))
        {
            stages |=
                ImplementationMetricWorkStage.LocalSignatureDecode;
        }
        if (facts.HasFlag(
            ImplementationMetricFactKind.CanonicalMethodContext))
        {
            stages |=
                ImplementationMetricWorkStage
                    .CanonicalMethodContext;
        }
        if (facts.HasFlag(
            ImplementationMetricFactKind.DirectCalls))
        {
            stages |=
                ImplementationMetricWorkStage.DirectCallCollection;
        }
        if (facts.HasFlag(
            ImplementationMetricFactKind.AllocationSignals))
        {
            stages |=
                ImplementationMetricWorkStage
                    .AllocationSignalCollection;
        }
        if (facts.HasFlag(
            ImplementationMetricFactKind.AllocationOccurrences))
        {
            stages |=
                ImplementationMetricWorkStage
                    .AllocationOccurrenceCollection;
        }
        if (facts.HasFlag(
            ImplementationMetricFactKind.BodySignals))
        {
            stages |=
                ImplementationMetricWorkStage.BodySignalCollection;
        }
        if (facts.HasFlag(
            ImplementationMetricFactKind.Safety))
        {
            stages |=
                ImplementationMetricWorkStage.SafetyCollection;
        }
        if (metrics.HasFlag(
            ImplementationMetricKind
                .SiblingOverloadRelationships))
        {
            stages |=
                ImplementationMetricWorkStage
                    .SiblingRelationshipProjection;
        }
        return stages;
    }

    const ImplementationMetricKind HeaderMetrics =
        ImplementationMetricKind.BodySize
        | ImplementationMetricKind.ExceptionRegions;

    const ImplementationMetricKind FocusedMetrics =
        HeaderMetrics
        | ImplementationMetricKind.Locals
        | FocusedContextMetrics
        | ImplementationMetricKind.DirectCalls
        | ImplementationMetricKind
            .SiblingOverloadRelationships;

    const ImplementationMetricKind FocusedContextMetrics =
        ImplementationMetricKind.InstructionShape
        | ImplementationMetricKind.ControlFlow;

    const ImplementationMetricKind MetricsRequiringContext =
        FocusedContextMetrics
        | ImplementationMetricKind.DirectCalls
        | ImplementationMetricKind.AllocationCount
        | ImplementationMetricKind.AllocationOccurrences
        | ImplementationMetricKind.ThrowCount
        | ImplementationMetricKind.UnsafePresence
        | ImplementationMetricKind.DirectReflectionCalls
        | ImplementationMetricKind
            .SiblingOverloadRelationships;

    const ImplementationMetricKind MetricsRequiringCalls =
        ImplementationMetricKind.DirectCalls
        | ImplementationMetricKind
            .SiblingOverloadRelationships
        | ImplementationMetricKind.UnsafePresence
        | ImplementationMetricKind.DirectReflectionCalls;

    const ImplementationMetricKind MetricsRequiringLocalSignature =
        MetricsRequiringContext
        | ImplementationMetricKind.Locals;

    const ImplementationMetricKind MetricsRequiringManagedBody =
        ImplementationMetricKind.All
        & ~ImplementationMetricKind.Async;
}
