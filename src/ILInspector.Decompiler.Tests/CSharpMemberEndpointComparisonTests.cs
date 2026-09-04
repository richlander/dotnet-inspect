using System.Collections.Immutable;
using System.Reflection.Metadata;

using DotnetInspector.Fixtures;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

public class CSharpMemberEndpointComparisonTests
{
    [Fact]
    public void CompareMemberEndpoints_BodyfulPair_RetainsFindingAndNativeResults()
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(
            oldSource,
            "DiffFixtureSample.DiffSample",
            "ConstantValue");
        var newMethod = FindMethod(
            newSource,
            "DiffFixtureSample.DiffSample",
            "ConstantValue");
        var oldSubject = Subject("old");
        var newSubject = Subject("new");
        var legacy = CSharpBodyDiff.CompareMembers(
            oldSource,
            oldMethod,
            newSource,
            newMethod);

        var result = CSharpBodyDiff.CompareMemberEndpoints(
            Present(oldSubject, oldSource, oldMethod),
            Present(newSubject, newSource, newMethod));

        var comparison = Assert.IsType<FindingComparison<CSharpCanonicalLine>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.New);
        Assert.Same(oldSubject, result.Old);
        Assert.Same(newSubject, result.New);
        Assert.NotNull(result.BodyDiff);
        Assert.Equal(legacy.Rows.ToArray(), result.BodyDiff.Rows.ToArray());
        Assert.Equal(legacy.FailureRows.ToArray(), result.BodyDiff.FailureRows.ToArray());
        Assert.Equal(
            legacy.IdentityFailures.ToArray(),
            result.BodyDiff.IdentityFailures.ToArray());
    }

    [Fact]
    public void CompareMemberEndpoints_BodylessAndBodyful_UsesNoApplicableInputWithoutBodyDiff()
    {
        using var oldSource = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethod(
            oldSource,
            "DiffFixtureSample.BodyStateSample",
            "BodyState");
        var newMethod = FindMethod(
            newSource,
            "DiffFixtureSample.BodyStateSample",
            "BodyState");

        var result = CSharpBodyDiff.CompareMemberEndpoints(
            Present(Subject("old"), oldSource, oldMethod),
            Present(Subject("new"), newSource, newMethod));

        var comparison = Assert.IsType<FindingComparison<CSharpCanonicalLine>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.NoApplicableInput, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.New);
        Assert.Null(result.BodyDiff);
    }

    [Fact]
    public void CompareMemberEndpoints_BodyfulAndSubjectAbsent_RetainsExplicitAbsenceWithoutBodyDiff()
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.NewAssemblyPath());
        var method = FindMethod(
            source,
            "DiffFixtureSample.DiffSample",
            "ConstantValue");
        var absentSubject = Subject("new");

        var result = CSharpBodyDiff.CompareMemberEndpoints(
            Present(Subject("old"), source, method),
            new CSharpMemberDiffEndpoint.SubjectAbsent(
                absentSubject,
                "Exact subject is absent."));

        var comparison = Assert.IsType<FindingComparison<CSharpCanonicalLine>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.SubjectAbsent, comparison.Transition.New);
        var absent = Assert.IsType<FindingInspection<CSharpCanonicalLine>.Absent>(
            result.Findings.NewInspection.Value);
        Assert.Equal(FindingInspectionAbsenceKind.SubjectAbsent, absent.Kind);
        Assert.Equal("Exact subject is absent.", absent.Detail);
        Assert.Same(absentSubject, result.New);
        Assert.Null(result.BodyDiff);
    }

    [Fact]
    public void CompareMemberEndpoints_SubjectAbsentAndBodyful_RetainsAddedCSharpFindingsWithoutBodyDiff()
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.NewAssemblyPath());
        var method = FindMethod(
            source,
            "DiffFixtureSample.DiffSample",
            "ConstantValue");
        var oldSubject = Subject("old");
        var newSubject = Subject("new");

        var result = CSharpBodyDiff.CompareMemberEndpoints(
            new CSharpMemberDiffEndpoint.SubjectAbsent(
                oldSubject,
                "Exact subject is absent."),
            Present(newSubject, source, method));

        var comparison = Assert.IsType<FindingComparison<CSharpCanonicalLine>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.SubjectAbsent, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.Complete, comparison.Transition.New);
        Assert.Empty(comparison.OldAtoms);
        Assert.NotEmpty(comparison.NewAtoms);
        Assert.Equal(comparison.NewAtoms.Length, comparison.Pairs.Length);
        Assert.All(
            comparison.Pairs,
            pair => Assert.IsType<PairFinding<CSharpCanonicalLine>.Added>(pair.Value));
        Assert.Same(oldSubject, result.Old);
        Assert.Same(newSubject, result.New);
        Assert.Null(result.BodyDiff);
    }

    [Fact]
    public void CompareMemberEndpoints_BothSubjectAbsent_IsExactWithoutBodyDiff()
    {
        var oldSubject = Subject("old");
        var newSubject = Subject("new");

        var result = CSharpBodyDiff.CompareMemberEndpoints(
            new CSharpMemberDiffEndpoint.SubjectAbsent(oldSubject),
            new CSharpMemberDiffEndpoint.SubjectAbsent(newSubject));

        var comparison = Assert.IsType<FindingComparison<CSharpCanonicalLine>.Complete>(
            result.Findings.Value);
        Assert.Equal(FindingInspectionState.SubjectAbsent, comparison.Transition.Old);
        Assert.Equal(FindingInspectionState.SubjectAbsent, comparison.Transition.New);
        Assert.True(result.Findings.IsExact);
        Assert.Same(oldSubject, result.Old);
        Assert.Same(newSubject, result.New);
        Assert.Null(result.BodyDiff);
    }

    [Fact]
    public void CompareMemberEndpoints_FailedInspection_RetainsFailureWithoutBodyDiff()
    {
        using var failedSource = MetadataSource.OpenFromPrefetchedImage(
            "Synthetic.dll",
            [.. CSharpBodyDiffRelationshipFailureTests.BuildBodyReferenceImage()]);
        using var validSource = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.NewAssemblyPath());
        var failedMethod = FindMethod(failedSource, "Valid", "M");
        var validMethod = FindMethod(
            validSource,
            "DiffFixtureSample.DiffSample",
            "ConstantValue");

        var result = CSharpBodyDiff.CompareMemberEndpoints(
            Present(Subject("old"), failedSource, failedMethod),
            Present(Subject("new"), validSource, validMethod));

        Assert.IsType<FindingComparison<CSharpCanonicalLine>.Failed>(
            result.Findings.Value);
        var failed = Assert.IsType<FindingInspection<CSharpCanonicalLine>.Failed>(
            result.Findings.OldInspection.Value);
        Assert.Contains("Cycle", failed.Error.Reason, StringComparison.Ordinal);
        Assert.IsType<FindingInspection<CSharpCanonicalLine>.Complete>(
            result.Findings.NewInspection.Value);
        Assert.Null(result.BodyDiff);
    }

    [Fact]
    public void PresentEndpoint_RejectsNullAndNilEvidence()
    {
        using var source = MetadataSource.OpenWithoutSymbols(
            FixtureCatalog.DiffPair.NewAssemblyPath());
        var subject = Subject("member");
        var method = FindMethod(
            source,
            "DiffFixtureSample.DiffSample",
            "ConstantValue");

        Assert.Throws<ArgumentNullException>(
            () => new CSharpMemberDiffEndpoint.Present(null!, source, method));
        Assert.Throws<ArgumentNullException>(
            () => new CSharpMemberDiffEndpoint.Present(subject, null!, method));
        Assert.Throws<ArgumentException>(
            () => new CSharpMemberDiffEndpoint.Present(subject, source, default));
        Assert.Throws<ArgumentNullException>(
            () => new CSharpMemberDiffEndpoint.SubjectAbsent(null!));
        Assert.Throws<ArgumentNullException>(
            () => CSharpBodyDiff.CompareMemberEndpoints(
                null!,
                new CSharpMemberDiffEndpoint.SubjectAbsent(subject)));
        Assert.Throws<ArgumentNullException>(
            () => CSharpBodyDiff.CompareMemberEndpoints(
                new CSharpMemberDiffEndpoint.SubjectAbsent(subject),
                null!));
    }

    static CSharpMemberDiffEndpoint.Present Present(
        FindingSubject subject,
        MetadataSource source,
        MethodDefinitionHandle method)
        => new(subject, source, method);

    static FindingSubject Subject(string key)
        => new(key, key);

    static MethodDefinitionHandle FindMethod(
        MetadataSource source,
        string typeName,
        string methodName)
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

        throw new InvalidOperationException(
            $"Could not find {typeName}.{methodName}.");
    }
}
