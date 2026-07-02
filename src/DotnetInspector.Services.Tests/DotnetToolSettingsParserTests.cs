namespace DotnetInspector.Services.Tests;

public class DotnetToolSettingsParserTests
{
    [Fact]
    public void ParseContent_Version2_IsRidSpecificPointerWithCommandsAndRidPackages()
    {
        const string xml = """
            <DotNetCliTool Version="2">
              <Commands>
                <Command Name="mytool" EntryPoint="mytool.dll" Runner="dotnet" />
              </Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="MyTool.win-x64" />
                <RuntimeIdentifierPackage RuntimeIdentifier="linux-x64" Id="MyTool.linux-x64" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """;

        var settings = DotnetToolSettingsParser.ParseContent(xml);

        Assert.NotNull(settings);
        Assert.Equal("2", settings!.Version);
        Assert.True(settings.IsRidSpecificPointerPackage);
        Assert.Equal(["mytool"], settings.Commands);
        Assert.NotNull(settings.RuntimeIdentifierPackages);
        Assert.Collection(settings.RuntimeIdentifierPackages!,
            r => { Assert.Equal("win-x64", r.RuntimeIdentifier); Assert.Equal("MyTool.win-x64", r.PackageId); },
            r => { Assert.Equal("linux-x64", r.RuntimeIdentifier); Assert.Equal("MyTool.linux-x64", r.PackageId); });
    }

    [Fact]
    public void ParseContent_Version1_IsPortableWithNoRidPackages()
    {
        const string xml = """
            <DotNetCliTool Version="1">
              <Commands>
                <Command Name="portabletool" EntryPoint="portabletool.dll" Runner="dotnet" />
              </Commands>
            </DotNetCliTool>
            """;

        var settings = DotnetToolSettingsParser.ParseContent(xml);

        Assert.NotNull(settings);
        Assert.Equal("1", settings!.Version);
        Assert.False(settings.IsRidSpecificPointerPackage);
        Assert.Equal(["portabletool"], settings.Commands);
        Assert.Null(settings.RuntimeIdentifierPackages);
    }

    [Fact]
    public void ParseContent_NoVersionAttribute_TreatedAsPortable()
    {
        const string xml = """
            <DotNetCliTool>
              <Commands>
                <Command Name="legacy" />
              </Commands>
            </DotNetCliTool>
            """;

        var settings = DotnetToolSettingsParser.ParseContent(xml);

        Assert.NotNull(settings);
        Assert.Null(settings!.Version);
        Assert.False(settings.IsRidSpecificPointerPackage);
        Assert.Equal(["legacy"], settings.Commands);
    }

    [Fact]
    public void ParseContent_UnrecognizedVersion_ReturnsNull()
    {
        const string xml = """<DotNetCliTool Version="99" />""";

        Assert.Null(DotnetToolSettingsParser.ParseContent(xml));
    }

    [Fact]
    public void ParseContent_MalformedXml_ReturnsNullWithoutThrowing()
    {
        Assert.Null(DotnetToolSettingsParser.ParseContent("<DotNetCliTool Version=\"2\"><Commands>"));
    }

    [Fact]
    public void FindSettings_LocatesFileAtRootOneAndTwoLevelsDeep()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tool-settings-{Guid.NewGuid():N}");
        try
        {
            // root level
            Directory.CreateDirectory(root);
            var atRoot = Path.Combine(root, "DotnetToolSettings.xml");
            File.WriteAllText(atRoot, "<DotNetCliTool Version=\"1\" />");
            Assert.Equal(atRoot, DotnetToolSettingsParser.FindSettings(root));

            // one level deep (tools/{tfm}/)
            var root1 = Path.Combine(Path.GetTempPath(), $"tool-settings-{Guid.NewGuid():N}");
            var tfmDir = Path.Combine(root1, "net8.0");
            Directory.CreateDirectory(tfmDir);
            var atLevel1 = Path.Combine(tfmDir, "DotnetToolSettings.xml");
            File.WriteAllText(atLevel1, "<DotNetCliTool Version=\"1\" />");
            Assert.Equal(atLevel1, DotnetToolSettingsParser.FindSettings(root1));

            // two levels deep (tools/{tfm}/{rid}/)
            var root2 = Path.Combine(Path.GetTempPath(), $"tool-settings-{Guid.NewGuid():N}");
            var ridDir = Path.Combine(root2, "net8.0", "any");
            Directory.CreateDirectory(ridDir);
            var atLevel2 = Path.Combine(ridDir, "DotnetToolSettings.xml");
            File.WriteAllText(atLevel2, "<DotNetCliTool Version=\"1\" />");
            Assert.Equal(atLevel2, DotnetToolSettingsParser.FindSettings(root2));

            Directory.Delete(root1, recursive: true);
            Directory.Delete(root2, recursive: true);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindSettings_NotPresent_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tool-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "net8.0"));
        try
        {
            Assert.Null(DotnetToolSettingsParser.FindSettings(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindAndParse_EndToEnd_ReturnsParsedSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tool-settings-{Guid.NewGuid():N}");
        var tfmDir = Path.Combine(root, "net8.0");
        Directory.CreateDirectory(tfmDir);
        File.WriteAllText(Path.Combine(tfmDir, "DotnetToolSettings.xml"), """
            <DotNetCliTool Version="2">
              <Commands><Command Name="mytool" /></Commands>
              <RuntimeIdentifierPackages>
                <RuntimeIdentifierPackage RuntimeIdentifier="win-x64" Id="MyTool.win-x64" />
              </RuntimeIdentifierPackages>
            </DotNetCliTool>
            """);
        try
        {
            var settings = DotnetToolSettingsParser.FindAndParse(root);

            Assert.NotNull(settings);
            Assert.True(settings!.IsRidSpecificPointerPackage);
            Assert.Equal(["mytool"], settings.Commands);
            Assert.Single(settings.RuntimeIdentifierPackages!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FindAndParse_NoManifest_ReturnsNull()
    {
        var root = Path.Combine(Path.GetTempPath(), $"tool-settings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(DotnetToolSettingsParser.FindAndParse(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
