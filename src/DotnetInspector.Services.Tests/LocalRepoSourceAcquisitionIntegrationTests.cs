using System.Net;
using System.Reflection;

using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.SourceLink;

namespace DotnetInspector.Services.Tests;

public class LocalRepoSourceAcquisitionIntegrationTests
{
    [Fact]
    public async Task ServiceLocalClone_SatisfiesMemberAndTypeSourceWithoutRemoteFetch()
    {
        string repositoryRoot = FindRepositoryRoot();
        Type targetType = typeof(VerifiedLocalSourceReadTests);
        MethodInfo targetMethod = targetType.GetMethod(
            nameof(VerifiedLocalSourceReadTests.ReturnsBytes_WhenChecksumMatches))!;
        var typeName = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create(
                targetType.Namespace!,
                [targetType.Name]));
        using SourceLinkService source = SourceLinkService.Open(targetType.Assembly.Location);
        var outage = new NetworkOutageHandler();
        using var client = new HttpClient(outage);
        var fetcher = new SourceFetcher(client, new InMemorySourceContentStore());
        var subject = new FindingSubject("local-repo-source", targetType.FullName!);

        PdbMemberSourceInspection member =
            await PdbSourceAcquisition.AcquireMemberAsync(
                source,
                targetMethod.MetadataToken,
                targetMethod.Name,
                subject,
                fetcher,
                [repositoryRoot],
                TestContext.Current.CancellationToken,
                allowLocalSource: false);
        PdbTypeSourceInspection type =
            await PdbSourceAcquisition.AcquireTypeAsync(
                source,
                typeName.Name,
                subject,
                fetcher,
                [repositoryRoot],
                TestContext.Current.CancellationToken,
                allowLocalSource: false);
        SourceDocumentObservation document = Assert.IsType<SourceDocumentObservation>(
            member.Document);
        VerifiedSourceTextResult projection =
            await PdbSourceAcquisition.AcquireVerifiedSourceTextAsync(
                fetcher,
                document.OriginalPath,
                document.ResolvedUrl!,
                document.ChecksumAlgorithm,
                Convert.FromHexString(document.Checksum!),
                [repositoryRoot],
                TestContext.Current.CancellationToken,
                allowLocalSource: false);

        Assert.IsType<FindingInspection<string>.Complete>(member.Lines.Value);
        Assert.Contains(targetMethod.Name, member.Text, StringComparison.Ordinal);
        Assert.Equal(SourceChecksumVerification.Exact, member.ChecksumVerification);
        Assert.IsType<FindingInspection<string>.Complete>(type.Lines.Value);
        Assert.Contains(targetType.Name, type.Text, StringComparison.Ordinal);
        Assert.Equal(SourceChecksumVerification.Exact, type.ChecksumVerification);
        Assert.Contains(targetType.Name, projection.Text, StringComparison.Ordinal);
        Assert.Equal(SourceChecksumVerification.Exact, projection.ChecksumVerification);
        Assert.Equal(0, outage.RequestCount);
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dotnet-inspect.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root from '{AppContext.BaseDirectory}'.");
    }

    sealed class NetworkOutageHandler : HttpMessageHandler
    {
        int _requestCount;

        public int RequestCount => Volatile.Read(ref _requestCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                RequestMessage = request,
            });
        }
    }
}
