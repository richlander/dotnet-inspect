using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public sealed record MetadataAssemblyReferenceRelationPopulationCountRequest;

public sealed record MetadataAssemblyReferenceRelationPopulationRowsRequest
{
    public MetadataAssemblyReferenceRelationPopulationRowsRequest(
        int startOrdinal,
        int maximumRows)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startOrdinal);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRows);
        StartOrdinal = startOrdinal;
        MaximumRows = maximumRows;
    }

    public int StartOrdinal { get; }

    public int MaximumRows { get; }

}

public sealed record MetadataAssemblyReferenceRelationPopulationRequest
{
    public MetadataAssemblyReferenceRelationPopulationRequest(
        MetadataOperationPolicy policy,
        MetadataAssemblyReferenceRelationPopulationCountRequest? count = null,
        MetadataAssemblyReferenceRelationPopulationRowsRequest? rows = null,
        Guid? expectedModuleVersionId = null)
    {
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "An assembly-reference relation population must request Count, Rows, or both.");
        }
        if (expectedModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An expected assembly-reference population MVID cannot be empty.",
                nameof(expectedModuleVersionId));
        }
        if (rows is { StartOrdinal: > 0 }
            && expectedModuleVersionId is null)
        {
            throw new ArgumentException(
                "A continued assembly-reference Rows request requires an expected source MVID.",
                nameof(expectedModuleVersionId));
        }

        Count = count;
        Rows = rows;
        ExpectedModuleVersionId = expectedModuleVersionId;
    }

    public MetadataOperationPolicy Policy { get; }

    public MetadataAssemblyReferenceRelationPopulationCountRequest? Count
    { get; }

    public MetadataAssemblyReferenceRelationPopulationRowsRequest? Rows
    { get; }

    public Guid? ExpectedModuleVersionId { get; }
}

public sealed record MetadataAssemblyReferenceRelationPopulationRow
{
    public MetadataAssemblyReferenceRelationPopulationRow(
        AssemblyReferenceIdentity target,
        IEnumerable<int> metadataTokens)
    {
        Target = target
            ?? throw new ArgumentNullException(nameof(target));
        ArgumentNullException.ThrowIfNull(metadataTokens);
        int[] tokens = [.. metadataTokens];
        if (tokens.Length == 0
            || tokens.Distinct().Count() != tokens.Length
            || tokens.Any(static token =>
                System.Reflection.Metadata.Ecma335.MetadataTokens
                    .EntityHandle(token).Kind
                    != HandleKind.AssemblyReference))
        {
            throw new ArgumentException(
                "An assembly-reference relation row requires distinct AssemblyRef tokens.",
                nameof(metadataTokens));
        }

        MetadataTokens = [.. tokens];
    }

    public AssemblyReferenceIdentity Target { get; }

    public ImmutableArray<int> MetadataTokens { get; }
}

public abstract record
    MetadataAssemblyReferenceRelationPopulationCountOutcome
{
    private protected
        MetadataAssemblyReferenceRelationPopulationCountOutcome()
    {
    }

    public sealed record Counted :
        MetadataAssemblyReferenceRelationPopulationCountOutcome
    {
        public Counted(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public int Value { get; }
    }

    public sealed record Unavailable :
        MetadataAssemblyReferenceRelationPopulationCountOutcome;

    public sealed record Incomplete :
        MetadataAssemblyReferenceRelationPopulationCountOutcome;

    public sealed record Failed :
        MetadataAssemblyReferenceRelationPopulationCountOutcome;
}

public enum MetadataAssemblyReferenceRelationPopulationRowsRejection
{
    StaleSource,
    ContinuationOutOfRange,
}

public abstract record MetadataAssemblyReferenceRelationPopulationRowsOutcome
{
    private protected MetadataAssemblyReferenceRelationPopulationRowsOutcome()
    {
    }

    public sealed record Read :
        MetadataAssemblyReferenceRelationPopulationRowsOutcome
    {
        public Read(
            IEnumerable<MetadataAssemblyReferenceRelationPopulationRow> items,
            int? nextOrdinal)
        {
            ArgumentNullException.ThrowIfNull(items);
            MetadataAssemblyReferenceRelationPopulationRow[] itemCopy =
                [.. items];
            if (itemCopy.Any(static item => item is null))
            {
                throw new ArgumentException(
                    "Assembly-reference relation Rows cannot contain null.",
                    nameof(items));
            }
            if (nextOrdinal is < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nextOrdinal));
            }

            Items = [.. itemCopy];
            NextOrdinal = nextOrdinal;
        }

        public ImmutableArray<
            MetadataAssemblyReferenceRelationPopulationRow> Items
        { get; }

        public int? NextOrdinal { get; }
    }

    public sealed record Rejected(
        MetadataAssemblyReferenceRelationPopulationRowsRejection Reason)
        : MetadataAssemblyReferenceRelationPopulationRowsOutcome;

    public sealed record Unavailable :
        MetadataAssemblyReferenceRelationPopulationRowsOutcome;

    public sealed record Incomplete :
        MetadataAssemblyReferenceRelationPopulationRowsOutcome;

    public sealed record Failed :
        MetadataAssemblyReferenceRelationPopulationRowsOutcome;
}

public sealed record MetadataAssemblyReferenceRelationPopulationResult
{
    public MetadataAssemblyReferenceRelationPopulationResult(
        MetadataRelationInspectionReceipt receipt,
        MetadataRelationFamilyDisposition disposition,
        MetadataRelationCoverage coverage,
        MetadataAssemblyReferenceRelationPopulationCountOutcome? count,
        MetadataAssemblyReferenceRelationPopulationRowsOutcome? rows,
        IEnumerable<MetadataRelationDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        ArgumentNullException.ThrowIfNull(coverage);
        if (receipt.Families.Length != 1
            || receipt.Families[0]
                != MetadataRelationFamily.AssemblyReferences)
        {
            throw new ArgumentException(
                "An assembly-reference population receipt must name only "
                    + "the assembly-reference family.",
                nameof(receipt));
        }
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "An assembly-reference population result requires Count, "
                    + "Rows, or both.");
        }
        MetadataRelationDiagnostic[] diagnosticCopy =
            [.. diagnostics ?? []];
        if (diagnosticCopy.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Assembly-reference relation diagnostics cannot contain null.",
                nameof(diagnostics));
        }
        if (disposition == MetadataRelationFamilyDisposition.Complete
            && diagnosticCopy.Length != 0)
        {
            throw new ArgumentException(
                "A complete assembly-reference relation population cannot retain diagnostics.",
                nameof(diagnostics));
        }
        if (count
                is MetadataAssemblyReferenceRelationPopulationCountOutcome
                    .Counted
            && disposition != MetadataRelationFamilyDisposition.Complete)
        {
            throw new ArgumentException(
                "Exact assembly-reference Count requires complete producer evidence.",
                nameof(count));
        }
        if (rows
                is MetadataAssemblyReferenceRelationPopulationRowsOutcome
                    .Read
            {
                NextOrdinal: not null,
            }
            && disposition != MetadataRelationFamilyDisposition.Complete)
        {
            throw new ArgumentException(
                "A partial assembly-reference population cannot issue continuation.",
                nameof(rows));
        }
        if (!CountMatches(disposition, count)
            || !RowsMatch(disposition, rows))
        {
            throw new ArgumentException(
                "Assembly-reference terminal outcomes must match the "
                    + "producer disposition.");
        }

        Receipt = receipt;
        Disposition = disposition;
        Coverage = coverage;
        Count = count;
        Rows = rows;
        Diagnostics = [.. diagnosticCopy];
    }

    public MetadataRelationInspectionReceipt Receipt { get; }

    public MetadataRelationFamilyDisposition Disposition { get; }

    public MetadataRelationCoverage Coverage { get; }

    public MetadataAssemblyReferenceRelationPopulationCountOutcome? Count
    { get; }

    public MetadataAssemblyReferenceRelationPopulationRowsOutcome? Rows
    { get; }

    public ImmutableArray<MetadataRelationDiagnostic> Diagnostics
    { get; }

    private static bool CountMatches(
        MetadataRelationFamilyDisposition disposition,
        MetadataAssemblyReferenceRelationPopulationCountOutcome? count) =>
        count is null
        || disposition switch
        {
            MetadataRelationFamilyDisposition.Complete =>
                count is
                    MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Counted,
            MetadataRelationFamilyDisposition.Partial =>
                count is
                    MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Incomplete
                    or MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Failed,
            MetadataRelationFamilyDisposition.Unavailable =>
                count is
                    MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Unavailable,
            MetadataRelationFamilyDisposition.Failed =>
                count is
                    MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Failed,
            _ => false,
        };

    private static bool RowsMatch(
        MetadataRelationFamilyDisposition disposition,
        MetadataAssemblyReferenceRelationPopulationRowsOutcome? rows) =>
        rows is null
        || disposition switch
        {
            MetadataRelationFamilyDisposition.Complete =>
                rows is
                    MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Read
                    or MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Rejected
                    {
                        Reason:
                            MetadataAssemblyReferenceRelationPopulationRowsRejection
                                .ContinuationOutOfRange,
                    },
            MetadataRelationFamilyDisposition.Partial =>
                rows is
                    MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Read
                    or MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Incomplete
                    or MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Failed,
            MetadataRelationFamilyDisposition.Unavailable =>
                rows is
                    MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Unavailable,
            MetadataRelationFamilyDisposition.Failed =>
                rows is
                    MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Failed
                    or MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Rejected
                    {
                        Reason:
                            MetadataAssemblyReferenceRelationPopulationRowsRejection
                                .StaleSource,
                    },
            _ => false,
        };
}

public abstract record MetadataAssemblyReferenceRelationPopulationOutcome
{
    private protected MetadataAssemblyReferenceRelationPopulationOutcome()
    {
    }

    public sealed record Available(
        MetadataAssemblyReferenceRelationPopulationResult Result)
        : MetadataAssemblyReferenceRelationPopulationOutcome;

    public sealed record Rejected(
        MetadataImageFormatResult Format,
        string Detail)
        : MetadataAssemblyReferenceRelationPopulationOutcome;
}

internal static partial class MetadataRelationInspection
{
    internal static MetadataAssemblyReferenceRelationPopulationOutcome
        ExecuteAssemblyReferencePopulation(
            PEReader image,
            MetadataAssemblyReferenceRelationPopulationRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataImageFormatResult format =
            MetadataImageFormatClassifier.Classify(image);
        if (format is not MetadataImageFormatResult.SupportedEcma335)
        {
            return new
                MetadataAssemblyReferenceRelationPopulationOutcome.Rejected(
                    format,
                    format switch
                    {
                        MetadataImageFormatResult.NoMetadata =>
                            "The selected image contains no managed metadata.",
                        MetadataImageFormatResult
                                .UnsupportedWindowsMetadata =>
                            "Windows Metadata is not a supported relation input.",
                        MetadataImageFormatResult.MalformedRoot =>
                            "The selected image has a malformed metadata root.",
                        _ => "The selected image format is unavailable.",
                    });
        }

        MetadataReader reader;
        try
        {
            reader = image.GetMetadataReader(
                MetadataReaderOptions.None);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or OverflowException)
        {
            return new
                MetadataAssemblyReferenceRelationPopulationOutcome.Rejected(
                    new MetadataImageFormatResult.MalformedRoot(
                        MetadataRootMalformedReason
                            .UnmappableMetadataDirectory),
                    exception.Message);
        }

        using var operation =
            new MetadataOperationContext(request.Policy);
        MetadataRelationReceiptIdentity receiptIdentity =
            ReadReceiptIdentity(reader, out string? receiptFailure);
        if (receiptIdentity.ModuleVersionId is not Guid moduleVersionId)
        {
            return Available(
                receiptIdentity,
                request,
                operation,
                MetadataRelationFamilyDisposition.Failed,
                new(1, 0, 0, 1, 0),
                CountFailure(request),
                RowsFailure(request),
                [
                    MalformedDiagnostic(
                        MetadataRelationFamily.AssemblyReferences,
                        null,
                        receiptFailure
                            ?? "The metadata image has no usable module identity."),
                ]);
        }
        if (!reader.IsAssembly
            || receiptIdentity.Assembly is null)
        {
            return Available(
                receiptIdentity,
                request,
                operation,
                MetadataRelationFamilyDisposition.Unavailable,
                new(1, 0, 0, 1, 0),
                request.Count is null
                    ? null
                    : new
                        MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Unavailable(),
                request.Rows is null
                    ? null
                    : new
                        MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Unavailable(),
                [
                    UnsupportedDiagnostic(
                        MetadataRelationFamily.AssemblyReferences,
                        null,
                        "A module without an Assembly row cannot issue assembly-reference relations."),
                ]);
        }

        bool staleSource =
            request.ExpectedModuleVersionId is Guid expected
            && expected != moduleVersionId;
        if (staleSource)
        {
            return Available(
                receiptIdentity,
                request,
                operation,
                MetadataRelationFamilyDisposition.Failed,
                new(1, 0, 0, 1, 0),
                CountFailure(request),
                request.Rows is null
                    ? null
                    : new
                        MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Rejected(
                            MetadataAssemblyReferenceRelationPopulationRowsRejection
                                .StaleSource),
                [
                    new MetadataRelationDiagnostic(
                        MetadataRelationFamily.AssemblyReferences,
                        MetadataRelationDiagnosticKind.StaleSource,
                        null,
                        "The assembly-reference population belongs to a different module version."),
                ]);
        }

        MetadataImageAdmissionResult admission =
            operation.AdmitImage(reader);
        if (admission is MetadataImageAdmissionResult.Rejected rejected)
        {
            MetadataRelationDiagnostic diagnostic = new(
                MetadataRelationFamily.AssemblyReferences,
                MetadataRelationDiagnosticKind.Limit,
                null,
                "The metadata image exceeds the operation row budget.",
                MetadataOperationDimension.MetadataRows,
                rejected.Failure.MaxMetadataRows,
                rejected.Failure.ImageMetadataRows);
            return Available(
                receiptIdentity,
                request,
                operation,
                MetadataRelationFamilyDisposition.Partial,
                new(1, 0, 0, 0, 1),
                request.Count is null
                    ? null
                    : new
                        MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Incomplete(),
                request.Rows is null
                    ? null
                    : new
                        MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Incomplete(),
                [diagnostic]);
        }

        return ScanAssemblyReferencePopulation(
            reader,
            receiptIdentity,
            request,
            operation,
            cancellationToken);
    }

    private static MetadataAssemblyReferenceRelationPopulationOutcome
        ScanAssemblyReferencePopulation(
            MetadataReader reader,
            MetadataRelationReceiptIdentity receiptIdentity,
            MetadataAssemblyReferenceRelationPopulationRequest request,
            MetadataOperationContext operation,
            CancellationToken cancellationToken)
    {
        MetadataAssemblyReferenceRelationPopulationRowsRequest?
            rowRequest = request.Rows;
        int startOrdinal = rowRequest?.StartOrdinal ?? 0;
        long endOrdinal = rowRequest is null
            ? 0
            : (long)startOrdinal + rowRequest.MaximumRows;
        var ordinals =
            new Dictionary<AssemblyReferenceIdentity, int>();
        var selected =
            new List<ReferenceAccumulator>();
        var selectedByOrdinal =
            new Dictionary<int, ReferenceAccumulator>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        int considered = reader.AssemblyReferences.Count;
        int examined = 0;
        bool limited = false;
        bool failed = false;

        try
        {
            foreach (AssemblyReferenceHandle handle
                in reader.AssemblyReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                AssemblyReferenceIdentity target =
                    AssemblyReferenceIdentity.From(reader, handle);
                operation.Charge(
                    MetadataOperationDimension.RelationshipEdges);
                if (!ordinals.TryGetValue(target, out int ordinal))
                {
                    ordinal = ordinals.Count;
                    ordinals.Add(target, ordinal);
                    if (rowRequest is not null
                        && ordinal >= startOrdinal
                        && ordinal < endOrdinal)
                    {
                        var accumulator =
                            new ReferenceAccumulator(ordinal, target);
                        selected.Add(accumulator);
                        selectedByOrdinal.Add(ordinal, accumulator);
                    }
                }

                if (selectedByOrdinal.TryGetValue(
                        ordinal,
                        out ReferenceAccumulator? selectedRow))
                {
                    selectedRow.MetadataTokens.Add(
                        MetadataTokens.GetToken(handle));
                }
                examined++;
            }
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            limited = true;
            diagnostics.Add(
                LimitDiagnostic(
                    MetadataRelationFamily.AssemblyReferences,
                    exception));
        }
        catch (BadImageFormatException exception)
        {
            failed = true;
            diagnostics.Add(
                MalformedDiagnostic(
                    MetadataRelationFamily.AssemblyReferences,
                    null,
                    exception.Message));
        }

        int remaining = considered - examined;
        MetadataRelationFamilyDisposition disposition =
            diagnostics.Count == 0
                ? MetadataRelationFamilyDisposition.Complete
                : MetadataRelationFamilyDisposition.Partial;
        var coverage = new MetadataRelationCoverage(
            considered,
            examined,
            0,
            failed ? remaining : 0,
            limited ? remaining : 0);
        MetadataAssemblyReferenceRelationPopulationCountOutcome? count =
            request.Count is null
                ? null
                : disposition == MetadataRelationFamilyDisposition.Complete
                    ? new
                        MetadataAssemblyReferenceRelationPopulationCountOutcome
                        .Counted(ordinals.Count)
                    : failed
                        ? new
                            MetadataAssemblyReferenceRelationPopulationCountOutcome
                            .Failed()
                        : new
                            MetadataAssemblyReferenceRelationPopulationCountOutcome
                            .Incomplete();
        MetadataAssemblyReferenceRelationPopulationRowsOutcome? rows = null;
        if (rowRequest is not null)
        {
            if (disposition == MetadataRelationFamilyDisposition.Complete
                && startOrdinal > ordinals.Count)
            {
                rows =
                    new
                        MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Rejected(
                            MetadataAssemblyReferenceRelationPopulationRowsRejection
                                .ContinuationOutOfRange);
            }
            else if (disposition
                        != MetadataRelationFamilyDisposition.Complete
                && selected.Count == 0
                && startOrdinal >= ordinals.Count)
            {
                rows = failed
                    ? new
                        MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Failed()
                    : new
                        MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Incomplete();
            }
            else
            {
                int? nextOrdinal =
                    disposition == MetadataRelationFamilyDisposition.Complete
                    && endOrdinal < ordinals.Count
                        ? checked((int)endOrdinal)
                        : null;
                rows =
                    new
                        MetadataAssemblyReferenceRelationPopulationRowsOutcome
                        .Read(
                            selected
                                .OrderBy(static row => row.Ordinal)
                                .Select(static row =>
                                    new
                                        MetadataAssemblyReferenceRelationPopulationRow(
                                            row.Target,
                                            row.MetadataTokens)),
                            nextOrdinal);
            }
        }

        return Available(
            receiptIdentity,
            request,
            operation,
            disposition,
            coverage,
            count,
            rows,
            diagnostics);
    }

    private static MetadataAssemblyReferenceRelationPopulationOutcome
        Available(
            MetadataRelationReceiptIdentity receiptIdentity,
            MetadataAssemblyReferenceRelationPopulationRequest request,
            MetadataOperationContext operation,
            MetadataRelationFamilyDisposition disposition,
            MetadataRelationCoverage coverage,
            MetadataAssemblyReferenceRelationPopulationCountOutcome? count,
            MetadataAssemblyReferenceRelationPopulationRowsOutcome? rows,
            IEnumerable<MetadataRelationDiagnostic> diagnostics) =>
        new MetadataAssemblyReferenceRelationPopulationOutcome.Available(
            new(
                new(
                    receiptIdentity.ModuleVersionId,
                    receiptIdentity.Assembly,
                    [MetadataRelationFamily.AssemblyReferences],
                    operation.Counters),
                disposition,
                coverage,
                request.Count is null ? null : count,
                request.Rows is null ? null : rows,
                diagnostics));

    private static
        MetadataAssemblyReferenceRelationPopulationCountOutcome?
        CountFailure(
            MetadataAssemblyReferenceRelationPopulationRequest request) =>
        request.Count is null
            ? null
            : new
                MetadataAssemblyReferenceRelationPopulationCountOutcome
                .Failed();

    private static MetadataAssemblyReferenceRelationPopulationRowsOutcome?
        RowsFailure(
            MetadataAssemblyReferenceRelationPopulationRequest request) =>
        request.Rows is null
            ? null
            : new
                MetadataAssemblyReferenceRelationPopulationRowsOutcome
                .Failed();

    private sealed class ReferenceAccumulator(
        int ordinal,
        AssemblyReferenceIdentity target)
    {
        public int Ordinal { get; } = ordinal;

        public AssemblyReferenceIdentity Target { get; } = target;

        public List<int> MetadataTokens { get; } = [];
    }
}
