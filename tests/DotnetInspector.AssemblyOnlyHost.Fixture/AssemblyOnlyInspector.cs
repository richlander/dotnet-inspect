using DotnetInspector.Artifacts;
using DotnetInspector.Artifacts.Local;
using DotnetInspector.Artifacts.Workspaces;
using ILInspector.Metadata;

namespace DotnetInspector.AssemblyOnlyHost.Fixture;

public static class AssemblyOnlyInspector
{
    public static async ValueTask<string>
        ReadAssemblyNameAfterDeletingSourceAsync(
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
        if (!artifacts.HasRole(
                descriptor.Identity,
                ArtifactWorkspaceRole.CallerDesignated,
                lease))
        {
            throw new UnauthorizedAccessException(
                "The local artifact lacks caller-designation authority.");
        }

        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => artifacts.OpenRead(
                    descriptor.Identity,
                    lease),
                AssemblyResolutionProvenance.Local(
                    "artifact-session"))
            ?? throw new BadImageFormatException(
                "The local artifact has no managed metadata.");
        using AssemblyInspectionSession session =
            AssemblyInspectionSession.Open(assembly);
        return session.AssemblyInfo().AssemblyName
            ?? throw new InvalidDataException(
                $"{assemblyPath} has no assembly name.");
    }

    private static T AssertSingle<T>(IReadOnlyList<T> values) =>
        values.Count == 1
            ? values[0]
            : throw new InvalidDataException(
                $"Expected one artifact, but found {values.Count}.");
}
