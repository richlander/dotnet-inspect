using System.Diagnostics;
using System.Net.Sockets;
using DotnetInspector.Artifacts.Local;
using DotnetInspector.Artifacts.Workspaces;
using Microsoft.Win32.SafeHandles;

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
        string socketPath = Path.Combine(
            Path.GetTempPath(),
            $"di-{Guid.NewGuid():N}.socket");
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
            File.Delete(socketPath);
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
                @"//?/C:/foo.dll"));
        foreach (char first in new[] { '\\', '/' })
        {
            foreach (char second in new[] { '\\', '/' })
            {
                foreach (char fourth in new[] { '\\', '/' })
                {
                    string questionPrefix =
                        string.Concat(first, second, '?', fourth);
                    WindowsPathSyntaxDisposition questionDisposition =
                        first == '\\'
                        && second == '\\'
                        && fourth == '\\'
                            ? WindowsPathSyntaxDisposition.Supported
                            : WindowsPathSyntaxDisposition.Invalid;
                    Assert.Equal(
                        questionDisposition,
                        LocalPathAdmission.ClassifyWindowsPathSyntax(
                            $"{questionPrefix}C:\\foo.dll"));

                    string dotPrefix =
                        string.Concat(first, second, '.', fourth);
                    WindowsPathSyntaxDisposition dotDisposition =
                        first == '\\'
                        && second == '\\'
                        && fourth == '\\'
                            ? WindowsPathSyntaxDisposition.Unsupported
                            : WindowsPathSyntaxDisposition.Invalid;
                    Assert.Equal(
                        dotDisposition,
                        LocalPathAdmission.ClassifyWindowsPathSyntax(
                            $"{dotPrefix}pipe\\dotnet-inspect"));
                }
            }
        }
        Assert.Equal(
            WindowsPathSyntaxDisposition.Invalid,
            LocalPathAdmission.ClassifyWindowsPathSyntax(
                @"/??/C:/foo.dll"));
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
            @"\\?\C:\root\target.dll",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\C:\root\links\..\target.dll"));
        Assert.Equal(
            @"\\?\UNC\server\share\target.dll",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\UNC\server\share\links\..\target.dll"));
        Assert.Equal(
            @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\target.dll",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\" +
                @"links\..\target.dll"));
        Assert.Equal(
            @"\\?\C:\target.dll",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\C:\..\..\target.dll"));
        Assert.Equal(
            @"\\?\C:\root\target.dll",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\C:\root\.\target.dll"));
        Assert.Equal(
            @"\\?\C:\root\",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\C:\root\links\..\"));
        Assert.Equal(
            @"\\?\C:\",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\C:\root\.."));
        Assert.Equal(
            @"\\?\UNC\server\share\target.dll",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"\\?\UNC\server\share\..\..\target.dll"));
        Assert.Equal(
            @"C:\root\target.dll",
            LocalPathAdmission.NormalizeRelativeResolvedWindowsLinkTarget(
                @"C:\root\target.dll"));
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
        Assert.True(
            LocalPathAdmission.TryGetWindowsSymbolicLinkRelativeFlag(
                0,
                out bool absoluteIsRelative));
        Assert.False(absoluteIsRelative);
        Assert.True(
            LocalPathAdmission.TryGetWindowsSymbolicLinkRelativeFlag(
                1,
                out bool relativeIsRelative));
        Assert.True(relativeIsRelative);
        Assert.False(
            LocalPathAdmission.TryGetWindowsSymbolicLinkRelativeFlag(
                2,
                out _));
        Assert.False(
            LocalPathAdmission.TryGetWindowsSymbolicLinkRelativeFlag(
                3,
                out _));
    }

    [Fact]
    public void LocalPathAdmission_WindowsReparsePayloadBoundsAreClosed()
    {
        const int PathBufferOffset = 20;
        byte[] substituteName =
            System.Text.Encoding.Unicode.GetBytes(@"\??\C:\x.dll");
        ushort substituteNameLength = (ushort)substituteName.Length;
        ushort validDataLength =
            (ushort)(PathBufferOffset - 8 + substituteNameLength);
        byte[] validPayload = new byte[8 + validDataLength];
        substituteName.CopyTo(validPayload, PathBufferOffset);

        Assert.True(
            LocalPathAdmission.TryReadWindowsReparseTarget(
                validPayload,
                PathBufferOffset,
                validDataLength,
                targetOffset: 0,
                substituteNameLength,
                printNameOffset: substituteNameLength,
                printNameLength: 0,
                out string target));
        Assert.Equal(@"\??\C:\x.dll", target);

        byte[] oddDeclaredPayload = new byte[validPayload.Length + 1];
        validPayload.CopyTo(oddDeclaredPayload, 0);
        Assert.False(
            LocalPathAdmission.TryReadWindowsReparseTarget(
                oddDeclaredPayload,
                PathBufferOffset,
                (ushort)(validDataLength + 1),
                targetOffset: 0,
                substituteNameLength,
                printNameOffset: substituteNameLength,
                printNameLength: 0,
                out _));
        Assert.False(
            LocalPathAdmission.TryReadWindowsReparseTarget(
                oddDeclaredPayload,
                PathBufferOffset,
                validDataLength,
                targetOffset: 0,
                substituteNameLength,
                printNameOffset: substituteNameLength,
                printNameLength: 0,
                out _));
        Assert.False(
            LocalPathAdmission.TryReadWindowsReparseTarget(
                validPayload,
                PathBufferOffset,
                validDataLength,
                targetOffset: 0,
                substituteNameLength,
                printNameOffset: (ushort)(substituteNameLength + 1),
                printNameLength: 0,
                out _));
        Assert.False(
            LocalPathAdmission.TryReadWindowsReparseTarget(
                validPayload,
                PathBufferOffset,
                validDataLength,
                targetOffset: 0,
                substituteNameLength,
                printNameOffset: substituteNameLength,
                printNameLength: 2,
                out _));
    }

    [Fact]
    public void LocalPathAdmission_WindowsAlternateDevicePrefixIsInvalid()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Alternate device-prefix coverage requires Windows.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string target = Path.Combine(root, "target.dll");
        string directory = Path.Combine(root, "directory");
        try
        {
            File.WriteAllBytes(target, [1]);
            Directory.CreateDirectory(directory);
            string driveRoot = Path.GetPathRoot(root)
                ?? throw new InvalidOperationException(
                    "The Windows temp path has no drive root.");
            string rootSuffix = root[driveRoot.Length..].Replace('\\', '/');
            foreach (string prefix in new[] { "//?/", @"\\?/" })
            {
                string requested =
                    $"{prefix}{driveRoot[0]}:/{rootSuffix}/" +
                    "directory/../target.dll";

                LocalPathClassification classification =
                    LocalPathAdmission.Classify(
                        requested,
                        cancellationToken);
                Assert.Equal(
                    LocalPathOutcome.Rejected,
                    classification.Outcome);
                Assert.Equal(
                    LocalPathReason.InvalidPath,
                    classification.Reason);
                Assert.Null(classification.CanonicalPath);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task
        LocalPathAdmission_WindowsExtendedRelativeLinkTargetIsNormalized()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Extended relative-link coverage requires Windows.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string links = Path.Combine(root, "links");
        string target = Path.Combine(root, "target.dll");
        string link = Path.Combine(links, "input.dll");
        try
        {
            Directory.CreateDirectory(links);
            await File.WriteAllBytesAsync(target, [1], cancellationToken);
            File.CreateSymbolicLink(link, @"..\target.dll");
            string extendedLink = @"\\?\" + link;
            await using LocalFileAdmission admission =
                LocalPathAdmission.AdmitRegularFile(
                    extendedLink,
                    cancellationToken);
            var acquired =
                Assert.IsType<ArtifactAcquisitionOutcome.Acquired>(
                    await AcquireAsync(extendedLink, cancellationToken));

            Assert.Equal(
                LocalPathOutcome.Classified,
                admission.Classification.Outcome);
            Assert.Equal(
                LocalPathKind.RegularFile,
                admission.Classification.Kind);
            Assert.Equal(
                extendedLink,
                admission.Classification.CanonicalPath);
            Assert.Equal(1, admission.Stream!.ReadByte());
            Assert.Equal(-1, admission.Stream.ReadByte());
            Assert.Single(acquired.Artifacts);
            await acquired.Lease.DisposeAsync();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task
        LocalPathAdmission_WindowsAbsoluteExtendedLinkTargetRetainsSyntaxPolicy()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Absolute extended-link coverage requires Windows.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string links = Path.Combine(root, "links");
        string target = Path.Combine(root, "target.dll");
        string link = Path.Combine(links, "input.dll");
        try
        {
            Directory.CreateDirectory(links);
            await File.WriteAllBytesAsync(target, [1], cancellationToken);
            string extendedTarget =
                @"\\?\" + Path.Combine(links, "..", "target.dll");
            File.CreateSymbolicLink(link, extendedTarget);

            LocalPathClassification classification =
                LocalPathAdmission.Classify(link, cancellationToken);
            Assert.Equal(
                LocalPathOutcome.Rejected,
                classification.Outcome);
            Assert.Equal(
                LocalPathReason.UnsupportedEntry,
                classification.Reason);

            var rejected =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await AcquireAsync(link, cancellationToken));
            Assert.Equal(
                "local.file.unsupported-entry",
                rejected.Diagnostic.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task LocalPathAdmission_WindowsAncestorLinkLoopIsRejected()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Ancestor link-loop coverage requires Windows.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        string child = Path.Combine(first, "child.dll");
        try
        {
            Directory.CreateSymbolicLink(first, second);
            Directory.CreateSymbolicLink(second, first);

            LocalPathClassification classification =
                LocalPathAdmission.Classify(child, cancellationToken);
            Assert.Equal(
                LocalPathOutcome.Rejected,
                classification.Outcome);
            Assert.Equal(
                LocalPathReason.UnsupportedEntry,
                classification.Reason);

            var rejected =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await AcquireAsync(child, cancellationToken));
            Assert.Equal(
                "local.file.unsupported-entry",
                rejected.Diagnostic.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocalPathAdmission_WindowsCaseDistinctLinkTargetIsNotCycle()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "Case-sensitive directory coverage requires Windows.");
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string target = Path.Combine(root, "evidence.dll");
        string link = Path.Combine(root, "Evidence.dll");
        try
        {
            EnableCaseSensitiveDirectory(root);
            File.WriteAllBytes(target, [1]);
            File.CreateSymbolicLink(link, "evidence.dll");

            LocalPathClassification classification =
                LocalPathAdmission.Classify(link, cancellationToken);
            Assert.Equal(
                LocalPathOutcome.Classified,
                classification.Outcome);
            Assert.Equal(
                LocalPathKind.RegularFile,
                classification.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocalPathAdmission_WindowsGetFileTypeFailureIsFailed()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows(),
            "GetFileType failure coverage requires Windows.");
        LocalPathClassification classified =
            LocalPathClassification.Classified(
                "requested",
                "canonical",
                LocalPathKind.RegularFile);
        using var invalidHandle = new SafeFileHandle(
            new IntPtr(-1),
            ownsHandle: false);

        LocalPathClassification result =
            LocalPathAdmission.VerifyWindowsRegularFileHandle(
                classified,
                invalidHandle);
        Assert.Equal(LocalPathOutcome.Failed, result.Outcome);
        Assert.Equal(LocalPathReason.AdmissionFailed, result.Reason);
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

    [Fact]
    public async Task
        LocalDirectoryAcquisition_BoundedDeterministicSelection()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string nestedDirectory = Path.Combine(root, "nested.dll");
        string linkedDirectory = Path.Combine(root, "linked-directory.dll");
        string linkedFile = Path.Combine(root, "linked-file.dll");
        string linkTarget = Path.Combine(root, "link-target.bin");
        string hardLink = Path.Combine(root, "hard-link.dll");
        bool linkedDirectoryCreated = false;
        bool linkedFileCreated = false;
        bool hardLinkCreated = false;
        try
        {
            Directory.CreateDirectory(nestedDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(nestedDirectory, "child.dll"),
                [9],
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(root, "B.dll"),
                [2],
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(root, "a.DLL"),
                [1],
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(root, "excluded.dll"),
                [3],
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(root, "ignored.txt"),
                [4],
                cancellationToken);
            await File.WriteAllBytesAsync(
                linkTarget,
                [5],
                cancellationToken);
            hardLinkCreated = TryCreateHardLink(
                hardLink,
                Path.Combine(root, "B.dll"));
            try
            {
                Directory.CreateSymbolicLink(
                    linkedDirectory,
                    nestedDirectory);
                linkedDirectoryCreated = true;
                File.CreateSymbolicLink(linkedFile, linkTarget);
                linkedFileCreated = true;
            }
            catch (Exception ex) when (OperatingSystem.IsWindows()
                && ex is IOException or UnauthorizedAccessException)
            {
            }

            var acquired =
                Assert.IsType<ArtifactAcquisitionOutcome.Acquired>(
                    await AcquireDirectoryAsync(
                        root,
                        new LocalDirectoryArtifactAcquisitionOptions
                        {
                            ExcludedFileNames = ["EXCLUDED.DLL"],
                            IncludedFileExtensions = [".DLL"],
                        },
                        cancellationToken));
            string[] expectedNames =
            [
                "B.dll",
                "a.DLL",
                .. hardLinkCreated
                    ? new[] { "hard-link.dll" }
                    : Array.Empty<string>(),
                .. linkedFileCreated
                    ? new[] { "linked-file.dll" }
                    : Array.Empty<string>(),
            ];
            Assert.Equal(
                expectedNames.Order(StringComparer.Ordinal),
                acquired.Artifacts.Select(
                    artifact =>
                        Assert.IsType<LocalDirectoryArtifactProvenance>(
                            artifact.Registration.Provenance)
                        .RelativeName));
            Assert.All(
                acquired.Artifacts,
                artifact =>
                {
                    Assert.Equal(
                        "local-directory-entry",
                        artifact.Descriptor.Kind);
                    Assert.Null(artifact.Descriptor.MediaType);
                });
            if (linkedDirectoryCreated)
            {
                Assert.DoesNotContain(
                    acquired.Artifacts,
                    artifact =>
                        Assert.IsType<LocalDirectoryArtifactProvenance>(
                            artifact.Registration.Provenance)
                        .RelativeName == "linked-directory.dll");
            }

            var observedLimit =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await AcquireDirectoryAsync(
                        root,
                        new LocalDirectoryArtifactAcquisitionOptions
                        {
                            MaxObservedEntries = 1,
                        },
                        cancellationToken));
            Assert.Equal(
                "local.directory.entry-limit",
                observedLimit.Diagnostic.Code);

            var selectedLimit =
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    await AcquireDirectoryAsync(
                        root,
                        new LocalDirectoryArtifactAcquisitionOptions
                        {
                            ExcludedFileNames =
                                linkedFileCreated
                                    ? ["linked-file.dll"]
                                    : Array.Empty<string>(),
                            MaxSelectedFiles = 1,
                        },
                        cancellationToken));
            Assert.Equal(
                "local.directory.selected-file-limit",
                selectedLimit.Diagnostic.Code);

            await Assert.ThrowsAsync<ArgumentException>(
                async () => await AcquireDirectoryAsync(
                    Path.Combine(root, "missing"),
                    new LocalDirectoryArtifactAcquisitionOptions
                    {
                        IncludedFileExtensions = ["*.dll"],
                    },
                    cancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await AcquireDirectoryAsync(
                    Path.Combine(root, "missing"),
                    new LocalDirectoryArtifactAcquisitionOptions
                    {
                        ExcludedFileNames = ["..", "input.dll"],
                    },
                    cancellationToken));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                async () => await AcquireDirectoryAsync(
                    Path.Combine(root, "missing"),
                    new LocalDirectoryArtifactAcquisitionOptions
                    {
                        MaxTotalBytes = (long)int.MaxValue + 1,
                    },
                    cancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task
        LocalDirectoryAcquisition_EmptyOrFailedBatchPublishesNothing()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string emptyRoot = TempDirectory();
        string fileLimitRoot = TempDirectory();
        string totalLimitRoot = TempDirectory();
        string missingEntryRoot = TempDirectory();
        string enumerationFailureRoot = TempDirectory();
        string unsupportedEntryRoot = TempDirectory();
        string admissionFailureRoot = TempDirectory();
        string readFailureRoot = TempDirectory();
        try
        {
            await File.WriteAllBytesAsync(
                Path.Combine(emptyRoot, "ignored.txt"),
                [1],
                cancellationToken);
            var empty =
                Assert.IsType<ArtifactAcquisitionOutcome.Acquired>(
                    await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                        emptyRoot,
                        options: null,
                        cancellationToken));
            Assert.Empty(empty.Artifacts);
            Assert.Same(
                ArtifactAcquisitionLeases.None,
                empty.Lease);

            ArtifactAcquisitionOutcome missingRoot =
                await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                    Path.Combine(emptyRoot, "missing"),
                    options: null,
                    cancellationToken);
            Assert.Equal(
                "local.directory.root-missing",
                Assert.IsType<ArtifactAcquisitionOutcome.Unavailable>(
                    missingRoot).Diagnostic.Code);

            ArtifactAcquisitionOutcome invalidRoot =
                await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                    "invalid\0path",
                    options: null,
                    cancellationToken);
            Assert.Equal(
                "local.directory.root-invalid-path",
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    invalidRoot).Diagnostic.Code);

            string fileAsRoot = Path.Combine(emptyRoot, "root.dll");
            await File.WriteAllBytesAsync(
                fileAsRoot,
                [1],
                cancellationToken);
            ArtifactAcquisitionOutcome unsupportedRoot =
                await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                    fileAsRoot,
                    options: null,
                    cancellationToken);
            Assert.Equal(
                "local.directory.root-unsupported",
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    unsupportedRoot).Diagnostic.Code);
            ArtifactAcquisitionOutcome rootAdmissionFailure =
                LocalArtifactSource.ProjectDirectoryRootOutcome(
                    LocalPathClassification.Failed(
                        LocalPathReason.AdmissionFailed,
                        emptyRoot,
                        Path.GetFullPath(emptyRoot)));
            Assert.Equal(
                "local.directory.root-admission-failed",
                Assert.IsType<ArtifactAcquisitionOutcome.Failed>(
                    rootAdmissionFailure).Diagnostic.Code);

            await File.WriteAllBytesAsync(
                Path.Combine(fileLimitRoot, "a.dll"),
                [1],
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(fileLimitRoot, "z.dll"),
                [2, 3],
                cancellationToken);
            ArtifactAcquisitionOutcome fileLimit =
                await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                    fileLimitRoot,
                    new LocalDirectoryArtifactAcquisitionOptions
                    {
                        MaxFileBytes = 1,
                        MaxTotalBytes = 2,
                    },
                    cancellationToken);
            Assert.Equal(
                "local.directory.file-size-limit",
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    fileLimit).Diagnostic.Code);

            await File.WriteAllBytesAsync(
                Path.Combine(totalLimitRoot, "a.dll"),
                [1, 2],
                cancellationToken);
            await File.WriteAllBytesAsync(
                Path.Combine(totalLimitRoot, "b.dll"),
                [3, 4],
                cancellationToken);
            ArtifactAcquisitionOutcome totalLimit =
                await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                    totalLimitRoot,
                    new LocalDirectoryArtifactAcquisitionOptions
                    {
                        MaxFileBytes = 2,
                        MaxTotalBytes = 3,
                    },
                    cancellationToken);
            Assert.Equal(
                "local.directory.total-size-limit",
                Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                    totalLimit).Diagnostic.Code);

            await File.WriteAllBytesAsync(
                Path.Combine(missingEntryRoot, "a.dll"),
                [1],
                cancellationToken);
            string danglingEntry =
                Path.Combine(missingEntryRoot, "z.dll");
            if (TryCreateFileLink(
                danglingEntry,
                Path.Combine(missingEntryRoot, "missing.dll")))
            {
                ArtifactAcquisitionOutcome missingEntry =
                    await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                        missingEntryRoot,
                        options: null,
                        cancellationToken);
                Assert.Equal(
                    "local.directory.entry-missing",
                    Assert.IsType<ArtifactAcquisitionOutcome.Unavailable>(
                        missingEntry).Diagnostic.Code);
            }

            if (!OperatingSystem.IsWindows())
            {
                string fifo =
                    Path.Combine(unsupportedEntryRoot, "input.dll");
                await CreateFifoAsync(fifo, cancellationToken);
                ArtifactAcquisitionOutcome unsupportedEntry =
                    await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                        unsupportedEntryRoot,
                        options: null,
                        cancellationToken);
                Assert.Equal(
                    "local.directory.entry-unsupported",
                    Assert.IsType<ArtifactAcquisitionOutcome.Rejected>(
                        unsupportedEntry).Diagnostic.Code);

                string inaccessibleFile =
                    Path.Combine(admissionFailureRoot, "input.dll");
                await File.WriteAllBytesAsync(
                    inaccessibleFile,
                    [1],
                    cancellationToken);
                UnixFileMode originalFileMode =
                    File.GetUnixFileMode(inaccessibleFile);
                try
                {
                    File.SetUnixFileMode(
                        inaccessibleFile,
                        UnixFileMode.None);
                    ArtifactAcquisitionOutcome admissionFailure =
                        await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                            admissionFailureRoot,
                            options: null,
                            cancellationToken);
                    Assert.Equal(
                        "local.directory.entry-admission-failed",
                        Assert.IsType<ArtifactAcquisitionOutcome.Failed>(
                            admissionFailure).Diagnostic.Code);
                }
                finally
                {
                    File.SetUnixFileMode(
                        inaccessibleFile,
                        originalFileMode);
                }

                if (OperatingSystem.IsLinux()
                    && File.Exists("/proc/self/mem"))
                {
                    File.CreateSymbolicLink(
                        Path.Combine(readFailureRoot, "input.dll"),
                        "/proc/self/mem");
                    ArtifactAcquisitionOutcome readFailure =
                        await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                            readFailureRoot,
                            options: null,
                            cancellationToken);
                    Assert.Equal(
                        "local.directory.read-failed",
                        Assert.IsType<ArtifactAcquisitionOutcome.Failed>(
                            readFailure).Diagnostic.Code);
                }

                await File.WriteAllBytesAsync(
                    Path.Combine(enumerationFailureRoot, "input.dll"),
                    [1],
                    cancellationToken);
                UnixFileMode originalMode =
                    File.GetUnixFileMode(enumerationFailureRoot);
                try
                {
                    File.SetUnixFileMode(
                        enumerationFailureRoot,
                        UnixFileMode.None);
                    ArtifactAcquisitionOutcome enumerationFailure =
                        await AcquireDirectoryAndCompleteEmptyGenerationAsync(
                            enumerationFailureRoot,
                            options: null,
                            cancellationToken);
                    Assert.Equal(
                        "local.directory.enumeration-failed",
                        Assert.IsType<ArtifactAcquisitionOutcome.Failed>(
                            enumerationFailure).Diagnostic.Code);
                }
                finally
                {
                    File.SetUnixFileMode(
                        enumerationFailureRoot,
                        originalMode);
                }
            }

            var owner = new ArtifactGenerationAuthority();
            ArtifactAdmissionAuthorization authorization =
                owner.CreateAdmissionAuthorization();
            ArtifactContributionScope disposedScope =
                owner.BeginContribution(authorization);
            disposedScope.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                async () => await LocalArtifactSource.AcquireDirectoryAsync(
                    disposedScope,
                    fileLimitRoot,
                    cancellationToken: cancellationToken));
            owner.CompleteAdmission(authorization);
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
            Directory.Delete(fileLimitRoot, recursive: true);
            Directory.Delete(totalLimitRoot, recursive: true);
            Directory.Delete(missingEntryRoot, recursive: true);
            Directory.Delete(enumerationFailureRoot, recursive: true);
            Directory.Delete(unsupportedEntryRoot, recursive: true);
            Directory.Delete(admissionFailureRoot, recursive: true);
            Directory.Delete(readFailureRoot, recursive: true);
        }
    }

    [Fact]
    public async Task
        LocalDirectoryAcquisition_ProvenanceSnapshotAndCancellationArePreserved()
    {
        CancellationToken cancellationToken =
            TestContext.Current.CancellationToken;
        string root = TempDirectory();
        string firstPath = Path.Combine(root, "first.dll");
        string secondPath = Path.Combine(root, "second.DLL");
        DateTime writeTimeSample = DateTime.UtcNow.AddMinutes(-5);
        DateTime firstWriteTime = new(
            writeTimeSample.Ticks
                - (writeTimeSample.Ticks % TimeSpan.TicksPerSecond),
            DateTimeKind.Utc);
        try
        {
            await File.WriteAllBytesAsync(
                firstPath,
                [1, 2, 3],
                cancellationToken);
            await File.WriteAllBytesAsync(
                secondPath,
                [4, 5],
                cancellationToken);
            File.SetLastWriteTimeUtc(firstPath, firstWriteTime);

            List<string> includedExtensions = [".dll"];
            List<string> excludedFileNames = [];
            var options = new LocalDirectoryArtifactAcquisitionOptions
            {
                IncludedFileExtensions =
                    new SingleEnumerationCollection(
                        includedExtensions),
                ExcludedFileNames =
                    new SingleEnumerationCollection(
                        excludedFileNames),
            };
            var owner = new ArtifactGenerationAuthority();
            ArtifactAdmissionAuthorization authorization =
                owner.CreateAdmissionAuthorization();
            using ArtifactContributionScope scope =
                owner.BeginContribution(authorization);
            ValueTask<ArtifactAcquisitionOutcome> pending =
                LocalArtifactSource.AcquireDirectoryAsync(
                    scope,
                    root,
                    options,
                    cancellationToken);
            includedExtensions.Clear();
            excludedFileNames.Add("first.dll");
            var acquired =
                Assert.IsType<ArtifactAcquisitionOutcome.Acquired>(
                    await pending);

            await File.WriteAllBytesAsync(
                firstPath,
                [9],
                cancellationToken);
            File.Delete(secondPath);

            using ArtifactAdmissionLease lease =
                owner.IssueLease(authorization);
            Assert.Equal(
                ["first.dll", "second.DLL"],
                acquired.Artifacts.Select(
                    artifact =>
                        Assert.IsType<LocalDirectoryArtifactProvenance>(
                            artifact.Registration.Provenance)
                        .RelativeName));
            Assert.Equal(
                [1, 2, 3],
                ReadAll(acquired.Artifacts[0].OpenRead(lease)));
            Assert.Equal(
                [4, 5],
                ReadAll(acquired.Artifacts[1].OpenRead(lease)));

            var provenance =
                Assert.IsType<LocalDirectoryArtifactProvenance>(
                    acquired.Artifacts[0].Registration.Provenance);
            Assert.Equal(Path.GetFullPath(root), provenance.CanonicalRoot);
            Assert.Equal(firstPath, provenance.FullPath);
            Assert.Equal(3, provenance.ContentLength);
            Assert.Equal(firstWriteTime, provenance.ObservedLastWriteTimeUtc);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(
                async () => await AcquireDirectoryAsync(
                    root,
                    options: null,
                    cancellation.Token));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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

    private static void EnableCaseSensitiveDirectory(string directory)
    {
        var startInfo = new ProcessStartInfo("fsutil.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("file");
        startInfo.ArgumentList.Add("SetCaseSensitiveInfo");
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add("enable");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Could not start fsutil.exe.");
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(
            process.ExitCode == 0,
            $"Could not enable case sensitivity for '{directory}'.\n" +
            $"stdout:\n{standardOutput}\nstderr:\n{standardError}");
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

    private static async ValueTask<ArtifactAcquisitionOutcome>
        AcquireDirectoryAsync(
            string path,
            LocalDirectoryArtifactAcquisitionOptions? options,
            CancellationToken cancellationToken)
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization authorization =
            owner.CreateAdmissionAuthorization();
        using ArtifactContributionScope scope =
            owner.BeginContribution(authorization);
        return await LocalArtifactSource.AcquireDirectoryAsync(
            scope,
            path,
            options,
            cancellationToken);
    }

    private static async ValueTask<ArtifactAcquisitionOutcome>
        AcquireDirectoryAndCompleteEmptyGenerationAsync(
            string path,
            LocalDirectoryArtifactAcquisitionOptions? options,
            CancellationToken cancellationToken)
    {
        var owner = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization authorization =
            owner.CreateAdmissionAuthorization();
        ArtifactAcquisitionOutcome outcome;
        using (ArtifactContributionScope scope =
            owner.BeginContribution(authorization))
        {
            outcome = await LocalArtifactSource.AcquireDirectoryAsync(
                scope,
                path,
                options,
                cancellationToken);
        }

        owner.CompleteAdmission(authorization);
        return outcome;
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

    private sealed class SingleEnumerationCollection(
        IReadOnlyCollection<string> values) :
        IReadOnlyCollection<string>
    {
        private int _enumerated;

        public int Count => values.Count;

        public IEnumerator<string> GetEnumerator()
        {
            Assert.Equal(
                1,
                Interlocked.Increment(ref _enumerated));
            return values.GetEnumerator();
        }

        System.Collections.IEnumerator
            System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
