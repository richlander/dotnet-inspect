---
id: demo-queries
description: Run curated demo queries that showcase tool capabilities
commands: [demo]
areas: [demo, discovery, onboarding]
---

# Demo Queries

> The `demo` command provides curated queries that showcase the tool's capabilities. Use `demo list` to see all available demos, run one by index, or use `--feeling-lucky` for a random pick. Demos are categorized by purpose: Insight, Discovery, Migration, and Security.

## 1. List available demos

> Goal: See all curated demos with their descriptions.

```prompt
What demos are available?
```

```bash
dotnet-inspect demo list
```

```expect
# Demo Queries
1.
2.
3.
```

```query
grep -c '^[0-9]'
```

## 2. Run a specific demo

> Goal: Execute a demo by its index number.

```bash
dotnet-inspect demo 4 -n 15
```

```expect
JsonSerializer
```

## 3. Run a random demo

> Goal: Pick and run a random demo — useful for exploration and onboarding.

```bash
dotnet-inspect demo --feeling-lucky -n 10
```

```query
head -1
```
