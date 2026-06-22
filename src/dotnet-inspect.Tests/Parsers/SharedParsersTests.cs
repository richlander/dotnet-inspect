using DotnetInspector.CommandLine;
using System.CommandLine;

namespace DotnetInspector.Tests.Parsers;

public class SharedParsersTests
{
    // ── ParseMemberFilter ────────────────────────────────────────────────

    [Fact]
    public void ParseMemberFilter_EmptyArray_ReturnsEmptyFilterAndNullLimit()
    {
        var (filter, limit) = SharedParsers.ParseMemberFilter([]);

        Assert.Empty(filter);
        Assert.Null(limit);
    }

    [Fact]
    public void ParseMemberFilter_SingleNumber_ReturnsLimit()
    {
        var (filter, limit) = SharedParsers.ParseMemberFilter(["5"]);

        Assert.Empty(filter);
        Assert.Equal(5, limit);
    }

    [Fact]
    public void ParseMemberFilter_SingleName_ReturnsFilter()
    {
        var (filter, limit) = SharedParsers.ParseMemberFilter(["GetValue"]);

        Assert.Single(filter);
        Assert.Contains("GetValue", filter);
        Assert.Null(limit);
    }

    [Fact]
    public void ParseMemberFilter_MultipleNames_ReturnsAllInFilter()
    {
        var (filter, limit) = SharedParsers.ParseMemberFilter(["GetValue", "SetValue", "Clear"]);

        Assert.Equal(3, filter.Count);
        Assert.Contains("GetValue", filter);
        Assert.Contains("SetValue", filter);
        Assert.Contains("Clear", filter);
        Assert.Null(limit);
    }

    [Fact]
    public void ParseMemberFilter_CaseInsensitive()
    {
        var (filter, _) = SharedParsers.ParseMemberFilter(["GetValue"]);

        Assert.Contains("getvalue", filter);
        Assert.Contains("GETVALUE", filter);
    }

    // ── ParseOverloadShorthand ───────────────────────────────────────────

    [Fact]
    public void ParseOverloadShorthand_NoColon_ReturnsNameOnly()
    {
        var (name, index) = SharedParsers.ParseOverloadShorthand("GetValue");

        Assert.Equal("GetValue", name);
        Assert.Null(index);
    }

    [Fact]
    public void ParseOverloadShorthand_WithIndex_ReturnsNameAndIndex()
    {
        var (name, index) = SharedParsers.ParseOverloadShorthand("GetValue:3");

        Assert.Equal("GetValue", name);
        Assert.Equal(3, index);
    }

    [Fact]
    public void ParseOverloadShorthand_ColonAtStart_ReturnsOriginal()
    {
        var (name, index) = SharedParsers.ParseOverloadShorthand(":123");

        Assert.Equal(":123", name);
        Assert.Null(index);
    }

    [Fact]
    public void ParseOverloadShorthand_ColonWithNonNumeric_ReturnsOriginal()
    {
        var (name, index) = SharedParsers.ParseOverloadShorthand("Type:Name");

        Assert.Equal("Type:Name", name);
        Assert.Null(index);
    }

    [Fact]
    public void ParseOverloadShorthand_MultipleColons_UsesLastOne()
    {
        var (name, index) = SharedParsers.ParseOverloadShorthand("Some:Complex:Name:2");

        Assert.Equal("Some:Complex:Name", name);
        Assert.Equal(2, index);
    }

    // ── ParseDottedMember ────────────────────────────────────────────────

    [Fact]
    public void ParseDottedMember_NoDot_ReturnsMemberOnly()
    {
        var (typeFilter, member) = SharedParsers.ParseDottedMember("GetValue");

        Assert.Null(typeFilter);
        Assert.Equal("GetValue", member);
    }

    [Fact]
    public void ParseDottedMember_WithDot_SplitsTypeAndMember()
    {
        var (typeFilter, member) = SharedParsers.ParseDottedMember("JsonSerializer.Deserialize");

        Assert.Equal("JsonSerializer", typeFilter);
        Assert.Equal("Deserialize", member);
    }

    [Fact]
    public void ParseDottedMember_GlobPattern_DoesNotSplit()
    {
        var (typeFilter, member) = SharedParsers.ParseDottedMember("*.GetValue");

        Assert.Null(typeFilter);
        Assert.Equal("*.GetValue", member);
    }

    [Fact]
    public void ParseDottedMember_QuestionMarkPattern_DoesNotSplit()
    {
        var (typeFilter, member) = SharedParsers.ParseDottedMember("Type?.Member");

        Assert.Null(typeFilter);
        Assert.Equal("Type?.Member", member);
    }

    [Fact]
    public void ParseDottedMember_DotAtStart_DoesNotSplit()
    {
        var (typeFilter, member) = SharedParsers.ParseDottedMember(".ctor");

        Assert.Null(typeFilter);
        Assert.Equal(".ctor", member);
    }

    [Fact]
    public void ParseDottedMember_MultipleDots_UsesLastOne()
    {
        var (typeFilter, member) = SharedParsers.ParseDottedMember("System.Text.Json.JsonSerializer.Deserialize");

        Assert.Equal("System.Text.Json.JsonSerializer", typeFilter);
        Assert.Equal("Deserialize", member);
    }

    // ── SplitTrailingMember ───────────────────────────────────────────────

    [Fact]
    public void SplitTrailingMember_SplitsSimpleMember()
    {
        var (typeName, memberName) = SharedParsers.SplitTrailingMember("System.Text.Json.JsonSerializer.Deserialize");

        Assert.Equal("System.Text.Json.JsonSerializer", typeName);
        Assert.Equal("Deserialize", memberName);
    }

    [Fact]
    public void SplitTrailingMember_DoesNotSplitGenericTypeSuffix()
    {
        var (typeName, memberName) = SharedParsers.SplitTrailingMember("System.Collections.Generic.List<T>");

        Assert.Equal("System.Collections.Generic.List<T>", typeName);
        Assert.Null(memberName);
    }

    [Fact]
    public void SplitTrailingMember_NoDot_ReturnsOriginal()
    {
        var (typeName, memberName) = SharedParsers.SplitTrailingMember("JsonSerializer");

        Assert.Equal("JsonSerializer", typeName);
        Assert.Null(memberName);
    }

    // ── ParseTypeFilter ──────────────────────────────────────────────────

    [Fact]
    public void ParseTypeFilter_Null_ReturnsNulls()
    {
        var (filter, limit) = SharedParsers.ParseTypeFilter(null);

        Assert.Null(filter);
        Assert.Null(limit);
    }

    [Fact]
    public void ParseTypeFilter_Number_ReturnsLimit()
    {
        var (filter, limit) = SharedParsers.ParseTypeFilter("10");

        Assert.Null(filter);
        Assert.Equal(10, limit);
    }

    [Fact]
    public void ParseTypeFilter_Pattern_ReturnsFilter()
    {
        var (filter, limit) = SharedParsers.ParseTypeFilter("*Json*");

        Assert.Equal("*Json*", filter);
        Assert.Null(limit);
    }

    // ── ParsePackageVersion ──────────────────────────────────────────────

    [Fact]
    public void ParsePackageVersion_BarePackage_ReturnsNameOnly()
    {
        var info = SharedParsers.ParsePackageVersion("System.Text.Json");

        Assert.Equal("System.Text.Json", info.BareName);
        Assert.Null(info.ExplicitVersion);
        Assert.False(info.HasExplicitVersion);
        Assert.False(info.ForceLatest);
    }

    [Fact]
    public void ParsePackageVersion_WithVersion_ReturnsNameAndVersion()
    {
        var info = SharedParsers.ParsePackageVersion("System.Text.Json@9.0.0");

        Assert.Equal("System.Text.Json", info.BareName);
        Assert.Equal("9.0.0", info.ExplicitVersion);
        Assert.True(info.HasExplicitVersion);
        Assert.False(info.ForceLatest);
    }

    [Fact]
    public void ParsePackageVersion_AtLatest_SetsForceLatest()
    {
        var info = SharedParsers.ParsePackageVersion("System.Text.Json@latest");

        Assert.Equal("System.Text.Json", info.BareName);
        Assert.Null(info.ExplicitVersion);
        Assert.False(info.HasExplicitVersion);
        Assert.True(info.ForceLatest);
    }

    [Fact]
    public void ParsePackageVersion_AtLatestCaseInsensitive()
    {
        var info = SharedParsers.ParsePackageVersion("System.Text.Json@LATEST");

        Assert.True(info.ForceLatest);
    }

    // ── ProcessMemberArguments ───────────────────────────────────────────

    [Fact]
    public void ProcessMemberArguments_ExtractsDottedSyntax()
    {
        var members = new[] { "JsonSerializer.Deserialize", "GetValue" };
        var (typeFilter, overloadIndex, memberDigest, kindFilter) = SharedParsers.ProcessMemberArguments(members);

        Assert.Equal("JsonSerializer", typeFilter);
        Assert.Equal("Deserialize", members[0]);
        Assert.Equal("GetValue", members[1]);
        Assert.Null(overloadIndex);
        Assert.Null(memberDigest);
        Assert.Empty(kindFilter);
    }

    [Fact]
    public void ProcessMemberArguments_ExtractsOverloadShorthand()
    {
        var members = new[] { "GetValue:2" };
        var (typeFilter, overloadIndex, memberDigest, kindFilter) = SharedParsers.ProcessMemberArguments(members);

        Assert.Null(typeFilter);
        Assert.Equal("GetValue", members[0]);
        Assert.Equal(2, overloadIndex);
        Assert.Null(memberDigest);
        Assert.Empty(kindFilter);
    }

    [Fact]
    public void ProcessMemberArguments_ExtractsBoth()
    {
        var members = new[] { "JsonSerializer.Deserialize:1" };
        var (typeFilter, overloadIndex, memberDigest, kindFilter) = SharedParsers.ProcessMemberArguments(members);

        Assert.Equal("JsonSerializer", typeFilter);
        Assert.Equal("Deserialize", members[0]);
        Assert.Equal(1, overloadIndex);
        Assert.Null(memberDigest);
        Assert.Empty(kindFilter);
    }

    [Fact]
    public void ProcessMemberArguments_ExtractsKindQualifiedSelector()
    {
        var members = new[] { "explicit:System.IConvertible.ToBoolean:1" };
        var (typeFilter, overloadIndex, memberDigest, kindFilter) = SharedParsers.ProcessMemberArguments(members);

        Assert.Null(typeFilter);
        Assert.Equal("System.IConvertible.ToBoolean", members[0]);
        Assert.Equal(1, overloadIndex);
        Assert.Null(memberDigest);
        Assert.Contains("explicit-interface-implementation", kindFilter);
    }

    [Fact]
    public void ProcessMemberArguments_ExtractsDigestSelector()
    {
        var members = new[] { "JsonSerializer.Deserialize~abc123" };
        var (typeFilter, overloadIndex, memberDigest, kindFilter) = SharedParsers.ProcessMemberArguments(members);

        Assert.Equal("JsonSerializer", typeFilter);
        Assert.Equal("Deserialize", members[0]);
        Assert.Null(overloadIndex);
        Assert.Equal("abc123", memberDigest);
        Assert.Empty(kindFilter);
    }

    // ── Source selection ──────────────────────────────────────────────────

    [Fact]
    public void ReadSourceSelectionInputs_ExplicitPackage_IsExplicitSource()
    {
        var root = new RootCommand();
        var argsArg = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore };
        var packageOption = new Option<string?>("--package");
        var assemblyOption = new Option<string?>("--library");
        var platformOption = new Option<string?>("--platform");
        root.Arguments.Add(argsArg);
        root.Options.Add(packageOption);
        root.Options.Add(assemblyOption);
        root.Options.Add(platformOption);

        var result = root.Parse(["JsonSerializer", "--package", "System.Text.Json"]);
        var inputs = SharedParsers.ReadSourceSelectionInputs(
            result, argsArg, packageOption, assemblyOption, platformOption);

        Assert.True(inputs.HasExplicitSource);
        Assert.False(inputs.IsLibrarySelector);
        Assert.Equal(["JsonSerializer"], inputs.Args);
        Assert.Equal("System.Text.Json", inputs.ExplicitPackage);
    }

    [Fact]
    public void ReadSourceSelectionInputs_LibrarySelector_IsNotExplicitSource()
    {
        var root = new RootCommand();
        var argsArg = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore };
        var packageOption = new Option<string?>("--package");
        var assemblyOption = new Option<string?>("--library");
        var platformOption = new Option<string?>("--platform");
        root.Arguments.Add(argsArg);
        root.Options.Add(packageOption);
        root.Options.Add(assemblyOption);
        root.Options.Add(platformOption);

        var result = root.Parse(["JsonSerializer", "--library", "System.Text.Json.dll"]);
        var inputs = SharedParsers.ReadSourceSelectionInputs(
            result, argsArg, packageOption, assemblyOption, platformOption);

        Assert.True(inputs.IsLibrarySelector);
        Assert.False(inputs.HasExplicitSource);
        Assert.Equal("System.Text.Json.dll", inputs.ExplicitAssembly);
    }
}
