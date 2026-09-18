Use $idd-factory-run to implement the current product intent described in
.idd/intent/IDD-0001.spec-mini-catalog.md.

This is an IDD Factory evaluation. It must exercise native-agent incremental
orchestration and at least two separate fresh worker contexts.

The first contractable batch must contain these two implementation tasks in this
order:

1. Implement the `MiniCatalog.ProductCode` value type only: canonicalization,
   empty-value rejection, equality, and meaningful string representation. Do not
   modify `Catalog` in this task.
2. Update `MiniCatalog.Catalog` to use `ProductCode`: store canonical values,
   reject normalized duplicates, preserve read-only `Codes` access, and
   implement ordinal `Summary()` output.

The second task is self-contained and must inspect repository reality produced by
the first worker. Do not use `RelevantCompletedWork`, runtime work-item IDs, or
a Factory MCP/runtime transport.

Do not combine these responsibilities into one worker context. Do not change
durable product intent, add external packages, or modify prepared tests except to
repair an objective test infrastructure error.

Complete configured/final project verification. After Factory finishes or
stops, return only one JSON object matching final-response.schema.json.
