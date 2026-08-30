using System.Diagnostics;
using System.Net.Sockets;
using DotnetInspector.Artifacts.Local;
using DotnetInspector.Artifacts.Workspaces;

namespace DotnetInspector.Artifacts.Tests;

public sealed class LocalArtifactSourceTests
{
    [Fact]
    public async Task LocalPathAdmission_ExpectedKindsAndLinksAreShared()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string file = Path.Combine(root, "input.dll");
        string fileLink = Path.Combine(root, "input-link.dll");
        string hardLink = Path.Combine(root, "input-hard-link.dll");
        string directoryLink = Path.Combine(root, "directory-link");
        string danglingLink = Path.Combine(root, "dangling.dll");
        await File.WriteAllBytesAsync(file, [1], cancellationToken);

        try
        {
            Assert.Equal(
                LocalPathKind.RegularFile,
                LocalPathAdmission.Classify(file, cancellationToken).Kind);
            Assert.Equal(
                LocalPathKind.Directory,
                LocalPathAdmission.Classify(root, cancellationToken).Kind);
            Assert.Equal(
                LocalPathOutcome.Classified,
                LocalPathAdmission.AdmitDirectory(
                    root,
                    cancellationToken).Outcome);
            Assert.Equal(
                LocalPathReason.KindMismatch,
                LocalPathAdmission.AdmitDirectory(
                    file,
                    cancellationToken).Reason);
            if (TryCreateHardLink(hardLink, file))
            {
                LocalPathClassification hardLinkClassification =
                    LocalPathAdmission.Classify(
                        hardLink,
                        cancellationToken);
                Assert.Equal(
                    LocalPathKind.RegularFile,
                    hardLinkClassification.Kind);
                Assert.NotEqual(
                    LocalPathAdmission.Classify(
                        file,
                        cancellationToken).CanonicalPath,
                    hardLinkClassification.CanonicalPath);
            }

            if (OperatingSystem.IsWindows())
            {
                string driveRoot = Path.GetPathRoot(
                    Environment.SystemDirectory)!;
                Assert.Equal(
                    LocalPathKind.Directory,
                    LocalPathAdmission.Classify(
                        driveRoot,
                        cancellationToken).Kind);
                Assert.Contains(
                    Directory.EnumerateFiles(
                        driveRoot,
                        "*",
                        SearchOption.TopDirectoryOnly),
                    candidate =>
                        LocalPathAdmission.Classify(
                            candidate,
                            cancellationToken).Kind
                        == LocalPathKind.RegularFile);
            }

            if (!TryCreateLinks(
                file,
                root,
                fileLink,
                directoryLink,
                danglingLink))
            {
                return;
            }

            Assert.Equal(
                LocalPathKind.RegularFile,
                LocalPathAdmission.Classify(
                    fileLink,
                    cancellationToken).Kind);
            Assert.Equal(
                LocalPathKind.Directory,
                LocalPathAdmission.Classify(
                    directoryLink,
                    cancellationToken).Kind);
            Assert.Equal(
                LocalPathOutcome.Unavailable,
                LocalPathAdmission.Classify(
                    danglingLink,
                    cancellationToken).Outcome);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task
        LocalPathAdmission_StableNonRegularEntriesRejectBeforeOpen()
    {
        Assert.SkipWhen(
            OperatingSystem.IsWindows(),
            "Unix special-entry coverage requires Unix filesystem entry kinds.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string emptyFile = Path.Combine(root, "empty.dll");
        string fifo = Path.Combine(root, "input.fifo");
        string fifoLink = Path.Combine(root, "input-link.fifo");
        string socketPath = Path.Combine(root, "input.socket");
        await File.WriteAllBytesAsync(emptyFile, [], cancellationToken);

        try
        {
            await using (LocalFileAdmission admitted =
                LocalPathAdmission.AdmitRegularFile(
                    emptyFile,
                    cancellationToken))
            {
                Assert.Equal(
                    LocalPathOutcome.Classified,
                    admitted.Classification.Outcome);
                Assert.NotNull(admitted.Stream);
            }

            await CreateFifoAsync(fifo, cancellationToken);
            File.CreateSymbolicLink(fifoLink, fifo);
            await AssertRejectedWithoutBlockingAsync(
                fifo,
                unblockFifo: true,
                cancellationToken);
            await AssertRejectedWithoutBlockingAsync(
                fifoLink,
                unblockFifo: true,
                cancellationToken);

            using (var socket = new Socket(
                AddressFamily.Unix,
                SocketType.Stream,
                ProtocolType.Unspecified))
            {
                socket.Bind(new UnixDomainSocketEndPoint(socketPath));
                await AssertRejectedWithoutBlockingAsync(
                    socketPath,
                    unblockFifo: false,
                    cancellationToken);
            }

            if (File.Exists("/dev/null"))
            {
                await AssertRejectedWithoutBlockingAsync(
                    "/dev/null",
                    unblockFifo: false,
                    cancellationToken);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task
        LocalPathAdmission_ConsumerReceivesTheVerifiedOpenGeneration()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string path = Path.Combine(root, "coordinate.dll");
        string original = Path.Combine(root, "original.dll");
        await File.WriteAllBytesAsync(path, [1, 2, 3], cancellationToken);

        try
        {
            await using LocalFileAdmission admission =
                LocalPathAdmission.AdmitRegularFile(
                    path,
                    cancellationToken);
            Assert.Equal(
                LocalPathOutcome.Classified,
                admission.Classification.Outcome);
            FileStream stream = Assert.IsType<FileStream>(admission.Stream);

            File.Move(path, original);
            await File.WriteAllBytesAsync(
                path,
                [9, 9, 9],
                cancellationToken);

            Assert.Equal([1, 2, 3], ReadAll(stream));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalPathAdmission_OutcomesAndCancellationRemainDistinct()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string missing = Path.Combine(root, "missing.dll");
        string loop = Path.Combine(root, "loop.dll");

        try
        {
            var unavailable =
                Assert.IsType<ArtifactAcquisitionOutcome.Unavailable>(
                    await AcquireAsync(missing, cancellationToken));
            LocalArtifactDiagnostic unavailableDiagnostic =
                Assert.IsType<LocalArtifactDiagnostic>(
                    unavailable.Diagnostic);
            Assert.Equal("local.file.missing", unavailableDiagnostic.Code);
            Assert.Equal(missing, unavailableDiagnostic.RequestedPath);
            Assert.Equal(
                Path.GetFullPath(missing),
                unavailableDiagnostic.CanonicalPath);
            Assert.Equal(
                unavailableDiagnostic.CanonicalPath,
                unavailableDiagnostic.FullPath);

            var wrongKind =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await AcquireAsync(root, cancellationToken));
            Assert.Equal(
                "local.file.unsupported-entry",
                wrongKind.Diagnostic.Code);

            string invalidPath = "invalid\0path";
            var invalid =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await AcquireAsync(invalidPath, cancellationToken));
            LocalArtifactDiagnostic invalidDiagnostic =
                Assert.IsType<LocalArtifactDiagnostic>(
                    invalid.Diagnostic);
            Assert.Equal(
                "local.file.invalid-path",
                invalidDiagnostic.Code);
            Assert.Equal(invalidPath, invalidDiagnostic.RequestedPath);
            Assert.Null(invalidDiagnostic.CanonicalPath);
            Assert.Equal(invalidPath, invalidDiagnostic.FullPath);

            if (TryCreateFileLink(loop, loop))
            {
                var unsupportedLink =
                    Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                        await AcquireAsync(loop, cancellationToken));
                Assert.Equal(
                    "local.file.unsupported-entry",
                    unsupportedLink.Diagnostic.Code);
            }

            var failed =
                Assert.IsType<ArtifactAcquisitionOutcome.Failed>(
                    LocalArtifactSource.ProjectAdmissionOutcome(
                        LocalPathClassification.Failed(
                            LocalPathReason.AdmissionFailed,
                            missing,
                            Path.GetFullPath(missing))));
            Assert.Equal(
                "local.file.read-failed",
                failed.Diagnostic.Code);

            var unsupportedClassifier =
                Assert.IsType<ArtifactAcquisitionOutcome.Failed>(
                    LocalArtifactSource.ProjectAdmissionOutcome(
                        LocalPathClassification.Failed(
                            LocalPathReason.ClassificationUnsupported,
                            missing,
                            Path.GetFullPath(missing))));
            Assert.Equal(
                "local.file.classification-unsupported",
                unsupportedClassifier.Diagnostic.Code);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await AcquireAsync(
                    missing,
                    cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocalPathAdmission_WindowsPoliciesAreEnumerated()
    {
        Assert.True(LocalPathAdmission.IsSupportedWindowsPathSyntax(@"C:\"));
        Assert.True(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"C:\foo.dll"));
        Assert.True(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\server\share\foo.dll"));
        Assert.True(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\?\C:\foo.dll"));
        Assert.True(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\?\UNC\server\share\foo.dll"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\.\pipe\dotnet-inspect"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\?\pipe\dotnet-inspect"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\server\pipe\dotnet-inspect"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\?\UNC\server\pipe\dotnet-inspect"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\??\C:\foo.dll"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\?\C:\foo\..\bar.dll"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\?\C:/foo.dll"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"\\?\UNC\server\..\foo.dll"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"C:\NUL.dll"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"C:\foo\COM1.txt"));
        Assert.False(
            LocalPathAdmission.IsSupportedWindowsPathSyntax(
                @"C:\foo\NUL::$DATA"));
        Assert.Equal(
            WindowsPathSyntaxDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsPathSyntax(
                @"C:\foo\COM1.txt"));
        Assert.Equal(
            WindowsPathSyntaxDisposition.Invalid,
            LocalPathAdmission.ClassifyWindowsPathSyntax(
                @"\\?\C:\foo\..\bar.dll"));
        Assert.Equal(
            WindowsPathSyntaxDisposition.Supported,
            LocalPathAdmission.ClassifyResolvedWindowsPathSyntax(
                @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\"));

        WindowsKnownReparseTag[] knownTags =
            Enum.GetValues<WindowsKnownReparseTag>();
        WindowsKnownReparseTag[] classifiedTags =
        [
            .. LocalPathAdmission.WindowsReparseTags
                .Select(entry => entry.Tag)
                .Order(),
        ];
        Assert.Equal(
            [.. knownTags.Order()],
            classifiedTags);
        Assert.Equal(
            WindowsReparseDisposition.SupportedLink,
            LocalPathAdmission.ClassifyWindowsReparseTag(0xA0000003));
        Assert.Equal(
            WindowsReparseDisposition.SupportedLink,
            LocalPathAdmission.ClassifyWindowsReparseTag(0xA000000C));
        Assert.Equal(
            WindowsReparseDisposition.DataBearing,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x80000013));
        Assert.Equal(
            WindowsReparseDisposition.DataBearing,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x9000001A));
        Assert.Equal(
            WindowsReparseDisposition.DataBearing,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x9000001C));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x80000014));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x80000023));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x80000024));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x80000025));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x80000026));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0xA000001D));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0xA000001F));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0xA0000022));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0xA0000027));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0x8000001B));
        Assert.Equal(
            WindowsReparseDisposition.Unsupported,
            LocalPathAdmission.ClassifyWindowsReparseTag(0xDEADBEEF));
    }

    [Fact]
    public async Task LocalArtifactSnapshot_MutationCannotChangeInspectionBytes()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string path = TempPath();
        await File.WriteAllBytesAsync(
            path,
            [1, 2, 3],
            cancellationToken);
        try
        {
            await using var session = new ArtifactSetSession();
            await session.AddRequiredAcquisitionAsync(
                (scope, cancellationToken) =>
                    LocalArtifactSource.AcquireFileAsync(
                        scope,
                        path,
                        cancellationToken: cancellationToken),
                [ArtifactWorkspaceRole.CallerDesignated],
                cancellationToken);

            await File.WriteAllBytesAsync(
                path,
                [9, 9, 9],
                cancellationToken);
            Assert.IsType<ArtifactSetPublicationOutcome.Published>(
                await session.SealAsync(cancellationToken));
            File.Delete(path);

            ArtifactQueryAuthorization authorization =
                session.CreateQueryAuthorization();
            using ArtifactQueryLease lease =
                session.IssueLease(authorization);
            ArtifactDescriptor descriptor =
                Assert.Single(session.GetCatalog(lease));
            using Stream opened =
                session.OpenRead(descriptor.Identity, lease);

            Assert.Equal([1, 2, 3], ReadAll(opened));
            var provenance =
                Assert.IsType<LocalArtifactProvenance>(
                    session.GetProvenance(
                        descriptor.Identity,
                        lease));
            Assert.Equal(Path.GetFullPath(path), provenance.FullPath);
            Assert.Equal(3, provenance.ContentLength);
            Assert.True(
                session.HasRole(
                    descriptor.Identity,
                    ArtifactWorkspaceRole.CallerDesignated,
                    lease));
            Assert.Null(descriptor.MediaType);
            Assert.Equal("local-file", descriptor.Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LocalFileAcquisition_ReportsMissingAndOversizeInputs()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string missing = TempPath();
        var missingOwner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization missingAuthorization =
            missingOwner.CreateAdmissionAuthorization();
        using ArtifactContributionScope missingScope =
            missingOwner.BeginContribution(missingAuthorization);

        var unavailable =
            Assert.IsType<ArtifactAcquisitionOutcome.Unavailable>(
                await LocalArtifactSource.AcquireFileAsync(
                    missingScope,
                    missing,
                    cancellationToken: cancellationToken));
        Assert.Equal(
            "local.file.missing",
            unavailable.Diagnostic.Code);

        string path = TempPath();
        await File.WriteAllBytesAsync(
            path,
            [1, 2],
            cancellationToken);
        try
        {
            var owner = new ArtifactGenerationAuthority();
            ArtifactAdmissionAuthorization authorization =
                owner.CreateAdmissionAuthorization();
            using ArtifactContributionScope scope =
                owner.BeginContribution(authorization);

            var rejected =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await LocalArtifactSource.AcquireFileAsync(
                        scope,
                        Path.Combine(
                            Path.GetDirectoryName(path)!,
                            ".",
                            Path.GetFileName(path)),
                        new LocalArtifactAcquisitionOptions
                        {
                            MaxFileBytes = 1,
                        },
                        cancellationToken));
            Assert.Equal(
                "local.file.size-limit",
                rejected.Diagnostic.Code);
            var diagnostic = Assert.IsType<LocalArtifactDiagnostic>(
                rejected.Diagnostic);
            Assert.Contains(
                $"{Path.DirectorySeparatorChar}.{Path.DirectorySeparatorChar}",
                diagnostic.RequestedPath,
                StringComparison.Ordinal);
            Assert.Equal(path, diagnostic.CanonicalPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ArtifactAcquisition_CancellationRemainsCancellation()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string path = TempPath();
        await File.WriteAllBytesAsync(
            path,
            [1],
            cancellationToken);
        try
        {
            var owner = new ArtifactGenerationAuthority();
            ArtifactAdmissionAuthorization authorization =
                owner.CreateAdmissionAuthorization();
            using ArtifactContributionScope scope =
                owner.BeginContribution(authorization);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await LocalArtifactSource.AcquireFileAsync(
                    scope,
                    path,
                    cancellationToken: cancellation.Token));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempPath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-local-artifact-{Guid.NewGuid():N}.bin");

    private static string TempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-local-artifact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async ValueTask<ArtifactAcquisitionOutcome> AcquireAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization authorization =
            owner.CreateAdmissionAuthorization();
        using ArtifactContributionScope scope =
            owner.BeginContribution(authorization);
        return await LocalArtifactSource.AcquireFileAsync(
            scope,
            path,
            cancellationToken: cancellationToken);
    }

    private static async Task AssertRejectedWithoutBlockingAsync(
        string path,
        bool unblockFifo,
        CancellationToken cancellationToken)
    {
        Task<ArtifactAcquisitionOutcome> acquisition = Task.Run(
            async () => await AcquireAsync(path, cancellationToken),
            cancellationToken);
        ArtifactAcquisitionOutcome outcome;
        try
        {
            outcome = await acquisition.WaitAsync(
                TimeSpan.FromSeconds(5),
                cancellationToken);
        }
        catch (TimeoutException)
        {
            if (unblockFifo)
            {
                await using var writer = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1,
                    useAsync: true);
                await acquisition.WaitAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken);
            }

            Assert.Fail(
                "Stable non-regular entry reached a blocking content open.");
            return;
        }

        var rejected =
            Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(outcome);
        Assert.Equal(
            "local.file.unsupported-entry",
            rejected.Diagnostic.Code);
    }

    private static async Task CreateFifoAsync(
        string path,
        CancellationToken cancellationToken)
    {
        using Process process = Process.Start(
            new ProcessStartInfo("mkfifo", [path])
            {
                RedirectStandardError = true,
            }) ?? throw new InvalidOperationException(
                "Could not start mkfifo.");
        await process.WaitForExitAsync(cancellationToken);
        Assert.Equal(0, process.ExitCode);
    }

    private static bool TryCreateLinks(
        string file,
        string directory,
        string fileLink,
        string directoryLink,
        string danglingLink)
    {
        try
        {
            File.CreateSymbolicLink(fileLink, file);
            Directory.CreateSymbolicLink(directoryLink, directory);
            File.CreateSymbolicLink(
                danglingLink,
                Path.Combine(directory, "missing.dll"));
            return true;
        }
        catch (Exception ex) when (OperatingSystem.IsWindows()
            && ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (OperatingSystem.IsWindows()
            && ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateHardLink(string link, string target)
    {
        try
        {
            File.CreateHardLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var destination = new MemoryStream();
        stream.CopyTo(destination);
        return destination.ToArray();
    }
}
