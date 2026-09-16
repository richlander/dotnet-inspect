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

#pragma warning disable CS0414
#pragma warning disable SYSLIB1038
    [SupportedOSPlatform("browser")]
    public partial class NestedContextConditionalValueDto
    {
        [JsonInclude]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        private HiddenPayload? Hidden;

        public static string SerializeNull() =>
            JsonSerializer.Serialize(
                new NestedContextConditionalValueDto(),
                ConditionalJsonContext.Default
                    .NestedContextConditionalValueDto);

        public static string SerializeValue() =>
            JsonSerializer.Serialize(
                new NestedContextConditionalValueDto
                {
                    Hidden = new HiddenPayload("value"),
                },
                ConditionalJsonContext.Default
                    .NestedContextConditionalValueDto);

        private sealed record HiddenPayload(string Value);

        [JsonSerializable(typeof(NestedContextConditionalValueDto))]
        private sealed partial class ConditionalJsonContext
            : JsonSerializerContext;
    }
#pragma warning restore SYSLIB1038
#pragma warning restore CS0414
}
