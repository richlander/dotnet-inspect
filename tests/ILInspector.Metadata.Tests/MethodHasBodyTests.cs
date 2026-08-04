using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Tests for <see cref="PdbContext.MethodHasBody"/>, the metadata fact behind the CLI's
/// "this member has no authored source" explanation (issue #3299). The distinction that
/// matters is between a definite "no body" and an unanswerable question: only the former
/// may be reported as a member property.
/// </summary>
public class MethodHasBodyTests
{
    static string CoreLibPath => typeof(object).Assembly.Location;

    [Theory]
    // -1 is the same runtime value as unchecked((int)0xFFFFFFFF), so one row covers both spellings.
    [InlineData(-1)]
    [InlineData(0x7F000001)]
    public void InvalidToken_IsUnknown_AndDoesNotThrow(int token)
    {
        // An inspected assembly is untrusted input and MetadataTokens.Handle rejects an invalid
        // token by throwing, so the guard must cover the decode itself.
        using var context = PdbContext.Open(CoreLibPath);
        Assert.Null(context.MethodHasBody(token));
    }

    [Fact]
    public void NonMethodDefToken_IsUnknown()
    {
        using var context = PdbContext.Open(CoreLibPath);
        Assert.Null(context.MethodHasBody(typeof(object).MetadataToken));
    }

    [Fact]
    public void OutOfRangeMethodDefRow_IsUnknown()
    {
        // Well-formed token kind, row that does not exist: still unanswerable, not "no body".
        using var context = PdbContext.Open(CoreLibPath);
        Assert.Null(context.MethodHasBody(MetadataTokens.GetToken(MetadataTokens.MethodDefinitionHandle(0x00FFFFFF))));
    }

    [Fact]
    public void ConcreteMethod_HasBody()
    {
        using var context = PdbContext.Open(CoreLibPath);
        var method = typeof(object).GetMethod(nameof(object.ToString), Type.EmptyTypes)!;
        Assert.True(context.MethodHasBody(method.MetadataToken));
    }

    [Fact]
    public void InterfaceMethod_HasNoBody()
    {
        using var context = PdbContext.Open(CoreLibPath);
        var method = typeof(System.Collections.IEnumerator).GetMethod(nameof(System.Collections.IEnumerator.MoveNext))!;
        Assert.False(context.MethodHasBody(method.MetadataToken));
    }

    [Fact]
    public void AbstractMethod_HasNoBody()
    {
        using var context = PdbContext.Open(CoreLibPath);
        var abstractMethod = typeof(System.IO.Stream)
            .GetMethod(nameof(System.IO.Stream.Read), [typeof(byte[]), typeof(int), typeof(int)])!;
        Assert.False(context.MethodHasBody(abstractMethod.MetadataToken));
    }

    [Fact]
    public void ReferenceAssembly_IsUnknown_ForEveryMethod()
    {
        // A reference assembly strips all IL, so RVA 0 describes the image's surface-only nature
        // rather than the method. Reporting that as "no body" would be false for every member.
        var referenceAssembly = FindReferenceAssembly();
        Assert.SkipWhen(referenceAssembly is null, "No targeting-pack reference assembly available.");

        using var context = PdbContext.Open(referenceAssembly!);
        using var peReader = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(referenceAssembly!));
        var reader = peReader.GetMetadataReader();

        int checkedMethods = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            Assert.Null(context.MethodHasBody(MetadataTokens.GetToken(handle)));
            if (++checkedMethods == 200)
                break;
        }

        Assert.True(checkedMethods > 0);
    }

    static string? FindReferenceAssembly()
    {
        // dotnet/packs/Microsoft.NETCore.App.Ref/<version>/ref/<tfm>/System.Runtime.dll
        var runtimeDir = Path.GetDirectoryName(CoreLibPath);
        var dotnetRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(runtimeDir)));
        if (dotnetRoot is null)
            return null;

        var packs = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packs))
            return null;

        return Directory.EnumerateFiles(packs, "System.Runtime.dll", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .LastOrDefault();
    }
}
