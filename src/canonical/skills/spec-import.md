# spec-import

Use this skill to import existing product specifications into the IDD structure.

The source material may come from another repository, another methodology, a
documentation folder, issue-derived product notes, architecture records, or any
other durable intent format. Its organization may be completely different from
IDD.

## Workflow

1. Identify which source documents describe durable product intent.
2. Separate current intent, old intent, temporary status, task notes, and local
   process details.
3. Rewrite current durable intent into numbered IDD documents under `.specs/`.
4. Put obsolete but historically useful normative intent under `.specs/archive/`.
5. Convert architectural rationale into ADRs and uncertainty checks into spikes.
6. Remove task notes, temporary status, and source-specific wrapper text from
   imported specifications.
7. Preserve meaning during import. Do not introduce new requirements silently.
