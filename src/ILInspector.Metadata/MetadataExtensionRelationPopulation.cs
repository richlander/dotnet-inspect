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
        MetadataTypeDefinitionName type,
        MetadataTypeDefinitionAddress? definition = null)
    {
        Assembly = assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        Type = type
            ?? throw new ArgumentNullException(nameof(type));
        Definition = definition;
    }

    public AssemblyReferenceIdentity Assembly { get; }

    public MetadataTypeDefinitionName Type { get; }

    public MetadataTypeDefinitionAddress? Definition { get; }
}

public sealed record MetadataExtensionRelationPresenceRequest
{
    public MetadataExtensionRelationPresenceRequest(
        MetadataExtensionReceiverSelection receiver,
        MetadataOperationPolicy policy,
        bool includeNonPublic = false,
        AssemblyReferenceIdentity? sourceAssembly = null)
    {
        Receiver = receiver
            ?? throw new ArgumentNullException(nameof(receiver));
        Policy = policy
            ?? throw new ArgumentNullException(nameof(policy));
        IncludeNonPublic = includeNonPublic;
        SourceAssembly = sourceAssembly;
    }

    public MetadataExtensionReceiverSelection Receiver { get; }

    public MetadataOperationPolicy Policy { get; }

    public bool IncludeNonPublic { get; }

    public AssemblyReferenceIdentity? SourceAssembly { get; }
}

public abstract record MetadataExtensionRelationPresenceOutcome
{
    private protected MetadataExtensionRelationPresenceOutcome()
    {
    }

    public sealed record Available(bool Exists)
        : MetadataExtensionRelationPresenceOutcome;

    public sealed record Incomplete
        : MetadataExtensionRelationPresenceOutcome;

    public sealed record Failed
        : MetadataExtensionRelationPresenceOutcome;
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
    private static readonly GenericContext
        s_extensionPresencePrefixContext = new([], []);

    internal static MetadataExtensionRelationPresenceOutcome
        ExecuteExtensionPresence(
            PEReader image,
            MetadataReader reader,
            MetadataExtensionRelationPresenceRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var operation =
            new MetadataOperationContext(request.Policy);
        if (!reader.IsAssembly)
        {
            return new MetadataExtensionRelationPresenceOutcome.Failed();
        }
        Guid moduleVersionId =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        if (moduleVersionId == Guid.Empty)
        {
            return new MetadataExtensionRelationPresenceOutcome.Failed();
        }
        AssemblyReferenceIdentity sourceAssembly =
            request.SourceAssembly
            ?? AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
        if (request.Receiver.Definition is { } definition
            && moduleVersionId != definition.ModuleVersionId)
        {
            return new MetadataExtensionRelationPresenceOutcome.Failed();
        }
        if (operation.AdmitImage(reader)
            is MetadataImageAdmissionResult.Rejected)
        {
            return new MetadataExtensionRelationPresenceOutcome
                .Incomplete();
        }

        bool incomplete = false;
        try
        {
            bool found = VisitExtensionCandidates(
                reader,
                static _ => true,
                request.IncludeNonPublic,
                cancellationToken,
                candidate =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    operation.Charge(
                        MetadataOperationDimension
                            .DeclarationCandidates);
                    if (!TryMatchExtensionPresenceCandidate(
                            reader,
                            candidate,
                            operation,
                            sourceAssembly,
                            request.Receiver,
                            out bool matches,
                            out _))
                    {
                        incomplete = true;
                        return false;
                    }
                    return matches;
                },
                static () => { });
            if (found)
            {
                return new MetadataExtensionRelationPresenceOutcome
                    .Available(true);
            }
        }
        catch (MetadataOperationBudgetExceededException)
        {
            return new MetadataExtensionRelationPresenceOutcome
                .Incomplete();
        }
        catch (BadImageFormatException)
        {
            return new MetadataExtensionRelationPresenceOutcome.Failed();
        }

        return incomplete
            ? new MetadataExtensionRelationPresenceOutcome.Incomplete()
            : new MetadataExtensionRelationPresenceOutcome
                .Available(false);
    }

    private static bool TryMatchExtensionPresenceCandidate(
        MetadataReader reader,
        ExtensionDeclarationCandidate candidate,
        MetadataOperationContext operation,
        AssemblyReferenceIdentity sourceAssembly,
        MetadataExtensionReceiverSelection selection,
        out bool matches,
        out string? failure)
    {
        TypeDefinitionHandle receiverContextHandle;
        MethodDefinitionHandle receiverMethodHandle;
        if (candidate.IsProperty)
        {
            if (!TryResolveExtensionPropertyReceiver(
                    reader,
                    candidate,
                    out receiverContextHandle,
                    out receiverMethodHandle))
            {
                matches = false;
                failure =
                    "An extension property lacks an exact receiver marker.";
                return false;
            }
        }
        else
        {
            receiverContextHandle = candidate.DeclaringType;
            receiverMethodHandle = candidate.Method;
        }

        TypeDefinition receiverContext =
            reader.GetTypeDefinition(receiverContextHandle);
        MethodDefinition receiverMethod =
            reader.GetMethodDefinition(receiverMethodHandle);
        operation.Charge(
            MetadataOperationDimension.SignatureBytes,
            reader.GetBlobReader(receiverMethod.Signature).Length);

        try
        {
            bool sourceDefinesPrimitiveTypes =
                ApiSurfaceExtractor.DefinesPrimitiveTypes(reader);
            var provider = new ExtensionReceiverMatchProvider(
                sourceAssembly,
                selection,
                sourceDefinesPrimitiveTypes);
            BlobReader signature =
                reader.GetBlobReader(receiverMethod.Signature);
            SignatureHeader header =
                signature.ReadSignatureHeader();
            if (header.Kind != SignatureKind.Method
                || header.IsInstance)
            {
                matches = false;
                failure =
                    "The extension signature is not a static method signature.";
                return false;
            }
            if (header.IsGeneric
                && signature.ReadCompressedInteger() < 0)
            {
                matches = false;
                failure =
                    "The extension signature has no valid generic arity.";
                return false;
            }
            int parameterCount =
                signature.ReadCompressedInteger();
            if (parameterCount <= 0)
            {
                matches = false;
                failure =
                    "The extension declaration has no receiver parameter.";
                return false;
            }

            var decoder =
                new SignatureDecoder<bool, GenericContext>(
                    provider,
                    reader,
                    s_extensionPresencePrefixContext);
            _ = decoder.DecodeType(
                ref signature,
                allowTypeSpecifications: true);
            bool receiverMatches = decoder.DecodeType(
                ref signature,
                allowTypeSpecifications: true);
            if (provider.Rejected)
            {
                matches = false;
                failure =
                    "The extension receiver could not be decoded exactly.";
                return false;
            }
            if (!receiverMatches)
            {
                matches = false;
                failure = null;
                return true;
            }
            if (!SignatureBlobGuard.IsSafeAndCompleteToDecode(
                    reader,
                    receiverMethod.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                matches = false;
                failure =
                    "The extension signature is not safe and complete to decode.";
                return false;
            }

            provider = new(
                sourceAssembly,
                selection,
                sourceDefinesPrimitiveTypes);
            GenericContext context =
                GenericContext.ForMethod(
                    reader,
                    receiverContext,
                    receiverMethod);
            MethodSignature<bool> methodSignature =
                receiverMethod.DecodeSignature(
                    provider,
                    context);
            if (provider.Rejected
                || methodSignature.ParameterTypes.IsEmpty)
            {
                matches = false;
                failure =
                    "The extension receiver could not be decoded exactly.";
                return false;
            }

            operation.Charge(
                MetadataOperationDimension.RelationshipEdges);
            matches = methodSignature.ParameterTypes[0];
            failure = null;
            return true;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException
                or ArgumentException
                or InvalidOperationException
                or OverflowException)
        {
            matches = false;
            failure = exception.Message;
            return false;
        }
    }

    internal static MetadataExtensionRelationPopulationOutcome
        ExecuteExtensionPopulation(
            PEReader image,
            MetadataReader reader,
            MetadataExtensionRelationPopulationRequest request,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

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
            reader,
            receiptIdentity,
            request,
            operation,
            cancellationToken);
    }

    private static MetadataExtensionRelationPopulationOutcome
        ScanExtensionPopulation(
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
        var propertySignatures = new HashSet<string>(
            StringComparer.Ordinal);
        int receiverExcluded = 0;
        bool limited = false;
        bool failed = false;

        try
        {
            population = ExtensionCandidates(
                reader,
                request.IncludeNonPublic,
                cancellationToken);
            foreach (ExtensionDeclarationCandidate candidate
                in population.Candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operation.Charge(
                    MetadataOperationDimension.DeclarationCandidates);
                if (!TryDecodeExtensionPopulationCandidate(
                        reader,
                        candidate,
                        operation,
                        out DecodedExtensionPopulationCandidate? decoded,
                        out MetadataRelationDiagnostic? diagnostic))
                {
                    diagnostics.Add(diagnostic!);
                    continue;
                }

                admittedDeclarations.Add(
                    candidate.MetadataToken);
                if (!MatchesReceiver(
                        receiptIdentity.Assembly!,
                        decoded!.Receiver,
                        request.Receiver))
                {
                    receiverExcluded++;
                    continue;
                }
                if (!TryCreateExtensionPopulationDeclaration(
                        reader,
                        decoded,
                        out DecodedExtensionDeclaration? declaration,
                        out diagnostic))
                {
                    admittedDeclarations.Remove(
                        candidate.MetadataToken);
                    diagnostics.Add(diagnostic!);
                    continue;
                }
                if (candidate.IsProperty
                    && !propertySignatures.Add(
                        declaration!.Member.CanonicalSignature))
                {
                    admittedDeclarations.Remove(
                        candidate.MetadataToken);
                    diagnostics.Add(
                        UnsupportedDiagnostic(
                            MetadataRelationFamily.Extensions,
                            candidate.MetadataToken,
                            "An extension property repeats a canonical declaration identity."));
                    continue;
                }

                ExtensionRowIdentity identity =
                    ExtensionRowIdentity.From(declaration!);
                if (!ordinals.TryGetValue(identity, out int ordinal))
                {
                    ordinal = ordinals.Count;
                    ordinals.Add(identity, ordinal);
                    if (rowRequest is not null
                        && ordinal >= startOrdinal
                        && ordinal < endOrdinal)
                    {
                        var accumulator =
                            new ExtensionRowAccumulator();
                        selected.Add(accumulator);
                        selectedByIdentity.Add(identity, accumulator);
                    }
                }

                if (selectedByIdentity.TryGetValue(
                        identity,
                        out ExtensionRowAccumulator? selectedRow))
                {
                    selectedRow.Occurrences.Add(
                        declaration!.ToEvidence());
                }
            }
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

    private static bool TryDecodeExtensionPopulationCandidate(
        MetadataReader reader,
        ExtensionDeclarationCandidate candidate,
        MetadataOperationContext operation,
        out DecodedExtensionPopulationCandidate? decoded,
        out MetadataRelationDiagnostic? diagnostic)
    {
        TypeDefinitionHandle receiverContextHandle;
        MethodDefinitionHandle receiverMethodHandle;
        if (candidate.IsProperty)
        {
            if (!TryResolveExtensionPropertyReceiver(
                    reader,
                    candidate,
                    out receiverContextHandle,
                    out receiverMethodHandle))
            {
                decoded = null;
                diagnostic = UnsupportedDiagnostic(
                    MetadataRelationFamily.Extensions,
                    candidate.MetadataToken,
                    "An extension property lacks an exact receiver marker.");
                return false;
            }
        }
        else
        {
            receiverContextHandle = candidate.DeclaringType;
            receiverMethodHandle = candidate.Method;
        }

        TypeDefinition receiverContext =
            reader.GetTypeDefinition(receiverContextHandle);
        MethodDefinition receiverMethod =
            reader.GetMethodDefinition(receiverMethodHandle);
        MetadataMethodSignatureDecodeResult signature =
            MetadataTypeIdentityDecoder.DecodeMethod(
                reader,
                receiverContext,
                receiverMethod,
                operation);
        if (signature
            is MetadataMethodSignatureDecodeResult.Rejected rejected)
        {
            decoded = null;
            diagnostic = UnsupportedDiagnostic(
                MetadataRelationFamily.Extensions,
                candidate.MetadataToken,
                rejected.Detail);
            return false;
        }

        MetadataMethodSignatureIdentity identity =
            ((MetadataMethodSignatureDecodeResult.Decoded)signature)
                .Signature;
        if ((candidate.IsProperty
                && identity.ParameterTypes.Length != 1)
            || identity.ParameterTypes.IsEmpty)
        {
            decoded = null;
            diagnostic = UnsupportedDiagnostic(
                MetadataRelationFamily.Extensions,
                candidate.MetadataToken,
                "An extension declaration has no exact receiver parameter.");
            return false;
        }

        operation.Charge(
            MetadataOperationDimension.RelationshipEdges);
        decoded = new(
            candidate,
            receiverContextHandle,
            receiverMethodHandle,
            identity.ParameterTypes[0]);
        diagnostic = null;
        return true;
    }

    private static bool TryCreateExtensionPopulationDeclaration(
        MetadataReader reader,
        DecodedExtensionPopulationCandidate decoded,
        out DecodedExtensionDeclaration? declaration,
        out MetadataRelationDiagnostic? diagnostic)
    {
        ExtensionDeclarationCandidate candidate = decoded.Candidate;
        if (MetadataTypeDefinitionNameReader.Read(
                reader,
                candidate.DeclaringType)
            is not MetadataTypeDefinitionNameReadResult.Read declaringType)
        {
            declaration = null;
            diagnostic = UnsupportedDiagnostic(
                MetadataRelationFamily.Extensions,
                candidate.MetadataToken,
                "An extension declaration lacks an exact declaring Type identity.");
            return false;
        }

        MemberAnchor anchor;
        if (candidate.IsProperty)
        {
            anchor =
                ApiMemberIdentity
                    .CreateExtensionPropertyDeclarationAnchorInfo(
                        reader,
                        candidate.DeclaringType,
                        reader.GetTypeDefinition(
                            decoded.ReceiverContext),
                        reader.GetMethodDefinition(
                            decoded.ReceiverMethod),
                        reader.GetPropertyDefinition(
                            candidate.Property))
                    .Anchor;
        }
        else
        {
            anchor =
                ApiMemberIdentity.CreateExtensionMethodAnchorInfo(
                    reader,
                    candidate.DeclaringType,
                    reader.GetMethodDefinition(candidate.Method))
                .Anchor;
        }

        declaration = new(
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                candidate.DeclaringType),
            declaringType.Name,
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                decoded.ReceiverContext),
            candidate.MetadataToken,
            MetadataMethodAddress.Create(
                reader,
                decoded.ReceiverMethod),
            anchor,
            decoded.Receiver);
        diagnostic = null;
        return true;
    }

    private static bool TryResolveExtensionPropertyReceiver(
        MetadataReader reader,
        ExtensionDeclarationCandidate candidate,
        out TypeDefinitionHandle receiverContext,
        out MethodDefinitionHandle receiverMethod)
    {
        PropertyDefinition property =
            reader.GetPropertyDefinition(candidate.Property);
        PropertyAccessors accessors = property.GetAccessors();
        if (!TryGetExtensionMarkerName(
                reader,
                property,
                accessors,
                out string? markerName))
        {
            receiverContext = default;
            receiverMethod = default;
            return false;
        }

        TypeDefinition grouping =
            reader.GetTypeDefinition(candidate.GroupingType);
        receiverContext = grouping.GetNestedTypes().FirstOrDefault(
            handle => reader.StringComparer.Equals(
                reader.GetTypeDefinition(handle).Name,
                markerName!));
        if (receiverContext.IsNil)
        {
            receiverMethod = default;
            return false;
        }

        TypeDefinition markerType =
            reader.GetTypeDefinition(receiverContext);
        receiverMethod = markerType.GetMethods().FirstOrDefault(
            handle => reader.StringComparer.Equals(
                reader.GetMethodDefinition(handle).Name,
                "<Extension>$"));
        return !receiverMethod.IsNil;
    }

    private static bool TryGetExtensionMarkerName(
        MetadataReader reader,
        PropertyDefinition property,
        PropertyAccessors accessors,
        out string? markerName) =>
        AttributeReader.TryGetExtensionMarkerName(
                reader,
                property.GetCustomAttributes(),
                out markerName)
            || !accessors.Getter.IsNil
            && AttributeReader.TryGetExtensionMarkerName(
                reader,
                reader.GetMethodDefinition(accessors.Getter)
                    .GetCustomAttributes(),
                out markerName)
            || !accessors.Setter.IsNil
            && AttributeReader.TryGetExtensionMarkerName(
                reader,
                reader.GetMethodDefinition(accessors.Setter)
                    .GetCustomAttributes(),
                out markerName);

    private static bool MatchesReceiver(
        AssemblyReferenceIdentity sourceAssembly,
        MetadataTypeIdentity receiver,
        MetadataExtensionReceiverSelection selection)
    {
        if (ReceiverPrimitive(receiver)
                is MetadataTypeIdentity.Primitive primitive)
        {
            return MatchesPrimitiveReceiver(primitive, selection);
        }

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

    private static bool MatchesPrimitiveReceiver(
        MetadataTypeIdentity.Primitive primitive,
        MetadataExtensionReceiverSelection selection)
    {
        string primitiveName = primitive.Name.ToString();
        string fullName;
        if (string.Equals(
                primitiveName,
                "TypedReference",
                StringComparison.Ordinal))
        {
            fullName = "System.TypedReference";
        }
        else if (!CSharpText.PrimitiveTypeNames.TryToClrFullName(
                primitiveName,
                out fullName))
        {
            return false;
        }

        const string systemPrefix = "System.";
        return ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                selection.Assembly)
            && fullName.StartsWith(
                systemPrefix,
                StringComparison.Ordinal)
            && MetadataTypeDefinitionName.Create(
                    "System",
                    [fullName[systemPrefix.Length..]])
                is MetadataTypeDefinitionNameResult.Valid valid
            && valid.Name == selection.Type;
    }

    private static MetadataTypeIdentity.Primitive? ReceiverPrimitive(
        MetadataTypeIdentity receiver) =>
        receiver switch
        {
            MetadataTypeIdentity.Primitive primitive => primitive,
            MetadataTypeIdentity.ByReference byReference =>
                ReceiverPrimitive(byReference.Element),
            MetadataTypeIdentity.Modified modified =>
                ReceiverPrimitive(modified.Type),
            MetadataTypeIdentity.Pinned pinned =>
                ReceiverPrimitive(pinned.Type),
            _ => null,
        };

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

    private sealed class ExtensionRowAccumulator
    {
        public List<MetadataExtensionRelationEvidence> Occurrences
        { get; } = [];
    }

    private sealed class ExtensionReceiverMatchProvider :
        ISignatureTypeProvider<bool, GenericContext>
    {
        readonly AssemblyReferenceIdentity _sourceAssembly;
        readonly MetadataExtensionReceiverSelection _selection;
        readonly bool _sourceDefinesPrimitiveTypes;

        internal ExtensionReceiverMatchProvider(
            AssemblyReferenceIdentity sourceAssembly,
            MetadataExtensionReceiverSelection selection,
            bool sourceDefinesPrimitiveTypes)
        {
            _sourceAssembly = sourceAssembly;
            _selection = selection;
            _sourceDefinesPrimitiveTypes = sourceDefinesPrimitiveTypes;
        }

        internal bool Rejected { get; private set; }

        public bool GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind) =>
            (_selection.Definition is { } definition
                ? definition.Definition
                    == TypeDefinitionToken.FromHandle(reader, handle)
                : NameMatches(reader, handle))
            && _sourceAssembly.IsEquivalentTo(
                _selection.Assembly);

        public bool GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            if (!NameMatches(reader, handle))
                return false;

            Span<TypeReferenceHandle> chain =
                stackalloc TypeReferenceHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal
                    .TryWalkTypeReferenceResolutionScope(
                        reader,
                        handle,
                        chain,
                        out _,
                        out EntityHandle terminal,
                        out _))
            {
                Rejected = true;
                return false;
            }

            return terminal.Kind switch
            {
                HandleKind.AssemblyReference =>
                    _selection.Assembly.IsEquivalentTo(
                        AssemblyReferenceIdentity.From(
                            reader,
                            (AssemblyReferenceHandle)terminal)),
                HandleKind.ModuleDefinition
                    or HandleKind.ModuleReference =>
                    _sourceAssembly.IsEquivalentTo(
                        _selection.Assembly),
                _ when terminal.IsNil =>
                    _sourceAssembly.IsEquivalentTo(
                        _selection.Assembly),
                _ => Reject(),
            };
        }

        public bool GetTypeFromSpecification(
            MetadataReader reader,
            GenericContext context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
            {
                Rejected = true;
                return false;
            }
            using (scope)
            {
                return reader.GetTypeSpecification(handle)
                    .DecodeSignature(this, context);
            }
        }

        // Primitive element types name local definitions only in an image
        // that defines them, matching the rich route's receiver decoding.
        public bool GetPrimitiveType(PrimitiveTypeCode typeCode) =>
            _sourceDefinesPrimitiveTypes
            && _sourceAssembly.IsEquivalentTo(_selection.Assembly)
            && IsSelectedSystemType(
                typeCode switch
                {
                    PrimitiveTypeCode.Boolean => "Boolean",
                    PrimitiveTypeCode.Byte => "Byte",
                    PrimitiveTypeCode.SByte => "SByte",
                    PrimitiveTypeCode.Char => "Char",
                    PrimitiveTypeCode.Int16 => "Int16",
                    PrimitiveTypeCode.UInt16 => "UInt16",
                    PrimitiveTypeCode.Int32 => "Int32",
                    PrimitiveTypeCode.UInt32 => "UInt32",
                    PrimitiveTypeCode.Int64 => "Int64",
                    PrimitiveTypeCode.UInt64 => "UInt64",
                    PrimitiveTypeCode.Single => "Single",
                    PrimitiveTypeCode.Double => "Double",
                    PrimitiveTypeCode.IntPtr => "IntPtr",
                    PrimitiveTypeCode.UIntPtr => "UIntPtr",
                    PrimitiveTypeCode.String => "String",
                    PrimitiveTypeCode.Object => "Object",
                    PrimitiveTypeCode.Void => "Void",
                    PrimitiveTypeCode.TypedReference =>
                        "TypedReference",
                    _ => "",
                });

        public bool GetGenericInstantiation(
            bool genericType,
            ImmutableArray<bool> typeArguments) =>
            genericType;

        public bool GetByReferenceType(bool elementType) =>
            elementType;

        public bool GetModifiedType(
            bool modifier,
            bool unmodifiedType,
            bool isRequired) =>
            unmodifiedType;

        public bool GetPinnedType(bool elementType) =>
            elementType;

        public bool GetSZArrayType(bool elementType) => false;

        public bool GetArrayType(
            bool elementType,
            ArrayShape shape) =>
            false;

        public bool GetPointerType(bool elementType) => false;

        public bool GetGenericTypeParameter(
            GenericContext context,
            int index) =>
            false;

        public bool GetGenericMethodParameter(
            GenericContext context,
            int index) =>
            false;

        public bool GetFunctionPointerType(
            MethodSignature<bool> signature) =>
            false;

        bool NameMatches(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            MetadataTypeDefinitionNameMatchResult result =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    handle,
                    _selection.Type,
                    out _);
            if (result
                is MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                Rejected = true;
            }
            return result
                is MetadataTypeDefinitionNameMatchResult.Match;
        }

        bool NameMatches(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            MetadataTypeDefinitionNameMatchResult result =
                MetadataTypeDefinitionName.Matches(
                    reader,
                    handle,
                    _selection.Type,
                    out _);
            if (result
                is MetadataTypeDefinitionNameMatchResult.Rejected)
            {
                Rejected = true;
            }
            return result
                is MetadataTypeDefinitionNameMatchResult.Match;
        }

        bool IsSelectedSystemType(string name) =>
            name.Length > 0
            && string.Equals(
                _selection.Type.Namespace,
                "System",
                StringComparison.Ordinal)
            && _selection.Type.Segments.Length == 1
            && string.Equals(
                _selection.Type.Segments[0],
                name,
                StringComparison.Ordinal);

        bool Reject()
        {
            Rejected = true;
            return false;
        }
    }

    private sealed record DecodedExtensionPopulationCandidate(
        ExtensionDeclarationCandidate Candidate,
        TypeDefinitionHandle ReceiverContext,
        MethodDefinitionHandle ReceiverMethod,
        MetadataTypeIdentity Receiver);
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
