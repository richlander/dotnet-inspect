using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.MetadataPrimitives;

/// <summary>The ECMA-335 table selected by a HasSemantics coded index.</summary>
public enum MethodSemanticsAssociationKind
{
    Event,
    Property,
}

/// <summary>One lossless physical MethodSemantics table row.</summary>
public readonly record struct MethodSemanticsRow(
    int RowNumber,
    ushort RawSemantics,
    MethodDefinitionHandle Method,
    MethodSemanticsAssociationKind AssociationKind,
    int AssociationRowNumber);

/// <summary>Why a MethodSemantics table could not be read mechanically.</summary>
public enum MethodSemanticsMalformedReason
{
    MetadataRootMalformed,
    MetadataReaderRejected,
    InvalidTableLayout,
    NilMethod,
    MethodOutOfRange,
    NilAssociation,
    AssociationOutOfRange,
}

/// <summary>A typed result from one complete MethodSemantics table read.</summary>
public abstract record MethodSemanticsReadResult
{
    private protected MethodSemanticsReadResult()
    {
    }

    /// <summary>The complete table was read in physical row order.</summary>
    public sealed record Success : MethodSemanticsReadResult
    {
        internal Success(
            ImmutableArray<MethodSemanticsRow> rows,
            bool associationsAreNondecreasing)
        {
            Rows = rows;
            AssociationsAreNondecreasing = associationsAreNondecreasing;
        }

        public ImmutableArray<MethodSemanticsRow> Rows { get; }

        public bool AssociationsAreNondecreasing { get; }

        public int RowsVisited => Rows.Length;
    }

    /// <summary>The PE image has no managed metadata directory.</summary>
    public sealed record NoMetadata : MethodSemanticsReadResult
    {
        internal NoMetadata()
        {
        }
    }

    /// <summary>The image contains unsupported Windows Metadata.</summary>
    public sealed record UnsupportedWindowsMetadata
        : MethodSemanticsReadResult
    {
        internal UnsupportedWindowsMetadata()
        {
        }
    }

    /// <summary>The metadata root or MethodSemantics table is malformed.</summary>
    public sealed record MalformedInput : MethodSemanticsReadResult
    {
        internal MalformedInput(
            MethodSemanticsMalformedReason reason,
            int rowsVisited,
            int? rowNumber = null,
            MetadataRootMalformedReason? metadataRootReason = null)
        {
            Reason = reason;
            RowsVisited = rowsVisited;
            RowNumber = rowNumber;
            MetadataRootReason = metadataRootReason;
        }

        public MethodSemanticsMalformedReason Reason { get; }

        public int RowsVisited { get; }

        public int? RowNumber { get; }

        public MetadataRootMalformedReason? MetadataRootReason { get; }
    }

    /// <summary>The caller's retained-association budget was exhausted.</summary>
    public sealed record RetainedAssociationBudgetExceeded
        : MethodSemanticsReadResult
    {
        internal RetainedAssociationBudgetExceeded(int rowsVisited)
        {
            RowsVisited = rowsVisited;
        }

        public int RowsVisited { get; }
    }
}

/// <summary>
/// Operation-owned budget for immutable MethodSemantics associations.
/// </summary>
public sealed class MethodSemanticsReadBudget
{
    /// <summary>
    /// Default finite ceiling for one operation. The .NET 11 preview 7 runtime
    /// and reference corpus maximum was 6,592 retained associations. Gated by
    /// <c>Mdp016_DefaultBudgetPinsMeasuredCorpusMargin</c>.
    /// </summary>
    public const int DefaultMaximumRetainedAssociations = 64 * 1024;

    readonly int _maximumRetainedAssociations;
    int _retainedAssociations;

    public MethodSemanticsReadBudget(int maximumRetainedAssociations)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumRetainedAssociations);
        _maximumRetainedAssociations = maximumRetainedAssociations;
    }

    /// <summary>
    /// Creates an explicit compatibility budget with no table-sized limit.
    /// Product entry points must instead supply a finite policy.
    /// </summary>
    public static MethodSemanticsReadBudget Unbounded
        => new(int.MaxValue);

    public static MethodSemanticsReadBudget Default
        => new(DefaultMaximumRetainedAssociations);

    public int MaximumRetainedAssociations
        => _maximumRetainedAssociations;

    public int RetainedAssociations
        => Volatile.Read(ref _retainedAssociations);

    internal bool TryChargeAssociation()
    {
        while (true)
        {
            int current = Volatile.Read(ref _retainedAssociations);
            if (current >= _maximumRetainedAssociations)
                return false;

            if (Interlocked.CompareExchange(
                    ref _retainedAssociations,
                    current + 1,
                    current)
                == current)
            {
                return true;
            }
        }
    }
}

/// <summary>
/// Reads the complete three-column MethodSemantics table without applying
/// property or event policy. The primitive-local portion of MDP016 is enforced
/// by <c>MethodSemanticsRowReaderTests</c> and
/// <c>LayeringTests.MetadataPrimitives_MethodSemanticsReaderIsIsolated</c>.
/// </summary>
public static class MethodSemanticsRowReader
{
    static readonly MethodSemanticsReadResult Missing =
        new MethodSemanticsReadResult.NoMetadata();
    static readonly MethodSemanticsReadResult Unsupported =
        new MethodSemanticsReadResult.UnsupportedWindowsMetadata();

    public static MethodSemanticsReadResult Read(
        PEReader peReader,
        MethodSemanticsReadBudget budget)
    {
        ArgumentNullException.ThrowIfNull(peReader);
        ArgumentNullException.ThrowIfNull(budget);

        switch (MetadataImageFormatClassifier.Classify(peReader))
        {
            case MetadataImageFormatResult.NoMetadata:
                return Missing;
            case MetadataImageFormatResult.UnsupportedWindowsMetadata:
                return Unsupported;
            case MetadataImageFormatResult.MalformedRoot malformed:
                return Malformed(
                    MethodSemanticsMalformedReason.MetadataRootMalformed,
                    metadataRootReason: malformed.Reason);
            case MetadataImageFormatResult.SupportedEcma335:
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown metadata image format result.");
        }

        MetadataReader reader;
        try
        {
            reader = peReader.GetMetadataReader();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException or OverflowException)
        {
            return Malformed(
                MethodSemanticsMalformedReason.MetadataReaderRejected);
        }

        int rowCount = reader.GetTableRowCount(
            TableIndex.MethodSemantics);
        int methodRowCount = reader.GetTableRowCount(
            TableIndex.MethodDef);
        int eventRowCount = reader.GetTableRowCount(
            TableIndex.Event);
        int propertyRowCount = reader.GetTableRowCount(
            TableIndex.Property);

        int methodIndexSize = methodRowCount < ushort.MaxValue + 1
            ? sizeof(ushort)
            : sizeof(uint);
        int associationIndexSize =
            Math.Max(eventRowCount, propertyRowCount) < (1 << 15)
                ? sizeof(ushort)
                : sizeof(uint);
        int rowSize = reader.GetTableRowSize(
            TableIndex.MethodSemantics);
        int expectedRowSize =
            sizeof(ushort) + methodIndexSize + associationIndexSize;
        if (rowSize != expectedRowSize)
        {
            return Malformed(
                MethodSemanticsMalformedReason.InvalidTableLayout);
        }

        if (rowCount == 0)
        {
            return new MethodSemanticsReadResult.Success(
                [],
                associationsAreNondecreasing: true);
        }

        int tableLength;
        int tableOffset;
        PEMemoryBlock metadata;
        try
        {
            tableLength = checked(rowCount * rowSize);
            tableOffset = reader.GetTableMetadataOffset(
                TableIndex.MethodSemantics);
            metadata = peReader.GetMetadata();
        }
        catch (BadImageFormatException)
        {
            return Malformed(
                MethodSemanticsMalformedReason.InvalidTableLayout);
        }
        catch (OverflowException)
        {
            return Malformed(
                MethodSemanticsMalformedReason.InvalidTableLayout);
        }

        if (tableOffset < 0
            || tableLength < 0
            || tableOffset > metadata.Length - tableLength)
        {
            return Malformed(
                MethodSemanticsMalformedReason.InvalidTableLayout);
        }

        BlobReader table = metadata.GetReader(
            tableOffset,
            tableLength);
        ImmutableArray<MethodSemanticsRow>.Builder? rows = null;
        uint previousAssociation = 0;
        bool associationsAreNondecreasing = true;

        for (int index = 0; index < rowCount; index++)
        {
            int rowNumber = index + 1;
            ushort semantics = table.ReadUInt16();
            uint methodRow = ReadIndex(ref table, methodIndexSize);
            uint association = ReadIndex(
                ref table,
                associationIndexSize);
            int rowsVisited = rowNumber;

            if (methodRow == 0)
            {
                return Malformed(
                    MethodSemanticsMalformedReason.NilMethod,
                    rowsVisited,
                    rowNumber);
            }

            if (methodRow > methodRowCount)
            {
                return Malformed(
                    MethodSemanticsMalformedReason.MethodOutOfRange,
                    rowsVisited,
                    rowNumber);
            }

            uint associationRow = association >> 1;
            if (associationRow == 0)
            {
                return Malformed(
                    MethodSemanticsMalformedReason.NilAssociation,
                    rowsVisited,
                    rowNumber);
            }

            var associationKind = (association & 1) == 0
                ? MethodSemanticsAssociationKind.Event
                : MethodSemanticsAssociationKind.Property;
            int associationRowCount =
                associationKind == MethodSemanticsAssociationKind.Event
                    ? eventRowCount
                    : propertyRowCount;
            if (associationRow > associationRowCount)
            {
                return Malformed(
                    MethodSemanticsMalformedReason.AssociationOutOfRange,
                    rowsVisited,
                    rowNumber);
            }

            if (!budget.TryChargeAssociation())
            {
                return new MethodSemanticsReadResult
                    .RetainedAssociationBudgetExceeded(rowsVisited);
            }

            if (index > 0 && association < previousAssociation)
                associationsAreNondecreasing = false;
            previousAssociation = association;

            rows ??= ImmutableArray.CreateBuilder<MethodSemanticsRow>();
            rows.Add(
                new MethodSemanticsRow(
                    rowNumber,
                    semantics,
                    MetadataTokens.MethodDefinitionHandle(
                        checked((int)methodRow)),
                    associationKind,
                    checked((int)associationRow)));
        }

        return new MethodSemanticsReadResult.Success(
            rows!.ToImmutable(),
            associationsAreNondecreasing);
    }

    static uint ReadIndex(
        ref BlobReader reader,
        int width)
        => width == sizeof(ushort)
            ? reader.ReadUInt16()
            : reader.ReadUInt32();

    static MethodSemanticsReadResult Malformed(
        MethodSemanticsMalformedReason reason,
        int rowsVisited = 0,
        int? rowNumber = null,
        MetadataRootMalformedReason? metadataRootReason = null)
        => new MethodSemanticsReadResult.MalformedInput(
            reason,
            rowsVisited,
            rowNumber,
            metadataRootReason);
}
