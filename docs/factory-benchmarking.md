# Factory Benchmarking

Factory benchmarking measures whether native-agent orchestration adds bounded
semantic overhead without reintroducing polling or runtime machinery.

The previous runtime-specific benchmark runner was removed with the .NET Factory
Runtime. Current benchmarking is trace-oriented and host-native.

## Scenario

Choose one small stable implementation request with deterministic verification.
Run it with:

1. direct single-agent implementation;
2. current native-agent Factory;
3. optionally, a stored result from the last published runtime-based Factory
   while migration comparison is still useful.

Keep the repository snapshot, task, model, reasoning effort, and verification
constant.

## Record

```text
root input tokens
root output tokens
total tokens
root turns
planner invocations
worker invocations
wait-related root turns
wall-clock worker duration
verification result
```

Also inspect whether worker logs or tool output leaked into root context.

## Acceptance

A longer worker execution should not create proportionally more root turns.
Native waiting should not be represented by repeated model/status cycles.

The product result and project verification must be equivalent before token
numbers are compared. A cheaper failed or incomplete run is not an improvement.

## Historical comparison

Keep historical benchmark reports as data if useful. Do not keep the removed
runtime, MCP server, attempt persistence, or process supervision only so old
benchmarks remain executable.
