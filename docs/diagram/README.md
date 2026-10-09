# IDD interactive overview

`idd-overview.workflow.json` is the editable Archify Workflow v2 source for the
standalone overview. The GitHub Pages workflow pins Archify 3.0.1, generates
`site/index.html`, runs the `showcase` delivery checks, and publishes that
generated directory. The generated HTML is deliberately not tracked: its
embedded third-party runtime text conflicts with a repository-wide legacy-name
validation test.

From the external diagram experiment directory, the generation command is:

```powershell
node <path-to-archify>\bin\archify.mjs finalize workflow docs\diagram\idd-overview.workflow.json site\index.html --repo-root <path-to-IDD> --quality showcase --json
```

The Pages workflow performs the same generation on every diagram change.
