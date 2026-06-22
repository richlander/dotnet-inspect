# Explain

Selected diagnostics: 29
Matched clusters: 1

## Async task misuse

Cluster: `async-task-misuse`

Task-returning operations are being ignored, used as values, or mixed with synchronous code.

### Applies to

| Severity | Code | Count | Example |
| --- | --- | ---: | --- |
| error | CS4014 | 14 | Because this call is not awaited, execution of the current method continues before the call is comp… |
| error | CS0029 | 9 | Cannot implicitly convert type 'System.Threading.Tasks.Task<EnterTheRing.Signal>' to 'EnterTheRing.… |
| error | CS0019 | 2 | Operator '+=' cannot be applied to operands of type 'double' and 'Task<double>' |
| error | CS1929 | 2 | 'Task<List<double>>' does not contain a definition for 'Average' and the best extension method over… |
| error | CS1503 | 1 | Argument 1: cannot convert from 'System.Threading.Tasks.Task<EnterTheRing.Signal>' to 'EnterTheRing… |
| error | CS4016 | 1 | Since this is an async method, the return expression must be of type 'string' rather than 'Task<str… |

### Likely cause

An async method result is not awaited, a Task<T> is being used where T is expected, or a collection of tasks is being treated as completed values.

### First fixes

1. Add await at the call site when the caller can become async.
2. Propagate async outward instead of blocking when possible.
3. Use Task.WhenAll before aggregating task results.
4. If a method has no real async work, return Task.CompletedTask or Task.FromResult instead of marking it async.

### Useful follow-up

- `dotnet-inspect build <log> -S Errors --code CS4014 --tsv`
- `dotnet-inspect build <log> -S Details --code CS4014 --markdown`
