using System;
using System.IO;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Local;

string path = Path.Combine(
    Path.GetTempPath(),
    $"dotnet-inspect-local-path-probe-{Guid.NewGuid():N}.dll");
await File.WriteAllBytesAsync(path, [1, 2, 3]);
try
{
    var authority = new ArtifactGenerationAuthority();
    ArtifactAdmissionAuthorization authorization =
        authority.CreateAdmissionAuthorization();
    using ArtifactContributionScope scope =
        authority.BeginContribution(authorization);
    ArtifactAcquisitionOutcome outcome =
        await LocalArtifactSource.AcquireFileAsync(scope, path);
    var acquired =
        outcome as ArtifactAcquisitionOutcome.Acquired
        ?? throw new InvalidOperationException(
            $"Unexpected local acquisition outcome: {outcome.GetType().Name}");
    await acquired.Lease.DisposeAsync();

    if (acquired.Artifacts.Count != 1)
        throw new InvalidOperationException(
            "The local admission probe did not acquire exactly one artifact.");
}
finally
{
    File.Delete(path);
}

Console.WriteLine("local-path-admission-platform-probe: supported");
