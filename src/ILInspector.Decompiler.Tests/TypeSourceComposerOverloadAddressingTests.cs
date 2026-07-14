using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Pins whole-type composition to metadata-handle member addressing rather than
/// name+overload-ordinal counting. The extractor drops some public methods from
/// the API surface (here, an <see cref="EditorBrowsableAttribute"/>-hidden
/// overload) that the by-name importer's public-only counting still counts, so
/// the surviving overload's running surface index no longer matches its metadata
/// overload index. Ordinal addressing then pairs the survivor's signature with
/// the hidden overload's body (invalid); handle addressing renders each member's
/// own body. See docs/design/member-body-substrate.md.
/// </summary>
public class TypeSourceComposerOverloadAddressingTests
{
    [Fact]
    public void EditorBrowsableHiddenOverload_DoesNotStealVisibleBody()
    {
        string assemblyPath = typeof(OverloadDriftSpecimen).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(OverloadDriftSpecimen).FullName);

        string? source = TypeSourceComposer.Compose(type, assemblyPath, pdbPath: null);

        Assert.NotNull(source);
        Assert.Contains("VISIBLE_OVERLOAD_BODY", source);
        Assert.DoesNotContain("HIDDEN_OVERLOAD_BODY", source);
    }
}

public class OverloadDriftSpecimen
{
    // First public overload in metadata order — hidden from the API surface, so
    // it is not composed but still occupies a public overload slot the by-name
    // importer counts.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string Describe(int value) => "HIDDEN_OVERLOAD_BODY";

    // Surviving overload: running surface index 0, metadata public index 1.
    public string Describe(string value) => "VISIBLE_OVERLOAD_BODY";
}
