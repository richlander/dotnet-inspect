using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.ILDiff;
using Inspector.Findings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.Metadata.Tests;

public sealed class FindingDiffAnyTests
{
    static readonly FindingSubject Subject = new("api", "API");

    [Theory]
    [InlineData("FindingDiffAnyFixture.Unchanged", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.BodyOnly", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.LambdaOnly", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.AsyncOnly", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.LocalFunctionOnly", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.StringLiteralOnly", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    // A catch Type is an exception clause fact canonical IL comparison
    // presents (#9942), so the Body axis compares it.
    [InlineData("FindingDiffAnyFixture.CatchTypeOnly", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.AbstractToVirtual", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.IDefaultImplementationAdded", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberAdded", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PrivateFieldAdded", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PrivateCalleeChanged", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PublicMemberAdded", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.MethodConstraint", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ConstraintType", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ConstantValue", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.DefaultParameter", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ParameterName", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.PublicAttribute", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.InterfaceAdded", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.BecomesPublic", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.Outer.Inner", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.NullableOuter", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.NullableOuter.Inner", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.Outer", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.Generic`1", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.InternalBodyOnly", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.Added", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.Removed", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.HiddenMemberChanged", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.HiddenTypeChanged", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ProtectedMemberChanged", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.PrivateProtectedMemberAdded", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PublicToProtected", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.PublicToProtectedInternal", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToProtected", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ProtectedToProtectedInternal", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToPublic", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.SetterToProtected", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.NestedPublicToProtected", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.NestedPublicToProtected.Nested", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.NestedProtectedToPublic", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.NestedProtectedToPublic.Nested", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.PublicToInternal", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PublicToPrivate", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.ProtectedToInternal", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.InternalToPrivate", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.ProtectedToPrivateProtected", FindingDiffAnyState.Changed, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PublicTypeToInternal", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.PropertyPublicToProtected", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.PropertyWithPrivateSetterPublicToProtected", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberRemoved", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberChanged", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.InternalTypeRemoved", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.InternalTypeMemberChanged", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.InternalOuterNestedMemberChanged.Nested", FindingDiffAnyState.Unchanged, FindingDiffAnyState.Changed)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesHidden", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesVisible", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.TypeBecomesHidden", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesObsolete", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ObsoleteWarningToError", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.TypeBecomesObsolete", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.ObsoleteMessageChanged", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesObsoleteError", FindingDiffAnyState.Changed, FindingDiffAnyState.Unchanged)]
    public void TypeStates_SeparateApiAndBodyChanges(
        string fullName,
        FindingDiffAnyState api,
        FindingDiffAnyState body)
    {
        FindingDiffAnyTypeState state = Assert.Single(
            Compare(FixtureCatalog.MetadataFindingDiffAnyPair).Types,
            type => type.FullName == fullName);

        Assert.Equal((api, body), (state.Api, state.Body));
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.Outer.Inner", "FindingDiffAnyFixture.Outer+Inner")]
    [InlineData("FindingDiffAnyFixture.Generic`1", "FindingDiffAnyFixture.Generic`1")]
    [InlineData("FindingDiffAnyFixture.Added", "FindingDiffAnyFixture.Added")]
    public void TypeStates_CarryTheEscapedDefinitionIdentifier(
        string fullName,
        string identifier)
    {
        FindingDiffAnyTypeState state = Assert.Single(
            Compare(FixtureCatalog.MetadataFindingDiffAnyPair).Types,
            type => type.FullName == fullName);

        Assert.Equal(identifier, state.Identifier);
    }

    [Fact]
    public void GeneratedTypes_FoldIntoTheirDeclaredOwner()
    {
        FindingDiffAnyResult result = Compare(FixtureCatalog.MetadataFindingDiffAnyPair);

        Assert.DoesNotContain(result.Types, type => type.FullName.Contains('<'));
    }

    [Theory]
    [InlineData("finding-diff-any")]
    [InlineData("diff")]
    [InlineData("library-api-diff")]
    public void ApiUnchanged_IsSoundAgainstTheCompletePublicApiComparison(string pairName)
    {
        FixturePair pair = PairOf(pairName);
        ApiSurface before = Surface(pair.OldAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis);
        ApiSurface after = Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis);
        ApiFindingComparison comparison =
            MetadataFindings.CompareApi(
                before, after, Subject, new ApiDiffOptions(ApiDiffScope.All));
        HashSet<string> changed = ChangedTypes(comparison);

        FindingDiffAnyResult result = Compare(pair);

        Assert.NotEmpty(changed);
        Assert.All(changed, typeName =>
        {
            FindingDiffAnyTypeState? state = result.Types.FirstOrDefault(
                type => type.FullName == typeName);
            Assert.True(
                state is { Api: not FindingDiffAnyState.Unchanged },
                $"{typeName} changes in the complete API comparison but Finding Diff Any reports {state?.Api}.");
        });
        Assert.All(
            result.Types.Where(type => type.Api == FindingDiffAnyState.Changed
                && !KnownApiOverReports.Contains((pairName, type.FullName))),
            type => Assert.True(
                changed.Contains(type.FullName),
                $"Finding Diff Any reports {type.FullName} API Changed but the complete API comparison does not."));
    }

    /// <summary>
    /// Types Finding Diff Any reports API <c>Changed</c> that the complete API axis comparison
    /// does not, each a known pre-existing divergence filed for follow-up rather than an
    /// accepted difference.
    /// </summary>
    static readonly HashSet<(string Pair, string Type)> KnownApiOverReports =
    [
        // A non-enum constant's value: Finding Diff Any compares the Constant row, while the
        // complete comparison does not compare ApiMember.ConstantValueLiteral.
        ("finding-diff-any", "FindingDiffAnyFixture.ConstantValue"),
    ];

    [Theory]
    [InlineData("finding-diff-any")]
    [InlineData("diff")]
    [InlineData("library-api-diff")]
    public void BodyUnchanged_IsSoundAgainstCanonicalIlComparison(string pairName)
    {
        FixturePair pair = PairOf(pairName);
        IlAssemblyDiffResult il = IlAssemblyDiff.CompareFiles(
            pair.OldAssemblyPath(),
            pair.NewAssemblyPath(),
            maxExamples: int.MaxValue,
            normalization: IlBodyDiffPolicy.Body).Diff;
        FindingDiffAnyResult result = Compare(pair);

        Assert.NotEmpty(il.Examples);
        Assert.All(il.Examples, example =>
        {
            string owner = DeclaredOwner(example.Method);
            FindingDiffAnyTypeState? state = result.Types.FirstOrDefault(
                type => type.FullName == owner);
            Assert.True(
                state is { Body: not FindingDiffAnyState.Unchanged },
                $"{example.Method} (owner {owner}) changes in canonical IL but Finding Diff Any reports {state?.Body}.");
        });
    }

    /// <summary>
    /// Body exactness: every method pair's Body verdict, under Finding Diff Any's own
    /// pairing, equals canonical IL comparison's verdict for the same two
    /// bodies under the Body policy. <c>Exact</c> is <c>Unchanged</c>, a body
    /// on one side only or any other available verdict is <c>Changed</c>, and
    /// a body canonical comparison cannot decide is <c>Indeterminate</c>. The
    /// one designed departure, a difference decided before a decode fault, has
    /// its own generated case in <c>FindingDiffAnyIdentityTests</c>.
    /// </summary>
    [Theory]
    [InlineData("finding-diff-any")]
    [InlineData("diff")]
    [InlineData("library-api-diff")]
    public void Body_EqualsCanonicalIlComparisonPerMethod(string pairName)
    {
        FixturePair pair = PairOf(pairName);
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        List<FindingDiffAny.BodyVerdict> verdicts = FindingDiffAny.BodyVerdicts(before, after);

        Assert.Contains(verdicts, verdict => verdict.Body == FindingDiffAnyState.Unchanged);
        Assert.Contains(verdicts, verdict => verdict.Body == FindingDiffAnyState.Changed);
        Assert.All(verdicts, verdict =>
        {
            FindingDiffAnyState canonical = CanonicalBodyVerdict(before, after, verdict);
            Assert.True(canonical == verdict.Body, $"{verdict.Type} {verdict.Method}: canonical {canonical}, Finding Diff Any {verdict.Body}.");
        });
    }

    /// <summary>
    /// Canonical IL comparison's verdict for one of Finding Diff Any's method pairs
    /// under the Body policy, in Finding Diff Any's states.
    /// </summary>
    internal static FindingDiffAnyState CanonicalBodyVerdict(PEReader before, PEReader after, FindingDiffAny.BodyVerdict verdict)
    {
        MetadataReader a = before.GetMetadataReader();
        MetadataReader b = after.GetMetadataReader();
        int rvaA = a.GetMethodDefinition(verdict.Before).RelativeVirtualAddress;
        int rvaB = b.GetMethodDefinition(verdict.After).RelativeVirtualAddress;
        if (rvaA == 0 || rvaB == 0)
            return rvaA == rvaB ? FindingDiffAnyState.Unchanged : FindingDiffAnyState.Changed;
        IlBodyDiffResult result = IlBodyDiff.Compare(
            a, before.GetMethodBody(rvaA), b, after.GetMethodBody(rvaB), IlBodyDiffPolicy.Body);
        return result.IsExact
            ? FindingDiffAnyState.Unchanged
            : result.IsAvailable ? FindingDiffAnyState.Changed : FindingDiffAnyState.Indeterminate;
    }

    /// <summary>
    /// The declared Type that owns a canonical IL method display, folding
    /// compiler-generated nested Types into their declaring Type.
    /// </summary>
    static string DeclaredOwner(string method)
    {
        string type = method[..method.IndexOf("::", StringComparison.Ordinal)];
        string[] segments = type.Split('/', '+');
        int declared = segments.Length;
        while (declared > 1 && segments[declared - 1].StartsWith('<'))
            declared--;
        return string.Join('.', segments[..declared]);
    }

    static HashSet<string> ChangedTypes(ApiFindingComparison comparison)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeDiff type in comparison.ApiDiff.TypeDiffs)
            changed.Add(type.TypeFullName);
        if (comparison.Types.Value is FindingComparison<ApiTypeHandle>.Complete types)
        {
            foreach (PairFinding<ApiTypeHandle> pair in types.Pairs)
            {
                if (pair.Kind != PairKind.Present)
                    changed.Add(TypeName(((IPairFinding)pair).Old, ((IPairFinding)pair).New, (ApiTypeHandle handle) => handle.TypeFullName));
            }
        }
        if (comparison.Members.Value is FindingComparison<ApiMemberHandle>.Complete members)
        {
            foreach (PairFinding<ApiMemberHandle> pair in members.Pairs)
            {
                if (pair.Kind != PairKind.Present)
                    changed.Add(TypeName(((IPairFinding)pair).Old, ((IPairFinding)pair).New, (ApiMemberHandle handle) => handle.TypeFullName));
            }
        }
        return changed;
    }

    static string TypeName<T>(IFinding? old, IFinding? @new, Func<T, string> name)
        where T : notnull
        => name(((Finding<T>?)@new ?? (Finding<T>)old!).Payload);

    static FixturePair PairOf(string name) => name switch
    {
        "finding-diff-any" => FixtureCatalog.MetadataFindingDiffAnyPair,
        "diff" => FixtureCatalog.DiffPair,
        _ => FixtureCatalog.LibraryApiDiffPair,
    };

    static ApiSurface Surface(string path, ApiSurfaceExtractionScope scope)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(reader, scope);
    }

    static ApiFindingComparison CompareFindingDiffAnyPair(ApiSurfaceExtractionScope scope)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        return MetadataFindings.CompareApi(
            Surface(pair.OldAssemblyPath(), scope),
            Surface(pair.NewAssemblyPath(), scope),
            Subject,
            new ApiDiffOptions(ApiDiffScope.All));
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.HiddenMemberChanged")]
    [InlineData("FindingDiffAnyFixture.HiddenTypeChanged")]
    [InlineData("FindingDiffAnyFixture.ProtectedMemberChanged")]
    public void ApiAxis_ComparesHiddenAndProtectedMembers(string typeName)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis);

        Assert.Contains(typeName, ChangedTypes(comparison));
        Assert.Equal(
            FindingDiffAnyState.Changed,
            Assert.Single(Compare(FixtureCatalog.MetadataFindingDiffAnyPair).Types, type => type.FullName == typeName).Api);
    }

    [Fact]
    public void ApiAxis_MarksAHiddenMemberHidden()
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "FindingDiffAnyFixture.HiddenMemberChanged");
        Assert.NotEmpty(type.Changes);
        Assert.All(type.Changes, change =>
        {
            ApiMember member = (change.Subject?.NewMember ?? change.Subject?.OldMember)!.Member;
            Assert.Equal("Hidden", member.Name);
            Assert.True(member.IsHidden);
        });
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.HiddenMemberChanged")]
    [InlineData("FindingDiffAnyFixture.HiddenTypeChanged")]
    [InlineData("FindingDiffAnyFixture.ProtectedMemberChanged")]
    public void OrdinaryPresentation_OmitsHiddenAndProtectedMembers(string typeName)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.Public);

        Assert.DoesNotContain(typeName, ChangedTypes(comparison));
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.PublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PublicToProtectedInternal", "public", "protected internal", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToProtected", "protected internal", "protected", ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedToProtectedInternal", "protected", "protected internal", ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToPublic", "protected internal", "public", ChangeClassification.Additive)]
    [InlineData("FindingDiffAnyFixture.PropertyPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PropertyWithPrivateSetterPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    public void ApiAxis_ReportsMemberAccessibilityChanges(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.MemberAccessibilityChanged, change.Kind);
        Assert.Equal(classification, change.Classification);
        Assert.Equal(oldAccessibility, change.OldValue);
        Assert.Equal(newAccessibility, change.NewValue);
        Assert.Equal("Value", change.Subject?.NewMember?.Member.Name);
        Assert.Equal(
            FindingDiffAnyState.Changed,
            Assert.Single(Compare(FixtureCatalog.MetadataFindingDiffAnyPair).Types, state => state.FullName == typeName).Api);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.NestedPublicToProtected.Nested", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.NestedProtectedToPublic.Nested", "protected", "public", ChangeClassification.Additive)]
    public void ApiAxis_ReportsNestedTypeAccessibilityChanges(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.TypeAccessibilityChanged, change.Kind);
        Assert.Equal(classification, change.Classification);
        Assert.Equal(oldAccessibility, change.OldValue);
        Assert.Equal(newAccessibility, change.NewValue);
    }

    [Fact]
    public void ApiAxis_ReportsAnAccessorAccessibilityChangeAsASignatureChange()
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "FindingDiffAnyFixture.SetterToProtected");
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("int Value { get; set; }", change.OldValue);
        Assert.Equal("int Value { get; protected set; }", change.NewValue);
    }

    [Fact]
    public void OrdinaryPresentation_ReportsAnAccessorAccessibilityChangeAsASignatureChange()
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.Public);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "FindingDiffAnyFixture.SetterToProtected");
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("int Value { get; set; }", change.OldValue);
        Assert.Equal("int Value { get; protected set; }", change.NewValue);
    }

    [Fact]
    public void ApiAxis_MarksAHiddenTypeHidden()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        ApiType type = Assert.Single(
            Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis).Types,
            type => type.FullName == "FindingDiffAnyFixture.HiddenTypeChanged");

        Assert.True(type.IsHidden);
    }

    // Whether every enclosing Type is ordinary is read from metadata, so a Type selection
    // that does not project the enclosing Type keeps it
    // (docs/design/library-api-diff-presentation.md#per-side-facts).
    [Theory]
    [InlineData(ApiSurfaceExtractionScope.ApiAxis)]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll)]
    public void EnclosingTypesOrdinary_RequiresEveryEnclosingTypePublicAndNotHidden(
        ApiSurfaceExtractionScope scope)
    {
        const string source = """
            using System.ComponentModel;
            namespace N;
            public class Outer
            {
                public class Nested { public class Deeper { } }
                protected class Protected { public class InProtected { } }
                [EditorBrowsable(EditorBrowsableState.Never)]
                public class Hidden { public class InHidden { } }
            }
            [EditorBrowsable(EditorBrowsableState.Never)]
            public class HiddenOuter { public class Inner { } }
            """;
        CSharpCompilation compilation = CSharpCompilation.Create(
            "EnclosingOrdinary",
            [CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result =
            compilation.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        image.Position = 0;
        using var reader = new PEReader(image);
        Dictionary<string, bool?> facts = ApiSurfaceExtractor.Extract(reader, scope).Types
            .ToDictionary(type => type.Name, type => type.EnclosingTypesOrdinary);

        Assert.True(facts["Outer"]);
        Assert.True(facts["Outer.Nested"]);
        Assert.True(facts["Outer.Nested.Deeper"]);
        Assert.True(facts["Outer.Protected"]);
        Assert.False(facts["Outer.Protected.InProtected"]);
        Assert.True(facts["Outer.Hidden"]);
        Assert.False(facts["Outer.Hidden.InHidden"]);
        Assert.False(facts["HiddenOuter.Inner"]);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.PublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PublicToProtectedInternal", "public", "protected internal", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToProtected", "protected internal", "protected", ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedToProtectedInternal", "protected", "protected internal", ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToPublic", "protected internal", "public", ChangeClassification.Additive)]
    [InlineData("FindingDiffAnyFixture.PublicToInternal", "public", "internal", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PublicToPrivate", "public", "private", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedToInternal", "protected", "internal", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedToPrivateProtected", "protected", "private protected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.InternalToPrivate", "internal", "private", ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.PropertyPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PropertyWithPrivateSetterPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    public void IncludeAll_ReportsEveryMemberAccessibilityMove(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.IncludeAll);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.MemberAccessibilityChanged, change.Kind);
        Assert.Equal(classification, change.Classification);
        Assert.Equal(oldAccessibility, change.OldValue);
        Assert.Equal(newAccessibility, change.NewValue);
        Assert.Equal("Value", change.Subject?.NewMember?.Member.Name);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.NestedPublicToProtected.Nested", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.NestedProtectedToPublic.Nested", "protected", "public", ChangeClassification.Additive)]
    [InlineData("FindingDiffAnyFixture.PublicTypeToInternal", "public", "internal", ChangeClassification.Breaking)]
    public void IncludeAll_ReportsEveryTypeAccessibilityMove(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.IncludeAll);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.TypeAccessibilityChanged, change.Kind);
        Assert.Equal(classification, change.Classification);
        Assert.Equal(oldAccessibility, change.OldValue);
        Assert.Equal(newAccessibility, change.NewValue);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.PrivateMemberAdded", ChangeKind.MemberAdded)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberRemoved", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberChanged", ChangeKind.MemberSignatureChanged)]
    [InlineData("FindingDiffAnyFixture.PrivateProtectedMemberAdded", ChangeKind.MemberAdded)]
    [InlineData("FindingDiffAnyFixture.InternalTypeRemoved", ChangeKind.TypeRemoved)]
    [InlineData("FindingDiffAnyFixture.InternalTypeMemberChanged", ChangeKind.MemberSignatureChanged)]
    [InlineData("FindingDiffAnyFixture.InternalOuterNestedMemberChanged.Nested", ChangeKind.MemberSignatureChanged)]
    public void IncludeAll_ClassifiesAChangeOutsideTheApiAxisAsNonBreaking(
        string typeName,
        ChangeKind kind)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.IncludeAll);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.PublicToInternal", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.PublicToPrivate", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.ProtectedToInternal", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.ProtectedToPrivateProtected", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.PublicTypeToInternal", ChangeKind.TypeRemoved)]
    public void ApiAxis_ReportsAMoveOffTheAxisAsPopulation(
        string typeName,
        ChangeKind kind)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ApiAxis_DoesNotAdmitAPublicNestedTypeOfAnInternalType()
    {
        // No other assembly can reach a public nested Type of an internal Type, so
        // the API axis admits neither it nor its Members
        // (docs/design/api-population-scope.md).
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis);

        Assert.DoesNotContain(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName.StartsWith(
                "FindingDiffAnyFixture.InternalOuterNestedMemberChanged",
                StringComparison.Ordinal));
    }

    [Fact]
    public void FocusedComparison_KeepsTheExtractionScope()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        FindingComparison<ApiMemberHandle> members = MetadataFindings.CompareApiMembers(
            Surface(pair.OldAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis),
            Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis),
            Subject,
            "FindingDiffAnyFixture.PublicToProtected",
            new ApiDiffOptions(ApiDiffScope.All));

        var complete = Assert.IsType<FindingComparison<ApiMemberHandle>.Complete>(members.Value);
        PairFinding<ApiMemberHandle> value = Assert.Single(
            complete.Pairs,
            finding => finding.Value is PairFinding<ApiMemberHandle>.Changed { New.Payload.MemberName: "Value" });
        Assert.Contains(nameof(ChangeKind.MemberAccessibilityChanged), value.Detail);
    }

    [Theory]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll, ApiSurfaceExtractionScope.IncludeAll, ApiSurfaceExtractionScope.IncludeAll)]
    [InlineData(ApiSurfaceExtractionScope.ApiAxis, ApiSurfaceExtractionScope.ApiAxis, ApiSurfaceExtractionScope.ApiAxis)]
    [InlineData(ApiSurfaceExtractionScope.IncludeAll, ApiSurfaceExtractionScope.Public, null)]
    public void MergedSurface_KeepsTheScopeItsContributorsShare(
        ApiSurfaceExtractionScope requested,
        ApiSurfaceExtractionScope contributor,
        ApiSurfaceExtractionScope? merged)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        var surface = new ApiSurface { ExtractionScope = requested };

        surface.MergeAssemblySurface(Surface(pair.OldAssemblyPath(), contributor));

        Assert.Equal(merged, surface.ExtractionScope);
    }

    [Fact]
    public void MergedIncludeAllSurfaces_ReportAccessibilityMoves()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        ApiSurface Merged(string path)
        {
            var merged = new ApiSurface { ExtractionScope = ApiSurfaceExtractionScope.IncludeAll };
            merged.MergeAssemblySurface(Surface(path, ApiSurfaceExtractionScope.IncludeAll));
            return merged;
        }

        ApiDiff diff = MetadataFindings.CompareApi(
            Merged(pair.OldAssemblyPath()),
            Merged(pair.NewAssemblyPath()),
            Subject,
            new ApiDiffOptions(ApiDiffScope.All)).ApiDiff;

        ApiChange change = Assert.Single(Assert.Single(
            diff.TypeDiffs,
            type => type.TypeFullName == "FindingDiffAnyFixture.PublicToInternal").Changes);
        Assert.Equal(ChangeKind.MemberAccessibilityChanged, change.Kind);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ApiAxis_OmitsAMoveOutsideTheAxis()
    {
        Assert.DoesNotContain(
            "FindingDiffAnyFixture.InternalToPrivate",
            ChangedTypes(CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis)));
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.PublicToProtected", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.PublicToProtectedInternal", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToPublic", ChangeKind.MemberAdded)]
    [InlineData("FindingDiffAnyFixture.NestedPublicToProtected.Nested", ChangeKind.TypeRemoved)]
    [InlineData("FindingDiffAnyFixture.NestedProtectedToPublic.Nested", ChangeKind.TypeAdded)]
    public void OrdinaryPresentation_ReportsAccessibilityAcrossThePublicBoundaryAsPopulation(
        string typeName,
        ChangeKind kind)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.Public);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        Assert.Equal(kind, Assert.Single(type.Changes).Kind);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToProtected")]
    [InlineData("FindingDiffAnyFixture.ProtectedToProtectedInternal")]
    public void OrdinaryPresentation_OmitsProtectedAccessibilityChanges(string typeName)
    {
        ApiFindingComparison comparison = CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.Public);

        Assert.DoesNotContain(typeName, ChangedTypes(comparison));
    }

    [Fact]
    public void ApiAxis_ExcludesPrivateProtectedMembers()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        ApiSurface after = Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis);
        ApiType type = Assert.Single(
            after.Types,
            type => type.FullName == "FindingDiffAnyFixture.PrivateProtectedMemberAdded");

        Assert.DoesNotContain(type.Members, member => member.Name == "Added");
        Assert.DoesNotContain(
            "FindingDiffAnyFixture.PrivateProtectedMemberAdded",
            ChangedTypes(CompareFindingDiffAnyPair(ApiSurfaceExtractionScope.ApiAxis)));
    }

    /// <summary>
    /// API status transitions (docs/design/api-status-analysis.md): each fixture case changes
    /// one subject's hidden or obsolete status alone.
    /// </summary>
    public static TheoryData<string, ChangeKind, ChangeClassification, string, string> StatusTransitions => new()
    {
        { "FindingDiffAnyFixture.MemberBecomesHidden", ChangeKind.MemberHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden" },
        { "FindingDiffAnyFixture.MemberBecomesVisible", ChangeKind.MemberHiddenChanged, ChangeClassification.NonBreaking, "hidden", "visible" },
        { "FindingDiffAnyFixture.TypeBecomesHidden", ChangeKind.TypeHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden" },
        { "FindingDiffAnyFixture.MemberBecomesObsolete", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\")]" },
        { "FindingDiffAnyFixture.ObsoleteWarningToError", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "[Obsolete(\"m\")]", "[Obsolete(\"m\", true)]" },
        { "FindingDiffAnyFixture.TypeBecomesObsolete", ChangeKind.TypeObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\")]" },
        { "FindingDiffAnyFixture.ObsoleteMessageChanged", ChangeKind.MemberObsoleteChanged, ChangeClassification.NonBreaking, "[Obsolete(\"before\")]", "[Obsolete(\"after\")]" },
        { "FindingDiffAnyFixture.MemberBecomesObsoleteError", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\", true)]" },
    };

    [Theory]
    [MemberData(nameof(StatusTransitions))]
    public void ApiAxis_ReportsEachStatusTransitionAsOneRow(
        string typeName,
        ChangeKind kind,
        ChangeClassification classification,
        string oldValue,
        string newValue)
    {
        AssertSingleStatusRow(ApiSurfaceExtractionScope.ApiAxis, typeName, kind, classification, oldValue, newValue);

        // The Exists contract: Changed opens a non-empty full diff.
        Assert.Equal(
            FindingDiffAnyState.Changed,
            Assert.Single(Compare(FixtureCatalog.MetadataFindingDiffAnyPair).Types, state => state.FullName == typeName).Api);
    }

    [Theory]
    [MemberData(nameof(StatusTransitions))]
    public void IncludeAll_ReportsEachStatusTransitionAsOneRow(
        string typeName,
        ChangeKind kind,
        ChangeClassification classification,
        string oldValue,
        string newValue)
        => AssertSingleStatusRow(ApiSurfaceExtractionScope.IncludeAll, typeName, kind, classification, oldValue, newValue);

    static void AssertSingleStatusRow(
        ApiSurfaceExtractionScope scope,
        string typeName,
        ChangeKind kind,
        ChangeClassification classification,
        string oldValue,
        string newValue)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        ApiSurface before = Surface(pair.OldAssemblyPath(), scope);
        ApiSurface after = Surface(pair.NewAssemblyPath(), scope);

        // The signature scope, which hosts compare by default, reports the transition alone.
        TypeDiff type = Assert.Single(
            MetadataFindings.CompareApi(before, after, Subject, ApiDiffOptions.Default).ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(ApiChangeCategory.Status, change.Category);
        Assert.Equal(classification, change.Classification);
        Assert.Equal(oldValue, change.OldValue);
        Assert.Equal(newValue, change.NewValue);
        Assert.Equal(
            kind is ChangeKind.TypeHiddenChanged or ChangeKind.TypeObsoleteChanged
                ? ApiChangeSubjectKind.Type
                : ApiChangeSubjectKind.Member,
            change.Subject?.Kind);

        // Every scope reports exactly one status row for it.
        TypeDiff all = Assert.Single(
            MetadataFindings.CompareApi(before, after, Subject, new ApiDiffOptions(ApiDiffScope.All)).ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        Assert.Equal(kind, Assert.Single(all.Changes, row => row.Category == ApiChangeCategory.Status).Kind);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.MemberBecomesHidden", ChangeKind.MemberRemoved)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesVisible", ChangeKind.MemberAdded)]
    [InlineData("FindingDiffAnyFixture.TypeBecomesHidden", ChangeKind.TypeRemoved)]
    [InlineData("FindingDiffAnyFixture.TypeBecomesObsolete", ChangeKind.TypeRemoved)]
    public void OrdinaryPresentation_ReportsAHiddenOrObsoleteTransitionAsPopulation(
        string typeName,
        ChangeKind kind)
    {
        // The Public scope suppresses hidden subjects and obsolete Types, so it keeps reporting
        // a transition across that boundary as a removal or an addition.
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        TypeDiff type = Assert.Single(
            MetadataFindings.CompareApi(
                Surface(pair.OldAssemblyPath(), ApiSurfaceExtractionScope.Public),
                Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.Public),
                Subject,
                ApiDiffOptions.Default).ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        Assert.Equal(kind, Assert.Single(type.Changes).Kind);
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.MemberBecomesObsolete")]
    [InlineData("FindingDiffAnyFixture.ObsoleteWarningToError")]
    [InlineData("FindingDiffAnyFixture.ObsoleteMessageChanged")]
    [InlineData("FindingDiffAnyFixture.MemberBecomesObsoleteError")]
    public void OrdinaryPresentation_ComparesNoMemberObsoleteStatus(string typeName)
    {
        // The Public scope admits an obsolete Member and compares no status, as before.
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        ApiDiff diff = MetadataFindings.CompareApi(
            Surface(pair.OldAssemblyPath(), ApiSurfaceExtractionScope.Public),
            Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.Public),
            Subject,
            ApiDiffOptions.Default).ApiDiff;

        Assert.DoesNotContain(diff.TypeDiffs, type => type.TypeFullName == typeName);
        Assert.DoesNotContain(
            diff.TypeDiffs.SelectMany(type => type.Changes),
            change => change.Category == ApiChangeCategory.Status);
    }

    [Fact]
    public void ApiAxis_SplitsATypesHiddenAndObsoleteStatus()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        ApiSurface after = Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis);

        ApiType hidden = Assert.Single(after.Types, type => type.FullName == "FindingDiffAnyFixture.TypeBecomesHidden");
        Assert.True(hidden.IsHidden);
        Assert.False(hidden.IsObsolete);
        ApiType obsolete = Assert.Single(after.Types, type => type.FullName == "FindingDiffAnyFixture.TypeBecomesObsolete");
        Assert.False(obsolete.IsHidden);
        Assert.True(obsolete.IsObsolete);
        Assert.Equal("m", obsolete.ObsoleteMessage);
        Assert.False(obsolete.ObsoleteIsError);
    }

    [Fact]
    public void Steps_ResumeToTheWholeResult()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        FindingDiffAnyResult whole = Compare(pair);
        var comparison = new FindingDiffAnyComparison();

        // A zero budget still advances one row or Type per step; each step
        // gets fresh readers over the same images, as a host's callbacks do.
        int steps = 0;
        bool complete = false;
        while (!complete)
        {
            using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
            using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
            complete = comparison.Step(before, after, TimeSpan.Zero, TestContext.Current.CancellationToken);
            steps++;
        }

        Assert.True(steps > whole.Types.Length);
        Assert.Equal(whole.Types, comparison.Result!.Types);
        Assert.Equal(whole.Receipt, comparison.Result.Receipt);
    }

    [Fact]
    public void Steps_RejectADifferentImage()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        var comparison = new FindingDiffAnyComparison();
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        Assert.False(comparison.Step(before, after, TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.Throws<InvalidOperationException>(
            () => comparison.Step(after, before, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ApiAxis_LeavesBodiesUncompared()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        FindingDiffAnyResult whole = Compare(pair);
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));

        FindingDiffAnyResult api = FindingDiffAny.Compare(
            before, after, FindingDiffAnyAxes.Api, TestContext.Current.CancellationToken);

        Assert.All(api.Types, type => Assert.Equal(FindingDiffAnyState.NotCompared, type.Body));
        Assert.Equal(0, api.Receipt.BodiesCompared);
        Assert.Equal(
            whole.Types.Select(type => (type.Identifier, type.Api)),
            api.Types.Select(type => (type.Identifier, type.Api)));
    }

    static FindingDiffAnyResult Compare(FixturePair pair)
    {
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        return FindingDiffAny.Compare(
            before, after, FindingDiffAnyAxes.ApiAndBody, TestContext.Current.CancellationToken);
    }
}
