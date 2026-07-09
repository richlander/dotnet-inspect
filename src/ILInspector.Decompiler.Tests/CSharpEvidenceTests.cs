using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Evidence;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CSharpEvidenceTests
{
    static readonly EvidenceSubject Subject = new("M", "M");

    [Fact]
    public void CSharpEvidence_SameBody_IsExact()
    {
        var body = Statements(
            "    int value = 1;",
            "    Sink(value);",
            "    return value;");

        var result = CSharpEvidence.CompareCanonicalized(body, body, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.IsExact);
        Assert.All(result.Rows, row => Assert.Equal(EvidenceRowKind.Present, row.Kind));
    }

    [Fact]
    public void CSharpEvidence_ChangedStatement_AgreesWithCSharpBodyDiff()
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(oldSource, "DiffFixtureSample.DiffSample", "ConstantValue");
        var newMethod = FindMethod(newSource, "DiffFixtureSample.DiffSample", "ConstantValue");

        var classic = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
        var evidence = CSharpEvidence.Compare(oldSource, oldMethod, newSource, newMethod, Subject);

        Assert.Null(evidence.Failure);
        Assert.False(evidence.IsExact);
        AssertRemovedAddedLineTextAgree(classic, evidence);
    }

    [Fact]
    public void CSharpEvidence_RelocatedBlock_IsDetectedAsMoved()
    {
        var old = Statements(
            "    int first = value + 1;",
            "    int second = value + 2;",
            "    Sink(10);",
            "    Sink(20);",
            "    return first + second;");
        var @new = Statements(
            "    Sink(10);",
            "    Sink(20);",
            "    int first = value + 1;",
            "    int second = value + 2;",
            "    return first + second;");

        var result = CSharpEvidence.CompareCanonicalized(old, @new, Subject);

        Assert.Null(result.Failure);
        Assert.False(result.IsExact);
        Assert.Equal(2, result.Rows.Count(row => row.DifferenceKind == EvidenceDifferenceKind.Moved));
        Assert.Equal(0, result.Rows.Count(row => row.Kind is EvidenceRowKind.Added or EvidenceRowKind.Removed));
        Assert.True(EvidenceEquivalence.Multiset.IsEquivalent(result.Rows));
    }

    [Theory]
    [InlineData("Stable")]
    [InlineData("ConstantValue")]
    [InlineData("MultipleHunks")]
    [InlineData("StringToken")]
    public void CSharpEvidence_Exactness_AgreesWithCSharpBodyDiff_AcrossFixtures(string methodName)
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(oldSource, "DiffFixtureSample.DiffSample", methodName);
        var newMethod = FindMethod(newSource, "DiffFixtureSample.DiffSample", methodName);

        var classic = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
        var evidence = CSharpEvidence.Compare(oldSource, oldMethod, newSource, newMethod, Subject);

        Assert.Null(evidence.Failure);
        Assert.Equal(classic.IsExact, evidence.IsExact);
    }

    [Fact]
    public void CSharpEvidence_CommittedCore_ReproducesCSharpBodyDiffResidual()
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(oldSource, "DiffFixtureSample.DiffSample", "MultipleHunks");
        var newMethod = FindMethod(newSource, "DiffFixtureSample.DiffSample", "MultipleHunks");

        var classic = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
        var evidence = CSharpEvidence.Compare(oldSource, oldMethod, newSource, newMethod, Subject);

        Assert.Null(evidence.Failure);
        Assert.Equal(0, evidence.Rows.Count(row => row.DifferenceKind == EvidenceDifferenceKind.Moved));
        AssertRemovedAddedLineTextAgree(classic, evidence);
    }

    [Fact]
    public void CSharpEvidence_TriviaOnlyMatchedLine_IsEncodingOnly()
    {
        var old = Statements("    return value;");
        var @new = Statements("        return value;   ");

        var result = CSharpEvidence.CompareCanonicalized(old, @new, Subject);

        var row = Assert.Single(result.Rows);
        Assert.Equal(EvidenceRowKind.Present, row.Kind);
        Assert.Equal(EvidenceDifferenceKind.EncodingOnly, row.DifferenceKind);
        Assert.True(result.IsExact);
    }

    [Theory]
    [InlineData(typeof(CSharpBodyDiff))]
    [InlineData(typeof(CSharpEvidenceTests))]
    [InlineData(typeof(System.Linq.Enumerable))]
    public void CSharpEvidence_SelfCompare_IsExactAcrossWholeAssembly(Type anchorType)
    {
        using var source = MetadataSource.OpenWithoutSymbols(anchorType.Assembly.Location);
        var reader = source.Reader;

        int canonicalized = 0;
        int declined = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;

            var token = MetadataTokens.GetToken(handle).ToString(CultureInfo.InvariantCulture);
            var subject = new EvidenceSubject(token, "m");
            var evidence = CSharpEvidence.Compare(source, handle, source, handle, subject);
            if (evidence.Failure is not null)
            {
                declined++;
                continue;
            }

            Assert.True(evidence.IsExact, $"CSharpEvidence self-compare not exact for token {token}");
            Assert.All(evidence.Rows, row => Assert.Equal(EvidenceRowKind.Present, row.Kind));
            canonicalized++;
        }

        Assert.True(canonicalized > 50, $"expected a substantial corpus; canonicalized={canonicalized} declined={declined}");
        Assert.True(declined <= canonicalized / 10, $"too many canonicalization declines: canonicalized={canonicalized} declined={declined}");
    }

    static ImmutableArray<CSharpCanonicalStatement> Statements(params string[] lines)
        => CSharpBodyDiff.CanonicalizeRenderedLines(lines);

    static MethodDefinitionHandle FindMethod(MetadataSource source, string typeName, string methodName)
    {
        var reader = source.Reader;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetFullTypeName(type) != typeName)
                continue;

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) == methodName)
                    return methodHandle;
            }
        }

        throw new InvalidOperationException($"Could not find {typeName}.{methodName}.");
    }

    static void AssertRemovedAddedLineTextAgree(CSharpBodyDiffResult classic, CSharpEvidenceResult evidence)
    {
        Assert.Equal(ClassicLineTexts(classic, CSharpDiffKind.Remove), EvidenceTexts(evidence, EvidenceRowKind.Removed));
        Assert.Equal(ClassicLineTexts(classic, CSharpDiffKind.Add), EvidenceTexts(evidence, EvidenceRowKind.Added));
    }

    static string[] ClassicLineTexts(CSharpBodyDiffResult classic, CSharpDiffKind kind)
        => classic.Rows
            .Select(row => kind == CSharpDiffKind.Remove ? row.OldOperation : row.NewOperation)
            .Where(operation => operation?.Kind == CSharpDiffOperationKind.Line)
            .Select(operation => operation!.Value)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

    static string[] EvidenceTexts(CSharpEvidenceResult evidence, EvidenceRowKind kind)
        => evidence.Rows
            .Where(row => row.Kind == kind)
            .Select(row => Assert.IsType<CSharpDiffOperation>(row.Payload).Value)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();
}
