---
id: il-coordinate-artifacts
description: Explain sparse IL coordinates from debugger, profiler, and analyzer-style artifacts
commands: [library]
areas: [debugging, analysis, il-offset, workflow]
---

# Debugging: IL Coordinate Artifacts

> Prototype workflows for an agent that receives runtime or analyzer artifacts
> containing sparse method-token + IL-offset coordinates. The agent performs the
> "sneakernet" normalization step, then asks `dotnet-inspect` to explain the
> coordinates.

## Preconditions

Build the CLI and test assembly used by the demo scenarios.

```bash
dotnet build src/dotnet-inspect -c Release -p:PublishAot=false
dotnet build src/dotnet-inspect.Tests -c Release -p:PublishAot=false
```

Generate three coordinate artifact files from the test assembly:

- `debugger.coords`: a callsite and its return address, like a debugger or dump
  stack frame might provide.
- `profiler.coords`: hot call and allocation coordinates, like a profiler or
  trace postprocessor might provide.
- `analyzer.coords`: safety/allocation coordinates plus a malformed line, like a
  static analyzer or CI artifact might provide.

```bash
mkdir -p /tmp/dotnet-inspect-coordinate-demo
cat > /tmp/dotnet-inspect-coordinate-demo/create.cs <<EOF
using System.Reflection;

var assemblyPath = Path.GetFullPath("artifacts/bin/dotnet-inspect.Tests/release/dotnet-inspect.Tests.dll");
var assembly = Assembly.LoadFile(assemblyPath);

MethodInfo Method(string typeName, string methodName)
{
    var type = assembly.GetType(typeName) ?? throw new InvalidOperationException(typeName);
    return type.GetMethod(
        methodName,
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        ?? throw new InvalidOperationException($"{typeName}.{methodName}");
}

int FindOpcode(MethodInfo method, byte opcode)
{
    var il = method.GetMethodBody()?.GetILAsByteArray() ?? throw new InvalidOperationException(method.Name);
    for (var i = 0; i < il.Length; i++)
    {
        if (il[i] == opcode)
            return i;
    }
    throw new InvalidOperationException($"opcode 0x{opcode:X2} not found in {method.Name}");
}

var memberCalls = Method("DotnetInspector.Tests.MemberCallsFixture", "CallsInterfaceItem");
var memberCallsToken = memberCalls.MetadataToken;
var interfaceCallOffset = FindOpcode(memberCalls, 0x6F); // callvirt
var interfaceReturnAddress = interfaceCallOffset + 5;

var semantic = Method("DotnetInspector.Tests.SemanticFactsFixture", "AllSignals");
var semanticToken = semantic.MetadataToken;
var allocationOffset = FindOpcode(semantic, 0x8D); // newarr
var virtualCallOffset = FindOpcode(semantic, 0x6F); // callvirt

var unsafeMethod = Method("DotnetInspector.Tests.SemanticFactsFixture", "UnsafeAs");
var unsafeToken = unsafeMethod.MetadataToken;
var unsafeCallOffset = FindOpcode(unsafeMethod, 0x28); // call

string C(int token, int offset) => $"0x{token:X8}+0x{offset:X}";

File.WriteAllLines("/tmp/dotnet-inspect-coordinate-demo/debugger.coords",
[
    "# debugger/dump-style normalized frames",
    $"caller-frame {C(memberCallsToken, interfaceCallOffset)}",
    $"return-address {C(memberCallsToken, interfaceReturnAddress)}",
]);

File.WriteAllLines("/tmp/dotnet-inspect-coordinate-demo/profiler.coords",
[
    "# profiler/trace-style hot sparse samples",
    $"hot-virtual-call {C(semanticToken, virtualCallOffset)}",
    $"alloc-sample {C(semanticToken, allocationOffset)}",
]);

File.WriteAllLines("/tmp/dotnet-inspect-coordinate-demo/analyzer.coords",
[
    "# analyzer/CI-style suspicious coordinates",
    $"unsafe-call {C(unsafeToken, unsafeCallOffset)}",
    $"allocation {C(semanticToken, allocationOffset)}",
    "unparsed analyzer line",
]);
EOF
dotnet run /tmp/dotnet-inspect-coordinate-demo/create.cs
```

## 1. Explain debugger or dump frames

> Goal: Given a sparse frame artifact with a callsite and return address, explain
> where the caller is and what call it corresponds to.

```prompt
Explain these debugger IL coordinates from my crash dump.
```

```bash
dotnet run --project src/dotnet-inspect -c Release -- \
  library artifacts/bin/dotnet-inspect.Tests/release/dotnet-inspect.Tests.dll \
  --il-offsets /tmp/dotnet-inspect-coordinate-demo/debugger.coords \
  --markdown --tips q
```

```expect
## IL Coordinates
caller-frame
callsite
return-address
return address
```

## 2. Collect a crash dump, normalize frames, and explain them

> Goal: Show the end-to-end agent workflow: run an app, collect a crash dump,
> use a debugger-style tool to extract method/offset data, normalize it into the
> neutral coordinate file, and then ask `dotnet-inspect` to explain the static
> context.

This scenario intentionally leaves the dump-symbolication command as a
producer-specific step. The important workflow contract is the handoff from a
debugger/SOS-like artifact to the neutral coordinate file.

```prompt
I have a crash dump. Normalize the stack frame IL offsets and explain them with dotnet-inspect.
```

Create a small app that throws after going through a callsite we can explain:

```bash
mkdir -p /tmp/dotnet-inspect-coordinate-demo/CrashApp
cat > /tmp/dotnet-inspect-coordinate-demo/CrashApp/CrashApp.csproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
EOF
cat > /tmp/dotnet-inspect-coordinate-demo/CrashApp/Program.cs <<'EOF'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

class Program
{
    public static void Main()
    {
        try
        {
            Entry();
        }
        catch
        {
            EmitCoordinates();
            throw;
        }
    }

    public static void Entry() => Caller();

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Caller() => CrashTarget(42);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void CrashTarget(int value)
    {
        var values = new int[value];
        throw new InvalidOperationException(values.Length.ToString());
    }

    static void EmitCoordinates()
    {
        var caller = typeof(Program).GetMethod(
            "Caller",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var crashTarget = typeof(Program).GetMethod(
            "CrashTarget",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var callOffset = FindOpcode(caller, 0x28); // call
        var returnAddress = callOffset + 5;
        var allocationOffset = FindOpcode(crashTarget, 0x8D); // newarr

        File.WriteAllLines("/tmp/dotnet-inspect-coordinate-demo/crash.coords",
        [
            "# normalized from crash stack / dump frames",
            $"caller-callsite 0x{caller.MetadataToken:X8}+0x{callOffset:X}",
            $"caller-return-address 0x{caller.MetadataToken:X8}+0x{returnAddress:X}",
            $"crash-allocation 0x{crashTarget.MetadataToken:X8}+0x{allocationOffset:X}",
        ]);
    }

    static int FindOpcode(MethodInfo method, byte opcode)
    {
        var il = method.GetMethodBody()!.GetILAsByteArray()!;
        for (var i = 0; i < il.Length; i++)
        {
            if (il[i] == opcode)
                return i;
        }
        throw new InvalidOperationException($"opcode 0x{opcode:X2} not found in {method.Name}");
    }
}
EOF
dotnet build /tmp/dotnet-inspect-coordinate-demo/CrashApp -c Release
```

Run the app and collect a dump. This uses `dotnet-dump` if available; the app
also writes `crash.coords` so the workflow remains executable without requiring
dump tooling in CI:

```bash
set +e
dotnet /tmp/dotnet-inspect-coordinate-demo/CrashApp/bin/Release/net10.0/CrashApp.dll
app_exit=$?
set -e
test "$app_exit" -ne 0

# Optional real collection step when a process is still alive or a dump is needed:
# dotnet-dump collect --process-id <pid> --output /tmp/dotnet-inspect-coordinate-demo/crash.dmp
```

In a real dump workflow, the agent would run a debugger/SOS command here and
normalize the frames:

```text
# Example shape of external debugger output (tool-specific, not parsed by dotnet-inspect):
#   Program.Caller() + IL_0000
#   Program.CrashTarget(int) + IL_0004
#
# Agent normalization result:
#   caller-callsite 0x06000002+0x0
#   crash-allocation 0x06000003+0x4
```

Then run `dotnet-inspect` on the normalized coordinate file:

```bash
dotnet run --project src/dotnet-inspect -c Release -- \
  library /tmp/dotnet-inspect-coordinate-demo/CrashApp/bin/Release/net10.0/CrashApp.dll \
  --il-offsets /tmp/dotnet-inspect-coordinate-demo/crash.coords \
  --markdown --tips q
```

```expect
caller-callsite
callsite
caller-return-address
return address
crash-allocation
allocation
```

## 3. Explain profiler or trace samples

> Goal: Given sparse profiler coordinates, separate objective code shapes such
> as dispatch and allocation without running a ranked triage.

```prompt
Explain these profiler sample coordinates without doing a full triage.
```

```bash
dotnet run --project src/dotnet-inspect -c Release -- \
  library artifacts/bin/dotnet-inspect.Tests/release/dotnet-inspect.Tests.dll \
  --il-offsets /tmp/dotnet-inspect-coordinate-demo/profiler.coords \
  --markdown --tips q
```

```expect
hot-virtual-call
virtual dispatch
alloc-sample
allocation
```

```expect-not
Performance Triage
```

## 4. Explain analyzer or CI artifact coordinates

> Goal: Given a mixed artifact, preserve malformed rows as visible errors while
> still explaining coordinates that can be normalized.

```prompt
Explain this analyzer artifact and keep bad lines visible.
```

```bash
dotnet run --project src/dotnet-inspect -c Release -- \
  library artifacts/bin/dotnet-inspect.Tests/release/dotnet-inspect.Tests.dll \
  --il-offsets /tmp/dotnet-inspect-coordinate-demo/analyzer.coords \
  --markdown --tips q
```

```expect-error
unsafe-call
safety
allocation
error
expected a MethodDef token + IL offset coordinate
```

## Skill candidate

The eventual debugging skill should focus on the artifact-to-coordinate
normalization step:

1. Identify the producer format (debugger/dump, profiler/trace, analyzer/CI).
2. Extract or symbolize method identity + IL offset.
3. Write the neutral coordinate file.
4. Run `library --il-offsets`.
5. Decide whether the output is enough or whether to drill into individual
   sections with `--il-offset -S "<Context>"`.
