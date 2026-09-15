using System.Collections.Immutable;

namespace ILInspector.Metadata;

/// <summary>An opaque static interpretation, invoked only by Integration operations.</summary>
public sealed class EcosystemIntegrationScannerBinding
{
    readonly Func<
        EcosystemIntegrationObservationContext,
        ImmutableArray<EcosystemIntegrationClassification>> _scan;

    EcosystemIntegrationScannerBinding(
        Func<EcosystemIntegrationObservationContext,
            ImmutableArray<EcosystemIntegrationClassification>> scan)
        => _scan = scan;

    public static EcosystemIntegrationScannerBinding Create(
        Func<EcosystemIntegrationObservationContext,
            ImmutableArray<EcosystemIntegrationClassification>> scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (scan.Target is not null || scan.GetInvocationList().Length != 1)
        {
            throw new ArgumentException(
                "An Integration scanner must be exactly one target-free static method group.",
                nameof(scan));
        }

        return new EcosystemIntegrationScannerBinding(scan);
    }

    internal ImmutableArray<EcosystemIntegrationClassification> Scan(
        EcosystemIntegrationObservationContext context)
    {
        ImmutableArray<EcosystemIntegrationClassification> classifications =
            _scan(context);
        if (classifications.IsDefault)
        {
            throw new InvalidOperationException(
                "The Integration scanner returned an uninitialized classification sequence.");
        }

        return classifications;
    }
}
