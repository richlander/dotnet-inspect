using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using CSharpText;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata.Tests;

public class ApiMemberIdentityTests
{
    [Theory]
    [InlineData(".ctor", false, ".ctor")]
    [InlineData("op_Addition", false, "operator:op_Addition")]
    [InlineData("IFoo.Bar", false, "explicit:IFoo.Bar")]
    [InlineData("Twice", true, "extension:Twice")]
    [InlineData("M", false, "M")]
    public void GetMemberSelectorName_PreservesMemberIndexPrefixes(string metadataName, bool isExtension, string expected)
    {
        Assert.Equal(expected, ApiMemberIdentity.GetMemberSelectorName(metadataName, isExtension));
    }

    [Fact]
    public void CreateMethodAnchor_UsesMetadataCanonicalSignature()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (typeHandle, method) = FindFixtureMethod(reader);

        var anchor = ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, method);

        Assert.Equal(
            "M:ILInspector.Metadata.Tests.ApiMemberIdentityTests+ApiMemberIdentityFixture<T>.M<U>(System.Int32,U)",
            anchor.CanonicalSignature);
        Assert.Equal("ILInspector.Metadata.Tests.ApiMemberIdentityTests+ApiMemberIdentityFixture<T>", anchor.TypeFullName);
        Assert.Equal("M<U>", anchor.MemberName);
        Assert.StartsWith("M~", anchor.StableSelector, StringComparison.Ordinal);
        Assert.Equal(MemberAnchor.ComputeFingerprint(anchor.CanonicalSignature), anchor.Fingerprint);
    }

    [Fact]
    public void CreateMethodAnchorInfo_IncludesCanonicalReturnType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (typeHandle, method) = FindFixtureMethod(reader);

        var identity = ApiMemberIdentity.CreateMethodAnchorInfo(reader, typeHandle, method);

        Assert.Equal("System.Void", identity.ReturnType);
        Assert.Equal(
            ApiMemberIdentity.CreateMethodAnchor(reader, typeHandle, method),
            identity.Anchor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreateMethodAnchorInfo_BoundedProjectionPreservesIdentity(
        bool isExtensionMethod)
    {
        using var stream =
            File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var (typeHandle, method) = FindFixtureMethod(reader);
        MethodAnchorInfo expected =
            ApiMemberIdentity.CreateMethodAnchorInfo(
                reader,
                typeHandle,
                method,
                isExtensionMethod);
        int workRemaining =
            MetadataSafetyPolicy.MaxAnchorSignatureWorkChars;

        MethodAnchorInfo actual =
            ApiMemberIdentity.CreateMethodAnchorInfo(
                reader,
                typeHandle,
                method,
                ref workRemaining,
                isExtensionMethod);

        Assert.Equal(expected, actual);
        int spent =
            MetadataSafetyPolicy.MaxAnchorSignatureWorkChars
            - workRemaining;
        Assert.True(
            spent > expected.Anchor.CanonicalSignature.Length * 2,
            $"Complete projection charged only {spent:N0} work units.");
    }

    [Fact]
    public void CreateMethodAnchorInfo_RepeatedLongNamesExhaustSharedProjectionBudget()
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 8,
            methodNameLength: 300_000);
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle =
            reader.TypeDefinitions.Last();
        TypeDefinition type = reader.GetTypeDefinition(typeHandle);
        int workRemaining =
            MetadataSafetyPolicy.MaxAnchorSignatureWorkChars;
        int completed = 0;

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () =>
                {
                    foreach (MethodDefinitionHandle methodHandle in
                        type.GetMethods())
                    {
                        ApiMemberIdentity.CreateMethodAnchorInfo(
                            reader,
                            typeHandle,
                            reader.GetMethodDefinition(methodHandle),
                            ref workRemaining);
                        completed++;
                    }
                });
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains(
            "cumulative work budget",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, workRemaining);
        Assert.InRange(completed, 2, 7);
        Assert.True(
            allocated < 24 * 1024 * 1024,
            $"Repeated long-name projection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CreateMethodAnchorInfo_HighGenericArityExhaustsBeforeContextAllocation()
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 1,
            methodNameLength: 1,
            typeGenericParameterCount: 16_384);
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle =
            reader.TypeDefinitions.Last();
        MethodDefinition method =
            reader.GetMethodDefinition(
                reader.GetTypeDefinition(typeHandle).GetMethods().Single());
        int workRemaining = 512 * 1024;

        long allocatedBefore =
            GC.GetAllocatedBytesForCurrentThread();
        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () => ApiMemberIdentity.CreateMethodAnchorInfo(
                    reader,
                    typeHandle,
                    method,
                    ref workRemaining));
        long allocated =
            GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        Assert.Contains(
            "cumulative work budget",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, workRemaining);
        Assert.True(
            allocated < 1024 * 1024,
            $"High-arity projection allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void CreateMethodAnchorInfo_SelectorProjectionHasANonVacuousBudgetGate()
        => AssertProjectionStageExhaustion(
            workRemaining: 166,
            expectedStage: "selector projection");

    [Fact]
    public void CreateMethodAnchorInfo_FingerprintProjectionHasANonVacuousBudgetGate()
        => AssertProjectionStageExhaustion(
            workRemaining: 208,
            expectedStage: "fingerprint projection");

    [Fact]
    public void CreateMethodAnchorInfo_StableSelectorProjectionHasANonVacuousBudgetGate()
        => AssertProjectionStageExhaustion(
            workRemaining: 520,
            expectedStage: "stable selector projection");

    [Fact]
    public void GetMemberAnchor_DisambiguatesConversionOperatorsByReturnType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var type = surface.Types.Single(t => t.Name.EndsWith(nameof(ConversionOperatorFixture), StringComparison.Ordinal));
        var conversions = type.Members
            .Where(member => member.Kind == "operator" && member.Name == "op_Explicit")
            .ToList();

        // Two explicit conversions that differ ONLY by return type (op_Explicit(Fixture)).
        Assert.Equal(2, conversions.Count);

        var anchors = conversions
            .Select(member => ApiMemberIdentity.GetMemberAnchor(type, member))
            .ToList();

        // Return type must be part of the canonical identity, so the two conversions
        // get distinct canonical signatures and fingerprints (no ambiguous anchor).
        Assert.Equal(2, anchors.Select(anchor => anchor.CanonicalSignature).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, anchors.Select(anchor => anchor.Fingerprint).Distinct(StringComparer.Ordinal).Count());
        Assert.All(anchors, anchor => Assert.Contains("op_Explicit", anchor.CanonicalSignature, StringComparison.Ordinal));
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.EndsWith("~int", StringComparison.Ordinal));
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.EndsWith("~long", StringComparison.Ordinal));
    }

    [Fact]
    public void ConversionOperatorNames_AreClosedAndRecognized()
    {
        string[] expected =
        [
            "op_Implicit",
            "op_Explicit",
            "op_CheckedImplicit",
            "op_CheckedExplicit",
        ];

        Assert.Equal(expected, ApiMemberIdentity.ConversionOperatorNames);
        Assert.All(
            ApiMemberIdentity.ConversionOperatorNames,
            name => Assert.True(ApiMemberIdentity.IsConversionOperator(name)));
        Assert.False(ApiMemberIdentity.IsConversionOperator("op_Addition"));
        Assert.False(ApiMemberIdentity.IsConversionOperator("op_CheckedAddition"));
    }

    [Fact]
    public void ConversionOperatorIdentity_PreservesReturnTypeForEveryDeclaredName()
    {
        byte[] image = BuildConversionOperatorIdentityImage();
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinition metadataType = reader.GetTypeDefinition(
            Assert.Single(
                reader.TypeDefinitions,
                handle => reader.GetString(
                    reader.GetTypeDefinition(handle).Name) == "C"));
        ApiSurface surface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType type = Assert.Single(
            surface.Types,
            candidate => candidate.Name == "C");

        foreach (string name in ApiMemberIdentity.ConversionOperatorNames)
        {
            List<MemberSignatureShape> shapes = [];
            foreach (MethodDefinitionHandle handle in
                metadataType.GetMethods().Where(
                    handle => reader.GetString(
                        reader.GetMethodDefinition(handle).Name) == name))
            {
                MemberSignatureShapeResult result =
                    MetadataMemberSignatureShape.Create(reader, handle);
                Assert.True(result.IsAvailable, result.UnavailableReason);
                Assert.NotNull(result.Shape!.ConversionReturnType);
                shapes.Add(result.Shape);
            }
            Assert.Equal(2, shapes.Distinct().Count());

            List<ApiMember> conversions =
            [
                .. type.Members.Where(member => member.Name == name),
            ];
            Assert.Equal(2, conversions.Count);
            Assert.All(conversions, member => Assert.NotNull(member.ReturnType));

            List<MemberAnchor> anchors =
            [
                .. conversions.Select(
                    member => ApiMemberIdentity.GetMemberAnchor(type, member)),
            ];
            Assert.Equal(
                2,
                anchors
                    .Select(anchor => anchor.CanonicalSignature)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.Equal(
                2,
                anchors
                    .Select(anchor => anchor.Fingerprint)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.Equal(
                2,
                anchors
                    .Select(anchor => anchor.StableSelector)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
            Assert.Contains(
                anchors,
                anchor => anchor.CanonicalSignature.EndsWith(
                    "~int",
                    StringComparison.Ordinal));
            Assert.Contains(
                anchors,
                anchor => anchor.CanonicalSignature.EndsWith(
                    "~long",
                    StringComparison.Ordinal));

            foreach (MemberAnchor anchor in anchors)
            {
                MemberTargetResolution resolution =
                    MemberTargetResolver.Resolve(
                        type,
                        MemberTargetSelector.Parse(anchor.StableSelector));
                Assert.True(resolution.Found);
                Assert.Equal(anchor, resolution.Target!.Anchor);
            }

            List<CSharpText.XmlDocMemberIdentity> xmlIdentities = [];
            foreach (ApiMember conversion in conversions)
            {
                Assert.True(
                    ApiMemberIdentity.TryGetXmlDocMemberIdentity(
                        type,
                        conversion,
                        out CSharpText.XmlDocMemberIdentity identity));
                xmlIdentities.Add(identity);
            }
            Assert.Equal(
                2,
                xmlIdentities
                    .Select(identity => identity.NormalizedReturnType)
                    .Distinct(StringComparer.Ordinal)
                    .Count());
        }

        List<ApiMember> additions =
        [
            .. type.Members.Where(member => member.Name == "op_Addition"),
        ];
        Assert.Equal(2, additions.Count);
        Assert.All(additions, member => Assert.Null(member.ReturnType));
        Assert.Single(
            additions
                .Select(member =>
                    ApiMemberIdentity
                        .GetMemberAnchor(type, member)
                        .CanonicalSignature)
                .Distinct(StringComparer.Ordinal));

        string json = JsonSerializer.Serialize(surface);
        ApiSurface roundTripped =
            JsonSerializer.Deserialize<ApiSurface>(json)!;
        ApiType roundTrippedType = Assert.Single(
            roundTripped.Types,
            candidate => candidate.Name == "C");
        foreach (string name in ApiMemberIdentity.ConversionOperatorNames)
        {
            List<ApiMember> conversions =
            [
                .. roundTrippedType.Members.Where(
                    member => member.Name == name),
            ];
            Assert.Equal(2, conversions.Count);
            Assert.All(conversions, member => Assert.Null(member.SignatureModel));
            Assert.All(conversions, member => Assert.NotNull(member.ReturnType));
            Assert.Equal(
                2,
                conversions
                    .Select(member =>
                        ApiMemberIdentity.GetCanonicalSignature(
                            roundTrippedType,
                            member))
                    .Distinct(StringComparer.Ordinal)
                    .Count());
        }
    }

    [Fact]
    public void GetMemberAnchor_DisambiguatesOverloadedIndexersByParameterType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var type = surface.Types.Single(t => t.Name.EndsWith(nameof(IndexerFixture), StringComparison.Ordinal));
        var indexers = type.Members
            .Where(member => member.Kind == "property" && member.SignatureModel is { Parameters.Count: > 0 })
            .ToList();

        // Two indexer overloads that differ only by parameter type: this[int] and this[string].
        Assert.Equal(2, indexers.Count);

        var anchors = indexers
            .Select(member => ApiMemberIdentity.GetMemberAnchor(type, member))
            .ToList();

        // Parameter types must be part of the canonical identity, so the two indexers get
        // distinct canonical signatures and fingerprints instead of colliding on "P:Type.Item"
        // and being paired by declaration order.
        Assert.Equal(2, anchors.Select(anchor => anchor.CanonicalSignature).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, anchors.Select(anchor => anchor.Fingerprint).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.Contains("(int)", StringComparison.Ordinal));
        Assert.Contains(anchors, anchor => anchor.CanonicalSignature.Contains("(string)", StringComparison.Ordinal));
    }

    [Fact]
    public void FallbackCanonicalSignature_DisambiguatesIndexers_AfterJsonRoundTrip()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var liveType = surface.Types.Single(t => t.Name.EndsWith(nameof(IndexerFixture), StringComparison.Ordinal));
        var liveIndexers = liveType.Members
            .Where(member => member.Kind == "property" && member.SignatureModel is { Parameters.Count: > 0 })
            .ToList();
        Assert.Equal(2, liveIndexers.Count);

        // Round-trip through JSON. SignatureModel is [JsonIgnore], so the deserialized
        // members have no SignatureModel and exercise the raw-signature fallback path.
        var json = JsonSerializer.Serialize(surface);
        var roundTripped = JsonSerializer.Deserialize<ApiSurface>(json)!;
        var roundTrippedType = roundTripped.Types.Single(t => t.Name.EndsWith(nameof(IndexerFixture), StringComparison.Ordinal));
        var roundTrippedIndexers = roundTrippedType.Members
            .Where(member => member.Kind == "property" && member.Signature != null && member.Signature.Contains("this[", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, roundTrippedIndexers.Count);
        Assert.All(roundTrippedIndexers, member => Assert.Null(member.SignatureModel));

        // The fallback must still disambiguate by parameter type on the round-tripped
        // surface (parsed from the raw "this[...]" signature text), and -- critically --
        // must produce the EXACT SAME canonical signatures as the live-assembly path, so a
        // JSON-persisted baseline pairs correctly against a live-extracted assembly.
        var liveCanonicals = liveIndexers
            .Select(member => ApiMemberIdentity.GetCanonicalSignature(liveType, member))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        var roundTrippedCanonicals = roundTrippedIndexers
            .Select(member => ApiMemberIdentity.GetCanonicalSignature(roundTrippedType, member))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, roundTrippedCanonicals.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(liveCanonicals, roundTrippedCanonicals);
        Assert.Contains(roundTrippedCanonicals, canonical => canonical.Contains("(int)", StringComparison.Ordinal));
        Assert.Contains(roundTrippedCanonicals, canonical => canonical.Contains("(string)", StringComparison.Ordinal));
    }

    [Fact]
    public void CompleteNestedAnchor_SurvivesJsonRoundTrip()
    {
        var definition = Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
            MetadataTypeDefinitionName.Create("N", ["Outer", "Inner"])).Name;
        var type = new ApiType
        {
            Namespace = "N",
            Name = "Outer.Inner",
            Kind = "class",
            DefinitionName = definition,
            IntroducedTypeParameterCounts = [0, 0],
            Members =
            [
                new ApiMember
                {
                    Name = "M",
                    Kind = "method",
                    Signature = "void M()",
                    SignatureModel = new ApiSignature
                    {
                        MemberName = "M",
                        ReturnType = "void",
                    },
                },
            ],
        };
        var surface = new ApiSurface { Types = [type] };
        ApiMemberIdentity.PopulateCanonicalIdentities(surface);
        MemberAnchor before =
            ApiMemberIdentity.GetMemberAnchor(type, type.Members[0]);

        string json = JsonSerializer.Serialize(surface);
        ApiSurface restored =
            JsonSerializer.Deserialize<ApiSurface>(json)!;
        ApiType restoredType = Assert.Single(restored.Types);
        MemberAnchor after = ApiMemberIdentity.GetMemberAnchor(
            restoredType,
            Assert.Single(restoredType.Members));

        Assert.Contains("definitionName", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(definition, restoredType.DefinitionName);
        Assert.Equal(before, after);
    }

    [Fact]
    public void GetCanonicalSignature_OrdinaryPropertyFormatIsUnchangedByIndexerFix()
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var property = new ApiMember
        {
            Name = "Name",
            Kind = "property",
            Signature = "string Name { get; set; }",
        };

        Assert.Equal("P:N.C.Name", ApiMemberIdentity.GetCanonicalSignature(type, property));
        Assert.True(ApiMemberIdentity.TryGetCanonicalSignature(type, property, out var tryResult));
        Assert.Equal("P:N.C.Name", tryResult);
    }

    [Fact]
    public void GetMemberAnchor_DisambiguatesDegradedPlaceholderFromGenuineSignature()
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var genuine = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = "object M()",
        };
        var degraded = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = "object M()",
            SignatureDecodeStatus = SignatureDecodeStatus.Degraded,
        };

        var genuineAnchor = ApiMemberIdentity.GetMemberAnchor(type, genuine);
        var degradedAnchor = ApiMemberIdentity.GetMemberAnchor(type, degraded);

        Assert.Equal(genuineAnchor.CanonicalSignature, degradedAnchor.CanonicalSignature);
        Assert.NotEqual(genuineAnchor.Fingerprint, degradedAnchor.Fingerprint);
        Assert.NotEqual(genuineAnchor.StableSelector, degradedAnchor.StableSelector);
        Assert.Equal(
            MemberAnchor.ComputeFingerprint(genuineAnchor.CanonicalSignature),
            genuineAnchor.Fingerprint);
        Assert.Equal(
            MemberAnchor.ComputeFingerprint(degradedAnchor.CanonicalSignature, isDegraded: true),
            degradedAnchor.Fingerprint);
    }

    [Fact]
    public void CreateMethodAnchor_DisambiguatesConversionOperatorsByReturnType()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var conversions = FindConversionOperatorMethods(reader);
        Assert.Equal(2, conversions.Count);

        var canonicals = conversions
            .Select(entry => ApiMemberIdentity.CreateMethodAnchor(reader, entry.TypeHandle, entry.Method).CanonicalSignature)
            .ToList();

        // The SRM-direct producer must also disambiguate the two op_Explicit conversions
        // that differ only by return type (its own full-name vocabulary: ~System.Int32/~System.Int64).
        Assert.Equal(2, canonicals.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~System.Int32", StringComparison.Ordinal));
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~System.Int64", StringComparison.Ordinal));
    }

    [Fact]
    public void FallbackCanonicalSignature_DisambiguatesConversionOperators_AfterJsonRoundTrip()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        // Round-trip through JSON. SignatureModel is [JsonIgnore], so the deserialized
        // members have no SignatureModel and exercise the GetCanonicalSignature fallback.
        var json = JsonSerializer.Serialize(surface);
        var roundTripped = JsonSerializer.Deserialize<ApiSurface>(json)!;

        var type = roundTripped.Types.Single(t => t.Name.EndsWith(nameof(ConversionOperatorFixture), StringComparison.Ordinal));
        var conversions = type.Members
            .Where(member => member.Kind == "operator" && member.Name == "op_Explicit")
            .ToList();
        Assert.Equal(2, conversions.Count);
        Assert.All(conversions, member => Assert.Null(member.SignatureModel));

        // The fallback must still disambiguate by return type on the round-tripped surface,
        // using the persisted ApiMember.ReturnType.
        var canonicals = conversions
            .Select(member => ApiMemberIdentity.GetCanonicalSignature(type, member))
            .ToList();
        Assert.Equal(2, canonicals.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~int", StringComparison.Ordinal));
        Assert.Contains(canonicals, canonical => canonical.EndsWith("~long", StringComparison.Ordinal));
    }

    [Fact]
    public void FallbackCanonicalSignature_AttributedParameterMatchesLiveAfterJsonRoundTrip()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var liveType = surface.Types.Single(
            type => type.Name.EndsWith(nameof(AttributedParameterFixture), StringComparison.Ordinal));
        var liveMember = liveType.Members.Single(
            member => member.Name == nameof(AttributedParameterFixture.M));
        Assert.NotNull(liveMember.SignatureModel);
        Assert.Contains("DateTimeConstant", liveMember.Signature, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize(surface);
        var roundTripped = JsonSerializer.Deserialize<ApiSurface>(json)!;
        var persistedType = roundTripped.Types.Single(
            type => type.Name.EndsWith(nameof(AttributedParameterFixture), StringComparison.Ordinal));
        var persistedMember = persistedType.Members.Single(
            member => member.Name == nameof(AttributedParameterFixture.M));
        Assert.Null(persistedMember.SignatureModel);

        var liveCanonical = ApiMemberIdentity.GetCanonicalSignature(liveType, liveMember);
        var persistedCanonical = ApiMemberIdentity.GetCanonicalSignature(persistedType, persistedMember);

        Assert.Equal(liveCanonical, persistedCanonical);
        Assert.EndsWith(".M(System.DateTime,int)", persistedCanonical, StringComparison.Ordinal);
        Assert.DoesNotContain("Optional", persistedCanonical, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeConstant", persistedCanonical, StringComparison.Ordinal);
    }

    [Fact]
    public void FallbackCanonicalSignature_AttributedIndexerMatchesLiveAfterJsonRoundTrip()
    {
        using var stream = File.OpenRead(typeof(ApiMemberIdentityTests).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var liveType = surface.Types.Single(
            type => type.Name.EndsWith(nameof(AttributedParameterFixture), StringComparison.Ordinal));
        var liveIndexer = liveType.Members.Single(
            member => member.Kind == "property" && member.Name == "Item");
        Assert.NotNull(liveIndexer.SignatureModel);
        Assert.Contains("DateTimeConstant", liveIndexer.Signature, StringComparison.Ordinal);

        var json = JsonSerializer.Serialize(surface);
        var roundTripped = JsonSerializer.Deserialize<ApiSurface>(json)!;
        var persistedType = roundTripped.Types.Single(
            type => type.Name.EndsWith(nameof(AttributedParameterFixture), StringComparison.Ordinal));
        var persistedIndexer = persistedType.Members.Single(
            member => member.Kind == "property" && member.Name == "Item");
        Assert.Null(persistedIndexer.SignatureModel);

        var liveCanonical = ApiMemberIdentity.GetCanonicalSignature(liveType, liveIndexer);
        var persistedCanonical = ApiMemberIdentity.GetCanonicalSignature(persistedType, persistedIndexer);

        Assert.Equal(liveCanonical, persistedCanonical);
        Assert.EndsWith(".Item(System.DateTime)", persistedCanonical, StringComparison.Ordinal);
        Assert.DoesNotContain("Optional", persistedCanonical, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeConstant", persistedCanonical, StringComparison.Ordinal);
    }

    [Fact]
    public void FallbackCanonicalSignature_StripsMultipleLeadingAttributeLists()
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = "void M([A][B(typeof(int[]))] System.DateTime when, int[] values)",
        };

        Assert.Equal(
            "M:N.C.M(System.DateTime,int[])",
            ApiMemberIdentity.GetCanonicalSignature(type, member));
    }

    [Theory]
    [InlineData("void M(System.String text = \"]\", System.Int32 count = 0)")]
    [InlineData("void M(System.String text = \"[\", System.Int32 count = 0)")]
    [InlineData("void M(System.String text = \"<>()\", System.Int32 count = 0)")]
    [InlineData("void M(System.String text = \"a\\\"]b\", System.Int32 count = 0)")]
    [InlineData("void M(System.String text = \"ok\", System.Int32 count = 0)")]
    public void FallbackCanonicalSignature_IgnoresDelimitersInsideStringDefaults(string signature)
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember { Name = "M", Kind = "method", Signature = signature };

        Assert.Equal(
            "M:N.C.M(System.String,System.Int32)",
            ApiMemberIdentity.GetCanonicalSignature(type, member));
    }

    [Fact]
    public void FallbackCanonicalSignature_IgnoresDelimitersInsideCharDefault()
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            Signature = "void M(System.Char sep = ']', System.Int32 count = 0)",
        };

        Assert.Equal(
            "M:N.C.M(System.Char,System.Int32)",
            ApiMemberIdentity.GetCanonicalSignature(type, member));
    }

    [Theory]
    [InlineData("void M)(int a")]
    [InlineData("foo)bar(")]
    [InlineData("void M) int a")]
    public void FallbackCanonicalSignature_DoesNotThrowOnMalformedParentheses(string signature)
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember { Name = "M", Kind = "method", Signature = signature };

        var canonical = ApiMemberIdentity.GetCanonicalSignature(type, member);

        Assert.False(string.IsNullOrEmpty(canonical));
    }

    [Theory]
    [InlineData("void M(SetTree<T> t', FSharpList<T> acc)", "M:N.C.M(SetTree<T>,FSharpList<T>)")]
    [InlineData("void M(System.Int32 x' = 5, System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    [InlineData("void M(System.Int32 x=', System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    public void FallbackCanonicalSignature_TreatsQuotesOutsideDefaultsAsOrdinary(string signature, string expected)
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember { Name = "M", Kind = "method", Signature = signature };

        Assert.Equal(expected, ApiMemberIdentity.GetCanonicalSignature(type, member));
    }

    [Theory]
    // An F# double-backtick identifier can legally contain spaces, '=', and an
    // apostrophe (e.g. ``x = '``), which the formatter emits verbatim. The default
    // separator " = " inside such a name must not cause the following apostrophe to
    // be read as a char literal that swallows the parameter-separating comma. This
    // is compiler-emittable and was handled correctly on main; the fallback must not
    // regress it.
    [InlineData("void M(System.Int32 x = ', System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    [InlineData("void M(System.Int32 x = \", System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    public void FallbackCanonicalSignature_DoesNotInterpretQuotesInDefaultRegion(string signature, string expected)
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember { Name = "M", Kind = "method", Signature = signature };

        Assert.Equal(expected, ApiMemberIdentity.GetCanonicalSignature(type, member));
    }

    [Theory]
    // Brackets outside a leading attribute list must be treated as ordinary text so
    // they never suppress the parameter-separating comma. This covers array types
    // (int[]) and compiler-emittable F# double-backtick names that contain an
    // unmatched bracket (e.g. ``x[``, emitted verbatim as "System.Int32 x["), which
    // main handled and the fallback must not regress. Bracket nesting is tracked
    // only inside the leading "[...]" attribute list.
    [InlineData("void M(System.Int32 x[, System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    [InlineData("void M(System.Int32 x], System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    [InlineData("void M(System.Int32[] a, System.Int32[] b)", "M:N.C.M(System.Int32[],System.Int32[])")]
    public void FallbackCanonicalSignature_TreatsBracketsOutsideAttributesAsOrdinary(string signature, string expected)
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember { Name = "M", Kind = "method", Signature = signature };

        Assert.Equal(expected, ApiMemberIdentity.GetCanonicalSignature(type, member));
    }

    [Theory]
    // main uses a single combined depth counter over '<'/'>'/'('/')' , so an F#
    // quoted name like ``x<)`` (emitted verbatim as "System.Int32 x<)") relies on
    // '<' and ')' cross-cancelling to keep depth at 0 at the separator comma.
    // Splitting that into independent angle/paren counters dropped the second
    // parameter; the fallback must preserve main's combined-counter behavior.
    [InlineData("void M(System.Int32 x<), System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    [InlineData("void M(System.Int32 x)<, System.Int32 y)", "M:N.C.M(System.Int32,System.Int32)")]
    public void FallbackCanonicalSignature_PreservesCombinedAngleParenDepth(string signature, string expected)
    {
        var type = new ApiType { Namespace = "N", Name = "C" };
        var member = new ApiMember { Name = "M", Kind = "method", Signature = signature };

        Assert.Equal(expected, ApiMemberIdentity.GetCanonicalSignature(type, member));
    }

    static (TypeDefinitionHandle TypeHandle, MethodDefinition Method) FindFixtureMethod(MetadataReader reader)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != "ApiMemberIdentityFixture`1")
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == "M")
                    return (typeHandle, method);
            }
        }

        throw new InvalidOperationException("Fixture method not found.");
    }

    static byte[] BuildRepeatedLongMethodNameImage(
        int methodCount,
        int methodNameLength,
        int typeGenericParameterCount = 0)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("LongMethodNames.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("LongMethodNames"),
            new Version(1, 0, 0, 0),
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
        TypeDefinitionHandle type = metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        StringHandle methodName =
            metadata.GetOrAddString(new string('M', methodNameLength));
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        BlobHandle signatureHandle =
            metadata.GetOrAddBlob(signature);
        for (int i = 0; i < methodCount; i++)
        {
            metadata.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static,
                MethodImplAttributes.IL,
                methodName,
                signatureHandle,
                bodyOffset: -1,
                MetadataTokens.ParameterHandle(1));
        }
        for (int i = 0; i < typeGenericParameterCount; i++)
        {
            metadata.AddGenericParameter(
                type,
                GenericParameterAttributes.None,
                default,
                i);
        }

        var pe = new ManagedPEBuilder(
            new PEHeaderBuilder(
                imageCharacteristics:
                    Characteristics.Dll
                    | Characteristics.ExecutableImage),
            new MetadataRootBuilder(metadata),
            ilStream: new BlobBuilder());
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static byte[] BuildConversionOperatorIdentityImage()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("ConversionOperators.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("ConversionOperators"),
            new Version(1, 0, 0, 0),
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
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("C"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));

        var intInstructions = new BlobBuilder();
        var intEncoder = new InstructionEncoder(
            intInstructions,
            new ControlFlowBuilder());
        intEncoder.OpCode(ILOpCode.Ldc_i4_0);
        intEncoder.OpCode(ILOpCode.Ret);
        var longInstructions = new BlobBuilder();
        var longEncoder = new InstructionEncoder(
            longInstructions,
            new ControlFlowBuilder());
        longEncoder.OpCode(ILOpCode.Ldc_i4_0);
        longEncoder.OpCode(ILOpCode.Conv_i8);
        longEncoder.OpCode(ILOpCode.Ret);
        var methodBodies = new BlobBuilder();
        var bodyEncoder = new MethodBodyStreamEncoder(methodBodies);
        int intBodyOffset = bodyEncoder.AddMethodBody(
            intEncoder,
            maxStack: 1);
        int longBodyOffset = bodyEncoder.AddMethodBody(
            longEncoder,
            maxStack: 1);

        BlobHandle intSignature = AddConversionSignature(
            metadata,
            returnElementType: 0x08);
        BlobHandle longSignature = AddConversionSignature(
            metadata,
            returnElementType: 0x0A);
        foreach (string name in
            ApiMemberIdentity.ConversionOperatorNames.Add("op_Addition"))
        {
            StringHandle methodName = metadata.GetOrAddString(name);
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                methodName,
                intSignature,
                intBodyOffset,
                MetadataTokens.ParameterHandle(1));
            metadata.AddMethodDefinition(
                MethodAttributes.Public
                    | MethodAttributes.Static
                    | MethodAttributes.SpecialName
                    | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                methodName,
                longSignature,
                longBodyOffset,
                MetadataTokens.ParameterHandle(1));
        }

        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            methodBodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        return image.ToArray();
    }

    static BlobHandle AddConversionSignature(
        MetadataBuilder metadata,
        byte returnElementType)
    {
        var signature = new BlobBuilder();
        signature.WriteByte(0x00);
        signature.WriteByte(0x01);
        signature.WriteByte(returnElementType);
        signature.WriteByte(0x12);
        // TypeDef row 2 encoded as a TypeDefOrRef coded index.
        signature.WriteByte(0x08);
        return metadata.GetOrAddBlob(signature);
    }

    static void AssertProjectionStageExhaustion(
        int workRemaining,
        string expectedStage)
    {
        byte[] image = BuildRepeatedLongMethodNameImage(
            methodCount: 1,
            methodNameLength: 32);
        using var peReader = new PEReader(new MemoryStream(image));
        MetadataReader reader = peReader.GetMetadataReader();
        TypeDefinitionHandle typeHandle =
            reader.TypeDefinitions.Last();
        MethodDefinition method =
            reader.GetMethodDefinition(
                reader.GetTypeDefinition(typeHandle).GetMethods().Single());

        BadImageFormatException ex =
            Assert.Throws<BadImageFormatException>(
                () => ApiMemberIdentity.CreateMethodAnchorInfo(
                    reader,
                    typeHandle,
                    method,
                    ref workRemaining,
                    isExtensionMethod: true));

        Assert.Contains(
            $"member anchor {expectedStage} exceeds",
            ex.Message,
            StringComparison.Ordinal);
        Assert.Equal(0, workRemaining);
    }

    static List<(TypeDefinitionHandle TypeHandle, MethodDefinition Method)> FindConversionOperatorMethods(MetadataReader reader)
    {
        var result = new List<(TypeDefinitionHandle, MethodDefinition)>();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetString(type.Name) != nameof(ConversionOperatorFixture))
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == "op_Explicit")
                    result.Add((typeHandle, method));
            }
        }

        return result;
    }

    sealed class ApiMemberIdentityFixture<T>
    {
        public void M<U>(int value, U item)
        {
        }
    }

    readonly struct ConversionOperatorFixture
    {
        public static explicit operator int(ConversionOperatorFixture value) => 0;

        public static explicit operator long(ConversionOperatorFixture value) => 0;
    }

    sealed class IndexerFixture
    {
        public int this[int index] => index;

        public int this[string key] => key.Length;
    }

    sealed class AttributedParameterFixture
    {
        public void M(
            [System.Runtime.InteropServices.Optional]
            [System.Runtime.CompilerServices.DateTimeConstant(630822816000000000L)]
            DateTime when,
            int count)
        {
        }

        public int this[
            [System.Runtime.InteropServices.Optional]
            [System.Runtime.CompilerServices.DateTimeConstant(630822816000000000L)]
            DateTime when] => when.Year;
    }
}
