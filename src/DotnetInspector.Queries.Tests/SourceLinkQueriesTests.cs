using System.Text;
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
        using SourceLinkService source = OpenSourceNeedingPdb();
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

    [Fact]
    public async Task Documents_WindowsPdbIsNoApplicableInput()
    {
        using SourceLinkService source = OpenSourceNeedingPdb();
        source.Context.LoadPdbFromStream(new MemoryStream(
            Encoding.ASCII.GetBytes(
                "Microsoft C/C++ MSF 7.00\r\n\u001ADS\0\0\0"),
            writable: false));
        Assert.True(source.Context.WindowsPdbDetected);
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

        var absent =
            Assert.IsType<FindingInspection<SourceDocumentObservation>.Absent>(
                result.Inspection.Value);
        Assert.Equal(
            FindingInspectionAbsenceKind.NoApplicableInput,
            absent.Kind);
        Assert.Contains("Windows PDB", absent.Detail);
    }

    static SourceLinkService OpenSourceNeedingPdb()
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
        return SourceLinkService.Open(assembly);
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
