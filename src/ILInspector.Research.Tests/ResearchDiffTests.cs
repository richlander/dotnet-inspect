using DotnetInspector.Fixtures;
using ILInspector.Metadata;
using ILInspector.Instructions;

namespace ILInspector.Research.Tests;

public class ResearchDiffTests
{
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
