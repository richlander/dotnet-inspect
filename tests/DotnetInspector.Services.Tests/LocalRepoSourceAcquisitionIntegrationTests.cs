using System.Net;
using System.Reflection;

using Inspector.Findings;
using ILInspector.SourceLink;

namespace DotnetInspector.Services.Tests;

public class LocalRepoSourceAcquisitionIntegrationTests
{
    [Fact]
    public async Task DiagnosticLocalClone_SatisfiesMemberSourceWithoutRemoteFetch()
    {
        string repositoryRoot = FindRepositoryRoot();
        Type targetType = typeof(LocalRepoSourceReadTests);
        MethodInfo targetMethod = targetType.GetMethod(
            nameof(LocalRepoSourceReadTests.ParsesGitHubRawUrl_IntoShaAndPath))!;
        using SourceLinkService source = SourceLinkService.Open(targetType.Assembly.Location);
        var outage = new NetworkOutageHandler();
        using var client = new HttpClient(outage);
        var fetcher = new SourceFetch(client, new InMemorySourceContentStore());
        var subject = new FindingSubject("local-repo-source", targetType.FullName!);

        PdbMemberSourceInspection member =
            await PdbMemberSourceAcquisition.AcquireAsync(
                source,
                targetMethod.MetadataToken,
                targetMethod.Name,
                subject,
                fetcher,
                [repositoryRoot],
                TestContext.Current.CancellationToken,
                allowLocalSource: false);

        Assert.IsType<FindingInspection<string>.Complete>(member.Lines.Value);
        Assert.Contains(targetMethod.Name, member.Text, StringComparison.Ordinal);
        Assert.Equal(SourceChecksumVerification.Exact, member.ChecksumVerification);
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
