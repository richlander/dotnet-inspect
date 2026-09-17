using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

public class ResearchEvidenceLocationTests
{
    [Fact]
    public void EqualOffsetsInDifferentMethodsRemainDistinct()
    {
        MethodIdentity first = Method(
            "First",
            0x06000001);
        MethodIdentity second = Method(
            "Second",
            0x06000002);

        ResearchEvidenceLocation firstLocation =
            ResearchEvidenceLocation.ForInstruction(first, 0);
        ResearchEvidenceLocation secondLocation =
            ResearchEvidenceLocation.ForInstruction(second, 0);

        Assert.NotEqual(firstLocation, secondLocation);
    }

    [Fact]
    public void MethodOnlyEvidenceHasNoInstructionCoordinate()
    {
        MethodIdentity method = Method(
            "Aggregate",
            0x06000001);

        ResearchEvidenceLocation location =
            ResearchEvidenceLocation.ForMethod(method);

        Assert.True(location.IsMethodOnly);
        Assert.Null(location.ILOffset);
        Assert.Equal(method, location.Method);
    }

    [Fact]
    public void MatchingMethodAdmissionRetainsExactLocation()
    {
        MethodIdentity method = Method(
            "Match",
            0x06000001);
        ResearchEvidenceLocation location =
            ResearchEvidenceLocation.ForInstruction(method, 12);

        var admitted =
            Assert.IsType<
                ResearchEvidenceLocationAdmission.Admitted>(
                location.Admit(method));

        Assert.Same(location, admitted.Location);
    }

    [Fact]
    public void MismatchedMethodAdmissionRetainsExpectedAndActualIdentity()
    {
        MethodIdentity actual = Method(
            "Actual",
            0x06000001);
        MethodIdentity expected = Method(
            "Expected",
            0x06000002);
        ResearchEvidenceLocation location =
            ResearchEvidenceLocation.ForInstruction(actual, 0);

        var rejected =
            Assert.IsType<
                ResearchEvidenceLocationAdmission.Rejected>(
                location.Admit(expected));

        Assert.Equal(expected, rejected.ExpectedMethod);
        Assert.Same(location, rejected.Location);
        Assert.Equal(actual, rejected.Location.Method);
    }

    [Fact]
    public void InstructionEvidenceRejectsNegativeOffset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResearchEvidenceLocation.ForInstruction(
                Method("Invalid", 0x06000001),
                -1));
    }

    static MethodIdentity Method(
        string name,
        int metadataToken) =>
        new(
            "EvidenceAssembly",
            new Guid(
                "11111111-1111-1111-1111-111111111111"),
            TypeRef.Definition(
                "EvidenceAssembly",
                "Fixtures",
                "EvidenceType"),
            name,
            [],
            TypeRef.CoreLib("System", "Void"),
            metadataToken,
            IsStatic: true);
}
