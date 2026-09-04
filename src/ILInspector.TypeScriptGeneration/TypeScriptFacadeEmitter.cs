using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using ILInspector.JsExportSurface;
using ILInspector.Metadata;

namespace ILInspector.TypeScriptGeneration;

internal static class TypeScriptFacadeEmitter
{
    private static readonly string[] InfrastructureBindings =
    [
        "dotnet",
        "RuntimeAPI",
        "JsExportRuntime",
        "then",
        "undefined",
        "createRuntime",
        "initializeRuntime",
        "runEntryPoint",
        "$ManagedExports",
        "$notInitializedError",
        "$runtime",
        "$managedExports",
        "$initialization",
        "$initializationFailure",
        "$initializeRuntimeCore",
        "$requireRuntime",
        "$requireManagedExports",
        "$ownDataProperty",
        "$validateManagedExports",
    ];

    public static string Emit(
        global::ILInspector.JsExportSurface.JsExportSurface surface,
        string runtimeModule,
        TypeScriptGenerationDiagnostics? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(runtimeModule);
        string assemblyName = surface.AssemblyIdentity?.Name
            ?? throw new UnsupportedWireContractException(
                "assembly",
                "TypeScript facade generation requires an assembly identity");

        JsExportFunction[] functions =
        [
            .. surface.Functions.OrderBy(
                CanonicalOperationIdentity,
                StringComparer.Ordinal),
        ];
        ValidateRuntimeIdentities(functions);

        TypeScriptNameAllocator names =
            TypeScriptNameAllocator.Create(surface, functions);
        var signatures = new Dictionary<JsExportFunction, TypeScriptFunctionSignature>();
        foreach (JsExportFunction function in functions)
        {
            TypeScriptFunctionSignature signature =
                DtsEmitter.GetFunctionSignature(
                    surface,
                    function,
                    diagnostics,
                    names.TypeNames);
            signatures.Add(
                function,
                names.Apply(function, signature));
        }

        var sb = new StringBuilder();
        sb.Append("import { dotnet } from ")
            .Append(Quote(runtimeModule))
            .Append(";\n\n");
        sb.Append(DtsEmitter.EmitWireDeclarations(
            surface,
            diagnostics,
            names.TypeNames));

        ExportPathNode exportTree = BuildExportTree(functions);
        EmitManagedExportsType(sb, exportTree, signatures);
        EmitLifecycle(sb, assemblyName, functions);

        foreach (JsExportFunction function in functions)
        {
            EmitFunction(sb, function, signatures[function]);
        }

        return sb.ToString();
    }

    static void ValidateRuntimeIdentities(
        IReadOnlyList<JsExportFunction> functions)
    {
        var operationIdentities = new HashSet<string>(StringComparer.Ordinal);
        var runtimeIdentities = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsExportFunction function in functions)
        {
            string operationIdentity = CanonicalOperationIdentity(function);
            if (IsValueTask(function.ReturnType))
            {
                throw new UnsupportedWireContractException(
                    operationIdentity,
                    "ValueTask returns are not supported by the [JSExport] runtime");
            }
            if (!operationIdentities.Add(operationIdentity))
            {
                throw new UnsupportedWireContractException(
                    operationIdentity,
                    "duplicate managed operation identity");
            }
            if (string.IsNullOrEmpty(function.RuntimeDispatchKey))
            {
                throw new UnsupportedWireContractException(
                    operationIdentity,
                    "an authenticated runtime dispatch key is required");
            }
            if (function.DeclaringType.Split('.') is not { Length: > 0 } segments
                || segments.Any(string.IsNullOrEmpty))
            {
                throw new UnsupportedWireContractException(
                    operationIdentity,
                    "the runtime declaring-type path contains an empty segment");
            }

            string runtimeIdentity =
                function.DeclaringType + "\0" + function.RuntimeDispatchKey;
            if (!runtimeIdentities.Add(runtimeIdentity))
            {
                throw new UnsupportedWireContractException(
                    operationIdentity,
                    "multiple managed operations use the same runtime dispatch identity");
            }
        }

        static bool IsValueTask(string type)
        {
            string trimmed = type.Trim();
            return trimmed is "ValueTask"
                or "System.Threading.Tasks.ValueTask"
                || trimmed.StartsWith("ValueTask<", StringComparison.Ordinal)
                || trimmed.StartsWith(
                    "System.Threading.Tasks.ValueTask<",
                    StringComparison.Ordinal);
        }
    }

    static ExportPathNode BuildExportTree(
        IReadOnlyList<JsExportFunction> functions)
    {
        var root = new ExportPathNode();
        foreach (JsExportFunction function in functions)
        {
            ExportPathNode node = root;
            foreach (string segment in function.DeclaringType.Split('.'))
            {
                if (!node.Children.TryGetValue(segment, out ExportPathNode? child))
                {
                    child = new ExportPathNode();
                    node.Children.Add(segment, child);
                }
                node = child;
            }
            node.Functions.Add(function.RuntimeDispatchKey!, function);
        }
        return root;
    }

    static void EmitManagedExportsType(
        StringBuilder sb,
        ExportPathNode root,
        IReadOnlyDictionary<JsExportFunction, TypeScriptFunctionSignature>
            signatures)
    {
        sb.Append("type $ManagedExports = ");
        EmitExportPathNode(sb, root, signatures, 0);
        sb.Append(";\n\n");
    }

    static void EmitExportPathNode(
        StringBuilder sb,
        ExportPathNode node,
        IReadOnlyDictionary<JsExportFunction, TypeScriptFunctionSignature>
            signatures,
        int indent)
    {
        sb.Append("{\n");
        foreach ((string segment, ExportPathNode child) in node.Children)
        {
            AppendIndent(sb, indent + 1);
            sb.Append("readonly ").Append(Quote(segment)).Append(": ");
            EmitExportPathNode(sb, child, signatures, indent + 1);
            sb.Append(";\n");
        }
        foreach ((string runtimeKey, JsExportFunction function)
            in node.Functions)
        {
            TypeScriptFunctionSignature signature = signatures[function];
            AppendIndent(sb, indent + 1);
            sb.Append("readonly ").Append(Quote(runtimeKey)).Append(": (")
                .Append(string.Join(
                    ", ",
                    signature.Parameters.Select(
                        parameter => $"{parameter.Name}: {parameter.Type}")))
                .Append(") => ")
                .Append(signature.RawReturnType)
                .Append(";\n");
        }
        AppendIndent(sb, indent);
        sb.Append('}');
    }

    static void EmitLifecycle(
        StringBuilder sb,
        string assemblyName,
        IReadOnlyList<JsExportFunction> functions)
    {
        sb.Append(
            """
            export interface JsExportRuntime {
              readonly getAssemblyExports: (assemblyName: string) => Promise<unknown>;
              readonly runMain: (
                mainAssemblyName?: string,
                args?: string[],
              ) => Promise<number>;
            }

            const $notInitializedError = new Error("The .NET runtime facade is not initialized.");
            let $runtime: JsExportRuntime | undefined;
            let $managedExports: $ManagedExports | undefined;
            let $initialization: Promise<void> | undefined;
            let $initializationFailure: { readonly error: unknown } | undefined;

            function $ownDataProperty(value: unknown, key: string): unknown {
              if (value === null || (typeof value !== "object" && typeof value !== "function")) {
                throw new Error(`Managed export path '${key}' has a non-object parent.`);
              }
              const descriptor = Object.getOwnPropertyDescriptor(value, key);
              if (descriptor === undefined || !("value" in descriptor)) {
                throw new Error(`Managed export path '${key}' is not an own data property.`);
              }
              return descriptor.value;
            }

            function $requireRuntime(): JsExportRuntime {
              if ($initializationFailure !== undefined) throw $initializationFailure.error;
              if ($runtime === undefined) {
                throw $notInitializedError;
              }
              return $runtime;
            }

            function $requireManagedExports(): $ManagedExports {
              if ($initializationFailure !== undefined) throw $initializationFailure.error;
              if ($managedExports === undefined) {
                throw $notInitializedError;
              }
              return $managedExports;
            }

            function $validateManagedExports(exports: unknown): asserts exports is $ManagedExports {

            """);

        foreach (JsExportFunction function in functions)
        {
            string identity =
                function.DeclaringType + "." + function.RuntimeDispatchKey;
            sb.Append("  {\n")
                .Append("    let value: unknown = exports;\n");
            foreach (string segment in function.DeclaringType.Split('.'))
            {
                sb.Append("    value = $ownDataProperty(value, ")
                    .Append(Quote(segment))
                    .Append(");\n");
            }
            sb.Append("    value = $ownDataProperty(value, ")
                .Append(Quote(function.RuntimeDispatchKey!))
                .Append(");\n")
                .Append("    if (typeof value !== \"function\") {\n")
                .Append("      throw new Error(")
                .Append(Quote(
                    $"Managed export '{identity}' is not callable."))
                .Append(");\n")
                .Append("    }\n")
                .Append("  }\n");
        }

        sb.Append(
            """
            }

            async function $initializeRuntimeCore(
              runtime: JsExportRuntime,
            ): Promise<void> {
              const exports: unknown = await runtime.getAssemblyExports(
            """)
            .Append(Quote(assemblyName))
            .Append(");\n")
            .Append(
                """
              $validateManagedExports(exports);
              $runtime = runtime;
              $managedExports = exports;
            }

            export function createRuntime(): Promise<JsExportRuntime> {
              return dotnet.create();
            }

            export function initializeRuntime(
              runtime?: JsExportRuntime | PromiseLike<JsExportRuntime>,
            ): Promise<void> {
              if ($initialization === undefined) {
                $initialization = Promise.resolve()
                  .then(() => runtime === undefined ? createRuntime() : runtime)
                  .then($initializeRuntimeCore)
                  .catch((error: unknown) => {
                    $initializationFailure = { error };
                    throw error;
                  });
              }
              return $initialization;
            }

            export function runEntryPoint(
              mainAssemblyName?: string,
              args?: string[],
            ): Promise<number> {
              return $requireRuntime().runMain(mainAssemblyName, args);
            }
            """)
            .Append("\n\n");
    }

    static void EmitFunction(
        StringBuilder sb,
        JsExportFunction function,
        TypeScriptFunctionSignature signature)
    {
        string parameters = string.Join(
            ", ",
            signature.Parameters.Select(
                parameter => $"{parameter.Name}: {parameter.Type}"));
        string arguments = string.Join(
            ", ",
            signature.Parameters.Select(parameter => parameter.Name));
        string call = "$requireManagedExports()"
            + string.Concat(
                function.DeclaringType.Split('.').Select(
                    segment => $"[{Quote(segment)}]"))
            + $"[{Quote(function.RuntimeDispatchKey!)}]({arguments})";

        sb.Append("export ")
            .Append(signature.IsAsync ? "async " : "")
            .Append("function ")
            .Append(signature.Name)
            .Append('(')
            .Append(parameters)
            .Append("): ")
            .Append(signature.PublicReturnType)
            .Append(" {\n");

        if (signature.ParsesJson)
        {
            sb.Append("  const $result = ")
                .Append(signature.IsAsync ? "await " : "")
                .Append(call)
                .Append(";\n");
            if (signature.JsonEnvelopeMayBeNull)
            {
                sb.Append("  if ($result === null) {\n")
                    .Append("    throw new Error(")
                    .Append(Quote(
                        $"Managed export '{function.DeclaringType}."
                        + $"{function.RuntimeDispatchKey}' returned null "
                        + "for an authenticated JSON envelope."))
                    .Append(");\n")
                    .Append("  }\n");
            }
            sb.Append("  const $parsed: unknown = JSON.parse($result);\n")
                .Append("  return $parsed as ")
                .Append(signature.IsAsync
                    ? UnwrapPromise(signature.PublicReturnType)
                    : signature.PublicReturnType)
                .Append(";\n");
        }
        else
        {
            sb.Append("  return ")
                .Append(signature.IsAsync ? "await " : "")
                .Append(call)
                .Append(";\n");
        }
        sb.Append("}\n\n");
    }

    static string UnwrapPromise(string type) =>
        type.StartsWith("Promise<", StringComparison.Ordinal)
            && type.EndsWith('>')
            ? type[8..^1]
            : throw new InvalidOperationException(
                $"Expected Promise return type, found '{type}'.");

    static string CanonicalOperationIdentity(JsExportFunction function) =>
        function.DeclaringType
        + "::"
        + function.Name
        + "("
        + string.Join(",", function.Parameters.Select(parameter => parameter.Type))
        + ")";

    static string Quote(string value) =>
        "\"" + JavaScriptEncoder.Default.Encode(value) + "\"";

    static void AppendIndent(StringBuilder sb, int depth) =>
        sb.Append(' ', depth * 2);

    private sealed class ExportPathNode
    {
        public SortedDictionary<string, ExportPathNode> Children { get; } =
            new(StringComparer.Ordinal);

        public SortedDictionary<string, JsExportFunction> Functions { get; } =
            new(StringComparer.Ordinal);
    }

    private sealed class TypeScriptNameAllocator
    {
        private readonly HashSet<string> _moduleBindings;
        private readonly Dictionary<ApiType, string> _typeNames;
        private readonly Dictionary<JsExportFunction, string> _operationNames;
        private readonly Dictionary<JsExportFunction, string[]> _parameterNames;

        private TypeScriptNameAllocator(
            HashSet<string> moduleBindings,
            Dictionary<ApiType, string> typeNames,
            Dictionary<JsExportFunction, string> operationNames,
            Dictionary<JsExportFunction, string[]> parameterNames)
        {
            _moduleBindings = moduleBindings;
            _typeNames = typeNames;
            _operationNames = operationNames;
            _parameterNames = parameterNames;
        }

        public static TypeScriptNameAllocator Create(
            global::ILInspector.JsExportSurface.JsExportSurface surface,
            IReadOnlyList<JsExportFunction> functions)
        {
            var moduleBindings = new HashSet<string>(
                InfrastructureBindings,
                StringComparer.Ordinal);
            var typeNames = new Dictionary<ApiType, string>();
            foreach (ApiType type in surface.Records
                .Concat(surface.Enums)
                .OrderBy(CanonicalTypeIdentity, StringComparer.Ordinal))
            {
                if (!TypeScriptIdentifier.IsIdentifierName(type.Name))
                {
                    throw new UnsupportedWireContractException(
                        type.MetadataToken is { } token
                            ? $"type 0x{token:X8}"
                            : "JSON type",
                        "managed type names must be TypeScript identifier names");
                }

                typeNames.Add(
                    type,
                    Allocate(
                        moduleBindings,
                        type.Name,
                        "type",
                        CanonicalTypeIdentity(type),
                        TypeScriptIdentifier.IsTypeDeclarationIdentifier));
            }

            var operationNames =
                new Dictionary<JsExportFunction, string>();
            var parameterNames =
                new Dictionary<JsExportFunction, string[]>();
            foreach (JsExportFunction function in functions)
            {
                string identity = CanonicalOperationIdentity(function);
                operationNames.Add(
                    function,
                    Allocate(
                        moduleBindings,
                        CamelCase.FromPascalCase(function.Name),
                        "operation",
                        identity,
                        TypeScriptIdentifier.IsStrictModeBindingIdentifier));

                var localBindings = new HashSet<string>(
                    InfrastructureBindings,
                    StringComparer.Ordinal)
                {
                    "$result",
                    "$parsed",
                };
                string[] allocatedParameters =
                    new string[function.Parameters.Count];
                for (int index = 0; index < function.Parameters.Count; index++)
                {
                    ApiParameter parameter = function.Parameters[index];
                    allocatedParameters[index] = Allocate(
                        localBindings,
                        CamelCase.FromPascalCase(parameter.Name),
                        "parameter",
                        $"{identity}#{index}",
                        TypeScriptIdentifier.IsStrictModeBindingIdentifier);
                }
                parameterNames.Add(function, allocatedParameters);
            }

            return new TypeScriptNameAllocator(
                moduleBindings,
                typeNames,
                operationNames,
                parameterNames);
        }

        public IReadOnlyDictionary<ApiType, string> TypeNames =>
            _typeNames;

        public TypeScriptFunctionSignature Apply(
            JsExportFunction function,
            TypeScriptFunctionSignature signature)
        {
            string[] parameterNames = _parameterNames[function];
            TypeScriptParameterSignature[] parameters =
            [
                .. signature.Parameters.Select(
                    (parameter, index) =>
                        parameter with { Name = parameterNames[index] }),
            ];
            return signature with
            {
                Name = _operationNames[function],
                Parameters = parameters,
            };
        }

        static string Allocate(
            HashSet<string> bindings,
            string preferred,
            string fallbackPrefix,
            string identity,
            Func<string, bool> isValid)
        {
            if (isValid(preferred) && bindings.Add(preferred))
                return preferred;

            string digest = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
                .ToLowerInvariant();
            for (int length = 8; length <= digest.Length; length += 4)
            {
                string candidate = $"{fallbackPrefix}_{digest[..length]}";
                if (bindings.Add(candidate))
                    return candidate;
            }

            for (long suffix = 2; suffix <= (long)bindings.Count + 2; suffix++)
            {
                string candidate = $"{fallbackPrefix}_{digest}_{suffix}";
                if (bindings.Add(candidate))
                    return candidate;
            }

            throw new UnreachableException();
        }

        static string CanonicalTypeIdentity(ApiType type) =>
            type.FullName
            + "|"
            + (type.MetadataName ?? "")
            + "|"
            + (type.DefinitionName?.ToString() ?? "")
            + "|"
            + type.Kind;
    }
}
