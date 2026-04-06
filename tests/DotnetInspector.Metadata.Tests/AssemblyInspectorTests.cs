using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotnetInspector.Metadata;

namespace DotnetInspector.Metadata.Tests;

public sealed class AssemblyInspectorTests
{
    public static IEnumerable<object[]> StrongNamedAssemblies()
    {
        yield return [typeof(System.Text.Json.JsonSerializer).Assembly];
        yield return [typeof(object).Assembly];
        yield return [typeof(System.Linq.Enumerable).Assembly];
    }

    [Theory]
    [MemberData(nameof(StrongNamedAssemblies))]
    public void ExtractAssemblyInfo_PublicKeyToken_MatchesRuntimeGroundTruth(Assembly assembly)
    {
        var expectedTokenBytes = Assert.IsType<byte[]>(AssemblyName.GetAssemblyName(assembly.Location).GetPublicKeyToken());
        Assert.NotEmpty(expectedTokenBytes);

        using var stream = File.OpenRead(assembly.Location);
        using var peReader = new PEReader(stream);

        var info = AssemblyInspector.ExtractAssemblyInfo(peReader);

        var expectedToken = Convert.ToHexString(expectedTokenBytes).ToLowerInvariant();
        Assert.Equal(expectedToken, info.PublicKeyToken);
    }
}
