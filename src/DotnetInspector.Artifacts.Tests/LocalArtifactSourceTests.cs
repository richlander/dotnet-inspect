using DotnetInspector.Artifacts.Local;
using DotnetInspector.Artifacts.Workspaces;

namespace DotnetInspector.Artifacts.Tests;

public sealed class LocalArtifactSourceTests
{
    [Fact]
    public async Task LocalArtifactSnapshot_MutationCannotChangeInspectionBytes()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string path = TempPath();
        await File.WriteAllBytesAsync(
            path,
            [1, 2, 3],
            cancellationToken);
        try
        {
            await using var session = new ArtifactSetSession();
            await session.AddRequiredAcquisitionAsync(
                (scope, cancellationToken) =>
                    LocalArtifactSource.AcquireFileAsync(
                        scope,
                        path,
                        cancellationToken: cancellationToken),
                [ArtifactWorkspaceRole.CallerDesignated],
                cancellationToken);

            await File.WriteAllBytesAsync(
                path,
                [9, 9, 9],
                cancellationToken);
            Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                await session.SealAsync(cancellationToken));
            File.Delete(path);

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            using ArtifactQueryLease lease =
                session.IssueLease(authorization);
            ArtifactDescriptor descriptor =
                Assert.Single(session.GetCatalog(lease));
            using Stream opened =
                session.OpenRead(descriptor.Identity, lease);

            Assert.Equal([1, 2, 3], ReadAll(opened));
            var provenance =
                Assert.IsType<LocalArtifactProvenance>(
                    session.GetProvenance(
                        descriptor.Identity,
                        lease));
            Assert.Equal(Path.GetFullPath(path), provenance.FullPath);
            Assert.Equal(3, provenance.ContentLength);
            Assert.True(
                session.HasRole(
                    descriptor.Identity,
                    ArtifactWorkspaceRole.CallerDesignated,
                    lease));
            Assert.Null(descriptor.MediaType);
            Assert.Equal("local-file", descriptor.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LocalFileAcquisition_ReportsMissingAndOversizeInputs()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string missing = TempPath();
        var missingOwner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization missingAuthorization =
            missingOwner.CreateAdmissionAuthorization();
        using ArtifactContributionScope missingScope =
            missingOwner.BeginContribution(missingAuthorization);

        var unavailable =
            Assert.IsType<ArtifactAcquisitionOutcome.Unavailable>(
                await LocalArtifactSource.AcquireFileAsync(
                    missingScope,
                    missing,
                    cancellationToken: cancellationToken));
        Assert.Equal(
            "local.file.missing",
            unavailable.Diagnostic.Code);

        string path = TempPath();
        await File.WriteAllBytesAsync(
            path,
            [1, 2],
            cancellationToken);
        try
        {
            var owner = new ArtifactGenerationAuthority();
            ArtifactAdmissionAuthorization authorization =
                owner.CreateAdmissionAuthorization();
            using ArtifactContributionScope scope =
                owner.BeginContribution(authorization);

            var rejected =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await LocalArtifactSource.AcquireFileAsync(
                        scope,
                        path,
                        new LocalArtifactAcquisitionOptions
                        {
                            MaxFileBytes = 1,
                        },
                        cancellationToken));
            Assert.Equal(
                "local.file.size-limit",
                rejected.Diagnostic.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ArtifactAcquisition_CancellationRemainsCancellation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string path = TempPath();
        await File.WriteAllBytesAsync(
            path,
            [1],
            cancellationToken);
        try
        {
            var owner = new ArtifactGenerationAuthority();
            ArtifactAdmissionAuthorization authorization =
                owner.CreateAdmissionAuthorization();
            using ArtifactContributionScope scope =
                owner.BeginContribution(authorization);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await LocalArtifactSource.AcquireFileAsync(
                    scope,
                    path,
                    cancellationToken: cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempPath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-local-artifact-{Guid.NewGuid():N}.bin");

    private static byte[] ReadAll(Stream stream)
    {
        using var destination = new MemoryStream();
        stream.CopyTo(destination);
        return destination.ToArray();
    }
}
