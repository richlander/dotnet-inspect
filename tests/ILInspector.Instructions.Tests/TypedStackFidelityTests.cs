using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Layer 1 fidelity: validate the typed-stack interpreter's depth accounting against an external
/// oracle — the method body's compiler-declared <c>MaxStack</c>. For any method whose typed stack
/// is complete, the deepest evaluation-stack depth the interpreter computes must not exceed the
/// declared bound; exceeding it means a pop/push miscount. Run over all of System.Private.CoreLib.
/// </summary>
public class TypedStackFidelityTests
{
    [Fact]
    public void Computed_stack_depth_never_exceeds_declared_maxstack_over_corelib()
    {
        using var pe = new PEReader(File.OpenRead(typeof(object).Assembly.Location));
        var reader = pe.GetMetadataReader();

        int checked_ = 0;
        var violations = new List<string>();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(method.RelativeVirtualAddress); }
            catch { continue; }
            if ((body.GetILBytes()?.Length ?? 0) == 0)
                continue;

            var mi = MethodInstructions.Decode(body);
            var ts = mi.InterpretStack(reader, handle, body);
            if (!ts.IsComplete)
                continue; // fail-closed methods don't make a depth claim

            checked_++;
            int computedMax = 0;
            foreach (var stack in ts.StackBefore.Values)
                if (stack.Length > computedMax)
                    computedMax = stack.Length;

            // Ceiling sanity bound, by design — not exact verification. computedMax is the deepest
            // *block-entry* stack we model; if it ever exceeds the declared MaxStack the interpreter
            // over-pushed (a real bug). The converse (under-push) is intentionally NOT asserted: we
            // sample block-entry depths rather than the true mid-block peak, and compilers may pad
            // MaxStack, so exact equality would be flaky. Over-push is the falsifiable claim here.
            if (computedMax > body.MaxStack)
                violations.Add($"{reader.GetString(method.Name)}: computed {computedMax} > declared MaxStack {body.MaxStack}");
        }

        Assert.True(checked_ > 5000, $"expected many complete methods, saw {checked_}");
        Assert.True(violations.Count == 0,
            $"{violations.Count} MaxStack violation(s):\n{string.Join("\n", violations.Take(15))}");
    }
}
