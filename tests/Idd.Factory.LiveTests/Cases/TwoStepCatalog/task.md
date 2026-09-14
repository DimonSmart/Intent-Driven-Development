Use $idd-factory-run to implement the current product intent described in
.idd/intent/IDD-0001.spec-mini-catalog.md.

This is an IDD Factory evaluation that must exercise sequential multi-step execution and completed-result handoff.

The first planning batch must contain at least these two implementation work items, in this order:

1. Implement the `MiniCatalog.ProductCode` value type only: canonicalization, empty-value rejection, equality, and meaningful string representation. Do not modify `Catalog` in this work item.
2. Update `MiniCatalog.Catalog` to consume the completed `ProductCode` abstraction: store canonical values, reject normalized duplicates, preserve read-only `Codes` access, and implement ordinal `Summary()` output. Treat the first work item as the completed prerequisite rather than reimplementing it unless authoritative verification requires a correction.

Do not combine these two responsibilities into one work item. Additional work items are allowed only if the Factory workflow genuinely requires them.
Do not change durable product intent, add external packages, or modify prepared tests except to repair an objective test infrastructure error.
Complete the product work and all required verification.

After the Factory attempt finishes or stops, return only one JSON object
matching final-response.schema.json.

For COMPLETED, factoryResultPath must point to the generated
factory-result.json.

For any other outcome, factoryResultPath must be null and reason must
describe the actual stop condition.
