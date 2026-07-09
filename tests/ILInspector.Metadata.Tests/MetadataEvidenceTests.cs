using System.Collections.Immutable;
using System.Reflection.PortableExecutable;

using ILInspector.Evidence;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public class MetadataEvidenceTests
{
    static readonly EvidenceSubject Subject = new("api", "API");

    [Fact]
    public void SameSurface_IsExactAndAllPresent()
    {
        var surface = Surface(Type("Widget", members:
        [
            Method("Get", "int Get()"),
            Property("Name", "string Name { get; }")
        ]));

        var result = MetadataEvidence.Compare(surface, surface, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.IsExact);
        Assert.All(result.Rows, row => Assert.Equal(EvidenceRowKind.Present, row.Kind));
        Assert.All(result.Rows, row => Assert.Equal(EvidenceDifferenceKind.None, row.DifferenceKind));
    }

    [Fact]
    public void AddedAndRemovedMembers_AgreeWithApiDiffAnalyzer()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Removed", "void Removed()")
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Keep", "void Keep()"),
            Method("Added", "void Added()")
        ]));

        var result = MetadataEvidence.Compare(oldSurface, newSurface, Subject);
        var expected = ExpectedFromApiDiff(oldSurface, newSurface);

        Assert.Null(result.Failure);
        Assert.Equal(expected.Added, RowKeys(result.Rows, EvidenceRowKind.Added));
        Assert.Equal(expected.Removed, RowKeys(result.Rows, EvidenceRowKind.Removed));
        Assert.Empty(RowKeys(result.Rows, EvidenceRowKind.Changed));
    }

    [Fact]
    public void MatchedMemberFacetChanges_AreChangedRows()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()")
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("Run", "void Run()", accessibility: "protected", isObsolete: true, attributes: ["System.ObsoleteAttribute"])
        ]));

        var result = MetadataEvidence.Compare(oldSurface, newSurface, Subject);

        var row = Assert.Single(result.Rows);
        Assert.Equal(EvidenceRowKind.Changed, row.Kind);
        Assert.Contains("accessibility: public -> protected", row.Detail ?? "", StringComparison.Ordinal);
        Assert.Contains("obsolete: false -> true", row.Detail ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void ReorderedMembers_AreAllPresentUnderIdentitySetMode()
    {
        var oldSurface = Surface(Type("Widget", members:
        [
            Method("A", "void A()"),
            Method("B", "void B()"),
            Method("C", "void C()")
        ]));
        var newSurface = Surface(Type("Widget", members:
        [
            Method("C", "void C()"),
            Method("A", "void A()"),
            Method("B", "void B()")
        ]));

        var result = MetadataEvidence.Compare(oldSurface, newSurface, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.IsExact);
        Assert.Equal(3, result.Rows.Length);
        Assert.All(result.Rows, row => Assert.Equal(EvidenceRowKind.Present, row.Kind));
        Assert.All(result.Rows, row => Assert.Equal(EvidenceDifferenceKind.None, row.DifferenceKind));
        Assert.DoesNotContain(result.Rows, row => row.Kind is EvidenceRowKind.Added or EvidenceRowKind.Removed);
    }

    [Fact]
    public void Exactness_AgreesWithApiDiffAnalyzer_AcrossFixtures()
    {
        (ApiSurface Old, ApiSurface New)[] pairs =
        [
            (
                Surface(Type("Widget", members: [Method("A", "void A()")])),
                Surface(Type("Widget", members: [Method("A", "void A()")]))
            ),
            (
                Surface(Type("Widget", members: [Method("A", "void A()")])),
                Surface(Type("Widget", members: [Method("A", "void A()"), Method("B", "void B()")]))
            ),
            (
                Surface(Type("Widget", members: [Method("A", "void A()"), Method("B", "void B()")])),
                Surface(Type("Widget", members: [Method("A", "void A()")]))
            ),
            (
                Surface(Type("Widget", members: [Method("A", "void A()", isVirtual: true)])),
                Surface(Type("Widget", members: [Method("A", "void A()")]))
            ),
            (
                Surface(Type("Widget", members: [Method("A", "void A(int value)")])),
                Surface(Type("Widget", members: [Method("A", "void A(string value)")]))
            ),
            (
                Surface(Type("Widget", members: [Method("A", "int A()")])),
                Surface(Type("Widget", members: [Method("A", "string A()")]))
            ),
            (
                Surface(Type("Widget", members: [Method("A", "void A()")])),
                Surface(Type("Widget", members: [Method("A", "void A()", isObsolete: true, attributes: ["System.ObsoleteAttribute"])]))
            )
        ];

        foreach (var (oldSurface, newSurface) in pairs)
        {
            var result = MetadataEvidence.Compare(oldSurface, newSurface, Subject);
            var expected = ExpectedFromApiDiff(oldSurface, newSurface);

            Assert.Null(result.Failure);
            Assert.Equal(expected.Added, RowKeys(result.Rows, EvidenceRowKind.Added));
            Assert.Equal(expected.Removed, RowKeys(result.Rows, EvidenceRowKind.Removed));
            Assert.Equal(expected.Changed, RowKeys(result.Rows, EvidenceRowKind.Changed));
        }
    }

    [Fact]
    public void RealAssemblySelfCompare_IsExact()
    {
        using var stream = File.OpenRead(typeof(ApiDiffAnalyzer).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);

        var result = MetadataEvidence.Compare(surface, surface, Subject);

        Assert.Null(result.Failure);
        Assert.True(result.IsExact);
        Assert.True(result.Rows.Length > 50, $"expected a substantial real API member corpus; rows={result.Rows.Length}");
        Assert.All(result.Rows, row => Assert.Equal(EvidenceRowKind.Present, row.Kind));
    }

    static ApiSurface Surface(params ApiType[] types)
    {
        var surface = new ApiSurface();
        surface.Types.AddRange(types);
        return surface;
    }

    static ApiType Type(string name, string kind = "class", string? ns = "TestNamespace", List<ApiMember>? members = null)
        => new()
        {
            Name = name,
            Kind = kind,
            Namespace = ns,
            Members = members ?? []
        };

    static ApiMember Method(
        string name,
        string signature,
        bool isVirtual = false,
        bool isObsolete = false,
        string? accessibility = null,
        IReadOnlyList<string>? attributes = null)
        => new()
        {
            Name = name,
            Kind = "method",
            Signature = signature,
            IsVirtual = isVirtual,
            IsObsolete = isObsolete,
            Accessibility = accessibility,
            Attributes = attributes?.ToList() ?? []
        };

    static ApiMember Property(string name, string signature)
        => new() { Name = name, Kind = "property", Signature = signature };

    static string[] RowKeys(ImmutableArray<EvidenceRow> rows, EvidenceRowKind kind)
        => rows
            .Where(row => row.Kind == kind)
            .Select(row => row.Anchor.IdentityKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

    static ExpectedEvidence ExpectedFromApiDiff(ApiSurface oldSurface, ApiSurface newSurface)
    {
        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();
        var diff = ApiDiffAnalyzer.Compare(oldSurface, newSurface, new ApiDiffOptions(ApiDiffScope.All));

        foreach (var change in diff.TypeDiffs.SelectMany(typeDiff => typeDiff.Changes))
        {
            if (change.Subject?.Kind != ApiChangeSubjectKind.Member)
                continue;

            var oldKey = change.Subject.OldMember?.CanonicalSignature;
            var newKey = change.Subject.NewMember?.CanonicalSignature;
            switch (change.Kind)
            {
                case ChangeKind.MemberAdded when newKey is not null:
                    added.Add(newKey);
                    break;
                case ChangeKind.MemberRemoved when oldKey is not null:
                    removed.Add(oldKey);
                    break;
                case ChangeKind.MemberSignatureChanged when oldKey is not null && newKey is not null && oldKey != newKey:
                    removed.Add(oldKey);
                    added.Add(newKey);
                    break;
                case ChangeKind.MemberSignatureChanged when newKey is not null:
                    changed.Add(newKey);
                    break;
                default:
                    if (oldKey is not null && oldKey == newKey)
                        changed.Add(oldKey);
                    break;
            }
        }

        return new ExpectedEvidence(
            [.. added.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal)],
            [.. removed.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal)],
            [.. changed.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal)]);
    }

    sealed record ExpectedEvidence(string[] Added, string[] Removed, string[] Changed);
}
