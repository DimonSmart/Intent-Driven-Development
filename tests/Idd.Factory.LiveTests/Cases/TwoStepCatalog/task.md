Use $idd-factory-run to implement the current product intent described in
.idd/intent/IDD-0001-mini-catalog.md.

This is an IDD Factory evaluation.

Plan and execute the work according to the current Factory workflow. Do not assume a fixed number of tasks or semantic workers in advance.
Do not change durable product intent, add external packages, or modify prepared tests except to repair an objective test infrastructure error.
Complete the product work and all required verification.

After the Factory attempt finishes or stops, return only one JSON object
matching final-response.schema.json.

For COMPLETED, factoryResultPath must point to the generated
factory-result.json.

For any other outcome, factoryResultPath must be null and reason must
describe the actual stop condition.
