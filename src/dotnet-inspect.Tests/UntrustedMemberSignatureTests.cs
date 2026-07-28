using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using ILInspector.CSharp;
using ILInspector.Metadata;

namespace DotnetInspector.Tests;

/// <summary>
/// Gates for issue #3319 on the member signature channel. A field, property, or
/// event name is untrusted metadata, and it is rendered into a signature cell of
/// a Markdown table. A hostile name must not be able to break that cell or move
/// the terminal cursor once the table is rendered.
/// </summary>
/// <remarks>
/// The first fix for #3319 covered the C# declaration and body producers but left
/// this channel keyword-escaped only; an adversarial reviewer found it. The
/// rendered evidence before the fix was a signature cell reading
/// <c>public int N&lt;VT&gt;    INJECTED</c> with a live vertical tab in it.
/// </remarks>
public class UntrustedMemberSignatureTests
{
    static readonly CSharpFormatter Formatter = new();

    // NUL is deliberately absent: metadata names live in the NUL-terminated
    // #Strings heap, so a name cannot carry one and the fixture builder truncates
    // it. The primitive still treats it as a hazard.
    public static TheoryData<string> Hazards => new()
    {
        "\n", "\r\n", "\u2028", "\v", "\u001b[31m", "\u202e",
    };

    [Theory]
    [MemberData(nameof(Hazards))]
    public void FieldPropertyAndEventSignatures_ContainHostileNames(string hazard)
    {
        string hostile = $"N{hazard}    INJECTED";
        string dir = Path.Combine(Path.GetTempPath(), $"dotnet-inspect-sig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var assemblyName = new AssemblyName("HostileSignature");
            var ab = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            var mb = ab.DefineDynamicModule(assemblyName.Name!);
            var tb = mb.DefineType("Hostile.Bag", TypeAttributes.Public | TypeAttributes.Class);

            tb.DefineField(hostile, typeof(int), FieldAttributes.Public);

            var backing = tb.DefineField("_p", typeof(int), FieldAttributes.Private);
            var method = tb.DefineMethod(
                hostile,
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(int),
                [typeof(int)]);
            method.GetILGenerator().Emit(OpCodes.Ret);
            var property = tb.DefineProperty(hostile, PropertyAttributes.None, typeof(int), null);
            var getter = tb.DefineMethod(
                "get_" + hostile,
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(int),
                Type.EmptyTypes);
            var il = getter.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, backing);
            il.Emit(OpCodes.Ret);
            property.SetGetMethod(getter);

            var @event = tb.DefineEvent(hostile, EventAttributes.None, typeof(EventHandler));
            var adder = tb.DefineMethod(
                "add_" + hostile,
                MethodAttributes.Public | MethodAttributes.SpecialName,
                typeof(void),
                [typeof(EventHandler)]);
            adder.GetILGenerator().Emit(OpCodes.Ret);
            @event.SetAddOnMethod(adder);

            tb.CreateType();

            string dllPath = Path.Combine(dir, "HostileSignature.dll");
            ab.Save(dllPath);

            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            var surface = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
            var type = Assert.Single(surface.Types, t => t.Name == "Bag");

            var kinds = type.Members
                .Where(m => m.Kind is "field" or "property" or "event" or "method" && m.Name.Contains("INJECTED", StringComparison.Ordinal))
                .ToList();

            // Non-vacuity: all four hostile member kinds really were extracted,
            // so a silently dropped kind cannot make this pass.
            Assert.Equal(
                new[] { "event", "field", "method", "property" },
                kinds.Select(m => m.Kind).OrderBy(k => k, StringComparer.Ordinal).ToArray());

            foreach (var member in kinds)
            {
                // The signature the extractor itself produces, which the type
                // tree renders directly without going through the formatter.
                if (member.Signature is { Length: > 0 } extracted)
                    AssertContained(extracted);

                // The signature cell, which is also what the decompiled and
                // annotated source blocks render.
                AssertContained(Formatter.FormatMember(type, member));

                // The Name column and the "# Type.Member" heading.
                AssertContained(OperatorNames.FormatDisplayName(member.Name));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// The harness owns this predicate rather than calling
    /// <see cref="CSharpIdentifier.IsRenderingHazard"/>: a gate that asks the
    /// product what counts as dangerous cannot fail when the product's answer is
    /// wrong, which is the case it exists to catch.
    /// </summary>
    static void AssertContained(string signature)
    {
        Assert.DoesNotContain(
            signature,
            c => c != '\t' && (char.IsControl(c) || IsBidiControl(c)));

        static bool IsBidiControl(char ch)
            => ch is '\u061C' or '\u200E' or '\u200F'
                or >= '\u202A' and <= '\u202E'
                or >= '\u2066' and <= '\u2069';
    }
}

/// <summary>
/// A whole-view gate for issue #3319. The per-channel gates only cover channels
/// someone thought to name, and three separate adversarial passes each found a
/// channel the previous fix had missed. This walks the built view object graph
/// reflectively instead, so a row type nobody has thought of yet is still
/// covered the day it is added.
/// </summary>
public class UntrustedViewContainmentTests
{
    [Fact]
    public void NoBuiltViewCarriesARenderingHazard()
    {
        const string Hazard = "\v";
        var type = new ApiType
        {
            Name = $"Holder{Hazard}INJECTED",
            Namespace = $"Ns{Hazard}INJECTED",
            Kind = "class",
            BaseType = $"Base{Hazard}INJECTED",
            Interfaces = [$"IFace{Hazard}INJECTED"],
            TypeParameters = [new TypeParameter { Name = $"T{Hazard}INJECTED" }],
            Members =
            [
                new ApiMember { Name = $"Fld{Hazard}INJECTED", Kind = "field", ReturnType = $"Ret{Hazard}INJECTED" },
                new ApiMember { Name = $"Prop{Hazard}INJECTED", Kind = "property", ReturnType = "int" },
                new ApiMember { Name = $"Evt{Hazard}INJECTED", Kind = "event", ReturnType = "EventHandler" },
                new ApiMember
                {
                    Name = $"Meth{Hazard}INJECTED",
                    Kind = "method",
                    ReturnType = $"Ret{Hazard}INJECTED",
                    Signature = $"Ret{Hazard}INJECTED Meth{Hazard}INJECTED(Arg{Hazard}INJECTED a)",
                },
                new ApiMember { Name = ".ctor", Kind = "constructor" },
            ]
        };

        var enumType = new ApiType
        {
            Name = "HostileEnum",
            Kind = "enum",
            Members =
            [
                new ApiMember { Name = $"Val{Hazard}INJECTED", Kind = "field", ReturnType = "int", EnumValue = 1 },
            ]
        };

        var summaryView = new TypeView();
        ApiOutputFormatter.PopulateMemberSummarySections(
            summaryView, new MethodGroupsView(), new EventsView(), type, new ApiOptions());

        var enumView = new TypeView();
        ApiOutputFormatter.PopulateEnumValues(enumView, enumType, new ApiOptions());

        var shapeView = ApiOutputFormatter.BuildShapeView(
            type, foundIn: null, packageName: null, packageVersion: null, memberFilter: []);

        var (tableView, _) = ApiOutputFormatter.BuildTypeTableView(type, new ApiOptions());

        // Non-vacuity: the hostile names must actually be present in the views,
        // or a view that dropped every member would pass trivially.
        int seen = 0;
        foreach (var view in new object[] { summaryView, enumView, shapeView, tableView })
        {
            foreach (string text in Strings(view, new HashSet<object>(ReferenceEqualityComparer.Instance)))
            {
                if (text.Contains("INJECTED", StringComparison.Ordinal))
                    seen++;
                Assert.DoesNotContain(text, IsHazard);
            }
        }

        Assert.True(seen >= 5, $"expected the hostile names to reach the views, saw {seen}");
    }

    /// <summary>
    /// The harness spells the hazard set out rather than calling
    /// <see cref="CSharpIdentifier.IsRenderingHazard"/>, so that a wrong answer
    /// from the product cannot make this gate agree with it.
    /// </summary>
    static bool IsHazard(char c)
        => c != '\t'
            && (char.IsControl(c)
                || c is '\u061C' or '\u200E' or '\u200F'
                    or >= '\u202A' and <= '\u202E'
                    or >= '\u2066' and <= '\u2069');

    /// <summary>Every string reachable from a built view, however nested.</summary>
    static IEnumerable<string> Strings(object? node, HashSet<object> seen)
    {
        if (node is null || !seen.Add(node))
            yield break;

        if (node is string s)
        {
            yield return s;
            yield break;
        }

        if (node is System.Collections.IEnumerable list)
        {
            foreach (var item in list)
                foreach (string text in Strings(item, seen))
                    yield return text;
            yield break;
        }

        var type = node.GetType();

        // KeyValuePair, ValueTuple, and Tuple live under System, so a dictionary's
        // entries or a tuple's items would otherwise be skipped by the namespace
        // bail below. Walk their public members explicitly.
        bool isTupleLike = type.IsGenericType
            && type.GetGenericTypeDefinition() is { } definition
            && (definition == typeof(KeyValuePair<,>)
                || definition.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true
                || definition.FullName?.StartsWith("System.Tuple`", StringComparison.Ordinal) == true);

        if (isTupleLike)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                object? entry;
                try { entry = property.GetValue(node); }
                catch { continue; }
                foreach (string text in Strings(entry, seen))
                    yield return text;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object? entry;
                try { entry = field.GetValue(node); }
                catch { continue; }
                foreach (string text in Strings(entry, seen))
                    yield return text;
            }

            yield break;
        }

        if (type.IsPrimitive || type.IsEnum || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true)
            yield break;

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
                continue;
            object? value;
            try { value = property.GetValue(node); }
            catch { continue; }
            foreach (string text in Strings(value, seen))
                yield return text;
        }

        // Public fields are rendered too, and are not reached by the property walk.
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? value;
            try { value = field.GetValue(node); }
            catch { continue; }
            foreach (string text in Strings(value, seen))
                yield return text;
        }
    }
}

/// <summary>
/// Gate for the diff channel (issue #3319). `ApiChange` messages embed untrusted
/// type and member names, and the diff renderer prints them into Markdown
/// headings, bullet lists, and table cells.
/// </summary>
public class UntrustedDiffContainmentTests
{
    [Theory]
    [InlineData("\v")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\u001b[31m")]
    [InlineData("\u202e")]
    public void ApiChangeText_IsContained(string hazard)
    {
        var change = new ApiChange(
            ChangeKind.MemberAdded,
            ChangeClassification.Additive,
            $"Member 'M{hazard}INJECTED' was added",
            OldValue: $"void M{hazard}INJECTED()",
            NewValue: $"int M{hazard}INJECTED()");

        // Non-vacuity: the name must still be there, contained rather than dropped.
        Assert.Contains("INJECTED", change.Message, StringComparison.Ordinal);

        foreach (string text in new[] { change.Message, change.OldValue!, change.NewValue! })
            Assert.DoesNotContain(text, c => c != '\t' && char.IsControl(c));
    }
}
