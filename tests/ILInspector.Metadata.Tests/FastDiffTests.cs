using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.ILDiff;
using Inspector.Findings;

namespace ILInspector.Metadata.Tests;

public sealed class FastDiffTests
{
    static readonly FindingSubject Subject = new("api", "API");

    [Theory]
    [InlineData("FastDiffFixture.Unchanged", FastDiffState.Unchanged, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.BodyOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.LambdaOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.AsyncOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.LocalFunctionOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.StringLiteralOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    // A catch Type is not an IL operation, so canonical IL comparison shows
    // no change for it (#9942) and the Body axis compares it Unchanged.
    [InlineData("FastDiffFixture.CatchTypeOnly", FastDiffState.Unchanged, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.AbstractToVirtual", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.IDefaultImplementationAdded", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PrivateMemberAdded", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PrivateFieldAdded", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PrivateCalleeChanged", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PublicMemberAdded", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.MethodConstraint", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ConstraintType", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ConstantValue", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.DefaultParameter", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ParameterName", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PublicAttribute", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.InterfaceAdded", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.BecomesPublic", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.Outer.Inner", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.NullableOuter", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.NullableOuter.Inner", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.Outer", FastDiffState.Unchanged, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.Generic`1", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.InternalBodyOnly", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.Added", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.Removed", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.HiddenMemberChanged", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.HiddenTypeChanged", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ProtectedMemberChanged", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PrivateProtectedMemberAdded", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PublicToProtected", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PublicToProtectedInternal", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ProtectedInternalToProtected", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ProtectedToProtectedInternal", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ProtectedInternalToPublic", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.SetterToProtected", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.NestedPublicToProtected", FastDiffState.Unchanged, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.NestedPublicToProtected.Nested", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.NestedProtectedToPublic", FastDiffState.Unchanged, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.NestedProtectedToPublic.Nested", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PublicToInternal", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PublicToPrivate", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.ProtectedToInternal", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.InternalToPrivate", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.ProtectedToPrivateProtected", FastDiffState.Changed, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PublicTypeToInternal", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PropertyPublicToProtected", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PropertyWithPrivateSetterPublicToProtected", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.PrivateMemberRemoved", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.PrivateMemberChanged", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.InternalTypeRemoved", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.InternalTypeMemberChanged", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.InternalOuterNestedMemberChanged.Nested", FastDiffState.Unchanged, FastDiffState.Changed)]
    [InlineData("FastDiffFixture.MemberBecomesHidden", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.MemberBecomesVisible", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.TypeBecomesHidden", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.MemberBecomesObsolete", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ObsoleteWarningToError", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.TypeBecomesObsolete", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.ObsoleteMessageChanged", FastDiffState.Changed, FastDiffState.Unchanged)]
    [InlineData("FastDiffFixture.MemberBecomesObsoleteError", FastDiffState.Changed, FastDiffState.Unchanged)]
    public void TypeStates_SeparateApiAndBodyChanges(
        string fullName,
        FastDiffState api,
        FastDiffState body)
    {
        FastDiffTypeState state = Assert.Single(
            Compare(FixtureCatalog.MetadataFastDiffPair).Types,
            type => type.FullName == fullName);

        Assert.Equal((api, body), (state.Api, state.Body));
    }

    [Theory]
    [InlineData("FastDiffFixture.Outer.Inner", "FastDiffFixture.Outer+Inner")]
    [InlineData("FastDiffFixture.Generic`1", "FastDiffFixture.Generic`1")]
    [InlineData("FastDiffFixture.Added", "FastDiffFixture.Added")]
    public void TypeStates_CarryTheEscapedDefinitionIdentifier(
        string fullName,
        string identifier)
    {
        FastDiffTypeState state = Assert.Single(
            Compare(FixtureCatalog.MetadataFastDiffPair).Types,
            type => type.FullName == fullName);

        Assert.Equal(identifier, state.Identifier);
    }

    [Fact]
    public void GeneratedTypes_FoldIntoTheirDeclaredOwner()
    {
        FastDiffResult result = Compare(FixtureCatalog.MetadataFastDiffPair);

        Assert.DoesNotContain(result.Types, type => type.FullName.Contains('<'));
    }

    [Theory]
    [InlineData("fast-diff")]
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

        FastDiffResult result = Compare(pair);

        Assert.NotEmpty(changed);
        Assert.All(changed, typeName =>
        {
            FastDiffTypeState? state = result.Types.FirstOrDefault(
                type => type.FullName == typeName);
            Assert.True(
                state is { Api: not FastDiffState.Unchanged },
                $"{typeName} changes in the complete API comparison but Fast Diff reports {state?.Api}.");
        });
        Assert.All(
            result.Types.Where(type => type.Api == FastDiffState.Changed
                && !KnownApiOverReports.Contains((pairName, type.FullName))),
            type => Assert.True(
                changed.Contains(type.FullName),
                $"Fast Diff reports {type.FullName} API Changed but the complete API comparison does not."));
    }

    /// <summary>
    /// Types Fast Diff reports API <c>Changed</c> that the complete API axis comparison
    /// does not, each a known pre-existing divergence filed for follow-up rather than an
    /// accepted difference.
    /// </summary>
    static readonly HashSet<(string Pair, string Type)> KnownApiOverReports =
    [
        // A non-enum constant's value: Fast Diff compares the Constant row, while the
        // complete comparison does not compare ApiMember.ConstantValueLiteral.
        ("fast-diff", "FastDiffFixture.ConstantValue"),
    ];

    [Theory]
    [InlineData("fast-diff")]
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
        FastDiffResult result = Compare(pair);

        Assert.NotEmpty(il.Examples);
        Assert.All(il.Examples, example =>
        {
            string owner = DeclaredOwner(example.Method);
            FastDiffTypeState? state = result.Types.FirstOrDefault(
                type => type.FullName == owner);
            Assert.True(
                state is { Body: not FastDiffState.Unchanged },
                $"{example.Method} (owner {owner}) changes in canonical IL but Fast Diff reports {state?.Body}.");
        });
    }

    /// <summary>
    /// Body exactness: every method pair's Body verdict, under Fast Diff's own
    /// pairing, equals canonical IL comparison's verdict for the same two
    /// bodies under the Body policy. <c>Exact</c> is <c>Unchanged</c>, a body
    /// on one side only or any other available verdict is <c>Changed</c>, and
    /// a body canonical comparison cannot decide is <c>Indeterminate</c>. The
    /// one designed departure, a difference decided before a decode fault, has
    /// its own generated case in <c>FastDiffIdentityTests</c>.
    /// </summary>
    [Theory]
    [InlineData("fast-diff")]
    [InlineData("diff")]
    [InlineData("library-api-diff")]
    public void Body_EqualsCanonicalIlComparisonPerMethod(string pairName)
    {
        FixturePair pair = PairOf(pairName);
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        List<FastDiff.BodyVerdict> verdicts = FastDiff.BodyVerdicts(before, after);

        Assert.Contains(verdicts, verdict => verdict.Body == FastDiffState.Unchanged);
        Assert.Contains(verdicts, verdict => verdict.Body == FastDiffState.Changed);
        Assert.All(verdicts, verdict =>
        {
            FastDiffState canonical = CanonicalBodyVerdict(before, after, verdict);
            Assert.True(canonical == verdict.Body, $"{verdict.Type} {verdict.Method}: canonical {canonical}, Fast Diff {verdict.Body}.");
        });
    }

    /// <summary>
    /// Canonical IL comparison's verdict for one of Fast Diff's method pairs
    /// under the Body policy, in Fast Diff's states.
    /// </summary>
    internal static FastDiffState CanonicalBodyVerdict(PEReader before, PEReader after, FastDiff.BodyVerdict verdict)
    {
        MetadataReader a = before.GetMetadataReader();
        MetadataReader b = after.GetMetadataReader();
        int rvaA = a.GetMethodDefinition(verdict.Before).RelativeVirtualAddress;
        int rvaB = b.GetMethodDefinition(verdict.After).RelativeVirtualAddress;
        if (rvaA == 0 || rvaB == 0)
            return rvaA == rvaB ? FastDiffState.Unchanged : FastDiffState.Changed;
        IlBodyDiffResult result = IlBodyDiff.Compare(
            a, before.GetMethodBody(rvaA), b, after.GetMethodBody(rvaB), IlBodyDiffPolicy.Body);
        return result.IsExact
            ? FastDiffState.Unchanged
            : result.IsAvailable ? FastDiffState.Changed : FastDiffState.Indeterminate;
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
        "fast-diff" => FixtureCatalog.MetadataFastDiffPair,
        "diff" => FixtureCatalog.DiffPair,
        _ => FixtureCatalog.LibraryApiDiffPair,
    };

    static ApiSurface Surface(string path, ApiSurfaceExtractionScope scope)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(reader, scope);
    }

    static ApiFindingComparison CompareFastDiffPair(ApiSurfaceExtractionScope scope)
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        return MetadataFindings.CompareApi(
            Surface(pair.OldAssemblyPath(), scope),
            Surface(pair.NewAssemblyPath(), scope),
            Subject,
            new ApiDiffOptions(ApiDiffScope.All));
    }

    [Theory]
    [InlineData("FastDiffFixture.HiddenMemberChanged")]
    [InlineData("FastDiffFixture.HiddenTypeChanged")]
    [InlineData("FastDiffFixture.ProtectedMemberChanged")]
    public void ApiAxis_ComparesHiddenAndProtectedMembers(string typeName)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis);

        Assert.Contains(typeName, ChangedTypes(comparison));
        Assert.Equal(
            FastDiffState.Changed,
            Assert.Single(Compare(FixtureCatalog.MetadataFastDiffPair).Types, type => type.FullName == typeName).Api);
    }

    [Fact]
    public void ApiAxis_MarksAHiddenMemberHidden()
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "FastDiffFixture.HiddenMemberChanged");
        Assert.NotEmpty(type.Changes);
        Assert.All(type.Changes, change =>
        {
            ApiMember member = (change.Subject?.NewMember ?? change.Subject?.OldMember)!.Member;
            Assert.Equal("Hidden", member.Name);
            Assert.True(member.IsHidden);
        });
    }

    [Theory]
    [InlineData("FastDiffFixture.HiddenMemberChanged")]
    [InlineData("FastDiffFixture.HiddenTypeChanged")]
    [InlineData("FastDiffFixture.ProtectedMemberChanged")]
    public void OrdinaryPresentation_OmitsHiddenAndProtectedMembers(string typeName)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.Public);

        Assert.DoesNotContain(typeName, ChangedTypes(comparison));
    }

    [Theory]
    [InlineData("FastDiffFixture.PublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PublicToProtectedInternal", "public", "protected internal", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.ProtectedInternalToProtected", "protected internal", "protected", ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.ProtectedToProtectedInternal", "protected", "protected internal", ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.ProtectedInternalToPublic", "protected internal", "public", ChangeClassification.Additive)]
    [InlineData("FastDiffFixture.PropertyPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PropertyWithPrivateSetterPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    public void ApiAxis_ReportsMemberAccessibilityChanges(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis);

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
            FastDiffState.Changed,
            Assert.Single(Compare(FixtureCatalog.MetadataFastDiffPair).Types, state => state.FullName == typeName).Api);
    }

    [Theory]
    [InlineData("FastDiffFixture.NestedPublicToProtected.Nested", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.NestedProtectedToPublic.Nested", "protected", "public", ChangeClassification.Additive)]
    public void ApiAxis_ReportsNestedTypeAccessibilityChanges(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis);

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
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "FastDiffFixture.SetterToProtected");
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("int Value { get; set; }", change.OldValue);
        Assert.Equal("int Value { get; protected set; }", change.NewValue);
    }

    [Fact]
    public void OrdinaryPresentation_ReportsAnAccessorAccessibilityChangeAsASignatureChange()
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.Public);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == "FastDiffFixture.SetterToProtected");
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(ChangeKind.MemberSignatureChanged, change.Kind);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
        Assert.Equal("int Value { get; set; }", change.OldValue);
        Assert.Equal("int Value { get; protected set; }", change.NewValue);
    }

    [Fact]
    public void ApiAxis_MarksAHiddenTypeHidden()
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        ApiType type = Assert.Single(
            Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis).Types,
            type => type.FullName == "FastDiffFixture.HiddenTypeChanged");

        Assert.True(type.IsHidden);
    }

    [Theory]
    [InlineData("FastDiffFixture.PublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PublicToProtectedInternal", "public", "protected internal", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.ProtectedInternalToProtected", "protected internal", "protected", ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.ProtectedToProtectedInternal", "protected", "protected internal", ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.ProtectedInternalToPublic", "protected internal", "public", ChangeClassification.Additive)]
    [InlineData("FastDiffFixture.PublicToInternal", "public", "internal", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PublicToPrivate", "public", "private", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.ProtectedToInternal", "protected", "internal", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.ProtectedToPrivateProtected", "protected", "private protected", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.InternalToPrivate", "internal", "private", ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.PropertyPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PropertyWithPrivateSetterPublicToProtected", "public", "protected", ChangeClassification.Breaking)]
    public void IncludeAll_ReportsEveryMemberAccessibilityMove(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.IncludeAll);

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
    [InlineData("FastDiffFixture.NestedPublicToProtected.Nested", "public", "protected", ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.NestedProtectedToPublic.Nested", "protected", "public", ChangeClassification.Additive)]
    [InlineData("FastDiffFixture.PublicTypeToInternal", "public", "internal", ChangeClassification.Breaking)]
    public void IncludeAll_ReportsEveryTypeAccessibilityMove(
        string typeName,
        string oldAccessibility,
        string newAccessibility,
        ChangeClassification classification)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.IncludeAll);

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
    [InlineData("FastDiffFixture.PrivateMemberAdded", ChangeKind.MemberAdded)]
    [InlineData("FastDiffFixture.PrivateMemberRemoved", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.PrivateMemberChanged", ChangeKind.MemberSignatureChanged)]
    [InlineData("FastDiffFixture.PrivateProtectedMemberAdded", ChangeKind.MemberAdded)]
    [InlineData("FastDiffFixture.InternalTypeRemoved", ChangeKind.TypeRemoved)]
    [InlineData("FastDiffFixture.InternalTypeMemberChanged", ChangeKind.MemberSignatureChanged)]
    [InlineData("FastDiffFixture.InternalOuterNestedMemberChanged.Nested", ChangeKind.MemberSignatureChanged)]
    public void IncludeAll_ClassifiesAChangeOutsideTheApiAxisAsNonBreaking(
        string typeName,
        ChangeKind kind)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.IncludeAll);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        ApiChange change = Assert.Single(type.Changes);
        Assert.Equal(kind, change.Kind);
        Assert.Equal(ChangeClassification.NonBreaking, change.Classification);
    }

    [Theory]
    [InlineData("FastDiffFixture.PublicToInternal", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.PublicToPrivate", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.ProtectedToInternal", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.ProtectedToPrivateProtected", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.PublicTypeToInternal", ChangeKind.TypeRemoved)]
    public void ApiAxis_ReportsAMoveOffTheAxisAsPopulation(
        string typeName,
        ChangeKind kind)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis);

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
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis);

        Assert.DoesNotContain(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName.StartsWith(
                "FastDiffFixture.InternalOuterNestedMemberChanged",
                StringComparison.Ordinal));
    }

    [Fact]
    public void FocusedComparison_KeepsTheExtractionScope()
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        FindingComparison<ApiMemberHandle> members = MetadataFindings.CompareApiMembers(
            Surface(pair.OldAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis),
            Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis),
            Subject,
            "FastDiffFixture.PublicToProtected",
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
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        var surface = new ApiSurface { ExtractionScope = requested };

        surface.MergeAssemblySurface(Surface(pair.OldAssemblyPath(), contributor));

        Assert.Equal(merged, surface.ExtractionScope);
    }

    [Fact]
    public void MergedIncludeAllSurfaces_ReportAccessibilityMoves()
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
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
            type => type.TypeFullName == "FastDiffFixture.PublicToInternal").Changes);
        Assert.Equal(ChangeKind.MemberAccessibilityChanged, change.Kind);
        Assert.Equal(ChangeClassification.Breaking, change.Classification);
    }

    [Fact]
    public void ApiAxis_OmitsAMoveOutsideTheAxis()
    {
        Assert.DoesNotContain(
            "FastDiffFixture.InternalToPrivate",
            ChangedTypes(CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis)));
    }

    [Theory]
    [InlineData("FastDiffFixture.PublicToProtected", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.PublicToProtectedInternal", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.ProtectedInternalToPublic", ChangeKind.MemberAdded)]
    [InlineData("FastDiffFixture.NestedPublicToProtected.Nested", ChangeKind.TypeRemoved)]
    [InlineData("FastDiffFixture.NestedProtectedToPublic.Nested", ChangeKind.TypeAdded)]
    public void OrdinaryPresentation_ReportsAccessibilityAcrossThePublicBoundaryAsPopulation(
        string typeName,
        ChangeKind kind)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.Public);

        TypeDiff type = Assert.Single(
            comparison.ApiDiff.TypeDiffs,
            diff => diff.TypeFullName == typeName);
        Assert.Equal(kind, Assert.Single(type.Changes).Kind);
    }

    [Theory]
    [InlineData("FastDiffFixture.ProtectedInternalToProtected")]
    [InlineData("FastDiffFixture.ProtectedToProtectedInternal")]
    public void OrdinaryPresentation_OmitsProtectedAccessibilityChanges(string typeName)
    {
        ApiFindingComparison comparison = CompareFastDiffPair(ApiSurfaceExtractionScope.Public);

        Assert.DoesNotContain(typeName, ChangedTypes(comparison));
    }

    [Fact]
    public void ApiAxis_ExcludesPrivateProtectedMembers()
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        ApiSurface after = Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis);
        ApiType type = Assert.Single(
            after.Types,
            type => type.FullName == "FastDiffFixture.PrivateProtectedMemberAdded");

        Assert.DoesNotContain(type.Members, member => member.Name == "Added");
        Assert.DoesNotContain(
            "FastDiffFixture.PrivateProtectedMemberAdded",
            ChangedTypes(CompareFastDiffPair(ApiSurfaceExtractionScope.ApiAxis)));
    }

    /// <summary>
    /// API status transitions (docs/design/api-status-analysis.md): each fixture case changes
    /// one subject's hidden or obsolete status alone.
    /// </summary>
    public static TheoryData<string, ChangeKind, ChangeClassification, string, string> StatusTransitions => new()
    {
        { "FastDiffFixture.MemberBecomesHidden", ChangeKind.MemberHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden" },
        { "FastDiffFixture.MemberBecomesVisible", ChangeKind.MemberHiddenChanged, ChangeClassification.NonBreaking, "hidden", "visible" },
        { "FastDiffFixture.TypeBecomesHidden", ChangeKind.TypeHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden" },
        { "FastDiffFixture.MemberBecomesObsolete", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\")]" },
        { "FastDiffFixture.ObsoleteWarningToError", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "[Obsolete(\"m\")]", "[Obsolete(\"m\", true)]" },
        { "FastDiffFixture.TypeBecomesObsolete", ChangeKind.TypeObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\")]" },
        { "FastDiffFixture.ObsoleteMessageChanged", ChangeKind.MemberObsoleteChanged, ChangeClassification.NonBreaking, "[Obsolete(\"before\")]", "[Obsolete(\"after\")]" },
        { "FastDiffFixture.MemberBecomesObsoleteError", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\", true)]" },
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
            FastDiffState.Changed,
            Assert.Single(Compare(FixtureCatalog.MetadataFastDiffPair).Types, state => state.FullName == typeName).Api);
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
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
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
    [InlineData("FastDiffFixture.MemberBecomesHidden", ChangeKind.MemberRemoved)]
    [InlineData("FastDiffFixture.MemberBecomesVisible", ChangeKind.MemberAdded)]
    [InlineData("FastDiffFixture.TypeBecomesHidden", ChangeKind.TypeRemoved)]
    [InlineData("FastDiffFixture.TypeBecomesObsolete", ChangeKind.TypeRemoved)]
    public void OrdinaryPresentation_ReportsAHiddenOrObsoleteTransitionAsPopulation(
        string typeName,
        ChangeKind kind)
    {
        // The Public scope suppresses hidden subjects and obsolete Types, so it keeps reporting
        // a transition across that boundary as a removal or an addition.
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
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
    [InlineData("FastDiffFixture.MemberBecomesObsolete")]
    [InlineData("FastDiffFixture.ObsoleteWarningToError")]
    [InlineData("FastDiffFixture.ObsoleteMessageChanged")]
    [InlineData("FastDiffFixture.MemberBecomesObsoleteError")]
    public void OrdinaryPresentation_ComparesNoMemberObsoleteStatus(string typeName)
    {
        // The Public scope admits an obsolete Member and compares no status, as before.
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
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
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        ApiSurface after = Surface(pair.NewAssemblyPath(), ApiSurfaceExtractionScope.ApiAxis);

        ApiType hidden = Assert.Single(after.Types, type => type.FullName == "FastDiffFixture.TypeBecomesHidden");
        Assert.True(hidden.IsHidden);
        Assert.False(hidden.IsObsolete);
        ApiType obsolete = Assert.Single(after.Types, type => type.FullName == "FastDiffFixture.TypeBecomesObsolete");
        Assert.False(obsolete.IsHidden);
        Assert.True(obsolete.IsObsolete);
        Assert.Equal("m", obsolete.ObsoleteMessage);
        Assert.False(obsolete.ObsoleteIsError);
    }

    [Fact]
    public void Steps_ResumeToTheWholeResult()
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        FastDiffResult whole = Compare(pair);
        var comparison = new FastDiffComparison();

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
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        var comparison = new FastDiffComparison();
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        Assert.False(comparison.Step(before, after, TimeSpan.Zero, TestContext.Current.CancellationToken));

        Assert.Throws<InvalidOperationException>(
            () => comparison.Step(after, before, TimeSpan.Zero, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ApiAxis_LeavesBodiesUncompared()
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
        FastDiffResult whole = Compare(pair);
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));

        FastDiffResult api = FastDiff.Compare(
            before, after, FastDiffAxes.Api, TestContext.Current.CancellationToken);

        Assert.All(api.Types, type => Assert.Equal(FastDiffState.NotCompared, type.Body));
        Assert.Equal(0, api.Receipt.BodiesCompared);
        Assert.Equal(
            whole.Types.Select(type => (type.Identifier, type.Api)),
            api.Types.Select(type => (type.Identifier, type.Api)));
    }

    static FastDiffResult Compare(FixturePair pair)
    {
        using var before = new PEReader(File.OpenRead(pair.OldAssemblyPath()));
        using var after = new PEReader(File.OpenRead(pair.NewAssemblyPath()));
        return FastDiff.Compare(
            before, after, FastDiffAxes.ApiAndBody, TestContext.Current.CancellationToken);
    }
}
