using System.Reflection;

namespace ILInspector.Metadata.Tests;

public class MetadataAccessibilityTests
{
    [Theory]
    [InlineData(
        MethodAttributes.Private,
        MethodAttributes.FamANDAssem,
        MethodAttributes.FamANDAssem)]
    [InlineData(
        MethodAttributes.FamANDAssem,
        MethodAttributes.Assembly,
        MethodAttributes.Assembly)]
    [InlineData(
        MethodAttributes.FamANDAssem,
        MethodAttributes.Family,
        MethodAttributes.Family)]
    [InlineData(
        MethodAttributes.Assembly,
        MethodAttributes.Family,
        MethodAttributes.FamORAssem)]
    [InlineData(
        MethodAttributes.Assembly,
        MethodAttributes.FamORAssem,
        MethodAttributes.FamORAssem)]
    [InlineData(
        MethodAttributes.Family,
        MethodAttributes.Public,
        MethodAttributes.Public)]
    public void Join_UsesAccessibilityLattice(
        MethodAttributes left,
        MethodAttributes right,
        MethodAttributes expected)
    {
        Assert.Equal(expected, MetadataAccessibility.Join(left, right));
        Assert.Equal(expected, MetadataAccessibility.Join(right, left));
    }
}
