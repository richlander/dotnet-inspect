using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NestedContextUnsupportedFixtures.Owners
{
#pragma warning disable CS0414
#pragma warning disable SYSLIB1038
    public class NestedContextBaseOwner
    {
        [JsonInclude]
        private HiddenProtected ProtectedField = HiddenProtected.Value;

        [JsonInclude]
        private HiddenPrivateProtected PrivateProtectedField =
            HiddenPrivateProtected.Value;

        protected enum HiddenProtected
        {
            Value,
        }

        private protected enum HiddenPrivateProtected
        {
            Value,
        }
    }
#pragma warning restore SYSLIB1038
#pragma warning restore CS0414
}

namespace ILInspector.JsExportSurface.NestedContextUnsupportedFixtures.Contexts
{
    using ILInspector.JsExportSurface.NestedContextUnsupportedFixtures.Owners;

    public class NestedContextIntermediateOwner
        : NestedContextBaseOwner;

    [SupportedOSPlatform("browser")]
    public partial class NestedContextProtectedValueDto
        : NestedContextIntermediateOwner
    {
        [JSExport]
        public static string GetProtectedValues() =>
            JsonSerializer.Serialize(
                new NestedContextBaseOwner(),
                NestedContextJsonContext.Default
                    .NestedContextBaseOwner);

        [JsonSerializable(typeof(NestedContextBaseOwner))]
        private sealed partial class NestedContextJsonContext
            : JsonSerializerContext;
    }
}
