# Prompt: Constructor Overloads

Show me all the constructors for HttpClient.

## Optimal Path (Expert, Markdown)

```bash
dotnet-inspect api HttpClient --platform System.Net.Http --ctor -v:d
```

## Optimal Path (Expert, JSON)

```bash
dotnet-inspect api HttpClient --platform System.Net.Http --ctor --json | jq '.members[] | .signature'
```

## Discovery Path (Learning)

```bash
# Step 1: Find where HttpClient lives
dotnet-inspect find HttpClient --framework runtime

# Output: System.Net.Http.HttpClient in System.Net.Http.dll

# Step 2: See type overview
dotnet-inspect type HttpClient --platform System.Net.Http

# Step 3: Get detailed constructor info
dotnet-inspect api HttpClient --platform System.Net.Http --ctor -v:d
```

## Expected Output

```text
## Constructors (3 overloads)

### Overload 1: 0 parameters
new HttpClient()

### Overload 2: 1 parameter
new HttpClient(HttpMessageHandler handler)

### Overload 3: 2 parameters
new HttpClient(HttpMessageHandler handler, bool disposeHandler)
```

## Key Learnings

- `--ctor` flag shows only constructors
- `-v:d` gives detailed output with parameter tables
- Equivalent to `-m .ctor` but more readable
- HttpClient is in `System.Net.Http` assembly
