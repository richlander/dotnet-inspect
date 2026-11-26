using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

var assembly = Assembly.LoadFile(Path.GetFullPath("lib/net10.0/System.Text.Json.dll"));
Console.WriteLine($"ASSEMBLY {assembly.FullName} | {assembly.Location}");
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
var types = assembly.GetTypes().Where(t => t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem).OrderBy(t => t.FullName).ToArray();
var members = types.SelectMany(t => t.GetMembers(flags).OfType<MethodBase>())
    .Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly).ToArray();

string TypeName(Type t)
{
    if (t.IsByRef) return TypeName(t.GetElementType()!);
    if (t.IsArray) return TypeName(t.GetElementType()!) + "[]";
    if (t.IsGenericParameter) return t.Name;
    if (t.IsGenericType)
    {
        var s = t.GetGenericTypeDefinition().FullName!.Split('`')[0].Replace('+', '.');
        return s + "<" + string.Join(", ", t.GetGenericArguments().Select(TypeName)) + ">";
    }
    return (t.FullName ?? t.Name).Replace('+', '.');
}
string Sig(MethodBase m)
{
    var method = m as MethodInfo;
    var prefix = method is null ? "" : TypeName(method.ReturnType) + " ";
    var name = m is ConstructorInfo ? m.DeclaringType!.Name : m.Name;
    if (m.IsGenericMethod) name += "<" + string.Join(", ", m.GetGenericArguments().Select(x => x.Name)) + ">";
    var parameters = string.Join(", ", m.GetParameters().Select(p =>
        (p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "") +
        TypeName(p.ParameterType) + " " + p.Name +
        (p.IsOptional ? " = " + (p.DefaultValue is null ? "null" : p.DefaultValue is Missing ? "default" : p.DefaultValue is bool b ? b.ToString().ToLowerInvariant() : p.DefaultValue) : "")));
    return $"{TypeName(m.DeclaringType!)}.{prefix}{name}({parameters})";
}

Console.WriteLine("=== STREAM ===");
foreach (var m in members.Where(m => m.GetParameters().Any(p => p.ParameterType.FullName == "System.IO.Stream")))
    Console.WriteLine(Sig(m));

Console.WriteLine("=== JSONDOCUMENT ===");
foreach (var m in members.Where(m => m is MethodInfo method &&
    (method.ReturnType.FullName == "System.Text.Json.JsonDocument"
    || method.ReturnType.IsGenericType && method.ReturnType.GetGenericArguments().Any(t => t.FullName == "System.Text.Json.JsonDocument") &&
        (method.ReturnType.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.Task`1" || method.ReturnType.GetGenericTypeDefinition().FullName == "System.Threading.Tasks.ValueTask`1")
    || method.GetParameters().Any(p => p.IsOut && p.ParameterType.GetElementType()?.FullName == "System.Text.Json.JsonDocument"))))
    Console.WriteLine(Sig(m));

Console.WriteLine("=== RUC ===");
foreach (var m in members.Where(m => m.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute")))
{
    var same = members.Any(other => other != m && other.DeclaringType == m.DeclaringType && other.Name == m.Name
        && !other.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"));
    Console.WriteLine($"{Sig(m)} | unmarked-same-name={same}");
}
Console.WriteLine("=== CONVERTER TYPES ===");
foreach (var t in types.Where(t => t.Name.Contains("Converter", StringComparison.Ordinal)))
{
    Console.WriteLine($"{TypeName(t)} | public={t.IsPublic || t.IsNestedPublic} abstract={t.IsAbstract} sealed={t.IsSealed} base={t.BaseType?.FullName}");
    foreach (var c in t.GetConstructors(flags).Where(c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly))
        Console.WriteLine("  ctor: " + Sig(c));
    foreach (var m in t.GetMembers(flags).OfType<MethodInfo>().Where(m => m.IsAbstract))
        Console.WriteLine("  abstract: " + Sig(m));
    foreach (var c in t.GetConstructors(flags))
        Console.WriteLine($"  ctor-access: {c.Attributes & MethodAttributes.MemberAccessMask} {Sig(c)}");
    foreach (var m in t.GetMethods(flags).Where(m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly))
        if (m.Name is "CanConvert" or "CreateConverter" or "Read" or "Write")
            Console.WriteLine($"  extension: abstract={m.IsAbstract} virtual={m.IsVirtual} {Sig(m)}");
}
Console.WriteLine("=== RUC GROUPS ===");
foreach (var group in members.Where(m => m.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))
    .GroupBy(m => (m.DeclaringType, Name: m.Name.StartsWith("get_") ? m.Name[4..] : m.Name)))
{
    bool unmarked = members.Any(m => m.DeclaringType == group.Key.DeclaringType
        && (m.Name.StartsWith("get_") ? m.Name[4..] : m.Name) == group.Key.Name
        && !m.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"));
    Console.WriteLine($"{group.Key.DeclaringType!.FullName}.{group.Key.Name} count={group.Count()} unmarked={unmarked}");
}
Console.WriteLine("=== OTHER RUC TARGETS ===");
foreach (var t in types)
{
    if (t.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))
        Console.WriteLine($"type {t.FullName}");
    foreach (var m in t.GetMembers(flags).Where(m => m.MemberType is MemberTypes.Field or MemberTypes.Property or MemberTypes.Event))
        if (m.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute"))
            Console.WriteLine($"member {t.FullName}.{m.Name}");
}
