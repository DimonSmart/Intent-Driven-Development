# Research idea: local semantic evaluation of Factory tests

This is an unimplemented research direction, not current Product Intent or an active release gate.

Explore a small locally hosted LLM (for example through Ollama) as an
optional judge for the meaning and coherence of free-form agent responses
in live evaluations. This may reduce reliance on brittle text patterns
without paid model calls. First calibrate it against manually reviewed cases,
including false positives and false negatives. Keep mechanical assertions
for native lifecycle, IDs, file structure and independently runnable product
tests. An LLM judgment is semantic evidence, not deterministic proof; it
must not become a mandatory release gate without an explicit evaluated
decision. This is a research direction, not a current Factory requirement.
