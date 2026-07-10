using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CSharpFindingsTests
{
    static readonly FindingSubject Subject = new("M", "M");

    [Fact]
    public void CSharpFindings_SameBody_IsExact()
    {
        var body = Statements(
            "    int value = 1;",
            "    Sink(value);",
            "    return value;");

        var result = CSharpFindings.CompareCanonicalized(body, body, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.IsExact);
        Assert.All(result.Pairs, pair => Assert.Equal(PairKind.Present, pair.Kind));
    }

    [Fact]
    public void CSharpFindings_ChangedStatement_AgreesWithCSharpBodyDiffResidual()
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(oldSource, "DiffFixtureSample.DiffSample", "ConstantValue");
        var newMethod = FindMethod(newSource, "DiffFixtureSample.DiffSample", "ConstantValue");

        var classic = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
        var findings = CSharpFindings.Compare(oldSource, oldMethod, newSource, newMethod, Subject);

        Assert.Null(findings.Failure);
        Assert.False(findings.IsExact);
        AssertRemovedAddedLineTextAgree(classic, findings);
    }

    [Fact]
    public void CSharpFindings_RelocatedBlock_IsDetectedAsMoved()
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

        var result = CSharpFindings.CompareCanonicalized(old, @new, Subject);

        Assert.Null(result.Failure);
        Assert.False(result.IsExact);
        Assert.Equal(2, result.Pairs.Count(pair => pair.Difference == FindingDifferenceKind.Moved));
        Assert.Equal(0, result.Pairs.Count(pair => pair.Kind is PairKind.Added or PairKind.Removed));
        Assert.True(FindingEquivalence.Multiset.IsEquivalent(result.Pairs));
    }

    [Theory]
    [InlineData("Stable")]
    [InlineData("ConstantValue")]
    [InlineData("MultipleHunks")]
    [InlineData("StringToken")]
    public void CSharpFindings_Exactness_AgreesWithCSharpBodyDiff_AcrossFixtures(string methodName)
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(oldSource, "DiffFixtureSample.DiffSample", methodName);
        var newMethod = FindMethod(newSource, "DiffFixtureSample.DiffSample", methodName);

        var classic = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
        var findings = CSharpFindings.Compare(oldSource, oldMethod, newSource, newMethod, Subject);

        Assert.Null(findings.Failure);
        Assert.Equal(classic.IsExact, findings.IsExact);
    }

    [Fact]
    public void CSharpFindings_CommittedCore_ReproducesCSharpBodyDiffResidual()
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(oldSource, "DiffFixtureSample.DiffSample", "MultipleHunks");
        var newMethod = FindMethod(newSource, "DiffFixtureSample.DiffSample", "MultipleHunks");

        var classic = CSharpBodyDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);
        var findings = CSharpFindings.Compare(oldSource, oldMethod, newSource, newMethod, Subject);

        Assert.Null(findings.Failure);
        Assert.Equal(0, findings.Pairs.Count(pair => pair.Difference == FindingDifferenceKind.Moved));
        AssertRemovedAddedLineTextAgree(classic, findings);
    }

    [Fact]
    public void CSharpFindings_MismatchResistance_SingletonStaysAddedRemoved()
    {
        var old = Statements(
            "    First();",
            "    Second();",
            "    Third();");
        var @new = Statements(
            "    Second();",
            "    Third();",
            "    First();");

        var result = CSharpFindings.CompareCanonicalized(old, @new, Subject);

        Assert.Equal(0, result.Pairs.Count(pair => pair.Difference == FindingDifferenceKind.Moved));
        Assert.Equal(1, result.Pairs.Count(pair => pair.Kind == PairKind.Added));
        Assert.Equal(1, result.Pairs.Count(pair => pair.Kind == PairKind.Removed));
    }

    [Fact]
    public void CSharpFindings_Inspect_IsACensus_QueriedWithLinq()
    {
        using var source = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        var method = FindMethod(source, "DiffFixtureSample.DiffSample", "ConstantValue");

        var inspection = CSharpFindings.Inspect(source, method, Subject);
        var body = CompleteInspection(inspection);
        IEnumerable<Finding<CSharpCanonicalLine>> census = body.Findings;

        Assert.Contains(census, finding => finding.Descriptor.Id == "csharp.line");
        var list = census.ToList();
        Assert.NotEmpty(list);
        Assert.Equal(Enumerable.Range(0, list.Count), list.Select(finding => finding.Position));
    }

    [Fact]
    public void CSharpFindings_Inspect_EmptyBodyIsBodyWithEmptyCensus()
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location);
        var method = FindMethod(source, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Noop));

        var inspection = CSharpFindings.Inspect(source, method, Subject);
        var body = CompleteInspection(inspection);

        Assert.Empty(body.Findings);
    }

    [Fact]
    public void CSharpFindings_Inspect_AbsentBodyIsDistinctFromEmptyBody()
    {
        using var source = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        var method = FindMethod(source, "DiffFixtureSample.BodyStateSample", "BodyState");

        var inspection = CSharpFindings.Inspect(source, method, Subject);

        Assert.True(inspection is FindingInspection<CSharpCanonicalLine>.Absent);
    }

    [Fact]
    public void CSharpFindings_Compare_AbsentBodyToBodyIsAValidTransition()
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(oldSource, "DiffFixtureSample.BodyStateSample", "BodyState");
        var newMethod = FindMethod(newSource, "DiffFixtureSample.BodyStateSample", "BodyState");

        var result = CSharpFindings.Compare(
            oldSource, oldMethod, newSource, newMethod, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.OldInspection is FindingInspection<CSharpCanonicalLine>.Absent);
        Assert.True(result.NewInspection is FindingInspection<CSharpCanonicalLine>.Complete);
        Assert.False(result.IsExact);
        Assert.All(result.Pairs, pair => Assert.Equal(PairKind.Added, pair.Kind));
    }

    [Fact]
    public void CSharpFindings_Compare_EmptyBodyAndAbsentBodyAreNotExact()
    {
        using var bodySource = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location);
        using var absentSource = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        var bodyMethod = FindMethod(
            bodySource, typeof(CfgSampleClass).FullName!, nameof(CfgSampleClass.Noop));
        var absentMethod = FindMethod(
            absentSource, "DiffFixtureSample.BodyStateSample", "BodyState");

        var result = CSharpFindings.Compare(
            bodySource, bodyMethod, absentSource, absentMethod, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.OldInspection is FindingInspection<CSharpCanonicalLine>.Complete);
        Assert.True(result.NewInspection is FindingInspection<CSharpCanonicalLine>.Absent);
        Assert.Empty(result.Pairs);
        Assert.False(result.IsExact);
    }

    [Fact]
    public void CSharpFindings_Compare_AbsentBodiesAreExact()
    {
        using var source = MetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        var method = FindMethod(source, "DiffFixtureSample.BodyStateSample", "BodyState");

        var result = CSharpFindings.Compare(
            source, method, source, method, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.OldInspection is FindingInspection<CSharpCanonicalLine>.Absent);
        Assert.True(result.NewInspection is FindingInspection<CSharpCanonicalLine>.Absent);
        Assert.Empty(result.Pairs);
        Assert.True(result.IsExact);
    }

    [Fact]
    public void CSharpFindingsResult_RejectsInvalidPublicStates()
    {
        FindingInspection<CSharpCanonicalLine> body
            = new FindingInspection<CSharpCanonicalLine>.Complete([]);
        var match = new FindingMatch([], []);

        Assert.Throws<ArgumentException>(
            () => new CSharpFindingsResult(default, match, body, body));
        Assert.Throws<ArgumentNullException>(
            () => new CSharpFindingsResult([], null!, body, body));
        Assert.Throws<ArgumentException>(
            () => CSharpFindingsResult.Failed(body, body));
    }

    [Fact]
    public void CSharpFindingsResult_DerivesFailureFromCensusErrors()
    {
        FindingInspection<CSharpCanonicalLine> body
            = new FindingInspection<CSharpCanonicalLine>.Complete([]);
        FindingInspection<CSharpCanonicalLine> oldFailed
            = new FindingInspection<CSharpCanonicalLine>.Failed(
                new CensusError(Subject, CSharpFindings.InspectionDescriptor, "old failure"));
        FindingInspection<CSharpCanonicalLine> newFailed
            = new FindingInspection<CSharpCanonicalLine>.Failed(
                new CensusError(Subject, CSharpFindings.InspectionDescriptor, "new failure"));

        var oldOnly = CSharpFindingsResult.Failed(oldFailed, body);
        var both = CSharpFindingsResult.Failed(oldFailed, newFailed);

        Assert.Equal("old: old failure", oldOnly.Failure);
        Assert.Equal("old: old failure; new: new failure", both.Failure);
        Assert.False(oldOnly.IsExact);
        Assert.False(both.IsExact);
    }

    [Theory]
    [InlineData(typeof(CSharpBodyDiff))]
    [InlineData(typeof(CSharpFindingsTests))]
    [InlineData(typeof(System.Linq.Enumerable))]
    public void CSharpFindings_SelfCompare_IsExactAcrossWholeAssembly(Type anchorType)
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
            var subject = new FindingSubject(token, "m");
            var findings = CSharpFindings.Compare(source, handle, source, handle, subject);
            if (findings.Failure is not null)
            {
                declined++;
                continue;
            }

            Assert.True(findings.IsExact, $"CSharpFindings self-compare not exact for token {token}");
            Assert.All(findings.Pairs, pair => Assert.Equal(PairKind.Present, pair.Kind));
            canonicalized++;
        }

        Assert.True(canonicalized > 50, $"expected a substantial corpus; canonicalized={canonicalized} declined={declined}");
        Assert.True(declined <= canonicalized / 10, $"too many canonicalization declines: canonicalized={canonicalized} declined={declined}");
    }

    static ImmutableArray<CSharpCanonicalLine> Statements(params string[] lines)
        => CSharpBodyDiff.CanonicalizeRenderedLines(lines);

    static FindingInspection<CSharpCanonicalLine>.Complete CompleteInspection(
        FindingInspection<CSharpCanonicalLine> inspection)
        => inspection switch
        {
            FindingInspection<CSharpCanonicalLine>.Complete complete => complete,
            _ => throw new InvalidOperationException("Expected a complete inspection."),
        };

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

    static void AssertRemovedAddedLineTextAgree(CSharpBodyDiffResult classic, CSharpFindingsResult findings)
    {
        Assert.Equal(ClassicLineTexts(classic, CSharpDiffKind.Remove), FindingTexts(findings, PairKind.Removed));
        Assert.Equal(ClassicLineTexts(classic, CSharpDiffKind.Add), FindingTexts(findings, PairKind.Added));
    }

    static string[] ClassicLineTexts(CSharpBodyDiffResult classic, CSharpDiffKind kind)
        => classic.Rows
            .Select(row => kind == CSharpDiffKind.Remove ? row.OldOperation : row.NewOperation)
            .Where(operation => operation?.Kind == CSharpDiffOperationKind.Line)
            .Select(operation => operation!.Value)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

    static string[] FindingTexts(CSharpFindingsResult findings, PairKind kind)
        => findings.Pairs
            .Where(pair => pair.Kind == kind)
            .Select(pair => AtomForKind(pair, kind).Payload.Text)
            .OrderBy(text => text, StringComparer.Ordinal)
            .ToArray();

    static Finding<CSharpCanonicalLine> AtomForKind(PairFinding<CSharpCanonicalLine> pair, PairKind kind)
        => (pair, kind) switch
        {
            (PairFinding<CSharpCanonicalLine>.Added added, PairKind.Added) => added.New,
            (PairFinding<CSharpCanonicalLine>.Removed removed, PairKind.Removed) => removed.Old,
            (PairFinding<CSharpCanonicalLine>.Present present, PairKind.Present) => present.New,
            (PairFinding<CSharpCanonicalLine>.Changed changed, PairKind.Changed) => changed.New,
            _ => throw new ArgumentException("Pair kind does not match the requested atom side.", nameof(pair)),
        };
}
