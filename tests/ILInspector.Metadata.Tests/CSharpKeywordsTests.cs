using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace ILInspector.Metadata.Tests;

public sealed class CSharpKeywordsTests
{
    public static TheoryData<string> DeclarationContextualKeywords => new()
    {
        "await", "file", "init", "record", "required", "scoped",
    };

    [Theory]
    [MemberData(nameof(DeclarationContextualKeywords))]
    public void DeclarationPolicy_EscapesConservativeContextualSet(string identifier)
        => Assert.True(CSharpKeywords.RequiresDeclarationEscape(identifier));

    [Theory]
    [InlineData("await", true)]
    [InlineData("file", false)]
    [InlineData("init", false)]
    [InlineData("record", false)]
    [InlineData("required", false)]
    [InlineData("scoped", false)]
    public void BodyPolicy_PreservesDeclarationOnlyContextualIdentifiers(string identifier, bool expected)
        => Assert.Equal(expected, CSharpKeywords.RequiresBodyEscape(identifier));

    [Fact]
    public void ApiSurfaceAndDeclarationQuery_UseDeclarationPolicyForContextualNames()
    {
        using var peReader = new PEReader(File.OpenRead(typeof(ContextualKeywordFixture).Assembly.Location));
        var reader = peReader.GetMetadataReader();
        var typeHandle = reader.TypeDefinitions.Single(handle =>
            reader.GetString(reader.GetTypeDefinition(handle).Name) == nameof(ContextualKeywordFixture));
        var type = reader.GetTypeDefinition(typeHandle);

        ApiType apiType = Assert.Single(
            ApiSurfaceExtractor.Extract(peReader, includeAll: true).Types,
            candidate => candidate.Name == nameof(ContextualKeywordFixture));

        foreach (string keyword in DeclarationContextualKeywords)
        {
            Assert.Contains(apiType.Members, member =>
                member.Name == keyword
                && member.Signature?.Contains($"int @{keyword}", StringComparison.Ordinal) == true);

            var methodHandle = type.GetMethods().Single(handle =>
                reader.GetString(reader.GetMethodDefinition(handle).Name) == keyword);
            var declaration = MetadataDeclarationQuery.GetMethod(
                reader,
                type,
                reader.GetMethodDefinition(methodHandle));
            Assert.Equal($"@{keyword}", declaration.CSharpName);
        }
    }
}

public sealed class ContextualKeywordFixture
{
    public void @await(int @await) { }
    public void @file(int @file) { }
    public void @init(int @init) { }
    public void @record(int @record) { }
    public void @required(int @required) { }
    public void @scoped(int @scoped) { }
}
