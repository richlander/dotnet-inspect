using DotnetInspector.Sections;

namespace DotnetInspector.Sections.Tests;

public class RelatedOperationAffordanceTests
{
    [Theory]
    [InlineData("member")]
    [InlineData("Member.inspect")]
    [InlineData("member.Inspect")]
    [InlineData("member.-inspect")]
    [InlineData("member.inspect-")]
    [InlineData("member.inspect_now")]
    public void Id_RejectsNonCanonicalValues(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            new RelatedOperationAffordanceId(value));
    }

    [Fact]
    public void MemberCatalog_IssuesDistinctExactIdentities()
    {
        RelatedOperationAffordance[] affordances =
            [.. MemberRelatedOperationAffordances.All];

        Assert.Equal(
            affordances.Length,
            affordances.Select(static affordance => affordance.Id).Distinct().Count());
        Assert.Contains(
            affordances,
            static affordance =>
                affordance.Id
                == MemberRelatedOperationAffordances.InspectMember.Id);
        Assert.Contains(
            affordances,
            static affordance =>
                affordance.Id
                == MemberRelatedOperationAffordances.InspectTypeHierarchy.Id);
    }
}
