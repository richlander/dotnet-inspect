using ILInspector.Metadata;

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
            DiffFixturePath("DiffFixtures.V1"),
            DiffFixturePath("DiffFixtures.V2"),
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
            DiffFixturePath("DiffFixtures.V1"),
            DiffFixturePath("DiffFixtures.V2"),
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
            Attributes = attributes?.ToList() ?? [],
        };

    static string DiffFixturePath(string project)
    {
        var outputDirectory = new DirectoryInfo(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string path = Path.GetFullPath(Path.Combine(
            outputDirectory.FullName, "..", "..", project, outputDirectory.Name, "DiffFixtureSample.dll"));
        Assert.True(File.Exists(path), $"Expected diff fixture assembly at {path}");
        return path;
    }
}
