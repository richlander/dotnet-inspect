namespace InspectWeb.Engine;

internal static class BrowserPackageWireProjection
{
    internal static BrowserPackageCacheStats Project(BrowserPackageCacheSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            snapshot.Packages,
            snapshot.Resident,
            snapshot.Workspaces,
            snapshot.ResidentBytes);
    }

    internal static BrowserPackageDocument Project(BrowserPackageDocumentEntry document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.Kind,
            document.Name,
            document.Path,
            document.Size);
    }

    internal static BrowserPackageDocument[] Project(
        IReadOnlyList<BrowserPackageDocumentEntry> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        return [.. documents.Select(Project)];
    }

    internal static BrowserPackageDocumentContent Project(
        BrowserPackageDocumentPayload document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new(
            document.Kind,
            document.Name,
            document.Path,
            document.Text);
    }
}
