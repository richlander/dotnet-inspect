using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal enum SignatureOccurrenceMetric
{
    SignatureNodes,
    OccurrenceCopies,
    TypeNameCharacters,
    TypeReferenceNameChainNodes,
    TypeReferenceScopeChainNodes,
    TypeDefinitionChainNodes,
    TypeSpecificationBytes,
    ArrayShapeSizes,
    ArrayShapeLowerBounds,
    AssemblyReferenceNameBytes,
    AssemblyReferenceCultureBytes,
    AssemblyReferenceTokenBytes,
    AssemblyReferenceFullKeyBytes,
    ModuleReferenceNameBytes,
}

internal readonly record struct SignatureOccurrenceMeasurement(
    long Count,
    long Total,
    int LargestCharge);

/// <summary>
/// Optional per-decode evidence, using the production enforcement path. Charges
/// include the first refused attempt; budget usage includes only accepted charges.
/// Array observations come from the guard, including a refused count, separately
/// from the provider node budget.
/// </summary>
internal sealed class SignatureOccurrenceMetrics
{
    readonly SignatureOccurrenceMeasurement[] _measurements =
        new SignatureOccurrenceMeasurement[Enum.GetValues<SignatureOccurrenceMetric>().Length];

    internal int Nodes { get; private set; }
    internal int Copies { get; private set; }
    internal int Work { get; private set; }

    internal SignatureOccurrenceMeasurement this[SignatureOccurrenceMetric metric] =>
        _measurements[(int)metric];

    internal void Observe(SignatureOccurrenceMetric metric, int amount)
        => Observe(metric, new SignatureOccurrenceMeasurement(1, amount, amount));

    internal void Observe(SignatureOccurrenceMetric metric, SignatureOccurrenceMeasurement observed)
    {
        ref var measurement = ref _measurements[(int)metric];
        measurement = new(
            measurement.Count + observed.Count,
            measurement.Total + observed.Total,
            Math.Max(measurement.LargestCharge, observed.LargestCharge));
    }

    internal void SetUsage(int nodes, int copies, int work)
    {
        Nodes = nodes;
        Copies = copies;
        Work = work;
    }
}

internal readonly record struct SignatureOccurrenceLimits(
    int Nodes = MetadataSafetyPolicy.MaxSignatureTypeNodes,
    int Copies = MetadataSafetyPolicy.MaxSignatureTypeNodes * 8,
    int Work = MetadataSafetyPolicy.MaxTypeNameCharacters * 64)
{
    internal static SignatureOccurrenceLimits Default { get; } = new(
        MetadataSafetyPolicy.MaxSignatureTypeNodes,
        MetadataSafetyPolicy.MaxSignatureTypeNodes * 8,
        MetadataSafetyPolicy.MaxTypeNameCharacters * 64);

    internal void Validate()
    {
        if (Nodes < 0 || Nodes > Default.Nodes
            || Copies < 0 || Copies > Default.Copies
            || Work < 0 || Work > Default.Work)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SignatureOccurrenceLimits),
                "Test limits may only reduce the production ceilings.");
        }
    }
}

internal sealed class SignatureOccurrenceWorkBudget(
    SignatureOccurrenceLimits limits,
    SignatureOccurrenceMetrics? metrics)
{
    int _nodes;
    int _copies;
    int _work;

    internal void Node() =>
        Charge(SignatureOccurrenceMetric.SignatureNodes, 1, ref _nodes,
            limits.Nodes, SignatureOccurrenceRejectionReason.NodeBudget);

    internal void Copies(int amount) =>
        Charge(SignatureOccurrenceMetric.OccurrenceCopies, amount, ref _copies,
            limits.Copies, SignatureOccurrenceRejectionReason.OccurrenceCopyBudget);

    internal void Work(SignatureOccurrenceMetric metric, int amount) =>
        Charge(metric, amount, ref _work, limits.Work,
            SignatureOccurrenceRejectionReason.WorkBudget);

    internal void ObserveGuard(SignatureBlobGuardMeasurements measurements)
    {
        metrics?.Observe(SignatureOccurrenceMetric.ArrayShapeSizes,
            new SignatureOccurrenceMeasurement(
                measurements.Sizes.Count, measurements.Sizes.Total, measurements.Sizes.Largest));
        metrics?.Observe(SignatureOccurrenceMetric.ArrayShapeLowerBounds,
            new SignatureOccurrenceMeasurement(
                measurements.LowerBounds.Count, measurements.LowerBounds.Total, measurements.LowerBounds.Largest));
    }

    internal void TypeNameCharacters(int amount) =>
        Work(SignatureOccurrenceMetric.TypeNameCharacters, amount);

    internal void TypeReferenceNameChain(int amount) =>
        Work(SignatureOccurrenceMetric.TypeReferenceNameChainNodes, amount);

    internal void TypeDefinitionChain(int amount) =>
        Work(SignatureOccurrenceMetric.TypeDefinitionChainNodes, amount);

    void Charge(
        SignatureOccurrenceMetric metric,
        int amount,
        ref int used,
        int ceiling,
        SignatureOccurrenceRejectionReason rejection)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        metrics?.Observe(metric, amount);
        if (amount > ceiling - used)
            throw new SignatureOccurrenceRejectedException(rejection);
        used += amount;
        metrics?.SetUsage(_nodes, _copies, _work);
    }
}

internal sealed class SignatureOccurrenceRejectedException(
    SignatureOccurrenceRejectionReason reason) : Exception
{
    internal SignatureOccurrenceRejectionReason Reason { get; } = reason;
}
