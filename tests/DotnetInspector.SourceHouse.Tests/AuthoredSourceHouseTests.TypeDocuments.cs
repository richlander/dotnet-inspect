using ILInspector.SourceLink;

namespace DotnetInspector.SourceHouse.Tests;

public sealed partial class AuthoredSourceHouseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealPartialType_ExplicitDocumentPreservesMappingAndSelection(
        bool additional)
    {
        var asset = PartialTypeAsset();
        string selectedPath = additional
            ? asset.Additional.FilePath
            : asset.Mapping.SourceFilePath!;
        var target = new SourceHouseTarget.TypeTarget(
            asset.Target.Type, selectedPath);
        await using LibraryFixture library = await LibraryFixture.CreateAsync(
            asset.AssemblyPath, Path.ChangeExtension(asset.AssemblyPath, ".pdb"));
        var operation = library.IssueOperation();
        byte[]? selectedBytes = null;
        var available = Assert.IsType<SourceHouseOutcome.Available>(
            await SourceHouse.ExecuteAuthoredAsync(
                Request(library, target,
                [
                    Capability("selected-document", SourceHouseCapabilityCategory.Local,
                        (candidate, maximumBytes, _) =>
                        {
                            Assert.Equal(selectedPath, candidate.Document.OriginalPath);
                            selectedBytes = ReadCandidateSource(candidate);
                            Assert.True(selectedBytes.Length <= maximumBytes);
                            return ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                                new SourceHouseCapabilityOutcome.Available(selectedBytes));
                        }),
                ]),
                operation,
                TestContext.Current.CancellationToken));

        var mapping = Assert.IsType<SourceHouseAuthoredMapping.Type>(available.Source.Mapping);
        Assert.Equal(additional
            ? SourceHouseSourceUnitScope.AdditionalTypeDocument
            : SourceHouseSourceUnitScope.PrimaryTypeDocument, mapping.Scope);
        Assert.True(mapping.IsPartial);
        Assert.Equal(SourceHouseMappingStrength.CorrelatedTypeDocument, mapping.Strength);
        Assert.Equal(selectedPath, mapping.Document.OriginalPath);
        Assert.Equal(asset.Mapping.SourceFilePath, mapping.SourceMapping.SourceFilePath);
        Assert.Equal(asset.Mapping.SourceUrl, mapping.SourceMapping.SourceUrl);
        Assert.Equal(asset.Mapping.LineNumber, mapping.SourceMapping.LineNumber);
        Assert.Equal(asset.Mapping.Checksum, mapping.SourceMapping.Checksum);
        Assert.Equal(
            asset.Mapping.AdditionalSourceFiles.Select(file => file.FilePath),
            mapping.SourceMapping.AdditionalSourceFiles.Select(file => file.FilePath));
        Assert.Equal(
            asset.Mapping.AdditionalSourceFiles.Select(file => file.FilePath),
            mapping.AdditionalDocuments.Select(file => file.OriginalPath));
        Assert.Equal(selectedPath,
            Assert.IsType<SourceHouseTarget.TypeTarget>(available.Receipt.Request.Target)
                .OriginalDocumentPath);
        Assert.Equal(SourceChecksumVerification.Exact,
            available.Source.Selected.ChecksumVerification);
        Assert.NotNull(selectedBytes);
        Assert.Equal(SourceLinkService.DecodeSourceText(selectedBytes), available.Source.Text);
        AssertOperationSettled(operation, library.Reference.ApiAssembly);

        if (additional)
        {
            var expected = asset.Additional;
            Assert.Equal(expected.SourceUrl, mapping.Document.ResolvedUrl);
            Assert.Equal(expected.Checksum, Convert.FromHexString(mapping.Document.Checksum!));
        }
        else
        {
            var primary = Assert.IsType<SourceHouseOutcome.Available>(
                await ExecuteAsync(library, Request(library, asset.Target,
                [
                    Capability("primary-neighbor", SourceHouseCapabilityCategory.Local,
                        (candidate, _, _) =>
                            ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                                new SourceHouseCapabilityOutcome.Available(
                                    ReadCandidateSource(candidate)))),
                ])));
            Assert.Equal(primary.Source.Text, available.Source.Text);
            Assert.Null(asset.Target.OriginalDocumentPath);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unrelated")]
    [InlineData("different-case")]
    [InlineData("basename")]
    public async Task RealPartialType_NonMemberDocumentDoesNotFetchOrSubstitute(
        string selection)
    {
        var asset = PartialTypeAsset();
        string additional = asset.Additional.FilePath;
        string selectedPath = selection switch
        {
            "missing" => additional + ".missing",
            "unrelated" => asset.UnrelatedPath,
            "different-case" => additional.ToUpperInvariant(),
            "basename" => Path.GetFileName(additional),
            _ => throw new ArgumentOutOfRangeException(nameof(selection)),
        };
        Assert.NotEqual(asset.Mapping.SourceFilePath, selectedPath);
        Assert.DoesNotContain(asset.Mapping.AdditionalSourceFiles,
            file => file.FilePath == selectedPath);
        await using LibraryFixture library = await LibraryFixture.CreateAsync(
            asset.AssemblyPath, Path.ChangeExtension(asset.AssemblyPath, ".pdb"));
        var operation = library.IssueOperation();
        int reads = 0;

        var unavailable = Assert.IsType<SourceHouseOutcome.Unavailable>(
            await SourceHouse.ExecuteAuthoredAsync(
                Request(library, new SourceHouseTarget.TypeTarget(asset.Target.Type, selectedPath),
                [
                    Capability("must-not-fetch", SourceHouseCapabilityCategory.Local,
                        (_, _, _) =>
                        {
                            reads++;
                            throw new InvalidOperationException("Unmapped document was fetched.");
                        }),
                ]),
                operation,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, reads);
        Assert.Empty(unavailable.AuthoredAttempt.SourceAttempts);
        Assert.Null(unavailable.AuthoredAttempt.Mapping);
        Assert.Equal(selectedPath,
            Assert.IsType<SourceHouseTarget.TypeTarget>(unavailable.Receipt.Request.Target)
                .OriginalDocumentPath);
        AssertOperationSettled(operation, library.Reference.ApiAssembly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealPartialType_AdditionalDocumentRetainsChecksumOrBoundFailure(
        bool limitBytes)
    {
        var asset = PartialTypeAsset();
        string selectedPath = asset.Additional.FilePath;
        await using LibraryFixture library = await LibraryFixture.CreateAsync(
            asset.AssemblyPath, Path.ChangeExtension(asset.AssemblyPath, ".pdb"));
        var operation = library.IssueOperation();
        byte[] wrongDocument = File.ReadAllBytes(Path.Combine(
            RepositoryRoot(), "src", "ILInspector.SourceLink", "SourceLinkService.cs"));
        SourceHouseOutcome outcome = await SourceHouse.ExecuteAuthoredAsync(
            Request(library, new SourceHouseTarget.TypeTarget(asset.Target.Type, selectedPath),
            [
                Capability("wrong-document", SourceHouseCapabilityCategory.Local,
                    (_, _, _) => ValueTask.FromResult<SourceHouseCapabilityOutcome>(
                        new SourceHouseCapabilityOutcome.Available(wrongDocument))),
            ],
            limitBytes ? Limits(maximumSourceBytes: 1) : null),
            operation,
            TestContext.Current.CancellationToken);

        var mapping = Assert.IsType<SourceHouseAuthoredMapping.Type>(outcome.AuthoredAttempt.Mapping);
        Assert.Equal(selectedPath, mapping.Document.OriginalPath);
        Assert.Equal(SourceHouseSourceUnitScope.AdditionalTypeDocument, mapping.Scope);
        Assert.Equal(selectedPath,
            Assert.IsType<SourceHouseTarget.TypeTarget>(outcome.Receipt.Request.Target)
                .OriginalDocumentPath);
        var attempt = Assert.Single(outcome.AuthoredAttempt.SourceAttempts);
        if (limitBytes)
        {
            var incomplete = Assert.IsType<SourceHouseOutcome.Incomplete>(outcome);
            Assert.Equal(SourceHouseIncompleteBoundary.SourceBytes, incomplete.Boundary);
            Assert.Equal(SourceHouseSourceAttemptKind.Incomplete, attempt.Kind);
        }
        else
        {
            Assert.IsType<SourceHouseOutcome.Unavailable>(outcome);
            Assert.Equal(SourceChecksumVerification.Mismatch, attempt.ChecksumVerification);
            Assert.Equal(SourceHouseSourceAttemptKind.Rejected, attempt.Kind);
        }
        AssertOperationSettled(operation, library.Reference.ApiAssembly);
    }

    [Fact]
    public async Task RealPartialType_SelectedDocumentCannotReplaceTypeIdentity()
    {
        var asset = PartialTypeAsset();
        await using LibraryFixture library = await LibraryFixture.CreateAsync(
            asset.AssemblyPath, Path.ChangeExtension(asset.AssemblyPath, ".pdb"));
        var operation = library.IssueOperation();
        var rejected = Assert.IsType<SourceHouseOutcome.Rejected>(
            await SourceHouse.ExecuteAuthoredAsync(
                Request(library, new SourceHouseTarget.TypeTarget(
                    MemberSlicingAsset().MemberTarget.Type, asset.Additional.FilePath), []),
                operation,
                TestContext.Current.CancellationToken));

        Assert.Equal(SourceHouseRejectionKind.TargetMismatch, rejected.Rejection.Kind);
        Assert.Equal(asset.Additional.FilePath,
            Assert.IsType<SourceHouseTarget.TypeTarget>(rejected.Receipt.Request.Target)
                .OriginalDocumentPath);
        AssertOperationSettled(operation, library.Reference.ApiAssembly);
    }

    private static (
        string AssemblyPath,
        SourceHouseTarget.TypeTarget Target,
        SourceLinkResolver.TypeSourceInfo Mapping,
        SourceLinkResolver.PartialSourceFile Additional,
        string UnrelatedPath) PartialTypeAsset()
    {
        string assemblyPath = typeof(SourceLinkService).Assembly.Location;
        var target = TypeTarget(assemblyPath, typeof(SourceLinkService).FullName!);
        using var source = SourceLinkService.Open(assemblyPath);
        var mapping = Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
            source.ResolveTypeSource(target.Type));
        Assert.True(mapping.IsPartialType);
        var additional = Assert.Single(mapping.AdditionalSourceFiles,
            file => file.FilePath.EndsWith(
                "SourceLinkService.SourceContent.cs", StringComparison.Ordinal));
        string unrelatedPath = source.GetTrackedFiles()
            .Select(document => document.FilePath)
            .First(path => path != mapping.SourceFilePath
                && !mapping.AdditionalSourceFiles.Any(file => file.FilePath == path));
        return (assemblyPath, target, mapping, additional, unrelatedPath);
    }
}
