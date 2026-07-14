using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Pins the member-command code path (<see cref="MemberCodeProvider"/>) to
/// metadata-handle member addressing rather than name+overload-ordinal counting.
/// The extractor drops some public methods from the API surface (here, an
/// <see cref="EditorBrowsableAttribute"/>-hidden overload) that the by-name
/// importer's public-only counting still counts, so a surviving overload's
/// surface index no longer matches its metadata overload index. Ordinal
/// addressing then pairs the survivor's selector with the hidden overload's
/// body/attributes; handle addressing (via the surface member's own metadata
/// token) renders each member's own body. See docs/design/member-body-substrate.md.
/// </summary>
public class MemberCodeProviderOverloadAddressingTests
{
    [Fact]
    public void SelectedOverload_RendersOwnBodyAndAttributes_NotHiddenSibling()
    {
        string assemblyPath = typeof(MemberOverloadDriftSpecimen).Assembly.Location;
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        var surface = ApiSurfaceExtractor.Extract(pe, includeAll: false);
        var type = surface.Types.Single(t => t.FullName == typeof(MemberOverloadDriftSpecimen).FullName);
        var describeOverloads = type.Members.Where(m => m.Name == "Describe").ToList();

        var request = new MemberCodeProvider.Request(
            DecompiledSource: true,
            AnnotatedSource: false,
            CostOverlay: false,
            SemanticsOverlay: false,
            IL: false,
            Attributes: true,
            Calls: false,
            Callers: false,
            CallGraph: false,
            UnsafeOperations: false);

        var results = MemberCodeProvider.Collect(
            type, describeOverloads, assemblyPath, overloadIndex: 0, request);

        var (_, code) = Assert.Single(results);
        Assert.NotNull(code.DecompiledResult?.Output);
        Assert.Contains("VISIBLE_MEMBER_BODY", code.DecompiledResult!.Output);
        Assert.DoesNotContain("HIDDEN_MEMBER_BODY", code.DecompiledResult.Output);
        // The hidden overload's [EditorBrowsable] must not drift onto the survivor.
        Assert.True(
            code.Attributes is null || code.Attributes.All(a => a.Name != "EditorBrowsable"),
            "EditorBrowsable must not be misattributed onto the surviving overload.");
    }
}

public class MemberOverloadDriftSpecimen
{
    // First public overload in metadata order — hidden from the API surface, so
    // it is not composed but still occupies a public overload slot the by-name
    // importer counts.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string Describe(int value) => "HIDDEN_MEMBER_BODY";

    // Surviving overload: surface index 0, metadata public index 1.
    public string Describe(string value) => "VISIBLE_MEMBER_BODY";
}
