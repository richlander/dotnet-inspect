using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.CSharp;
using ILInspector.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Validity")]
public sealed class CSharpMemorySafetySpellingCompileTests
{
    const string LegacyFixtureType =
        "ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures";
    const string UpdatedFixtureType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetySpellingFixture";
    const string ExplicitLayoutFixtureType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyExplicitLayoutFixture";
    const string ExplicitAccessorFixtureType =
        "ILInspector.Decompiler.Fixtures.NewUnsafe.MemorySafetyExplicitAccessorFixture";

    [Theory]
    [InlineData(CSharpMemorySafetyLanguage.Legacy, true)]
    [InlineData(CSharpMemorySafetyLanguage.RelaxedPointerSyntax, false)]
    public void LegacyBinaryContractsRoundTripUnderBothPointerSyntaxProfiles(
        CSharpMemorySafetyLanguage language,
        bool expectsUnsafeModifier)
    {
        ApiType original = ExtractType(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath(),
            LegacyFixtureType);
        AssertRules(original, MemorySafetyRulesState.Legacy);
        ApiMember pointer = Member(original, "ConsumePointer");
        ApiMember pointerFree = Member(original, "Risky");
        CSharpTypePrintResult printed = Print(
            original,
            [pointer, pointerFree],
            CSharpBodyPolicy.Stub,
            language);

        Assert.Equal(
            expectsUnsafeModifier,
            HasWord(MemberLine(printed.Source, pointer.Name), "unsafe"));
        Assert.False(HasWord(MemberLine(printed.Source, pointerFree.Name), "unsafe"));

        CSharpParseOptions parseOptions = language == CSharpMemorySafetyLanguage.Legacy
            ? new CSharpParseOptions(LanguageVersion.Latest)
            : new CSharpParseOptions(LanguageVersion.Preview);
        using CompiledAssembly compiled = CompileUnchanged(
            printed.Source,
            parseOptions,
            $"MemorySafety{language}");
        ApiType emitted = ExtractType(compiled.PE, LegacyFixtureType);

        AssertRules(emitted, MemorySafetyRulesState.Legacy);
        AssertContractPreserved(original, emitted, pointer.Name);
        AssertContractPreserved(original, emitted, pointerFree.Name);
    }

    [Fact]
    public void UpdatedContractsRoundTripForMethodsFieldsAndExplicitExtern()
    {
        ApiType original = ExtractType(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            UpdatedFixtureType);
        AssertRules(original, MemorySafetyRulesState.Updated);
        Assert.Equal(ApiTypeLayout.Sequential, original.Layout);
        var originalExternImplementation =
            Assert.IsType<ApiMethodImplementationFacts>(
                Member(original, "SafeExtern").MethodImplementation);
        Assert.True(
            (originalExternImplementation.Attributes
                & MethodAttributes.PinvokeImpl) != 0);
        string[] names =
        [
            "PointerFreeUnsafeMethod",
            "PointerNoneMethod",
            "PointerNoneField",
            "UnsafeField",
            "NormalField",
            "NormalMethod",
            "SafeExtern",
        ];
        ApiMember[] members = names.Select(name => Member(original, name)).ToArray();
        CSharpMemberPolicy[] policies = members
            .Where(member => member.Kind == "method")
            .Select(member => new CSharpMemberPolicy(
                member,
                member.Name == "SafeExtern"
                    ? CSharpBodyPolicy.Extern
                    : CSharpBodyPolicy.Stub))
            .ToArray();
        CSharpTypePrintResult printed = Print(
            original,
            members,
            CSharpBodyPolicy.Skeleton,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts,
            policies,
            includeCustomAttributes: true);

        Assert.True(HasWord(
            MemberLine(printed.Source, "PointerFreeUnsafeMethod"),
            "unsafe"));
        Assert.False(HasWord(
            MemberLine(printed.Source, "PointerNoneMethod"),
            "unsafe"));
        Assert.False(HasWord(
            MemberLine(printed.Source, "PointerNoneField"),
            "unsafe"));
        Assert.True(HasWord(MemberLine(printed.Source, "UnsafeField"), "unsafe"));
        Assert.False(HasWord(MemberLine(printed.Source, "NormalField"), "safe"));
        Assert.False(HasWord(MemberLine(printed.Source, "NormalMethod"), "unsafe"));
        Assert.True(HasWord(MemberLine(printed.Source, "SafeExtern"), "safe"));
        Assert.True(HasWord(MemberLine(printed.Source, "SafeExtern"), "extern"));

        using CompiledAssembly compiled = CompileUnchanged(
            printed.Source,
            UpdatedParseOptions(),
            "MemorySafetyUpdatedCallerContracts");
        ApiType emitted = ExtractType(compiled.PE, UpdatedFixtureType);

        AssertRules(emitted, MemorySafetyRulesState.Updated);
        foreach (string name in names)
            AssertContractPreserved(original, emitted, name);

        ApiMember emittedExtern = Member(emitted, "SafeExtern");
        var implementation =
            Assert.IsType<ApiMethodImplementationFacts>(emittedExtern.MethodImplementation);
        Assert.False(implementation.HasBodyRva);
    }

    [Fact]
    public void ExplicitLayoutFixtureCompilesUnchangedAndPreservesFieldContracts()
    {
        ApiType original = ExtractType(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            ExplicitLayoutFixtureType);
        ApiMember safe = Member(original, "SafeInstanceField");
        ApiMember unsafeField = Member(original, "UnsafeInstanceField");
        ApiMember staticField = Member(original, "StaticField");

        AssertRules(original, MemorySafetyRulesState.Updated);
        Assert.Equal(ApiTypeLayout.Explicit, original.Layout);
        Assert.Equal(16, original.LayoutDetails!.Size);
        Assert.Equal(2, original.LayoutDetails.PackingSize);
        Assert.Equal(0, safe.FieldLayout!.Offset);
        Assert.Equal(4, unsafeField.FieldLayout!.Offset);
        CSharpTypePrintResult printed = Print(
            original,
            [safe, unsafeField, staticField],
            CSharpBodyPolicy.Skeleton,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);

        Assert.True(HasWord(MemberLine(printed.Source, safe.Name), "safe"));
        Assert.True(HasWord(MemberLine(printed.Source, unsafeField.Name), "unsafe"));
        Assert.False(HasWord(MemberLine(printed.Source, staticField.Name), "safe"));
        Assert.False(HasWord(MemberLine(printed.Source, staticField.Name), "unsafe"));
        Assert.Contains(
            "[global::System.Runtime.InteropServices.StructLayoutAttribute(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = 16, Pack = 2)]",
            printed.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.InteropServices.FieldOffsetAttribute(0)]\n    public safe int SafeInstanceField;",
            printed.Source,
            StringComparison.Ordinal);
        Assert.Contains(
            "[global::System.Runtime.InteropServices.FieldOffsetAttribute(4)]\n    public unsafe int UnsafeInstanceField;",
            printed.Source,
            StringComparison.Ordinal);
        string[] lines = printed.Source.Split('\n');
        int staticLine = Array.FindIndex(
            lines,
            line => line.Contains("StaticField", StringComparison.Ordinal));
        Assert.True(staticLine > 0);
        Assert.DoesNotContain(
            "FieldOffsetAttribute",
            lines[staticLine - 1],
            StringComparison.Ordinal);

        using CompiledAssembly compiled = CompileUnchanged(
            printed.Source,
            UpdatedParseOptions(),
            "MemorySafetyExplicitLayoutFields");
        ApiType emitted = ExtractType(compiled.PE, ExplicitLayoutFixtureType);

        AssertRules(emitted, MemorySafetyRulesState.Updated);
        AssertContractPreserved(original, emitted, safe.Name);
        AssertContractPreserved(original, emitted, unsafeField.Name);
        AssertContractPreserved(original, emitted, staticField.Name);
        Assert.Equal(ApiTypeLayout.Explicit, emitted.Layout);
        Assert.Equal(16, emitted.LayoutDetails!.Size);
        Assert.Equal(2, emitted.LayoutDetails.PackingSize);
        Assert.Equal(0, Member(emitted, safe.Name).FieldLayout!.Offset);
        Assert.Equal(4, Member(emitted, unsafeField.Name).FieldLayout!.Offset);
    }

    [Fact]
    public void MixedLegacyAndUpdatedFixturesAreRejectedAsOneSourceBatch()
    {
        ApiType legacy = ExtractType(
            FixtureCatalog.DecompilerUnsafeLegacy.AssemblyPath(),
            LegacyFixtureType);
        ApiType updated = ExtractType(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            UpdatedFixtureType);
        ApiMember legacyPointer = Member(legacy, "ConsumePointer");
        ApiMember updatedUnsafe = Member(updated, "PointerFreeUnsafeMethod");

        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().PrintBatch(
                [
                    new CSharpTypePrintRequest(
                        legacy,
                        CSharpBodyPolicy.Stub,
                        [legacyPointer]),
                    new CSharpTypePrintRequest(
                        updated,
                        CSharpBodyPolicy.Stub,
                        [updatedUnsafe]),
                ],
                new CSharpTypePrintOptions
                {
                    MemorySafetyLanguage =
                        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
                }));

        Assert.Empty(outcome.SelfNameFailures);
        CSharpTypePrintDiagnostic failure =
            Assert.Single(outcome.MemorySafetyFailures);
        Assert.Equal("<batch>", failure.TypeName);
        Assert.Contains("legacy", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("updated", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExplicitInterfaceAccessorIsAtomicallyUnavailable()
    {
        ApiType original = ExtractType(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            ExplicitAccessorFixtureType);
        ApiMember accessor = Assert.Single(
            original.Members,
            member => member.Kind == "explicit-interface-implementation");

        Assert.Equal(
            ApiMethodSemanticsKind.PropertyGetter,
            accessor.MethodSemantics);
        var outcome = Assert.IsType<CSharpTypePrintOutcome.NotRendered>(
            new CSharpTypePrinter().Print(
                new CSharpTypePrintRequest(
                    original,
                    CSharpBodyPolicy.Stub,
                    [accessor]),
                new CSharpTypePrintOptions
                {
                    MemorySafetyLanguage =
                        CSharpMemorySafetyLanguage.UpdatedCallerContracts,
                }));

        Assert.Empty(outcome.SelfNameFailures);
        CSharpTypePrintDiagnostic failure =
            Assert.Single(outcome.MemorySafetyFailures);
        Assert.Equal(original.FullName, failure.TypeName);
        Assert.Contains(accessor.Name, failure.Message, StringComparison.Ordinal);
        Assert.Contains("accessor", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdatedUnsafeConstructorRoundTripsAsOrdinaryAndExplicitExtern()
    {
        ApiType original = ExtractType(
            FixtureCatalog.DecompilerUnsafeNew.AssemblyPath(),
            UpdatedFixtureType);
        ApiMember constructor = Member(original, ".ctor");

        CSharpTypePrintResult ordinary = Print(
            original,
            [constructor],
            CSharpBodyPolicy.Stub,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts);
        CSharpTypePrintResult external = Print(
            original,
            [constructor],
            CSharpBodyPolicy.Skeleton,
            CSharpMemorySafetyLanguage.UpdatedCallerContracts,
            [new CSharpMemberPolicy(constructor, CSharpBodyPolicy.Extern)]);

        string constructorSignature = $"{original.Name}()";
        Assert.True(HasWord(
            MemberLine(ordinary.Source, constructorSignature),
            "unsafe"));
        Assert.False(HasWord(
            MemberLine(ordinary.Source, constructorSignature),
            "extern"));
        Assert.True(HasWord(
            MemberLine(external.Source, constructorSignature),
            "unsafe"));
        Assert.True(HasWord(
            MemberLine(external.Source, constructorSignature),
            "extern"));

        using CompiledAssembly ordinaryCompiled = CompileUnchanged(
            ordinary.Source,
            UpdatedParseOptions(),
            "MemorySafetyOrdinaryConstructor");
        using CompiledAssembly externCompiled = CompileUnchanged(
            external.Source,
            UpdatedParseOptions(),
            "MemorySafetyExternConstructor");
        ApiType ordinaryEmitted = ExtractType(
            ordinaryCompiled.PE,
            UpdatedFixtureType);
        ApiType externEmitted = ExtractType(
            externCompiled.PE,
            UpdatedFixtureType);

        AssertRules(ordinaryEmitted, MemorySafetyRulesState.Updated);
        AssertRules(externEmitted, MemorySafetyRulesState.Updated);
        AssertContractPreserved(original, ordinaryEmitted, ".ctor");
        AssertContractPreserved(original, externEmitted, ".ctor");
        Assert.True(Member(ordinaryEmitted, ".ctor").MethodImplementation!.HasBodyRva);
        Assert.False(Member(externEmitted, ".ctor").MethodImplementation!.HasBodyRva);
    }

    static CSharpTypePrintResult Print(
        ApiType type,
        IReadOnlyList<ApiMember> members,
        CSharpBodyPolicy bodyPolicy,
        CSharpMemorySafetyLanguage language,
        IReadOnlyList<CSharpMemberPolicy>? policies = null,
        bool includeCustomAttributes = false)
    {
        var outcome = new CSharpTypePrinter().Print(
            new CSharpTypePrintRequest(
                type,
                bodyPolicy,
                members,
                policies),
            new CSharpTypePrintOptions
            {
                IncludeCustomAttributes = includeCustomAttributes,
                MemorySafetyLanguage = language,
            });
        return Assert.IsType<CSharpTypePrintOutcome.Printed>(outcome).Result;
    }

    static ApiType ExtractType(string path, string fullName)
    {
        using var pe = new PEReader(File.OpenRead(path));
        return ExtractType(pe, fullName);
    }

    static ApiType ExtractType(PEReader pe, string fullName)
        => Assert.Single(
            ApiSurfaceExtractor.Extract(pe, includeAll: true).Types,
            type => type.FullName == fullName);

    static ApiMember Member(ApiType type, string name)
        => Assert.Single(type.Members, member => member.Name == name);

    static void AssertRules(ApiType type, MemorySafetyRulesState expected)
    {
        var rules =
            Assert.IsType<MemorySafetyRulesResult.Available>(type.MemorySafety!.Rules);
        Assert.Equal(expected, rules.State);
    }

    static void AssertContractPreserved(
        ApiType original,
        ApiType emitted,
        string memberName)
    {
        ApiMember expected = Member(original, memberName);
        ApiMember actual = Member(emitted, memberName);
        ApiMemberMemorySafetyFacts expectedFacts =
            Assert.IsType<ApiMemberMemorySafetyFacts>(expected.MemorySafety);
        ApiMemberMemorySafetyFacts actualFacts =
            Assert.IsType<ApiMemberMemorySafetyFacts>(actual.MemorySafety);

        Assert.Equal(
            expectedFacts.CallerContract.GetType(),
            actualFacts.CallerContract.GetType());
        Assert.Equal(expectedFacts.SignaturePointer, actualFacts.SignaturePointer);
        Assert.Equal(
            emitted.MemorySafety!.ModuleVersionId,
            actualFacts.ModuleVersionId);
        Assert.Equal(
            Assert.IsType<MemorySafetyRulesResult.Available>(
                emitted.MemorySafety.Rules).State,
            actualFacts.CallerContract.Evidence.RulesState);
    }

    static string MemberLine(string source, string memberName)
        => Assert.Single(
            source.Split('\n'),
            line => line.Contains(memberName, StringComparison.Ordinal));

    static bool HasWord(string text, string word)
        => text.Split(
                [' ', '\t', '\r', '\n', '(', ')', ';', '{', '}', '[', ']', ','],
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(word, StringComparer.Ordinal);

    static CSharpParseOptions UpdatedParseOptions()
        => new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures(
            [
                new KeyValuePair<string, string>(
                    "updated-memory-safety-rules",
                    "true"),
            ]);

    static CompiledAssembly CompileUnchanged(
        string source,
        CSharpParseOptions parseOptions,
        string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, parseOptions)],
            RoslynTestReferences.TrustedPlatform,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                optimizationLevel: OptimizationLevel.Release));
        var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        ImmutableArray<Diagnostic> errors = emit.Diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        Assert.True(
            emit.Success,
            $"Product source must compile unchanged.\n{string.Join(Environment.NewLine, errors)}"
                + $"\n--- source ---\n{source}");
        stream.Position = 0;
        return new CompiledAssembly(
            stream,
            new PEReader(stream, PEStreamOptions.LeaveOpen));
    }

    sealed class CompiledAssembly(MemoryStream stream, PEReader pe) : IDisposable
    {
        public PEReader PE { get; } = pe;

        public void Dispose()
        {
            PE.Dispose();
            stream.Dispose();
        }
    }
}
