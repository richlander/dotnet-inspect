using DotnetInspector.Sections;
using DotnetInspector.SourceHouse;

namespace DotnetInspector.Queries.Tests;

public sealed partial class AssemblyContextSourceQueryTests
{
    [Fact]
    public async Task SourcePairInspection_ReturnsDetachedEnvelope()
    {
        var (before, after) = SourcePairAssemblies();
        using var host = SourcePairHost(before, after);
        InspectionEnvelope<AssemblyMemberSourcePairResult> inspection;

        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup beforeGroup =
                workspace.CreateAssemblyContextGroup([before.Participant]);
            using AssemblyContextGroup afterGroup =
                workspace.CreateAssemblyContextGroup([after.Participant]);
            var target = before.MemberTarget("Value", "Counter");

            inspection = await MemberSourcePairInspection.ExecuteAsync(
                beforeGroup,
                before.Participant,
                afterGroup,
                after.Participant,
                AssemblyMemberSourcePairRequest.From(
                    target.Type,
                    target.Member),
                host.Context,
                TestContext.Current.CancellationToken);
        }

        AssemblyMemberSourcePairResult content = inspection.Content;
        Assert.Equal(AssemblyMemberSourcePairStatus.Compared, content.Status);
        Assert.False(content.IsExact);
        Assert.Equal(
            before.Assembly.Registration,
            content.Before.Subject.Registration);
        Assert.Equal(
            after.Assembly.Registration,
            content.After.Subject.Registration);
        InspectionPortableProjection.NonProjectable share =
            Assert.IsType<InspectionPortableProjection.NonProjectable>(inspection.PortableProjection);
        Assert.Equal("member-source-pair/share", share.Path);
        Assert.Empty(inspection.Diagnostics);
        foreach (var endpoint in new[] { content.Before, content.After })
        {
            var resolved = Assert.IsType<AssemblyMemberSourcePairEndpoint.Resolved>(endpoint);
            var house = Assert.IsType<SourceHouseOutcome.Available>(resolved.HouseOutcome);
            Assert.Equal(SourceHouseLibraryLeaseConsumer.SourceHouse, house.Receipt.LeaseSettlement.Consumer);
            Assert.NotEmpty(house.Source.Text);
        }
    }
}
