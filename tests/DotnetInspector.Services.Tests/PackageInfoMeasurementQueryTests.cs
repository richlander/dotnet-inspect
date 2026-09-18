using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using DotnetInspector.Packages;

namespace DotnetInspector.Services.Tests;

public sealed class PackageInfoMeasurementQueryTests
{
    [Fact]
    public void RealPackage_SystemTextJsonReportsArchiveAndSelectedSlice()
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "PackageHouse",
            "System.Text.Json.10.0.0.nupkg");
        byte[] package = File.ReadAllBytes(packagePath);
        var content = new InMemoryPackageContent(
            package,
            fromCache: false,
            producerKey: "nuget.org");

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                content,
                "System.Text.Json",
                PackageInfoSliceProfile.Compile);

        var archive = Assert.IsType<PackageArchiveSizeMeasurement.Available>(
            receipt.ArchiveSize);
        var selected =
            Assert.IsType<PackageSelectedTfmMeasurement.Available>(
                receipt.SelectedTargetFramework);
        Assert.Equal(package.Length, archive.Bytes);
        Assert.Equal("net10.0", selected.TargetFramework);
        Assert.Equal(1, selected.LibraryCount);
        Assert.True(selected.UncompressedSize > 0);
        Assert.True(archive.Bytes > selected.UncompressedSize);
    }

    [Fact]
    public void CompileProfile_AggregatesImplementationEntries()
    {
        InMemoryPackageContent content = Content(
            out int archiveLength,
            ("ref/net8.0/Example.dll", 4),
            ("ref/net8.0/Example.More.dll", 5),
            ("lib/net8.0/Example.dll", 40),
            ("lib/net8.0/Example.More.dll", 50),
            ("lib/net8.0/fr/Example.resources.dll", 100),
            ("lib/net6.0/Example.dll", 60));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                content,
                "Example",
                PackageInfoSliceProfile.Compile);

        var archive = Assert.IsType<PackageArchiveSizeMeasurement.Available>(
            receipt.ArchiveSize);
        Assert.Equal(archiveLength, archive.Bytes);
        Assert.Same(content.GenerationIdentity, receipt.Generation);
        Assert.Equal(PackageInfoSliceProfile.Compile, receipt.Profile);
        Assert.Null(receipt.RequestedTargetFramework);
        Assert.Equal(["net8.0", "net6.0"], receipt.AvailableTargetFrameworks);
        Assert.Equal(
            PackageCompileAssetSelectionPolicy.HighestAvailable,
            receipt.CompileSelection!.Policy);
        var selected =
            Assert.IsType<PackageSelectedTfmMeasurement.Available>(
                receipt.SelectedTargetFramework);
        Assert.Equal("net8.0", selected.TargetFramework);
        Assert.Equal(90, selected.UncompressedSize);
        Assert.Equal(2, selected.LibraryCount);
        Assert.Equal(
            ["lib/net8.0/Example.More.dll", "lib/net8.0/Example.dll"],
            selected.MeasuredEntries);
    }

    [Fact]
    public void CompileProfile_ExplicitTargetReportsCompatibleSelection()
    {
        InMemoryPackageContent content = Content(
            out _,
            ("lib/net8.0/Example.dll", 8),
            ("lib/net6.0/Example.dll", 6));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                content,
                "Example",
                PackageInfoSliceProfile.Compile,
                requestedTargetFramework: "net10.0");

        Assert.Equal(
            PackageCompileAssetSelectionPolicy.ExplicitTarget,
            receipt.CompileSelection!.Policy);
        Assert.Equal(
            "net10.0",
            receipt.CompileSelection.RequestedTargetFramework);
        Assert.Equal("net10.0", receipt.RequestedTargetFramework);
        var selected =
            Assert.IsType<PackageSelectedTfmMeasurement.Available>(
                receipt.SelectedTargetFramework);
        Assert.Equal("net8.0", selected.TargetFramework);
        Assert.Equal(8, selected.UncompressedSize);
    }

    [Fact]
    public void CompileProfile_EmptyGroupIsAZeroValuedSelection()
    {
        InMemoryPackageContent content = Content(
            out _,
            ("ref/net9.0/_._", 0),
            ("lib/net8.0/Example.dll", 8));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                content,
                "Example",
                PackageInfoSliceProfile.Compile);

        var selected =
            Assert.IsType<PackageSelectedTfmMeasurement.Available>(
                receipt.SelectedTargetFramework);
        Assert.Equal("net9.0", selected.TargetFramework);
        Assert.Equal(0, selected.UncompressedSize);
        Assert.Equal(0, selected.LibraryCount);
        Assert.Empty(selected.MeasuredEntries);
    }

    [Fact]
    public void EmptyGroupWithoutManifestIsUnavailable()
    {
        InMemoryPackageContent inner = Content(
            out _,
            ("ref/net9.0/_._", 0));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                new ContentWithoutManifest(inner),
                "Example",
                PackageInfoSliceProfile.Compile);

        var unavailable =
            Assert.IsType<PackageSelectedTfmMeasurement.Unavailable>(
                receipt.SelectedTargetFramework);
        Assert.Equal(
            PackageInfoMeasurementFailureKind.EntryManifestUnavailable,
            unavailable.Failure.Kind);
    }

    [Fact]
    public void CompileProfile_CaseCollidingSelectedEntriesAreInvalid()
    {
        InMemoryPackageContent content = Content(
            out _,
            ("ref/net8.0/Example.dll", 4),
            ("REF/NET8.0/example.dll", 5));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                content,
                "Example",
                PackageInfoSliceProfile.Compile);

        var invalid = Assert.IsType<PackageSelectedTfmMeasurement.Invalid>(
            receipt.SelectedTargetFramework);
        Assert.Equal(
            PackageInfoMeasurementFailureKind.AmbiguousSelectedEntry,
            invalid.Failure.Kind);
    }

    [Fact]
    public void ToolProfile_AggregatesEveryLibraryShapedDll()
    {
        InMemoryPackageContent content = Content(
            out _,
            ("tools/net10.0/any/Alpha.dll", 10),
            ("tools/net10.0/any/Beta.dll", 20),
            ("tools/net10.0/any/fr/Alpha.resources.dll", 100),
            ("tools/net8.0/any/Alpha.dll", 80));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                content,
                "Example.Tool",
                PackageInfoSliceProfile.Tool);

        Assert.Equal(["net10.0", "net8.0"], receipt.AvailableTargetFrameworks);
        Assert.Null(receipt.CompileSelection);
        var selected =
            Assert.IsType<PackageSelectedTfmMeasurement.Available>(
                receipt.SelectedTargetFramework);
        Assert.Equal("net10.0", selected.TargetFramework);
        Assert.Equal(30, selected.UncompressedSize);
        Assert.Equal(2, selected.LibraryCount);
    }

    [Fact]
    public void ToolProfile_ExcludesNativeRuntimeDlls()
    {
        InMemoryPackageContent content = Content(
            out _,
            ("tools/net10.0/any/Managed.dll", 10),
            ("tools/net10.0/any/runtimes/win-x64/native/Native.dll", 100),
            ("tools/net10.0/any/.store/native.package/1.0.0/"
                + "native.package/1.0.0/runtimes/linux-x64/native/Native.dll",
                200));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                content,
                "Example.Tool",
                PackageInfoSliceProfile.Tool);

        var selected =
            Assert.IsType<PackageSelectedTfmMeasurement.Available>(
                receipt.SelectedTargetFramework);
        Assert.Equal(10, selected.UncompressedSize);
        Assert.Equal(1, selected.LibraryCount);
        Assert.Equal(
            ["tools/net10.0/any/Managed.dll"],
            selected.MeasuredEntries);
    }

    [Fact]
    public void MissingEntryManifestIsUnavailable()
    {
        InMemoryPackageContent inner = Content(
            out _,
            ("lib/net8.0/Example.dll", 8));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                new ContentWithoutManifest(inner),
                "Example",
                PackageInfoSliceProfile.Compile);

        var unavailable =
            Assert.IsType<PackageSelectedTfmMeasurement.Unavailable>(
                receipt.SelectedTargetFramework);
        Assert.Equal(
            PackageInfoMeasurementFailureKind.EntryManifestUnavailable,
            unavailable.Failure.Kind);
    }

    [Fact]
    public void MissingArchiveIsUnavailable()
    {
        InMemoryPackageContent inner = Content(
            out _,
            ("lib/net8.0/Example.dll", 8));

        PackageInfoMeasurementReceipt receipt =
            PackageInfoMeasurementQuery.Evaluate(
                new ContentWithoutArchive(inner),
                "Example",
                PackageInfoSliceProfile.Compile);

        var unavailable =
            Assert.IsType<PackageArchiveSizeMeasurement.Unavailable>(
                receipt.ArchiveSize);
        Assert.Equal(
            PackageInfoMeasurementFailureKind.ArchiveUnavailable,
            unavailable.Failure.Kind);
    }

    private static InMemoryPackageContent Content(
        out int archiveLength,
        params (string Path, int Length)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(
            buffer,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string path, int length) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    path,
                    CompressionLevel.NoCompression);
                using Stream stream = entry.Open();
                stream.Write(new byte[length]);
            }
        }

        byte[] bytes = buffer.ToArray();
        archiveLength = bytes.Length;
        return new InMemoryPackageContent(
            bytes,
            fromCache: false,
            producerKey: "test");
    }

    private sealed class ContentWithoutManifest(IPackageContent inner)
        : IPackageContent
    {
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public PackageContentGenerationIdentity GenerationIdentity =>
            inner.GenerationIdentity;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;
        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);
        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenEntry(relativePath, out stream);
        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();
    }

    private sealed class ContentWithoutArchive(InMemoryPackageContent inner)
        : IPackageContent, IPackageContentEntryManifest
    {
        public string? RootPath => inner.RootPath;
        public string? NupkgPath => inner.NupkgPath;
        public bool FromCache => inner.FromCache;
        public string ProducerKey => inner.ProducerKey;
        public PackageContentGenerationIdentity GenerationIdentity =>
            inner.GenerationIdentity;
        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;
        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream)
        {
            stream = null;
            return false;
        }
        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenEntry(relativePath, out stream);
        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();
        public bool TryGetEntryLength(
            string relativePath,
            out long length) =>
            inner.TryGetEntryLength(relativePath, out length);
        public IReadOnlyList<PackageContentEntry>
            EnumerateEntriesWithLengths() =>
            inner.EnumerateEntriesWithLengths();
    }
}
