using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using DotnetInspector.Fixtures;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.Presentation.Tests;

public sealed class LibraryApiDiffTypeSelectionTests
{
    static ApiSurfaceProjectionLimits GenerousLimits { get; } =
        new(64, 1_000_000, 1_000_000, int.MaxValue, int.MaxValue, int.MaxValue);

    public static TheoryData<string, string, ApiSurfaceScope> SelectionCases()
    {
        var cases = new TheoryData<string, string, ApiSurfaceScope>();
        foreach (string pair in (string[])["LibraryApiDiff", "Diff"])
        {
            foreach (string filter in (string[])
                [
                    "ProjectionReceiver",
                    "ProjectionExtensions",
                    "LibraryApiDiffFixture",
                    "HardChangedType",
                    "*Changed*",
                    "removedtype",
                    "DiffFixtureSample.MethodRemovalSample",
                    "DiffFixtureSample",
                    "Nonexistent.Type",
                ])
            {
                cases.Add(pair, filter, ApiSurfaceScope.Public);
                cases.Add(pair, filter, ApiSurfaceScope.IncludeAll);
            }
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(SelectionCases))]
    public async Task SelectedComparison_MatchesCompleteComparisonSubjects(
        string pair,
        string filter,
        ApiSurfaceScope scope)
    {
        (string before, string after) = Paths(pair);
        var selection = new ApiTypeSelection(
            name => TypeMatcher.MatchesTypeFilter(name, filter));

        LibraryApiDiffOutcome complete = LibraryApiDiffPresentationAdapter.Create(
            await Compare(before, after, scope, typeSelection: null));
        LibraryApiDiffOutcome selected = LibraryApiDiffPresentationAdapter.Create(
            await Compare(before, after, scope, selection));

        LibraryApiDiffOutcome.Available completeDocument =
            Assert.IsType<LibraryApiDiffOutcome.Available>(complete);
        LibraryApiDiffOutcome.Available selectedDocument =
            Assert.IsType<LibraryApiDiffOutcome.Available>(selected);
        Assert.Equal(
            completeDocument.Document.Comparison.Subjects
                .Where(subject => TypeMatcher.MatchesTypeFilter(subject.Display, filter)),
            selectedDocument.Document.Comparison.Subjects
                .Where(subject => TypeMatcher.MatchesTypeFilter(subject.Display, filter)));
    }

    // A selection by full-name predicate, as the CLI's -t filter makes, pairs
    // like an exact selection: with only the Types that declare an extension
    // for a selected Type.
    [Theory]
    [InlineData(
        "LibraryApiDiffFixture.ProjectionReceiver",
        new[] { "LibraryApiDiffFixture.ProjectionExtensions", "LibraryApiDiffFixture.ProjectionReceiver" })]
    [InlineData(
        "LibraryApiDiffFixture.HardChangedType",
        new[] { "LibraryApiDiffFixture.HardChangedType" })]
    public async Task SelectedComparison_ExtractsSelectedTypeAndItsExtensionDeclaringTypesOnly(
        string name,
        string[] expected)
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
            ApiSurfaceScope.Public,
            new ApiTypeSelection(candidate => candidate == name));

        foreach (AssemblyContextApiComparisonEndpoint endpoint in (AssemblyContextApiComparisonEndpoint[])
            [result.Before, result.After])
        {
            ApiSurface surface = Assert.IsType<ApiSurface>(endpoint.Surface);
            Assert.Equal(
                expected,
                surface.Types.Select(type => type.FullName).Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task SelectionOfExtensionDeclaringType_KeepsCompleteSurface()
    {
        string before = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string after = FixtureCatalog.LibraryApiDiffV2.AssemblyPath();
        AssemblyContextApiComparisonResult complete =
            await Compare(before, after, ApiSurfaceScope.Public, typeSelection: null);
        AssemblyContextApiComparisonResult selected = await Compare(
            before,
            after,
            ApiSurfaceScope.Public,
            new ApiTypeSelection(name => name == "LibraryApiDiffFixture.ProjectionExtensions"));

        Assert.Equal(
            complete.Before.Surface!.Types.Select(type => type.FullName),
            selected.Before.Surface!.Types.Select(type => type.FullName));
        Assert.Equal(
            complete.After.Surface!.Types.Select(type => type.FullName),
            selected.After.Surface!.Types.Select(type => type.FullName));
    }

    public static TheoryData<string, ApiSurfaceScope> ExactSelectionCases()
    {
        var cases = new TheoryData<string, ApiSurfaceScope>();
        foreach (string pair in (string[])["LibraryApiDiff", "Diff"])
        {
            cases.Add(pair, ApiSurfaceScope.Public);
            cases.Add(pair, ApiSurfaceScope.IncludeAll);
            cases.Add(pair, ApiSurfaceScope.ApiAxis);
        }
        return cases;
    }

    // An exact Type load gives every Type the subject the complete comparison
    // gives it, though it pairs with only the Types that declare an
    // extension for it.
    [Theory]
    [MemberData(nameof(ExactSelectionCases))]
    public async Task ExactSelection_MatchesCompleteComparisonForEveryType(
        string pair,
        ApiSurfaceScope scope)
    {
        (string before, string after) = Paths(pair);
        AssemblyContextApiComparisonResult completeResult =
            await Compare(before, after, scope, typeSelection: null);
        LibraryApiDiffOutcome.Available complete =
            Assert.IsType<LibraryApiDiffOutcome.Available>(
                LibraryApiDiffPresentationAdapter.Create(completeResult));
        string[] names =
        [
            .. completeResult.Before.Surface!.Types
                .Concat(completeResult.After.Surface!.Types)
                .Select(type => type.FullName)
                .Append("Nonexistent.Type")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        Assert.True(names.Length > 2);

        foreach (string name in names)
        {
            LibraryApiDiffOutcome.Available selected =
                Assert.IsType<LibraryApiDiffOutcome.Available>(
                    LibraryApiDiffPresentationAdapter.Create(await Compare(
                        before, after, scope, ApiTypeSelection.ForFullNames([name]))));
            Assert.Equal(
                complete.Document.Comparison.Subjects.Where(subject => subject.Display == name),
                selected.Document.Comparison.Subjects.Where(subject => subject.Display == name));
        }
    }

    [Theory]
    [InlineData("LibraryApiDiffFixture.ProjectionReceiver",
        new[] { "LibraryApiDiffFixture.ProjectionExtensions", "LibraryApiDiffFixture.ProjectionReceiver" })]
    [InlineData("LibraryApiDiffFixture.HardChangedType",
        new[] { "LibraryApiDiffFixture.HardChangedType" })]
    public async Task ExactSelection_ExtractsSelectedTypeAndItsExtensionDeclaringTypesOnly(
        string name,
        string[] expected)
    {
        AssemblyContextApiComparisonResult result = await Compare(
            FixtureCatalog.LibraryApiDiffV1.AssemblyPath(),
            FixtureCatalog.LibraryApiDiffV2.AssemblyPath(),
            ApiSurfaceScope.Public,
            ApiTypeSelection.ForFullNames([name]));

        foreach (AssemblyContextApiComparisonEndpoint endpoint in (AssemblyContextApiComparisonEndpoint[])
            [result.Before, result.After])
        {
            ApiSurface surface = Assert.IsType<ApiSurface>(endpoint.Surface);
            Assert.Equal(
                expected,
                surface.Types.Select(type => type.FullName).Order(StringComparer.Ordinal));
        }
    }

    // An extension-declaring Type's extensions pair with members of the Types
    // they extend, so selecting it exactly keeps both projections complete,
    // as any selection of it does.
    [Fact]
    public async Task ExactSelectionOfExtensionDeclaringType_KeepsCompleteSurface()
    {
        string before = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string after = FixtureCatalog.LibraryApiDiffV2.AssemblyPath();
        AssemblyContextApiComparisonResult complete =
            await Compare(before, after, ApiSurfaceScope.Public, typeSelection: null);
        AssemblyContextApiComparisonResult selected = await Compare(
            before,
            after,
            ApiSurfaceScope.Public,
            ApiTypeSelection.ForFullNames(["LibraryApiDiffFixture.ProjectionExtensions"]));

        Assert.Equal(
            complete.Before.Surface!.Types.Select(type => type.FullName),
            selected.Before.Surface!.Types.Select(type => type.FullName));
        Assert.Equal(
            complete.After.Surface!.Types.Select(type => type.FullName),
            selected.After.Surface!.Types.Select(type => type.FullName));
    }

    [Fact]
    public async Task UnselectedMalformedType_DoesNotMakeSelectedComparisonUnavailable()
    {
        // Completeness is operation-scoped: a failure in a Type the selection
        // does not extract no longer withholds the selected Types' comparison.
        byte[] before = ImageWithMalformedSignature("Before", addedMethod: false);
        byte[] after = ImageWithMalformedSignature("After", addedMethod: true);

        LibraryApiDiffOutcome complete = LibraryApiDiffPresentationAdapter.Create(
            await Compare(before, after, typeSelection: null));
        LibraryApiDiffOutcome selected = LibraryApiDiffPresentationAdapter.Create(
            await Compare(before, after, new ApiTypeSelection(name => name == "Ns.Good")));

        Assert.IsType<LibraryApiDiffOutcome.Unavailable>(complete);
        LibraryApiDiffOutcome.Available available =
            Assert.IsType<LibraryApiDiffOutcome.Available>(selected);
        Assert.Equal(
            "Ns.Good",
            Assert.Single(available.Document.Comparison.Subjects).Display);
    }

    static (string Before, string After) Paths(string pair)
        => pair == "LibraryApiDiff"
            ? (FixtureCatalog.LibraryApiDiffV1.AssemblyPath(), FixtureCatalog.LibraryApiDiffV2.AssemblyPath())
            : (FixtureCatalog.DiffV1.AssemblyPath(), FixtureCatalog.DiffV2.AssemblyPath());

    static async Task<AssemblyContextApiComparisonResult> Compare(
        string beforePath,
        string afterPath,
        ApiSurfaceScope scope,
        ApiTypeSelection? typeSelection)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([PathParticipant(beforePath, "Before", policy)]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([PathParticipant(afterPath, "After", policy)]);
        return AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            Assert.Single(beforeGroup.Participants),
            afterGroup,
            Assert.Single(afterGroup.Participants),
            scope,
            GenerousLimits,
            typeSelection);
    }

    static async Task<AssemblyContextApiComparisonResult> Compare(
        byte[] beforeBytes,
        byte[] afterBytes,
        ApiTypeSelection? typeSelection)
    {
        var policy = new TestBindingPolicy();
        await using var workspace = new InspectionWorkspace();
        AssemblyContextParticipant beforeParticipant =
            BytesParticipant(beforeBytes, "Before", policy);
        AssemblyContextParticipant afterParticipant =
            BytesParticipant(afterBytes, "After", policy);
        using AssemblyContextGroup beforeGroup =
            workspace.CreateAssemblyContextGroup([beforeParticipant]);
        using AssemblyContextGroup afterGroup =
            workspace.CreateAssemblyContextGroup([afterParticipant]);
        return AssemblyContextApiComparisonQuery.Execute(
            beforeGroup,
            beforeParticipant,
            afterGroup,
            afterParticipant,
            ApiSurfaceScope.Public,
            GenerousLimits,
            typeSelection);
    }

    static AssemblyContextParticipant PathParticipant(
        string path,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
        => new(
            ResolvedAssemblyReference.CreateFromPath(
                path,
                AssemblyResolutionProvenance.Local(provenanceLabel)),
            policy);

    static AssemblyContextParticipant BytesParticipant(
        byte[] bytes,
        string provenanceLabel,
        IAssemblyBindingPolicy policy)
    {
        using var reader = new PEReader(new MemoryStream(bytes, writable: false));
        AssemblyReferenceIdentity identity =
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader.GetMetadataReader());
        return new AssemblyContextParticipant(
            ResolvedAssemblyReference.Create(
                identity,
                path: null,
                () => new MemoryStream(bytes, writable: false),
                AssemblyResolutionProvenance.Local(provenanceLabel)),
            policy);
    }

    /// <summary>
    /// Builds an image with a well-formed <c>Ns.Good</c> and an <c>Ns.Bad</c>
    /// whose public method signature blob cannot be decoded.
    /// </summary>
    static byte[] ImageWithMalformedSignature(string label, bool addedMethod)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"Selection{label}.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Selection"),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            AssemblyHashAlgorithm.Sha1);
        var runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            metadata.GetOrAddBlob(
                new byte[] { 0xb0, 0x3f, 0x5f, 0x7f, 0x11, 0xd5, 0x0a, 0x3a }),
            default,
            default);
        var objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

        var voidSignature = new BlobBuilder();
        new BlobEncoder(voidSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(0, returnType => returnType.Void(), _ => { });
        BlobHandle goodSignature = metadata.GetOrAddBlob(voidSignature);
        BlobHandle badSignature = metadata.GetOrAddBlob(new byte[] { 0x20, 0x00, 0x7F });

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        MethodDefinitionHandle first = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Kept"),
            goodSignature,
            -1,
            default);
        if (addedMethod)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Abstract
                    | MethodAttributes.Virtual,
                MethodImplAttributes.IL,
                metadata.GetOrAddString("Added"),
                goodSignature,
                -1,
                default);
        }
        MethodDefinitionHandle bad = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Abstract
                | MethodAttributes.Virtual,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("Broken"),
            badSignature,
            -1,
            default);

        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Class,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Good"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            first);
        metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Class,
            metadata.GetOrAddString("Ns"),
            metadata.GetOrAddString("Bad"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            bad);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
                new PEHeaderBuilder(
                    imageCharacteristics: Characteristics.Dll | Characteristics.ExecutableImage),
                new MetadataRootBuilder(metadata),
                new BlobBuilder())
            .Serialize(image);
        return image.ToArray();
    }

    sealed class TestBindingPolicy : IAssemblyBindingPolicy
    {
        public AssemblyBindingPolicyVersion Version { get; } = new();

        public AssemblyBindingSelectionSnapshot Select(AssemblyBindingRequest request)
            => new(
                Version,
                AssemblyBindingSelection.CannotSelect(
                    new AssemblyBindingFailure(
                        AssemblyBindingFailureKind.CandidateUnavailable)));
    }
}
