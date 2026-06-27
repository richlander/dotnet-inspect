using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

using NewFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// The API surface <c>unsafe</c> modifier under the updated memory-safety rules.
/// A member declared <c>unsafe</c>/<c>extern</c> is stamped with
/// <c>RequiresUnsafeAttribute</c> even when no pointer appears in its signature,
/// so the extractor must treat that attribute — not just a textual <c>*</c> in
/// the signature — as evidence of an unsafe member. The fixtures live in the
/// new-rules assembly (compiled with <c>updated-memory-safety-rules</c>).
/// </summary>
public sealed class ApiSurfaceUnsafeTests
{
    private static ApiMember Method(string name)
    {
        // ApiSurfaceExtractor materializes the surface into lists, so the reader
        // can be disposed once Extract returns. typeof(...).Name is the simple
        // type name ("UnsafeFixtures"); nameof on the using-alias would yield the
        // alias identifier instead.
        using var stream = File.OpenRead(typeof(NewFixtures).Assembly.Location);
        using var peReader = new PEReader(stream);
        var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        return surface.Types.First(t => t.Name == typeof(NewFixtures).Name)
            .Members.First(m => m.Name == name && m.Kind == "method");
    }

    [Fact]
    public void RequiresUnsafeMember_WithoutPointerInSignature_IsUnsafe()
    {
        // `public static unsafe int Risky() => 42;` — declared unsafe, no pointer
        // in the signature, so the only evidence is RequiresUnsafeAttribute.
        Assert.True(Method(nameof(NewFixtures.Risky)).IsUnsafe);
    }

    [Fact]
    public void PointerSignatureMember_IsUnsafe()
    {
        // `void* p` parameter — caught by the signature pointer check.
        Assert.True(Method(nameof(NewFixtures.FreePointer)).IsUnsafe);
    }

    [Fact]
    public void FunctionPointerSignature_PreservesDelegatePointerShape()
    {
        Assert.Equal(
            "int InvokeFunctionPointer(delegate*<int, int> callback, int x)",
            Method(nameof(NewFixtures.InvokeFunctionPointer)).Signature);
    }

    [Fact]
    public void SafeMember_IsNotUnsafe()
    {
        // No pointer and not declared unsafe at the member level.
        Assert.False(Method(nameof(NewFixtures.StackAllocDefault)).IsUnsafe);
    }
}
