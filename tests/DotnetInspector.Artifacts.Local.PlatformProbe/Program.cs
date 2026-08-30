using System;
using System.IO;
using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Local;

string path = Path.Combine(
    Path.GetTempPath(),
    $"dotnet-inspect-local-path-probe-{Guid.NewGuid():N}.dll");
string missingPath = path + ".missing";
string notDirectoryPath = Path.Combine(path, "child.dll");
string loopPath = path + ".loop";
if (OperatingSystem.IsBrowser()
    && (LocalPathAdmission.IsUnixMissing(2)
        || LocalPathAdmission.IsUnixMissing(20)
        || !LocalPathAdmission.IsUnixMissing(44)
        || !LocalPathAdmission.IsUnixMissing(54)
        || LocalPathAdmission.IsUnixSymbolicLinkLoop(40)
        || !LocalPathAdmission.IsUnixSymbolicLinkLoop(32)))
{
    throw new InvalidOperationException(
        "Browser errno classification did not select only WASI values.");
}

await File.WriteAllBytesAsync(path, [1, 2, 3]);
try
{
    var authority = new ArtifactGenerationAuthority();
    ArtifactAdmissionAuthorization authorization =
        authority.CreateAdmissionAuthorization();
    using ArtifactContributionScope scope =
        authority.BeginContribution(authorization);
    ArtifactAcquisitionOutcome regularOutcome =
        await LocalArtifactSource.AcquireFileAsync(scope, path);
    var acquired =
        regularOutcome as ArtifactAcquisitionOutcome.Acquired
        ?? throw new InvalidOperationException(
            $"Unexpected local acquisition outcome: " +
            $"{regularOutcome.GetType().Name}");
    await acquired.Lease.DisposeAsync();

    if (acquired.Artifacts.Count != 1)
        throw new InvalidOperationException(
            "The local admission probe did not acquire exactly one artifact.");

    ArtifactAcquisitionOutcome missingOutcome =
        await LocalArtifactSource.AcquireFileAsync(scope, missingPath);
    if (missingOutcome is not ArtifactAcquisitionOutcome.Unavailable missing
        || missing.Diagnostic.Code != "local.file.missing")
    {
        throw new InvalidOperationException(
            $"Unexpected missing-path outcome: " +
            $"{missingOutcome.GetType().Name}");
    }

    ArtifactAcquisitionOutcome notDirectoryOutcome =
        await LocalArtifactSource.AcquireFileAsync(scope, notDirectoryPath);
    if (notDirectoryOutcome
            is not ArtifactAcquisitionOutcome.Unavailable notDirectory
        || notDirectory.Diagnostic.Code != "local.file.missing")
    {
        throw new InvalidOperationException(
            $"Unexpected not-directory outcome: " +
            $"{notDirectoryOutcome.GetType().Name}");
    }

    File.CreateSymbolicLink(loopPath, loopPath);
    ArtifactAcquisitionOutcome loopOutcome =
        await LocalArtifactSource.AcquireFileAsync(scope, loopPath);
    if (loopOutcome is not ArtifactAcquisitionOutcome.Rejected rejected
        || rejected.Diagnostic.Code != "local.file.unsupported-entry")
    {
        throw new InvalidOperationException(
            $"Unexpected link-loop outcome: {loopOutcome.GetType().Name}");
    }
}
finally
{
    File.Delete(path);
    File.Delete(loopPath);
}

Console.WriteLine("local-path-admission-platform-probe: supported");
