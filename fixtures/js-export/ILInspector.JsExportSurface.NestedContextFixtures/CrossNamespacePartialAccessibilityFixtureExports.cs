using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ILInspector.JsExportSurface.NestedContextFixtures.Owners
{
#pragma warning disable CS0414
#pragma warning disable SYSLIB1038
    public class PartiallyAccessibleBaseOwner
    {
        public string Public { get; set; } = "public";

        [JsonInclude]
        private KeyValuePair<ReachableValue, HiddenValue> Mixed =
            new(ReachableValue.Value, HiddenValue.Value);

        protected enum ReachableValue
        {
            Value,
        }

        private enum HiddenValue
        {
            Value,
        }
    }
#pragma warning restore SYSLIB1038
#pragma warning restore CS0414
}

namespace ILInspector.JsExportSurface.NestedContextFixtures.Contexts
{
    using ILInspector.JsExportSurface.NestedContextFixtures.Owners;

    [SupportedOSPlatform("browser")]
    public partial class PartiallyAccessibleDerivedOwner
        : PartiallyAccessibleBaseOwner
    {
        [JSExport]
        public static string GetPartiallyAccessible() =>
            JsonSerializer.Serialize(
                new PartiallyAccessibleBaseOwner(),
                PartiallyAccessibleJsonContext.Default
                    .PartiallyAccessibleBaseOwner);

        public static string SerializeValue() =>
            GetPartiallyAccessible();

        [JsonSerializable(typeof(PartiallyAccessibleBaseOwner))]
        private sealed partial class PartiallyAccessibleJsonContext
            : JsonSerializerContext;
    }
}
