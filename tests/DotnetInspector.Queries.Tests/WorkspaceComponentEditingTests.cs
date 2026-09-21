using DotnetInspector.Queries.Definitions;

namespace DotnetInspector.Queries.Tests;

public sealed class WorkspaceComponentEditingTests
{
    [Fact]
    public void Describe_EmitsStablePackagePathsAcrossUnrelatedReordering()
    {
        WorkspaceSharePacket first = Parse(
            """
            {"f":4,"t":[["Example.Core","1.0.0","net10.0",null],["Example.Json","2.0.0","net10.0",null]],"g":[[0,1]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}},{"t":1,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);
        WorkspaceSharePacket reordered = Parse(
            """
            {"f":4,"t":[["Example.Json","2.0.0","net10.0",null],["Example.Core","1.0.0","net10.0",null]],"g":[[1,0]],"r":[],"a":1,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}},{"t":1,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);

        WorkspaceComponentDocument firstDocument =
            WorkspaceComponentCatalog.Describe(first);
        WorkspaceComponentDocument reorderedDocument =
            WorkspaceComponentCatalog.Describe(reordered);

        Assert.Equal(
            [
                "packages/example.core@1.0.0/net10.0/~",
                "packages/example.json@2.0.0/net10.0/~",
            ],
            firstDocument.Packages.Select(package => package.Path.Value));
        Assert.Equal(
            firstDocument.Packages.Select(package => package.Path.Value)
                .Order(),
            reorderedDocument.Packages.Select(package => package.Path.Value)
                .Order());
    }

    [Fact]
    public void AddThenRemove_ReturnsNewCanonicalPackets()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[["Example.Core","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);
        string original = WorkspaceSharePacketCodec.Encode(input);

        WorkspacePackageComponentEditResult added =
            WorkspacePackageComponentEditor.Add(
                input,
                "Example.Json",
                "2.0.0",
                framework: null,
                runtimeIdentifier: null);

        Assert.True(added.Succeeded);
        WorkspaceComponentDocument addedDocument =
            WorkspaceComponentCatalog.Describe(added.Packet!);
        WorkspacePackageComponentDescriptor addedPackage =
            Assert.Single(
                addedDocument.Packages,
                package => package.PackageId == "Example.Json");
        Assert.Equal(
            "packages/example.json@2.0.0/net10.0/~",
            addedPackage.Path.Value);
        Assert.Equal(
            ["contexts/g0"],
            addedPackage.Contexts.Select(context => context.Value));

        WorkspacePackageComponentEditResult removed =
            WorkspacePackageComponentEditor.Remove(
                added.Packet!,
                addedPackage.Path);

        Assert.True(removed.Succeeded);
        Assert.Equal(original, WorkspaceSharePacketCodec.Encode(input));
        Assert.Equal(
            original,
            WorkspaceSharePacketCodec.Encode(removed.Packet!));
    }

    [Fact]
    public void Add_DuplicateAndIncompatibleTargetAreVisibleRefusals()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[["Example.Core","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);

        WorkspacePackageComponentEditResult duplicate =
            WorkspacePackageComponentEditor.Add(
                input,
                "example.core",
                "1.0.0",
                "net10.0",
                runtimeIdentifier: null);
        WorkspacePackageComponentEditResult incompatible =
            WorkspacePackageComponentEditor.Add(
                input,
                "Example.Json",
                "2.0.0",
                "net9.0",
                runtimeIdentifier: null);

        Assert.Equal(
            WorkspacePackageComponentEditFailureKind.DuplicateComponent,
            duplicate.Failure?.Kind);
        Assert.Equal(
            WorkspacePackageComponentEditFailureKind.IncompatibleContext,
            incompatible.Failure?.Kind);
    }

    [Fact]
    public void Add_ToRegistrationOnlyPacketSupportsFloatingPackage()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[],"g":[],"r":[["p","Microsoft.Extensions."]],"a":null,"x":null,"v":[{"t":null,"u":{"k":"workspace"}}]}
            """);

        WorkspacePackageComponentEditResult result =
            WorkspacePackageComponentEditor.Add(
                input,
                "Example.Core",
                version: null,
                framework: "net10.0",
                runtimeIdentifier: null);

        Assert.True(result.Succeeded);
        Assert.Equal(input.Registrations, result.Packet!.Registrations);
        Assert.Equal(0, result.Packet.SelectedContextIndex);
        Assert.Equal(
            "packages/example.core@~/net10.0/~",
            Assert.Single(
                WorkspaceComponentCatalog.Describe(result.Packet).Packages)
                .Path.Value);
    }

    [Fact]
    public void AddThenRemove_PreservesFormat5PackageSources()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":5,"s":[["https://api.nuget.org/v3/index.json"]],"t":[["Example.Core","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":null,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0}]}
            """);

        WorkspacePackageComponentEditResult added =
            WorkspacePackageComponentEditor.Add(
                input,
                "Example.Json",
                "2.0.0",
                framework: null,
                runtimeIdentifier: null);
        WorkspacePackageComponentPath addedPath =
            Assert.Single(
                WorkspaceComponentCatalog.Describe(added.Packet!).Packages,
                package => package.PackageId == "Example.Json").Path;
        WorkspacePackageComponentEditResult removed =
            WorkspacePackageComponentEditor.Remove(
                added.Packet!,
                addedPath);

        Assert.True(removed.Succeeded);
        Assert.Equal(
            WorkspaceSharePacketCodec.Encode(input),
            WorkspaceSharePacketCodec.Encode(removed.Packet!));
    }

    [Fact]
    public void Remove_FocusedPackageDropsItsStateAndUnreferencedQuery()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[["Example.Core","1.0.0","net10.0",null],["Avalonia","12.1.2","net10.0",null]],"g":[[0,1]],"r":[["p","Microsoft.Extensions."]],"a":1,"x":0,"q":[["type-query/v1",{}]],"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}},{"t":1,"r":{"k":"member","l":["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"],"y":"Avalonia.Data.MultiBinding","s":"M:Avalonia.Data.MultiBinding.#ctor()"},"u":{"k":"type"},"f":"type.metadata","q":[0],"l":[["Avalonia.Base","12.1.2.0",null,"c8d484a7012f9a8b"]]}]}
            """);
        WorkspacePackageComponentPath.TryCreate(
            "packages/avalonia@12.1.2/net10.0/~",
            out WorkspacePackageComponentPath? path,
            out _);

        WorkspacePackageComponentEditResult result =
            WorkspacePackageComponentEditor.Remove(input, path!);

        Assert.True(result.Succeeded);
        Assert.Null(result.Packet!.FocusedTabIndex);
        Assert.Empty(result.Packet.Queries);
        Assert.Single(result.Packet.Tabs);
        Assert.Equal(2, result.Packet.ViewStates.Count);
    }

    [Fact]
    public void Remove_LastPackageFromOtherwiseEmptyPacketIsRefused()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[["Example.Core","1.0.0","net10.0",null]],"g":[[0]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);
        WorkspacePackageComponentPath path =
            Assert.Single(
                WorkspaceComponentCatalog.Describe(input).Packages).Path;

        WorkspacePackageComponentEditResult result =
            WorkspacePackageComponentEditor.Remove(input, path);

        Assert.Equal(
            WorkspacePackageComponentEditFailureKind.InvalidDerivedPacket,
            result.Failure?.Kind);
        Assert.Contains("requires at least", result.Failure?.Detail);
    }

    [Fact]
    public void Remove_SharedPackageDropsEmptySelectedContextAndSelectsFirstSurvivor()
    {
        WorkspaceSharePacket input = Parse(
            """
            {"f":4,"t":[["Example.Core","1.0.0","net10.0",null],["Example.Json","2.0.0","net10.0",null]],"g":[[0],[0,1]],"r":[],"a":0,"x":0,"v":[{"t":null,"u":{"k":"workspace"}},{"t":0,"u":{"k":"package"},"r":{"k":"package"}},{"t":1,"u":{"k":"package"},"r":{"k":"package"}}]}
            """);
        WorkspacePackageComponentPath path =
            Assert.Single(
                WorkspaceComponentCatalog.Describe(input).Packages,
                package => package.PackageId == "Example.Core").Path;

        WorkspacePackageComponentEditResult result =
            WorkspacePackageComponentEditor.Remove(input, path);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Packet!.SelectedContextIndex);
        Assert.Null(result.Packet.FocusedTabIndex);
        Assert.Equal([0], Assert.Single(result.Packet.Contexts).TabIndexes);
        Assert.Equal(
            "Example.Json",
            Assert.Single(result.Packet.Tabs).Source);
    }

    private static WorkspaceSharePacket Parse(string json) =>
        WorkspaceSharePacketCodec.ParseJson(
            json,
            TestContext.Current.CancellationToken);
}
