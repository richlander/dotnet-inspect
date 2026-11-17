using System.Reflection;
using System.Runtime.Loader;

var path = Path.GetFullPath("package/lib/net10.0/System.Text.Json.dll");
var context = new AssemblyLoadContext("Package", isCollectible: true);
var assembly = context.LoadFromAssemblyPath(path);
var types = assembly.GetExportedTypes();
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
var nullability = new NullabilityInfoContext();

string TypeName(Type type, NullabilityInfo? info = null)
{
    if (type.IsByRef) return TypeName(type.GetElementType()!, info?.ElementType);
    if (type.IsArray) return TypeName(type.GetElementType()!, info?.ElementType) + "[]";
    var primitives = new Dictionary<Type, string>
    {
        [typeof(void)] = "void", [typeof(object)] = "object", [typeof(string)] = "string",
        [typeof(bool)] = "bool", [typeof(int)] = "int", [typeof(byte)] = "byte",
        [typeof(char)] = "char", [typeof(long)] = "long"
    };
    var name = primitives.GetValueOrDefault(type);
    if (name == null)
    {
        name = type.IsGenericParameter ? type.Name : type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName!.Split('`')[0].Replace('+', '.')
            : type.FullName?.Replace('+', '.') ?? type.Name;
        if (type.IsGenericType)
            name += "<" + string.Join(", ", type.GetGenericArguments().Select((t, i) =>
                TypeName(t, info?.GenericTypeArguments.ElementAtOrDefault(i)))) + ">";
    }
    if (info?.ReadState == NullabilityState.Nullable && !type.IsValueType) name += "?";
    return name;
}

string Signature(MethodBase method)
{
    var visibility = method.IsPublic ? "public" : method.IsFamily ? "protected" :
        method.IsFamilyOrAssembly ? "protected internal" : method.IsAssembly ? "internal" :
        method.IsFamilyAndAssembly ? "private protected" : "private";
    var modifiers = method.IsStatic ? " static" : method.IsAbstract ? " abstract" : method.IsVirtual ? " virtual" : "";
    var returnType = method is MethodInfo m ? TypeName(m.ReturnType, nullability.Create(m.ReturnParameter)) + " " : "";
    var name = method.IsConstructor ? method.DeclaringType!.Name : method.Name;
    if (method.IsGenericMethod)
        name += "<" + string.Join(", ", method.GetGenericArguments().Select(t => t.Name)) + ">";
    var args = string.Join(", ", method.GetParameters().Select(p =>
    {
        var prefix = p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "";
        var formatted = prefix + TypeName(p.ParameterType, nullability.Create(p)) + " " + p.Name;
        if (p.IsOptional)
        {
            var value = p.DefaultValue;
            formatted += " = " + (value == null ? (p.ParameterType.IsValueType ? "default" : "null") : value is bool b ? (b ? "true" : "false") :
                value is string s ? "\"" + s + "\"" : value is CancellationToken ? "default" :
                value is Enum ? TypeName(value.GetType()) + "." + value : value.ToString());
        }
        return formatted;
    }));
    return visibility + modifiers + " " + returnType + name + "(" + args + ")";
}

bool IsPublicOrProtected(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;
bool IsRuc(MemberInfo m) => m.CustomAttributes.Any(a => a.AttributeType.FullName ==
    "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute");
IEnumerable<MethodBase> Methods(Type t) =>
    t.GetMethods(flags).Cast<MethodBase>().Concat(t.GetConstructors(flags));

Console.WriteLine("ASSEMBLY " + assembly.FullName + " " + assembly.Location);
Console.WriteLine("TYPES " + types.Length);
Console.WriteLine("\n=== STREAM PUBLIC/PROTECTED ===");
foreach (var m in types.SelectMany(Methods).Where(IsPublicOrProtected).Where(m =>
             m.GetParameters().Any(p => p.ParameterType.FullName == "System.IO.Stream"))
         .OrderBy(m => m.DeclaringType!.FullName).ThenBy(m => m.Name).ThenBy(Signature))
    Console.WriteLine(m.DeclaringType!.FullName + " :: " + Signature(m));

Console.WriteLine("\n=== JSONDOCUMENT RESULTS PUBLIC/PROTECTED ===");
foreach (var m in types.SelectMany(Methods).Where(IsPublicOrProtected).Where(m =>
{
    if (m.GetParameters().Any(p => p.IsOut && p.ParameterType.GetElementType()?.FullName == "System.Text.Json.JsonDocument"))
        return true;
    if (m is not MethodInfo method) return false;
    var result = method.ReturnType;
    return result.FullName == "System.Text.Json.JsonDocument" ||
        (result.IsGenericType && (result.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1" ||
                                  result.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.ValueTask`1") &&
         result.GetGenericArguments()[0].FullName == "System.Text.Json.JsonDocument");
}).OrderBy(m => m.DeclaringType!.FullName).ThenBy(m => m.Name).ThenBy(Signature))
    Console.WriteLine(m.DeclaringType!.FullName + " :: " + Signature(m));

Console.WriteLine("\n=== RUC ALL DECLARED METHODS (including internal/private) ===");
var all = assembly.GetTypes();
foreach (var group in all.SelectMany(Methods).Where(IsRuc).GroupBy(m => (m.DeclaringType!.FullName, m.Name))
             .OrderBy(g => g.Key.FullName).ThenBy(g => g.Key.Name))
{
    var method = group.First();
    var unmarked = Methods(method.DeclaringType!).Where(m => m.Name == method.Name && !IsRuc(m)).ToArray();
    Console.WriteLine($"{group.Key.FullName} :: {group.Key.Name} count={group.Count()} same-named-unmarked={unmarked.Length}");
    foreach (var m in group.OrderBy(Signature)) Console.WriteLine("  + " + Signature(m));
    foreach (var m in unmarked.OrderBy(Signature)) Console.WriteLine("  - " + Signature(m));
}
Console.WriteLine("\n=== RUC TYPES ===");
foreach (var t in all.Where(IsRuc)) Console.WriteLine(t.FullName);

Console.WriteLine("\n=== RUC PUBLIC/PROTECTED GROUPS ===");
foreach (var g in types.SelectMany(Methods).Where(m => IsPublicOrProtected(m) && IsRuc(m))
             .GroupBy(m => (m.DeclaringType!.FullName, m.Name))
             .OrderBy(g => g.Key.FullName).ThenBy(g => g.Key.Name))
{
    var same = Methods(g.First().DeclaringType!).Any(m => IsPublicOrProtected(m) && m.Name == g.Key.Name && !IsRuc(m));
    Console.WriteLine($"{g.Key.FullName} :: {g.Key.Name} count={g.Count()} unmarked-public-protected-overload={same}");
}
Console.WriteLine("\n=== RUC PUBLIC/PROTECTED PROPERTIES ===");
foreach (var t in types)
    foreach (var p in t.GetProperties(flags).Where(p => IsRuc(p)))
        Console.WriteLine(t.FullName + " :: " + p.Name);

Console.WriteLine("\n=== CONVERTER TYPES PUBLIC ===");
foreach (var t in types.Where(t => t.Name.Contains("Converter")).OrderBy(t => t.FullName))
{
    Console.WriteLine($"{t.FullName} abstract={t.IsAbstract} sealed={t.IsSealed} base={t.BaseType?.FullName}");
    Console.WriteLine("  constructors: " + string.Join("; ", t.GetConstructors(flags).Where(IsPublicOrProtected).Select(Signature)));
    if (t.IsSealed || (!t.IsPublic && !t.IsNestedPublic)) continue;
    for (var baseType = t; baseType != null && baseType != typeof(object); baseType = baseType.BaseType)
        foreach (var m in baseType.GetMethods(flags).Where(m => m.IsAbstract && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly)))
            Console.WriteLine("  abstract " + m.DeclaringType?.FullName + " :: " + Signature(m));
}
