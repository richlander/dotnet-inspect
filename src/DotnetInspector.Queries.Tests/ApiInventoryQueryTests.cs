using DotnetInspector.Queries;
using ILInspector.Metadata;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace DotnetInspector.Queries.Tests;

public class ApiInventoryQueryTests
{
    [Fact]
    public void Types_DescriptorsDriveOrderedFiltering()
    {
        var surface = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "C", Kind = "class" },
                new ApiType { Name = "S", Kind = "struct" },
                new ApiType { Name = "I", Kind = "interface" },
                new ApiType { Name = "E", Kind = "enum" },
                new ApiType { Name = "D", Kind = "delegate" },
                new ApiType { Name = "C2", Kind = "class" },
            ]
        };

        var result = ApiInventoryQuery.Types(surface);

        Assert.Equal(
            ["class", "struct", "interface", "enum", "delegate"],
            result.KindFacets.Select(facet => facet.SingularLabel));
        Assert.Equal([2, 1, 1, 1, 1], result.KindFacets.Select(facet => facet.Count));
        Assert.True(result.KindFacets.Select(facet => facet.Weight).SequenceEqual(
            result.KindFacets.Select(facet => facet.Weight).Order()));
        Assert.All(result.KindFacets, facet => Assert.True(facet.IsDefault));
        var publicAccessibility = Assert.Single(result.AccessibilityFacets);
        Assert.Equal("public", publicAccessibility.SingularLabel);
        Assert.Equal(surface.Types.Count, publicAccessibility.Count);
        Assert.True(publicAccessibility.IsDefault);
        Assert.Equal(surface.Types, result.Types);

        foreach (var facet in result.KindFacets)
        {
            var filtered = ApiInventoryQuery.Types(
                surface,
                new ApiTypeInventoryRequest([facet.Id]));
            Assert.Equal(facet.Count, filtered.Types.Count);
        }

        var firstTwo = result.KindFacets.Take(2).Select(facet => facet.Id).ToList();
        var combined = ApiInventoryQuery.Types(
            surface,
            new ApiTypeInventoryRequest(firstTwo));
        Assert.Equal(3, combined.Types.Count);

        var defaults = ApiInventoryQuery.Types(
            surface,
            new ApiTypeInventoryRequest([]));
        Assert.Equal(surface.Types, defaults.Types);
    }

    [Fact]
    public void Members_RealMetadataShapesHaveProductOwnedFacets()
    {
        using var inspection = AssemblyInspectionSession.Open(
            typeof(ApiInventoryQueryTests).Assembly.Location);
        var surface = inspection.ApiSurface(includeAll: true);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryFixture).FullName);

        var catalog = ApiInventoryQuery.Members(type);
        Assert.Equal(
            [
                "public",
                "protected",
                "protected internal",
                "private protected",
                "internal",
                "private",
            ],
            catalog.AccessibilityFacets.Select(facet => facet.SingularLabel));
        Assert.True(Assert.Single(
            catalog.AccessibilityFacets,
            facet => facet.SingularLabel == "public").IsDefault);
        Assert.All(
            catalog.AccessibilityFacets.Where(facet => facet.SingularLabel != "public"),
            facet => Assert.False(facet.IsDefault));
        Assert.All(
            catalog.Members,
            member => Assert.True(
                string.IsNullOrEmpty(member.Accessibility)
                || member.Accessibility == "public"));

        var allAccessibilityIds = catalog.AccessibilityFacets
            .Select(facet => facet.Id)
            .ToList();
        var result = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest(AccessibilityFacetIds: allAccessibilityIds));

        Assert.Equal(
            [
                "constructor",
                "finalizer",
                "constant",
                "field",
                "property",
                "method",
                "operator",
                "extension method",
                "explicit implementation",
                "event",
            ],
            result.KindFacets.Select(facet => facet.SingularLabel));

        foreach (var facet in result.KindFacets)
        {
            var filtered = ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest([facet.Id], allAccessibilityIds));
            Assert.Equal(facet.Count, filtered.Members.Count);
        }

        foreach (var facet in catalog.AccessibilityFacets)
        {
            var filtered = ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest(AccessibilityFacetIds: [facet.Id]));
            Assert.Equal(facet.Count, filtered.Members.Count);
        }

        var constant = Assert.Single(result.KindFacets, facet => facet.SingularLabel == "constant");
        var constants = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest([constant.Id], allAccessibilityIds));
        Assert.Contains(constants.Members, member => member.Name == nameof(InventoryFixture.Constant));
        Assert.All(constants.Members, member => Assert.True(member.IsConst));

        var extension = Assert.Single(result.KindFacets, facet => facet.SingularLabel == "extension method");
        var extensions = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest([extension.Id], allAccessibilityIds));
        Assert.Contains(extensions.Members, member => member.Name == nameof(InventoryExtensions.Extend));
        Assert.Equal(
            "internal",
            Assert.Single(
                type.Members,
                member => member.Name == "InternalExtend"
                    && member.Kind == "extension-method").Accessibility);
        Assert.Equal(
            "private",
            Assert.Single(
                type.Members,
                member => member.Name == "PrivateExtend"
                    && member.Kind == "extension-method").Accessibility);

        ApiMember finalizer = Assert.Single(type.Members, member => member.Kind == "finalizer");
        ApiMember explicitImplementation = Assert.Single(
            type.Members,
            member => member.Kind == "explicit-interface-implementation");
        Assert.Equal("protected", finalizer.Accessibility);
        Assert.Equal("private", explicitImplementation.Accessibility);
        Assert.DoesNotContain(finalizer, catalog.Members);
        Assert.DoesNotContain(explicitImplementation, catalog.Members);

        var privateFacet = Assert.Single(
            catalog.AccessibilityFacets,
            facet => facet.SingularLabel == "private");
        var privateMembers = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest(AccessibilityFacetIds: [privateFacet.Id]));
        Assert.Contains(privateMembers.Members, member => member.Name == "s_privateField");
        Assert.DoesNotContain(privateMembers.Members, member => member.Name == "s_publicField");

        var publicFacet = Assert.Single(
            catalog.AccessibilityFacets,
            facet => facet.SingularLabel == "public");
        var publicMembers = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest(AccessibilityFacetIds: [publicFacet.Id]));
        Assert.Contains(publicMembers.Members, member => member.Name == "s_publicField");
        Assert.DoesNotContain(publicMembers.Members, member => member.Name == "s_privateField");

        var methodFacet = Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "method");
        var privateMethods = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest([methodFacet.Id], [privateFacet.Id]));
        Assert.All(privateMethods.Members, member =>
        {
            Assert.Equal("method", member.Kind);
            Assert.Equal("private", member.Accessibility);
        });
        Assert.Equal(
            privateMethods.Members.Count,
            Assert.Single(
                privateMethods.KindFacets,
                facet => facet.Id == methodFacet.Id).Count);
    }

    [Fact]
    public void Types_AccessibilityFacetsClassifyCompoundValues()
    {
        var surface = new ApiSurface
        {
            Types =
            [
                new ApiType { Name = "DefaultPublic", Kind = "class" },
                new ApiType { Name = "ExplicitPublic", Kind = "class", Accessibility = "public" },
                new ApiType { Name = "Protected", Kind = "class", Accessibility = "protected" },
                new ApiType { Name = "ProtectedInternal", Kind = "class", Accessibility = "protected internal" },
                new ApiType { Name = "PrivateProtected", Kind = "class", Accessibility = "private protected" },
                new ApiType { Name = "Internal", Kind = "class", Accessibility = "internal" },
                new ApiType { Name = "Private", Kind = "class", Accessibility = "private" },
            ]
        };

        var result = ApiInventoryQuery.Types(surface);

        Assert.Equal(
            [
                "public",
                "protected",
                "protected internal",
                "private protected",
                "internal",
                "private",
            ],
            result.AccessibilityFacets.Select(facet => facet.SingularLabel));
        Assert.Equal([2, 1, 1, 1, 1, 1], result.AccessibilityFacets.Select(facet => facet.Count));
        Assert.True(result.AccessibilityFacets.Select(facet => facet.Weight).SequenceEqual(
            result.AccessibilityFacets.Select(facet => facet.Weight).Order()));
        Assert.Equal(["DefaultPublic", "ExplicitPublic"], result.Types.Select(type => type.Name));

        foreach (var facet in result.AccessibilityFacets)
        {
            var filtered = ApiInventoryQuery.Types(
                surface,
                new ApiTypeInventoryRequest(AccessibilityFacetIds: [facet.Id]));
            Assert.Equal(facet.Count, filtered.Types.Count);
        }

        var protectedFamily = result.AccessibilityFacets
            .Where(facet => facet.SingularLabel.Contains("protected", StringComparison.Ordinal))
            .Select(facet => facet.Id)
            .ToList();
        var combined = ApiInventoryQuery.Types(
            surface,
            new ApiTypeInventoryRequest(AccessibilityFacetIds: protectedFamily));
        Assert.Equal(3, combined.Types.Count);
    }

    [Fact]
    public void AccessibilityClassification_UsesMetadataFactsNotGeneratedNames()
    {
        using var stream = File.OpenRead(typeof(ApiInventoryQueryTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var extracted = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true,
            includeCompilerGenerated: true);
        var generated = Assert.Single(
            extracted.Types,
            type => type.MetadataName == "ApiInventoryQueryTests+<>c");
        var generatedLookingPublic = Assert.Single(
            extracted.Types,
            type => type.FullName == typeof(__PublicAccessibilityFixture).FullName);
        var surface = new ApiSurface
        {
            Types = [generated, generatedLookingPublic]
        };

        var catalog = ApiInventoryQuery.Types(surface);
        var privateFacet = Assert.Single(
            catalog.AccessibilityFacets,
            facet => facet.SingularLabel == "private");
        var publicFacet = Assert.Single(
            catalog.AccessibilityFacets,
            facet => facet.SingularLabel == "public");

        var privateTypes = ApiInventoryQuery.Types(
            surface,
            new ApiTypeInventoryRequest(AccessibilityFacetIds: [privateFacet.Id]));
        var publicTypes = ApiInventoryQuery.Types(
            surface,
            new ApiTypeInventoryRequest(AccessibilityFacetIds: [publicFacet.Id]));

        Assert.Contains(generated, privateTypes.Types);
        Assert.Contains(generatedLookingPublic, publicTypes.Types);
    }

    [Fact]
    public void Members_CompilerProducedExtensionOperatorHasOneKindFacet()
    {
        using var inspection = AssemblyInspectionSession.Open(
            typeof(ApiInventoryQueryTests).Assembly.Location);
        var surface = inspection.ApiSurface(includeAll: true);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryExtensions).FullName);
        var extensionOperator = Assert.Single(
            type.Members,
            member => member.Name == "op_Addition");

        Assert.Equal("operator", extensionOperator.Kind);
        Assert.True(extensionOperator.IsExtension);

        var result = ApiInventoryQuery.Members(type);

        var operatorFacet = Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "operator" && facet.Count == 1);
        Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "extension method" && facet.Count == 1);
        Assert.Single(
            ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest([operatorFacet.Id]))
            .Members,
            member => member.Name == "op_Addition");

        var targetType = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryFixture).FullName);
        var projected = Assert.Single(
            targetType.Members,
            member => member.Name == "op_Addition"
                && member.DeclaringType == typeof(InventoryExtensions).FullName);
        Assert.Equal("extension-method", projected.Kind);

        var targetResult = ApiInventoryQuery.Members(targetType);
        var extensionFacet = Assert.Single(
            targetResult.KindFacets,
            facet => facet.SingularLabel == "extension method");
        Assert.Contains(
            ApiInventoryQuery.Members(
                targetType,
                new ApiMemberInventoryRequest([extensionFacet.Id]))
            .Members,
            member => ReferenceEquals(member, projected));
    }

    [Fact]
    public void Members_StaticConstructorUsesConstructorFacet()
    {
        using var inspection = AssemblyInspectionSession.Open(
            typeof(ApiInventoryQueryTests).Assembly.Location);
        var surface = inspection.ApiSurface(includeAll: true);
        var type = Assert.Single(
            surface.Types,
            candidate => candidate.FullName == typeof(InventoryFixture).FullName);
        var staticConstructor = Assert.Single(
            type.Members,
            member => member.Name == ".cctor");

        Assert.Equal("method", staticConstructor.Kind);

        var result = ApiInventoryQuery.Members(type);
        var allAccessibilities = result.AccessibilityFacets
            .Select(facet => facet.Id)
            .ToList();
        result = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest(AccessibilityFacetIds: allAccessibilities));
        var constructorFacet = Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "constructor");
        var methodFacet = Assert.Single(
            result.KindFacets,
            facet => facet.SingularLabel == "method");

        Assert.Contains(
            ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest(
                    [constructorFacet.Id],
                    allAccessibilities))
            .Members,
            member => ReferenceEquals(member, staticConstructor));
        Assert.DoesNotContain(
            ApiInventoryQuery.Members(
                type,
                new ApiMemberInventoryRequest(
                    [methodFacet.Id],
                    allAccessibilities))
            .Members,
            member => ReferenceEquals(member, staticConstructor));
    }

    [Fact]
    public void Types_PreservesPartialInspectionFailures()
    {
        var failure = new ApiSurfaceInspectionFailure(
            "relationship",
            0x02000001,
            MetadataTypeNameFailureMechanism.Relationship,
            "base-type",
            "cycle");
        var surface = new ApiSurface
        {
            Types = [new ApiType { Name = "C", Kind = "class" }],
            InspectionFailures = [failure]
        };

        var result = ApiInventoryQuery.Types(surface);

        Assert.Equal([failure], result.InspectionFailures);
        Assert.NotSame(surface.InspectionFailures, result.InspectionFailures);
    }

    [Fact]
    public void Members_PrivateScopeMetadataFlowsToPrivateFacet()
    {
        using var peReader = new PEReader(new MemoryStream(BuildPrivateScopeImage()));
        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        ApiType type = Assert.Single(surface.Types, candidate => candidate.Name == "PrivateScopeHost");
        ApiMemberInventoryResult catalog = ApiInventoryQuery.Members(type);
        ApiFacetDescriptor privateFacet = Assert.Single(
            catalog.AccessibilityFacets,
            facet => facet.SingularLabel == "private");

        ApiMemberInventoryResult result = ApiInventoryQuery.Members(
            type,
            new ApiMemberInventoryRequest(AccessibilityFacetIds: [privateFacet.Id]));

        Assert.Equal(2, result.Members.Count);
        Assert.Contains(result.Members, member => member.Name == "HiddenField");
        Assert.Contains(result.Members, member => member.Name == "HiddenMethod");
        Assert.All(result.Members, member => Assert.Equal("private", member.Accessibility));
        Assert.DoesNotContain(
            result.Members,
            member => string.IsNullOrEmpty(member.Accessibility));
    }

    [Fact]
    public void Extract_AccessorlessPropertyRecordsVisibleFailure()
    {
        using var peReader = new PEReader(new MemoryStream(BuildAccessorlessPropertyImage()));

        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "AccessorlessHost");
        Assert.Empty(type.Members);
        ApiSurfaceInspectionFailure failure = Assert.Single(surface.InspectionFailures);
        Assert.Equal("property accessors", failure.Operation);
        Assert.Contains("no getter or setter", failure.Detail, StringComparison.Ordinal);

        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == "AccessorlessHost");
        Assert.Throws<BadImageFormatException>(
            () => MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true));
    }

    [Fact]
    public void Extract_ReservedAccessibilitySkipsOnlyMalformedMembers()
    {
        using var peReader = new PEReader(new MemoryStream(BuildReservedAccessibilityImage()));

        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "ReservedAccessibilityHost");
        Assert.Contains(type.Members, member => member.Name == "GoodMethod");
        Assert.Contains(type.Members, member => member.Name == "GoodField");
        Assert.DoesNotContain(type.Members, member => member.Name == "Contracts.IProbe.BadMethod");
        Assert.DoesNotContain(type.Members, member => member.Name == "BadField");

        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == "ReservedAccessibilityHost");
        MethodDefinitionHandle badMethod = reader.GetTypeDefinition(typeHandle).GetMethods().Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name) == "Contracts.IProbe.BadMethod");
        MethodDefinitionHandle badGetter = reader.GetTypeDefinition(typeHandle).GetMethods().Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name) == "get_BadProperty");
        MethodDefinitionHandle badAdder = reader.GetTypeDefinition(typeHandle).GetMethods().Single(handle =>
            reader.GetString(reader.GetMethodDefinition(handle).Name) == "add_BadEvent");
        FieldDefinitionHandle badField = reader.GetTypeDefinition(typeHandle).GetFields().Single(handle =>
            reader.GetString(reader.GetFieldDefinition(handle).Name) == "BadField");
        PropertyDefinitionHandle badProperty = reader.GetTypeDefinition(typeHandle).GetProperties().Single();
        EventDefinitionHandle badEvent = reader.GetTypeDefinition(typeHandle).GetEvents().Single();
        Assert.Equal(
            new[]
            {
                ("event accessibility", MetadataTokens.GetToken(badEvent)),
                ("field accessibility", MetadataTokens.GetToken(badField)),
                ("method accessibility", MetadataTokens.GetToken(badMethod)),
                ("method accessibility", MetadataTokens.GetToken(badGetter)),
                ("method accessibility", MetadataTokens.GetToken(badAdder)),
                ("property accessibility", MetadataTokens.GetToken(badProperty))
            },
            surface.InspectionFailures
                .Select(failure => (failure.Operation, failure.SubjectToken))
                .OrderBy(failure => failure.Operation)
                .ThenBy(failure => failure.SubjectToken));

        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(peReader);
        ApiType summarized = Assert.Single(
            summary.Types,
            candidate => candidate.Name == "ReservedAccessibilityHost");
        Assert.Contains(summarized.Members, member => member.Name == "GoodMethod");
        Assert.Contains(summarized.Members, member => member.Name == "GoodField");
        Assert.DoesNotContain(
            summarized.Members,
            member => member.Name == "Contracts.IProbe.BadMethod");
        Assert.DoesNotContain(summarized.Members, member => member.Name == "BadField");
        Assert.Equal(
            surface.InspectionFailures
                .Select(failure => (failure.Operation, failure.SubjectToken))
                .OrderBy(failure => failure.Operation)
                .ThenBy(failure => failure.SubjectToken),
            summary.InspectionFailures
                .Select(failure => (failure.Operation, failure.SubjectToken))
                .OrderBy(failure => failure.Operation)
                .ThenBy(failure => failure.SubjectToken));

        Assert.Throws<BadImageFormatException>(
            () => MetadataDeclarationQuery.GetTypeSurface(
                reader,
                typeHandle,
                includeNonPublicMembers: true));
    }

    [Fact]
    public void Extract_EventAccessibilityUsesBothAccessorsAndRetainsRemoveOnlyEvents()
    {
        using var peReader = new PEReader(new MemoryStream(BuildEventAccessibilityImage()));

        ApiSurface surface = ApiSurfaceExtractor.Extract(peReader);

        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "EventAccessibilityHost");
        ApiMember changed = Assert.Single(type.Members, member => member.Name == "Changed");
        Assert.Null(changed.Accessibility);
        Assert.NotNull(changed.AdderToken);
        Assert.NotNull(changed.RemoverToken);
        ApiMember brokenMember = Assert.Single(type.Members, member => member.Name == "Broken");
        Assert.Null(brokenMember.AdderToken);
        Assert.NotNull(brokenMember.RemoverToken);
        Assert.Empty(surface.InspectionFailures);

        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(peReader);
        ApiType summarized = Assert.Single(
            summary.Types,
            candidate => candidate.Name == "EventAccessibilityHost");
        Assert.Contains(summarized.Members, member => member is { Name: "Changed", Kind: "event" });
        Assert.Contains(summarized.Members, member => member is { Name: "Broken", Kind: "event" });
        Assert.Equal(2, summary.PublicEventCount);

        MetadataReader reader = peReader.GetMetadataReader();
        EventDefinitionHandle broken = reader.GetTypeDefinition(
                reader.TypeDefinitions.Single(handle =>
                    reader.GetString(reader.GetTypeDefinition(handle).Name) == "EventAccessibilityHost"))
            .GetEvents()
            .Single(handle => reader.GetString(reader.GetEventDefinition(handle).Name) == "Broken");
        TypeDefinitionHandle typeHandle = reader.GetEventDefinition(broken).GetDeclaringType();
        ApiType queried = MetadataDeclarationQuery.GetTypeSurface(
            reader,
            typeHandle,
            includeNonPublicMembers: true);
        Assert.Single(queried.Members, member => member.Name == "Broken");
    }

    [Fact]
    public void Selection_RejectsUnknownFacetIds()
    {
        var surface = new ApiSurface
        {
            Types = [new ApiType { Name = "C", Kind = "class" }]
        };

        var error = Assert.Throws<ArgumentException>(() =>
            ApiInventoryQuery.Types(
                surface,
                new ApiTypeInventoryRequest(["consumer-invented-kind"])));

        Assert.Contains("consumer-invented-kind", error.Message);

        error = Assert.Throws<ArgumentException>(() =>
            ApiInventoryQuery.Types(
                surface,
                new ApiTypeInventoryRequest(
                    AccessibilityFacetIds: ["consumer-invented-accessibility"])));

        Assert.Contains("consumer-invented-accessibility", error.Message);
    }

    [Fact]
    public void Classification_RejectsUnknownProducerKinds()
    {
        var surface = new ApiSurface
        {
            Types = [new ApiType { Name = "Future", Kind = "future-kind" }]
        };
        var type = new ApiType
        {
            Name = "Future",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "Future",
                    Kind = "future-kind",
                    IsConst = true
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() => ApiInventoryQuery.Types(surface));
        Assert.Throws<InvalidOperationException>(() => ApiInventoryQuery.Members(type));

        var unknownAccessibility = new ApiSurface
        {
            Types =
            [
                new ApiType
                {
                    Name = "Future",
                    Kind = "class",
                    Accessibility = "future-accessibility"
                }
            ]
        };
        Assert.Throws<InvalidOperationException>(
            () => ApiInventoryQuery.Types(unknownAccessibility));

        var emptyAccessibility = new ApiSurface
        {
            Types = [new ApiType { Name = "Future", Kind = "class", Accessibility = "" }]
        };
        var emptyMemberAccessibility = new ApiType
        {
            Name = "Future",
            Kind = "class",
            Members = [new ApiMember { Name = "Run", Kind = "method", Accessibility = "" }]
        };
        Assert.Throws<InvalidOperationException>(
            () => ApiInventoryQuery.Types(emptyAccessibility));
        Assert.Throws<InvalidOperationException>(
            () => ApiInventoryQuery.Members(emptyMemberAccessibility));
    }

    private static byte[] BuildPrivateScopeImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("PrivateScope.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("PrivateScope"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("PrivateScopeHost"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06); // FIELD
        fieldSignature.WriteByte(0x08); // ELEMENT_TYPE_I4
        metadata.AddFieldDefinition(
            FieldAttributes.PrivateScope,
            metadata.GetOrAddString("HiddenField"),
            metadata.GetOrAddBlob(fieldSignature));

        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x00); // DEFAULT
        methodSignature.WriteByte(0x00); // parameter count
        methodSignature.WriteByte(0x01); // ELEMENT_TYPE_VOID
        metadata.AddMethodDefinition(
            MethodAttributes.PrivateScope | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("HiddenMethod"),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static byte[] BuildAccessorlessPropertyImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("Accessorless.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Accessorless"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("AccessorlessHost"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var propertySignature = new BlobBuilder();
        propertySignature.WriteByte(0x08); // PROPERTY
        propertySignature.WriteByte(0x00); // parameter count
        propertySignature.WriteByte(0x08); // ELEMENT_TYPE_I4
        PropertyDefinitionHandle property = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("Value"),
            metadata.GetOrAddBlob(propertySignature));
        metadata.AddPropertyMap(type, property);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static byte[] BuildReservedAccessibilityImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("ReservedAccessibility.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ReservedAccessibility"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("ReservedAccessibilityHost"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        AssemblyReferenceHandle contractsAssembly = metadata.AddAssemblyReference(
            metadata.GetOrAddString("Contracts"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKeyOrToken: default,
            flags: default,
            hashValue: default);
        TypeReferenceHandle interfaceType = metadata.AddTypeReference(
            contractsAssembly,
            metadata.GetOrAddString("Contracts"),
            metadata.GetOrAddString("IProbe"));
        metadata.AddInterfaceImplementation(type, interfaceType);

        var fieldSignature = new BlobBuilder();
        fieldSignature.WriteByte(0x06); // FIELD
        fieldSignature.WriteByte(0x08); // ELEMENT_TYPE_I4
        BlobHandle fieldSignatureHandle = metadata.GetOrAddBlob(fieldSignature);
        metadata.AddFieldDefinition(
            FieldAttributes.Public | FieldAttributes.Static,
            metadata.GetOrAddString("GoodField"),
            fieldSignatureHandle);
        metadata.AddFieldDefinition(
            (FieldAttributes)0x0007 | FieldAttributes.Static,
            metadata.GetOrAddString("BadField"),
            fieldSignatureHandle);

        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x00); // DEFAULT
        methodSignature.WriteByte(0x00); // parameter count
        methodSignature.WriteByte(0x01); // ELEMENT_TYPE_VOID
        BlobHandle methodSignatureHandle = metadata.GetOrAddBlob(methodSignature);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("GoodMethod"),
            methodSignatureHandle,
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle badMethod = metadata.AddMethodDefinition(
            (MethodAttributes)0x0007 | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("Contracts.IProbe.BadMethod"),
            methodSignatureHandle,
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        MemberReferenceHandle interfaceMethod = metadata.AddMemberReference(
            interfaceType,
            metadata.GetOrAddString("BadMethod"),
            methodSignatureHandle);
        metadata.AddMethodImplementation(type, badMethod, interfaceMethod);

        MethodDefinitionHandle badGetter = metadata.AddMethodDefinition(
            (MethodAttributes)0x0007 | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("get_BadProperty"),
            metadata.GetOrAddBlob(new byte[] { 0x00, 0x00, 0x08 }),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        PropertyDefinitionHandle badProperty = metadata.AddProperty(
            PropertyAttributes.None,
            metadata.GetOrAddString("BadProperty"),
            metadata.GetOrAddBlob(new byte[] { 0x08, 0x00, 0x08 }));
        metadata.AddPropertyMap(type, badProperty);
        metadata.AddMethodSemantics(
            badProperty,
            MethodSemanticsAttributes.Getter,
            badGetter);

        var eventAccessorSignature = new BlobBuilder();
        new BlobEncoder(eventAccessorSignature)
            .MethodSignature()
            .Parameters(
                1,
                returnType => returnType.Void(),
                parameters => parameters.AddParameter().Type().Type(type, isValueType: false));
        MethodDefinitionHandle badAdder = metadata.AddMethodDefinition(
            (MethodAttributes)0x0007 | MethodAttributes.Static | MethodAttributes.SpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("add_BadEvent"),
            metadata.GetOrAddBlob(eventAccessorSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        EventDefinitionHandle badEvent = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("BadEvent"),
            type);
        metadata.AddEventMap(type, badEvent);
        metadata.AddMethodSemantics(
            badEvent,
            MethodSemanticsAttributes.Adder,
            badAdder);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    private static byte[] BuildEventAccessibilityImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            generation: 0,
            moduleName: metadata.GetOrAddString("EventAccessibility.dll"),
            mvid: metadata.GetOrAddGuid(Guid.NewGuid()),
            encId: default,
            encBaseId: default);
        metadata.AddAssembly(
            metadata.GetOrAddString("EventAccessibility"),
            new Version(1, 0, 0, 0),
            culture: default,
            publicKey: default,
            flags: default,
            hashAlgorithm: default);
        metadata.AddTypeDefinition(
            default,
            default,
            metadata.GetOrAddString("<Module>"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("Fixtures"),
            metadata.GetOrAddString("EventAccessibilityHost"),
            baseType: default,
            fieldList: MetadataTokens.FieldDefinitionHandle(1),
            methodList: MetadataTokens.MethodDefinitionHandle(1));

        var methodSignature = new BlobBuilder();
        methodSignature.WriteByte(0x00); // DEFAULT
        methodSignature.WriteByte(0x00); // parameter count
        methodSignature.WriteByte(0x01); // ELEMENT_TYPE_VOID
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("GoodMethod"),
            metadata.GetOrAddBlob(methodSignature),
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        var accessorSignature = new BlobBuilder();
        accessorSignature.WriteByte(0x20); // HASTHIS
        accessorSignature.WriteByte(0x01); // parameter count
        accessorSignature.WriteByte(0x01); // ELEMENT_TYPE_VOID
        accessorSignature.WriteByte(0x12); // ELEMENT_TYPE_CLASS
        accessorSignature.WriteByte(0x08); // TypeDef row 2
        BlobHandle accessorSignatureHandle = metadata.GetOrAddBlob(accessorSignature);
        MethodDefinitionHandle adder = metadata.AddMethodDefinition(
            MethodAttributes.Private | MethodAttributes.SpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("add_Changed"),
            accessorSignatureHandle,
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle remover = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("remove_Changed"),
            accessorSignatureHandle,
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));
        MethodDefinitionHandle brokenRemover = metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.SpecialName,
            MethodImplAttributes.Runtime,
            metadata.GetOrAddString("remove_Broken"),
            accessorSignatureHandle,
            bodyOffset: 0,
            parameterList: MetadataTokens.ParameterHandle(1));

        EventDefinitionHandle changed = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("Changed"),
            type);
        EventDefinitionHandle broken = metadata.AddEvent(
            EventAttributes.None,
            metadata.GetOrAddString("Broken"),
            type);
        metadata.AddEventMap(type, changed);
        metadata.AddMethodSemantics(changed, MethodSemanticsAttributes.Adder, adder);
        metadata.AddMethodSemantics(changed, MethodSemanticsAttributes.Remover, remover);
        metadata.AddMethodSemantics(broken, MethodSemanticsAttributes.Remover, brokenRemover);

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata, suppressValidation: true),
            new BlobBuilder(),
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }
}

public interface IInventoryFixture
{
    void Explicit();
}

public class InventoryFixture : IInventoryFixture
{
    public const int Constant = 1;
    public int Field;
    public static int s_publicField;
    private static int s_privateField = 1;
    public int Property { get; set; }
    public event EventHandler? Changed;

    public InventoryFixture() { }

    static InventoryFixture() { }

    ~InventoryFixture() { }

    public void Method() => Changed?.Invoke(this, EventArgs.Empty);

    protected void ProtectedMethod() { }

    protected internal void ProtectedInternalMethod() { }

    private protected void PrivateProtectedMethod() { }

    internal void InternalMethod() { }

    private int PrivateMethod() => s_privateField;

    void IInventoryFixture.Explicit() { }

    public static InventoryFixture operator +(InventoryFixture left, InventoryFixture right)
        => left;
}

public static class InventoryExtensions
{
    public static void Extend(this InventoryFixture fixture) => fixture.Method();

    internal static void InternalExtend(this InventoryFixture fixture) => fixture.Method();

    private static void PrivateExtend(this InventoryFixture fixture) => fixture.Method();

    public static void op_Addition(this InventoryFixture fixture) => fixture.Method();
}

public struct InventoryStruct;
public interface IInventoryType;
public enum InventoryEnum;
public delegate void InventoryDelegate();
public class __PublicAccessibilityFixture;
