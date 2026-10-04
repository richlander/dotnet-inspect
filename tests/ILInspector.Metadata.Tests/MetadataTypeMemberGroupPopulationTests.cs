using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataTypeMemberGroupPopulationTests
{
    static readonly string PackageJsonPath =
        Pinned("packages", "System.Text.Json.10.0.0.dll");
    static readonly string RuntimeJsonPath =
        typeof(System.Text.Json.JsonSerializer).Assembly.Location;
    static readonly ApiSurfaceExtractionBounds Unbounded =
        new(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);

    [Fact]
    public void JsonSerializer_ProducesCompactGroupsAndExactCounts()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            Request(
                Name("System.Text.Json", "JsonSerializer"),
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.Public,
                count: true,
                rows: new(int.MaxValue),
                includeComposition: true,
                includeSelectorCounts: true),
            RuntimeJsonPath);

        Assert.Equal(11, population.Count);
        MetadataTypeMemberGroupRows rows = Required(population.Rows);
        Assert.Equal(11, rows.Items.Length);
        Assert.Null(rows.NextOrdinal);
        Assert.False(rows.ContinuationOutOfRange);
        Assert.Null(rows.IncompleteRetainedTextCharacters);
        Assert.Equal(
            107,
            rows.Items
                .Where(row =>
                    row.Category
                        is MetadataTypeMemberGroupCategory.Method)
                .Sum(row =>
                    Assert.IsType<int>(row.ExactMemberCount)));
        Assert.Equal(
            10,
            rows.Items.Count(row =>
                row.Category is MetadataTypeMemberGroupCategory.Method));
        Assert.Single(
            rows.Items,
            row => row.Category
                is MetadataTypeMemberGroupCategory.Property);
        Assert.Equal(
            108,
            Required(population.SelectorCounts).Traits.All);
        MetadataTypeMemberComposition composition =
            Required(population.Composition);
        Assert.Equal(
            108,
            composition.Public);
    }

    [Fact]
    public void CountOnly_DoesNotMaterializeRows()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            Request(
                Name("System.Text.Json", "JsonSerializer"),
                count: true),
            RuntimeJsonPath);

        Assert.Equal(11, population.Count);
        Assert.Null(population.Rows);
        Assert.Null(population.Composition);
        Assert.Null(population.SelectorCounts);
    }

    [Fact]
    public void Rows_AreStableAndBoundedByOrdinal()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MetadataTypeMemberGroupRows all = Required(
            Inspect(
                Request(type, rows: new(int.MaxValue)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows first = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    includeExactMemberCount: false)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows second = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    startOrdinal: Assert.IsType<int>(first.NextOrdinal),
                    includeExactMemberCount: false)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows outOfRange = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    startOrdinal: all.Items.Length)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows remaining = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: int.MaxValue,
                    startOrdinal: 1)),
                RuntimeJsonPath).Rows);

        Assert.Equal(
            all.Items.Take(3).Select(WithoutExactCount),
            first.Items);
        Assert.Equal(
            all.Items.Skip(3).Take(3).Select(WithoutExactCount),
            second.Items);
        Assert.All(
            first.Items.Concat(second.Items),
            row => Assert.Null(row.ExactMemberCount));
        Assert.True(outOfRange.ContinuationOutOfRange);
        Assert.Empty(outOfRange.Items);
        Assert.Null(outOfRange.NextOrdinal);
        Assert.Equal(all.Items.Skip(1), remaining.Items);
        Assert.Null(remaining.NextOrdinal);
    }

    [Fact]
    public void IndependentTerminalsPublishOnlyRequestedResults()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonDocument");
        MetadataTypeMemberGroupPopulation composition = Inspect(
            Request(type, includeComposition: true));
        MetadataTypeMemberGroupPopulation selectors = Inspect(
            Request(type, includeSelectorCounts: true));

        Assert.NotNull(composition.Composition);
        Assert.Null(composition.SelectorCounts);
        Assert.Null(composition.Count);
        Assert.Null(composition.Rows);

        Assert.Null(selectors.Composition);
        Assert.NotNull(selectors.SelectorCounts);
        Assert.Null(selectors.Count);
        Assert.Null(selectors.Rows);
    }

    [Fact]
    public void JsonDocument_CompositionAndNestedCountsAgree()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            Request(
                Name("System.Text.Json", "JsonDocument"),
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                count: true,
                rows: new(int.MaxValue),
                includeComposition: true,
                includeSelectorCounts: true));

        MetadataTypeMemberComposition composition =
            Required(population.Composition);
        Assert.Equal(
            (11, 0, 44, 27),
            (
                composition.Public,
                composition.Protected,
                composition.Internal,
                composition.Private));
        int exactCount = Required(population.Rows).Items.Sum(
            row => Assert.IsType<int>(row.ExactMemberCount));
        Assert.Equal(82, exactCount);
        Assert.Equal(
            exactCount,
            Required(population.SelectorCounts).Traits.All);
    }

    [Fact]
    public void JsonSerializer_ReceiverFiltersPreserveGroupIdentity()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MetadataTypeMemberGroupPopulation all = Inspect(
            Request(
                type,
                rows: new(int.MaxValue),
                includeComposition: true,
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation @static = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.Static,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation @this = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.This,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation extension = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.Extension,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation nonExtension = Inspect(
            Request(
                type,
                receiver:
                    MetadataTypeMemberGroupReceiverFilter.NonExtension,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);

        MetadataTypeMemberComposition composition =
            Required(all.Composition);
        Assert.Equal(
            composition.Static,
            Required(@static.SelectorCounts).Traits.All);
        Assert.Equal(
            composition.This,
            Required(@this.SelectorCounts).Traits.All);
        Assert.Equal(
            composition.Extension,
            Required(extension.SelectorCounts).Traits.All);
        Assert.Equal(
            checked(composition.Static + composition.This),
            Required(nonExtension.SelectorCounts).Traits.All);
        Assert.Equal(
            Required(all.SelectorCounts).Traits.All,
            checked(
                Required(@static.SelectorCounts).Traits.All
                + Required(@this.SelectorCounts).Traits.All
                + Required(extension.SelectorCounts).Traits.All));

        HashSet<GroupIdentity> allIdentities =
            [.. Required(all.Rows).Items.Select(Identity)];
        Assert.All(
            Required(@static.Rows).Items
                .Concat(Required(@this.Rows).Items)
                .Concat(Required(extension.Rows).Items)
                .Concat(Required(nonExtension.Rows).Items),
            row => Assert.Contains(Identity(row), allIdentities));

        MetadataTypeMemberGroupRow declaredDeserialize = Assert.Single(
            Required(@static.Rows).Items,
            row => row.Name == "Deserialize");
        Assert.Equal(25, declaredDeserialize.ExactMemberCount);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.Static,
            declaredDeserialize.Receivers);

        MetadataTypeMemberGroupRow extensionDeserialize = Assert.Single(
            Required(extension.Rows).Items,
            row => row.Name == "Deserialize");
        Assert.Equal(
            15,
            extensionDeserialize.ExactMemberCount);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.Extension,
            extensionDeserialize.Receivers);

        MetadataTypeMemberGroupRow allDeserialize = Assert.Single(
            Required(all.Rows).Items,
            row => row.Name == "Deserialize");
        Assert.Equal(40, allDeserialize.ExactMemberCount);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.Static
                | MetadataTypeMemberGroupReceiverForms.Extension,
            allDeserialize.Receivers);
    }

    [Fact]
    public void DeclarationPopulation_DoesNotAttachReceiverExtensions()
    {
        MetadataTypeDefinitionName type = Name("Ns`1", "Widget");
        MetadataTypeMemberGroupPopulation population = Inspect(
            ExtensionAttachmentNameBoundaryTests.BuildImage(),
            Request(
                type,
                rows: new(int.MaxValue),
                includeSelectorCounts: true));

        MetadataTypeMemberGroupRow declared = Assert.Single(
            Required(population.Rows).Items,
            row => row.Name == "Extend");
        Assert.Equal(
            MetadataTypeMemberGroupCategory.Method,
            declared.Category);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.This,
            declared.Receivers);
        Assert.Equal(1, declared.ExactMemberCount);
        Assert.DoesNotContain(
            Required(population.Rows).Items,
            row => row.Receivers
                is MetadataTypeMemberGroupReceiverForms.Extension);
    }

    [Fact]
    public void EqualNamesAtDistinctStringOffsetsShareOneGroup()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            BuildClassificationBoundaryImage(),
            Request(
                Name("Fixtures", "Target"),
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                rows: new(int.MaxValue)));

        MetadataTypeMemberGroupRow duplicate = Assert.Single(
            Required(population.Rows).Items,
            row => row.Name == "DuplicateNameA");
        Assert.Equal(MetadataTypeMemberGroupCategory.Method, duplicate.Category);
        Assert.Equal(2, duplicate.ExactMemberCount);
    }

    [Fact]
    public void OnlyPrivateMethodImplBodiesAreExplicitInterfaceDeclarations()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            BuildClassificationBoundaryImage(),
            Request(
                Name("Fixtures", "Target"),
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                rows: new(int.MaxValue),
                includeSelectorCounts: true));

        MetadataTypeMemberGroupRow publicOverride = Assert.Single(
            Required(population.Rows).Items,
            row => row.Name == "PublicOverride");
        Assert.Equal(
            MetadataTypeMemberGroupCategory.Method,
            publicOverride.Category);
        Assert.Equal(
            MetadataTypeMemberGroupCategory.ExplicitInterfaceImplementation,
            Assert.Single(
                Required(population.Rows).Items,
                row => row.Name == "Explicit").Category);
        Assert.Equal(
            MetadataTypeMemberGroupCategory.Property,
            Assert.Single(
                Required(population.Rows).Items,
                row => row.Name == "Value").Category);
        Assert.Equal(
            MetadataTypeMemberGroupCategory.Event,
            Assert.Single(
                Required(population.Rows).Items,
                row => row.Name == "Changed").Category);
        Assert.Equal(
            MetadataTypeMemberGroupCategory.Property,
            Assert.Single(
                Required(population.Rows).Items,
                row => row.Name == "ExplicitValue").Category);
        Assert.Equal(
            MetadataTypeMemberGroupCategory.Event,
            Assert.Single(
                Required(population.Rows).Items,
                row => row.Name == "ExplicitChanged").Category);
        Assert.Equal(4, Required(population.SelectorCounts).Traits.Interface);
    }

    [Fact]
    public void HiddenPublicMethodImplAccessorOwnersRequireIncludeHidden()
    {
        MetadataTypeMemberGroupPopulation visible = Inspect(
            BuildClassificationBoundaryImage(),
            Request(
                Name("Fixtures", "Target"),
                includeHidden: false,
                accessibility: MetadataMethodAccessibilityFilter.All,
                rows: new(int.MaxValue)));
        MetadataTypeMemberGroupPopulation all = Inspect(
            BuildClassificationBoundaryImage(),
            Request(
                Name("Fixtures", "Target"),
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                rows: new(int.MaxValue)));

        Assert.DoesNotContain(
            Required(visible.Rows).Items,
            row => row.Name is
                "PublicOverride" or
                "Value" or
                "Changed" or
                "Mixed");
        Assert.Equal(
            MetadataTypeMemberGroupCategory.ExplicitInterfaceImplementation,
            Assert.Single(
                Required(visible.Rows).Items,
                row => row.Name == "Explicit").Category);
        Assert.Contains(
            Required(visible.Rows).Items,
            row => row.Name == "ExplicitValue");
        Assert.Contains(
            Required(visible.Rows).Items,
            row => row.Name == "ExplicitChanged");
        Assert.All(
            new[]
            {
                "PublicOverride",
                "Value",
                "Changed",
                "Mixed",
                "Explicit",
                "ExplicitValue",
                "ExplicitChanged",
            },
            name => Assert.Contains(
                Required(all.Rows).Items,
                row => row.Name == name));
    }

    [Theory]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonSerializer" },
        MetadataMemberSpelling.CSharp)]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonDocument" },
        MetadataMemberSpelling.CSharp)]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonElement", "ArrayEnumerator" },
        MetadataMemberSpelling.CSharp)]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonElement", "ArrayEnumerator" },
        MetadataMemberSpelling.Metadata)]
    public void SelectorCounts_MatchTheEagerReference(
        string @namespace,
        string[] segments,
        MetadataMemberSpelling spelling)
    {
        MetadataTypeDefinitionName type = Name(@namespace, segments);
        string path = segments is ["JsonSerializer"]
            ? RuntimeJsonPath
            : PackageJsonPath;
        MetadataTypeMemberGroupPopulation compact = Inspect(
            Request(
                type,
                spelling,
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                includeSelectorCounts: true),
            path);

        MetadataTypeMemberSelectorCounts eager =
            DeclaredSelectorCounts(type, spelling, path);

        Assert.Equal(
            eager.Traits,
            Required(compact.SelectorCounts).Traits);
        Assert.Equal(
            eager.Kinds.ToDictionary(
                count => count.Value,
                count => count.Count,
                StringComparer.Ordinal),
            Required(compact.SelectorCounts).Kinds.ToDictionary(
                count => count.Value,
                count => count.Count,
                StringComparer.Ordinal));
    }

    [Fact]
    public void BoundsRemainVisible()
    {
        MetadataTypeMemberGroupPopulationRequest count = Request(
            Name("System.Text.Json", "JsonSerializer"),
            count: true);
        var memberBound = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            maxMembers: 1,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);

        var incomplete = Assert.IsType<
            MetadataTypeMemberGroupPopulationOutcome.Incomplete>(
            InspectOutcome(count, memberBound, RuntimeJsonPath));
        Assert.Equal(
            MetadataTypeMemberGroupPopulationBound.Members,
            incomplete.Bound);
        Assert.Equal(1, incomplete.Limit);
        Assert.Equal(2, incomplete.Measured);

        MetadataTypeMemberGroupPopulation textBound = Assert.IsType<
                MetadataTypeMemberGroupPopulationOutcome.Available>(
                InspectOutcome(
                    Request(
                        Name("System.Text.Json", "JsonSerializer"),
                        rows: new(1)),
                    new(
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        maxRetainedTextCharacters: 0),
                    RuntimeJsonPath))
            .Population;
        MetadataTypeMemberGroupRows rows =
            Required(textBound.Rows);
        Assert.Empty(rows.Items);
        Assert.Null(rows.NextOrdinal);
        Assert.True(rows.IncompleteRetainedTextCharacters > 0);

        var zeroMetadataRows = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            maxMetadataRows: 0,
            int.MaxValue);
        var metadataIncomplete = Assert.IsType<
            MetadataTypeMemberGroupPopulationOutcome.Incomplete>(
            InspectOutcome(count, zeroMetadataRows, RuntimeJsonPath));
        Assert.Equal(
            MetadataTypeMemberGroupPopulationBound.MetadataRows,
            metadataIncomplete.Bound);
        Assert.Equal(0, metadataIncomplete.Limit);
        Assert.True(metadataIncomplete.Measured > 0);

        var exactMetadataRows = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            checked((int)metadataIncomplete.Measured),
            int.MaxValue);
        Assert.IsType<MetadataTypeMemberGroupPopulationOutcome.Available>(
            InspectOutcome(count, exactMetadataRows, RuntimeJsonPath));

        var oneBelowMetadataRows = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            checked((int)metadataIncomplete.Measured - 1),
            int.MaxValue);
        var oneBelow = Assert.IsType<
            MetadataTypeMemberGroupPopulationOutcome.Incomplete>(
            InspectOutcome(count, oneBelowMetadataRows, RuntimeJsonPath));
        Assert.Equal(
            MetadataTypeMemberGroupPopulationBound.MetadataRows,
            oneBelow.Bound);
        Assert.Equal(metadataIncomplete.Measured, oneBelow.Measured);
    }

    static MetadataTypeMemberGroupPopulationRequest Request(
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling =
            MetadataMemberSpelling.CSharp,
        bool includeHidden = false,
        MetadataMethodAccessibilityFilter accessibility =
            MetadataMethodAccessibilityFilter.Public,
        MetadataTypeMemberGroupReceiverFilter receiver =
            MetadataTypeMemberGroupReceiverFilter.All,
        bool count = false,
        MetadataTypeMemberGroupRowsRequest? rows = null,
        bool includeComposition = false,
        bool includeSelectorCounts = false) =>
        new(
            type,
            spelling,
            includeHidden,
            accessibility,
            receiver,
            count ? new() : null,
            rows,
            includeComposition,
            includeSelectorCounts);

    static MetadataTypeMemberGroupPopulation Inspect(
        MetadataTypeMemberGroupPopulationRequest request) =>
        Inspect(request, PackageJsonPath);

    static MetadataTypeMemberGroupPopulation Inspect(
        MetadataTypeMemberGroupPopulationRequest request,
        string path) =>
        Assert.IsType<
                MetadataTypeMemberGroupPopulationOutcome.Available>(
                InspectOutcome(request, Unbounded, path))
            .Population;

    static MetadataTypeMemberGroupPopulation Inspect(
        byte[] image,
        MetadataTypeMemberGroupPopulationRequest request)
    {
        using var assembly =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(image, writable: false));
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(
                new MetadataOperationContext(
                    MetadataOperationPolicy.Unbounded));
        return Assert.IsType<
                MetadataTypeMemberGroupPopulationOutcome.Available>(
                declaration.InspectTypeMemberGroups(
                    request,
                    Unbounded))
            .Population;
    }

    static MetadataTypeMemberGroupPopulationOutcome InspectOutcome(
        MetadataTypeMemberGroupPopulationRequest request,
        ApiSurfaceExtractionBounds bounds) =>
        InspectOutcome(request, bounds, PackageJsonPath);

    static MetadataTypeMemberGroupPopulationOutcome InspectOutcome(
        MetadataTypeMemberGroupPopulationRequest request,
        ApiSurfaceExtractionBounds bounds,
        string path)
    {
        using var assembly =
            AssemblyInspectionSession.Open(path);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(
                new MetadataOperationContext(
                    MetadataOperationPolicy.Unbounded));
        return declaration.InspectTypeMemberGroups(request, bounds);
    }

    static MetadataTypeMemberSelectorCounts DeclaredSelectorCounts(
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling,
        string path)
    {
        using var session = AssemblyInspectionSession.Open(path);
        ApiType subject = Assert.Single(
            session
                .ApiSurface(
                    ApiSurfaceExtractionScope.IncludeAll)
                .Types,
            candidate => candidate.DefinitionName == type);
        IReadOnlyList<ApiMember> members =
            ApiTypeMemberPopulationProjection.Project(
                subject,
                spelling,
                includeHidden: true);
        var kinds = members
            .GroupBy(member => member.Kind, StringComparer.Ordinal)
            .Select(group =>
                new MetadataTypeMemberFacetCount(
                    group.Key,
                    group.Count()))
            .ToImmutableArray();
        int extensions = members.Count(member => member.IsExtension);
        int @static = members.Count(member =>
            !member.IsExtension && member.IsStatic);
        int instance = members.Count(member =>
            !member.IsExtension && !member.IsStatic);
        return new(
            kinds,
            new(
                members.Count,
                @static,
                instance,
                members.Count(member => member.IsVirtual),
                members.Count(member =>
                    member.IsExplicitInterfaceImplementation),
                extensions));
    }

    static byte[] BuildClassificationBoundaryImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ClassificationBoundary.dll"),
            metadata.GetOrAddGuid(
                new Guid("39360AD3-A5C8-4546-83A4-07ED4884D167")),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ClassificationBoundary"),
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
        TypeReferenceHandle contract = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("IContract"));
        TypeReferenceHandle editorBrowsable = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableAttribute"));
        TypeReferenceHandle editorBrowsableState = metadata.AddTypeReference(
            runtime,
            metadata.GetOrAddString("System.ComponentModel"),
            metadata.GetOrAddString("EditorBrowsableState"));
        var editorBrowsableConstructorSignature = new BlobBuilder();
        new BlobEncoder(editorBrowsableConstructorSignature).MethodSignature(
            SignatureCallingConvention.Default,
            genericParameterCount: 0,
            isInstanceMethod: true).Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(
                    editorBrowsableState,
                    isValueType: true));
        MemberReferenceHandle editorBrowsableConstructor =
            metadata.AddMemberReference(
                editorBrowsable,
                metadata.GetOrAddString(".ctor"),
                metadata.GetOrAddBlob(editorBrowsableConstructorSignature));

        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle target = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("Target"),
            objectType,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        BlobHandle staticVoid = metadata.GetOrAddBlob(
            (byte[])[0x00, 0x00, 0x01]);
        BlobHandle instanceVoid = metadata.GetOrAddBlob(
            (byte[])[0x20, 0x00, 0x01]);
        BlobHandle instanceInt = metadata.GetOrAddBlob(
            (byte[])[0x20, 0x00, 0x08]);
        BlobHandle instanceVoidObject = metadata.GetOrAddBlob(
            (byte[])[0x20, 0x01, 0x01, 0x1c]);
        BlobHandle instanceVoidInt = metadata.GetOrAddBlob(
            (byte[])[0x20, 0x01, 0x01, 0x08]);

        AddMethod(
            metadata,
            "DuplicateNameA",
            MethodAttributes.Public | MethodAttributes.Static,
            staticVoid);
        AddMethod(
            metadata,
            "DuplicateNameB",
            MethodAttributes.Public | MethodAttributes.Static,
            staticVoid);
        MethodDefinitionHandle publicOverride = AddMethod(
            metadata,
            "PublicOverride",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.HideBySig,
            instanceVoid);
        MethodDefinitionHandle explicitImplementation = AddMethod(
            metadata,
            "Explicit",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.HideBySig,
            instanceVoid);
        MethodDefinitionHandle getter = AddMethod(
            metadata,
            "get_Value",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            instanceInt);
        MethodDefinitionHandle adder = AddMethod(
            metadata,
            "add_Changed",
            MethodAttributes.Public
                | MethodAttributes.Virtual
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            instanceVoidObject);
        MethodDefinitionHandle mixedGetter = AddMethod(
            metadata,
            "get_Mixed",
            MethodAttributes.Public
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            instanceInt);
        MethodDefinitionHandle mixedSetter = AddMethod(
            metadata,
            "set_Mixed",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            instanceVoidInt);
        MethodDefinitionHandle explicitGetter = AddMethod(
            metadata,
            "get_ExplicitValue",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            instanceInt);
        MethodDefinitionHandle explicitAdder = AddMethod(
            metadata,
            "add_ExplicitChanged",
            MethodAttributes.Private
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.SpecialName
                | MethodAttributes.HideBySig,
            instanceVoidObject);

        metadata.AddMethodImplementation(
            target,
            publicOverride,
            metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("PublicOverride"),
                instanceVoid));
        metadata.AddMethodImplementation(
            target,
            explicitImplementation,
            metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("Explicit"),
                instanceVoid));
        metadata.AddMethodImplementation(
            target,
            getter,
            metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("get_Value"),
                instanceInt));
        metadata.AddMethodImplementation(
            target,
            adder,
            metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("add_Changed"),
                instanceVoidObject));
        metadata.AddMethodImplementation(
            target,
            mixedSetter,
            metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("set_Mixed"),
                instanceVoidInt));
        metadata.AddMethodImplementation(
            target,
            explicitGetter,
            metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("get_ExplicitValue"),
                instanceInt));
        metadata.AddMethodImplementation(
            target,
            explicitAdder,
            metadata.AddMemberReference(
                contract,
                metadata.GetOrAddString("add_ExplicitChanged"),
                instanceVoidObject));

        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob((byte[])[0x28, 0x00, 0x08]));
        metadata.AddPropertyMap(target, property);
        metadata.AddMethodSemantics(
            property,
            MethodSemanticsAttributes.Getter,
            getter);
        PropertyDefinitionHandle mixedProperty = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Mixed"),
            metadata.GetOrAddBlob((byte[])[0x28, 0x00, 0x08]));
        metadata.AddMethodSemantics(
            mixedProperty,
            MethodSemanticsAttributes.Getter,
            mixedGetter);
        metadata.AddMethodSemantics(
            mixedProperty,
            MethodSemanticsAttributes.Setter,
            mixedSetter);
        PropertyDefinitionHandle explicitProperty = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("ExplicitValue"),
            metadata.GetOrAddBlob((byte[])[0x28, 0x00, 0x08]));
        metadata.AddMethodSemantics(
            explicitProperty,
            MethodSemanticsAttributes.Getter,
            explicitGetter);
        EventDefinitionHandle @event = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("Changed"),
            objectType);
        metadata.AddEventMap(target, @event);
        metadata.AddMethodSemantics(
            @event,
            MethodSemanticsAttributes.Adder,
            adder);
        EventDefinitionHandle explicitEvent = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("ExplicitChanged"),
            objectType);
        metadata.AddMethodSemantics(
            explicitEvent,
            MethodSemanticsAttributes.Adder,
            explicitAdder);
        AddEditorBrowsableNever(
            metadata,
            publicOverride,
            editorBrowsableConstructor);
        AddEditorBrowsableNever(
            metadata,
            explicitImplementation,
            editorBrowsableConstructor);
        AddEditorBrowsableNever(
            metadata,
            property,
            editorBrowsableConstructor);
        AddEditorBrowsableNever(
            metadata,
            mixedProperty,
            editorBrowsableConstructor);
        AddEditorBrowsableNever(
            metadata,
            explicitProperty,
            editorBrowsableConstructor);
        AddEditorBrowsableNever(
            metadata,
            @event,
            editorBrowsableConstructor);
        AddEditorBrowsableNever(
            metadata,
            explicitEvent,
            editorBrowsableConstructor);

        var image = new BlobBuilder();
        new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(
                metadata,
                suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly)
            .Serialize(image);
        byte[] bytes = image.ToArray();
        ReplaceAsciiOnce(
            bytes,
            "DuplicateNameB"u8,
            "DuplicateNameA"u8);
        return bytes;
    }

    static MethodDefinitionHandle AddMethod(
        MetadataBuilder metadata,
        string name,
        MethodAttributes attributes,
        BlobHandle signature) =>
        metadata.AddMethodDefinition(
            attributes,
            MethodImplAttributes.IL,
            metadata.GetOrAddString(name),
            signature,
            bodyOffset: -1,
            MetadataTokens.ParameterHandle(1));

    static void AddEditorBrowsableNever(
        MetadataBuilder metadata,
        EntityHandle parent,
        EntityHandle constructor)
    {
        var value = new BlobBuilder();
        value.WriteUInt16(1);
        value.WriteInt32(1);
        value.WriteUInt16(0);
        metadata.AddCustomAttribute(
            parent,
            constructor,
            metadata.GetOrAddBlob(value));
    }

    static void ReplaceAsciiOnce(
        byte[] image,
        ReadOnlySpan<byte> from,
        ReadOnlySpan<byte> to)
    {
        Assert.Equal(from.Length, to.Length);
        int offset = image.AsSpan().IndexOf(from);
        Assert.True(offset >= 0);
        Assert.Equal(
            -1,
            image.AsSpan(offset + from.Length).IndexOf(from));
        to.CopyTo(image.AsSpan(offset, to.Length));
    }

    static GroupIdentity Identity(MetadataTypeMemberGroupRow row) =>
        new(row.Name, row.Category);

    static MetadataTypeMemberGroupRow WithoutExactCount(
        MetadataTypeMemberGroupRow row) =>
        row with { ExactMemberCount = null };

    static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    static T Required<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    static string Pinned(params string[] parts) =>
        Path.Combine(
            [AppContext.BaseDirectory, "PinnedArtifacts", .. parts]);

    readonly record struct GroupIdentity(
        string Name,
        MetadataTypeMemberGroupCategory Category);
}
