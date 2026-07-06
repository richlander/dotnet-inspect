using DotnetInspector.Fixtures;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using ILInspector.Instructions;

namespace ILInspector.Research.Tests;

public class ResearchDiffTests
{
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

        Assert.Equal(ResearchDiffSubjectKind.Member, subject.Kind);
        Assert.Equal(anchor.StableSelector, subject.Id);
        Assert.Equal("Sample.Widget.M(System.Int32)", subject.Display);
        Assert.Equal(anchor.TypeFullName, subject.TypeName);
        Assert.Equal(anchor.MemberName, subject.MemberName);
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

        var row = Assert.Single(diff.Rows);
        Assert.Equal("api.member-added", row.ChangeId);
        Assert.Equal(ResearchDiffEvidenceKind.MetadataApi, row.EvidenceKind);
        Assert.Equal("Member 'Added' was added", row.Message);
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

        var row = Assert.Single(diff.Rows);
        Assert.Equal("il.operation.added", row.ChangeId);
        Assert.Equal(ilRow.Message, row.Message);
        Assert.Same(ilRow, row.IlRow);
        Assert.NotNull(row.IlDisplayRow);
        Assert.Equal(3, row.IlDisplayRow.HunkId);
        Assert.Equal("+", row.IlDisplayRow.Marker);
        Assert.Equal("IL_0000", row.IlDisplayRow.Offset);
        Assert.Equal("ldc.i4", row.IlDisplayRow.OpcodeFamily);
        Assert.Equal(IlOperandIdentityKind.Immediate, row.IlDisplayRow.OperandKind);
        Assert.Equal("2", row.IlDisplayRow.OperandValue);
        Assert.Equal("h3 + IL_0000 ldc.i4 2", row.IlDisplayRow.UnifiedLine);
    }

    [Fact]
    public void FromIlBodyDiff_PreservesProducerFailureRow()
    {
        var il = IlBodyDiffResult.NewBodyMissing("metadata row absent");

        var diff = ResearchDiff.FromIlBodyDiff(il);

        var row = Assert.Single(diff.Rows);
        Assert.Equal("il.diff.new-body-missing", row.ChangeId);
        Assert.Equal(ResearchDiffEvidenceKind.IlBody, row.EvidenceKind);
        Assert.Equal("new body missing", row.Message);
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
            diff.Rows,
            failure =>
            {
                Assert.Equal("il.diff.unsupported-boundary", failure.ChangeId);
                Assert.NotNull(failure.IlFailureRow);
                Assert.Null(failure.IlRow);
            },
            operationRow =>
            {
                Assert.Equal("il.operation.removed", operationRow.ChangeId);
                Assert.Same(ilRow, operationRow.IlRow);
                Assert.NotNull(operationRow.IlDisplayRow);
                Assert.Equal("h0 - IL_0000 nop", operationRow.IlDisplayRow.UnifiedLine);
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

        var row = Assert.Single(diff.Rows);
        Assert.Equal(csharpRow.ChangeId, row.ChangeId);
        Assert.Equal(ResearchDiffEvidenceKind.CSharp, row.EvidenceKind);
        Assert.Equal(csharpRow.Message, row.Message);
        Assert.Same(csharpRow, row.CSharpRow);
        Assert.NotNull(row.CSharpDisplayRow);
        Assert.Equal(csharpRow.HunkId, row.CSharpDisplayRow.HunkId);
        Assert.Equal("-", row.CSharpDisplayRow.Marker);
        Assert.Equal(CSharpDiffOperationKind.Line, row.CSharpDisplayRow.OperationKind);
        Assert.Equal(csharpRow.Text, row.CSharpDisplayRow.Operation);
        Assert.Contains(csharpRow.Text, row.CSharpDisplayRow.UnifiedLine, StringComparison.Ordinal);
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

        var row = Assert.Single(diff.Rows, row => row.CSharpFailureRow is not null);
        Assert.Equal("csharp.diff.old-body-missing", row.ChangeId);
        Assert.Equal(ResearchDiffEvidenceKind.CSharp, row.EvidenceKind);
        Assert.Equal(failureRow.Message, row.Message);
        Assert.Same(failureRow, row.CSharpFailureRow);
        Assert.NotNull(row.CSharpDisplayFailureRow);
        Assert.Equal(CSharpDiffFailureKind.OldBodyMissing, row.CSharpDisplayFailureRow.Kind);
        Assert.Equal("old", row.CSharpDisplayFailureRow.Side);
        Assert.Equal("C# diff failed: Old method has no C# body.", row.CSharpDisplayFailureRow.UnifiedLine);
        Assert.Contains(diff.Rows, row => row.CSharpRow is not null && row.ChangeId == "csharp.method.body-added");
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
        Assert.Single(combined.Rows);
    }

    [Fact]
    public void CompareApiSurfaces_AttributeScope_QueriesMemberAttributeChanges()
    {
        var oldSurface = Surface("Widget", Member("Existing", ["A"]));
        var newSurface = Surface("Widget", Member("Existing", ["B"]));

        var diff = ResearchDiff.Compare(
            ResearchDiffInput.FromApiSurface(oldSurface),
            ResearchDiffInput.FromApiSurface(newSurface),
            new ResearchDiffOptions(ResearchDiffMechanism.Api, ApiScope: ApiDiffScope.Attributes));

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
            new ResearchDiffOptions(ResearchDiffMechanism.Api, ApiScope: ApiDiffScope.All));

        Assert.Single(diff.MembersWhere(member => member.ApiSignatureChanged && member.Subject.MemberName == "Added"));
        Assert.Single(diff.MembersWhere(member => member.ApiAttributeChanged && member.Subject.MemberName == "Existing"));
    }

    [Fact]
    public void CompareAssemblies_BodySignals_QueryUnsafeAddedChange()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.BodySignals));

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
            new ResearchDiffOptions(ResearchDiffMechanism.BodySignals));
        var targetId = Assert.Single(unfiltered.MembersWhere(member =>
            member.Subject.Display.Contains("AddsUnsafe", StringComparison.Ordinal)
            && member.HasChange("unsafe.stackalloc.added"))).Subject.Id;

        var filtered = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(
                ResearchDiffMechanism.BodySignals,
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
                ResearchDiffMechanism.BodySignals,
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
            new ResearchDiffOptions(ResearchDiffMechanism.BodySignals));

        Assert.Empty(diff.MembersWhere(member => member.HasChange("unsafe.stackalloc.added")));
    }

    [Fact]
    public void CompareAssemblies_IlBody_QueryImplementationChanges()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.IlBody));

        var changedMembers = diff.MembersWhere(member => member.ImplementationChanged);

        Assert.Contains(changedMembers, member =>
            member.Subject.Display.Contains("ConstantValue", StringComparison.Ordinal)
            && member.HasChange("il.hunk.changed"));
        Assert.DoesNotContain(changedMembers, member =>
            member.Subject.Display.Contains("Stable", StringComparison.Ordinal));

        var constantValue = Assert.Single(changedMembers, member =>
            member.Subject.Display.Contains("ConstantValue", StringComparison.Ordinal));
        var ilEvidence = Assert.Single(constantValue.Evidence, evidence => evidence.ChangeId == "il.hunk.changed");
        Assert.NotEmpty(ilEvidence.IlDisplayRows);
        Assert.Contains(ilEvidence.IlDisplayRows, row =>
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
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));

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
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.Display.Contains("SemanticReturnExpression", StringComparison.Ordinal)
            && member.HasChange("csharp.return-expression.changed")));
        Assert.Contains("SemanticReturnExpression(System.Int32)", changed.Subject.Display, StringComparison.Ordinal);
        var evidence = Assert.Single(changed.Evidence, evidence => evidence.ChangeId == "csharp.return-expression.changed");
        Assert.Equal("value + 1", evidence.OldValue);
        Assert.Equal("value + 2", evidence.NewValue);
        var displayRow = Assert.Single(evidence.CSharpDisplayRows);
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
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BodyStateSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.Display.Contains("BodyStateSample", StringComparison.Ordinal)
            && member.HasChange("csharp.diff.old-body-missing")));
        Assert.Contains("BodyStateSample.BodyState()", changed.Subject.Display, StringComparison.Ordinal);
        var evidence = Assert.Single(changed.Evidence, evidence => evidence.ChangeId == "csharp.diff.old-body-missing");
        Assert.Equal(ResearchDiffDirection.Added, evidence.Direction);
        Assert.NotNull(evidence.CSharpDisplayFailureRow);
        Assert.Equal(CSharpDiffFailureKind.OldBodyMissing, evidence.CSharpDisplayFailureRow.Kind);
        Assert.Equal("old", evidence.CSharpDisplayFailureRow.Side);
        Assert.Equal("C# diff failed: Old method has no C# body.", evidence.CSharpDisplayFailureRow.UnifiedLine);
        Assert.Contains(changed.Evidence, evidence => evidence.ChangeId == "csharp.method.body-added");
    }

    [Fact]
    public void CompareAssemblies_CSharpAndApiEvidence_GroupOnMemberAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.Api | ResearchDiffMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MethodRemovalSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "Removed"
            && member.HasMechanism(ResearchDiffMechanism.Api)
            && member.HasMechanism(ResearchDiffMechanism.CSharp)));

        Assert.StartsWith("Removed~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndApiEvidence_GroupOnOverloadAnchorDespiteDisplayDifferences()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.Api | ResearchDiffMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MethodRemovalSample" }));

        Assert.Contains(diff.MembersWhere(member =>
            member.Subject.MemberName == "Removed"
            && member.HasMechanism(ResearchDiffMechanism.Api)
            && member.HasMechanism(ResearchDiffMechanism.CSharp)), member =>
            member.Subject.Id.StartsWith("Removed~", StringComparison.Ordinal)
            && member.Evidence.Any(evidence => evidence.Mechanism == ResearchDiffMechanism.CSharp && evidence.OldValue?.Contains("method removed", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void CompareAssemblies_CSharpAndApiEvidence_GroupOnConstructorAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.Api | ResearchDiffMechanism.CSharp, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConstructorRemovalSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == ".ctor"
            && member.HasMechanism(ResearchDiffMechanism.Api)
            && member.HasMechanism(ResearchDiffMechanism.CSharp)));

        Assert.StartsWith(".ctor~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnOperatorAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp | ResearchDiffMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "OperatorSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "op_Addition"
            && member.HasMechanism(ResearchDiffMechanism.CSharp)
            && member.HasMechanism(ResearchDiffMechanism.IlBody)));

        Assert.StartsWith("operator:op_Addition~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnConversionOperatorAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp | ResearchDiffMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ConversionSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "op_Implicit"
            && member.HasMechanism(ResearchDiffMechanism.CSharp)
            && member.HasMechanism(ResearchDiffMechanism.IlBody)));

        Assert.StartsWith("operator:op_Implicit~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnGenericMethodAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp | ResearchDiffMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "DiffSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "GenericParamBody"
            && member.HasMechanism(ResearchDiffMechanism.CSharp)
            && member.HasMechanism(ResearchDiffMechanism.IlBody)));

        Assert.StartsWith("GenericParamBody~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnExtensionAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp | ResearchDiffMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExtensionSample" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "Twice"
            && member.HasMechanism(ResearchDiffMechanism.CSharp)
            && member.HasMechanism(ResearchDiffMechanism.IlBody)));

        Assert.StartsWith("extension:Twice~", changed.Subject.Id, StringComparison.Ordinal);
    }

    [Fact]
    public void CompareAssemblies_CSharpAndIlEvidence_GroupOnExplicitImplementationAnchor()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath(),
            new ResearchDiffOptions(ResearchDiffMechanism.CSharp | ResearchDiffMechanism.IlBody, TypeFilters: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ExplicitSurface" }));

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "DiffFixtureSample.IExplicitSurface.Get"
            && member.HasMechanism(ResearchDiffMechanism.CSharp)
            && member.HasMechanism(ResearchDiffMechanism.IlBody)));

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
            && member.HasMechanism(ResearchDiffMechanism.CSharp));
    }

    [Fact]
    public void CompareAssemblies_DefaultMechanisms_GroupCSharpAndIlEvidence()
    {
        var diff = ResearchDiff.CompareAssemblies(
            FixtureCatalog.DiffPair.OldAssemblyPath(),
            FixtureCatalog.DiffPair.NewAssemblyPath());

        var changed = Assert.Single(diff.MembersWhere(member =>
            member.Subject.MemberName == "ConstantValue"
            && member.HasMechanism(ResearchDiffMechanism.CSharp)
            && member.HasMechanism(ResearchDiffMechanism.IlBody)));

        Assert.StartsWith("ConstantValue~", changed.Subject.Id, StringComparison.Ordinal);
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
