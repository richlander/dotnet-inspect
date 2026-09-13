using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using ILInspector.Analysis;

namespace ILInspector.Research.Tests;

public sealed class ResearchAssemblyContextCacheTests
{
    [Fact]
    public void SameIndexSharesContextAndDifferentIndexesRemainIsolated()
    {
        ImmutableArray<byte> image = Image();
        LibraryBodyIndex first = Open(image);
        LibraryBodyIndex second = Open(image);

        ResearchAssemblyContext firstContext =
            ResearchAssemblyContextCache.ForIndex(first);

        Assert.Same(
            firstContext,
            ResearchAssemblyContextCache.ForIndex(first));
        Assert.NotSame(
            firstContext,
            ResearchAssemblyContextCache.ForIndex(second));
    }

    [Fact]
    public void CacheDoesNotKeepIndexOrContextAlive()
    {
        (
            WeakReference<LibraryBodyIndex> index,
            WeakReference<ResearchAssemblyContext> context) =
            CreateWeakCachedContext();

        for (int attempt = 0; attempt < 10; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(index.TryGetTarget(out _));
        Assert.False(context.TryGetTarget(out _));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static (
        WeakReference<LibraryBodyIndex> Index,
        WeakReference<ResearchAssemblyContext> Context)
        CreateWeakCachedContext()
    {
        LibraryBodyIndex index = Open(Image());
        ResearchAssemblyContext context =
            ResearchAssemblyContextCache.ForIndex(index);
        return (new(index), new(context));
    }

    static LibraryBodyIndex Open(ImmutableArray<byte> image) =>
        LibraryBodyIndex.OpenFromPrefetchedImage(
            "research-context-owner.dll",
            image,
            LibraryBodyAnalysisFeatures.MethodEvidence);

    static ImmutableArray<byte> Image() =>
        [.. File.ReadAllBytes(
            typeof(ResearchAssemblyContextCacheTests).Assembly.Location)];
}
