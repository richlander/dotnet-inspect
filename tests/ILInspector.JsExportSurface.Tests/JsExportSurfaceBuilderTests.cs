using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ILInspector.Analysis;
using ILInspector.JsExportSurface.Fixtures;
using ILInspector.JsExportSurface.NamingFixtures;
using ILInspector.JsExportSurface.OperatorFixtures;
using ILInspector.JsExportSurface.PublishabilityFixtures;
using ILInspector.JsExportSurface.ScalarFixtures;
using ILInspector.Metadata;

namespace ILInspector.JsExportSurface.Tests;

public sealed partial class JsExportSurfaceBuilderTests
{
    [Fact]
    public void Extract_CapturesStructuredSerializerContextBaseIdentity()
    {
        using FileStream stream = File.OpenRead(
            typeof(FixtureExports).Assembly.Location);
        using var peReader = new PEReader(stream);
        ApiSurface apiSurface = ApiSurfaceExtractor.Extract(
            peReader,
            includeAll: true);
        ApiType context = Assert.Single(
            apiSurface.Types,
            type => type.Name == "FixtureJsonContext");

        Assert.Equal(
            TopLevelDefinitionName(
                "System.Text.Json.Serialization",
                "JsonSerializerContext"),
            context.BaseTypeReference?.DefinitionName);
    }

    [Fact]
    public void Build_DiscoversAllJsExportFunctions()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        var names = surface.Functions.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(59, surface.Functions.Count);
        Assert.Contains("GetWidget", names);
        Assert.Contains("GetWidgetAsync", names);
        Assert.Contains("RenameWidgetAsync", names);
        Assert.Contains("RenameNormalizedWidgetAsync", names);
        Assert.Contains("GetWidgetSerializedBeforeAwait", names);
        Assert.Contains(
            "GetWidgetConditionallySerializedBeforeAwait",
            names);
        Assert.Contains("GetStringArrayAsyncAfterAwait", names);
        Assert.Contains("GetWidgetOrRawAfterAwait", names);
        Assert.Contains(
            "GetWidgetFromIncompleteFlowAfterAwait",
            names);
        Assert.Contains("GetWidgetThroughLocalAsync", names);
        Assert.Contains("EchoBytes", names);
        Assert.Contains("ReportValue", names);
        Assert.Contains("ReportValueAgain", names);
        Assert.Contains("ReportNullableText", names);
        Assert.Contains("TransformValue", names);
        Assert.Contains("ObserveValues", names);
        Assert.Contains("GetRegisteredString", names);
        Assert.Contains("Ping", names);
        Assert.Contains("RenameWidget", names);
        Assert.Contains("RenameWidgetForOwner", names);
        Assert.Contains("ReadWidgetOrAudit", names);
        Assert.Contains("RenameNormalizedWidget", names);
        Assert.Contains("WidgetMatchesAudit", names);
        Assert.Contains("GetWidgetOrOwner", names);
        Assert.Contains("GetWidgetOrRawOk", names);
        Assert.Contains("GetWidgetOrCached", names);
        Assert.Contains("GetWidgetOrCachedViaLocal", names);
        Assert.Contains("GetWidgetFromEitherJsonBranch", names);
        Assert.Contains("GetWidgetArray", names);
        Assert.Contains("GetWidgetSummary", names);
        Assert.Contains("GetWidgetPermissionSummary", names);
        Assert.Contains("GetWidgetPrioritySummary", names);
        Assert.Contains("GetWidgetAudit", names);
        Assert.Contains("GetCustomNamedGenerated", names);
        Assert.Contains("GetCustomNamedHandwritten", names);
        Assert.Contains("SetUnrelatedAsyncBuilder", names);
        Assert.Contains("RoundTripWidgetWithRuntimeTypeInfo", names);
        Assert.Contains("RoundTripWidgetWithUnrelatedTypeInfo", names);
        Assert.Contains("QueryPackage", names);
        Assert.Contains("GetInternalContextWidget", names);
        Assert.Contains("GetInternalContextCamelWidget", names);
        Assert.Contains("GetNeedsUnmappedType", names);
        Assert.Contains("GetDirectionalOutput", names);
        Assert.Contains("SetDirectionalInput", names);
        Assert.Contains("SetDirectionalSharedInput", names);
        Assert.Contains("SetDirectionalAccessorInput", names);
        Assert.Contains("RoundTripDirectional", names);
        Assert.Contains("GetClosedGenericRoot", names);
        Assert.Contains("GetRegisteredInt", names);
        Assert.Contains("GetRegisteredIntArray", names);
        Assert.Contains("GetRegisteredByteArray", names);
        Assert.Contains("GetRegisteredDecimal", names);
        Assert.Contains("GetRegisteredDecimalArray", names);
        Assert.Contains("ReadRegisteredInt", names);
        Assert.Contains("GetContextSerializationOnly", names);
        Assert.Contains("SetContextSerializationOnly", names);
        Assert.Contains("SetMetadataOverride", names);
    }

    [Fact]
    public void Build_ReportsFunctionSignaturesUnmodified()
    {
        ILInspector.JsExportSurface.JsExportSurface surface = BuildFixtureSurface();

        JsExportFunction getWidget = surface.Functions.Single(f => f.Name == "GetWidget");
        Assert.Equal("string", getWidget.ReturnType);
        Assert.Equal(2, getWidget.Parameters.Count);

        JsExportFunction getWidgetAsync = surface.Functions.Single(f => f.Name == "GetWidgetAsync");
        Assert.Contains("Task", getWidgetAsync.ReturnType, StringComparison.Ordinal);
        Assert.Contains("string", getWidgetAsync.ReturnType, StringComparison.Ordinal);

        JsExportFunction ping = surface.Functions.Single(f => f.Name == "Ping");
        Assert.Contains("Task", ping.ReturnType, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PublishesAuthenticatedSynchronousDelegateSignatures()
    {
        ILInspector.JsExportSurface.JsExportSurface surface =
            BuildFixtureSurfaceWithBodies();

        JsExportDelegateParameter action = Assert.Single(
            Assert.Single(
                surface.Functions,
                function => function.Name == "ReportValue")
            .DelegateParameters);
        Assert.Equal(0, action.ParameterIndex);
        Assert.Equal(JsExportDelegateKind.Action, action.Kind);
        Assert.Equal(
            "int",
            Assert.Single(action.ParameterTypes).ToDisplayString());
        Assert.Null(action.ReturnType);

        JsExportDelegateParameter func = Assert.Single(
            Assert.Single(
                surface.Functions,
                function => function.Name == "TransformValue")
            .DelegateParameters);
        Assert.Equal(JsExportDelegateKind.Func, func.Kind);
        Assert.Collection(
            func.ParameterTypes,
            type => Assert.Equal("int", type.ToDisplayString()),
            type => Assert.Equal("string", type.ToDisplayString()));
        Assert.Equal("bool", func.ReturnType?.ToDisplayString());
    }

    [Fact]
    public void TryGetDelegateShape_RejectsDecodedFourArgumentAction()
    {
        LibraryBodyIndex bodyIndex = LibraryBodyIndex.Open(
            typeof(JsExportSurfaceBuilderTests).Assembly.Location,
            LibraryBodyAnalysisFeatures.MethodEvidence);
        MethodIdentity method = Assert.Single(
            bodyIndex.DeclaredMethods,
            candidate => candidate.Name
                == nameof(FourArgumentCallback));
        TypeRef callbackType = Assert.Single(method.ParameterTypes);

        Assert.False(
            JsExportSurfaceBuilder.TryGetDelegateShape(
                callbackType,
                out _,
                out _,
                out _));
    }
}
