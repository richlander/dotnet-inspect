using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

using DotnetInspector.Packages;

namespace DotnetInspector.Queries.Tests;

/// <summary>
/// Documents current, non-deduplicated realization behavior for one
/// <see cref="InspectionWorkspace"/>. Nothing here recognizes that two
/// realization calls request the exact same package identity and content, so
/// each call independently reopens the package's entries and mints a wholly
/// separate <see cref="AssemblyContextGroup"/> and set of
/// <see cref="AssemblyContextParticipant"/>s.
/// </summary>
/// <remarks>
/// This is the gap named by the "Interaction model" subsection of
/// `docs/design/artifact-acquisition-and-workspaces.md#artifactsetsession`
/// and by the header comment of
/// `docs/models/artifact-session-admission/ArtifactSessionAdmission.tla`:
/// that model checks the doc's stated design intent (single-flight admission
/// across concurrent demands), not shipped behavior.
/// `ArtifactSetSession`'s own doc comment states that it does not yet
/// implement workspace-wide reservation or single-flight admission, and this
/// realization path does not consult `ArtifactSetSession` at all. These tests
/// exist so that implementing that admission-coordination layer has a
/// concrete, reproducible starting point to change, rather than only a
/// design-doc claim.
/// </remarks>
public sealed class PackageAssemblyContextRealizationConcurrentDemandTests
{
    const string Framework = "net11.0";

    [Fact]
    public void IdenticalPackageRealizedTwice_ReopensContentAndMintsSeparateGroups()
    {
        byte[] image =
            File.ReadAllBytes(
                typeof(PackageAssemblyContextRealizationConcurrentDemandTests)
                    .Assembly.Location);
        const string path = "lib/net11.0/Dedup.Sample.dll";
        var content = new CountingPackageContent(Archive((path, image)));
        var package = new PackageRootRealization(
            content,
            "Dedup.Sample",
            "1.0.0",
            Framework);
        using var workspace = new InspectionWorkspace();

        using PackageAssemblyContextRealization first =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);
        int entryOpensAfterFirst = content.EntryOpenRequests;
        Assert.True(entryOpensAfterFirst > 0);

        // A second demand for the exact same package identity and content is
        // not recognized as a duplicate of the first: nothing joins an
        // existing operation or reuses its decoded participants.
        using PackageAssemblyContextRealization second =
            workspace.RealizePackageAssemblyContextRoles(
                [package],
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            2 * entryOpensAfterFirst,
            content.EntryOpenRequests);
        Assert.NotSame(first.SurfaceGroup, second.SurfaceGroup);
        Assert.NotSame(
            Assert.Single(first.SurfaceParticipants).Participant,
            Assert.Single(second.SurfaceParticipants).Participant);
        Assert.Equal(2, GroupCount(workspace));
    }

    static byte[] Archive(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, byte[] content) in entries)
            {
                using Stream destination = archive
                    .CreateEntry(path, CompressionLevel.NoCompression)
                    .Open();
                destination.Write(content);
            }
        }

        return buffer.ToArray();
    }

    static int GroupCount(InspectionWorkspace workspace)
    {
        System.Reflection.FieldInfo field =
            typeof(InspectionWorkspace).GetField(
                "_groups",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "InspectionWorkspace._groups was not found.");
        return ((System.Collections.ICollection)field.GetValue(workspace)!)
            .Count;
    }

    /// <summary>
    /// Wraps an in-memory nupkg archive and counts every entry-open call so a
    /// test can observe whether a second realization request reopens content
    /// that a first request already opened.
    /// </summary>
    sealed class CountingPackageContent(byte[] nupkgBytes)
        : IPackageContent, IPackageContentEntryManifest
    {
        readonly InMemoryPackageContent _inner =
            new(nupkgBytes, fromCache: false, producerKey: "tests");

        public int EntryOpenRequests { get; private set; }

        public string? RootPath => _inner.RootPath;
        public string? NupkgPath => _inner.NupkgPath;
        public bool FromCache => _inner.FromCache;
        public string ProducerKey => _inner.ProducerKey;
        public bool RequiresArchiveTreeMatch =>
            _inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive([NotNullWhen(true)] out Stream? stream) =>
            _inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            TryOpenEntry(relativePath, long.MaxValue, out stream);

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            EntryOpenRequests++;
            return _inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries() =>
            _inner.EnumerateEntries();

        public bool TryGetEntryLength(string relativePath, out long length) =>
            _inner.TryGetEntryLength(relativePath, out length);

        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
            _inner.EnumerateEntriesWithLengths();
    }
}
