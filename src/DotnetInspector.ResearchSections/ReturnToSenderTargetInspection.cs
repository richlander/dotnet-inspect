using DotnetInspector.ResearchQueries;
using DotnetInspector.Sections;

using ILInspector.Metadata;

using QuerySpace.Composition;

namespace DotnetInspector.ResearchSections;

public sealed record ReturnToSenderTargetSubject(
    string Identity,
    AssemblyInspectionSession Assembly);

public sealed record ReturnToSenderTargetAssemblyCountReceipt(
    string Identity,
    int EligibleCount,
    int ScannedBodyCount,
    int DeclarationCandidateCount,
    int MaterializedRowCount);

public sealed record ReturnToSenderTargetCountReceipt(
    IReadOnlyList<ReturnToSenderTargetAssemblyCountReceipt>
        Assemblies,
    int ScannedBodyCount,
    int DeclarationCandidateCount,
    int MaterializedRowCount);

public abstract record ReturnToSenderTargetCountOutcome
{
    private ReturnToSenderTargetCountOutcome()
    {
    }

    public sealed record Counted(
        int Count,
        ReturnToSenderTargetCountReceipt Receipt)
        : ReturnToSenderTargetCountOutcome;

    public sealed record Rejected(
        ReturnToSenderTargetQueryResolution Resolution)
        : ReturnToSenderTargetCountOutcome;
}

public static class ReturnToSenderTargetInspection
{
    private static readonly SectionRowSchemaIdentity<
        ReturnToSenderTarget> Schema =
            SectionRowSchemaIdentity<
                ReturnToSenderTarget>.Create();

    public static ReturnToSenderTargetCountOutcome Count(
        IReadOnlyList<ReturnToSenderTargetSubject> subjects,
        QuerySpaceRequest request)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(request);

        ReturnToSenderTargetQueryResolution query =
            ReturnToSenderTargetQuery.ResolveRequest(request);
        if (query is not
            ReturnToSenderTargetQueryResolution.Accepted)
        {
            return new ReturnToSenderTargetCountOutcome.Rejected(
                query);
        }

        var assemblyReceipts =
            new ReturnToSenderTargetAssemblyCountReceipt[
                subjects.Count];
        int eligibleCount = 0;
        int scannedBodyCount = 0;
        int declarationCandidateCount = 0;
        int materializedRowCount = 0;
        for (int index = 0; index < subjects.Count; index++)
        {
            ReturnToSenderTargetSubject subject =
                subjects[index]
                ?? throw new ArgumentNullException(
                    nameof(subjects),
                    $"Subject {index + 1} is null.");
            using var source =
                new ReturnToSenderTargetSourceSession(
                    subject.Identity,
                    subject.Assembly);
            ReturnToSenderTargetSourceCount count =
                source.Count();
            eligibleCount = checked(
                eligibleCount + count.EligibleCount);
            scannedBodyCount = checked(
                scannedBodyCount + count.ScannedBodyCount);
            declarationCandidateCount = checked(
                declarationCandidateCount
                + count.DeclarationCandidateCount);
            materializedRowCount = checked(
                materializedRowCount
                + count.MaterializedRowCount);
            assemblyReceipts[index] =
                new(
                    subject.Identity,
                    count.EligibleCount,
                    count.ScannedBodyCount,
                    count.DeclarationCandidateCount,
                    count.MaterializedRowCount);
        }

        var receipt = new ReturnToSenderTargetCountReceipt(
            Array.AsReadOnly(assemblyReceipts),
            scannedBodyCount,
            declarationCandidateCount,
            materializedRowCount);
        var declaration =
            new SectionRowSetDeclaration<
                string,
                Projection,
                ReturnToSenderTarget>(
                    ReturnToSenderTargetQuery.RowSet,
                    Schema,
                    [],
                    static (projection, _) => projection);
        var sourceState =
            new SectionRowSourceState<
                string,
                SourceDisposition,
                ReturnToSenderTargetCountReceipt>(
                    ReturnToSenderTargetQuery.RowSet,
                    new(
                        SourceDisposition.Complete,
                        receipt),
                    eligibleCount);
        QuerySpaceSectionSourceRowResolutionResult<
            Projection,
            SourceDisposition,
            ReturnToSenderTargetCountReceipt> resolution =
                QuerySpaceSectionRowResolver.Resolve(
                    ReturnToSenderTargetQuery.QuerySpace,
                    request,
                    [declaration],
                    [sourceState],
                    new SectionQuerySpaceRowScopeBinding<
                        ReturnToSenderTarget>(
                            ReturnToSenderTargetQuery.RowScope,
                            Schema));
        if (!resolution.IsSuccess)
        {
            throw new InvalidOperationException(
                "The owner-created RTS target Count request did not "
                + "resolve: "
                + $"{resolution.Failure?.RowQueryFailure.Reason}.");
        }

        SectionCountOutcome<
            string,
            SectionRowSourceEvidence<
                SourceDisposition,
                ReturnToSenderTargetCountReceipt>> outcome =
                    QuerySpaceSectionRowExecutor.ApplyCount(
                        resolution.Request!);
        var completed =
            outcome as SectionCountOutcome<
                string,
                SectionRowSourceEvidence<
                    SourceDisposition,
                    ReturnToSenderTargetCountReceipt>>.Completed
            ?? throw new InvalidOperationException(
                "The complete RTS target source did not produce an "
                + "exact Count.");
        SectionCountEntry<string> countEntry =
            AssertSingle(completed.Counts);
        if (countEntry.Value != eligibleCount)
        {
            throw new InvalidOperationException(
                "The RTS target source Count changed during QuerySpace "
                + "execution.");
        }

        return new ReturnToSenderTargetCountOutcome.Counted(
            countEntry.Value,
            receipt);
    }

    private static T AssertSingle<T>(
        IReadOnlyList<T> values) =>
        values.Count == 1
            ? values[0]
            : throw new InvalidOperationException(
                "RTS target Count requires exactly one row-set result.");

    private sealed record Projection;

    private enum SourceDisposition
    {
        Complete,
    }
}
