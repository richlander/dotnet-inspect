---
id: samples-local-files
description: Read local C# files and extract regions for code snippets
commands: [samples]
areas: [samples, source, regions, local-files]
---

# Samples: Local Files

> The `samples --file` flag reads a local `.cs` file directly, bypassing SourceLink and PDB lookup. Combined with `--region`, it extracts specific `#region` blocks — useful for pulling code snippets from a project, building documentation, or feeding examples to an agent.

## 1. Read a local file

> Goal: Display the full contents of a local C# file.

```prompt
Show me the contents of this C# file.
```

```setup
cat > /tmp/workflow-sample.cs << 'CSHARP'
using System;

public class Example
{
    #region greeting
    public static void SayHello(string name)
    {
        Console.WriteLine($"Hello, {name}!");
    }
    #endregion

    #region math
    public static int Add(int a, int b) => a + b;
    #endregion
}
CSHARP
```

```bash
dotnet-inspect samples --file /tmp/workflow-sample.cs
```

```expect
using System;
public class Example
#region greeting
#region math
```

## 2. Extract a named region

> Goal: Pull out a specific `#region` block, stripping the region markers.

### 2a. Extract greeting region

```prompt
Show me just the greeting code from this file.
```

```bash
dotnet-inspect samples --file /tmp/workflow-sample.cs --region greeting
```

```expect
public static void SayHello(string name)
Console.WriteLine
```

```expect-not
#region
using System;
Add
```

### 2b. Extract math region

```bash
dotnet-inspect samples --file /tmp/workflow-sample.cs --region math
```

```expect
public static int Add(int a, int b) => a + b;
```

```expect-not
#region
SayHello
```

## 3. Line-limited output

> Goal: Truncate file output when only a preview is needed.

```bash
dotnet-inspect samples --file /tmp/workflow-sample.cs -n 5
```

```expect
using System;
public class Example
```

```query
wc -l | tr -d ' '
```
