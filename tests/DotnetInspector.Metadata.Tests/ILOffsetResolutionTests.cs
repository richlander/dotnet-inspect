using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DotnetInspector.Metadata;

namespace DotnetInspector.Metadata.Tests;

/// <summary>
/// Exercises the sequence-point walk in <see cref="SourceLinkResolver.ResolveByILOffsetDirect"/>
/// against this test assembly's own portable PDB.
/// </summary>
public sealed class ILOffsetResolutionTests
{
    // Several statements on distinct lines, so the compiler emits multiple
    // sequence points at increasing IL offsets with gaps between them.
    static int SampleMethod(int a, int b)
    {
        int x = a + b;
        int y = x * 2;
        int z = y - a;
        return z + x;
    }

    [Fact]
    public void SampleMethod_Sanity() => Assert.Equal(13, SampleMethod(2, 3));

    [Fact]
    public void EachSequencePointOffset_ResolvesToItsOwnLine()
    {
        using var img = new SelfImage();
        var method = FindMethod(img.Metadata, nameof(SampleMethod));
        int token = MetadataTokens.GetToken(method);
        var points = VisibleSequencePoints(img.Pdb, method);
        Assert.True(points.Count >= 2, "fixture should emit multiple sequence points");

        foreach (var (offset, line) in points)
        {
            var result = SourceLinkResolver.ResolveByILOffsetDirect(img.Metadata, img.Pdb, token, offset);
            Assert.NotNull(result);
            Assert.Equal(line, result!.Line);
            Assert.Equal(offset, result.MatchedOffset);
            Assert.EndsWith("ILOffsetResolutionTests.cs", result.FilePath);
            Assert.EndsWith("." + nameof(SampleMethod), result.MethodName);
        }
    }

    [Fact]
    public void OffsetBetweenSequencePoints_ResolvesToPrecedingPoint()
    {
        using var img = new SelfImage();
        var method = FindMethod(img.Metadata, nameof(SampleMethod));
        int token = MetadataTokens.GetToken(method);
        var points = VisibleSequencePoints(img.Pdb, method);

        // Pick an adjacent pair with a gap, then query an offset strictly between them.
        var pair = points.Zip(points.Skip(1)).First(p => p.Second.Offset - p.First.Offset >= 2);
        int between = pair.First.Offset + 1;

        var result = SourceLinkResolver.ResolveByILOffsetDirect(img.Metadata, img.Pdb, token, between);
        Assert.NotNull(result);
        Assert.Equal(pair.First.Offset, result!.MatchedOffset);
        Assert.Equal(pair.First.Line, result.Line);
    }

    [Fact]
    public void NonMethodDefToken_ReturnsNull()
    {
        using var img = new SelfImage();
        var method = FindMethod(img.Metadata, nameof(SampleMethod));
        var typeHandle = img.Metadata.GetMethodDefinition(method).GetDeclaringType();
        int typeToken = MetadataTokens.GetToken(typeHandle); // TypeDef table (0x02), not MethodDef

        Assert.Null(SourceLinkResolver.ResolveByILOffsetDirect(img.Metadata, img.Pdb, typeToken, 0));
    }

    [Fact]
    public void OutOfRangeMethodToken_ReturnsNull()
    {
        using var img = new SelfImage();
        // MethodDef table (0x06) but a row that does not exist.
        Assert.Null(SourceLinkResolver.ResolveByILOffsetDirect(img.Metadata, img.Pdb, 0x06FFFFFF, 0));
    }

    static MethodDefinitionHandle FindMethod(MetadataReader md, string methodName)
    {
        foreach (var handle in md.MethodDefinitions)
        {
            if (md.GetString(md.GetMethodDefinition(handle).Name) == methodName)
                return handle;
        }
        throw new InvalidOperationException($"Method '{methodName}' not found in metadata.");
    }

    static List<(int Offset, int Line)> VisibleSequencePoints(MetadataReader pdb, MethodDefinitionHandle method)
    {
        var debugInfo = pdb.GetMethodDebugInformation(method.ToDebugInformationHandle());
        var points = new List<(int, int)>();
        foreach (var sp in debugInfo.GetSequencePoints())
        {
            if (!sp.IsHidden)
                points.Add((sp.Offset, sp.StartLine));
        }
        return points;
    }

    /// <summary>Opens this test assembly's PE image and associated portable PDB (embedded or beside the DLL).</summary>
    sealed class SelfImage : IDisposable
    {
        public PEReader Pe { get; }
        public MetadataReader Metadata { get; }
        public MetadataReader Pdb { get; }
        readonly MetadataReaderProvider _pdbProvider;

        public SelfImage()
        {
            string dllPath = typeof(ILOffsetResolutionTests).Assembly.Location;
            using (var peStream = File.OpenRead(dllPath))
                Pe = new PEReader(peStream, PEStreamOptions.PrefetchEntireImage);
            Metadata = Pe.GetMetadataReader();

            if (!Pe.TryOpenAssociatedPortablePdb(dllPath,
                    p => File.Exists(p) ? File.OpenRead(p) : null,
                    out var provider, out _) || provider is null)
            {
                Pe.Dispose();
                throw new InvalidOperationException("No portable PDB found for the test assembly.");
            }

            _pdbProvider = provider;
            Pdb = provider.GetMetadataReader();
        }

        public void Dispose()
        {
            _pdbProvider.Dispose();
            Pe.Dispose();
        }
    }
}
