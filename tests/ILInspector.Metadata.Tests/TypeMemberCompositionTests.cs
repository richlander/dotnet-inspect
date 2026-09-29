using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

// A Type's Composition Count
// (docs/design/type-member-inspection-documents.md#composition-count) counts
// the same declarations, under the same admission rules, as the Rows each
// accessibility bucket returns.
public sealed class TypeMemberCompositionTests
{
    static readonly string PackageJsonPath =
        Pinned("packages", "System.Text.Json.10.0.0.dll");

    [Fact]
    public void JsonDocument_MatchesTheDesignComposition()
    {
        MetadataTypeMemberComposition composition = Compose(
            PackageJsonPath,
            Name("System.Text.Json", "JsonDocument"),
            MetadataMemberSpelling.CSharp,
            includeHidden: true,
            MetadataMethodAccessibilityFilter.Public);

        Assert.Equal(
            (16, 0, 44, 27),
            (composition.Public, composition.Protected, composition.Internal, composition.Private));
    }

    [Theory]
    [InlineData(MetadataMemberSpelling.CSharp, 8, 0, 1, 3)]
    [InlineData(MetadataMemberSpelling.Metadata, 6, 0, 1, 7)]
    public void ArrayEnumerator_CountsInItsSpellingUnit(
        MetadataMemberSpelling spelling,
        int @public,
        int @protected,
        int @internal,
        int @private)
    {
        MetadataTypeMemberComposition composition = Compose(
            PackageJsonPath,
            Name("System.Text.Json", "JsonElement", "ArrayEnumerator"),
            spelling,
            includeHidden: true,
            MetadataMethodAccessibilityFilter.Public);

        Assert.Equal(
            (@public, @protected, @internal, @private),
            (composition.Public, composition.Protected, composition.Internal, composition.Private));
    }

    // Receiver Counts cover the declarations the request's own accessibility
    // admits: JsonSerializer's public declarations include its extension
    // overloads.
    [Fact]
    public void ReceiverCounts_FollowTheRequestedAccessibility()
    {
        MetadataTypeDefinitionName serializer = Name("System.Text.Json", "JsonSerializer");
        MetadataTypeMemberComposition @public = Compose(
            PackageJsonPath,
            serializer,
            MetadataMemberSpelling.CSharp,
            includeHidden: false,
            MetadataMethodAccessibilityFilter.Public);
        MetadataTypeMemberComposition all = Compose(
            PackageJsonPath,
            serializer,
            MetadataMemberSpelling.CSharp,
            includeHidden: false,
            MetadataMethodAccessibilityFilter.All);

        Assert.Equal(@public.Public, @public.Static + @public.This + @public.Extension);
        Assert.True(@public.Extension > 0);
        Assert.Equal(
            all.Public + all.Protected + all.Internal + all.Private,
            all.Static + all.This + all.Extension);
    }

    [Fact]
    public void PrivateScopeMethod_CountsAsPrivateAndNotAsPublicReceiver()
    {
        byte[] image = BuildPrivateScopeMethodImage();
        MetadataTypeDefinitionName type =
            Name("Fixtures", "PrivateScopeType");

        MetadataTypeMemberComposition @public = Compose(
            image,
            type,
            MetadataMethodAccessibilityFilter.Public);
        MetadataTypeMemberComposition @private = Compose(
            image,
            type,
            MetadataMethodAccessibilityFilter.Private);

        Assert.Equal(
            (0, 0, 0, 1),
            (@public.Public, @public.Protected, @public.Internal, @public.Private));
        Assert.Equal(
            (0, 0, 0),
            (@public.Static, @public.This, @public.Extension));
        Assert.Equal(
            (1, 0, 0),
            (@private.Static, @private.This, @private.Extension));

        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        ApiType publicType = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: false).Types,
            candidate => candidate.DefinitionName == type);
        Assert.Empty(publicType.Members);
    }

    [Theory]
    [InlineData("packages", "System.Text.Json.10.0.0.dll")]
    [InlineData(null, "System.Private.CoreLib.dll")]
    public void Composition_AgreesWithExtractedRowsOnEveryType(string? folder, string file)
    {
        string path = folder is null ? Pinned(file) : Pinned(folder, file);
        ApiSurface all = Extract(path, includeAll: true);
        ApiSurface @public = Extract(path, includeAll: false);
        Dictionary<MetadataTypeDefinitionName, int> publicRows = @public.Types
            .Where(type => type.DefinitionName is not null)
            .GroupBy(type => type.DefinitionName!)
            .ToDictionary(group => group.Key, group => group.Sum(type => type.Members.Count));

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        int checkedTypes = 0;
        var mismatches = new List<string>();
        foreach (ApiType type in all.Types)
        {
            if (type.DefinitionName is not { } name)
                continue;

            var rows = new int[4];
            foreach (ApiMember member in type.Members)
                rows[BucketOf(member.Accessibility)]++;

            if (MetadataTypeMemberCompositionInspection.Read(
                    reader,
                    name,
                    MetadataMemberSpelling.CSharp,
                    includeHidden: true,
                    MetadataMethodAccessibilityFilter.All)
                is not MetadataTypeMemberCompositionOutcome.Counted counted)
            {
                continue;
            }
            MetadataTypeMemberComposition composition = counted.Composition;
            int[] counts = [composition.Public, composition.Protected, composition.Internal, composition.Private];
            if (!counts.SequenceEqual(rows))
                mismatches.Add($"{type.FullName}: rows {string.Join('/', rows)} counts {string.Join('/', counts)}");

            if (publicRows.TryGetValue(name, out int publicRowCount)
                && MetadataTypeMemberCompositionInspection.Read(
                    reader,
                    name,
                    MetadataMemberSpelling.CSharp,
                    includeHidden: false,
                    MetadataMethodAccessibilityFilter.Public)
                    is MetadataTypeMemberCompositionOutcome.Counted publicCounted
                && publicCounted.Composition.Public != publicRowCount)
            {
                mismatches.Add($"{type.FullName}: public rows {publicRowCount} count {publicCounted.Composition.Public}");
            }
            checkedTypes++;
        }

        Assert.Empty(mismatches);
        Assert.True(checkedTypes > 50, $"Only {checkedTypes} Types were compared.");
    }

    static int BucketOf(string? accessibility)
        => accessibility switch
        {
            null or "" or "public" => 0,
            string value when value.Contains("protected", StringComparison.Ordinal) => 1,
            string value when value.Contains("internal", StringComparison.Ordinal) => 2,
            _ => 3,
        };

    static MetadataTypeMemberComposition Compose(
        string path,
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling,
        bool includeHidden,
        MetadataMethodAccessibilityFilter accessibility)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return Assert.IsType<MetadataTypeMemberCompositionOutcome.Counted>(
                MetadataTypeMemberCompositionInspection.Read(
                    peReader.GetMetadataReader(),
                    type,
                    spelling,
                    includeHidden,
                    accessibility))
            .Composition;
    }

    static MetadataTypeMemberComposition Compose(
        byte[] image,
        MetadataTypeDefinitionName type,
        MetadataMethodAccessibilityFilter accessibility)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        return Assert.IsType<MetadataTypeMemberCompositionOutcome.Counted>(
                MetadataTypeMemberCompositionInspection.Read(
                    peReader.GetMetadataReader(),
                    type,
                    MetadataMemberSpelling.CSharp,
                    includeHidden: true,
                    accessibility))
            .Composition;
    }

    static MetadataTypeDefinitionName Name(string @namespace, params string[] segments)
        => Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(@namespace, [.. segments]))
            .Name;

    static ApiSurface Extract(string path, bool includeAll)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll);
    }

    static string Pinned(params string[] parts)
        => Path.Combine([AppContext.BaseDirectory, "PinnedArtifacts", .. parts]);

    static byte[] BuildPrivateScopeMethodImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("PrivateScope.dll"),
            metadata.GetOrAddGuid(
                new Guid("D31947D2-E090-44E8-969D-4A4F24AA4361")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PrivateScope"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.PrivateScope | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Scoped"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("PrivateScopeType"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            method);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }
}
