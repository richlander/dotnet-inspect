using System.Text;

using DotnetInspector.Fixtures;
using DotnetInspector.Services;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

public sealed class ExternalBaseSiblingWriter : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override void Flush()
    {
    }
}

public static class ExternalBaseSiblingFixtures
{
    public static async Task FlushesExternalBaseWriter(
        ExternalBaseSiblingWriter writer)
    {
        await Task.Yield();
        writer.Flush();
    }
}

public class VirtualSiblingBase
{
    public virtual Task WriteAsync() => Task.CompletedTask;
}

public sealed class VirtualSiblingTarget : VirtualSiblingBase
{
    public void Write()
    {
    }
}

public sealed class UnresolvedBaseOverrideCaller : TextWriter
{
    public override Encoding Encoding => Encoding.UTF8;

    public override async Task FlushAsync()
    {
        new VirtualSiblingTarget().Write();
        await Task.Yield();
    }
}

public class AsyncSiblingProducerTests
{
    static readonly string FixturePath =
        typeof(OptimizationOpportunityFixtures).Assembly.Location;

    static AssemblyAnalysisOperation<AsyncSiblingProducerResult> CreateOperation()
    {
        AsyncSiblingProducer producer = AsyncSiblingProducer.Instance;
        WorkDescription work = Assert.IsType<ProducerPlanResult.Accepted>(
            ProducerPlanner.Plan(
                [new ProducerRequest(producer, ProducerTerminal.Rows)]))
            .Description;
        return AssemblyAnalysisOperation<AsyncSiblingProducerResult>.Create(
            "async-sibling-test",
            MethodDefinitionSourceRequest<AsyncSiblingProducerResult>.Create(
                work,
                producer,
                MethodDefinitionSourceBreadth.AllDefinitions));
    }

    static AssemblyReferenceBindingAccess CreateBinding()
    {
        var resolver = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(FixturePath)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        return new AssemblyReferenceBindingAccess(
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local("async sibling test")),
            new AssemblyReferenceBindingPolicy(resolver));
    }

    sealed class NoResolver : IAssemblyReferenceResolver
    {
        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope) => null;
    }

    sealed class ChangingPolicy(IAssemblyBindingPolicy inner)
        : IAssemblyBindingPolicy
    {
        bool _changed;

        public AssemblyBindingPolicyVersion Version => inner.Version;

        public AssemblyBindingSelectionSnapshot Select(
            AssemblyBindingRequest request)
        {
            AssemblyBindingSelectionSnapshot snapshot = inner.Select(request);
            if (!_changed)
            {
                _changed = true;
                return snapshot;
            }

            return new(new AssemblyBindingPolicyVersion(), snapshot.Selection);
        }
    }

    [Fact]
    public void Producer_ReportsUnresolvedInheritedBaseAsDiagnostic()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);
        var binding = new AssemblyReferenceBindingAccess(
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local("async sibling test")),
            new AssemblyReferenceBindingPolicy(new NoResolver()));

        AsyncSiblingProducerResult result = session.SnapshotOperation(
            operation,
            binding,
            access =>
            {
                var completed = Assert.IsType<
                    AssemblyAnalysisServiceResult<AsyncSiblingProducerResult>
                        .Completed>(
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
                return completed.Execution.ResultOf(
                    AsyncSiblingProducer.Instance).Value!;
            });

        Assert.DoesNotContain(
            result.Rows,
            row => row.Caller.Name
                == nameof(ExternalBaseSiblingFixtures
                    .FlushesExternalBaseWriter));
        Assert.Contains(
            result.Diagnostics,
            d => d.Method == nameof(ExternalBaseSiblingFixtures
                    .FlushesExternalBaseWriter)
                && d.Message.Contains(
                    "could not be resolved",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Producer_FailsExecutionWhenBindingPolicyChanges()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);
        AssemblyReferenceBindingAccess real = CreateBinding();
        var binding = new AssemblyReferenceBindingAccess(
            real.Subject,
            new ChangingPolicy(real.Policy));

        ProducerResult<AsyncSiblingProducerResult> result =
            session.SnapshotOperation(
                operation,
                binding,
                access =>
                {
                    var completed = Assert.IsType<
                        AssemblyAnalysisServiceResult<
                            AsyncSiblingProducerResult>.Completed>(
                        AssemblyAnalysisService.Instance.Execute(
                            operation,
                            access));
                    return completed.Execution.ResultOf(
                        AsyncSiblingProducer.Instance);
                });

        Assert.False(result.HasValue);
        Assert.NotNull(result.Critical);
        Assert.Equal("ReferenceBinding", result.Critical.Owner);
    }

    [Fact]
    public void Producer_ReportsUnresolvedCallerBaseWhenSlotCheckSkipsSibling()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);
        var binding = new AssemblyReferenceBindingAccess(
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local("async sibling test")),
            new AssemblyReferenceBindingPolicy(new NoResolver()));

        AsyncSiblingProducerResult result = session.SnapshotOperation(
            operation,
            binding,
            access =>
            {
                var completed = Assert.IsType<
                    AssemblyAnalysisServiceResult<AsyncSiblingProducerResult>
                        .Completed>(
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
                return completed.Execution.ResultOf(
                    AsyncSiblingProducer.Instance).Value!;
            });

        Assert.DoesNotContain(
            result.Rows,
            row => row.Caller.Name
                == nameof(UnresolvedBaseOverrideCaller.FlushAsync));
        Assert.Contains(
            result.Diagnostics,
            d => d.Method == nameof(UnresolvedBaseOverrideCaller.FlushAsync)
                && d.Message.Contains(
                    "'Write'",
                    StringComparison.Ordinal)
                && d.Message.Contains(
                    "could not be resolved",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Producer_ReportsUnresolvedReferencesAsDiagnostics()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);
        var binding = new AssemblyReferenceBindingAccess(
            ResolvedAssemblyReference.CreateFromPath(
                FixturePath,
                AssemblyResolutionProvenance.Local("async sibling test")),
            new AssemblyReferenceBindingPolicy(new NoResolver()));

        AsyncSiblingProducerResult result = session.SnapshotOperation(
            operation,
            binding,
            access =>
            {
                var completed = Assert.IsType<
                    AssemblyAnalysisServiceResult<AsyncSiblingProducerResult>
                        .Completed>(
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
                return completed.Execution.ResultOf(
                    AsyncSiblingProducer.Instance).Value!;
            });

        Assert.Contains(
            result.Diagnostics,
            d => d.Message.Contains(
                "could not be resolved",
                StringComparison.Ordinal));
    }

    [Fact]
    public void Service_RejectsMissingReferenceBinding()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);

        var result = session.SnapshotOperation(
            operation,
            access => AssemblyAnalysisService.Instance.Execute(
                operation,
                access));

        var rejected = Assert.IsType<
            AssemblyAnalysisServiceResult<AsyncSiblingProducerResult>.Rejected>(
            result);
        Assert.Equal(
            AssemblyAnalysisRejectionKind.ReferenceBindingUnavailable,
            rejected.Kind);
    }

    [Fact]
    public void Producer_FindsSyncCallWithAsyncSibling()
    {
        var operation = CreateOperation();
        using var session = AssemblyInspectionSession.Open(FixturePath);

        AsyncSiblingProducerResult result = session.SnapshotOperation(
            operation,
            CreateBinding(),
            access =>
            {
                var completed = Assert.IsType<
                    AssemblyAnalysisServiceResult<AsyncSiblingProducerResult>
                        .Completed>(
                    AssemblyAnalysisService.Instance.Execute(
                        operation,
                        access));
                var produced = completed.Execution.ResultOf(
                    AsyncSiblingProducer.Instance);
                Assert.True(produced.HasValue, produced.Failure?.ToString());
                return produced.Value!;
            });

        AsyncSiblingRow row = Assert.Single(
            result.Rows,
            r => r.Caller.Name
                == nameof(OptimizationOpportunityAsyncSiblingFixtures
                    .CallsSyncSiblingFromAsync));
        Assert.Contains("Async", row.Alternative.Name, StringComparison.Ordinal);
        Assert.Equal(AsyncSiblingPairKind.Operation, row.PairKind);
        Assert.Empty(result.Diagnostics);
    }
}
