using Xunit;

namespace DotnetInspector.Tests;

/// <summary>
/// Non-vacuity gate for <see cref="TestAssemblySourceLink"/>.
/// </summary>
/// <remarks>
/// The helper's whole value is that it produces a hint exactly when SourceLink is missing
/// and stays silent otherwise. Both halves fail silently on their own: a probe that always
/// reported "unavailable" would smear an irrelevant note across unrelated failures, and one
/// that always reported "available" would never fire and would leave issue #3658's
/// misdiagnosis in place. Nothing else in the suite would notice either way.
///
/// A normal build stamps SourceLink into the test assembly's PDB, so this asserts the
/// available branch against the real artifact. The unavailable branch is exercised by
/// rebuilding with <c>/p:EnableSourceLink=false</c>, which cannot be a standing test because
/// it is a property of the build rather than of the run.
/// </remarks>
public class TestAssemblySourceLinkTests
{
    [Fact]
    public void NormalBuild_StampsSourceLink_SoTheHintStaysSilent()
    {
        Assert.True(
            TestAssemblySourceLink.IsAvailable,
            "The test assembly's PDB should carry SourceLink in a normal build, but the probe "
            + $"reported: {TestAssemblySourceLink.UnavailableReason}."
            + TestAssemblySourceLink.FailureHint);

        Assert.Null(TestAssemblySourceLink.UnavailableReason);
        Assert.Empty(TestAssemblySourceLink.FailureHint);
    }
}
