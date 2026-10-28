using System.Collections.Concurrent;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using Inspector.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gates for the unchanged prefilter's closure hash
/// (docs/design/finding-producers.md#unchanged-prefilter): no Type whose
/// closure hash is equal on both sides has a row in the production API
/// comparison of the same view, and the canonical encoding ignores row
/// numbering and order but not a custom modifier.
/// </summary>
public sealed class ApiTypeClosureHashTests
{
    /// <summary>
    /// Names a file of <c>old..new</c> assembly path pairs, one per line, for
    /// the opt-in corpus agreement sweep.
    /// </summary>
    const string CorpusPairsVariable = "API_CLOSURE_HASH_PAIRS";

    static readonly FindingSubject Subject = new("api", "API surface");

    [Theory]
    [InlineData("finding-diff-any", ApiSurfaceExtractionScope.ApiAxis)]
    [InlineData("finding-diff-any", ApiSurfaceExtractionScope.IncludeAll)]
    [InlineData("diff", ApiSurfaceExtractionScope.ApiAxis)]
    [InlineData("diff", ApiSurfaceExtractionScope.IncludeAll)]
    [InlineData("library-api-diff", ApiSurfaceExtractionScope.ApiAxis)]
    [InlineData("library-api-diff", ApiSurfaceExtractionScope.IncludeAll)]
    [InlineData("api-correspondence", ApiSurfaceExtractionScope.ApiAxis)]
    [InlineData("api-correspondence", ApiSurfaceExtractionScope.IncludeAll)]
    public void FixturePair_NoEqualHashTypeHasAComparisonRow(
        string pairName,
        ApiSurfaceExtractionScope view)
    {
        FixturePair pair = pairName switch
        {
            "finding-diff-any" => FixtureCatalog.MetadataFindingDiffAnyPair,
            "diff" => FixtureCatalog.DiffPair,
            "library-api-diff" => FixtureCatalog.LibraryApiDiffPair,
            _ => FixtureCatalog.MetadataApiCorrespondencePair,
        };

        ClosureAgreement agreement = Measure(pair.OldAssemblyPath(), pair.NewAssemblyPath(), view);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{pairName} {view}: Types {agreement.Types}, equal hash {agreement.Identical}, "
                + $"misses {agreement.Misses.Count}, false changes {agreement.FalseChanges}");

        Assert.True(agreement.ContextEqual, "The fixture pair's context hashes differ, so the gate would be vacuous.");
        Assert.Empty(agreement.Misses);
    }

    /// <summary>
    /// The fixture gate is not vacuous: in each view the pairs hold Types with
    /// an equal hash and Types whose hash differs.
    /// </summary>
    [Theory]
    [InlineData(ApiSurfaceExtractionScope.ApiAxis)]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll)]
    public void FixturePairs_HoldEqualAndDifferentHashes(ApiSurfaceExtractionScope view)
    {
        ClosureAgreement[] agreements =
        [
            .. new[]
            {
                FixtureCatalog.MetadataFindingDiffAnyPair,
                FixtureCatalog.DiffPair,
                FixtureCatalog.LibraryApiDiffPair,
                FixtureCatalog.MetadataApiCorrespondencePair,
            }.Select(pair => Measure(pair.OldAssemblyPath(), pair.NewAssemblyPath(), view)),
        ];

        Assert.True(agreements.Sum(agreement => agreement.Identical) > 0);
        Assert.True(agreements.Sum(agreement => agreement.Types - agreement.Identical) > 0);
    }

    /// <summary>
    /// The agreement gate over an external pair population, such as the
    /// runtime and ASP.NET Core packs of two releases. Set
    /// <see cref="CorpusPairsVariable"/> to run it.
    /// </summary>
    [Fact]
    [Trait("Speed", "Slow")]
    public void CorpusPairs_NoEqualHashTypeHasAComparisonRow()
    {
        string? pairsFile = Environment.GetEnvironmentVariable(CorpusPairsVariable);
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(pairsFile),
            $"Set {CorpusPairsVariable} to a file of old..new assembly pairs to run the corpus sweep.");
        Assert.True(
            File.Exists(pairsFile),
            $"{CorpusPairsVariable} is set to '{pairsFile}', which is not a file. Unset it to skip the corpus sweep.");

        (string Old, string New)[] pairs =
        [
            .. File.ReadAllLines(pairsFile!)
                .Where(line => line.Length > 0)
                .Select(line =>
                {
                    int separator = line.IndexOf("..", StringComparison.Ordinal);
                    return (line[..separator], line[(separator + 2)..]);
                }),
        ];

        var report = new List<string>();
        var misses = new List<string>();
        foreach (ApiSurfaceExtractionScope view in new[] { ApiSurfaceExtractionScope.ApiAxis, ApiSurfaceExtractionScope.IncludeAll })
        {
            var results = new ConcurrentBag<ClosureAgreement>();
            Parallel.ForEach(
                pairs,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                pair => results.Add(Measure(pair.Old, pair.New, view)));
            int types = results.Sum(result => result.Types);
            int identical = results.Sum(result => result.Identical);
            int falseChanges = results.Sum(result => result.FalseChanges);
            int contextDiffers = results.Count(result => !result.ContextEqual);
            misses.AddRange(results.SelectMany(result => result.Misses).Select(miss => $"{view}: {miss}"));
            report.Add(
                $"{view}: pairs {results.Count}, context differs {contextDiffers}, Types {types}, "
                    + $"equal hash {identical}, misses {results.Sum(result => result.Misses.Count)}, false changes {falseChanges}");
        }

        foreach (string line in report)
            TestContext.Current.TestOutputHelper?.WriteLine(line);
        Assert.True(misses.Count == 0, string.Join(Environment.NewLine, report.Concat(misses.Take(200))));
    }

    [Fact]
    public void TypeReferenceRowNumbers_DoNotEnterTheHash()
    {
        const string member = "public class C { public System.Text.StringBuilder U(System.Version v) => null; }";
        byte[] first = Compile(member);
        byte[] second = Compile(
            "internal class Z { System.IO.Stream s; System.Text.StringBuilder b; System.Version v; } " + member);

        Assert.NotEqual(TypeReferenceRow(first, "Version"), TypeReferenceRow(second, "Version"));
        Assert.NotEqual(TypeReferenceRow(first, "StringBuilder"), TypeReferenceRow(second, "StringBuilder"));
        AssertEqualHash(first, second, "C");
    }

    [Fact]
    public void ReorderedRows_DoNotEnterTheHash()
    {
        byte[] first = Compile(
            """
            public class C : System.IComparable, System.IDisposable
            {
                [System.Obsolete, System.CLSCompliant(false)]
                public void A() { }
                public int F;
                public int G;
                public void B() { }
                public void Dispose() { }
                public int CompareTo(object o) => 0;
            }
            """);
        byte[] second = Compile(
            """
            public class C : System.IDisposable, System.IComparable
            {
                public int CompareTo(object o) => 0;
                public void Dispose() { }
                public void B() { }
                public int G;
                public int F;
                [System.CLSCompliant(false), System.Obsolete]
                public void A() { }
            }
            """);

        Assert.NotEqual(MethodRow(first, "A"), MethodRow(second, "A"));
        AssertEqualHash(first, second, "C");
    }

    [Fact]
    public void ChangedRequiredModifier_EntersTheHash()
    {
        // An init accessor differs from a set accessor only by the
        // modreq(IsExternalInit) on its return type.
        byte[] set = Compile("public class C { public int P { get; set; } }");
        byte[] init = Compile("public class C { public int P { get; init; } }");

        foreach (ApiSurfaceExtractionScope view in Views)
        {
            Assert.DoesNotContain("modreq(", Encode(set, "C", view), StringComparison.Ordinal);
            Assert.Contains("modreq(", Encode(init, "C", view), StringComparison.Ordinal);
        }
        AssertDifferentHash(set, init, "C");
    }

    [Fact]
    public void ChangedParameterName_EntersTheHash()
        => AssertDifferentHash(
            Compile("public class C { public void M(int a) { } }"),
            Compile("public class C { public void M(int b) { } }"),
            "C");

    /// <summary>
    /// The extractor attaches E.X to C, but the comparer compares only
    /// declared members, so E's extension methods are outside C's closure.
    /// If the comparer starts reporting attached members, this fails and the
    /// closure must take the extension-declaring Types.
    /// </summary>
    [Fact]
    public void AttachedExtensionChange_IsOutsideTheReceiverClosure()
    {
        byte[] first = Compile("public class C { } public static class E { public static void X(this C c) { } }");
        byte[] second = Compile("public class C { } public static class E { public static void Y(this C c) { } }");

        foreach (ApiSurfaceExtractionScope view in Views)
        {
            using var before = new PEReader(new MemoryStream(first));
            Assert.Contains(
                ApiSurfaceExtractor.Extract(before, view).Types.Single(type => type.Name == "C").Members,
                member => member.Name == "X");
            Assert.DoesNotContain("C", ComparerRows(first, second, view));
        }
        AssertEqualHash(first, second, "C");
    }

    [Fact]
    public void EnumDefaultMemberRename_EntersTheParameterOwnerHash()
    {
        // The default value is spelled E.A, a field of another Type.
        byte[] first = Compile("public enum E { A = 1 } public class C { public void M(E e = E.A) { } }");
        byte[] second = Compile("public enum E { B = 1 } public class C { public void M(E e = E.B) { } }");

        AssertComparerRow(first, second, "C");
        AssertDifferentHash(first, second, "C");
    }

    [Theory]
    [InlineData("enum-default", "public enum E { A = 1 } public class C { public void M(E e = E.A) { } }", "public enum E { B = 1 } public class C { public void M(E e = E.B) { } }", true)]
    [InlineData("interface-admission", "internal interface I { void M(); } public class C : I { void I.M() { } }", "public interface I { void M(); } public class C : I { void I.M() { } }", true)]
    [InlineData("internal-interface-negative", "internal interface I { void M(); } public class C : I { void I.M() { } }", "internal interface I { void N(); } public class C : I { void I.N() { } }", false)]
    [InlineData("module-negative", "[module: System.CLSCompliant(true)] public class C { }", "[module: System.CLSCompliant(false)] public class C { }", false)]
    [InlineData("nullable-enum-default-negative", "public enum E { A = 1 } public class C { public void M(E? e = E.A) { } }", "public enum E { B = 1 } public class C { public void M(E? e = E.B) { } }", false)]
    [InlineData("nested-enum-default", "public class Outer { public enum E { A = 1 } } public class C { public void M(Outer.E e = Outer.E.A) { } }", "public class Outer { public enum E { B = 1 } } public class C { public void M(Outer.E e = Outer.E.B) { } }", true)]
    [InlineData("unused-enum-literal-negative", "public enum E { A = 1, B = 2 } public class C { public void M(E e = E.A) { } }", "public enum E { A = 1, Other = 2 } public class C { public void M(E e = E.A) { } }", false)]
    [InlineData("enum-value-default-negative", "public enum E { A = 1 } public class C { public void M(E e = E.A) { } }", "public enum E { A = 2 } public class C { public void M(E e = E.A) { } }", false)]
    [InlineData("multiple-defaults-and-methods", "public enum E { A = 1 } public enum F { B = 2 } public class C { public void M(E x = E.A, int count = 7) { } public void N(F y = F.B, int count = 8) { } }", "public enum E { A = 1 } public enum F { Other = 2 } public class C { public void M(E x = E.A, int count = 7) { } public void N(F y = F.Other, int count = 8) { } }", true)]
    [InlineData("module-multiple-types-negative", "[module: System.CLSCompliant(true)] public class A { } public class C { }", "[module: System.CLSCompliant(false)] public class A { } public class C { }", false)]
    public void CensusAndClosureHash_RespectCrossTypeDependencies(string scenario, string oldSource, string newSource, bool expectedChanged)
    {
        byte[] oldImage = Compile(oldSource);
        byte[] newImage = Compile(newSource);
        using var before = new PEReader(new MemoryStream(oldImage));
        using var after = new PEReader(new MemoryStream(newImage));
        var full = MetadataFindings.CompareApi(
            ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.ApiAxis),
            ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.ApiAxis),
            Subject, new ApiDiffOptions(ApiDiffScope.All));
        bool oracle = FindingDiffAnyTests.ChangedTypes(full).Contains("C");
        var census = Assert.Single(FindingDiffAny.Compare(before, after, axes: FindingDiffAnyAxes.Api, cancellationToken: TestContext.Current.CancellationToken).Types, t => t.FullName == "C").Api;
        var oldHash = new ApiTypeClosureHash(before.GetMetadataReader());
        var newHash = new ApiTypeClosureHash(after.GetMetadataReader());
        bool hashEqual = oldHash.Context() == newHash.Context()
            && oldHash.Type(Definition(before.GetMetadataReader(), "C"), ApiSurfaceExtractionScope.ApiAxis)
                == newHash.Type(Definition(after.GetMetadataReader(), "C"), ApiSurfaceExtractionScope.ApiAxis);
        TestContext.Current.TestOutputHelper!.WriteLine($"{scenario}: completeChanged={oracle}, census={census}, hashEqual={hashEqual}");
        Assert.Equal(expectedChanged, oracle);
        Assert.False(hashEqual);
        Assert.Equal(expectedChanged ? FindingDiffAnyState.Changed : FindingDiffAnyState.Unchanged, census);
        var pairing = new FindingDiffAnyTypePairing(before, after);
        Assert.Equal(census, pairing.Inventory("C").Api);
        FindingDiffAnyTypeMembers partition = pairing.Members("C");
        Assert.Equal(census, partition.Api);
        if (expectedChanged)
            Assert.Contains(partition.Members, member => member.Api == FindingDiffAnyState.Changed);
        else
            Assert.All(partition.Members, member => Assert.Equal(FindingDiffAnyState.Unchanged, member.Api));
        if (scenario == "multiple-defaults-and-methods")
        {
            FindingDiffAnyMemberState member = Assert.Single(partition.Members, row => row.Api == FindingDiffAnyState.Changed);
            Assert.Equal("N", before.GetMetadataReader().GetString(before.GetMetadataReader().GetMethodDefinition((MethodDefinitionHandle)member.Before).Name));
        }
        if (scenario.Contains("enum", StringComparison.Ordinal))
        {
            using var declarations = DeclarationCensusSide.Create(before);
            var cursor = DeclarationCensusCursor.Create(declarations, Definition(before.GetMetadataReader(), "C"), FindingDiffAnyAxis.Api);
            bool dependency = false;
            while (cursor.Advance()) dependency |= cursor.Current!.Kind == DeclarationFactKind.ApiDependency;
            Assert.True(dependency, "The signature dependency must reach the shared declaration census.");
        }
    }

    [Fact]
    public void NullDefaults_DoNotDependOnEnumLiterals()
    {
        byte[] oldImage = Compile("public enum E { A = 1 } public struct S { public const int A = 1; } public class C { public void M(S value = default, E? nullable = null) { } }");
        byte[] newImage = Compile("public enum E { B = 1 } public struct S { public const int B = 1; } public class C { public void M(S value = default, E? nullable = null) { } }");
        using var before = new PEReader(new MemoryStream(oldImage));
        using var after = new PEReader(new MemoryStream(newImage));
        var full = MetadataFindings.CompareApi(
            ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.ApiAxis),
            ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.ApiAxis),
            Subject, new ApiDiffOptions(ApiDiffScope.All));
        Assert.DoesNotContain("C", FindingDiffAnyTests.ChangedTypes(full));
        var result = FindingDiffAny.Compare(before, after, FindingDiffAnyAxes.Api, TestContext.Current.CancellationToken);
        Assert.Equal(FindingDiffAnyState.Unchanged, Assert.Single(result.Types, t => t.FullName == "C").Api);
        Assert.Equal(0, result.Receipt.ApiSurfacesBuilt);
    }

    [Fact]
    public void EnumDefaultDependency_DoesNotWidenTheBodyAxis()
    {
        byte[] oldImage = Compile("public enum E { A = 1 } public class C { private void M(E e = E.A) { } }");
        byte[] newImage = Compile("public enum E { B = 1 } public class C { private void M(E e = E.B) { } }");
        using var before = new PEReader(new MemoryStream(oldImage));
        using var after = new PEReader(new MemoryStream(newImage));
        var state = Assert.Single(FindingDiffAny.Compare(before, after,
            cancellationToken: TestContext.Current.CancellationToken).Types, t => t.FullName == "C");
        Assert.Equal(FindingDiffAnyState.Unchanged, state.Api);
        Assert.Equal(FindingDiffAnyState.Unchanged, state.Body);
    }

    static readonly ApiSurfaceExtractionScope[] Views =
        [ApiSurfaceExtractionScope.ApiAxis, ApiSurfaceExtractionScope.IncludeAll];

    internal sealed record ClosureAgreement(
        int Types,
        int Identical,
        int FalseChanges,
        bool ContextEqual,
        List<string> Misses);

    /// <summary>
    /// Compares one pair through the production comparer at one view and
    /// classifies every Type both images define and either view admits.
    /// </summary>
    internal static ClosureAgreement Measure(
        string oldPath,
        string newPath,
        ApiSurfaceExtractionScope view)
    {
        using var oldPe = new PEReader(File.OpenRead(oldPath));
        using var newPe = new PEReader(File.OpenRead(newPath));
        ApiSurface oldSurface = ApiSurfaceExtractor.Extract(oldPe, view);
        ApiSurface newSurface = ApiSurfaceExtractor.Extract(newPe, view);

        // The arguments of ApiComparisonQuery.Execute, which diff --library uses.
        ApiFindingComparison comparison = MetadataFindings.CompareApi(
            oldSurface,
            newSurface,
            Subject,
            new ApiDiffOptions(ApiDiffScope.Signature),
            memberAcceptanceThreshold: MetadataFindings.ExtensionInstanceMatchTier.Confidence);
        HashSet<string> rows = FindingDiffAnyTests.ChangedTypes(comparison);

        MetadataReader oldReader = oldPe.GetMetadataReader();
        MetadataReader newReader = newPe.GetMetadataReader();
        var oldHash = new ApiTypeClosureHash(oldReader);
        var newHash = new ApiTypeClosureHash(newReader);
        bool contextEqual = oldHash.Context() is { } oldContext
            && newHash.Context() is { } newContext
            && oldContext == newContext;

        Dictionary<int, string> oldNames = SurfaceNames(oldSurface);
        Dictionary<int, string> newNames = SurfaceNames(newSurface);
        Dictionary<string, TypeDefinitionHandle> newDefinitions = Definitions(newReader);

        int types = 0;
        int identical = 0;
        int falseChanges = 0;
        var misses = new List<string>();
        foreach ((string identity, TypeDefinitionHandle oldHandle) in Definitions(oldReader))
        {
            if (!newDefinitions.TryGetValue(identity, out TypeDefinitionHandle newHandle))
                continue;
            oldNames.TryGetValue(MetadataTokens.GetToken(oldHandle), out string? oldName);
            newNames.TryGetValue(MetadataTokens.GetToken(newHandle), out string? newName);
            if (oldName is null && newName is null)
                continue;

            types++;
            bool equal = contextEqual
                && oldHash.Type(oldHandle, view) is { } before
                && newHash.Type(newHandle, view) is { } after
                && before == after;
            bool hasRow = (oldName is not null && rows.Contains(oldName))
                || (newName is not null && rows.Contains(newName));
            if (equal)
            {
                identical++;
                if (hasRow)
                    misses.Add($"{Path.GetFileName(oldPath)} {identity}");
            }
            else if (!hasRow)
            {
                falseChanges++;
            }
        }

        return new ClosureAgreement(types, identical, falseChanges, contextEqual, misses);
    }

    static Dictionary<int, string> SurfaceNames(ApiSurface surface)
    {
        var names = new Dictionary<int, string>();
        foreach (ApiType type in surface.Types)
        {
            if (type.MetadataToken is { } token)
                names.TryAdd(token, type.FullName);
        }
        return names;
    }

    static Dictionary<string, TypeDefinitionHandle> Definitions(MetadataReader reader)
    {
        var definitions = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            if (MetadataTypeDefinitionName.Read(reader, handle) is MetadataTypeDefinitionNameReadResult.Read read)
                definitions.TryAdd(read.Name.ToEscapedFullName(), handle);
        }
        return definitions;
    }

    /// <summary>
    /// The production comparer reports a row for <paramref name="typeName"/>
    /// in every view, so a closure fact that tells the two images apart is
    /// load-bearing.
    /// </summary>
    static void AssertComparerRow(byte[] first, byte[] second, string typeName)
    {
        foreach (ApiSurfaceExtractionScope view in Views)
            Assert.Contains(typeName, ComparerRows(first, second, view));
    }

    static HashSet<string> ComparerRows(byte[] first, byte[] second, ApiSurfaceExtractionScope view)
    {
        using var before = new PEReader(new MemoryStream(first));
        using var after = new PEReader(new MemoryStream(second));
        return FindingDiffAnyTests.ChangedTypes(MetadataFindings.CompareApi(
            ApiSurfaceExtractor.Extract(before, view),
            ApiSurfaceExtractor.Extract(after, view),
            Subject,
            new ApiDiffOptions(ApiDiffScope.Signature),
            memberAcceptanceThreshold: MetadataFindings.ExtensionInstanceMatchTier.Confidence));
    }

    static void AssertEqualHash(byte[] first, byte[] second, string typeName)
    {
        foreach (ApiSurfaceExtractionScope view in Views)
        {
            (UInt128? before, UInt128? after) = Hashes(first, second, typeName, view);
            Assert.NotNull(before);
            Assert.True(before == after, $"{typeName} hashes differ at {view}.");
        }
    }

    static void AssertDifferentHash(byte[] first, byte[] second, string typeName)
    {
        foreach (ApiSurfaceExtractionScope view in Views)
        {
            (UInt128? before, UInt128? after) = Hashes(first, second, typeName, view);
            Assert.NotNull(before);
            Assert.NotNull(after);
            Assert.True(before != after, $"{typeName} hashes are equal at {view}.");
        }
    }

    static (UInt128? Before, UInt128? After) Hashes(
        byte[] first,
        byte[] second,
        string typeName,
        ApiSurfaceExtractionScope view)
    {
        using var before = new PEReader(new MemoryStream(first));
        using var after = new PEReader(new MemoryStream(second));
        var beforeHash = new ApiTypeClosureHash(before.GetMetadataReader());
        var afterHash = new ApiTypeClosureHash(after.GetMetadataReader());
        Assert.Equal(beforeHash.Context(), afterHash.Context());
        return (
            beforeHash.Type(Definition(before.GetMetadataReader(), typeName), view),
            afterHash.Type(Definition(after.GetMetadataReader(), typeName), view));
    }

    static string Encode(byte[] image, string typeName, ApiSurfaceExtractionScope view)
    {
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        return new ApiTypeClosureHash(reader).Encode(Definition(reader, typeName), view)
            ?? throw new InvalidOperationException($"{typeName} has no closure.");
    }

    static TypeDefinitionHandle Definition(MetadataReader reader, string typeName)
        => Assert.Single(
            reader.TypeDefinitions,
            handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == typeName);

    static int TypeReferenceRow(byte[] image, string typeName)
    {
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        return MetadataTokens.GetRowNumber(Assert.Single(
            reader.TypeReferences,
            handle => reader.GetString(reader.GetTypeReference(handle).Name) == typeName));
    }

    static int MethodRow(byte[] image, string methodName)
    {
        using var pe = new PEReader(new MemoryStream(image));
        MetadataReader reader = pe.GetMetadataReader();
        return MetadataTokens.GetRowNumber(Assert.Single(
            reader.MethodDefinitions,
            handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName));
    }

    static byte[] Compile(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "ClosureHash",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result =
            compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return image.ToArray();
    }
}
