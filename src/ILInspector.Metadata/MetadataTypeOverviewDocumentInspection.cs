using System.Reflection.Metadata;

namespace ILInspector.Metadata;

public sealed record MetadataTypeOverviewDocumentInspectionRequest
{
    public MetadataTypeOverviewDocumentInspectionRequest(
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

public sealed record MetadataTypeOverviewDocument(
    MetadataTypeDeclarationEvidence Subject,
    MetadataTypeOverviewDocumentDeclarations Declarations);

public abstract record MetadataTypeOverviewDocumentDeclarations
{
    private protected MetadataTypeOverviewDocumentDeclarations()
    {
    }

    public sealed record NotRequested
        : MetadataTypeOverviewDocumentDeclarations;

    public sealed record BindingMismatch
        : MetadataTypeOverviewDocumentDeclarations;

    public sealed record Inspected(
        MetadataTypeMemberGroupPopulationOutcome Outcome)
        : MetadataTypeOverviewDocumentDeclarations;
}

public enum MetadataTypeOverviewDocumentBound
{
    MetadataRows,
}

public abstract record MetadataTypeOverviewDocumentInspectionOutcome
{
    private protected MetadataTypeOverviewDocumentInspectionOutcome()
    {
    }

    public sealed record Available(MetadataTypeOverviewDocument Document)
        : MetadataTypeOverviewDocumentInspectionOutcome;

    public sealed record TypeNotFound
        : MetadataTypeOverviewDocumentInspectionOutcome;

    public sealed record TypeAmbiguous
        : MetadataTypeOverviewDocumentInspectionOutcome;

    public sealed record Incomplete(
        MetadataTypeOverviewDocumentBound Bound,
        long Limit,
        long Measured)
        : MetadataTypeOverviewDocumentInspectionOutcome;

    public sealed record Failed
        : MetadataTypeOverviewDocumentInspectionOutcome;
}

internal static class MetadataTypeOverviewDocumentInspection
{
    internal static MetadataTypeOverviewDocumentInspectionOutcome Read(
        MetadataReader reader,
        MetadataTypeOverviewDocumentInspectionRequest request,
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
                        new MetadataTypeOverviewDocumentInspectionOutcome
                            .TypeNotFound(),
                    MetadataExactTypeDefinitionResolution.TypeAmbiguous =>
                        new MetadataTypeOverviewDocumentInspectionOutcome
                            .TypeAmbiguous(),
                    MetadataExactTypeDefinitionResolution.Incomplete
                        incomplete =>
                            new MetadataTypeOverviewDocumentInspectionOutcome
                                .Incomplete(
                                    MetadataTypeOverviewDocumentBound.MetadataRows,
                                    incomplete.Limit,
                                    incomplete.Measured),
                    MetadataExactTypeDefinitionResolution.Failed =>
                        new MetadataTypeOverviewDocumentInspectionOutcome.Failed(),
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
                        ? new MetadataTypeOverviewDocumentInspectionOutcome
                            .Incomplete(
                                MetadataTypeOverviewDocumentBound.MetadataRows,
                                limit,
                                measured)
                        : new MetadataTypeOverviewDocumentInspectionOutcome.Failed();
            }

            MetadataTypeDeclarationEvidence subject =
                ((MetadataTypeDeclarationResult.Posted)declaration)
                    .Evidence;
            MetadataTypeOverviewDocumentDeclarations declarations =
                request.Declarations is null
                    ? new MetadataTypeOverviewDocumentDeclarations.NotRequested()
                    : request.ExpectedDeclarationPopulationType
                        is { } expected
                        && expected != subject.Type
                            ? new MetadataTypeOverviewDocumentDeclarations
                                .BindingMismatch()
                            : new MetadataTypeOverviewDocumentDeclarations.Inspected(
                                MetadataTypeMemberGroupPopulationInspection
                                    .ReadResolved(
                                        reader,
                                        request.Declarations,
                                        bounds,
                                        resolved,
                                        cancellationToken));
            return new MetadataTypeOverviewDocumentInspectionOutcome.Available(
                new(subject, declarations));
        }
        catch (Exception exception) when (
            MetadataTypeMemberCompositionInspection.IsMetadataFailure(
                exception))
        {
            return new MetadataTypeOverviewDocumentInspectionOutcome.Failed();
        }
    }
}
