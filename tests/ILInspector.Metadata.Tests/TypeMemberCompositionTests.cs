using System.Collections.Immutable;
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

    [Fact]
    public void JsonDocument_AllPopulationReturnsEveryAccessibilityBucket()
    {
        using var session = AssemblyInspectionSession.Open(PackageJsonPath);
        MetadataTypeMemberPopulation population = Assert.IsType<
                MetadataTypeMemberPopulationOutcome.Available>(
                MetadataTypeMemberPopulationInspection.Inspect(
                    session,
                    new(
                        Name("System.Text.Json", "JsonDocument"),
                        MetadataMemberSpelling.CSharp,
                        includeHidden: false,
                        MetadataMethodAccessibilityFilter.All),
                    new(
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue)))
            .Population;

        Assert.Equal(
            population.Composition.Public
                + population.Composition.Protected
                + population.Composition.Internal
                + population.Composition.Private,
            population.Groups.Sum(group => group.Members.Length));
        Assert.Contains(
            population.Groups.SelectMany(group => group.Members),
            member => string.IsNullOrEmpty(member.Accessibility));
        Assert.Contains(
            population.Groups.SelectMany(group => group.Members),
            member => member.Accessibility == "private");
        AssertSelectorCountsMatchRows(population);
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

    [Theory]
    [InlineData(MetadataMemberSpelling.CSharp)]
    [InlineData(MetadataMemberSpelling.Metadata)]
    public void ArrayEnumerator_SelectorCountsRetainExplicitInterfaceDeclarations(
        MetadataMemberSpelling spelling)
    {
        using var session = AssemblyInspectionSession.Open(PackageJsonPath);
        MetadataTypeMemberPopulation population = Assert.IsType<
                MetadataTypeMemberPopulationOutcome.Available>(
                MetadataTypeMemberPopulationInspection.Inspect(
                    session,
                    new(
                        Name(
                            "System.Text.Json",
                            "JsonElement",
                            "ArrayEnumerator"),
                        spelling,
                        includeHidden: true,
                        MetadataMethodAccessibilityFilter.All),
                    new(
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue)))
            .Population;

        AssertSelectorCountsMatchRows(population);
        Assert.True(population.SelectorCounts.Traits.Interface > 0);
        Assert.Contains(
            population.Groups.SelectMany(group => group.Members),
            member => member.IsExplicitInterfaceImplementation);
    }

    [Theory]
    [InlineData(nameof(CovariantEmitDerived))]
    [InlineData(nameof(StaticAbstractEmitImpl))]
    [InlineData(nameof(ImplicitEmitImpl))]
    public void SelectorCounts_ExcludeNonExplicitMethodImplProperties(
        string typeName)
    {
        using var session = AssemblyInspectionSession.Open(
            typeof(TypeMemberCompositionTests).Assembly.Location);
        MetadataTypeMemberPopulation population = Assert.IsType<
                MetadataTypeMemberPopulationOutcome.Available>(
                MetadataTypeMemberPopulationInspection.Inspect(
                    session,
                    new(
                        Name("ILInspector.Metadata.Tests", typeName),
                        MetadataMemberSpelling.CSharp,
                        includeHidden: true,
                        MetadataMethodAccessibilityFilter.All),
                    new(
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue)))
            .Population;

        AssertSelectorCountsMatchRows(population);
        Assert.Equal(0, population.SelectorCounts.Traits.Interface);
        Assert.DoesNotContain(
            population.Groups.SelectMany(group => group.Members),
            member => member.IsExplicitInterfaceImplementation);
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
    public void JsonSerializer_ExtensionCountAgreesWithMethodGroupFilter()
    {
        MetadataTypeDefinitionName serializer =
            Name("System.Text.Json", "JsonSerializer");
        using var assembly = AssemblyInspectionSession.Open(PackageJsonPath);
        using var declaration = assembly.CreateDeclarationSession(
            new MetadataOperationContext(MetadataOperationPolicy.Unbounded));
        MetadataTypeMemberComposition composition = Assert.IsType<
                MetadataTypeMemberCompositionOutcome.Counted>(
                declaration.InspectTypeMemberComposition(
                    serializer,
                    MetadataMemberSpelling.CSharp,
                    includeHidden: false,
                    MetadataMethodAccessibilityFilter.Public))
            .Composition;

        using var stream = File.OpenRead(PackageJsonPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        MetadataTypeDefinitionIndex index =
            MetadataTypeDefinitionIndex.Create(reader);
        Assert.True(index.TryGetDefinitions(
            serializer,
            out ImmutableArray<TypeDefinitionHandle> definitions,
            out bool ambiguous));
        Assert.False(ambiguous);
        TypeDefinition type = reader.GetTypeDefinition(Assert.Single(definitions));
        string[] methodNames = type.GetMethods()
            .Select(handle => reader.GetString(
                reader.GetMethodDefinition(handle).Name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        int filteredCount = 0;
        foreach (string methodName in methodNames)
        {
            MetadataMethodGroupInspectionOutcome outcome =
                declaration.InspectMethodGroup(
                    serializer,
                    methodName,
                    startOrdinal: 0,
                    maximumRows: 1,
                    materializeRows: false,
                    MetadataMethodAccessibilityFilter.Public,
                    MetadataMethodReceiverFilter.Extension,
                    includeHidden: false,
                    maximumMembers: int.MaxValue,
                    maximumRetainedTextCharacters: int.MaxValue);
            if (outcome is MetadataMethodGroupInspectionOutcome.Read read)
                filteredCount = checked(filteredCount + read.Count);
            else
                Assert.IsType<
                    MetadataMethodGroupInspectionOutcome.MemberGroupNotFound>(
                    outcome);
        }

        Assert.Equal(composition.Extension, filteredCount);
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

        ApiMember allRow = Assert.Single(
            Extract(image, includeAll: true).Types
                .Single(candidate => candidate.DefinitionName == type)
                .Members);
        Assert.Equal("private", allRow.Accessibility);

        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        MetadataTypeMemberPopulation population = Assert.IsType<
                MetadataTypeMemberPopulationOutcome.Available>(
                MetadataTypeMemberPopulationInspection.Inspect(
                    session,
                    new(
                        type,
                        MetadataMemberSpelling.Metadata,
                        includeHidden: true,
                        MetadataMethodAccessibilityFilter.Private),
                    new(
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue)))
            .Population;
        ApiMember physicalRow =
            Assert.Single(Assert.Single(population.Groups).Members);
        Assert.Equal("private", physicalRow.Accessibility);
    }

    [Fact]
    public void PrivateScopePropertyAccessor_AgreesWithMetadataPopulation()
    {
        byte[] image = BuildPrivateScopePropertyImage();
        MetadataTypeDefinitionName type =
            Name("Fixtures", "PrivateScopePropertyType");

        ApiMember property = Assert.Single(
            Extract(image, includeAll: true).Types
                .Single(candidate => candidate.DefinitionName == type)
                .Members);
        Assert.Null(property.GetterAccessibility);
        Assert.Equal(
            MethodAttributes.PrivateScope,
            property.GetterPhysicalMethodAccess);

        using var session = AssemblyInspectionSession.OpenPrefetched(
            new MemoryStream(image, writable: false));
        MetadataTypeMemberPopulation population = Assert.IsType<
                MetadataTypeMemberPopulationOutcome.Available>(
                MetadataTypeMemberPopulationInspection.Inspect(
                    session,
                    new(
                        type,
                        MetadataMemberSpelling.Metadata,
                        includeHidden: true,
                        MetadataMethodAccessibilityFilter.Private),
                    new(
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue)))
            .Population;
        ApiMember[] rows =
        [
            .. population.Groups.SelectMany(group => group.Members),
        ];
        Assert.Equal(2, rows.Length);
        Assert.All(
            rows,
            static row => Assert.Equal("private", row.Accessibility));
        Assert.Contains(rows, row => row.Kind == "property");
        Assert.Contains(rows, row => row.Name == "get_Value");
        Assert.Equal(2, population.Composition.Private);
        Assert.Equal(0, population.Composition.Public);
    }

    [Fact]
    public void PrivateScopeAttachedExtension_CountsAsPrivateReceiver()
    {
        byte[] image = BuildPrivateScopeExtensionImage();
        MetadataTypeDefinitionName type =
            Name("Fixtures", "Receiver");

        MetadataTypeMemberComposition @public = Compose(
            image,
            type,
            MetadataMethodAccessibilityFilter.Public);
        MetadataTypeMemberComposition @protected = Compose(
            image,
            type,
            MetadataMethodAccessibilityFilter.Protected);
        MetadataTypeMemberComposition @private = Compose(
            image,
            type,
            MetadataMethodAccessibilityFilter.Private);

        Assert.Equal(
            (0, 0, 0, 1),
            (@public.Public, @public.Protected, @public.Internal, @public.Private));
        Assert.Equal(0, @public.Extension);
        Assert.Equal(0, @protected.Extension);
        Assert.Equal(1, @private.Extension);

        ApiMember attached = Assert.Single(
            Extract(image, includeAll: true).Types
                .Single(candidate => candidate.DefinitionName == type)
                .Members,
            member => member.IsExtension);
        Assert.Equal("private", attached.Accessibility);
    }

    // A malformed extension fails only the requests whose receiver it
    // reaches, whether a request classifies its own extensions or reads a
    // module's shared incidence.
    [Fact]
    public void MalformedAttachedExtension_FailsOnlyItsReceiver()
    {
        byte[] image = BuildMalformedAttachedExtensionImage();
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        MetadataTypeDefinitionName good = Name("Fixtures", "Good");
        MetadataTypeDefinitionName bad = Name("Fixtures", "Bad");
        var module = new MetadataTypeMemberCompositionModule(reader);

        foreach (MetadataTypeMemberCompositionModule? shared in new[] { null, module, module })
        {
            MetadataTypeMemberCompositionOutcome goodOutcome = shared is null
                ? MetadataTypeMemberCompositionInspection.Read(
                    reader, good, MetadataMemberSpelling.CSharp, includeHidden: true,
                    MetadataMethodAccessibilityFilter.All)
                : MetadataTypeMemberCompositionInspection.Read(
                    reader, shared, good, MetadataMemberSpelling.CSharp, includeHidden: true,
                    MetadataMethodAccessibilityFilter.All);
            Assert.Equal(
                1,
                Assert.IsType<MetadataTypeMemberCompositionOutcome.Counted>(goodOutcome)
                    .Composition.Extension);

            MetadataTypeMemberCompositionOutcome badOutcome = shared is null
                ? MetadataTypeMemberCompositionInspection.Read(
                    reader, bad, MetadataMemberSpelling.CSharp, includeHidden: true,
                    MetadataMethodAccessibilityFilter.All)
                : MetadataTypeMemberCompositionInspection.Read(
                    reader, shared, bad, MetadataMemberSpelling.CSharp, includeHidden: true,
                    MetadataMethodAccessibilityFilter.All);
            Assert.IsType<MetadataTypeMemberCompositionOutcome.Failed>(badOutcome);
        }
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
        // One module state for every Type, as a session holds: the first
        // request classifies its own extensions, later ones the incidence.
        var module = new MetadataTypeMemberCompositionModule(reader);
        int checkedTypes = 0;
        var mismatches = new List<string>();
        foreach (ApiType type in all.Types)
        {
            if (type.DefinitionName is not { } name)
                continue;

            var rows = new int[4];
            var receivers = new int[3];
            foreach (ApiMember member in type.Members)
            {
                rows[BucketOf(member.Accessibility)]++;
                receivers[ReceiverOf(member)]++;
            }

            if (MetadataTypeMemberCompositionInspection.Read(
                    reader,
                    module,
                    name,
                    MetadataMemberSpelling.CSharp,
                    includeHidden: true,
                    MetadataMethodAccessibilityFilter.All)
                is not MetadataTypeMemberCompositionOutcome.Counted counted)
            {
                mismatches.Add($"{type.FullName}: not Counted");
                continue;
            }
            MetadataTypeMemberComposition composition = counted.Composition;
            int[] counts = [composition.Public, composition.Protected, composition.Internal, composition.Private];
            if (!counts.SequenceEqual(rows))
                mismatches.Add($"{type.FullName}: rows {string.Join('/', rows)} counts {string.Join('/', counts)}");
            int[] receiverCounts = [composition.Static, composition.This, composition.Extension];
            if (!receiverCounts.SequenceEqual(receivers))
            {
                mismatches.Add(
                    $"{type.FullName}: row receivers {string.Join('/', receivers)} counts {string.Join('/', receiverCounts)}");
            }

            if (publicRows.TryGetValue(name, out int publicRowCount)
                && MetadataTypeMemberCompositionInspection.Read(
                    reader,
                    module,
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

    // Static, this, extension, as a row carries them.
    static int ReceiverOf(ApiMember member)
        => member.Kind == "extension-method" || member.IsExtension ? 2
            : member.IsStatic ? 0
            : 1;

    static int BucketOf(string? accessibility)
        => accessibility switch
        {
            null or "" or "public" => 0,
            string value when value.Contains("protected", StringComparison.Ordinal) => 1,
            string value when value.Contains("internal", StringComparison.Ordinal) => 2,
            _ => 3,
        };

    static void AssertSelectorCountsMatchRows(
        MetadataTypeMemberPopulation population)
    {
        ApiMember[] rows =
        [
            .. population.Groups.SelectMany(group => group.Members),
        ];
        Assert.Equal(rows.Length, population.SelectorCounts.Traits.All);
        Assert.Equal(
            rows.Count(member => member.IsStatic && !member.IsExtension),
            population.SelectorCounts.Traits.Static);
        Assert.Equal(
            rows.Count(member => !member.IsStatic && !member.IsExtension),
            population.SelectorCounts.Traits.Instance);
        Assert.Equal(
            rows.Count(member => member.IsVirtual),
            population.SelectorCounts.Traits.Virtual);
        Assert.Equal(
            rows.Count(member => member.IsExplicitInterfaceImplementation),
            population.SelectorCounts.Traits.Interface);
        Assert.Equal(
            rows.Count(member => member.IsExtension),
            population.SelectorCounts.Traits.Extensions);
        Assert.Equal(
            rows.GroupBy(member => member.Kind, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal),
            population.SelectorCounts.Kinds.ToDictionary(
                count => count.Value,
                count => count.Count,
                StringComparer.Ordinal));
    }

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

    static ApiSurface Extract(byte[] image, bool includeAll)
    {
        using var peReader = new PEReader(
            new MemoryStream(image, writable: false));
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
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

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
            objectType,
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

    static byte[] BuildPrivateScopePropertyImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("PrivateScopeProperty.dll"),
            metadata.GetOrAddGuid(
                new Guid("7AD0D27B-6862-4EAF-A4A8-22A48977BDCB")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PrivateScopeProperty"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);
        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));

        var getterSignature = new BlobBuilder();
        new BlobEncoder(getterSignature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Type().Int32(),
                parameters => { });
        MethodDefinitionHandle getter = metadata.AddMethodDefinition(
            MethodAttributes.PrivateScope
                | MethodAttributes.Static
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("get_Value"),
            metadata.GetOrAddBlob(getterSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("PrivateScopePropertyType"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            getter);
        var propertySignature = new BlobBuilder();
        new BlobEncoder(propertySignature)
            .PropertySignature(isInstanceProperty: false)
            .Parameters(
                0,
                returnType => returnType.Type().Int32(),
                parameters => { });
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildPrivateScopeExtensionImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("PrivateScopeExtension.dll"),
            metadata.GetOrAddGuid(
                new Guid("7C6CF356-B6AC-4449-A04B-8B4B76F624F8")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PrivateScopeExtension"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle extensionAttribute = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("ExtensionAttribute"));
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        var attributeConstructorSignature = new BlobBuilder();
        new BlobEncoder(attributeConstructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        MemberReferenceHandle extensionAttributeConstructor =
            metadata.AddMemberReference(
                extensionAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(attributeConstructorSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle receiver = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Receiver"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters
                    .AddParameter()
                    .Type()
                    .Type(receiver, isValueType: false));
        MethodDefinitionHandle method = metadata.AddMethodDefinition(
            MethodAttributes.PrivateScope | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Extend"),
            metadata.GetOrAddBlob(signature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        TypeDefinitionHandle extensions = metadata.AddTypeDefinition(
            TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Extensions"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            method);

        BlobHandle attributeValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        metadata.AddCustomAttribute(
            method,
            extensionAttributeConstructor,
            attributeValue);
        metadata.AddCustomAttribute(
            extensions,
            extensionAttributeConstructor,
            attributeValue);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        return image.ToArray();
    }

    // Fixtures.Extensions extends Good with a public method and Bad with one
    // whose access field holds the reserved value 7.
    static byte[] BuildMalformedAttachedExtensionImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("MalformedExtension.dll"),
            metadata.GetOrAddGuid(
                new Guid("2B0F1E7A-5C8D-4B61-9E3A-7D4C1F2A6B90")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("MalformedExtension"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            default);

        AssemblyReferenceHandle runtime = metadata.AddAssemblyReference(
            metadata.GetOrAddString("System.Runtime"),
            new Version(11, 0, 0, 0),
            default,
            default,
            default,
            default);
        TypeReferenceHandle extensionAttribute = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.Runtime.CompilerServices"),
            metadata.GetOrAddString("ExtensionAttribute"));
        TypeReferenceHandle objectType = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System"),
            metadata.GetOrAddString("Object"));
        var attributeConstructorSignature = new BlobBuilder();
        new BlobEncoder(attributeConstructorSignature)
            .MethodSignature(isInstanceMethod: true)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        MemberReferenceHandle extensionAttributeConstructor =
            metadata.AddMemberReference(
                extensionAttribute,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(attributeConstructorSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle good = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Good"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle bad = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Bad"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        MethodDefinitionHandle AddExtension(
            string name,
            MethodAttributes access,
            TypeDefinitionHandle receiver)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature)
                .MethodSignature(isInstanceMethod: false)
                .Parameters(
                    1,
                    returnType => returnType.Void(),
                    parameters => parameters
                        .AddParameter()
                        .Type()
                        .Type(receiver, isValueType: false));
            return metadata.AddMethodDefinition(
                access | MethodAttributes.Static,
                MethodImplAttributes.Runtime,
                metadata.GetOrAddString(name),
                metadata.GetOrAddBlob(signature),
                bodyOffset: 0,
                parameterList: MetadataTokens.ParameterHandle(1));
        }

        MethodDefinitionHandle extend = AddExtension("Extend", MethodAttributes.Public, good);
        MethodDefinitionHandle broken = AddExtension(
            "Broken",
            (MethodAttributes)7,
            bad);
        TypeDefinitionHandle extensions = metadata.AddTypeDefinition(
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Extensions"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            extend);

        BlobHandle attributeValue =
            metadata.GetOrAddBlob(new byte[] { 0x01, 0x00, 0x00, 0x00 });
        metadata.AddCustomAttribute(extend, extensionAttributeConstructor, attributeValue);
        metadata.AddCustomAttribute(broken, extensionAttributeConstructor, attributeValue);
        metadata.AddCustomAttribute(extensions, extensionAttributeConstructor, attributeValue);

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
