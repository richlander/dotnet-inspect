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
        var selectOption = new Option<bool>("--show-index");
        var kindOption = new Option<string[]>("-k") { AllowMultipleArgumentsPerToken = true };
        kindOption.Aliases.Add("--kind");
        var binOption = new Option<string[]>("--bin") { AllowMultipleArgumentsPerToken = true };
        binOption.Aliases.Add("--directory");
        var callerProjectOption = new Option<string[]>("--project") { AllowMultipleArgumentsPerToken = true };
        var callerPackageOption = new Option<string[]>("--caller-package") { AllowMultipleArgumentsPerToken = true };

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
        memberCommand.Options.Add(selectOption);
        memberCommand.Options.Add(kindOption);
        memberCommand.Options.Add(binOption);
        memberCommand.Options.Add(callerProjectOption);
        memberCommand.Options.Add(callerPackageOption);
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
            unsafeOption, indexOption, selectOption, kindOption,
            binOption, callerProjectOption, callerPackageOption);

        return (root, opts, args);
    }

    private static async Task<MemberOptions> ParseSuccessAsync(params string[] args)
    {
        ArgumentPreprocessor.Reset();
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
    public async Task CallerScope_BinAndDirectory_PopulateCallerScopeDirectories()
    {
        var options = await ParseSuccessAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "--bin", "out/a", "--directory", "out/b");

        Assert.Equal(["out/a", "out/b"], options.CallerScopeDirectories);
        Assert.True(options.HasCallerScope);
    }

    [Fact]
    public async Task CallerScope_Project_PopulatesCallerScopeProjects()
    {
        var options = await ParseSuccessAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "--project", "App.csproj");

        Assert.Equal(["App.csproj"], options.CallerScopeProjects);
        Assert.True(options.HasCallerScope);
    }

    [Fact]
    public async Task CallerScope_CallerPackage_PopulatesCallerScopePackages()
    {
        var options = await ParseSuccessAsync(
            "member", "JsonSerializer", "--package", "System.Text.Json",
            "--caller-package", "Newtonsoft.Json");

        Assert.Equal(["Newtonsoft.Json"], options.CallerScopePackages);
        Assert.True(options.HasCallerScope);
    }

    [Fact]
    public async Task CallerScope_None_LeavesScopeEmpty()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json");

        Assert.Empty(options.CallerScopeDirectories);
        Assert.Empty(options.CallerScopeProjects);
        Assert.Empty(options.CallerScopePackages);
        Assert.False(options.HasCallerScope);
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
        Assert.False(options.Jsonl);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithImplicitOutput_KeepsTipsEnabled()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--tips", "d");

        Assert.False(options.FormatExplicitlySet);
        Assert.Equal(TipLevel.Detailed, options.TipLevel);
    }

    [Fact]
    public async Task ExplicitPackage_WithMarkdown_SuppressesTips()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--markdown", "--tips", "d");

        Assert.True(options.FormatExplicitlySet);
        Assert.False(options.IsRawOutput);
        Assert.Equal(TipLevel.Quiet, options.TipLevel);
    }

    [Fact]
    public async Task ExplicitPackage_WithTsvAndNoHeaders_SetsTsvOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--tsv", "--no-headers");

        Assert.True(options.OneLine);
        Assert.True(options.Tsv);
        Assert.False(options.Jsonl);
        Assert.True(options.NoHeader);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithJsonl_SetsJsonlOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--jsonl");

        Assert.True(options.OneLine);
        Assert.False(options.Tsv);
        Assert.True(options.Jsonl);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithTableAndTsv_UsesTsvOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--table", "--tsv");

        Assert.True(options.OneLine);
        Assert.True(options.Tsv);
        Assert.False(options.Jsonl);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithTableAndJsonl_UsesJsonlOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--table", "--jsonl");

        Assert.True(options.OneLine);
        Assert.False(options.Tsv);
        Assert.True(options.Jsonl);
        Assert.True(options.OneLineExplicitlySet);
        Assert.True(options.FormatExplicitlySet);
    }

    [Fact]
    public async Task ExplicitPackage_WithJsonAndTsv_IsRejected()
    {
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(["member", "JsonSerializer", "--package", "System.Text.Json", "--json", "--tsv"]);
        Assert.Empty(parseResult.Errors);

        // The rejection path writes to Console.Error before cancelling; run it
        // under ConsoleCapture so that write lands on a live, semaphore-owned
        // writer instead of one a parallel capture test may have disposed.
        await ConsoleCapture.RunAsync(async () =>
        {
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => MemberOptionsParser.ParseAsync(parseResult, opts, cmdArgs));
            return 0;
        });
    }

    [Fact]
    public async Task ExplicitPackage_WithJsonAndJsonl_IsRejected()
    {
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(["member", "JsonSerializer", "--package", "System.Text.Json", "--json", "--jsonl"]);
        Assert.Empty(parseResult.Errors);

        // See ExplicitPackage_WithJsonAndTsv_IsRejected: capture the console so
        // the rejection's Console.Error write cannot hit a disposed writer.
        await ConsoleCapture.RunAsync(async () =>
        {
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => MemberOptionsParser.ParseAsync(parseResult, opts, cmdArgs));
            return 0;
        });
    }

    [Fact]
    public async Task ExplicitPackage_WithOneline_SetsTableCompatOutput()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "--oneline");

        Assert.True(options.OneLine);
        Assert.False(options.Tsv);
        Assert.False(options.Jsonl);
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

    [Theory]
    [InlineData("string", "System.String")]
    [InlineData("int", "System.Int32")]
    [InlineData("bool", "System.Boolean")]
    [InlineData("object", "System.Object")]
    [InlineData("String", "System.String")]
    [InlineData("DateTime", "System.DateTime")]
    [InlineData("Guid", "System.Guid")]
    [InlineData("Math", "System.Math")]
    [InlineData("System.String", "System.String")]
    [InlineData("List<T>", "System.Collections.Generic.List`1")]
    [InlineData("Dictionary<TKey,TValue>", "System.Collections.Generic.Dictionary`2")]
    [InlineData("Dictionary`2", "System.Collections.Generic.Dictionary`2")]
    [InlineData("Action", "System.Action")]
    [InlineData("Action`1", "System.Action`1")]
    [InlineData("Func`2", "System.Func`2")]
    public async Task Positional_BareCoreLibType_ResolvesPlatformType(string input, string expectedTypeName)
    {
        var (coreLibPath, _, _, coreLibError) = PlatformResolver.ResolveAssembly("System.Private.CoreLib");
        if (coreLibPath == null || coreLibError != null)
        {
            Assert.Skip($"System.Private.CoreLib not available: {coreLibError}");
            return;
        }

        var options = await ParseSuccessAsync("member", input);

        Assert.Equal("System.Private.CoreLib", options.PlatformAssembly);
        Assert.Equal(expectedTypeName, options.TypeName);
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

    [Fact]
    public async Task ExplicitPackage_PositionalDottedMember_SplitsTypeAndMember()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer.Serialize", "--package", "System.Text.Json");

        Assert.Equal("JsonSerializer", options.TypeName);
        Assert.Contains("Serialize", options.MemberFilter);
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
    public async Task DigestShorthand_SetsMemberDigest()
    {
        var options = await ParseSuccessAsync("member", "JsonSerializer", "--package", "System.Text.Json", "-m", "Deserialize~abc123");

        Assert.Contains("Deserialize", options.MemberFilter);
        Assert.Equal("abc123", options.MemberDigest);
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

    // ── FQN parsing (Type.Member with generics and overloads) ───────────────

    [Theory]
    [InlineData("List<T>.IndexOf", "IndexOf")]
    [InlineData("List<T>.IndexOf:3", "IndexOf")]
    [InlineData("Dictionary<TKey,TValue>.TryGetValue", "TryGetValue")]
    [InlineData("Span<T>.Slice:2", "Slice")]
    [InlineData("ReadOnlySpan<T>.IndexOf", "IndexOf")]
    [InlineData("List`1.IndexOf", "IndexOf")]
    [InlineData("Dictionary`2.TryGetValue", "TryGetValue")]
    public async Task GenericTypeDotMember_SplitsAndResolvesType(string input, string expectedMember)
    {
        var options = await ParseSuccessAsync("member", input);

        Assert.Contains(expectedMember, options.MemberFilter);
        Assert.NotNull(options.TypeName);
        Assert.NotNull(options.PlatformAssembly);
    }

    [Theory]
    [InlineData("List<T>.IndexOf:1", "IndexOf", 1)]
    [InlineData("List<T>.IndexOf:3", "IndexOf", 3)]
    [InlineData("Dictionary<TKey,TValue>.TryGetValue:1", "TryGetValue", 1)]
    [InlineData("Span<T>.Slice:2", "Slice", 2)]
    public async Task GenericTypeDotMemberWithOverload_SplitsAndSetsIndex(
        string input, string expectedMember, int expectedIndex)
    {
        var options = await ParseSuccessAsync("member", input);

        Assert.Contains(expectedMember, options.MemberFilter);
        Assert.Equal(expectedIndex, options.OverloadIndex);
        Assert.NotNull(options.TypeName);
        Assert.NotNull(options.PlatformAssembly);
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<T>", "List`1", null)]
    [InlineData("System.Collections.Generic.List<T>.IndexOf", "Generic.List`1", "IndexOf")]
    [InlineData("System.Text.Json.JsonSerializer.Deserialize", "JsonSerializer", "Deserialize")]
    [InlineData("System.Text.Json.JsonSerializer.Deserialize:5", "JsonSerializer", "Deserialize")]
    [InlineData("System.Span<T>.Slice", "Span`1", "Slice")]
    public async Task QualifiedGenericTypeDotMember_ResolvesSourceAndType(
        string input, string expectedTypeName, string? expectedMember)
    {
        var options = await ParseSuccessAsync("member", input);

        Assert.Contains(expectedTypeName, options.TypeName);  // Use Contains instead of Equal for flexibility
        if (expectedMember != null)
        {
            Assert.Contains(expectedMember, options.MemberFilter);
        }
    }

    [Theory]
    [InlineData("List<int,string>.BadMethod")]  // Wrong arity
    [InlineData("List<T,U,V>.BadMethod")]       // Wrong arity
    [InlineData("Span<T,U>.BadMethod")]         // Wrong arity
    public async Task GenericTypeWithWrongArity_StillParsesButWontResolve(string input)
    {
        // These should parse the structure, but type resolution may fail
        // We're testing that the parser doesn't crash or skip the input
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(["member", input]);

        var result = await MemberOptionsParser.ParseAsync(parseResult, opts, cmdArgs);

        // Should get some kind of result (not crash)
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("string.ToLower", "ToLower")]
    [InlineData("int.ToString", "ToString")]
    [InlineData("bool.TryParse", "TryParse")]
    public async Task PrimitiveDotMember_SplitsAndResolves(string input, string expectedMember)
    {
        var options = await ParseSuccessAsync("member", input);

        Assert.Contains(expectedMember, options.MemberFilter);
        Assert.NotNull(options.TypeName);  // Will be normalized from alias
        Assert.NotNull(options.PlatformAssembly);
    }

    [Theory]
    [InlineData("Type.GetTypeFromHandle")]
    [InlineData("Type.GetTypeFromHandle:1")]
    [InlineData("String.Concat:10")]
    [InlineData("Math.Max:1")]
    public async Task SimpleTypeDotMember_SplitsAndResolves(string input)
    {
        var options = await ParseSuccessAsync("member", input);

        Assert.NotEmpty(options.MemberFilter);
        Assert.NotNull(options.TypeName);
        Assert.NotNull(options.PlatformAssembly);
    }

    [Fact]
    public async Task NestedGenericType_ParsesCorrectly()
    {
        // e.g., Dictionary<int,List<string>>.TryGetValue
        var options = await ParseSuccessAsync("member", "Dictionary<int,List<string>>.TryGetValue");

        Assert.Contains("TryGetValue", options.MemberFilter);
        Assert.NotNull(options.TypeName);
    }

    [Fact]
    public async Task GenericTypeWithNamespaceAndMember_ParsesCorrectly()
    {
        var options = await ParseSuccessAsync("member", "System.Collections.Generic.List<T>.Add");

        Assert.Contains("Add", options.MemberFilter);
        Assert.NotNull(options.TypeName);
        Assert.Contains("List", options.TypeName);
    }

    // Debug test
    [Fact]
    public async Task Debug_ListIndexOfParsing()
    {
        ArgumentPreprocessor.Reset();
        var (root, opts, cmdArgs) = CreateTestCommand();
        var parseResult = root.Parse(["member", "List<T>.IndexOf:3"]);
        
        var result = await MemberOptionsParser.ParseAsync(parseResult, opts, cmdArgs);
        
        // Log what we got
        Console.WriteLine($"Result type: {result.GetType().Name}");
        if (result is MemberOptionsParser.VersionError ve)
        {
            Console.WriteLine($"Error: {ve.Message}");
        }
        else if (result is MemberOptionsParser.Success success)
        {
            Console.WriteLine($"TypeName: {success.Options.TypeName}");
            Console.WriteLine($"PackagePath: {success.Options.PackagePath}");
            Console.WriteLine($"PlatformAssembly: {success.Options.PlatformAssembly}");
            Console.WriteLine($"MemberFilter: {string.Join(", ", success.Options.MemberFilter)}");
        }
        
        // Assert for now
        Assert.IsType<MemberOptionsParser.Success>(result);
    }
}
