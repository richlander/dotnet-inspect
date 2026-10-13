using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json.Serialization;

namespace ILInspector.Metadata;

public sealed record MetadataTypeDocumentInspectionRequest
{
    public MetadataTypeDocumentInspectionRequest(
        MetadataTypeDefinitionName type,
        MetadataTypeMemberGroupPopulationRequest? declarations = null,
        MetadataTypeDefinitionAddress? expectedDeclarationPopulationType =
            null,
        MetadataTypeDeclarationPopulationRequest? memberDeclarations =
            null,
        MetadataExactTypeInput? exactInput = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        if (exactInput is not null && !type.Equals(exactInput.Type))
        {
            throw new ArgumentException(
                "The exact input must describe the requested Type.",
                nameof(exactInput));
        }
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
        if (memberDeclarations is not null
            && memberDeclarations.Type != type)
        {
            throw new ArgumentException(
                "The Type document and complete Member declarations must identify the same Type.",
                nameof(memberDeclarations));
        }
        if (declarations is not null
            && memberDeclarations is not null)
        {
            throw new ArgumentException(
                "A Type document request cannot inspect compact and complete Member declarations together.",
                nameof(memberDeclarations));
        }

        Declarations = declarations;
        ExpectedDeclarationPopulationType =
            expectedDeclarationPopulationType;
        MemberDeclarations = memberDeclarations;
        ExactInput = exactInput;
    }

    public MetadataTypeDefinitionName Type { get; }
    public MetadataTypeMemberGroupPopulationRequest? Declarations { get; }
    public MetadataTypeDefinitionAddress? ExpectedDeclarationPopulationType
    { get; }
    public MetadataTypeDeclarationPopulationRequest? MemberDeclarations
    { get; }
    public MetadataExactTypeInput? ExactInput { get; }
}

public sealed record MetadataTypeDocument(
    MetadataTypeDeclarationEvidence Subject,
    MetadataTypeDocumentDeclarations Declarations,
    MetadataTypeDeclarationPopulationOutcome? MemberDeclarations = null)
{
    /// <summary>The uniquely matched Metadata identity detached as contained byte ranges.</summary>
    [JsonIgnore]
    public MetadataUtf8TypeDefinitionName? MatchedName
    {
        get;
        internal init;
    }

    /// <summary>Whether exact byte proof authorized reuse of host-owned query bytes.</summary>
    [JsonIgnore]
    public bool ReusedExactInput
    {
        get;
        internal init;
    }
}

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
        PEReader peReader,
        MetadataReader reader,
        MetadataTypeDocumentInspectionRequest request,
        ApiSurfaceExtractionBounds bounds,
        Func<
            MetadataTypeDefinitionAddress,
            CancellationToken,
            MetadataTypeDeclarationResult> postDeclaration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peReader);
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
            MetadataTypeDeclarationPopulationOutcome? memberDeclarations =
                request.MemberDeclarations is null
                    ? null
                    : MetadataTypeDeclarationPopulationInspection
                        .ReadResolved(
                            reader,
                            request.MemberDeclarations,
                            bounds,
                            resolved,
                            cancellationToken);
            MetadataExactTypeMatch? exactMatch =
                request.ExactInput is null
                    ? null
                    : MetadataUtf8TypeDefinitionNameReader.Match(
                        peReader,
                        reader,
                        resolved.Handle,
                        request.ExactInput);
            MetadataUtf8TypeDefinitionName matchedName =
                exactMatch?.Name
                ?? MetadataUtf8TypeDefinitionNameReader.Detach(
                    peReader,
                    reader,
                    resolved.Handle);
            return new MetadataTypeDocumentInspectionOutcome.Available(
                new(
                    subject,
                    declarations,
                    memberDeclarations)
                {
                    MatchedName = matchedName,
                    ReusedExactInput = exactMatch is not null,
                });
        }
        catch (Exception exception) when (
            MetadataTypeMemberCompositionInspection.IsMetadataFailure(
                exception))
        {
            return new MetadataTypeDocumentInspectionOutcome.Failed();
        }
    }
}
