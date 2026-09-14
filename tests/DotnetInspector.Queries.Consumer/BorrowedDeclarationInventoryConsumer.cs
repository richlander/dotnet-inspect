using ILInspector.Metadata;
using Inspector.Artifacts;

namespace DotnetInspector.Queries.Consumer;

public static class BorrowedDeclarationInventoryConsumer
{
    public static ArtifactAssemblyQueryOutcome<AssemblyTypeDeclarationInventoryOutcome> Read(
        scoped ArtifactQueryContentView view,
        ArtifactAssemblyProjection projection,
        CancellationToken cancellationToken) =>
        ArtifactAssemblyInspection.Execute(view, projection,
            static (session, token) => session.TypeDeclarations(token), cancellationToken);
}
