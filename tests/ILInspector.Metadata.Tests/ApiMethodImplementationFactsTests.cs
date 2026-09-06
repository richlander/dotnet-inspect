using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using ILInspector.Metadata.MemorySafetyFixtures;

namespace ILInspector.Metadata.Tests;

public sealed class ApiMethodImplementationFactsTests
{
    static string FixturePath => typeof(MethodImplementationFixtures).Assembly.Location;
    static readonly Lazy<ApiSurface> Surface = new(() =>
    {
        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(pe, includeAll: true);
    });

    [Theory]
    [InlineData(".ctor", false, false, false, true)]
    [InlineData("Managed", false, false, false, true)]
    [InlineData("Abstract", true, false, false, false)]
    [InlineData("Native", false, true, false, false)]
    [InlineData("InternalCall", false, false, true, false)]
    public void MethodFlagsDistinguishDeclarationsWithAndWithoutBodies(
        string name, bool isAbstract, bool pinvoke, bool internalCall, bool hasRva)
    {
        ApiMember member = Member(Type(nameof(MethodImplementationFixtures)), name);
        var facts = Assert.IsType<ApiMethodImplementationFacts>(member.MethodImplementation);
        Assert.Equal(isAbstract, (facts.Attributes & MethodAttributes.Abstract) != 0);
        Assert.Equal(pinvoke, (facts.Attributes & MethodAttributes.PinvokeImpl) != 0);
        Assert.Equal(internalCall, (facts.ImplAttributes & MethodImplAttributes.InternalCall) != 0);
        Assert.Equal(hasRva, facts.HasBodyRva);
        Assert.Equal(member.HasMethodBody, facts.HasBodyRva);
        Assert.Equal(member.MetadataToken, facts.MethodToken);
        AssertMetadata(facts);
    }

    [Theory]
    [InlineData(".ctor")]
    [InlineData("Invoke")]
    public void RuntimeProvidedDelegateMethodsRetainTheirCodeType(string name)
    {
        var facts = Member(Type(nameof(RuntimeMethodImplementationFixture)), name)
            .MethodImplementation!;
        Assert.Equal(MethodImplAttributes.Runtime, facts.ImplAttributes & MethodImplAttributes.CodeTypeMask);
        Assert.False(facts.HasBodyRva);
        AssertMetadata(facts);
    }

    [Theory]
    [InlineData("Property", false)]
    [InlineData("AbstractProperty", true)]
    [InlineData("Event", false)]
    [InlineData("AbstractEvent", true)]
    public void AccessorsRetainTheirOwnFlagsAndTokens(string name, bool isAbstract)
    {
        ApiMember member = Member(Type(nameof(MethodImplementationFixtures)), name);
        Assert.Null(member.MethodImplementation);
        var facts = member.AccessorImplementations!.Value;
        Assert.Equal(2, facts.Length);
        Assert.Equal(2, facts.Select(fact => fact.MethodToken).Distinct().Count());
        Assert.Contains(facts, fact => fact.MethodToken == (member.GetterToken ?? member.AdderToken));
        Assert.Contains(facts, fact => fact.MethodToken == (member.SetterToken ?? member.RemoverToken));
        Assert.All(facts, fact =>
        {
            Assert.Equal(isAbstract, (fact.Attributes & MethodAttributes.Abstract) != 0);
            Assert.Equal(!isAbstract, fact.HasBodyRva);
            AssertMetadata(fact);
        });
        if (name == "Property")
        {
            Assert.Equal(MethodAttributes.Private,
                facts.Single(fact => fact.MethodToken == member.SetterToken).Attributes
                    & MethodAttributes.MemberAccessMask);
        }
    }

    [Fact]
    public void ExtensionProjectionKeepsDeclarationEvidence()
    {
        ApiMember original = Member(Type(nameof(ILInspector.Metadata.MemorySafetyFixtures.MemorySafetyFixtures)), "ExtensionContract");
        ApiMember projected = Member(Type(nameof(MemorySafetyDeclarationFixtures)), "ExtensionContract");
        Assert.Equal("extension-method", projected.Kind);
        Assert.NotNull(original.MethodImplementation);
        Assert.Same(original.MethodImplementation, projected.MethodImplementation);
        Assert.Equal(original.HasMethodBody, projected.HasMethodBody);
    }

    [Theory]
    [InlineData(MethodAttributes.Public, false, false, false, false, false, false)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Static, false, true, false, false, false, false)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot, false, false, true, false, false, false)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Abstract, false, false, true, true, false, false)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual, false, false, true, false, true, false)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final, false, false, true, false, true, true)]
    [InlineData(MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final, false, false, true, false, false, false)]
    [InlineData(MethodAttributes.Private | MethodAttributes.Virtual | MethodAttributes.Final, true, false, true, false, false, false)]
    public void AccessorProjectionUsesExactMethodDefModifiers(
        MethodAttributes attributes,
        bool isExplicit,
        bool isStatic,
        bool isVirtual,
        bool isAbstract,
        bool isOverride,
        bool isSealed)
    {
        var owner = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            GetterToken = 0x06000001,
            SignatureModel = new ApiSignature
            {
                ReturnType = "int",
                Accessors =
                [
                    new ApiAccessor
                    {
                        Kind = "get",
                        IsExplicitInterfaceImplementation = isExplicit,
                    },
                ],
            },
            AccessorImplementations =
            [
                new ApiMethodImplementationFacts(
                    Guid.NewGuid(),
                    0x06000001,
                    attributes,
                    MethodImplAttributes.IL,
                    !isAbstract),
            ],
        };

        ApiMember accessor = Assert.Single(
            ApiMemberAccessors.Create(owner, new ApiType { Name = "Target" }));
        Assert.Equal(isStatic, accessor.IsStatic);
        Assert.Equal(isVirtual, accessor.IsVirtual);
        Assert.Equal(isAbstract, accessor.IsAbstract);
        Assert.Equal(isOverride, accessor.IsOverride);
        Assert.Equal(isSealed, accessor.IsSealed);
    }

    [Fact]
    public void AccessorProjectionWithoutMethodDefFactsKeepsLegacyModifierFallback()
    {
        var owner = new ApiMember
        {
            Name = "Value",
            Kind = "property",
            GetterToken = 0x06000001,
            GetterHasMethodBody = true,
            IsVirtual = true,
            IsAbstract = true,
        };

        ApiMember accessor = Assert.Single(
            ApiMemberAccessors.Create(owner, new ApiType { Name = "Target" }));
        Assert.True(accessor.IsVirtual);
        Assert.False(accessor.IsAbstract);
        Assert.Null(accessor.MethodImplementation);
    }

    [Fact]
    public void HandleBasedSurfaceRetainsMethodAndAccessorEvidence()
    {
        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var handle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == nameof(MethodImplementationFixtures));
        ApiType type = MetadataDeclarationQuery.GetTypeSurface(reader, handle, includeNonPublicMembers: true);
        Assert.Equal(Member(Type(nameof(MethodImplementationFixtures)), "Native").MethodImplementation,
            Member(type, "Native").MethodImplementation);
        Assert.Equal(Member(Type(nameof(MethodImplementationFixtures)), "Property").AccessorImplementations!.Value,
            Member(type, "Property").AccessorImplementations!.Value);
    }

    [Fact]
    public void ReferenceAssemblyRvaIsNotAnExternClassification()
    {
        string? path = MethodHasBodyTests.FindReferenceAssembly();
        Assert.SkipWhen(path is null, "No targeting-pack reference assembly available.");
        using var stream = File.OpenRead(path!);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        var handle = reader.TypeDefinitions.Single(handle =>
        {
            var type = reader.GetTypeDefinition(handle);
            return reader.StringComparer.Equals(type.Namespace, "System")
                && reader.StringComparer.Equals(type.Name, "Object");
        });
        ApiType type = MetadataDeclarationQuery.GetTypeSurface(reader, handle);
        var facts = Member(type, "ToString").MethodImplementation!;
        Assert.Equal(0, (int)(facts.Attributes & (MethodAttributes.Abstract | MethodAttributes.PinvokeImpl)));
        Assert.Equal(MethodImplAttributes.IL, facts.ImplAttributes & MethodImplAttributes.CodeTypeMask);
        Assert.Equal(reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(facts.MethodToken)).RelativeVirtualAddress != 0,
            facts.HasBodyRva);
    }

    [Fact]
    public void FactsSurviveReaderDisposalAndJson()
    {
        ApiSurface original = Surface.Value;
        ApiSurface restored = JsonSerializer.Deserialize<ApiSurface>(JsonSerializer.Serialize(original))!;
        foreach (ApiType type in restored.Types)
        {
            ApiType expectedType = original.Types.Single(candidate => candidate.FullName == type.FullName);
            foreach (ApiMember member in type.Members)
            {
                ApiMember expected = expectedType.Members.Single(candidate =>
                    candidate.Name == member.Name && candidate.Signature == member.Signature);
                Assert.Equal(expected.MethodImplementation, member.MethodImplementation);
                Assert.Equal(expected.AccessorImplementations?.ToArray(), member.AccessorImplementations?.ToArray());
            }
        }
    }

    [Fact]
    public void MissingFactsRemainUnavailableRatherThanOrdinaryIL()
    {
        ApiMember member = JsonSerializer.Deserialize<ApiMember>("""{"Name":"Old","HasMethodBody":false}""")!;
        Assert.Null(member.MethodImplementation);
        Assert.Null(member.AccessorImplementations);
        Assert.DoesNotContain("MethodImplementation", JsonSerializer.Serialize(member));
        ApiMember field = Member(Type(nameof(MethodImplementationFixtures)), "Field");
        Assert.Null(field.MethodImplementation);
        Assert.Null(field.AccessorImplementations);
    }

    [Fact]
    public void CompactSummaryDoesNotInventImplementationEvidence()
    {
        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        ApiSurface summary = ApiSurfaceExtractor.ExtractSummary(pe);
        Assert.NotEmpty(summary.Types);
        Assert.All(summary.Types.SelectMany(type => type.Members), member =>
        {
            Assert.Null(member.MethodImplementation);
            Assert.Null(member.AccessorImplementations);
        });
    }

    static void AssertMetadata(ApiMethodImplementationFacts facts)
    {
        using var stream = File.OpenRead(FixturePath);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        MethodDefinition method = reader.GetMethodDefinition(
            (MethodDefinitionHandle)MetadataTokens.EntityHandle(facts.MethodToken));
        Assert.Equal(reader.GetGuid(reader.GetModuleDefinition().Mvid), facts.ModuleVersionId);
        Assert.Equal(method.Attributes, facts.Attributes);
        Assert.Equal(method.ImplAttributes, facts.ImplAttributes);
        Assert.Equal(method.RelativeVirtualAddress != 0, facts.HasBodyRva);
    }

    static ApiType Type(string name) => Surface.Value.Types.Single(type => type.Name == name);
    static ApiMember Member(ApiType type, string name) => type.Members.Single(member => member.Name == name);
}
