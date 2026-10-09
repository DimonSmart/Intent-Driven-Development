This evaluation also exercises recovery from a deliberate final-verification
failure. The configured `recovery-gate` check must first run and fail in the root
after the planner returns `# Done`. It succeeds only after the normal bounded
`verification-failure.md` diagnostic has been written.

Do not pre-create that diagnostic, change the gate, remove it from the policy,
or change the prepared tests. After the actual failure, persist the bounded
diagnostic and invoke a fresh planner with it. Follow the planner's result; after
a new `# Done`, repeat every configured final check, including the gate, build
and tests. Archive the diagnostic with the completed run. The gate is test
infrastructure, not product behavior or durable Intent.
