using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

static class ReturnToSenderSourceProbe
{
    sealed record ProbeTarget(ReturnToSender.RequestedTarget Target, IReadOnlyList<string> ExpectedFragments);

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        int attempted = 0, passed = 0, failed = 0, skipped = 0;
        var buckets = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var examples = new List<string>();

        foreach (var assemblyPath in assemblies)
        {
            if (attempted >= cap)
                break;

            var targets = DiscoverTargets(assemblyPath, cap - attempted);
            if (targets.Count == 0)
                continue;

            var results = ReturnToSender.CompileBackTargets(
                    assemblyPath,
                    targets.Select(target => target.Target).Distinct().ToArray())
                .ToDictionary(
                    result => Key(
                        result.Plan.TargetMethod.Type,
                        result.Plan.TargetMethod.Method,
                        result.Plan.TargetMethod.Overload),
                    StringComparer.Ordinal);

            foreach (var target in targets)
            {
                attempted++;
                if (!results.TryGetValue(Key(target.Target.Type, target.Target.Method, target.Target.Overload), out var result))
                {
                    skipped++;
                    AddBucket("unsupported-rts-target");
                    AddExample(target, "Skipped", "unsupported-rts-target", null);
                    continue;
                }

                if (result.Status != FidelityCheck.CompileBackStatus.Exact)
                {
                    failed++;
                    var bucket = result.Status == FidelityCheck.CompileBackStatus.RecompileFail
                        ? DiagnosticCode(result.Detail)
                        : result.Status.ToString();
                    AddBucket(bucket);
                    AddExample(target, result.Status.ToString(), bucket, result.Detail);
                    continue;
                }

                var missing = target.ExpectedFragments.FirstOrDefault(fragment => !result.Source.Contains(fragment, StringComparison.Ordinal));
                if (missing is not null)
                {
                    failed++;
                    AddBucket("source-fragment-missing");
                    AddExample(target, "Exact", "source-fragment-missing", $"missing expected source fragment: {missing}");
                    continue;
                }

                passed++;
            }
        }

        Console.WriteLine($"RETURNTOSENDER SOURCE PROBE over {attempted} target(s)");
        Console.WriteLine();
        Console.WriteLine($"  Passed : {passed}");
        Console.WriteLine($"  Failed : {failed}");
        Console.WriteLine($"  Skipped: {skipped}");
        if (buckets.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Buckets:");
            foreach (var bucket in buckets.OrderByDescending(bucket => bucket.Value).ThenBy(bucket => bucket.Key, StringComparer.Ordinal))
                Console.WriteLine($"  {bucket.Value}: {bucket.Key}");
        }
        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Examples (first {examples.Count}):");
            foreach (var example in examples)
                Console.WriteLine(example);
        }

        return failed == 0 ? 0 : 1;

        void AddBucket(string bucket)
            => buckets[bucket] = buckets.GetValueOrDefault(bucket) + 1;

        void AddExample(ProbeTarget target, string status, string bucket, string? detail)
        {
            if (examples.Count >= maxExamples)
                return;

            examples.Add(detail is null
                ? $"  {target.Target.Type}::{target.Target.Method}#{target.Target.Overload}  rts={status}  bucket={bucket}"
                : $"  {target.Target.Type}::{target.Target.Method}#{target.Target.Overload}  rts={status}  bucket={bucket}\n      detail: {detail}");
        }
    }

    static IReadOnlyList<ProbeTarget> DiscoverTargets(string assemblyPath, int cap)
    {
        var targets = new List<ProbeTarget>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return targets;

        var reader = pe.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (targets.Count >= cap)
                break;

            var type = reader.GetTypeDefinition(typeHandle);
            var typeNamespace = reader.GetString(type.Namespace);
            if (!type.GetDeclaringType().IsNil
                || !type.IsPublic
                || typeNamespace == "System"
                || typeNamespace.StartsWith("System.", StringComparison.Ordinal)
                || AttributeReader.HasAttribute(reader, type.GetCustomAttributes(), "System.CodeDom.Compiler.GeneratedCodeAttribute")
                || AttributeReader.HasAttribute(reader, type.GetCustomAttributes(), "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
                || reader.GetString(type.Name) == "<Module>"
                || reader.GetString(type.Name).Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            var fullType = reader.GetFullTypeName(type);
            var typeFragments = AttributeReader.RenderAttributes(reader, type.GetCustomAttributes(), qualifyNames: true)
                .Select(attribute => $"[{attribute}]")
                .ToArray();

            var methodOverloads = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var methodHandle in type.GetMethods())
            {
                if (targets.Count >= cap)
                    break;

                var method = reader.GetMethodDefinition(methodHandle);
                var methodName = reader.GetString(method.Name);
                var overload = methodOverloads.GetValueOrDefault(methodName);
                methodOverloads[methodName] = overload + 1;

                if (method.RelativeVirtualAddress == 0
                    || (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                    || methodName is ".ctor" or ".cctor"
                    || methodName.StartsWith("get_", StringComparison.Ordinal)
                    || methodName.StartsWith("set_", StringComparison.Ordinal)
                    || methodName.StartsWith("add_", StringComparison.Ordinal)
                    || methodName.StartsWith("remove_", StringComparison.Ordinal)
                    || methodName.Contains('<', StringComparison.Ordinal))
                {
                    continue;
                }

                var fragments = new List<string>();
                fragments.AddRange(typeFragments);
                fragments.AddRange(AttributeReader.RenderAttributes(reader, method.GetCustomAttributes(), qualifyNames: true)
                    .Select(attribute => $"[{attribute}]"));
                AddReturnAndParameterFragments(reader, method.GetParameters(), fragments);
                if (fragments.Count == 0)
                    continue;

                targets.Add(new ProbeTarget(new ReturnToSender.RequestedTarget(fullType, methodName, overload), fragments.Distinct(StringComparer.Ordinal).ToArray()));
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                if (targets.Count >= cap)
                    break;

                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (accessors.Getter.IsNil)
                    continue;

                var getter = reader.GetMethodDefinition(accessors.Getter);
                if (getter.RelativeVirtualAddress == 0
                    || (getter.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }

                var methodName = reader.GetString(getter.Name);
                var overload = OverloadIndex(reader, type, accessors.Getter, methodName);
                var fragments = new List<string>();
                fragments.AddRange(typeFragments);
                fragments.AddRange(AttributeReader.RenderAttributes(reader, property.GetCustomAttributes(), qualifyNames: true)
                    .Select(attribute => $"[{attribute}]"));
                AddReturnAndParameterFragments(reader, getter.GetParameters(), fragments);
                if (fragments.Count == 0)
                    continue;

                targets.Add(new ProbeTarget(new ReturnToSender.RequestedTarget(fullType, methodName, overload), fragments.Distinct(StringComparer.Ordinal).ToArray()));
            }
        }

        return targets;
    }

    static void AddReturnAndParameterFragments(MetadataReader reader, ParameterHandleCollection parameters, List<string> fragments)
    {
        foreach (var parameterHandle in parameters)
        {
            var parameter = reader.GetParameter(parameterHandle);
            var attributes = AttributeReader.RenderParameterAttributes(reader, parameterHandle)
                .Select(attribute => parameter.SequenceNumber == 0 ? $"[return: {attribute}]" : $"[{attribute}]");
            fragments.AddRange(attributes);
        }
    }

    static int OverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string methodName)
    {
        int overload = 0;
        foreach (var handle in typeDef.GetMethods())
        {
            if (handle == target)
                return overload;
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName)
                overload++;
        }
        return overload;
    }

    static string Key(string type, string method, int overload) => $"{type}::{method}#{overload}";

    static string DiagnosticCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "recompile-fail";
        var match = System.Text.RegularExpressions.Regex.Match(detail, @"CS\d{4}");
        return match.Success ? match.Value : "recompile-fail";
    }
}
