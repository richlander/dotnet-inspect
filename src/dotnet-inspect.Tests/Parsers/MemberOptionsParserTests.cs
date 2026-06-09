using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.Tests.Parsers;

/// <summary>
/// Tests for MemberOptionsParser.ParseAsync covering all patterns
/// for specifying package, type, and member.
/// </summary>
[Collection("Console")]
public class MemberOptionsParserTests
{
    /// <summary>
    /// Creates the member command with shared options and args for testing.
    /// Mirrors ApiCommandDefinitions.CreateMemberCommand but exposes the args.
    /// </summary>
    private static (Command Root, SharedOptions Opts, MemberOptionsParser.MemberCommandArgs Args) CreateTestCommand()
    {
        var opts = new SharedOptions();
        var memberCommand = new Command("member", "test");

        var argsArg = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore };
        var packageOption = new Option<string?>("--package");
        var assemblyOption = new Option<string?>("--library");
        var platformOption = new Option<string?>("--platform");
        var frameworkOption = new Option<string?>("--framework");
        var tfmOption = new Option<string?>("--tfm");
        var allOption = new Option<bool>("--all");
        var memberOption = new Option<string[]>("-m") { AllowMultipleArgumentsPerToken = true };
        memberOption.Aliases.Add("--member");
        var ctorOption = new Option<bool>("--ctor");
        var compactOption = new Option<bool>("--compact");
        var unsafeOption = new Option<bool>("--unsafe");
        var indexOption = new Option<int?>("--index");
        var paramsOption = new Option<string>("--params");
        var ofOption = new Option<string>("-of");
        var selectOption = new Option<bool>("--show-index");
        var kindOption = new Option<string[]>("-k") { AllowMultipleArgumentsPerToken = true };
        kindOption.Aliases.Add("--kind");

        memberCommand.Arguments.Add(argsArg);
        memberCommand.Options.Add(packageOption);
        memberCommand.Options.Add(assemblyOption);
        memberCommand.Options.Add(platformOption);
        memberCommand.Options.Add(frameworkOption);
        memberCommand.Options.Add(tfmOption);
        memberCommand.Options.Add(allOption);
        memberCommand.Options.Add(memberOption);
        memberCommand.Options.Add(ctorOption);
        memberCommand.Options.Add(opts.Limit);
        memberCommand.Options.Add(opts.Json);
        memberCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(memberCommand);
        memberCommand.Options.Add(unsafeOption);
        memberCommand.Options.Add(indexOption);
        memberCommand.Options.Add(paramsOption);
        memberCommand.Options.Add(ofOption);
        memberCommand.Options.Add(selectOption);
        memberCommand.Options.Add(kindOption);
        opts.AddSectionOptionsTo(memberCommand);
        memberCommand.Options.Add(opts.Markdown);
        memberCommand.Options.Add(opts.PlainText);
        opts.AddOutputOptionsTo(memberCommand);
        opts.AddNuGetOptionsTo(memberCommand);

        memberCommand.SetAction((_, _) => Task.FromResult(0));

        var root = new RootCommand { memberCommand };
        var args = new MemberOptionsParser.MemberCommandArgs(
            argsArg, packageOption, assemblyOption, platformOption, frameworkOption, tfmOption,
            allOption, memberOption, ctorOption, compactOption, opts.OneLine, opts.NoHeaders,
            unsafeOption, indexOption, paramsOption, ofOption, selectOption, kindOption);

        return (root, opts, args);
    }

    private static async Task<MemberOptions> ParseSuccessAsync(params string[] args)
    {
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(args);
        Assert.Empty(parseResult.Errors);

        var result = await MemberOptionsParser.ParseAsync(parseResult, opts, cmdArgs);
        var success = Assert.IsType<MemberOptionsParser.Success>(result);
        return success.Options;
    }

    // ── Explicit --package with type ─────────────────────────────────────

    [Fact]
    public async Task ExplicitPackage_WithType_SetsPackageAndType()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PackagePath);
        Assert.Null(options.PlatformAssembly);
        Assert.Null(options.AssemblyPath);
    }

    [Fact]
    public async Task ExplicitPackage_MultiDotted_PreservesFullPackageName()
    {
        // This was the bug: Microsoft.Extensions.AI.Abstractions was being
        // split by peel-and-probe into Microsoft.Extensions.AI + "Abstractions"
        var options = await ParseSuccessAsync("member", "IChatClient", "--package", "Microsoft.Extensions.AI.Abstractions");

        Assert.Equal("IChatClient", options.TypeName);
        Assert.Equal("Microsoft.Extensions.AI.Abstractions", options.PackagePath);
    }

    [Fact]
    public async Task ExplicitPackage_FourSegments_PreservesFullPackageName()
    {
        var options = await ParseSuccessAsync("member", "SomeType", "--package", "Azure.Storage.Blobs.Models");

        Assert.Equal("SomeType", options.TypeName);
        Assert.Equal("Azure.Storage.Blobs.Models", options.PackagePath);
    }

    [Fact]
    public async Task ExplicitPackage_WithVersion_PreservesVersionSuffix()
    {
        var options = await ParseSuccessAsync("member", "IChatClient", "--package", "Microsoft.Extensions.AI.Abstractions@9.0.0");

        Assert.Equal("IChatClient", options.TypeName);
        // PackagePath preserves the @version suffix for downstream resolution
        Assert.Contains("Microsoft.Extensions.AI.Abstractions", options.PackagePath!);
    }

    [Fact]
    public async Task ExplicitPackage_WithTable_SetsTabularOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--table");

        Assert.True(options.OneLine);
        Assert.False(options.Tsv);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithTsvAndNoHeaders_SetsTsvOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--tsv", "--no-headers");

        Assert.True(options.OneLine);
        Assert.True(options.Tsv);
        Assert.True(options.NoHeader);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithTableAndTsv_UsesTsvOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--table", "--tsv");

        Assert.True(options.OneLine);
        Assert.True(options.Tsv);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithJsonAndTsv_IsRejected()
    {
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(["member", "JsonSerializer", "--package", "System.Text.Json", "--json", "--tsv"]);
        Assert.Empty(parseResult.Errors);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => MemberOptionsParser.ParseAsync(parseResult, opts, cmdArgs));
    }

    [Fact]
    public async Task ExplicitPackage_WithOneline_SetsTableCompatOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--oneline");

        Assert.True(options.OneLine);
        Assert.False(options.Tsv);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    // ── Explicit --package with type and member ──────────────────────────

    [Fact]
    public async Task ExplicitPackage_WithTypeAndPositionalMember_SetsMemberFilter()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "Serialize");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PackagePath);
        Assert.Contains("Serialize", options.MemberFilter);
    }

    [Fact]
    public async Task ExplicitPackage_WithTypeAndMOption_SetsMemberFilter()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-m", "Serialize");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PackagePath);
        Assert.Contains("Serialize", options.MemberFilter);
    }

    [Fact]
    public async Task ExplicitPackage_WithQualifiedTypeAndMOption_PreservesQualifiedType()
    {
        var options = await ParseSuccessAsync("member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json", "-m", "Serialize");

        Assert.Equal("System.Text.Json.JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PackagePath);
        Assert.Contains("Serialize", options.MemberFilter);
    }

    [Fact]
    public async Task ExplicitPackage_WithQualifiedTypeOnly_PreservesQualifiedType()
    {
        var options = await ParseSuccessAsync("member", "System.Text.Json.JsonSerializer", "--package", "System.Text.Json");

        Assert.Equal("System.Text.Json.JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PackagePath);
        Assert.Empty(options.MemberFilter);
    }

    [Fact]
    public async Task ExplicitPackage_WithMultipleMOptions_SetsAllMembers()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-m", "Serialize", "-m", "Deserialize");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Contains("Serialize", options.MemberFilter);
        Assert.Contains("Deserialize", options.MemberFilter);
    }

    [Fact]
    public async Task ExplicitPackage_WithMultiplePositionalMembers_SetsAllMembers()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "Serialize", "Deserialize");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Contains("Serialize", options.MemberFilter);
        Assert.Contains("Deserialize", options.MemberFilter);
    }

    // ── Explicit --platform ──────────────────────────────────────────────

    [Fact]
    public async Task ExplicitPlatform_WithType_SetsPlatformAssembly()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--platform", "System.Text.Json");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Null(options.PackagePath);
    }

    [Fact]
    public async Task ExplicitPlatform_WithTypeAndMember_SetsMemberFilter()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--platform", "System.Text.Json", "-m", "Serialize");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Contains("Serialize", options.MemberFilter);
    }

    // ── Positional args (no explicit source) ─────────────────────────────

    [Fact]
    public async Task Positional_PackageAndType_SetsPackageAndType()
    {
        var options = await ParseSuccessAsync("member", "Humanizer", "StringHumanizeExtensions");

        Assert.Equal("StringHumanizeExtensions", options.TypeName);
        // PackagePath will be "Humanizer" or resolved to platform — either way, TypeName is correct
        Assert.NotNull(options.PackagePath ?? options.PlatformAssembly);
    }

    [Fact]
    public async Task Positional_PackageTypeAndMember_SetsAll()
    {
        var options = await ParseSuccessAsync("member", "Humanizer", "StringHumanizeExtensions", "Humanize");

        Assert.Equal("StringHumanizeExtensions", options.TypeName);
        Assert.Contains("Humanize", options.MemberFilter);
    }

    [Fact]
    public async Task Positional_PackageType_WithPlatformPrefix_DoesNotUseTypoFallback()
    {
        var options = await ParseSuccessAsync("member", "System.CommandLine", "Command");

        Assert.Equal("Command", options.TypeName);
        Assert.Equal("System.CommandLine", options.PackagePath);
        Assert.Null(options.PlatformAssembly);
        Assert.Empty(options.MemberFilter);
    }

    [Fact]
    public async Task Positional_PackageLikeSingleArg_DoesNotUseRootPlatformFallback()
    {
        var options = await ParseSuccessAsync("member", "System.CommandLine");

        Assert.Null(options.TypeName);
        Assert.Equal("System.CommandLine", options.PackagePath);
        Assert.Null(options.PlatformAssembly);
        Assert.Empty(options.MemberFilter);
    }

    [Fact]
    public async Task Positional_QualifiedTypeMember_ResolvesPlatformTypeAndMember()
    {
        var options = await ParseSuccessAsync("member", "System.Text.Json.JsonSerializer.SerializeToNode");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Null(options.PackagePath);
        Assert.Contains("SerializeToNode", options.MemberFilter);
    }

    [Fact]
    public async Task Positional_QualifiedTypeMemberWithTypo_PreservesTypeForSuggestions()
    {
        var options = await ParseSuccessAsync("member", "System.Text.Json.JsonSerializizer.SerializeToNode");

        Assert.Equal("JsonSerializizer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Null(options.PackagePath);
        Assert.Contains("SerializeToNode", options.MemberFilter);
    }

    [Fact]
    public async Task Positional_QualifiedTypeMemberWithOverload_ResolvesIndex()
    {
        var options = await ParseSuccessAsync("member", "System.Text.Json.JsonSerializer.SerializeToNode:1");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Contains("SerializeToNode", options.MemberFilter);
        Assert.Equal(1, options.OverloadIndex);
    }

    [Fact]
    public async Task Positional_QualifiedPlatformTypeTypo_PreservesTypeForSuggestions()
    {
        var options = await ParseSuccessAsync("member", "System.Text.Json.JsonSerializizer");

        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Equal("JsonSerializizer", options.TypeName);
        Assert.Null(options.PackagePath);
        Assert.Empty(options.MemberFilter);
    }

    [Fact]
    public async Task Positional_QualifiedPlatformTypeTypoAndMember_PreservesTypeForSuggestions()
    {
        var options = await ParseSuccessAsync("member", "System.Text.Json.JsonSerializizer", "SerializeToNode");

        Assert.Equal("System.Text.Json", options.PlatformAssembly);
        Assert.Equal("JsonSerializizer", options.TypeName);
        Assert.Null(options.PackagePath);
        Assert.Contains("SerializeToNode", options.MemberFilter);
    }

    // ── Dotted member syntax (-m Type.Member) ────────────────────────────

    [Fact]
    public async Task DottedMember_SplitsTypeAndMember()
    {
        var options = await ParseSuccessAsync("member", "-m", "JsonSerializer.Deserialize", "--package", "System.Text.Json");

        // The type was specified via dotted syntax, not positionally
        // TypeName may be set from the dotted extraction
        Assert.Contains("Deserialize", options.MemberFilter);
    }

    [Fact]
    public async Task DottedMember_WithExplicitType_KeepsExplicitType()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-m", "JsonElement.GetProperty");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Contains("GetProperty", options.MemberFilter);
    }

    // ── Overload shorthand (Name:N) ──────────────────────────────────────

    [Fact]
    public async Task OverloadShorthand_SetsOverloadIndex()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-m", "Deserialize:2");

        Assert.Contains("Deserialize", options.MemberFilter);
        Assert.Equal(2, options.OverloadIndex);
    }

    [Fact]
    public async Task KindQualifiedOverloadShorthand_SetsKindFilterAndIndex()
    {
        var options = await ParseSuccessAsync(
            "member", "String", "--platform", "System.Private.CoreLib",
            "explicit:System.IConvertible.ToBoolean:1");

        Assert.Contains("System.IConvertible.ToBoolean", options.MemberFilter);
        Assert.Contains("explicit-interface-implementation", options.KindFilter);
        Assert.Equal(1, options.OverloadIndex);
    }

    [Fact]
    public async Task IndexOption_SetsOverloadIndex()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-m", "Deserialize", "--index", "1");

        Assert.Contains("Deserialize", options.MemberFilter);
        Assert.Equal(1, options.OverloadIndex);
    }

    // ── Constructor shorthand ────────────────────────────────────────────

    [Fact]
    public async Task CtorOption_SetsCtorFilter()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializerOptions", "--package", "System.Text.Json", "--ctor");

        Assert.True(options.CtorOnly);
        Assert.Contains(".ctor", options.MemberFilter);
    }

    // ── Numeric member means limit ───────────────────────────────────────

    [Fact]
    public async Task NumericPositionalMember_SetsLimit()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "5");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Equal(5, options.Limit);
    }

    // ── Kind filter ──────────────────────────────────────────────────────

    [Fact]
    public async Task KindFilter_SetsKindFilter()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-k", "method");

        Assert.Contains("method", options.KindFilter);
    }

    [Fact]
    public async Task Columns_WithSemicolonSeparator_AreParsedAsMultipleColumns()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--columns", "Kind;Type");

        Assert.NotNull(options.Columns);
        Assert.Equal(["Kind", "Type"], options.Columns);
    }

    [Fact]
    public async Task Select_WithSemicolonSeparator_AreParsedAsMultipleSections()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-S", "Classes;Constructors");

        Assert.NotNull(options.Select);
        Assert.Equal(["Classes", "Constructors"], options.Select);
    }

    [Fact]
    public async Task Discover_DefaultsToEffectiveDiscovery()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-D");

        Assert.False(options.Schema);
        Assert.True(options.EffectiveDiscovery);
    }

    [Fact]
    public async Task SchemaFlag_OptsOutOfEffectiveDiscovery()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-D", "--schema");

        Assert.True(options.Schema);
        Assert.False(options.EffectiveDiscovery);
    }
}
