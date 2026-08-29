using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Local;
using DotnetInspector.Artifacts.Workspaces;
using ILInspector.Metadata;

namespace DotnetInspector.AssemblyOnlyHost.Fixture;

public sealed record AssemblyOnlyInspectionResult(
    string AssemblyName,
    ArtifactAcquisitionRegistration ArtifactRegistration,
    AssemblyAcquisitionRegistration AssemblyRegistration);

public static class AssemblyOnlyInspector
{
    public static async ValueTask<AssemblyOnlyInspectionResult>
        InspectAfterDeletingSourceAsync(
        string assemblyPath,
        CancellationToken cancellationToken = default)
    {
        await using var artifacts = new ArtifactSetSession();
        await artifacts.AddRequiredAcquisitionAsync(
            (scope, token) =>
                LocalArtifactSource.AcquireFileAsync(
                    scope,
                    assemblyPath,
                    cancellationToken: token),
            [ArtifactWorkspaceRole.CallerDesignated],
            cancellationToken);
        ArtifactSetPublicationOutcome publication =
            await artifacts.SealAsync(cancellationToken);
        if (publication
            is ArtifactSetPublicationOutcome.NotPublished rejected)
        {
            string detail = string.Join(
                "; ",
                rejected.Failures.Select(
                    failure => failure.Diagnostic.Summary));
            throw new InvalidDataException(detail);
        }

        File.Delete(assemblyPath);

        ArtifactQueryAuthorization authorization =
            artifacts.CreateQueryAuthorization();
        using ArtifactQueryLease lease =
            artifacts.IssueLease(authorization);
        ArtifactDescriptor descriptor =
            AssertSingle(artifacts.GetCatalog(lease));
        ArtifactContentReference content =
            artifacts.GetContentReference(
                descriptor.Identity,
                lease);
        if (!content.HasRole(
                ArtifactWorkspaceRole.CallerDesignated))
        {
            throw new UnauthorizedAccessException(
                "The local artifact lacks caller-designation authority.");
        }

        ArtifactAcquisitionRegistration artifactRegistration =
            content.Registration;
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactIfManaged(
                artifactRegistration,
                content.OpenRead,
                AssemblyResolutionProvenance.Local(
                    "artifact-session"))
            ?? throw new BadImageFormatException(
                "The local artifact has no managed metadata.");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assembly);
        return new AssemblyOnlyInspectionResult(
            session.AssemblyInfo().AssemblyName
                ?? throw new InvalidDataException(
                    $"{assemblyPath} has no assembly name."),
            artifactRegistration,
            assembly.Registration);
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values) =>
        values.Count == 1
            ? values[0]
            : throw new InvalidDataException(
                $"Expected one artifact, but found {values.Count}.");
}
