using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

internal static partial class MetadataRelationInspection
{
    internal static MetadataRelationInspectionOutcome Execute(
        PEReader image,
        MetadataRelationInspectionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        MetadataImageFormatResult format =
            MetadataImageFormatClassifier.Classify(image);
        if (format is not MetadataImageFormatResult.SupportedEcma335)
        {
            return new MetadataRelationInspectionOutcome.Rejected(
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
            reader = image.GetMetadataReader(
                MetadataReaderOptions.None);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or OverflowException)
        {
            return new MetadataRelationInspectionOutcome.Rejected(
                new MetadataImageFormatResult.MalformedRoot(
                    MetadataRootMalformedReason
                        .UnmappableMetadataDirectory),
                exception.Message);
        }

        ValidateTypeScope(reader, request);
        using var operation =
            new MetadataOperationContext(request.Policy);
        MetadataImageAdmissionResult admission =
            operation.AdmitImage(reader);
        if (admission is MetadataImageAdmissionResult.Rejected rejected)
        {
            MetadataRelationDiagnostic diagnostic = new(
                request.Families[0],
                MetadataRelationDiagnosticKind.Limit,
                null,
                "The metadata image exceeds the operation row budget.",
                MetadataOperationDimension.MetadataRows,
                rejected.Failure.MaxMetadataRows,
                rejected.Failure.ImageMetadataRows);
            return new MetadataRelationInspectionOutcome.Available(
                new(
                    Receipt(reader, request, operation),
                    Unavailable<MetadataHierarchyRelationEvidence>(
                        request,
                        MetadataRelationFamily.Hierarchy,
                        diagnostic),
                    Unavailable<MetadataExtensionRelationEvidence>(
                        request,
                        MetadataRelationFamily.Extensions,
                        diagnostic),
                    Unavailable<MetadataAssemblyReferenceRelationEvidence>(
                        request,
                        MetadataRelationFamily.AssemblyReferences,
                        diagnostic),
                    Unavailable<MetadataSignatureRelationEvidence>(
                        request,
                        MetadataRelationFamily.Signatures,
                        diagnostic)));
        }

        MetadataVisibilityClassification visibility;
        try
        {
            visibility = MetadataVisibility.ClassifyAll(reader);
        }
        catch (BadImageFormatException exception)
        {
            return new MetadataRelationInspectionOutcome.Available(
                FailedAll(
                    reader,
                    request,
                    operation,
                    exception.Message));
        }

        MetadataRelationFamilyResult<MetadataHierarchyRelationEvidence>
            hierarchy =
                request.Includes(MetadataRelationFamily.Hierarchy)
                    ? ScanHierarchy(
                        reader,
                        request,
                        visibility,
                        operation,
                        cancellationToken)
                    : MetadataRelationFamilyResult<
                        MetadataHierarchyRelationEvidence>.NotRequested();
        MetadataRelationFamilyResult<MetadataExtensionRelationEvidence>
            extensions =
                request.Includes(MetadataRelationFamily.Extensions)
                    ? ScanExtensions(
                        image,
                        reader,
                        request,
                        operation,
                        cancellationToken)
                    : MetadataRelationFamilyResult<
                        MetadataExtensionRelationEvidence>.NotRequested();
        MetadataRelationFamilyResult<
            MetadataAssemblyReferenceRelationEvidence> references =
                request.Includes(
                    MetadataRelationFamily.AssemblyReferences)
                    ? ScanAssemblyReferences(
                        reader,
                        operation,
                        cancellationToken)
                    : MetadataRelationFamilyResult<
                        MetadataAssemblyReferenceRelationEvidence>
                        .NotRequested();
        MetadataRelationFamilyResult<MetadataSignatureRelationEvidence>
            signatures =
                request.Includes(MetadataRelationFamily.Signatures)
                    ? ScanSignatures(
                        reader,
                        request,
                        visibility,
                        operation,
                        cancellationToken)
                    : MetadataRelationFamilyResult<
                        MetadataSignatureRelationEvidence>.NotRequested();

        return new MetadataRelationInspectionOutcome.Available(
            new(
                Receipt(reader, request, operation),
                hierarchy,
                extensions,
                references,
                signatures));
    }

    private static void ValidateTypeScope(
        MetadataReader reader,
        MetadataRelationInspectionRequest request)
    {
        if (request.TypeScope.IsEmpty)
            return;

        Guid moduleVersionId =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        int rowCount =
            reader.GetTableRowCount(TableIndex.TypeDef);
        foreach (MetadataTypeDefinitionAddress address
            in request.TypeScope)
        {
            int row = address.Definition.Value & 0x00FFFFFF;
            if (address.ModuleVersionId != moduleVersionId
                || row <= 0
                || row > rowCount)
            {
                throw new ArgumentException(
                    "A Metadata relation type scope must address a TypeDef "
                    + "in the exact inspected image.",
                    nameof(request));
            }
        }
    }

    private static MetadataTypeDefinitionName? ReadTypeName(
        MetadataReader reader,
        TypeDefinitionHandle handle,
        MetadataRelationFamily family,
        ImmutableArray<MetadataRelationDiagnostic>.Builder diagnostics)
    {
        MetadataTypeDefinitionNameReadResult result =
            MetadataTypeDefinitionNameReader.Read(reader, handle);
        if (result is MetadataTypeDefinitionNameReadResult.Read read)
            return read.Name;

        var rejected =
            (MetadataTypeDefinitionNameReadResult.Rejected)result;
        diagnostics.Add(
            MalformedDiagnostic(
                family,
                MetadataTokens.GetToken(handle),
                rejected.Failure.Detail));
        return null;
    }

    private static MetadataRelationInspectionReceipt Receipt(
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        MetadataOperationContext operation) =>
        new(
            reader.GetGuid(reader.GetModuleDefinition().Mvid),
            reader.IsAssembly
                ? AssemblyReferenceIdentity
                    .FromAssemblyDefinition(reader)
                : null,
            request.Families,
            operation.Counters);

    private static MetadataRelationInspectionResult FailedAll(
        MetadataReader reader,
        MetadataRelationInspectionRequest request,
        MetadataOperationContext operation,
        string detail) =>
        new(
            Receipt(reader, request, operation),
            Failed<MetadataHierarchyRelationEvidence>(
                request,
                MetadataRelationFamily.Hierarchy,
                detail),
            Failed<MetadataExtensionRelationEvidence>(
                request,
                MetadataRelationFamily.Extensions,
                detail),
            Failed<MetadataAssemblyReferenceRelationEvidence>(
                request,
                MetadataRelationFamily.AssemblyReferences,
                detail),
            Failed<MetadataSignatureRelationEvidence>(
                request,
                MetadataRelationFamily.Signatures,
                detail));

    private static MetadataRelationFamilyResult<TEvidence>
        CompleteOrPartial<TEvidence>(
            ImmutableArray<TEvidence>.Builder evidence,
            ImmutableArray<MetadataRelationDiagnostic>.Builder diagnostics,
            MetadataRelationCoverage? coverage = null)
        => new(
            true,
            diagnostics.Count == 0
                ? MetadataRelationFamilyDisposition.Complete
                : MetadataRelationFamilyDisposition.Partial,
            coverage ?? Coverage(evidence.Count, diagnostics),
            evidence,
            diagnostics);

    private static MetadataRelationFamilyResult<TEvidence>
        Unavailable<TEvidence>(
            MetadataRelationInspectionRequest request,
            MetadataRelationFamily family,
            MetadataRelationDiagnostic diagnostic) =>
        request.Includes(family)
            ? new(
                true,
                MetadataRelationFamilyDisposition.Unavailable,
                new(1, 0, 0, 1, 0),
                [],
                [diagnostic with { Family = family }])
            : MetadataRelationFamilyResult<TEvidence>.NotRequested();

    private static MetadataRelationFamilyResult<TEvidence>
        Failed<TEvidence>(
            MetadataRelationInspectionRequest request,
            MetadataRelationFamily family,
            string detail) =>
        request.Includes(family)
            ? new(
                true,
                MetadataRelationFamilyDisposition.Failed,
                new(1, 0, 0, 1, 0),
                [],
                [
                    new(
                        family,
                        MetadataRelationDiagnosticKind.MalformedMetadata,
                        null,
                        detail),
                ])
            : MetadataRelationFamilyResult<TEvidence>.NotRequested();

    private static MetadataRelationCoverage Coverage(
        int examined,
        ImmutableArray<MetadataRelationDiagnostic>.Builder diagnostics)
    {
        int limited = diagnostics.Count(static diagnostic =>
            diagnostic.Kind == MetadataRelationDiagnosticKind.Limit);
        int unavailable = diagnostics.Count - limited;
        return new(
            examined + unavailable + limited,
            examined,
            0,
            unavailable,
            limited);
    }

    private static MetadataRelationDiagnostic LimitDiagnostic(
        MetadataRelationFamily family,
        MetadataOperationBudgetExceededException exception) =>
        new(
            family,
            MetadataRelationDiagnosticKind.Limit,
            null,
            "The Metadata relation operation exceeded its work budget.",
            exception.Dimension,
            exception.Limit,
            exception.AttemptedCharge);

    private static MetadataRelationDiagnostic MalformedDiagnostic(
        MetadataRelationFamily family,
        int? metadataToken,
        string detail) =>
        new(
            family,
            MetadataRelationDiagnosticKind.MalformedMetadata,
            metadataToken,
            detail);

    private static MetadataRelationDiagnostic UnsupportedDiagnostic(
        MetadataRelationFamily family,
        int? metadataToken,
        string detail) =>
        new(
            family,
            MetadataRelationDiagnosticKind.UnsupportedShape,
            metadataToken,
            detail);
}
