using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gate for #3975: method-list membership uses property/event
/// <c>MethodSemantics</c>, not a <c>get_</c>/<c>set_</c>/<c>add_</c>/<c>remove_</c>
/// name prefix. Ordinary prefix-named methods stay methods; ordinary accessors
/// do not; explicit-interface accessors stay
/// <c>explicit-interface-implementation</c> because their private property or
/// event row would otherwise hide the public contract.
/// </summary>
public sealed class ApiSurfaceEmitSetTests
{
    static readonly ApiSurface PublicSurface;
    static readonly ApiSurface IncludeAllSurface;
    static readonly ApiSurface SummarySurface;

    static ApiSurfaceEmitSetTests()
    {
        string path = typeof(ApiSurfaceEmitSetTests).Assembly.Location;
        PublicSurface = Extract(path, includeAll: false);
        IncludeAllSurface = Extract(path, includeAll: true);
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        SummarySurface = ApiSurfaceExtractor.ExtractSummary(peReader);
    }

    [Fact]
    public void PublicExtract_KeepsOrdinaryPrefixNamedMethods()
    {
        ApiType type = PublicType();

        Assert.Contains(
            type.Members,
            member => member.Kind == "method"
                && member.Name == nameof(EmitSetFixture.get_Standalone));
        Assert.Contains(
            type.Members,
            member => member.Kind == "method"
                && member.Name == nameof(EmitSetFixture.set_Standalone));
        Assert.Contains(
            type.Members,
            member => member.Kind == "method"
                && member.Name == nameof(EmitSetFixture.add_Standalone));
        Assert.Contains(
            type.Members,
            member => member.Kind == "method"
                && member.Name == nameof(EmitSetFixture.remove_Standalone));
    }

    [Fact]
    public void PublicExtract_OmitsOrdinarySemanticAccessorsFromMethodList()
    {
        ApiType type = PublicType();

        Assert.Contains(
            type.Members,
            member => member.Kind == "property"
                && member.Name == nameof(EmitSetFixture.Value));
        Assert.Contains(
            type.Members,
            member => member.Kind == "event"
                && member.Name == nameof(EmitSetFixture.PublicChanged));
        Assert.DoesNotContain(
            type.Members,
            member => member.Kind == "method" && member.Name == "get_Value");
        Assert.DoesNotContain(
            type.Members,
            member => member.Kind == "method" && member.Name == "set_Value");
        Assert.DoesNotContain(
            type.Members,
            member => member.Kind == "method" && member.Name == "add_PublicChanged");
        Assert.DoesNotContain(
            type.Members,
            member => member.Kind == "method" && member.Name == "remove_PublicChanged");
    }

    [Fact]
    public void PublicExtract_KeepsExplicitInterfaceAccessorsAsMethods()
    {
        ApiType type = PublicType();

        ApiMember propertyAccessor = Assert.Single(
            type.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(
                    $".get_{nameof(IEmitSetContract.ExplicitValue)}",
                    StringComparison.Ordinal));
        Assert.Contains(
            type.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(
                    $".add_{nameof(IEmitSetContract.Changed)}",
                    StringComparison.Ordinal));
        Assert.Contains(
            type.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(
                    $".remove_{nameof(IEmitSetContract.Changed)}",
                    StringComparison.Ordinal));

        Assert.DoesNotContain(
            type.Members,
            member => member.Kind == "property"
                && member.Name.EndsWith(
                    $".{nameof(IEmitSetContract.ExplicitValue)}",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            type.Members,
            member => member.Kind == "event"
                && member.Name.EndsWith(
                    $".{nameof(IEmitSetContract.Changed)}",
                    StringComparison.Ordinal));
        Assert.NotNull(propertyAccessor.MetadataToken);
    }

    [Fact]
    public void IncludeAllExtract_StillKeepsExplicitInterfaceAccessorsAsMethods()
    {
        ApiType type = IncludeAllType();

        Assert.Contains(
            type.Members,
            member => member.Kind == "explicit-interface-implementation"
                && member.Name.EndsWith(
                    $".get_{nameof(IEmitSetContract.ExplicitValue)}",
                    StringComparison.Ordinal));
        Assert.Contains(
            type.Members,
            member => member.Kind == "property"
                && member.Name.EndsWith(
                    $".{nameof(IEmitSetContract.ExplicitValue)}",
                    StringComparison.Ordinal));
        Assert.Contains(
            type.Members,
            member => member.Kind == "method"
                && member.Name == nameof(EmitSetFixture.get_Standalone));
        Assert.DoesNotContain(
            type.Members,
            member => member.Kind == "method" && member.Name == "get_Value");
    }

    [Fact]
    public void ExtractSummary_AgreesWithPublicExtractOnEmitSetMembers()
    {
        string[] publicNames = PublicType().Members
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string[] summaryNames = SummaryType().Members
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(publicNames, summaryNames);
    }

    static ApiType PublicType() => Type(PublicSurface);

    static ApiType IncludeAllType() => Type(IncludeAllSurface);

    static ApiType SummaryType() => Type(SummarySurface);

    static ApiType Type(ApiSurface surface)
        => surface.Types.Single(type => type.Name == nameof(EmitSetFixture));

    static ApiSurface Extract(string path, bool includeAll)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return ApiSurfaceExtractor.Extract(peReader, includeAll);
    }
}

public interface IEmitSetContract
{
    int ExplicitValue { get; }

    event EventHandler Changed;
}

public sealed class EmitSetFixture : IEmitSetContract
{
    public int Value { get; set; }

    public event EventHandler? PublicChanged;

    int IEmitSetContract.ExplicitValue => 42;

    event EventHandler IEmitSetContract.Changed
    {
        add { }
        remove { }
    }

    public int get_Standalone() => 1;

    public void set_Standalone(int value)
    {
    }

    public void add_Standalone(EventHandler? handler)
    {
    }

    public void remove_Standalone(EventHandler? handler)
    {
    }
}
