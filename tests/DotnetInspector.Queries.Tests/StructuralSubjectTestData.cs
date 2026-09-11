using System.IO.Compression;

using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

internal static class StructuralSubjectTestData
{
    internal sealed record PackageContext(
        InspectionWorkspaceIdentity WorkspaceIdentity,
        StructuralSubjectIdentity.WorkspaceSubject Workspace,
        WorkspacePackageOccurrence Occurrence,
        StructuralSubjectIdentity.PackageSubject Subject);

    internal static PackageContext Package(
        RealizedMemberCoordinate.Package coordinate,
        InspectionWorkspaceIdentity? workspaceIdentity = null)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        workspaceIdentity ??= new InspectionWorkspaceIdentity();
        PackageRootBinding binding = Binding(coordinate);
        if (binding.Coordinate != coordinate)
        {
            throw new InvalidOperationException(
                "The test Package binding did not preserve the requested coordinate.");
        }

        var occurrence = new WorkspacePackageOccurrence(
            workspaceIdentity,
            new WorkspacePackageDescriptor(binding),
            new PackageArtifactRootCorrespondence(
                workspaceIdentity,
                PackageArtifactRootRequest.From(binding)));
        StructuralSubjectIdentity.WorkspaceSubject workspace =
            StructuralSubjectIdentity.ForWorkspace(workspaceIdentity);
        return new(
            workspaceIdentity,
            workspace,
            occurrence,
            StructuralSubjectIdentity.ForPackage(workspace, occurrence));
    }

    static PackageRootBinding Binding(
        RealizedMemberCoordinate.Package coordinate)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using Stream entry = archive.CreateEntry(
                $"lib/{coordinate.Framework}/Test.Library.dll").Open();
            entry.WriteByte(0);
        }

        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(
                    coordinate.PackageId,
                    coordinate.Version),
                new InMemoryPackageContent(
                    bytes.ToArray(),
                    fromCache: false,
                    producerKey: coordinate.Producer),
                coordinate.Producer,
                PackagePayloadOrigin.Download),
            coordinate.Framework,
            coordinate.RuntimeIdentifier);
    }
}
