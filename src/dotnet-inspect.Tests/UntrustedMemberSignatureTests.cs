using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
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
