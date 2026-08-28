using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.SourceLink;

namespace DotnetInspector.Queries.Tests;

public sealed class SourceLinkQueriesTests
{
    [Fact]
    public async Task Documents_PdbTransportFailureFailsWithoutClaimingApplicability()
    {
        byte[] assemblyBytes = File.ReadAllBytes(
            typeof(SourceLinkQueriesTests).Assembly.Location);
        AssemblyReferenceIdentity identity;
        using (var stream = new MemoryStream(
                   assemblyBytes,
                   writable: false))
        using (var reader = new PEReader(stream))
        {
            identity = AssemblyReferenceIdentity.FromAssemblyDefinition(
                reader.GetMetadataReader());
        }

        var assembly = ResolvedAssemblyReference.Create(
            identity,
            path: null,
            () => new MemoryStream(
                assemblyBytes,
                writable: false),
            AssemblyResolutionProvenance.Local(
                "unresolved portable PDB query test"));
        using SourceLinkService source = SourceLinkService.Open(assembly);
        Assert.True(source.Context.NeedsPdb);
        using var symbolClient = new HttpClient(new ThrowingHandler());
        using var sourceClient = new HttpClient(new ThrowingHandler());

        SourceLinkDocumentsResult result =
            await SourceLinkDocumentsQuery.ExecuteAsync(
                new SourceLinkQueryContext(
                    source,
                    new FindingSubject("source", "source"),
                    symbolClient,
                    sourceClient),
                TestContext.Current.CancellationToken);

        var failed =
            Assert.IsType<FindingInspection<SourceDocumentObservation>.Failed>(
                result.Inspection.Value);
        Assert.Contains("remains unresolved", failed.Error.Reason);
        Assert.True(source.Context.NeedsPdb);
    }

    sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => throw new HttpRequestException(
                $"Simulated symbol transport failure for {request.RequestUri}.");
    }
}
