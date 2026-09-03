using System.Reflection;
using InspectWeb.Engine;
using TsJsExport;

public sealed class ProductionFacadeContextTests
{
    [Fact]
    public void ProductionFacadeContext_DeclaresCurrentMonolithicAssemblySet()
    {
        JsExportRootAttribute root = Assert.Single(
            typeof(InspectWebJsExportContext)
                .GetCustomAttributes<JsExportRootAttribute>(inherit: false));

        Assert.Same(typeof(InspectionEngine), root.RootType);
    }
}
