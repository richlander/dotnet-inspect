using System.Reflection.PortableExecutable;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

/// <summary>
/// Gate for #3975: method-list membership uses property/event
/// <c>MethodSemantics</c>, not a <c>get_</c>/<c>set_</c>/<c>add_</c>/<c>remove_</c>
/// name prefix. Ordinary prefix-named methods stay methods; ordinary accessors
/// do not; a private MethodImpl accessor stays
/// <c>explicit-interface-implementation</c> because its private property or
/// event row would otherwise hide the public contract. A public MethodImpl
/// accessor — covariant override or static-abstract implementation — stays on
/// that public property or event row.
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
    public void PublicExtract_OmitsPublicMethodImplAccessors()
    {
        ApiType covariant = Type(PublicSurface, nameof(CovariantEmitDerived));
        Assert.Contains(
            covariant.Members,
            member => member.Kind == "property"
                && member.Name == nameof(CovariantEmitDerived.P));
        Assert.DoesNotContain(
            covariant.Members,
            member => member.Name == "get_P");

        ApiType staticImpl = Type(PublicSurface, nameof(StaticAbstractEmitImpl));
        Assert.Contains(
            staticImpl.Members,
            member => member.Kind == "property"
                && member.Name == nameof(StaticAbstractEmitImpl.Value));
        Assert.DoesNotContain(
            staticImpl.Members,
            member => member.Name == "get_Value");

        ApiType implicitImpl = Type(PublicSurface, nameof(ImplicitEmitImpl));
        Assert.Contains(
            implicitImpl.Members,
            member => member.Kind == "property"
                && member.Name == nameof(ImplicitEmitImpl.Count));
        Assert.DoesNotContain(
            implicitImpl.Members,
            member => member.Name == "get_Count");
    }

    [Theory]
    [InlineData(nameof(CovariantEmitDerived), nameof(CovariantEmitDerived.P))]
    [InlineData(nameof(StaticAbstractEmitImpl), nameof(StaticAbstractEmitImpl.Value))]
    [InlineData(nameof(ImplicitEmitImpl), nameof(ImplicitEmitImpl.Count))]
    public void PublicAccessorProjection_RetainsMethodClassification(
        string typeName,
        string propertyName)
    {
        ApiType type = Type(PublicSurface, typeName);
        ApiMember property = Assert.Single(
            type.Members,
            member => member.Kind == "property" && member.Name == propertyName);
        Assert.NotNull(property.SignatureModel);
        Assert.All(
            property.SignatureModel.Accessors,
            accessor => Assert.False(accessor.IsExplicitInterfaceImplementation));
        Assert.Equal("method", Assert.Single(ApiMemberAccessors.Create(property, type)).Kind);
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

    static ApiType PublicType() => Type(PublicSurface, nameof(EmitSetFixture));

    static ApiType IncludeAllType() => Type(IncludeAllSurface, nameof(EmitSetFixture));

    static ApiType SummaryType() => Type(SummarySurface, nameof(EmitSetFixture));

    static ApiType Type(ApiSurface surface, string typeName)
        => surface.Types.Single(type => type.Name == typeName);

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

public class CovariantEmitBase
{
    public virtual object P => new();
}

public sealed class CovariantEmitDerived : CovariantEmitBase
{
    public override string P => "";
}

public interface IStaticAbstractEmit
{
    static abstract int Value { get; }
}

public sealed class StaticAbstractEmitImpl : IStaticAbstractEmit
{
    public static int Value => 1;
}

public interface IImplicitEmit
{
    int Count { get; }
}

public sealed class ImplicitEmitImpl : IImplicitEmit
{
    public int Count => 1;
}
