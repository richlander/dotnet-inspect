namespace ILInspector.Analysis;

internal enum ImplementationMetricWorkLimitKind
{
    PhysicalBodies,
    EncodedIlBytes,
    AttributionProbeBodies,
    AttributionProbeIlBytes,
}

internal sealed record ImplementationMetricWorkBudgetSnapshot(
    ImplementationMetricWorkLimits Limits,
    int AttributionProbeBodies,
    long AttributionProbeIlBytes,
    int MetricBodies,
    long MetricIlBytes,
    ImplementationMetricWorkLimitKind? AttributionExhaustedLimit,
    int? AttributionExhaustedMethodToken,
    ImplementationMetricWorkLimitKind? MetricExhaustedLimit,
    int? MetricExhaustedMethodToken);

internal sealed class ImplementationMetricWorkLimitExceededException(
    ImplementationMetricWorkLimitKind limit,
    int methodToken)
    : InvalidOperationException(MessageFor(limit))
{
    internal ImplementationMetricWorkLimitKind Limit { get; } = limit;

    internal int MethodToken { get; } = methodToken;

    internal static string MessageFor(
        ImplementationMetricWorkLimitKind limit) =>
        limit switch
        {
            ImplementationMetricWorkLimitKind.PhysicalBodies =>
                "Implementation metric physical-body limit was exhausted.",
            ImplementationMetricWorkLimitKind.EncodedIlBytes =>
                "Implementation metric encoded-IL-byte limit was exhausted.",
            ImplementationMetricWorkLimitKind.AttributionProbeBodies =>
                "Implementation metric attribution-probe body limit was exhausted.",
            ImplementationMetricWorkLimitKind.AttributionProbeIlBytes =>
                "Implementation metric attribution-probe encoded-IL-byte limit was exhausted.",
            _ => throw new ArgumentOutOfRangeException(nameof(limit)),
        };
}

/// <summary>
/// Monotonic execution budget. Exhausting one dimension rejects the
/// deterministic remainder of that work category.
/// </summary>
internal sealed class ImplementationMetricWorkBudget
{
    readonly object _gate = new();
    readonly ImplementationMetricWorkLimits _limits;
    int _attributionProbeBodies;
    long _attributionProbeIlBytes;
    int _metricBodies;
    long _metricIlBytes;
    ImplementationMetricWorkLimitKind? _attributionExhaustedLimit;
    int? _attributionExhaustedMethodToken;
    ImplementationMetricWorkLimitKind? _metricExhaustedLimit;
    int? _metricExhaustedMethodToken;

    ImplementationMetricWorkBudget(
        ImplementationMetricWorkLimits limits)
    {
        _limits = limits;
    }

    internal static ImplementationMetricWorkBudget? Create(
        ImplementationMetricAnalysisPlan? plan) =>
        plan is null || plan.Limits.IsLegacyUnbounded
            ? null
            : new(plan.Limits);

    internal void ReserveAttributionProbeBody(
        int methodToken)
    {
        lock (_gate)
        {
            ThrowIfAttributionExhausted(methodToken);
            if (_attributionProbeBodies
                >= _limits.MaximumAttributionProbeBodies)
            {
                _attributionExhaustedLimit =
                    ImplementationMetricWorkLimitKind
                        .AttributionProbeBodies;
                _attributionExhaustedMethodToken =
                    methodToken;
                ThrowIfAttributionExhausted(methodToken);
            }
            _attributionProbeBodies++;
        }
    }

    internal void ReserveAttributionProbeIlBytes(
        int methodToken,
        int encodedIlBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            encodedIlBytes);
        lock (_gate)
        {
            ThrowIfAttributionExhausted(methodToken);
            if (encodedIlBytes
                > _limits.MaximumAttributionProbeIlBytes
                    - _attributionProbeIlBytes)
            {
                _attributionExhaustedLimit =
                    ImplementationMetricWorkLimitKind
                        .AttributionProbeIlBytes;
                _attributionExhaustedMethodToken =
                    methodToken;
                ThrowIfAttributionExhausted(methodToken);
            }
            _attributionProbeIlBytes += encodedIlBytes;
        }
    }

    internal void AdmitMetricBody(
        int methodToken,
        int encodedIlBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            encodedIlBytes);
        lock (_gate)
        {
            ThrowIfMetricExhausted(methodToken);
            if (_metricBodies
                >= _limits.MaximumPhysicalBodies)
            {
                _metricExhaustedLimit =
                    ImplementationMetricWorkLimitKind
                        .PhysicalBodies;
                _metricExhaustedMethodToken =
                    methodToken;
                ThrowIfMetricExhausted(methodToken);
            }
            if (encodedIlBytes
                > _limits.MaximumEncodedIlBytes
                    - _metricIlBytes)
            {
                _metricExhaustedLimit =
                    ImplementationMetricWorkLimitKind
                        .EncodedIlBytes;
                _metricExhaustedMethodToken =
                    methodToken;
                ThrowIfMetricExhausted(methodToken);
            }
            _metricBodies++;
            _metricIlBytes += encodedIlBytes;
        }
    }

    internal ImplementationMetricWorkBudgetSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new(
                _limits,
                _attributionProbeBodies,
                _attributionProbeIlBytes,
                _metricBodies,
                _metricIlBytes,
                _attributionExhaustedLimit,
                _attributionExhaustedMethodToken,
                _metricExhaustedLimit,
                _metricExhaustedMethodToken);
        }
    }

    void ThrowIfAttributionExhausted(
        int methodToken)
    {
        if (_attributionExhaustedLimit is { } limit)
        {
            throw new ImplementationMetricWorkLimitExceededException(
                limit,
                methodToken);
        }
    }

    void ThrowIfMetricExhausted(
        int methodToken)
    {
        if (_metricExhaustedLimit is { } limit)
        {
            throw new ImplementationMetricWorkLimitExceededException(
                limit,
                methodToken);
        }
    }
}
