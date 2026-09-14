namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Closed terminal PlatformHouse outcome. Cancellation remains
/// <see cref="OperationCanceledException"/>.
/// </summary>
public abstract class PlatformHouseOutcome<TValue>
    where TValue : notnull
{
    private protected PlatformHouseOutcome(
        PlatformHouseReceipt receipt,
        PlatformHouseSettlementKind expectedKind)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.SettlementKind != expectedKind)
        {
            throw new ArgumentException(
                $"The receipt must represent a {expectedKind} settlement.",
                nameof(receipt));
        }
        Receipt = receipt;
    }

    public PlatformHouseReceipt Receipt { get; }

    public sealed class Completed : PlatformHouseOutcome<TValue>
    {
        internal Completed(
            PlatformHouseCompletedValue<TValue> value,
            PlatformHouseReceipt receipt)
            : base(receipt, PlatformHouseSettlementKind.Completed)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (receipt.Completion is null
                || !ReferenceEquals(
                    receipt.Completion.Identity,
                    value.Identity))
            {
                throw new ArgumentException(
                    "The completed value must be bound to the receipt's completion identity.",
                    nameof(value));
            }
            Value = value.Value;
        }

        public TValue Value { get; }
    }

    public sealed class Unavailable : PlatformHouseOutcome<TValue>
    {
        internal Unavailable(
            PlatformHouseTermination.Unavailable evidence,
            PlatformHouseReceipt receipt)
            : base(receipt, PlatformHouseSettlementKind.Unavailable)
        {
            if (!ReferenceEquals(receipt.Termination, evidence))
                throw new ArgumentException(
                    "Unavailable evidence must be the receipt's exact termination.",
                    nameof(evidence));
            Evidence = evidence;
        }

        public PlatformHouseTermination.Unavailable Evidence { get; }
    }

    public sealed class Ambiguous : PlatformHouseOutcome<TValue>
    {
        internal Ambiguous(
            PlatformHouseTermination.Ambiguous evidence,
            PlatformHouseReceipt receipt)
            : base(receipt, PlatformHouseSettlementKind.Ambiguous)
        {
            if (!ReferenceEquals(receipt.Termination, evidence))
                throw new ArgumentException(
                    "Ambiguity evidence must be the receipt's exact termination.",
                    nameof(evidence));
            Evidence = evidence;
        }

        public PlatformHouseTermination.Ambiguous Evidence { get; }
    }

    public sealed class Rejected : PlatformHouseOutcome<TValue>
    {
        internal Rejected(
            PlatformHouseTermination.Rejected evidence,
            PlatformHouseReceipt receipt)
            : base(receipt, PlatformHouseSettlementKind.Rejected)
        {
            if (!ReferenceEquals(receipt.Termination, evidence))
                throw new ArgumentException(
                    "Rejection evidence must be the receipt's exact termination.",
                    nameof(evidence));
            Evidence = evidence;
        }

        public PlatformHouseTermination.Rejected Evidence { get; }
    }

    public sealed class Incomplete : PlatformHouseOutcome<TValue>
    {
        internal Incomplete(
            PlatformHouseTermination.Incomplete evidence,
            PlatformHouseReceipt receipt)
            : base(receipt, PlatformHouseSettlementKind.Incomplete)
        {
            if (!ReferenceEquals(receipt.Termination, evidence))
                throw new ArgumentException(
                    "Incomplete evidence must be the receipt's exact termination.",
                    nameof(evidence));
            Evidence = evidence;
        }

        public PlatformHouseTermination.Incomplete Evidence { get; }
    }
}
