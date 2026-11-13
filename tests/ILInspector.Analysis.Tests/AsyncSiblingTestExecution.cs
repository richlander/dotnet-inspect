using DotnetInspector.Services;
using ILInspector.Analysis.Planning;
using ILInspector.Metadata;

namespace ILInspector.Analysis.Tests;

internal static class AsyncSiblingTestExecution
{
    internal static AsyncSiblingProducerResult Execute(
        string path,
        IAssemblyReferenceResolver? resolver = null)
    {
        resolver ??= new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(path)
            {
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
            });
        ResolvedAssemblyReference subject =
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(
                    "async sibling test"));
        return Execute(subject, resolver);
    }

    internal static AsyncSiblingProducerResult Execute(
        string sourceName,
        byte[] image,
        IAssemblyReferenceResolver? resolver = null)
    {
        ResolvedAssemblyReference subject = Assert.IsType<
            ResolvedAssemblyReference>(
                ResolvedAssemblyReference.CreateFromStreamIfManaged(
                    () => new MemoryStream(image, writable: false),
                    AssemblyResolutionProvenance.Local(
                        "async sibling test"),
                    lastWriteTimeUtc: null,
                    assetFileName: sourceName));
        return Execute(
            subject,
            resolver ?? NoResolver.Instance);
    }

    static AsyncSiblingProducerResult Execute(
        ResolvedAssemblyReference subject,
        IAssemblyReferenceResolver resolver)
    {
        using var session = AssemblyInspectionSession.Open(subject);
        var binding = new AssemblyReferenceBindingAccess(
            subject,
            new AssemblyReferenceBindingPolicy(resolver));
        AsyncSiblingProducer producer = AsyncSiblingProducer.Instance;
        WorkDescription work = Assert.IsType<ProducerPlanResult.Accepted>(
            ProducerPlanner.Plan(
                [new ProducerRequest(producer, ProducerTerminal.Rows)]))
            .Description;
        AssemblyAnalysisOperation<AsyncSiblingProducerResult> operation =
            AssemblyAnalysisOperation<AsyncSiblingProducerResult>.Create(
                "async-sibling-test",
                MethodDefinitionSourceRequest<
                    AsyncSiblingProducerResult>.Create(
                        work,
                        producer,
                        MethodDefinitionSourceBreadth.AllDefinitions)
                    .AsAssemblyAnalysisSource());

        return session.SnapshotOperation(
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
                return Assert.IsType<AsyncSiblingProducerResult>(
                    completed.Execution.ResultOf(producer).Value);
            });
    }

    sealed class NoResolver : IAssemblyReferenceResolver
    {
        internal static NoResolver Instance { get; } = new();

        public ResolvedAssemblyReference? Resolve(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope) => null;
    }
}
