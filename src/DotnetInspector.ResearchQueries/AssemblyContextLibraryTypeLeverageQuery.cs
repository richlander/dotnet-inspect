using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspector.Queries;

public abstract record LibraryBodyTypeLeverageResult
{
    private LibraryBodyTypeLeverageResult()
    {
    }

    public sealed record Available(
        ImmutableArray<LibraryStructuralBodyTypeLeverageShard> Shards)
        : LibraryBodyTypeLeverageResult;

    public sealed record Rejected(
        AnalysisLibraryBodyUseRejectionKind Kind,
        string Detail)
        : LibraryBodyTypeLeverageResult;
}

public sealed record LibraryTypeLeverageDocument(
    LibraryStructuralSalienceDocument Surface,
    LibraryBodyTypeLeverageResult Implementation);

public abstract record LibraryTypeLeverageResult
{
    private LibraryTypeLeverageResult()
    {
    }

    public sealed record Available(LibraryTypeLeverageDocument Document)
        : LibraryTypeLeverageResult;

    public sealed record Rejected(
        MetadataLibrarySignatureUseRejectionKind Kind,
        string Detail,
        MetadataOperationCounters? Counters)
        : LibraryTypeLeverageResult;
}

/// <summary>
/// Produces exhaustive signature and body-use Type leverage from one immutable
/// implementation Library generation.
/// </summary>
public static class AssemblyContextLibraryTypeLeverageQuery
{
    public static InspectionQuery<LibraryTypeLeverageResult> Definition
        { get; } =
        new("Library Type leverage", InspectionCost.Unbounded);

    public static AssemblyContextEntry<LibraryTypeLeverageResult>
        ExecuteParticipant(
            AssemblyContextGroup group,
            AssemblyContextParticipant participant,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        cancellationToken.ThrowIfCancellationRequested();

        return AssemblyContextQueryExecutor
            .ExecuteParticipantOverSnapshot<LibraryTypeLeverageResult>(
                group,
                participant,
                cancellationToken,
                (subject, snapshot) =>
                {
                    LibrarySurfaceLeverageExhaustiveAcquisition surface;
                    using (AssemblyInspectionSession session =
                        AssemblyInspectionSession.Open(snapshot))
                    {
                        surface = AssemblyContextLibrarySurfaceLeverageQuery
                            .AcquireExhaustive(
                                session,
                                cancellationToken);
                    }

                    if (surface
                        is LibrarySurfaceLeverageExhaustiveAcquisition.Rejected
                            rejected)
                    {
                        return new LibraryTypeLeverageResult.Rejected(
                            rejected.Kind,
                            rejected.Detail,
                            rejected.Counters);
                    }

                    var available =
                        (LibrarySurfaceLeverageExhaustiveAcquisition.Available)
                            surface;
                    AnalysisLibraryBodyUseOutcome bodyUse =
                        AnalysisLibraryBodyUseService.ExecuteImage(
                            AssemblyContextAnalysisSource.Name(subject),
                            snapshot.Content,
                            new AnalysisLibraryBodyUseRequest(),
                            cancellationToken);
                    LibraryBodyTypeLeverageResult implementation =
                        bodyUse switch
                        {
                            AnalysisLibraryBodyUseOutcome.Available body =>
                                new LibraryBodyTypeLeverageResult.Available(
                                    LibraryStructuralReport
                                        .CreateBodyTypeLeverageShards(
                                            available.TypeInventories,
                                            body.Result)),
                            AnalysisLibraryBodyUseOutcome.Rejected body =>
                                new LibraryBodyTypeLeverageResult.Rejected(
                                    body.Kind,
                                    body.Detail),
                            _ => throw new InvalidOperationException(
                                "Unknown Library body-use outcome."),
                        };
                    return new LibraryTypeLeverageResult.Available(
                        new(
                            available.Document,
                            implementation));
                });
    }
}
