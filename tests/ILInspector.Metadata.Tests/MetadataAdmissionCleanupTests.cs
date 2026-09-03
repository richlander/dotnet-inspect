using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataAdmissionCleanupTests
{
    const string ExtensionScannerWorkerVariable =
        "DOTNET_INSPECT_EXTENSION_SCANNER_LIFETIME_WORKER";

    // A reader whose types were partially indexed is still aliased by every
    // published index entry, so it must stay alive for the whole walk. The
    // regression this guards terminates the process, so it runs in a child.
    [Fact]
    public void ExtensionScanner_PartialIndexKeepsReaderAliveForWholeWalk()
        => RunExtensionScannerWorker(
            nameof(ExtensionScannerPartialIndexWorker));

    [Fact]
    public void ExtensionScannerPartialIndexWorker()
    {
        if (!IsSelectedExtensionScannerWorker(
                nameof(ExtensionScannerPartialIndexWorker)))
        {
            return;
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-partial-index-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildTruncatedStringHeap());
        try
        {
            string indexed = LastTypeIndexedBeforeDecodeFailure(path);

            try
            {
                ExtensionMethodScanner.FindReachableTypes(
                    indexed,
                    [path],
                    maxDepth: 3);
            }
            catch (BadImageFormatException)
            {
                // A visible decode failure is an acceptable outcome. Reading
                // through a disposed reader would terminate the process, which
                // the parent observes as a non-zero exit code.
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    static string LastTypeIndexedBeforeDecodeFailure(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen);
        Assert.True(MetadataFormatAdmission.AdmitImage(peReader));
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);

        string? indexed = null;
        try
        {
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition =
                    reader.GetTypeDefinition(handle);
                string fullName = reader.GetFullTypeName(typeDefinition);
                _ = reader.GetString(typeDefinition.Name);
                indexed = fullName;
            }
        }
        catch (BadImageFormatException)
        {
            Assert.NotNull(indexed);
            return indexed;
        }

        Assert.Fail(
            "The truncated string heap indexed every type without failing, "
            + "so the reader-lifetime property is not exercised.");
        return indexed!;
    }

    internal static byte[] BuildTruncatedStringHeap()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MetadataAdmissionCleanupTests).Assembly.Location);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        int cursor =
            metadataStart
            + 16
            + versionLength
            + sizeof(ushort);
        int streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(cursor, sizeof(ushort)));
        cursor += sizeof(ushort);
        for (int i = 0; i < streamCount; i++)
        {
            int sizeOffset = cursor + sizeof(int);
            int nameOffset = cursor + (2 * sizeof(int));
            int nameEnd = nameOffset;
            while (image[nameEnd] != 0)
                nameEnd++;

            int nameLength = nameEnd - nameOffset;
            if (Encoding.ASCII.GetString(image, nameOffset, nameLength)
                is "#Strings")
            {
                int size = BinaryPrimitives.ReadInt32LittleEndian(
                    image.AsSpan(sizeOffset, sizeof(int)));
                BinaryPrimitives.WriteInt32LittleEndian(
                    image.AsSpan(sizeOffset, sizeof(int)),
                    (size / 2) & ~3);
                return image;
            }

            cursor = nameOffset + ((nameLength + 1 + 3) & ~3);
        }

        throw new InvalidOperationException(
            "The image does not declare a #Strings heap.");
    }

    static bool IsSelectedExtensionScannerWorker(string methodName)
        => Environment.GetEnvironmentVariable(
            ExtensionScannerWorkerVariable) == methodName;

    static void RunExtensionScannerWorker(string workerMethod)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(
            typeof(MetadataAdmissionCleanupTests).Assembly.Location);
        startInfo.ArgumentList.Add("-method");
        startInfo.ArgumentList.Add($"*{workerMethod}*");
        startInfo.Environment[ExtensionScannerWorkerVariable] = workerMethod;

        using Process? process = Process.Start(startInfo);
        Assert.NotNull(process);
        bool exited = process.WaitForExit(120_000);
        if (!exited)
            process.Kill(entireProcessTree: true);
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();

        Assert.True(exited, $"Child worker {workerMethod} timed out.");
        Assert.True(
            process.ExitCode == 0,
            $"Child worker {workerMethod} exited {process.ExitCode}.\n"
            + $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
    }
    [Fact]
    public void TypeDeclarationInventory_CleanupCannotReplaceFormatRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildManagedWindowsMetadata()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")));

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.UnsupportedMetadataFormat,
            rejected.Failure.Kind);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void TypeDeclarationInventory_PreservesMalformedRootReason()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildMalformedMetadataRoot()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")));

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            rejected.Failure.MetadataRootReason);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void TypeDeclarationInventory_MetadataStreamCountOverflowIsInvalidImage()
    {
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                Descriptor(BuildOverflowingMetadataStreamCount()));

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Null(rejected.Failure.MetadataRootReason);
    }

    [Fact]
    public void TypeDeclarationInventory_CleanupCannotReplaceNoMetadataRejection()
    {
        AssemblyTypeDeclarationInventoryOutcome outcome =
            AssemblyTypeDeclarationInventoryReader.Read(
                Descriptor(BuildNoMetadataImage()));

        var rejected =
            Assert.IsType<AssemblyTypeDeclarationInventoryOutcome.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
    }

    static string? LastPublicTypeIndexedBeforeDecodeFailure(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(
            stream,
            PEStreamOptions.LeaveOpen);
        MetadataReader reader =
            MetadataFormatAdmission.GetMetadataReader(peReader);

        string? indexed = null;
        try
        {
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition typeDefinition =
                    reader.GetTypeDefinition(handle);
                if (!typeDefinition.IsPublic)
                    continue;

                string name = reader.GetString(typeDefinition.Name);
                if (TypeFilters.IsCompilerGenerated(name))
                    continue;

                string ns = reader.GetString(typeDefinition.Namespace);
                indexed = TypeResolver.GetFullName(ns, name);
            }
        }
        catch (BadImageFormatException)
        {
            return indexed;
        }

        return null;
    }

    internal static byte[] BuildTruncatedStringHeapAt(int pct)
    {
        byte[] image = File.ReadAllBytes(
            typeof(MetadataAdmissionCleanupTests).Assembly.Location);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        int cursor = metadataStart + 16 + versionLength + sizeof(ushort);
        int streamCount = BinaryPrimitives.ReadUInt16LittleEndian(
            image.AsSpan(cursor, sizeof(ushort)));
        cursor += sizeof(ushort);
        for (int i = 0; i < streamCount; i++)
        {
            int sizeOffset = cursor + sizeof(int);
            int nameOffset = cursor + (2 * sizeof(int));
            int nameEnd = nameOffset;
            while (image[nameEnd] != 0) nameEnd++;
            int nameLength = nameEnd - nameOffset;
            if (Encoding.ASCII.GetString(image, nameOffset, nameLength) is "#Strings")
            {
                int size = BinaryPrimitives.ReadInt32LittleEndian(
                    image.AsSpan(sizeOffset, sizeof(int)));
                BinaryPrimitives.WriteInt32LittleEndian(
                    image.AsSpan(sizeOffset, sizeof(int)),
                    (int)(((long)size * pct / 100) & ~3));
                return image;
            }
            cursor = nameOffset + ((nameLength + 1 + 3) & ~3);
        }
        throw new InvalidOperationException("no #Strings");
    }

    [Fact]
    public void DependencyScan_RejectedParticipantContributesNoRowsToTheIndex()
    {
        // A 90% string-heap truncation decodes several public type rows before
        // failing, which is what makes the contamination observable; the 50%
        // fixture fails before any public row and cannot exercise it.
        string truncated = WriteTempImage(BuildTruncatedStringHeapAt(90));
        // A neighbor that does not define the probed name, so resolving it can
        // only come from the rejected participant's rows.
        string healthy = typeof(string).Assembly.Location;
        try
        {
            string? leaked =
                LastPublicTypeIndexedBeforeDecodeFailure(truncated);
            Assert.NotNull(leaked);

            TypeDependencyResult result =
                TypeDependencyScanner.BuildDependencyTree(
                    leaked!,
                    [truncated, healthy]);

            // The participant is reported as rejected, so none of its rows may
            // resolve. Otherwise the scan emits a tree built from an assembly
            // it simultaneously reports as excluded — and these names are read
            // from the truncated heap, so they are not even correct.
            Assert.Single(result.Rejections);
            Assert.False(result.Found);
        }
        finally
        {
            File.Delete(truncated);
        }
    }

    [Fact]
    public void DependencyScan_MalformedRootKeepsItsExactReasonBesideHealthyNeighbor()
    {
        string malformed = WriteTempImage(BuildMalformedMetadataRoot());
        string healthy =
            typeof(MetadataAdmissionCleanupTests).Assembly.Location;
        try
        {
            TypeDependencyResult result =
                TypeDependencyScanner.BuildDependencyTree(
                    typeof(MetadataAdmissionCleanupTests).FullName!,
                    [malformed, healthy]);

            Assert.True(result.Found);
            TypeDependencyRejection rejection = Assert.Single(
                result.Rejections);
            Assert.Equal(malformed, rejection.AssemblyPath);
            Assert.Equal(
                TypeDependencyRejectionKind.MalformedMetadataRoot,
                rejection.Kind);
            Assert.Equal(
                MetadataRootMalformedReason.InvalidSignature,
                rejection.MetadataRootReason);
        }
        finally
        {
            File.Delete(malformed);
        }
    }

    [Fact]
    public void DependencyScan_InvalidImageDoesNotHideHealthyNeighbor()
    {
        string malformed = WriteTempImage(
            BuildOverflowingMetadataStreamCount());
        string healthy =
            typeof(MetadataAdmissionCleanupTests).Assembly.Location;
        try
        {
            TypeDependencyResult result =
                TypeDependencyScanner.BuildDependencyTree(
                    typeof(MetadataAdmissionCleanupTests).FullName!,
                    [malformed, healthy]);

            Assert.True(result.Found);
            TypeDependencyRejection rejection = Assert.Single(
                result.Rejections);
            Assert.Equal(malformed, rejection.AssemblyPath);
            Assert.Equal(
                TypeDependencyRejectionKind.InvalidImage,
                rejection.Kind);
        }
        finally
        {
            File.Delete(malformed);
        }
    }

    [Fact]
    public void DependencyScan_SoleInvalidImageStaysExact()
    {
        string malformed = WriteTempImage(
            BuildOverflowingMetadataStreamCount());
        try
        {
            // The scan must not degrade a decode failure into "type not
            // found": with no surviving participant the invalid-image
            // outcome is the caller's exact result.
            BadImageFormatException thrown =
                Assert.Throws<BadImageFormatException>(
                    () => TypeDependencyScanner.BuildDependencyTree(
                        "Missing.Type",
                        [malformed]));

            // The decoder's own failure is carried through, not replaced by a
            // reconstruction. A reconstruction would name the file and carry
            // no cause, which is what these two assertions pin against.
            Assert.IsType<OverflowException>(
                Assert.Throws<OverflowException>(
                    () => ReadMetadataDirectly(malformed)));
            Assert.IsType<OverflowException>(thrown.InnerException);
            Assert.DoesNotContain(
                malformed,
                thrown.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(malformed);
        }
    }

    [Fact]
    public void DependencyScan_EveryRejectionSurvivesAnAllRejectedScan()
    {
        string winmd = WriteTempImage(BuildManagedWindowsMetadata());
        string malformedRoot = WriteTempImage([0x4d, 0x5a]);
        string invalidImage = WriteTempImage(
            BuildOverflowingMetadataStreamCount());
        try
        {
            // Throwing one rejection would silently discard the others, which
            // is the evidence loss this contract exists to prevent. Every
            // candidate keeps its own mechanism, and the path-to-mechanism
            // correspondence stays typed rather than living in display text.
            AllCandidatesRejectedException rejected =
                Assert.Throws<AllCandidatesRejectedException>(
                    () => TypeDependencyScanner.BuildDependencyTree(
                        "Missing.Type",
                        [winmd, malformedRoot, invalidImage]));

            Assert.Equal(3, rejected.Rejections.Length);
            Assert.Equal(
                rejected.Rejections.Length,
                rejected.InnerExceptions.Count);

            // Scan order, so each record pairs with the inner at its index.
            Assert.Equal(
                [winmd, malformedRoot, invalidImage],
                rejected.Rejections.Select(r => r.AssemblyPath));
            Assert.Equal(
                [
                    TypeDependencyRejectionKind.UnsupportedMetadataFormat,
                    TypeDependencyRejectionKind.MalformedMetadataRoot,
                    TypeDependencyRejectionKind.InvalidImage,
                ],
                rejected.Rejections.Select(r => r.Kind));

            Assert.IsType<UnsupportedMetadataFormatException>(
                rejected.InnerExceptions[0]);

            MalformedMetadataRootException root =
                Assert.IsType<MalformedMetadataRootException>(
                    rejected.InnerExceptions[1]);
            Assert.Equal(
                MetadataRootMalformedReason.UnmappableMetadataDirectory,
                root.Reason);
            Assert.Equal(
                root.Reason,
                rejected.Rejections[1].MetadataRootReason);

            // The invalid image keeps the decoder's captured failure rather
            // than one reconstructed from the record, and it is the plain
            // invalid-image type, not the malformed-root refinement.
            BadImageFormatException invalid =
                Assert.IsType<BadImageFormatException>(
                    rejected.InnerExceptions[2]);
            Assert.IsType<OverflowException>(invalid.InnerException);
            Assert.DoesNotContain(
                invalidImage,
                invalid.Message,
                StringComparison.Ordinal);

            // Each path is rendered beside its own mechanism, so a displayed
            // error cannot pair a file with the wrong reason.
            foreach (var (record, mechanism) in rejected.Rejections.Zip(
                rejected.InnerExceptions))
            {
                Assert.Contains(
                    $"'{record.AssemblyPath}': {mechanism.Message}",
                    rejected.Message,
                    StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(winmd);
            File.Delete(malformedRoot);
            File.Delete(invalidImage);
        }
    }

    static void ReadMetadataDirectly(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        _ = peReader.GetMetadataReader().TypeDefinitions.Count;
    }

    static string WriteTempImage(byte[] image)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dependency-scan-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        return path;
    }

    [Fact]
    public void ApiSurface_CleanupCannotReplaceFormatRejection()
    {
        var stream = new DisposeCountingMemoryStream(
            BuildManagedWindowsMetadata());

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyReader.ExtractApiSurface(
                stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void ApiSummary_CleanupCannotReplaceFormatRejection()
    {
        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyReader.ExtractApiSummarySurface(
                new ThrowingDisposeMemoryStream(
                    BuildManagedWindowsMetadata())));
    }

    [Theory]
    [InlineData(AssemblyReaderOverflowEntryPoint.ModulePath)]
    [InlineData(AssemblyReaderOverflowEntryPoint.ApiSurfacePath)]
    [InlineData(AssemblyReaderOverflowEntryPoint.ApiSurfaceStream)]
    [InlineData(AssemblyReaderOverflowEntryPoint.ApiSummaryPath)]
    [InlineData(AssemblyReaderOverflowEntryPoint.ApiSummaryStream)]
    public void
        AssemblyReader_MetadataStreamCountOverflowUsesInvalidImageOutcome(
            AssemblyReaderOverflowEntryPoint entryPoint)
    {
        byte[] image = BuildOverflowingMetadataStreamCount();
        string path = Path.Combine(
            Path.GetTempPath(),
            $"assembly-reader-overflow-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            ApiSurface? surface = entryPoint switch
            {
                AssemblyReaderOverflowEntryPoint.ModulePath =>
                    AssemblyReader.ExtractModuleApiSurface(path),
                AssemblyReaderOverflowEntryPoint.ApiSurfacePath =>
                    AssemblyReader.ExtractApiSurface(path),
                AssemblyReaderOverflowEntryPoint.ApiSurfaceStream =>
                    AssemblyReader.ExtractApiSurface(
                        new MemoryStream(image, writable: false)),
                AssemblyReaderOverflowEntryPoint.ApiSummaryPath =>
                    AssemblyReader.ExtractApiSummarySurface(path),
                AssemblyReaderOverflowEntryPoint.ApiSummaryStream =>
                    AssemblyReader.ExtractApiSummarySurface(
                        new MemoryStream(image, writable: false)),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(entryPoint)),
            };

            Assert.Null(surface);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ApiSurface_NoMetadataCleanupCannotReplaceNoResult()
    {
        Assert.Null(
            AssemblyReader.ExtractApiSurface(
                new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage())));
    }

    [Fact]
    public void ApiSummary_NoMetadataCleanupCannotReplaceNoResult()
    {
        Assert.Null(
            AssemblyReader.ExtractApiSummarySurface(
                new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage())));
    }

    [Fact]
    public void AssemblyImage_NoMetadataCleanupCannotReplaceEstablishedOutcome()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyImage image = AssemblyImage.Open(
            ResolvedAssemblyReference.Create(
                Identity(),
                path: null,
                () => opened = new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage()),
                AssemblyResolutionProvenance.Local(
                    "format admission test")));

        Assert.False(image.HasMetadata);
        image.Dispose();

        Assert.Equal(1, opened!.DisposeCount);
        Assert.Throws<ObjectDisposedException>(
            () => _ = image.HasMetadata);
    }

    [Fact]
    public void ApiSurface_ReaderConstructionFailureDisposesStreamOnce()
    {
        var stream = new UnreadableDisposeCountingStream();

        Assert.Null(AssemblyReader.ExtractApiSurface(stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void ApiSummary_ReaderConstructionFailureDisposesStreamOnce()
    {
        var stream = new UnreadableDisposeCountingStream();

        Assert.Null(AssemblyReader.ExtractApiSummarySurface(stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void FallbackIdentity_CleanupCannotPreventFallback()
    {
        AssertFallback(BuildManagedWindowsMetadata());
    }

    [Fact]
    public void FallbackIdentity_NoMetadataCleanupCannotPreventFallback()
    {
        AssertFallback(BuildNoMetadataImage());
    }

    [Fact]
    public void FallbackIdentity_ModuleCleanupCannotPreventFallback()
    {
        AssertFallback(BuildManagedModule());
    }

    [Fact]
    public void FallbackIdentity_MetadataStreamCountOverflowCannotPreventFallback()
    {
        AssertFallback(BuildOverflowingMetadataStreamCount());
    }

    [Fact]
    public void StreamIfManaged_NoMetadataCleanupCannotReplaceRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        ResolvedAssemblyReference? assembly =
            ResolvedAssemblyReference.CreateFromStreamIfManaged(
                () => opened = new ThrowingDisposeMemoryStream(
                    BuildNoMetadataImage()),
                AssemblyResolutionProvenance.Local(
                    "format admission test"));

        Assert.Null(assembly);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void Snapshot_CleanupCannotReplaceDirectRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyImageSnapshotResult outcome =
            AssemblyImageSnapshot.Open(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildManagedWindowsMetadata()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")),
                static _ => false,
                static _ => { });

        var rejected =
            Assert.IsType<AssemblyImageSnapshotResult.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.ResourceBudget,
            rejected.Failure.Kind);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void Snapshot_PreservesMalformedRootReason()
    {
        ThrowingDisposeMemoryStream? opened = null;
        AssemblyImageSnapshotResult outcome =
            AssemblyImageSnapshot.Open(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => opened = new ThrowingDisposeMemoryStream(
                        BuildMalformedMetadataRoot()),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")),
                static _ => true,
                static _ => { });

        var rejected =
            Assert.IsType<AssemblyImageSnapshotResult.Rejected>(
                outcome);
        Assert.Equal(
            CandidateOpenFailureKind.InvalidImage,
            rejected.Failure.Kind);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            rejected.Failure.MetadataRootReason);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void RetainedSnapshot_PreservesMalformedRootReason()
    {
        byte[] image = BuildMalformedMetadataRoot();
        AssemblyImageSnapshotResult outcome =
            AssemblyImageSnapshot.FromRetainedContent(
                ResolvedAssemblyReference.Create(
                    Identity(),
                    path: null,
                    () => new MemoryStream(image, writable: false),
                    AssemblyResolutionProvenance.Local(
                        "format admission test")),
                ImmutableArray.Create(image));

        var rejected =
            Assert.IsType<AssemblyImageSnapshotResult.Rejected>(
                outcome);
        Assert.Equal(
            MetadataRootMalformedReason.InvalidSignature,
            rejected.Failure.MetadataRootReason);
    }

    [Fact]
    public void CandidateOpenFailure_PreservesTwoPositionRecordContract()
    {
        Type type = typeof(CandidateOpenFailure);

        Assert.NotNull(type.GetConstructor(
            [typeof(CandidateOpenFailureKind), typeof(string)]));
        System.Reflection.MethodInfo deconstruct = Assert.Single(
            type.GetMethods(),
            method => method.Name == "Deconstruct");
        Assert.Equal(2, deconstruct.GetParameters().Length);
    }

    [Fact]
    public void OpenPrefetched_FormatRejectionDisposesStreamOnce()
    {
        var stream = new DisposeCountingMemoryStream(
            BuildManagedWindowsMetadata());

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyInspectionSession.OpenPrefetched(stream));

        Assert.Equal(1, stream.DisposeCount);
    }

    [Fact]
    public void SurfaceClassification_PreservesMalformedRootReason()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"malformed-surface-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, BuildMalformedMetadataRoot());
        try
        {
            var rejected =
                Assert.IsType<AssemblySurfaceClassificationOutcome.Rejected>(
                    AssemblySurfaceClassifier.Classify(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "format admission test")));
            Assert.Equal(
                MetadataRootMalformedReason.InvalidSignature,
                rejected.Failure.MetadataRootReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SurfaceClassification_MetadataStreamCountOverflowIsInvalidImage()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"overflow-surface-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(
            path,
            BuildOverflowingMetadataStreamCount());
        try
        {
            var rejected =
                Assert.IsType<AssemblySurfaceClassificationOutcome.Rejected>(
                    AssemblySurfaceClassifier.Classify(
                        path,
                        AssemblyResolutionProvenance.Local(
                            "format admission test")));
            Assert.Equal(
                CandidateOpenFailureKind.InvalidImage,
                rejected.Failure.Kind);
            Assert.Null(rejected.Failure.MetadataRootReason);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AssemblyInspector_CleanupCannotReplaceFormatRejection()
    {
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            Identity(),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(
                BuildManagedWindowsMetadata()),
            AssemblyResolutionProvenance.Local(
                "format admission test"));

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => AssemblyInspector.ExtractReferenceIdentitiesAndCompany(
                assembly));
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void AssemblyInspector_NoMetadataCleanupCannotReplaceNoResult()
    {
        ThrowingDisposeMemoryStream? opened = null;
        var assembly = ResolvedAssemblyReference.Create(
            Identity(),
            path: null,
            () => opened = new ThrowingDisposeMemoryStream(
                BuildNoMetadataImage()),
            AssemblyResolutionProvenance.Local(
                "format admission test"));

        var (references, company) =
            AssemblyInspector.ExtractReferenceIdentitiesAndCompany(
                assembly);

        Assert.Empty(references);
        Assert.Null(company);
        Assert.Equal(1, opened!.DisposeCount);
    }

    [Fact]
    public void StreamScanners_CleanupCannotReplaceFormatRejection()
    {
        Action<Stream>[] scans =
        [
            stream => _ = ResourceScanner.Scan(stream),
            stream => _ = ResourceScanner.ExtractAll(
                stream,
                Path.GetTempPath()),
            stream => _ = MethodClassificationScanner.Scan(stream),
            stream => _ = ExtensionMethodScanner.FindAllExtensions(
                stream).ToList(),
            stream => _ = ExtensionMethodScanner.FindExtensions(
                stream,
                "System.String").ToList(),
        ];

        foreach (Action<Stream> scan in scans)
            AssertFormatRejectionSurvivesCleanup(scan);
    }

    [Fact]
    public void StreamScanners_NoMetadataCleanupCannotReplaceNoResult()
    {
        Action<Stream>[] scans =
        [
            stream => Assert.Empty(ResourceScanner.Scan(stream)),
            stream => Assert.Empty(ResourceScanner.ExtractAll(
                stream,
                Path.GetTempPath())),
            stream => Assert.Empty(MethodClassificationScanner.Scan(stream)),
            stream => Assert.Empty(
                ExtensionMethodScanner.FindAllExtensions(stream)),
            stream => Assert.Empty(
                ExtensionMethodScanner.FindExtensions(
                    stream,
                    "System.String")),
        ];

        foreach (Action<Stream> scan in scans)
            AssertNoMetadataResultSurvivesCleanup(scan);
    }

    static void AssertFormatRejectionSurvivesCleanup(
        Action<Stream> scan)
    {
        var stream = new ThrowingDisposeMemoryStream(
            BuildManagedWindowsMetadata());

        Assert.Throws<UnsupportedMetadataFormatException>(
            () => scan(stream));
        Assert.Equal(1, stream.DisposeCount);
    }

    static void AssertNoMetadataResultSurvivesCleanup(
        Action<Stream> scan)
    {
        var stream = new ThrowingDisposeMemoryStream(
            BuildNoMetadataImage());

        scan(stream);

        Assert.Equal(1, stream.DisposeCount);
    }

    static void AssertFallback(byte[] image)
    {
        AssemblyReferenceIdentity fallback = Identity();
        ThrowingDisposeMemoryStream? opened = null;
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromStreamWithFallbackIdentity(
                () => opened = new ThrowingDisposeMemoryStream(image),
                fallback,
                AssemblyResolutionProvenance.Local("format admission test"),
                out bool usedFallback);
        Assert.True(usedFallback);
        Assert.Equal(fallback, assembly.Identity);
        Assert.Equal(1, opened!.DisposeCount);
    }

    static ResolvedAssemblyReference Descriptor(byte[] image)
    {
        return ResolvedAssemblyReference.Create(
            Identity(),
            path: null,
            () => new ThrowingDisposeMemoryStream(image),
            AssemblyResolutionProvenance.Local("format admission test"));
    }

    static AssemblyReferenceIdentity Identity() =>
        new(
            "Unsupported",
            new Version(1, 0, 0, 0),
            Culture: null,
            PublicKeyToken: null);

    internal static byte[] BuildManagedWindowsMetadata()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Unsupported.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Unsupported"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddAssemblyReference(
            metadata.GetOrAddString("mscorlib"),
            new Version(4, 0, 0, 0),
            default,
            default,
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                "WindowsRuntime 1.4;CLR v4.0.30319",
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    internal static byte[] BuildOverflowingMetadataStreamCount()
    {
        byte[] image = File.ReadAllBytes(
            typeof(MetadataAdmissionCleanupTests).Assembly.Location);
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        int metadataStart = peReader.PEHeaders.MetadataStartOffset;
        int versionLength = BinaryPrimitives.ReadInt32LittleEndian(
            image.AsSpan(metadataStart + 12, sizeof(int)));
        int streamCountOffset =
            metadataStart
            + 16
            + versionLength
            + sizeof(ushort);
        BinaryPrimitives.WriteUInt16LittleEndian(
            image.AsSpan(streamCountOffset, sizeof(ushort)),
            ushort.MaxValue);
        return image;
    }

    public enum AssemblyReaderOverflowEntryPoint
    {
        ModulePath,
        ApiSurfacePath,
        ApiSurfaceStream,
        ApiSummaryPath,
        ApiSummaryStream,
    }

    static byte[] BuildManagedModule()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Module.netmodule"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    internal static byte[] BuildNoMetadataImage()
    {
        byte[] image = BuildManagedModule();
        using var peReader = new PEReader(
            ImmutableArray.Create(image));
        PEHeader peHeader = peReader.PEHeaders.PEHeader!;
        int directoryBase =
            peReader.PEHeaders.PEHeaderStartOffset
            + (peHeader.Magic == PEMagic.PE32Plus ? 112 : 96);
        image.AsSpan(directoryBase + (14 * 8), 8).Clear();
        return image;
    }

    internal static byte[] BuildMalformedMetadataRoot()
    {
        byte[] image = BuildManagedWindowsMetadata();
        using var peReader = new PEReader(
            ImmutableArray.Create(image));
        image.AsSpan(
            peReader.PEHeaders.MetadataStartOffset,
            sizeof(uint)).Clear();
        return image;
    }

    internal sealed class ThrowingDisposeMemoryStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
            if (disposing)
            {
                throw new InvalidOperationException(
                    "Synthetic disposal failure.");
            }
        }
    }

    sealed class DisposeCountingMemoryStream(byte[] image)
        : MemoryStream(image, writable: false)
    {
        public int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    sealed class UnreadableDisposeCountingStream : MemoryStream
    {
        public int DisposeCount { get; private set; }

        public override bool CanRead => false;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }
}
