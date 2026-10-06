using System.Reflection.Metadata;

namespace ILInspector.Metadata;

public sealed record MetadataTypeDocumentInspectionRequest
{
    public MetadataTypeDocumentInspectionRequest(
        MetadataTypeDefinitionName type,
        MetadataTypeMemberGroupPopulationRequest? declarations = null,
        MetadataTypeDefinitionAddress? expectedDeclarationPopulationType =
            null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        if (declarations is not null && declarations.Type != type)
        {
            throw new ArgumentException(
                "The Type document and Member-group population must identify the same Type.",
                nameof(declarations));
        }
        if (declarations is null
            && expectedDeclarationPopulationType is not null)
        {
            throw new ArgumentException(
                "A Member-group population binding requires a population request.",
                nameof(expectedDeclarationPopulationType));
        }

        Declarations = declarations;
        ExpectedDeclarationPopulationType =
            expectedDeclarationPopulationType;
    }

    public MetadataTypeDefinitionName Type { get; }
    public MetadataTypeMemberGroupPopulationRequest? Declarations { get; }
    public MetadataTypeDefinitionAddress? ExpectedDeclarationPopulationType
    { get; }
}

public sealed record MetadataTypeDocument(
    MetadataTypeDeclarationEvidence Subject,
    MetadataTypeDocumentDeclarations Declarations);

public abstract record MetadataTypeDocumentDeclarations
{
    private protected MetadataTypeDocumentDeclarations()
    {
    }

    public sealed record NotRequested
        : MetadataTypeDocumentDeclarations;

    public sealed record BindingMismatch
        : MetadataTypeDocumentDeclarations;

    public sealed record Inspected(
        MetadataTypeMemberGroupPopulationOutcome Outcome)
        : MetadataTypeDocumentDeclarations;
}

public enum MetadataTypeDocumentBound
{
    MetadataRows,
}

public abstract record MetadataTypeDocumentInspectionOutcome
{
    private protected MetadataTypeDocumentInspectionOutcome()
    {
    }

    public sealed record Available(MetadataTypeDocument Document)
        : MetadataTypeDocumentInspectionOutcome;

    public sealed record TypeNotFound
        : MetadataTypeDocumentInspectionOutcome;

    public sealed record TypeAmbiguous
        : MetadataTypeDocumentInspectionOutcome;

    public sealed record Incomplete(
        MetadataTypeDocumentBound Bound,
        long Limit,
        long Measured)
        : MetadataTypeDocumentInspectionOutcome;

    public sealed record Failed
        : MetadataTypeDocumentInspectionOutcome;
}

internal static class MetadataTypeDocumentInspection
{
    internal static MetadataTypeDocumentInspectionOutcome Read(
        MetadataReader reader,
        MetadataTypeDocumentInspectionRequest request,
        ApiSurfaceExtractionBounds bounds,
        Func<
            MetadataTypeDefinitionAddress,
            CancellationToken,
            MetadataTypeDeclarationResult> postDeclaration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(postDeclaration);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            MetadataExactTypeDefinitionResolution resolution =
                MetadataExactTypeDefinitionInspection.Resolve(
                    reader,
                    request.Type,
                    bounds,
                    cancellationToken);
            if (resolution is not
                MetadataExactTypeDefinitionResolution.Resolved resolved)
            {
                return resolution switch
                {
                    MetadataExactTypeDefinitionResolution.TypeNotFound =>
                        new MetadataTypeDocumentInspectionOutcome
                            .TypeNotFound(),
                    MetadataExactTypeDefinitionResolution.TypeAmbiguous =>
                        new MetadataTypeDocumentInspectionOutcome
                            .TypeAmbiguous(),
                    MetadataExactTypeDefinitionResolution.Incomplete
                        incomplete =>
                            new MetadataTypeDocumentInspectionOutcome
                                .Incomplete(
                                    MetadataTypeDocumentBound.MetadataRows,
                                    incomplete.Limit,
                                    incomplete.Measured),
                    MetadataExactTypeDefinitionResolution.Failed =>
                        new MetadataTypeDocumentInspectionOutcome.Failed(),
                    _ => throw new InvalidOperationException(
                        "Unknown exact Type definition resolution."),
                };
            }

            MetadataTypeDeclarationResult declaration =
                postDeclaration(
                    resolved.Address(reader),
                    cancellationToken);
            if (declaration is
                MetadataTypeDeclarationResult.Rejected rejected)
            {
                MetadataTypeDeclarationFailure failure =
                    rejected.Failure;
                return failure.Reason
                        is MetadataTypeDeclarationFailureReason
                            .BudgetExceeded
                    && failure.BudgetDimension
                        is MetadataOperationDimension.MetadataRows
                    && failure.BudgetLimit is { } limit
                    && failure.BudgetAttempted is { } measured
                        ? new MetadataTypeDocumentInspectionOutcome
                            .Incomplete(
                                MetadataTypeDocumentBound.MetadataRows,
                                limit,
                                measured)
                        : new MetadataTypeDocumentInspectionOutcome.Failed();
            }

            MetadataTypeDeclarationEvidence subject =
                ((MetadataTypeDeclarationResult.Posted)declaration)
                    .Evidence;
            MetadataTypeDocumentDeclarations declarations =
                request.Declarations is null
                    ? new MetadataTypeDocumentDeclarations.NotRequested()
                    : request.ExpectedDeclarationPopulationType
                        is { } expected
                        && expected != subject.Type
                            ? new MetadataTypeDocumentDeclarations
                                .BindingMismatch()
                            : new MetadataTypeDocumentDeclarations.Inspected(
                                MetadataTypeMemberGroupPopulationInspection
                                    .ReadResolved(
                                        reader,
                                        request.Declarations,
                                        bounds,
                                        resolved,
                                        cancellationToken));
            return new MetadataTypeDocumentInspectionOutcome.Available(
                new(subject, declarations));
        }
        catch (Exception exception) when (
            MetadataTypeMemberCompositionInspection.IsMetadataFailure(
                exception))
        {
            return new MetadataTypeDocumentInspectionOutcome.Failed();
        }
    }
}
