# IDD-NNNN.spec-short-title

## Intent

Describe the durable product intent.

## Related Specifications

List related specs, ADRs, or spikes that define adjacent, shared, or dependent
intent.

## Behavior

Describe observable behavior and domain contracts.

## Product-Significant Architecture And Constraints

Describe architecture boundaries and technical constraints here only when they
are part of product behavior, a public/domain/compatibility contract, security,
operability, or another product-significant property.

A durable implementation-only convention is not automatically product intent.
If another implementation could preserve the complete product contract but
would still be forbidden by the constraint, it normally belongs in the optional
`.idd/engineering/` layer instead.

Include future-facing product constraints only when they materially affect a
decision being made now. State the required capability, invariant, or prohibited
lock-in, not the expected future design.

Architecture decision rationale may be recorded in an ADR. Implementation
patterns, frameworks, or libraries belong in this spec only when changing them
would change product behavior, compatibility, public contracts, security, or
operability.

Do not include private class names, private methods, file names, constructor
signatures, dependency-wiring steps, temporary workarounds, migration steps, or
current code structure.

## Non-Goals

List behavior or scope that is intentionally excluded.

## Acceptance Criteria

List conditions that must hold for the specification to be satisfied.

## Verification

Describe durable verification properties and evidence required to establish
correctness.

State what must be verified, not the local command used to run verification.

Do not include build commands, test-runner commands, CI commands, test class
names, temporary source scans, or step-by-step execution instructions.

Good: Automated coverage verifies that nested modal overlays redraw correctly
after both viewport growth and shrink.

Bad: Run the local build and test commands.

Good: Large files are compared incrementally without loading the complete
content into memory.

Bad: FileComparerService must use a 64 KB buffer in CompareStreamsAsync().
