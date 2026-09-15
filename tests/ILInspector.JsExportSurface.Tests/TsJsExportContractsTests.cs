using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using TsJsExport;

namespace ILInspector.JsExportSurface.Tests;

public sealed class TsJsExportContractsTests
{
    [Fact]
    public void RootAttributeHasExactMetadataContract()
    {
        Type attribute = typeof(JsExportRootAttribute);
        AttributeUsageAttribute usage =
            Assert.Single(attribute.GetCustomAttributes<AttributeUsageAttribute>());
        ConstructorInfo constructor = Assert.Single(
            attribute.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        ParameterInfo parameter = Assert.Single(constructor.GetParameters());
        PropertyInfo rootType = Assert.Single(
            attribute.GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => property.DeclaringType == attribute);

        Assert.True(attribute.IsSealed);
        Assert.Equal(typeof(Attribute), attribute.BaseType);
        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.True(usage.AllowMultiple);
        Assert.False(usage.Inherited);
        Assert.Equal(typeof(Type), parameter.ParameterType);
        Assert.Equal(nameof(JsExportRootAttribute.RootType), rootType.Name);
        Assert.Equal(typeof(Type), rootType.PropertyType);
        Assert.True(rootType.CanRead);
        Assert.False(rootType.CanWrite);
    }

    [Fact]
    public void ContractsProjectHasNoProjectOrPackageReferences()
    {
        string attributeAssemblyPath =
            typeof(JsExportRootAttribute).Assembly.Location;
        string projectPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "TsJsExport.Contracts",
            "TsJsExport.Contracts.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.DoesNotContain(
            project.Descendants(),
            element =>
                element.Name.LocalName is "ProjectReference"
                    or "PackageReference");

        using var stream = File.OpenRead(attributeAssemblyPath);
        using var peReader = new PEReader(stream);
        MetadataReader reader = peReader.GetMetadataReader();
        Assert.Equal(
            ["System.Runtime"],
            reader.AssemblyReferences.Select(handle =>
                reader.GetString(
                    reader.GetAssemblyReference(handle).Name)));
    }

    static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory =
                new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "dotnet-inspect.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the repository root.");
    }
}
