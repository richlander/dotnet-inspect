using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

public sealed record MetadataExtensionReceiverSelection
{
    public MetadataExtensionReceiverSelection(
        AssemblyReferenceIdentity assembly,
        MetadataTypeDefinitionName type)
    {
        Assembly = assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        Type = type
            ?? throw new ArgumentNullException(nameof(type));
    }

    public AssemblyReferenceIdentity Assembly { get; }

    public MetadataTypeDefinitionName Type { get; }
}

public sealed record MetadataExtensionRelationPopulationCountRequest;

public sealed record MetadataExtensionRelationPopulationRowsRequest
{
    public MetadataExtensionRelationPopulationRowsRequest(
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

public sealed record MetadataExtensionRelationPopulationRequest
{
    public MetadataExtensionRelationPopulationRequest(
        MetadataExtensionReceiverSelection receiver,
        MetadataOperationPolicy policy,
        MetadataExtensionRelationPopulationCountRequest? count = null,
        MetadataExtensionRelationPopulationRowsRequest? rows = null,
        bool includeNonPublic = false,
        Guid? expectedModuleVersionId = null)
    {
        Receiver = receiver
            ?? throw new ArgumentNullException(nameof(receiver));
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "An extension relation population must request Count, Rows, or both.");
        }
        if (expectedModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An expected extension population MVID cannot be empty.",
                nameof(expectedModuleVersionId));
        }
        if (rows is { StartOrdinal: > 0 }
            && expectedModuleVersionId is null)
        {
            throw new ArgumentException(
                "A continued extension Rows request requires an expected source MVID.",
                nameof(expectedModuleVersionId));
        }

        Count = count;
        Rows = rows;
        IncludeNonPublic = includeNonPublic;
        ExpectedModuleVersionId = expectedModuleVersionId;
    }

    public MetadataExtensionReceiverSelection Receiver { get; }

    public MetadataOperationPolicy Policy { get; }

    public MetadataExtensionRelationPopulationCountRequest? Count
    { get; }

    public MetadataExtensionRelationPopulationRowsRequest? Rows
    { get; }

    public bool IncludeNonPublic { get; }

    public Guid? ExpectedModuleVersionId { get; }
}

public sealed record MetadataExtensionRelationPopulationRow
{
    public MetadataExtensionRelationPopulationRow(
        IEnumerable<MetadataExtensionRelationEvidence> occurrences)
    {
        ArgumentNullException.ThrowIfNull(occurrences);
        MetadataExtensionRelationEvidence[] copy = [.. occurrences];
        if (copy.Length == 0
            || copy.Any(static occurrence => occurrence is null))
        {
            throw new ArgumentException(
                "An extension relation row requires physical declaration evidence.",
                nameof(occurrences));
        }
        if (copy.Select(static occurrence =>
                occurrence.DeclarationMetadataToken)
            .Distinct()
            .Count() != copy.Length)
        {
            throw new ArgumentException(
                "An extension relation row cannot repeat a declaration token.",
                nameof(occurrences));
        }
        if (copy.Any(static occurrence =>
            {
                HandleKind kind = MetadataTokens.EntityHandle(
                    occurrence.DeclarationMetadataToken).Kind;
                return kind is not HandleKind.MethodDefinition
                    and not HandleKind.PropertyDefinition;
            }))
        {
            throw new ArgumentException(
                "Extension relation occurrences require MethodDef or Property tokens.",
                nameof(occurrences));
        }

        ExtensionRowIdentity identity = ExtensionRowIdentity.From(copy[0]);
        if (copy.Skip(1).Any(occurrence =>
                ExtensionRowIdentity.From(occurrence) != identity))
        {
            throw new ArgumentException(
                "Physical declarations in one extension row must share exact logical endpoints.",
                nameof(occurrences));
        }

        Occurrences = [.. copy];
    }

    public ImmutableArray<MetadataExtensionRelationEvidence> Occurrences
    { get; }
}

public abstract record MetadataExtensionRelationPopulationCountOutcome
{
    private protected MetadataExtensionRelationPopulationCountOutcome()
    {
    }

    public sealed record Counted :
        MetadataExtensionRelationPopulationCountOutcome
    {
        public Counted(int value)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Value = value;
        }

        public int Value { get; }
    }

    public sealed record Unavailable :
        MetadataExtensionRelationPopulationCountOutcome;

    public sealed record Incomplete :
        MetadataExtensionRelationPopulationCountOutcome;

    public sealed record Failed :
        MetadataExtensionRelationPopulationCountOutcome;
}

public enum MetadataExtensionRelationPopulationRowsRejection
{
    StaleSource,
    ContinuationOutOfRange,
}

public abstract record MetadataExtensionRelationPopulationRowsOutcome
{
    private protected MetadataExtensionRelationPopulationRowsOutcome()
    {
    }

    public sealed record Read :
        MetadataExtensionRelationPopulationRowsOutcome
    {
        public Read(
            IEnumerable<MetadataExtensionRelationPopulationRow> items,
            int? nextOrdinal)
        {
            ArgumentNullException.ThrowIfNull(items);
            MetadataExtensionRelationPopulationRow[] itemCopy =
                [.. items];
            if (itemCopy.Any(static item => item is null))
            {
                throw new ArgumentException(
                    "Extension relation Rows cannot contain null.",
                    nameof(items));
            }
            if (nextOrdinal is < 0)
                throw new ArgumentOutOfRangeException(nameof(nextOrdinal));

            Items = [.. itemCopy];
            NextOrdinal = nextOrdinal;
        }

        public ImmutableArray<MetadataExtensionRelationPopulationRow> Items
        { get; }

        public int? NextOrdinal { get; }
    }

    public sealed record Rejected(
        MetadataExtensionRelationPopulationRowsRejection Reason)
        : MetadataExtensionRelationPopulationRowsOutcome;

    public sealed record Unavailable :
        MetadataExtensionRelationPopulationRowsOutcome;

    public sealed record Incomplete :
        MetadataExtensionRelationPopulationRowsOutcome;

    public sealed record Failed :
        MetadataExtensionRelationPopulationRowsOutcome;
}

public sealed record MetadataExtensionRelationPopulationResult
{
    public MetadataExtensionRelationPopulationResult(
        MetadataRelationInspectionReceipt receipt,
        MetadataRelationFamilyDisposition disposition,
        MetadataRelationCoverage coverage,
        MetadataExtensionRelationPopulationCountOutcome? count,
        MetadataExtensionRelationPopulationRowsOutcome? rows,
        IEnumerable<MetadataRelationDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!Enum.IsDefined(disposition))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        ArgumentNullException.ThrowIfNull(coverage);
        if (receipt.Families.Length != 1
            || receipt.Families[0] != MetadataRelationFamily.Extensions)
        {
            throw new ArgumentException(
                "An extension population receipt must name only the extension family.",
                nameof(receipt));
        }
        if (count is null && rows is null)
        {
            throw new ArgumentException(
                "An extension population result requires Count, Rows, or both.");
        }
        MetadataRelationDiagnostic[] diagnosticCopy =
            [.. diagnostics ?? []];
        if (diagnosticCopy.Any(static diagnostic => diagnostic is null))
        {
            throw new ArgumentException(
                "Extension relation diagnostics cannot contain null.",
                nameof(diagnostics));
        }
        if (disposition == MetadataRelationFamilyDisposition.Complete
            && diagnosticCopy.Length != 0)
        {
            throw new ArgumentException(
                "A complete extension relation population cannot retain diagnostics.",
                nameof(diagnostics));
        }
        if (count is MetadataExtensionRelationPopulationCountOutcome.Counted
            && disposition != MetadataRelationFamilyDisposition.Complete)
        {
            throw new ArgumentException(
                "Exact extension Count requires complete producer evidence.",
                nameof(count));
        }
        if (rows is MetadataExtensionRelationPopulationRowsOutcome.Read
            {
                NextOrdinal: not null,
            }
            && disposition != MetadataRelationFamilyDisposition.Complete)
        {
            throw new ArgumentException(
                "A partial extension population cannot issue continuation.",
                nameof(rows));
        }
        if (!CountMatches(disposition, count)
            || !RowsMatch(disposition, rows))
        {
            throw new ArgumentException(
                "Extension terminal outcomes must match the producer disposition.");
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

    public MetadataExtensionRelationPopulationCountOutcome? Count
    { get; }

    public MetadataExtensionRelationPopulationRowsOutcome? Rows
    { get; }

    public ImmutableArray<MetadataRelationDiagnostic> Diagnostics
    { get; }

    private static bool CountMatches(
        MetadataRelationFamilyDisposition disposition,
        MetadataExtensionRelationPopulationCountOutcome? count) =>
        count is null
        || disposition switch
        {
            MetadataRelationFamilyDisposition.Complete =>
                count is MetadataExtensionRelationPopulationCountOutcome
                    .Counted,
            MetadataRelationFamilyDisposition.Partial =>
                count is MetadataExtensionRelationPopulationCountOutcome
                    .Incomplete
                    or MetadataExtensionRelationPopulationCountOutcome
                        .Failed,
            MetadataRelationFamilyDisposition.Unavailable =>
                count is MetadataExtensionRelationPopulationCountOutcome
                    .Unavailable,
            MetadataRelationFamilyDisposition.Failed =>
                count is MetadataExtensionRelationPopulationCountOutcome
                    .Failed,
            _ => false,
        };

    private static bool RowsMatch(
        MetadataRelationFamilyDisposition disposition,
        MetadataExtensionRelationPopulationRowsOutcome? rows) =>
        rows is null
        || disposition switch
        {
            MetadataRelationFamilyDisposition.Complete =>
                rows is MetadataExtensionRelationPopulationRowsOutcome.Read
                    or MetadataExtensionRelationPopulationRowsOutcome
                        .Rejected
                    {
                        Reason:
                            MetadataExtensionRelationPopulationRowsRejection
                                .ContinuationOutOfRange,
                    },
            MetadataRelationFamilyDisposition.Partial =>
                rows is MetadataExtensionRelationPopulationRowsOutcome.Read
                    or MetadataExtensionRelationPopulationRowsOutcome
                        .Incomplete
                    or MetadataExtensionRelationPopulationRowsOutcome
                        .Failed,
            MetadataRelationFamilyDisposition.Unavailable =>
                rows is MetadataExtensionRelationPopulationRowsOutcome
                    .Unavailable,
            MetadataRelationFamilyDisposition.Failed =>
                rows is MetadataExtensionRelationPopulationRowsOutcome
                    .Failed
                    or MetadataExtensionRelationPopulationRowsOutcome
                        .Rejected
                    {
                        Reason:
                            MetadataExtensionRelationPopulationRowsRejection
                                .StaleSource,
                    },
            _ => false,
        };
}

public abstract record MetadataExtensionRelationPopulationOutcome
{
    private protected MetadataExtensionRelationPopulationOutcome()
    {
    }

    public sealed record Available(
        MetadataExtensionRelationPopulationResult Result)
        : MetadataExtensionRelationPopulationOutcome;

    public sealed record Rejected(
        MetadataImageFormatResult Format,
        string Detail)
        : MetadataExtensionRelationPopulationOutcome;
}

internal static partial class MetadataRelationInspection
{
    internal static MetadataExtensionRelationPopulationOutcome
        ExecuteExtensionPopulation(
            PEReader image,
            MetadataExtensionRelationPopulationRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataImageFormatResult format =
            MetadataImageFormatClassifier.Classify(image);
        if (format is not MetadataImageFormatResult.SupportedEcma335)
        {
            return new MetadataExtensionRelationPopulationOutcome.Rejected(
                format,
                format switch
                {
                    MetadataImageFormatResult.NoMetadata =>
                        "The selected image contains no managed metadata.",
                    MetadataImageFormatResult.UnsupportedWindowsMetadata =>
                        "Windows Metadata is not a supported relation input.",
                    MetadataImageFormatResult.MalformedRoot =>
                        "The selected image has a malformed metadata root.",
                    _ => "The selected image format is unavailable.",
                });
        }

        MetadataReader reader;
        try
        {
            reader = image.GetMetadataReader(MetadataReaderOptions.None);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or OverflowException)
        {
            return new MetadataExtensionRelationPopulationOutcome.Rejected(
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
                        MetadataRelationFamily.Extensions,
                        null,
                        receiptFailure
                            ?? "The metadata image has no usable module identity."),
                ]);
        }
        if (!reader.IsAssembly || receiptIdentity.Assembly is null)
        {
            return Available(
                receiptIdentity,
                request,
                operation,
                MetadataRelationFamilyDisposition.Unavailable,
                new(1, 0, 0, 1, 0),
                request.Count is null
                    ? null
                    : new MetadataExtensionRelationPopulationCountOutcome
                        .Unavailable(),
                request.Rows is null
                    ? null
                    : new MetadataExtensionRelationPopulationRowsOutcome
                        .Unavailable(),
                [
                    UnsupportedDiagnostic(
                        MetadataRelationFamily.Extensions,
                        null,
                        "A module without an Assembly row cannot issue extension relations."),
                ]);
        }

        if (request.ExpectedModuleVersionId is Guid expected
            && expected != moduleVersionId)
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
                    : new MetadataExtensionRelationPopulationRowsOutcome
                        .Rejected(
                            MetadataExtensionRelationPopulationRowsRejection
                                .StaleSource),
                [
                    new MetadataRelationDiagnostic(
                        MetadataRelationFamily.Extensions,
                        MetadataRelationDiagnosticKind.StaleSource,
                        null,
                        "The extension population belongs to a different module version."),
                ]);
        }

        MetadataImageAdmissionResult admission =
            operation.AdmitImage(reader);
        if (admission is MetadataImageAdmissionResult.Rejected rejected)
        {
            MetadataRelationDiagnostic diagnostic = new(
                MetadataRelationFamily.Extensions,
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
                    : new MetadataExtensionRelationPopulationCountOutcome
                        .Incomplete(),
                request.Rows is null
                    ? null
                    : new MetadataExtensionRelationPopulationRowsOutcome
                        .Incomplete(),
                [diagnostic]);
        }

        return ScanExtensionPopulation(
            image,
            reader,
            receiptIdentity,
            request,
            operation,
            cancellationToken);
    }

    private static MetadataExtensionRelationPopulationOutcome
        ScanExtensionPopulation(
            PEReader image,
            MetadataReader reader,
            MetadataRelationReceiptIdentity receiptIdentity,
            MetadataExtensionRelationPopulationRequest request,
            MetadataOperationContext operation,
            CancellationToken cancellationToken)
    {
        MetadataExtensionRelationPopulationRowsRequest? rowRequest =
            request.Rows;
        int startOrdinal = rowRequest?.StartOrdinal ?? 0;
        long endOrdinal = rowRequest is null
            ? 0
            : (long)startOrdinal + rowRequest.MaximumRows;
        var ordinals = new Dictionary<ExtensionRowIdentity, int>();
        var selected = new List<ExtensionRowAccumulator>();
        var selectedByIdentity =
            new Dictionary<ExtensionRowIdentity, ExtensionRowAccumulator>();
        var diagnostics =
            ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
        ExtensionCandidatePopulation? population = null;
        var admittedDeclarations = new HashSet<int>();
        int receiverExcluded = 0;
        bool scanCompleted = false;
        bool limited = false;
        bool failed = false;

        try
        {
            population = ExtensionCandidates(
                reader,
                request.IncludeNonPublic,
                cancellationToken);
            foreach (ExtensionMethodInfo extension
                in ExtensionMethodScanner.FindAllExtensions(
                    image,
                    request.IncludeNonPublic))
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                if (!TryReadExtensionDeclaration(
                        reader,
                        extension,
                        operation,
                        out DecodedExtensionDeclaration? declaration,
                        out MetadataRelationDiagnostic? diagnostic))
                {
                    diagnostics.Add(diagnostic!);
                    continue;
                }

                admittedDeclarations.Add(
                    extension.DeclarationMetadataToken);
                if (!MatchesReceiver(
                        receiptIdentity.Assembly!,
                        declaration!.Receiver,
                        request.Receiver))
                {
                    receiverExcluded++;
                    continue;
                }

                ExtensionRowIdentity identity =
                    ExtensionRowIdentity.From(declaration);
                if (!ordinals.TryGetValue(identity, out int ordinal))
                {
                    ordinal = ordinals.Count;
                    ordinals.Add(identity, ordinal);
                    if (rowRequest is not null
                        && ordinal >= startOrdinal
                        && ordinal < endOrdinal)
                    {
                        var accumulator =
                            new ExtensionRowAccumulator(ordinal);
                        selected.Add(accumulator);
                        selectedByIdentity.Add(identity, accumulator);
                    }
                }

                if (selectedByIdentity.TryGetValue(
                        identity,
                        out ExtensionRowAccumulator? selectedRow))
                {
                    selectedRow.Occurrences.Add(
                        declaration.ToEvidence());
                }
            }
            scanCompleted = true;
        }
        catch (MetadataOperationBudgetExceededException exception)
        {
            limited = true;
            diagnostics.Add(
                LimitDiagnostic(
                    MetadataRelationFamily.Extensions,
                    exception));
        }
        catch (BadImageFormatException exception)
        {
            failed = true;
            diagnostics.Add(
                MalformedDiagnostic(
                    MetadataRelationFamily.Extensions,
                    null,
                    exception.Message));
        }

        if (scanCompleted)
        {
            var auditDiagnostics =
                ImmutableArray.CreateBuilder<MetadataRelationDiagnostic>();
            AuditRejectedExtensionCandidates(
                reader,
                new MetadataRelationInspectionRequest(
                    [MetadataRelationFamily.Extensions],
                    request.Policy,
                    includeNonPublic: request.IncludeNonPublic),
                admittedDeclarations,
                auditDiagnostics,
                cancellationToken);
            diagnostics.AddRange(auditDiagnostics);
        }

        if (population is null)
        {
            return Available(
                receiptIdentity,
                request,
                operation,
                MetadataRelationFamilyDisposition.Partial,
                new(1, 0, 0, failed ? 1 : 0, limited ? 1 : 0),
                failed
                    ? CountFailure(request)
                    : request.Count is null
                        ? null
                        : new
                            MetadataExtensionRelationPopulationCountOutcome
                            .Incomplete(),
                failed
                    ? RowsFailure(request)
                    : request.Rows is null
                        ? null
                        : new
                            MetadataExtensionRelationPopulationRowsOutcome
                            .Incomplete(),
                diagnostics);
        }

        int unavailable = diagnostics
            .Where(static diagnostic =>
                diagnostic.Kind != MetadataRelationDiagnosticKind.Limit
                && diagnostic.MetadataToken is not null)
            .Select(static diagnostic =>
                diagnostic.MetadataToken!.Value)
            .Distinct()
            .Count(population.Included.Contains);
        int remaining =
            population.Included.Count
            - admittedDeclarations.Count
            - unavailable;
        if (!limited)
            unavailable += remaining;
        MetadataRelationFamilyDisposition disposition =
            diagnostics.Count == 0
                ? MetadataRelationFamilyDisposition.Complete
                : MetadataRelationFamilyDisposition.Partial;
        var coverage = new MetadataRelationCoverage(
            population.Included.Count + population.Excluded,
            admittedDeclarations.Count - receiverExcluded,
            population.Excluded + receiverExcluded,
            unavailable,
            limited ? remaining : 0);
        MetadataExtensionRelationPopulationCountOutcome? count =
            request.Count is null
                ? null
                : disposition == MetadataRelationFamilyDisposition.Complete
                    ? new MetadataExtensionRelationPopulationCountOutcome
                        .Counted(ordinals.Count)
                    : failed
                        ? new MetadataExtensionRelationPopulationCountOutcome
                            .Failed()
                        : new MetadataExtensionRelationPopulationCountOutcome
                            .Incomplete();
        MetadataExtensionRelationPopulationRowsOutcome? rows = null;
        if (rowRequest is not null)
        {
            if (disposition == MetadataRelationFamilyDisposition.Complete
                && startOrdinal > ordinals.Count)
            {
                rows =
                    new MetadataExtensionRelationPopulationRowsOutcome
                        .Rejected(
                            MetadataExtensionRelationPopulationRowsRejection
                                .ContinuationOutOfRange);
            }
            else if (disposition
                        != MetadataRelationFamilyDisposition.Complete
                && selected.Count == 0
                && startOrdinal >= ordinals.Count)
            {
                rows = failed
                    ? new MetadataExtensionRelationPopulationRowsOutcome
                        .Failed()
                    : new MetadataExtensionRelationPopulationRowsOutcome
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
                    new MetadataExtensionRelationPopulationRowsOutcome.Read(
                        selected
                            .OrderBy(static row => row.Ordinal)
                            .Select(static row =>
                                new MetadataExtensionRelationPopulationRow(
                                    row.Occurrences)),
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

    private static bool MatchesReceiver(
        AssemblyReferenceIdentity sourceAssembly,
        MetadataTypeIdentity receiver,
        MetadataExtensionReceiverSelection selection)
    {
        MetadataNamedTypeIdentity? named =
            ReceiverDefinition(receiver);
        if (named is null
            || MetadataTypeDefinitionName.Create(
                    named.Namespace.ToString(),
                    [.. named.Segments.Select(static segment =>
                        segment.ToString())])
                is not MetadataTypeDefinitionNameResult.Valid valid
            || valid.Name != selection.Type)
        {
            return false;
        }

        return named.Scope.Kind switch
        {
            MetadataTypeScopeKind.CurrentModule
                or MetadataTypeScopeKind.ModuleReference =>
                sourceAssembly.IsEquivalentTo(selection.Assembly),
            MetadataTypeScopeKind.AssemblyReference
                when named.Scope.Assembly is { } assembly =>
                selection.Assembly.IsEquivalentTo(
                    new AssemblyReferenceIdentity(
                        assembly.Name.ToString(),
                        assembly.Version,
                        EmptyToNull(assembly.Culture),
                        EmptyToNull(assembly.PublicKeyToken))),
            _ => false,
        };
    }

    private static MetadataNamedTypeIdentity? ReceiverDefinition(
        MetadataTypeIdentity receiver) =>
        receiver switch
        {
            MetadataTypeIdentity.Named named => named.Definition,
            MetadataTypeIdentity.GenericInstance generic =>
                generic.Definition,
            MetadataTypeIdentity.ByReference byReference =>
                ReceiverDefinition(byReference.Element),
            MetadataTypeIdentity.Modified modified =>
                ReceiverDefinition(modified.Type),
            MetadataTypeIdentity.Pinned pinned =>
                ReceiverDefinition(pinned.Type),
            _ => null,
        };

    private static string? EmptyToNull(InertText.InertString? value) =>
        value is null || value.Value.IsEmpty
            ? null
            : value.Value.ToString();

    private static MetadataExtensionRelationPopulationOutcome Available(
        MetadataRelationReceiptIdentity receiptIdentity,
        MetadataExtensionRelationPopulationRequest request,
        MetadataOperationContext operation,
        MetadataRelationFamilyDisposition disposition,
        MetadataRelationCoverage coverage,
        MetadataExtensionRelationPopulationCountOutcome? count,
        MetadataExtensionRelationPopulationRowsOutcome? rows,
        IEnumerable<MetadataRelationDiagnostic> diagnostics) =>
        new MetadataExtensionRelationPopulationOutcome.Available(
            new(
                new(
                    receiptIdentity.ModuleVersionId,
                    receiptIdentity.Assembly,
                    [MetadataRelationFamily.Extensions],
                    operation.Counters),
                disposition,
                coverage,
                request.Count is null ? null : count,
                request.Rows is null ? null : rows,
                diagnostics));

    private static MetadataExtensionRelationPopulationCountOutcome?
        CountFailure(
            MetadataExtensionRelationPopulationRequest request) =>
        request.Count is null
            ? null
            : new MetadataExtensionRelationPopulationCountOutcome.Failed();

    private static MetadataExtensionRelationPopulationRowsOutcome?
        RowsFailure(
            MetadataExtensionRelationPopulationRequest request) =>
        request.Rows is null
            ? null
            : new MetadataExtensionRelationPopulationRowsOutcome.Failed();

    private sealed class ExtensionRowAccumulator(int ordinal)
    {
        public int Ordinal { get; } = ordinal;

        public List<MetadataExtensionRelationEvidence> Occurrences
        { get; } = [];
    }
}

internal sealed record ExtensionRowIdentity(
    MetadataTypeDefinitionName DeclaringType,
    MemberAnchor Member,
    MetadataTypeIdentity Receiver,
    MetadataTypeDefinitionAddress? ReceiverContextType,
    MetadataMethodAddress? ReceiverDeclarationMethod)
{
    internal static ExtensionRowIdentity From(
        MetadataExtensionRelationEvidence evidence)
    {
        GenericParameterUse use = GenericParameters(evidence.Receiver);
        return new(
            evidence.DeclaringTypeName,
            evidence.Member,
            evidence.Receiver,
            use.HasType ? evidence.ReceiverContextType : null,
            use.HasMethod ? evidence.ReceiverDeclarationMethod : null);
    }

    internal static ExtensionRowIdentity From(
        MetadataRelationInspection.DecodedExtensionDeclaration declaration)
    {
        GenericParameterUse use =
            GenericParameters(declaration.Receiver);
        return new(
            declaration.DeclaringTypeName,
            declaration.Member,
            declaration.Receiver,
            use.HasType ? declaration.ReceiverContextType : null,
            use.HasMethod
                ? declaration.ReceiverDeclarationMethod
                : null);
    }

    private static GenericParameterUse GenericParameters(
        MetadataTypeIdentity type) =>
        type switch
        {
            MetadataTypeIdentity.GenericParameter parameter =>
                parameter.IsMethodParameter
                    ? new(false, true)
                    : new(true, false),
            MetadataTypeIdentity.GenericInstance generic =>
                Combine(generic.Arguments),
            MetadataTypeIdentity.SzArray array =>
                GenericParameters(array.Element),
            MetadataTypeIdentity.Array array =>
                GenericParameters(array.Element),
            MetadataTypeIdentity.Pointer pointer =>
                GenericParameters(pointer.Element),
            MetadataTypeIdentity.ByReference byReference =>
                GenericParameters(byReference.Element),
            MetadataTypeIdentity.FunctionPointer pointer =>
                GenericParameters(pointer.Signature),
            MetadataTypeIdentity.Modified modified =>
                GenericParameters(modified.Modifier)
                    | GenericParameters(modified.Type),
            MetadataTypeIdentity.Pinned pinned =>
                GenericParameters(pinned.Type),
            _ => default,
        };

    private static GenericParameterUse GenericParameters(
        MetadataMethodSignatureIdentity signature) =>
        GenericParameters(signature.ReturnType)
        | Combine(signature.ParameterTypes);

    private static GenericParameterUse Combine(
        IEnumerable<MetadataTypeIdentity> types)
    {
        GenericParameterUse result = default;
        foreach (MetadataTypeIdentity type in types)
            result |= GenericParameters(type);
        return result;
    }

    private readonly record struct GenericParameterUse(
        bool HasType,
        bool HasMethod)
    {
        public static GenericParameterUse operator |(
            GenericParameterUse left,
            GenericParameterUse right) =>
            new(
                left.HasType || right.HasType,
                left.HasMethod || right.HasMethod);
    }
}
