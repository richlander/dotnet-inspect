using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Release gates for the host-neutral Sync Calls in Async query: closings are
/// independent QuerySpace requests that share one Method-source traversal and
/// carry the host's reference binding.
/// </summary>
public sealed class SyncCallsInAsyncQueryTests
{
    static readonly string RepositoryPath =
        FixtureCatalog.AnalysisAsyncSiblingRepository.AssemblyPath();

    [Fact]
    public void Execute_AnswersRowsCountAndExistsFromOneRequestSet()
    {
        using var session = AssemblyInspectionSession.Open(RepositoryPath);

        SyncCallsInAsyncResult result = SyncCallsInAsyncQuery.Execute(
            session,
            CreateBinding(RepositoryPath),
            [
                SyncCallsInAsyncClosing.Rows,
                SyncCallsInAsyncClosing.Count,
                SyncCallsInAsyncClosing.Exists,
            ]);

        var rows = Assert.IsType<SyncCallsInAsyncAnswer.Rows>(
            result.AnswerTo(SyncCallsInAsyncClosing.Rows));
        AsyncSiblingRow read = Assert.Single(
            rows.Calls,
            row => row.Caller.Name == "Main"
                && row.Callee.Name == nameof(File.ReadAllText));
        Assert.Equal(nameof(File.ReadAllTextAsync), read.Alternative.Name);
        Assert.Equal(
            rows.Calls.Length,
            Assert.IsType<SyncCallsInAsyncAnswer.Count>(
                result.AnswerTo(SyncCallsInAsyncClosing.Count)).Value);
        Assert.True(
            Assert.IsType<SyncCallsInAsyncAnswer.Exists>(
                result.AnswerTo(SyncCallsInAsyncClosing.Exists)).Value);
        Assert.Equal(3, result.Receipts.Length);
        Assert.Null(result.Critical);
    }

    [Fact]
    public void Execute_ExistsStopsBeforeVisitingEveryMethod()
    {
        using var session = AssemblyInspectionSession.Open(RepositoryPath);
        AssemblyReferenceBindingAccess binding =
            CreateBinding(RepositoryPath);

        SyncCallsInAsyncResult all = SyncCallsInAsyncQuery.Execute(
            session,
            binding,
            [SyncCallsInAsyncClosing.Count]);
        SyncCallsInAsyncResult exists = SyncCallsInAsyncQuery.Execute(
            session,
            binding,
            [SyncCallsInAsyncClosing.Exists]);

        Assert.True(
            Assert.IsType<SyncCallsInAsyncAnswer.Exists>(
                exists.AnswerTo(SyncCallsInAsyncClosing.Exists)).Value);
        Assert.True(
            exists.Receipts[0].Receipt.UnitsVisited
                < all.Receipts[0].Receipt.UnitsVisited);
    }

    [Fact]
    public void Execute_CountClosingAnswersCount()
    {
        using var session = AssemblyInspectionSession.Open(RepositoryPath);

        SyncCallsInAsyncResult result = SyncCallsInAsyncQuery.Execute(
            session,
            CreateBinding(RepositoryPath),
            [SyncCallsInAsyncClosing.Count]);

        Assert.True(
            Assert.IsType<SyncCallsInAsyncAnswer.Count>(
                result.AnswerTo(SyncCallsInAsyncClosing.Count)).Value > 0);
    }

    static AssemblyReferenceBindingAccess CreateBinding(string path)
    {
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        return new AssemblyReferenceBindingAccess(
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "sync calls in async query test")),
            new AssemblyReferenceBindingPolicy(resolver));
    }
}
