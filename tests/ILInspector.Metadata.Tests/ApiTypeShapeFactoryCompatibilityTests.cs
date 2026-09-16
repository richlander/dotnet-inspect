using System.Collections.Immutable;
using System.Reflection;
using ILInspector.Metadata;

namespace ILInspector.Metadata.Tests;

public sealed class ApiTypeShapeFactoryCompatibilityTests
{
    [Fact]
    public void OriginalFactoryClrSignaturesRemainAvailable()
    {
        Assert.NotNull(Factory(
            nameof(ApiTypeShape.Named),
            [typeof(ApiTypeReferenceIdentity)]));
        Assert.NotNull(Factory(
            nameof(ApiTypeShape.GenericInstance),
            [
                typeof(ApiTypeReferenceIdentity),
                typeof(ImmutableArray<ApiTypeShape>),
            ]));
    }

    static MethodInfo? Factory(string name, Type[] parameterTypes) =>
        typeof(ApiTypeShape).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: parameterTypes,
            modifiers: null);
}
