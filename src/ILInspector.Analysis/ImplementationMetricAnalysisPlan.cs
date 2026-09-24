namespace ILInspector.Analysis;

[Flags]
internal enum ImplementationMetricEvidenceKind
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
    }

    internal int MaximumPhysicalBodies { get; }

    internal long MaximumEncodedIlBytes { get; }

    internal int MaximumAttributionProbeBodies { get; }

    internal long MaximumAttributionProbeIlBytes { get; }

    internal bool IsLegacyUnbounded =>
        this == LegacyUnbounded;

    internal static ImplementationMetricWorkLimits LegacyUnbounded
    { get; } = new(
        int.MaxValue,
        long.MaxValue,
        int.MaxValue,
        long.MaxValue);
}

internal sealed record ImplementationMetricAnalysisRequest(
    ImplementationMetricEvidenceKind RequestedEvidence,
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

    internal static ImplementationMetricEvidenceKind CompleteProfileV1 =>
        ImplementationMetricEvidenceKind.BodySize
        | ImplementationMetricEvidenceKind.InstructionShape
        | ImplementationMetricEvidenceKind.ControlFlow
        | ImplementationMetricEvidenceKind.ExceptionRegions
        | ImplementationMetricEvidenceKind.Locals
        | ImplementationMetricEvidenceKind.DirectCalls
        | ImplementationMetricEvidenceKind.SiblingOverloadRelationships
        | ImplementationMetricEvidenceKind.AllocationCount
        | ImplementationMetricEvidenceKind.ThrowCount
        | ImplementationMetricEvidenceKind.UnsafePresence
        | ImplementationMetricEvidenceKind.DirectReflectionCalls
        | ImplementationMetricEvidenceKind.Async;
}

internal sealed record ImplementationMetricAnalysisPlan(
    ImplementationMetricEvidenceKind RequestedEvidence,
    ImplementationMetricEvidenceKind EffectiveEvidence,
    ImplementationMetricWorkStage WorkStages,
    ImplementationMetricWorkLimits Limits,
    ImplementationMetricRequestOrigin Origin)
{
    internal static ImplementationMetricAnalysisPlan Create(
        ImplementationMetricAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ImplementationMetricEvidenceKind requested =
            request.RequestedEvidence;
        if (requested == ImplementationMetricEvidenceKind.None)
        {
            throw new ArgumentException(
                "Implementation metric evidence cannot be empty.",
                nameof(request));
        }
        if ((requested & ~ImplementationMetricEvidenceKind.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Implementation metric evidence contains an unknown kind.");
        }

        ImplementationMetricEvidenceKind effective = requested;
        if (effective.HasFlag(
                ImplementationMetricEvidenceKind
                    .SiblingOverloadRelationships))
        {
            effective |=
                ImplementationMetricEvidenceKind.DirectCalls;
        }

        return new(
            requested,
            effective,
            ComputeWorkStages(effective),
            request.Limits,
            request.Origin);
    }

    static ImplementationMetricWorkStage ComputeWorkStages(
        ImplementationMetricEvidenceKind evidence)
    {
        ImplementationMetricWorkStage stages =
            ImplementationMetricWorkStage.SourceAttribution
            | ImplementationMetricWorkStage
                .SourceAttributionBodyProbe;

        if ((evidence & BodyEvidence) != 0)
        {
            stages |=
                ImplementationMetricWorkStage
                    .ManagedBodyAcquisition;
        }
        if ((evidence & ContextEvidence) != 0)
        {
            stages |=
                ImplementationMetricWorkStage.LocalSignatureDecode
                | ImplementationMetricWorkStage
                    .CanonicalMethodContext;
        }
        else if (evidence.HasFlag(
            ImplementationMetricEvidenceKind.Locals))
        {
            stages |=
                ImplementationMetricWorkStage.LocalSignatureDecode;
        }
        if ((evidence & CallEvidence) != 0)
        {
            stages |=
                ImplementationMetricWorkStage.DirectCallCollection;
        }
        if (evidence.HasFlag(
            ImplementationMetricEvidenceKind.AllocationCount))
        {
            stages |=
                ImplementationMetricWorkStage
                    .AllocationSignalCollection;
        }
        if (evidence.HasFlag(
            ImplementationMetricEvidenceKind.AllocationOccurrences))
        {
            stages |=
                ImplementationMetricWorkStage
                    .AllocationOccurrenceCollection;
        }
        if (evidence.HasFlag(
            ImplementationMetricEvidenceKind.ThrowCount))
        {
            stages |=
                ImplementationMetricWorkStage.BodySignalCollection;
        }
        if (evidence.HasFlag(
            ImplementationMetricEvidenceKind.UnsafePresence))
        {
            stages |=
                ImplementationMetricWorkStage.SafetyCollection;
        }
        if (evidence.HasFlag(
            ImplementationMetricEvidenceKind
                .SiblingOverloadRelationships))
        {
            stages |=
                ImplementationMetricWorkStage
                    .SiblingRelationshipProjection;
        }
        return stages;
    }

    const ImplementationMetricEvidenceKind BodyEvidence =
        ImplementationMetricEvidenceKind.All
        & ~ImplementationMetricEvidenceKind.Async;

    const ImplementationMetricEvidenceKind ContextEvidence =
        ImplementationMetricEvidenceKind.InstructionShape
        | ImplementationMetricEvidenceKind.ControlFlow
        | ImplementationMetricEvidenceKind.DirectCalls
        | ImplementationMetricEvidenceKind.AllocationCount
        | ImplementationMetricEvidenceKind.AllocationOccurrences
        | ImplementationMetricEvidenceKind.ThrowCount
        | ImplementationMetricEvidenceKind.UnsafePresence
        | ImplementationMetricEvidenceKind.DirectReflectionCalls;

    const ImplementationMetricEvidenceKind CallEvidence =
        ImplementationMetricEvidenceKind.DirectCalls
        | ImplementationMetricEvidenceKind
            .SiblingOverloadRelationships
        | ImplementationMetricEvidenceKind.UnsafePresence
        | ImplementationMetricEvidenceKind.DirectReflectionCalls;
}
