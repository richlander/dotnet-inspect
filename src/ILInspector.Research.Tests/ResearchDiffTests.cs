using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;
using DecompilerMetadataSource = ILInspector.Decompiler.Pipeline.MetadataSource;

namespace ILInspector.Research.Tests;

public class ResearchDiffTests
{
    [Fact]
    public void ResearchComparison_StoresChangesOnceAndComputesSubjectGroups()
    {
        var subject = new ResearchSubjectKey(
            ResearchSubjectKind.Member,
            "M~1234567890",
            "Sample.Widget.M()");
        var first = new ResearchChange(
            subject,
            ResearchChangeMechanism.CSharp,
            new FindingDescriptor("csharp.line.added", "C# line"),
            ResearchChangeKind.Added);
        var second = new ResearchChange(
            subject,
            ResearchChangeMechanism.IlBody,
            new FindingDescriptor("il.operation.added", "IL operation"),
            ResearchChangeKind.Added);
        var comparison = new ResearchComparison([first, second]);

        var group = Assert.Single(comparison.BySubject());

        Assert.Equal(subject, group.Subject);
        Assert.Equal(2, comparison.Changes.Length);
        Assert.Contains(first, group.Changes);
        Assert.Contains(second, group.Changes);
    }

    [Fact]
    public void ResearchComparison_RejectsDefaultChanges()
        => Assert.Throws<ArgumentException>(() => new ResearchComparison(default));

    [Fact]
    public void ResearchChange_RejectsCompositeMechanism()
    {
        var subject = new ResearchSubjectKey(
            ResearchSubjectKind.Member,
            "M~1234567890",
            "Sample.Widget.M()");

        Assert.Throws<ArgumentOutOfRangeException>(() => new ResearchChange(
            subject,
            ResearchChangeMechanism.CSharp | ResearchChangeMechanism.IlBody,
            new FindingDescriptor("implementation.changed", "Implementation"),
            ResearchChangeKind.Changed));
    }

    [Fact]
    public void ResearchMemberIdentity_SubjectFromAnchor_PreservesAnchorIdentityAndDisplay()
    {
        var anchor = new MemberAnchor(
            "M~1234567890",
            "M:Sample.Widget.M()",
            "1234567890",
            "Sample.Widget",
            "M");

        var subject = ResearchMemberIdentity.SubjectFromAnchor(anchor, "Sample.Widget.M(System.Int32)");

        Assert.Equal(ResearchSubjectKind.Member, subject.Kind);
        Assert.Equal(anchor.StableSelector, subject.Id);
        Assert.Equal("Sample.Widget.M(System.Int32)", subject.Display);
        Assert.Equal(anchor.TypeFullName, subject.TypeName);
        Assert.Equal(anchor.MemberName, subject.MemberName);
    }

    [Theory]
    [InlineData(".ctor", false)]
    [InlineData("op_Addition", false)]
    [InlineData("Twice", true)]
    [InlineData("IFoo.Bar", false)]
    [InlineData("M", false)]
    public void ResearchMemberIdentity_SubjectFromMethod_UsesMetadataSelectorPolicy(string methodName, bool isExtension)
    {
        var method = new MethodIdentity(
            "Asm",
            Guid.Empty,
            TypeRef.Definition("Asm", "Sample", "Widget"),
            methodName,
            [],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true,
            IsExtension: isExtension);

        var subject = ResearchMemberIdentity.SubjectFromMethod(method);

        Assert.StartsWith($"{ApiMemberIdentity.GetMemberSelectorName(methodName, isExtension)}~", subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void ResearchMemberIdentity_SubjectFromMethod_DisambiguatesConversionOperatorsByReturnType()
    {
        // Two op_Explicit conversions that differ only by return type must get distinct body
        // identities, matching the API-side anchor so C# and IL evidence group (regression #2433).
        var widget = TypeRef.Definition("Asm", "Sample", "Widget");
        var toInt = new MethodIdentity(
            "Asm", Guid.Empty, widget, "op_Explicit", [widget],
            TypeRef.CoreLib("System", "Int32"), MetadataToken: 0x06000001, IsStatic: true);
        var toLong = toInt with { ReturnType = TypeRef.CoreLib("System", "Int64"), MetadataToken = 0x06000002 };

        var idInt = ResearchMemberIdentity.SubjectFromMethod(toInt).Id;
        var idLong = ResearchMemberIdentity.SubjectFromMethod(toLong).Id;

        Assert.StartsWith("operator:op_Explicit~", idInt, StringComparison.Ordinal);
        Assert.StartsWith("operator:op_Explicit~", idLong, StringComparison.Ordinal);
        Assert.NotEqual(idInt, idLong);
    }

    [Theory]
    [InlineData(".ctor", false)]
    [InlineData("op_Addition", false)]
    [InlineData("Twice", true)]
    [InlineData("IFoo.Bar", false)]
    [InlineData("M", false)]
    public void ResearchMemberIdentity_SelectorForMetadataName_DelegatesToMetadataPolicy(string methodName, bool isExtension)
    {
#pragma warning disable CS0618
        var selector = ResearchMemberIdentity.SelectorForMetadataName(methodName, isExtension);
#pragma warning restore CS0618

        Assert.Equal(ApiMemberIdentity.GetMemberSelectorName(methodName, isExtension), selector);
    }

    [Fact]
    public void MetadataApiDiff_DefaultScope_IgnoresAttributeOnlyChanges()
    {
        var oldSurface = Surface("Widget", Member("Existing", ["A"]));
        var newSurface = Surface("Widget", Member("Existing", ["B"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        Assert.Empty(diff.TypeDiffs);
    }

    [Fact]
    public void MetadataApiDiff_AttributeScope_ReportsAttributeOnlyChanges()
    {
        var oldSurface = Surface("Widget", Member("Existing", ["A"]));
        var newSurface = Surface("Widget", Member("Existing", ["B"]));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, new ApiDiffOptions(ApiDiffScope.Attributes));

        var type = Assert.Single(diff.TypeDiffs);
        Assert.Collection(
            type.Changes,
            removed =>
            {
                Assert.Equal(ChangeKind.MemberAttributeRemoved, removed.Kind);
                Assert.Equal(ApiChangeCategory.Attribute, removed.Category);
                Assert.Equal("A", removed.OldValue);
            },
            added =>
            {
                Assert.Equal(ChangeKind.MemberAttributeAdded, added.Kind);
                Assert.Equal(ApiChangeCategory.Attribute, added.Category);
                Assert.Equal("B", added.NewValue);
            });
    }

    [Fact]
    public void CompareApiSurfaces_QueriesMemberApiChanges()
    {
        var oldSurface = Surface("Widget", Member("Existing"));
        var newSurface = Surface("Widget", Member("Existing"), Member("Added"));

        var diff = ResearchDiff.CompareApiSurfaces(oldSurface, newSurface);

        var apiComparison = Assert.IsType<ApiFindingComparison>(diff.ApiComparison);
        var memberComparison = apiComparison.Members switch
        {
            FindingComparison<ApiMemberHandle>.Complete complete => complete,
            _ => throw new Xunit.Sdk.XunitException("Expected a complete API member comparison."),
        };
        Assert.Contains(memberComparison.Pairs, pair =>
            pair is PairFinding<ApiMemberHandle>.Added added
            && added.New.Payload.MemberName == "Added");
        var changed = Assert.Single(diff.MembersWhere(member => member.ApiChanged));
        Assert.Equal("Added", changed.Subject.MemberName);
        Assert.True(changed.HasChange("api.member-added"));
        Assert.True(changed.ApiSignatureChanged);
        Assert.False(changed.ApiAttributeChanged);
        Assert.False(changed.ImplementationChanged);
    }

    [Fact]
    public void MetadataApiDiff_MemberChange_CarriesStructuredSubject()
    {
        var oldSurface = Surface("Widget", Member("Existing"));
        var newSurface = Surface("Widget", Member("Existing"), Member("Added"));

        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var change = Assert.Single(Assert.Single(diff.TypeDiffs).Changes);
        Assert.Equal(ChangeKind.MemberAdded, change.Kind);
        Assert.Equal(ApiChangeSubjectKind.Member, change.Subject?.Kind);
        Assert.Equal("Added", change.Subject?.MemberName);
        Assert.Same(newSurface.Types[0], change.Subject?.NewMember?.Type);
        Assert.Same(newSurface.Types[0].Members[1], change.Subject?.NewMember?.Member);
        Assert.Equal("M:Sample.Widget.Added()", change.Subject?.NewMember?.Anchor?.CanonicalSignature);
        Assert.Equal("953f7c0720", change.Subject?.NewMember?.Anchor?.Fingerprint);
        Assert.Equal("Added~953f7c0720", change.Subject?.NewMember?.Anchor?.StableSelector);
        Assert.Equal("Added~953f7c0720", change.Subject?.NewIdentity);
    }

    [Fact]
    public void CompareApiSurfaces_UsesStructuredSubjectRatherThanParsingMessage()
    {
        var oldSurface = Surface("Widget", Member("Existing"));
        var newSurface = Surface("Widget", Member("Existing"), Member("Has'Quote"));

        var diff = ResearchDiff.CompareApiSurfaces(oldSurface, newSurface);

        var changed = Assert.Single(diff.MembersWhere(member => member.ApiChanged));
        Assert.Equal("Has'Quote", changed.Subject.MemberName);
    }

    [Fact]
    public void FromApiDiff_PreservesProducerMessageAndTypedChange()
    {
        var oldSurface = Surface("Widget", Member("Existing"));
        var newSurface = Surface("Widget", Member("Existing"), Member("Added"));
        var api = ApiDiffAnalyzer.Compare(oldSurface, newSurface);

        var diff = ResearchDiff.FromApiDiff(api);

        var row = Assert.Single(diff.Changes);
        Assert.Equal("api.member-added", row.Descriptor.Id);
        Assert.Equal(ResearchChangeMechanism.Api, row.Mechanism);
        Assert.Equal("Member 'Added' was added", row.Detail);
        Assert.Same(Assert.Single(Assert.Single(api.TypeDiffs).Changes), row.ApiChange);
    }

    [Fact]
    public void FromIlBodyDiff_PreservesProducerMessageAndTypedRow()
    {
        var operation = new CanonicalIlOperation(
            Offset: 0,
            OpcodeFamily: "ldc.i4",
            Operand: new IlOperandIdentity(IlOperandIdentityKind.Immediate, "2"));
        var ilRow = new IlDiffRow(3, IlDiffKind.Add, operation, "Added IL operation 'ldc.i4 2'");
        var il = new IlBodyDiffResult(IsExact: false, Failure: null, [ilRow]);

        var diff = ResearchDiff.FromIlBodyDiff(il);

        var row = Assert.Single(diff.Changes);
        Assert.Equal("il.operation.added", row.Descriptor.Id);
        Assert.Equal(ilRow.Message, row.Detail);
        Assert.Same(ilRow, row.IlRow);
        var displayRow = Assert.Single(row.IlDisplayRows);
        Assert.Equal(3, displayRow.HunkId);
        Assert.Equal("+", displayRow.Marker);
        Assert.Equal("IL_0000", displayRow.Offset);
        Assert.Equal("ldc.i4", displayRow.OpcodeFamily);
        Assert.Equal(IlOperandIdentityKind.Immediate, displayRow.OperandKind);
        Assert.Equal("2", displayRow.OperandValue);
        Assert.Equal("h3 + IL_0000 ldc.i4 2", displayRow.UnifiedLine);
    }

    [Fact]
    public void FromIlBodyDiff_PreservesProducerFailureRow()
    {
        var il = IlBodyDiffResult.NewBodyMissing("metadata row absent");

        var diff = ResearchDiff.FromIlBodyDiff(il);

        var row = Assert.Single(diff.Changes);
        Assert.Equal("il.diff.new-body-missing", row.Descriptor.Id);
        Assert.Equal(ResearchChangeMechanism.IlBody, row.Mechanism);
        Assert.Equal("new body missing", row.Detail);
        var failure = Assert.IsType<IlDiffFailureRow>(row.IlFailureRow);
        Assert.Equal(IlDiffFailureKind.NewBodyMissing, failure.Kind);
        Assert.Equal("new", failure.Side);
        Assert.Equal("metadata row absent", failure.Detail);
        Assert.NotNull(row.IlDisplayFailureRow);
        Assert.Equal(IlDiffFailureKind.NewBodyMissing, row.IlDisplayFailureRow.Kind);
        Assert.Equal("new body missing", row.IlDisplayFailureRow.Message);
        Assert.Equal("new", row.IlDisplayFailureRow.Side);
        Assert.Equal("metadata row absent", row.IlDisplayFailureRow.Detail);
        Assert.Equal("IL diff failed: new body missing", row.IlDisplayFailureRow.UnifiedLine);
    }

    [Fact]
    public void FromIlBodyDiff_PreservesFailureAndPartialOperationRows()
    {
        var operation = new CanonicalIlOperation(
            Offset: 0,
            OpcodeFamily: "nop",
            Operand: null);
        var ilRow = new IlDiffRow(0, IlDiffKind.Remove, operation, "Removed IL operation 'nop'");
        var il = IlBodyDiffResult.UnsupportedBoundary(
            "unsupported canonicalization boundary",
            [ilRow],
            detail: "slot identity");

        var diff = ResearchDiff.FromIlBodyDiff(il);

        Assert.Collection(
            diff.Changes,
            failure =>
            {
                Assert.Equal("il.diff.unsupported-boundary", failure.Descriptor.Id);
                Assert.NotNull(failure.IlFailureRow);
                Assert.Null(failure.IlRow);
            },
            operationRow =>
            {
                Assert.Equal("il.operation.removed", operationRow.Descriptor.Id);
                Assert.Same(ilRow, operationRow.IlRow);
                Assert.Equal("h0 - IL_0000 nop", Assert.Single(operationRow.IlDisplayRows).UnifiedLine);
                Assert.Null(operationRow.IlFailureRow);
            });
    }

    [Fact]
    public void FromCSharpBodyDiff_PreservesProducerMessageAndTypedRow()
    {
        var csharpRow = Assert.Single(CSharpBodyDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConstructorSample" }).Rows,
            row => row.ChangeId == "csharp.line.removed");

        var diff = ResearchDiff.FromCSharpBodyDiff(new CSharpBodyDiffResult([csharpRow]));

        var row = Assert.Single(diff.Changes);
        Assert.Equal(csharpRow.ChangeId, row.Descriptor.Id);
        Assert.Equal(ResearchChangeMechanism.CSharp, row.Mechanism);
        Assert.Equal(csharpRow.Message, row.Detail);
        Assert.Same(csharpRow, row.CSharpRow);
        var displayRow = Assert.Single(row.CSharpDisplayRows);
        Assert.Equal(csharpRow.HunkId, displayRow.HunkId);
        Assert.Equal("-", displayRow.Marker);
        Assert.Equal(CSharpDiffOperationKind.Line, displayRow.OperationKind);
        Assert.Equal(csharpRow.Text, displayRow.Operation);
        Assert.Contains(csharpRow.Text, displayRow.UnifiedLine, StringComparison.Ordinal);
    }

    [Fact]
    public void FromCSharpBodyDiff_PreservesProducerFailureRow()
    {
        var csharp = CSharpBodyDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            typeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" });
        var failureRow = Assert.Single(csharp.FailureRows);

        var diff = ResearchDiff.FromCSharpBodyDiff(csharp);

        var row = Assert.Single(diff.Changes, row => row.CSharpFailureRow is not null);
        Assert.Equal("csharp.diff.old-body-missing", row.Descriptor.Id);
        Assert.Equal(ResearchChangeMechanism.CSharp, row.Mechanism);
        Assert.Equal(failureRow.Message, row.Detail);
        Assert.Same(failureRow, row.CSharpFailureRow);
        Assert.NotNull(row.CSharpDisplayFailureRow);
        Assert.Equal(CSharpDiffFailureKind.OldBodyMissing, row.CSharpDisplayFailureRow.Kind);
        Assert.Equal("old", row.CSharpDisplayFailureRow.Side);
        Assert.Equal("C# diff failed: Old method has no C# body.", row.CSharpDisplayFailureRow.UnifiedLine);
        Assert.Contains(diff.Changes, row =>
            row.CSharpRow is not null
            && row.Descriptor.Id == "csharp.method.body-added");
    }

    [Fact]
    public void Combine_PreservesStructuredApiDiff()
    {
        var oldSurface = Surface("Widget", Member("Existing"));
        var newSurface = Surface("Widget", Member("Existing"), Member("Added"));
        var api = ApiDiffAnalyzer.Compare(oldSurface, newSurface);
        var apiResult = ResearchDiff.FromApiDiff(api);
        var ilResult = ResearchDiff.FromIlBodyDiff(new IlBodyDiffResult(IsExact: true, Failure: null, []));

        var combined = ResearchDiff.Combine(apiResult, ilResult);

        Assert.Same(api, combined.ApiDiff);
        Assert.Null(combined.ApiComparison);
        Assert.Single(combined.Changes);
    }

    [Fact]
    public void Combine_PreservesStructuredApiComparison()
    {
        var oldSurface = Surface("Widget", Member("Existing"));
        var newSurface = Surface("Widget", Member("Existing"), Member("Added"));
        var apiResult = ResearchDiff.CompareApiSurfaces(oldSurface, newSurface);
        var ilResult = ResearchDiff.FromIlBodyDiff(new IlBodyDiffResult(IsExact: true, Failure: null, []));

        var combined = ResearchDiff.Combine(apiResult, ilResult);

        Assert.Same(apiResult.ApiComparison, combined.ApiComparison);
        Assert.Same(apiResult.ApiDiff, combined.ApiDiff);
    }

    [Fact]
    public void Combine_PrefersStructuredApiComparisonOverEarlierLegacyApiDiff()
    {
        var legacySurface = Surface("Legacy", Member("Existing"));
        var legacyResult = ResearchDiff.FromApiDiff(
            ApiDiffAnalyzer.Compare(legacySurface, Surface("Legacy", Member("Added"))));
        var oldSurface = Surface("Widget", Member("Existing"));
        var newSurface = Surface("Widget", Member("Existing"), Member("Added"));
        var structuredResult = ResearchDiff.CompareApiSurfaces(oldSurface, newSurface);

        var combined = ResearchDiff.Combine(legacyResult, structuredResult);

        Assert.Same(structuredResult.ApiComparison, combined.ApiComparison);
        Assert.Same(structuredResult.ApiDiff, combined.ApiDiff);
    }

    [Fact]
    public void CompareApiSurfaces_AttributeScope_QueriesMemberAttributeChanges()
    {
        var oldSurface = Surface("Widget", Member("Existing", ["A"]));
        var newSurface = Surface("Widget", Member("Existing", ["B"]));

        var diff = ResearchDiff.Compare(
            ResearchDiffInput.FromApiSurface(oldSurface),
            ResearchDiffInput.FromApiSurface(newSurface),
            new ResearchDiffOptions(ResearchChangeMechanism.Api, ApiScope: ApiDiffScope.Attributes));

        var changed = Assert.Single(diff.MembersWhere(member => member.ApiAttributeChanged));
        Assert.Equal("Existing", changed.Subject.MemberName);
        Assert.True(changed.ApiChanged);
        Assert.False(changed.ApiSignatureChanged);
        Assert.True(changed.HasChange("api.member-attribute-added"));
        Assert.True(changed.HasChange("api.member-attribute-removed"));
    }

    [Fact]
    public void CompareApiSurfaces_AllApiScope_SeparatesSignatureAndAttributeChanges()
    {
        var oldSurface = Surface("Widget", Member("Existing", ["A"]));
        var newSurface = Surface("Widget", Member("Existing", ["B"]), Member("Added"));

        var diff = ResearchDiff.Compare(
            ResearchDiffInput.FromApiSurface(oldSurface),
            ResearchDiffInput.FromApiSurface(newSurface),
            new ResearchDiffOptions(ResearchChangeMechanism.Api, ApiScope: ApiDiffScope.All));

        Assert.Single(diff.MembersWhere(member => member.ApiSignatureChanged && member.Subject.MemberName == "Added"));
        Assert.Single(diff.MembersWhere(member => member.ApiAttributeChanged && member.Subject.MemberName == "Existing"));
    }

    [Fact]
    public void CompareAssemblies_BodySignals_QueryUnsafeAddedChange()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.BodySignals));

        var unsafeMembers = diff.MembersWhere(member => member.HasChange("unsafe.stackalloc.added"));

        var changed = Assert.Single(unsafeMembers);
        Assert.Contains("AddsUnsafe", changed.Subject.Display);
        Assert.True(changed.ImplementationChanged);
        Assert.False(changed.ApiChanged);
    }

    [Fact]
    public void CompareAssemblies_BodySignals_MemberTargetsKeepUnsafeRows()
    {
        var unfiltered = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.BodySignals));
        var targetId = Assert.Single(unfiltered.MembersWhere(member =>
            member.Subject.Display.Contains("AddsUnsafe", StringComparison.Ordinal)
            && member.HasChange("unsafe.stackalloc.added"))).Subject.Id;

        var filtered = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(
                ResearchChangeMechanism.BodySignals,
                MemberTargetIdentities: new HashSet<string>(StringComparer.Ordinal) { targetId }));

        var changed = Assert.Single(filtered.MembersWhere(member => member.HasChange("unsafe.stackalloc.added")));
        Assert.Equal(targetId, changed.Subject.Id);
        Assert.Contains("AddsUnsafe", changed.Subject.Display);
    }

    [Fact]
    public void CompareAssemblies_BodySignals_TypeFiltersApplyToUnsafeRows()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(
                ResearchChangeMechanism.BodySignals,
                TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NoSuchType" }));

        Assert.Empty(diff.MembersWhere(member => member.HasChange("unsafe.stackalloc.added")));
    }

    [Fact]
    public void CompareAssemblies_BodySignals_SuppressesGeneratedUnsafeRows()
    {
        var method = new MethodIdentity(
            "Asm",
            Guid.Empty,
            TypeRef.Definition("Asm", "Generated", "<JsonContext>g__Generated|0_0"),
            "Use",
            [],
            TypeRef.CoreLib("System", "Void"),
            MetadataToken: 0x06000001,
            IsStatic: true);
        var oldIndex = LibraryBodyIndex.FromEvidence([method], []);
        var newIndex = LibraryBodyIndex.FromEvidence(
            [method],
            [new UnsafeEvidence(method, "Unsafe operation", "stackalloc", "opcode", 0, null)]);

        var diff = ResearchDiff.Compare(
            ResearchDiffInput.FromAssembly("old.dll", bodyIndex: oldIndex),
            ResearchDiffInput.FromAssembly("new.dll", bodyIndex: newIndex),
            new ResearchDiffOptions(ResearchChangeMechanism.BodySignals));

        Assert.Empty(diff.MembersWhere(member => member.HasChange("unsafe.stackalloc.added")));
    }

    [Fact]
    public void CompareAssemblies_IlBody_QueryImplementationChanges()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.IlBody));

        var changedMembers = diff.MembersWhere(member => member.ImplementationChanged);

        Assert.Contains(changedMembers, member =>
            member.Subject.Display.Contains("ConstantValue", StringComparison.Ordinal)
            && member.HasChange("il.hunk.changed"));
        Assert.DoesNotContain(changedMembers, member =>
            member.Subject.Display.Contains("Stable", StringComparison.Ordinal));

        var constantValue = Assert.Single(changedMembers, member =>
            member.Subject.Display.Contains("ConstantValue", StringComparison.Ordinal));
        var ilChange = Assert.Single(
            constantValue.Changes,
            change => change.Descriptor.Id == "il.hunk.changed");
        Assert.NotEmpty(ilChange.IlDisplayRows);
        Assert.Contains(ilChange.IlDisplayRows, row =>
            row.Kind == IlDiffKind.Remove
            && row.Marker == "-"
            && row.OpcodeFamily == "ldc.i4"
            && row.OperandKind == IlOperandIdentityKind.Immediate
            && row.OperandValue == "1"
            && row.UnifiedLine.Contains("ldc.i4 1", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_CSharp_QueryImplementationChanges()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));

        var changedMembers = diff.MembersWhere(member => member.ImplementationChanged);

        Assert.Contains(changedMembers, member =>
            member.Subject.Display.Contains("ConstantValue", StringComparison.Ordinal)
            && member.HasChange("csharp.line.removed"));
        Assert.Contains(changedMembers, member =>
            member.Subject.Display.Contains("ConstantValue", StringComparison.Ordinal)
            && member.HasChange("csharp.line.added"));
        Assert.DoesNotContain(changedMembers, member =>
            member.Subject.Display.Contains("Stable", StringComparison.Ordinal));
    }

    [Fact]
    public void CompareAssemblies_CSharp_QuerySemanticReturnExpressionChange()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.Display.Contains("SemanticReturnExpression", StringComparison.Ordinal)
            && member.HasChange("csharp.return-expression.changed")));
        Assert.Contains("SemanticReturnExpression(System.Int32)", changed.Subject.Display, StringComparison.Ordinal);
        var change = Assert.Single(
            changed.Changes,
            change => change.Descriptor.Id == "csharp.return-expression.changed");
        Assert.Equal("value + 1", change.OldValue);
        Assert.Equal("value + 2", change.NewValue);
        var displayRow = Assert.Single(change.CSharpDisplayRows);
        Assert.Equal("~", displayRow.Marker);
        Assert.Equal(CSharpDiffOperationKind.ReturnExpression, displayRow.OperationKind);
        Assert.Equal("return value + 1 => return value + 2", displayRow.Operation);
        Assert.Contains("return value + 1 => return value + 2", displayRow.UnifiedLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharp_QueryFailureRows()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.Display.Contains("BodyStateSample", StringComparison.Ordinal)
            && member.HasChange("csharp.diff.old-body-missing")));
        Assert.Contains("BodyStateSample.BodyState()", changed.Subject.Display, StringComparison.Ordinal);
        var change = Assert.Single(
            changed.Changes,
            change => change.Descriptor.Id == "csharp.diff.old-body-missing");
        Assert.Equal(ResearchChangeKind.Added, change.Kind);
        Assert.NotNull(change.CSharpDisplayFailureRow);
        Assert.Equal(CSharpDiffFailureKind.OldBodyMissing, change.CSharpDisplayFailureRow.Kind);
        Assert.Equal("old", change.CSharpDisplayFailureRow.Side);
        Assert.Equal("C# diff failed: Old method has no C# body.", change.CSharpDisplayFailureRow.UnifiedLine);
        Assert.Contains(changed.Changes, change => change.Descriptor.Id == "csharp.method.body-added");
    }

    [Fact]
    public void CompareAssemblies_CSharpAndApiEvidence_GroupOnMemberAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.Api | ResearchChangeMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MethodRemovalSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "Removed"
            && member.HasMechanism(ResearchChangeMechanism.Api)
            && member.HasMechanism(ResearchChangeMechanism.CSharp)));

        Assert.StartsWith("Removed~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndApiEvidence_GroupOnOverloadAnchorDespiteDisplayDifferences()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.Api | ResearchChangeMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MethodRemovalSample" }));

        Assert.Contains(diff.MembersWhere(member =>
            member.Subject.MemberName == "Removed"
            && member.HasMechanism(ResearchChangeMechanism.Api)
            && member.HasMechanism(ResearchChangeMechanism.CSharp)), member =>
            member.Subject.Id.StartsWith("Removed~", StringComparison.Ordinal)
            && member.Changes.Any(change =>
                change.Mechanism == ResearchChangeMechanism.CSharp
                && change.OldValue?.Contains("method removed", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void CompareAssemblies_CSharpAndApiEvidence_GroupOnConstructorAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.Api | ResearchChangeMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConstructorRemovalSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == ".ctor"
            && member.HasMechanism(ResearchChangeMechanism.Api)
            && member.HasMechanism(ResearchChangeMechanism.CSharp)));

        Assert.StartsWith(".ctor~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnOperatorAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp | ResearchChangeMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OperatorSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "op_Addition"
            && member.HasMechanism(ResearchChangeMechanism.CSharp)
            && member.HasMechanism(ResearchChangeMechanism.IlBody)));

        Assert.StartsWith("operator:op_Addition~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnConversionOperatorAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp | ResearchChangeMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConversionSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "op_Implicit"
            && member.HasMechanism(ResearchChangeMechanism.CSharp)
            && member.HasMechanism(ResearchChangeMechanism.IlBody)));

        Assert.StartsWith("operator:op_Implicit~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnGenericMethodAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp | ResearchChangeMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "GenericParamBody"
            && member.HasMechanism(ResearchChangeMechanism.CSharp)
            && member.HasMechanism(ResearchChangeMechanism.IlBody)));

        Assert.StartsWith("GenericParamBody~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnExtensionAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp | ResearchChangeMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExtensionSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "Twice"
            && member.HasMechanism(ResearchChangeMechanism.CSharp)
            && member.HasMechanism(ResearchChangeMechanism.IlBody)));

        Assert.StartsWith("extension:Twice~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnExplicitImplementationAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchChangeMechanism.CSharp | ResearchChangeMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExplicitSurface" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "DiffFixtureSample.IExplicitSurface.Get"
            && member.HasMechanism(ResearchChangeMechanism.CSharp)
            && member.HasMechanism(ResearchChangeMechanism.IlBody)));

        Assert.StartsWith("explicit:DiffFixtureSample.IExplicitSurface.Get~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_DefaultMechanisms_IncludeCSharpChanges()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath());

        Assert.Contains(diff.MembersWhere(member => member.ImplementationChanged), member =>
            member.Subject.Display.Contains("ConstantValue", StringComparison.Ordinal)
            && member.HasMechanism(ResearchChangeMechanism.CSharp));
    }

    [Fact]
    public void CompareAssemblies_DefaultMechanisms_GroupCSharpAndIlEvidence()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath());

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "ConstantValue"
            && member.HasMechanism(ResearchChangeMechanism.CSharp)
            && member.HasMechanism(ResearchChangeMechanism.IlBody)));

        Assert.StartsWith("ConstantValue~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementationDiff_CompareAssemblies_GroupsCSharpAndIlEvidence()
    {
        var diff = ImplementationDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ImplementationDiffOptions(TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));

        var changed = Assert.Single(diff.Members, member => member.Subject.MemberName == "ConstantValue");

        Assert.True(changed.HasCSharpChanges);
        Assert.True(changed.HasIlChanges);
        Assert.Contains(changed.Changes, change =>
            change.Mechanism == ResearchChangeMechanism.CSharp
            && ImplementationDiff.UnifiedLines(change).Any(line => line.Contains("return 1", StringComparison.Ordinal)));
        Assert.Contains(changed.Changes, change =>
            change.Mechanism == ResearchChangeMechanism.IlBody
            && ImplementationDiff.UnifiedLines(change).Any(line => line.Contains("ldc.i4 1", StringComparison.Ordinal)));
    }

    [Fact]
    public void ImplementationDiff_CompareAssemblies_FiltersIlEvidenceByType()
    {
        var diff = ImplementationDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ImplementationDiffOptions(TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OperatorSample" }));

        Assert.DoesNotContain(diff.Members, member =>
            string.Equals(member.Subject.MemberName, "ConstantValue", StringComparison.Ordinal));
        Assert.Contains(diff.Members, member =>
            string.Equals(member.Subject.MemberName, "op_Addition", StringComparison.Ordinal)
            && member.HasCSharpChanges
            && member.HasIlChanges);
    }

    [Fact]
    public void ImplementationDiff_CompareAssemblies_FiltersUnderlyingResearchDiffByMemberTarget()
    {
        var full = ImplementationDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ImplementationDiffOptions(TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));
        var targetId = Assert.Single(full.Members, member => member.Subject.MemberName == "ConstantValue").Subject.Id;

        var scoped = ImplementationDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ImplementationDiffOptions(
                TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" },
                MemberTargetIdentities: new HashSet<string>(StringComparer.Ordinal) { targetId }));

        var researchMembers = scoped.Research.MembersWhere(member => member.ImplementationChanged);
        Assert.All(researchMembers, member => Assert.Equal(targetId, member.Subject.Id));
        var member = Assert.Single(scoped.Members);
        Assert.Equal(targetId, member.Subject.Id);
        Assert.True(member.HasCSharpChanges);
        Assert.True(member.HasIlChanges);
    }

    [Fact]
    public void ImplementationDiff_CompareMembers_SameMemberIsExact()
    {
        using var source = DecompilerMetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        var stable = FindMethodHandle(FixtureCatalog.DiffPair.OldAssemblyPath(), "DiffFixtureSample.DiffSample", "Stable");

        var diff = ImplementationDiff.CompareMembers(source, stable, source, stable);

        Assert.True(diff.IsExact);
        Assert.Equal("Stable", diff.Subject.MemberName);
        Assert.NotNull(diff.CSharpDiff);
        Assert.True(diff.CSharpDiff.IsExact);
        Assert.NotNull(diff.IlDiff);
        Assert.True(diff.IlDiff.Diff.IsExact);
        Assert.Empty(diff.Changes);
    }

    [Fact]
    public void ImplementationDiff_CompareMembers_GroupsCSharpAndIlEvidence()
    {
        using var oldSource = DecompilerMetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.OldAssemblyPath());
        using var newSource = DecompilerMetadataSource.OpenWithoutSymbols(FixtureCatalog.DiffPair.NewAssemblyPath());
        var oldMethod = FindMethodHandle(FixtureCatalog.DiffPair.OldAssemblyPath(), "DiffFixtureSample.DiffSample", "ConstantValue");
        var newMethod = FindMethodHandle(FixtureCatalog.DiffPair.NewAssemblyPath(), "DiffFixtureSample.DiffSample", "ConstantValue");

        var diff = ImplementationDiff.CompareMembers(oldSource, oldMethod, newSource, newMethod);

        Assert.False(diff.IsExact);
        Assert.Equal("ConstantValue", diff.Subject.MemberName);
        Assert.True(diff.HasCSharpChanges);
        Assert.True(diff.HasIlChanges);
        Assert.Contains(diff.Changes, change =>
            change.Mechanism == ResearchChangeMechanism.CSharp
            && ImplementationDiff.UnifiedLines(change).Any(line => line.Contains("return 1", StringComparison.Ordinal)));
        Assert.Contains(diff.Changes, change =>
            change.Mechanism == ResearchChangeMechanism.IlBody
            && ImplementationDiff.UnifiedLines(change).Any(line => line.Contains("ldc.i4 1", StringComparison.Ordinal)));
    }

    [Fact]
    public void ImplementationDiff_ToIlChanges_ProjectsTypedMemberDiffRows()
    {
        var typedDiff = new IlMemberDiffResult(
            new IlMemberDiffSubject("old-id", "old-label"),
            new IlMemberDiffSubject("new-id", "new-label"),
            new IlBodyDiffResult(
                IsExact: false,
                Failure: null,
                Rows:
                [
                    new IlDiffRow(
                        0,
                        IlDiffKind.Context,
                        new CanonicalIlOperation(0, "nop", Operand: null),
                        "Unchanged IL operation 'nop'"),
                    new IlDiffRow(
                        1,
                        IlDiffKind.Remove,
                        new CanonicalIlOperation(1, "ldc.i4", new IlOperandIdentity(IlOperandIdentityKind.Immediate, "1")),
                        "Removed IL operation 'ldc.i4 1'"),
                    new IlDiffRow(
                        1,
                        IlDiffKind.Add,
                        new CanonicalIlOperation(1, "ldc.i4", new IlOperandIdentity(IlOperandIdentityKind.Immediate, "2")),
                        "Added IL operation 'ldc.i4 2'"),
                ]));

        var changes = ImplementationDiff.ToIlChanges(typedDiff);

        Assert.DoesNotContain(changes, item => item.Descriptor.Id == "il.operation.context");
        Assert.Contains(changes, item =>
            item.Descriptor.Id == "il.operation.removed"
            && item.OldValue == "ldc.i4 1"
            && item.OldIlOffset == 1
            && item.IlMemberDiff == typedDiff
            && item.IlDisplayRows.Single().UnifiedLine == "h1 - IL_0001 ldc.i4 1");
        Assert.Contains(changes, item =>
            item.Descriptor.Id == "il.operation.added"
            && item.NewValue == "ldc.i4 2"
            && item.NewIlOffset == 1
            && item.IlMemberDiff == typedDiff
            && item.IlDisplayRows.Single().UnifiedLine == "h1 + IL_0001 ldc.i4 2");
    }

    [Fact]
    public void ImplementationDiff_ToIlChanges_FallsBackWhenTypedDiffHasNoRows()
    {
        var typedDiff = new IlMemberDiffResult(
            new IlMemberDiffSubject("old-id", "old-label"),
            new IlMemberDiffSubject("new-id", "new-label"),
            new IlBodyDiffResult(
                IsExact: true,
                Failure: null,
                Rows: []));
        var failure = new IlDiffDisplayFailureRow(
            IlDiffFailureKind.NewBodyMissing,
            "new body missing",
            Side: "new",
            Detail: "method has no body");
        var fallback = new IlDiffDisplayResult(
            Failure: failure.UnifiedLine,
            Rows: [],
            FailureRows: [failure]);

        var changes = ImplementationDiff.ToIlChanges(
            typedDiff,
            fallbackDisplay: fallback);

        var row = Assert.Single(changes);
        Assert.Equal("il.diff.new-body-missing", row.Descriptor.Id);
        Assert.Equal("method has no body", row.Detail);
        Assert.Same(failure, row.IlDisplayFailureRow);
        Assert.Null(row.IlMemberDiff);
    }

    static ApiSurface Surface(string typeName, params ApiMember[] members)
        => new()
        {
            Types =
            [
                new ApiType
                {
                    Namespace = "Sample",
                    Name = typeName,
                    Kind = "class",
                    Members = [.. members],
                }
            ],
        };

    static MethodDefinitionHandle FindMethodHandle(string assemblyPath, string typeName, string methodName, int overload = 0)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (reader.GetFullTypeName(type) != typeName)
                continue;

            int seen = 0;
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (seen++ == overload)
                    return methodHandle;
            }
        }

        throw new InvalidOperationException($"Method not found: {typeName}.{methodName}#{overload}");
    }

    static ApiMember Member(string name, IReadOnlyList<string>? attributes = null)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = $"void {name}()",
            SignatureModel = new ApiSignature
            {
                ReturnType = "void",
                MemberName = name,
            },
            Attributes = attributes?.ToList() ?? [],
        };

}
