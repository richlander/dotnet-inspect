using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal enum AnalysisLibraryBodyUseTerminalDisposition
{
    Settled,
    Complete,
    Qualified,
    Partial,
}

internal sealed record AnalysisLibraryBodyUseTerminalEvidence(
    AnalysisLibraryBodyUseReceipt Receipt,
    AnalysisLibraryBodyUseTerminalDisposition Disposition,
    AnalysisLibraryBodyUseCoverage Coverage,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);

internal abstract record AnalysisLibraryBodyUseAnswer
{
    private protected AnalysisLibraryBodyUseAnswer()
    {
    }

    internal sealed record Exists(
        bool Value,
        AnalysisLibraryBodyUseTerminalEvidence Evidence)
        : AnalysisLibraryBodyUseAnswer;

    internal sealed record Count(
        int Value,
        AnalysisLibraryBodyUseTerminalEvidence Evidence)
        : AnalysisLibraryBodyUseAnswer;

    internal sealed record Rows(AnalysisLibraryBodyUseResult Result)
        : AnalysisLibraryBodyUseAnswer;
}

internal abstract record AnalysisLibraryBodyUseQueryOutcome
{
    private protected AnalysisLibraryBodyUseQueryOutcome()
    {
    }

    internal sealed record Available(AnalysisLibraryBodyUseAnswer Answer)
        : AnalysisLibraryBodyUseQueryOutcome;

    internal sealed record Rejected(
        AnalysisLibraryBodyUseRejectionKind Kind,
        string Detail)
        : AnalysisLibraryBodyUseQueryOutcome;
}

/// <summary>
/// Produces qualified whole-Library Type uses from managed method bodies.
/// </summary>
public static class AnalysisLibraryBodyUseService
{
    public static AnalysisLibraryBodyUseOutcome ExecutePath(
        string path,
        AnalysisLibraryBodyUseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        using FileStream stream = File.OpenRead(path);
        using var image = new PEReader(
            stream,
            PEStreamOptions.PrefetchEntireImage);
        return Rows(
            Query(
                path,
                image,
                ProducerTerminal.Rows,
                request.Limits,
                cancellationToken));
    }

    public static AnalysisLibraryBodyUseOutcome ExecuteImage(
        string sourceName,
        ImmutableArray<byte> image,
        AnalysisLibraryBodyUseRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A prefetched PE image is required.",
                nameof(image));
        }
        ArgumentNullException.ThrowIfNull(request);
        using var reader = new PEReader(image);
        return Rows(
            Query(
                sourceName,
                reader,
                ProducerTerminal.Rows,
                request.Limits,
                cancellationToken));
    }

    internal static AnalysisLibraryBodyUseQueryOutcome ExecuteTerminalImage(
        string sourceName,
        ImmutableArray<byte> image,
        ProducerTerminal terminal,
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (image.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A prefetched PE image is required.",
                nameof(image));
        }
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        using var reader = new PEReader(image);
        return Query(
            sourceName,
            reader,
            terminal,
            limits,
            cancellationToken);
    }

    static AnalysisLibraryBodyUseQueryOutcome Query(
        string sourceName,
        PEReader image,
        ProducerTerminal terminal,
        AnalysisLibraryBodyUseLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssemblyTypeDeclarationInventoryOutcome inventoryOutcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                image,
                limits.MaximumTypeDefinitions,
                limits.MaximumRetainedTextCharacters);
        if (inventoryOutcome
            is AssemblyTypeDeclarationInventoryOutcome.Incomplete incomplete)
        {
            return new AnalysisLibraryBodyUseQueryOutcome.Rejected(
                AnalysisLibraryBodyUseRejectionKind.Limit,
                $"The Type inventory exceeded {incomplete.Bound}.");
        }
        if (inventoryOutcome
            is AssemblyTypeDeclarationInventoryOutcome.Rejected rejected)
        {
            return new AnalysisLibraryBodyUseQueryOutcome.Rejected(
                AnalysisLibraryBodyUseRejectionKind.UnsupportedImage,
                rejected.Failure.Detail);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            MetadataReader metadata = image.GetMetadataReader();
            if (!metadata.IsAssembly)
            {
                return new AnalysisLibraryBodyUseQueryOutcome.Rejected(
                    AnalysisLibraryBodyUseRejectionKind
                        .MissingAssemblyIdentity,
                    "A Library body-use population requires an assembly manifest.");
            }

            Guid mvid = metadata.GetGuid(
                metadata.GetModuleDefinition().Mvid);
            if (mvid == Guid.Empty)
            {
                return new AnalysisLibraryBodyUseQueryOutcome.Rejected(
                    AnalysisLibraryBodyUseRejectionKind
                        .MissingAssemblyIdentity,
                    "The metadata image has an empty module version identifier.");
            }

            var producer =
                new AnalysisLibraryBodyUseProducer(
                    limits,
                    cancellationToken);
            ProducerPlanResult plan =
                ProducerPlanner.Plan(
                    [new ProducerRequest(
                        producer,
                        terminal)]);
            if (plan is not ProducerPlanResult.Accepted accepted)
            {
                return new AnalysisLibraryBodyUseQueryOutcome.Rejected(
                    AnalysisLibraryBodyUseRejectionKind.Planning,
                    "The Analysis body-use producer could not be planned.");
            }

            MethodDefinitionExecution execution =
                MethodDefinitionExecution.Execute(
                    accepted.Description,
                    sourceName,
                    image);
            ProducerResult<AnalysisLibraryBodyUseProducer.Result> result =
                execution.ResultOf(producer);
            if (!result.HasValue || result.Value is not { } value)
            {
                string detail =
                    result.Failure?.Message
                    ?? result.Critical?.Message
                    ?? "The Analysis body-use producer did not complete.";
                return new AnalysisLibraryBodyUseQueryOutcome.Rejected(
                    result.Critical is null
                        ? AnalysisLibraryBodyUseRejectionKind.Execution
                        : AnalysisLibraryBodyUseRejectionKind.Limit,
                    detail);
            }

            var inventory =
                ((AssemblyTypeDeclarationInventoryOutcome.Read)
                    inventoryOutcome).Inventory;
            var receipt =
                new AnalysisLibraryBodyUseReceipt(
                    mvid,
                    inventory.Identity,
                    execution.Receipt);
            return new AnalysisLibraryBodyUseQueryOutcome.Available(
                Answer(
                    terminal,
                    metadata,
                    inventory,
                    receipt,
                    result,
                    value));
        }
        catch (Exception exception)
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(exception))
        {
            return new AnalysisLibraryBodyUseQueryOutcome.Rejected(
                AnalysisLibraryBodyUseRejectionKind.MalformedImage,
                ProducerFailure.Describe(exception));
        }
    }

    static AnalysisLibraryBodyUseAnswer Answer(
        ProducerTerminal terminal,
        MetadataReader metadata,
        AssemblyTypeDeclarationInventory inventory,
        AnalysisLibraryBodyUseReceipt receipt,
        ProducerResult<AnalysisLibraryBodyUseProducer.Result> result,
        AnalysisLibraryBodyUseProducer.Result value)
    {
        if (terminal == ProducerTerminal.Rows)
        {
            AnalysisLibraryBodyUseProjection projection = Project(
                metadata,
                inventory,
                value);
            return new AnalysisLibraryBodyUseAnswer.Rows(
                new(
                    receipt,
                    projection.Disposition,
                    projection.Types,
                    projection.Occurrences,
                    projection.PhysicalEvidence,
                    projection.Coverage,
                    projection.Diagnostics));
        }

        var evidence = new AnalysisLibraryBodyUseTerminalEvidence(
            receipt,
            TerminalDisposition(
                value,
                result.Outcome == ProducerOutcome.Stopped),
            value.Coverage,
            value.Diagnostics);
        return terminal == ProducerTerminal.Count
            ? new AnalysisLibraryBodyUseAnswer.Count(
                value.OccurrenceCount,
                evidence)
            : new AnalysisLibraryBodyUseAnswer.Exists(
                value.OccurrenceCount != 0,
                evidence);
    }

    internal static AnalysisLibraryBodyUseTerminalDisposition
        TerminalDisposition(
            AnalysisLibraryBodyUseProducer.Result value,
            bool settled)
    {
        if (settled)
            return AnalysisLibraryBodyUseTerminalDisposition.Settled;
        AnalysisLibraryBodyUseDisposition disposition =
            Disposition(value);
        return disposition switch
        {
            AnalysisLibraryBodyUseDisposition.Complete =>
                AnalysisLibraryBodyUseTerminalDisposition.Complete,
            AnalysisLibraryBodyUseDisposition.Qualified =>
                AnalysisLibraryBodyUseTerminalDisposition.Qualified,
            AnalysisLibraryBodyUseDisposition.Partial =>
                AnalysisLibraryBodyUseTerminalDisposition.Partial,
            _ => throw new ArgumentOutOfRangeException(nameof(value)),
        };
    }

    internal static AnalysisLibraryBodyUseProjection Project(
        MetadataReader metadata,
        AssemblyTypeDeclarationInventory inventory,
        AnalysisLibraryBodyUseProducer.Result value)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Occurrences.IsDefault || value.Bodies.IsDefault)
        {
            throw new InvalidOperationException(
                "Rows projection requires retained occurrence and body rows.");
        }

        ImmutableArray<AnalysisLibraryBodyUseType> types =
            BuildTypes(metadata, inventory);
        var names = types.ToDictionary(
            static type => type.Type,
            static type => type.Name);
        ImmutableArray<AnalysisLibraryBodyUseOccurrence> occurrences =
            [.. value.Occurrences.Select(
                occurrence => ProjectOccurrence(
                    metadata,
                    names,
                    occurrence))];
        ImmutableArray<AnalysisLibraryBodyUsePhysicalEvidence> physical =
            [.. value.Bodies.Select(
                body =>
                {
                    MetadataTypeDefinitionAddress address =
                        MetadataTypeDefinitionAddress.FromHandle(
                            metadata,
                            body.PhysicalType);
                    return new AnalysisLibraryBodyUsePhysicalEvidence(
                        address,
                        names[address],
                        body.PhysicalMethodToken,
                        body.Fidelity);
                })];
        AnalysisLibraryBodyUseDisposition disposition =
            Disposition(value);
        return new(
            disposition,
            types,
            occurrences,
            physical,
            value.Coverage,
            value.Diagnostics);
    }

    static AnalysisLibraryBodyUseDisposition Disposition(
        AnalysisLibraryBodyUseProducer.Result value)
    {
        bool partial = value.Diagnostics.Any(
            static diagnostic =>
                diagnostic.Kind
                    is not AnalysisLibraryBodyUseDiagnosticKind
                        .UnavailableLogicalOwner);
        return partial
            ? AnalysisLibraryBodyUseDisposition.Partial
            : value.Coverage.BodiesPhysicalOnly != 0
                ? AnalysisLibraryBodyUseDisposition.Qualified
                : AnalysisLibraryBodyUseDisposition.Complete;
    }

    static AnalysisLibraryBodyUseOutcome Rows(
        AnalysisLibraryBodyUseQueryOutcome outcome) =>
        outcome switch
        {
            AnalysisLibraryBodyUseQueryOutcome.Available
            {
                Answer: AnalysisLibraryBodyUseAnswer.Rows rows,
            } => new AnalysisLibraryBodyUseOutcome.Available(rows.Result),
            AnalysisLibraryBodyUseQueryOutcome.Rejected rejected =>
                new AnalysisLibraryBodyUseOutcome.Rejected(
                    rejected.Kind,
                    rejected.Detail),
            _ => throw new InvalidOperationException(
                "A Rows body-use query returned a non-Rows answer."),
        };

    static ImmutableArray<AnalysisLibraryBodyUseType> BuildTypes(
        MetadataReader reader,
        AssemblyTypeDeclarationInventory inventory)
    {
        var types =
            ImmutableArray.CreateBuilder<AnalysisLibraryBodyUseType>();
        foreach (AssemblyTypeDeclaration declaration
            in inventory.Declarations)
        {
            if (declaration.Kind
                    != AssemblyTypeDeclarationKind.Definition
                || declaration.DefinitionToken is not { } token
                || declaration.DefinitionKind is not { } kind)
            {
                continue;
            }

            EntityHandle entity =
                MetadataTokens.EntityHandle(token.Value);
            if (entity.Kind != HandleKind.TypeDefinition)
                throw new BadImageFormatException(
                    "A Type inventory definition token is not a TypeDef.");
            types.Add(
                new(
                    MetadataTypeDefinitionAddress.FromHandle(
                        reader,
                        (TypeDefinitionHandle)entity),
                    declaration.Name,
                    kind));
        }
        return types.ToImmutable();
    }

    static AnalysisLibraryBodyUseOccurrence ProjectOccurrence(
        MetadataReader reader,
        IReadOnlyDictionary<
            MetadataTypeDefinitionAddress,
            MetadataTypeDefinitionName> names,
        BodyTypeUseOccurrence occurrence)
    {
        MetadataTypeDefinitionAddress source =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                occurrence.Source);
        MetadataTypeDefinitionAddress target =
            MetadataTypeDefinitionAddress.FromHandle(
                reader,
                occurrence.Target);
        return new(
            source,
            names[source],
            target,
            names[target],
            occurrence.PhysicalMethodToken,
            occurrence.OperandKind,
            occurrence.OperandToken,
            occurrence.IlOffset,
            occurrence.OccurrenceOrdinal);
    }
}

internal readonly record struct AnalysisLibraryBodyUseProjection(
    AnalysisLibraryBodyUseDisposition Disposition,
    ImmutableArray<AnalysisLibraryBodyUseType> Types,
    ImmutableArray<AnalysisLibraryBodyUseOccurrence> Occurrences,
    ImmutableArray<AnalysisLibraryBodyUsePhysicalEvidence> PhysicalEvidence,
    AnalysisLibraryBodyUseCoverage Coverage,
    ImmutableArray<AnalysisLibraryBodyUseDiagnostic> Diagnostics);
