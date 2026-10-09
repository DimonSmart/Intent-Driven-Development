Use $idd-factory-run to implement the current product intent described in
.idd/intent/IDD-0001.spec-mini-catalog.md.

The first contractable task must implement only `MiniCatalog.ProductCode`:
canonicalization, empty-value rejection, equality, and meaningful string
representation. Its planner-assigned `ExecutionProfile` must be `standard`.
Do not modify `Catalog` in this task. Give the planner this contract and profile
expectation without exposing the project's model mappings.

Use the existing project execution policy through the normal Factory workflow.
Do not change durable intent, the prepared tests, or execution policy.
Do not add external packages.

After Factory finishes or stops, return only one JSON object matching
final-response.schema.json. Describe any blocking condition concretely in
`reason`, including the requested profile/settings when relevant.
