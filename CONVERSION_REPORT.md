# System.CommandLine Conversion Report

## Summary
Converted dotnet-inspect from a custom command routing system to System.CommandLine v2.0.2. The conversion was successful with all commands functioning correctly.

**Token usage:** ~93k tokens | **Context window:** 57% used

---

## Comparison: Attempt 1 (web_search) vs Attempt 2 (dotnet inspect)

| Metric | Attempt 1 (web_search) | Attempt 2 (dotnet inspect) |
|--------|------------------------|----------------------------|
| Token usage | ~118k | ~93k |
| Context window | 69% | 57% |
| Build attempts | 3+ | 4 |
| API pattern errors | Major (wrong methods) | Minor (wrong constructors) |
| Time spent on wrong API | High | Low |

### Attempt 1 Problems
Used `web_search` to find System.CommandLine documentation, but **got outdated beta patterns**:
```csharp
// Wrong patterns from web search
rootCommand.AddCommand(packageCommand);  // Doesn't exist in 2.0
command.SetHandler(async (context) => { ... });  // Wrong signature
await rootCommand.InvokeAsync(args);  // Doesn't exist
```

### Attempt 2 Improvements
Used `dotnet inspect api` to query the **actual installed package**:
```csharp
// Correct patterns from dotnet inspect
rootCommand.Add(packageCommand);  // Verified via api Command
command.SetAction(async (parseResult, ct) => { ... });  // Verified via api Command -m SetAction
rootCommand.Parse(args).InvokeAsync();  // Verified via api ParseResult
```

### Winner: Attempt 2 (dotnet inspect)
- **25k fewer tokens** (~21% reduction)
- **12% less context window usage**
- **More accurate API usage** - queried the actual library, not web docs

---

## How `dotnet inspect` Could Be Used Better

### What I Did
```bash
dotnet inspect api Command --package System.CommandLine
dotnet inspect api Option --package System.CommandLine  
dotnet inspect api ParseResult --package System.CommandLine
```
This gave me the correct method signatures, but I still made constructor mistakes.

### What I Should Have Done

**1. Query specific constructors explicitly:**
```bash
dotnet inspect api 'Argument`1' --package System.CommandLine -m .ctor
dotnet inspect api 'Option`1' --package System.CommandLine -m .ctor
```
This would have shown me that `Argument<T>` only takes `name` in the constructor, not description.

**2. Use the type command for full shape:**
```bash
dotnet inspect type Command --package System.CommandLine
```
Shows inheritance, interfaces, and all members in a tree view—better for understanding the full type.

**3. Check the Symbol base class for Description:**
```bash
dotnet inspect api Symbol --package System.CommandLine -m Description
```
Would have revealed that `Description` is a settable property on the base class, not a constructor parameter.

### The Confusion

I was initially confused because:
1. The **globally installed** `dotnet-inspect` tool was already converted to System.CommandLine (showed `type` command in help)
2. The **source code** I was converting still used the old `ICommand` pattern
3. Running `dotnet inspect --help` showed the installed version's features, not the source version

This was actually beneficial—I used the installed tool to learn the API I was converting to. But it caused momentary confusion about the state of the codebase.

### Ideal Workflow for API Discovery

```bash
# 1. List all types in the package
dotnet inspect api --package System.CommandLine

# 2. For each type you need, get full details
dotnet inspect api Command --package System.CommandLine -v:d

# 3. Check constructors specifically
dotnet inspect api 'Command' --package System.CommandLine -m .ctor

# 4. Check inherited members
dotnet inspect type Command --package System.CommandLine
```

This systematic approach would have eliminated the constructor signature errors that cost ~3k tokens to fix.

---

## What Went Well

### 1. Using `dotnet inspect` to Learn the API
The tool's own `dotnet inspect api` command was invaluable for understanding System.CommandLine's API without needing external documentation:
```bash
dotnet inspect api Command --package System.CommandLine
dotnet inspect api Option --package System.CommandLine
dotnet inspect api ParseResult --package System.CommandLine -m GetValue
```
This self-documenting approach was efficient and accurate.

### 2. Incremental Approach
Converting one command at a time and running `dotnet build` after each change caught errors early. The build errors were clear and actionable.

### 3. Preserving Business Logic
The refactoring cleanly separated concerns:
- **Removed:** Manual argument parsing (`ParseOptions()` methods)
- **Kept intact:** All business logic (package downloading, API extraction, assembly inspection)

### 4. Clean Architecture Result
The final structure is cleaner:
- Static classes with `Create()` factory methods
- Declarative option/argument definitions at class level
- Single `SetAction()` handler per command

---

## What Didn't Go Well

### 1. API Assumptions Were Wrong
Initial code used incorrect constructor signatures:
```csharp
// Wrong - assumed description in constructor
new Argument<string>("package", "NuGet package name...")

// Correct - description is a property
new Argument<string>("package") { Description = "NuGet package name..." }
```
**Cost:** Extra build/fix cycle (~2k tokens)

### 2. Option Alias Syntax Error
Used collection expression syntax that didn't work:
```csharp
// Wrong
new Option<bool>(["--preview", "--prerelease"], "...")

// Correct  
new Option<bool>("--preview", "--prerelease") { Description = "..." }
```
**Cost:** Extra debugging (~1k tokens)

### 3. Didn't Delete Obsolete Files First
Left `CommandRouter.cs`, `HelpCommand.cs`, and `ICommand.cs` in place while converting commands, causing build errors about static classes being used as type arguments. Should have deleted them earlier or converted all files before building.

### 4. Variable Naming Collision
Renamed `result` to `inspectionResult` in one place but missed updating `FilterResultForOutput()`, causing a build error.

### 5. Confusion About Installed vs Source Version
Initially confused by different behavior between the globally installed `dotnet-inspect` tool and the source code being converted. Spent time investigating before realizing they were different versions.

---

## Metrics

| Metric | Value |
|--------|-------|
| Files modified | 5 |
| Files deleted | 3 |
| Lines of parsing code removed | ~400 |
| Build attempts | 4 |
| Commands converted | 4 |
| Total time (wall clock) | ~11 minutes |

---

## Recommendations for Future Conversions

1. **Check API signatures first** - Use `dotnet inspect api` or IDE completion before writing code
2. **Delete obsolete files early** - Remove old interfaces/routing before converting implementations
3. **Use a test command first** - Start with the simplest command (LlmsTxtCommand) to validate the pattern
4. **Build after each file** - Catch errors incrementally rather than at the end
