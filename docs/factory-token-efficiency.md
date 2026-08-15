# Factory Token Efficiency

Factory is intended to add decomposition, isolation, deterministic verification, and independent review without making orchestration an uncontrolled token multiplier. Its token cost therefore needs to be understood as several scopes rather than one undifferentiated number:

- the root launcher or transport;
- semantic workers grouped by role and attempt;
- the complete end-to-end Factory total;
- tool activity, failures, retries, and corrective cycles that help explain the total.

Gross input, cached input, new input, and output are reported separately. New input is calculated as gross input minus cached input only when both counters are available and consistent. Sequential tool batches are also important: many independent tool calls issued together represent one model/tool round rather than many sequential rounds.

Factory telemetry by itself cannot say whether Factory is economical. A meaningful comparison must hold the task, model, reasoning effort, workspace, and correctness check constant while adding Factory mechanisms incrementally. The repository's [Factory Benchmark Runner](factory-benchmarking.md) provides that comparison.
