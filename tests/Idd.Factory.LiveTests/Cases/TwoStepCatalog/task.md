Use $idd-factory-run to implement the current product intent described in
.idd/intent/IDD-0001-mini-catalog.md.

This is an IDD Factory evaluation.

Organize the implementation as exactly two implementation Subtasks:

1. Establish the reusable ProductCode normalization, validation, and equality contract.
2. Integrate ProductCode into Catalog, duplicate detection, and Summary.

The second Subtask depends on the public behavior established by the first.
Create exactly one independent Review checkpoint after the first Subtask and before the second Subtask.
Do not change durable product intent, add external packages, or modify prepared tests except to repair an objective test infrastructure error.
Complete all required verification and final integrated review.

After the Factory attempt finishes or stops, return only one JSON object
matching final-response.schema.json.

For COMPLETED, factoryResultPath must point to the generated
factory-result.json.

For any other outcome, factoryResultPath must be null and reason must
describe the actual stop condition.
