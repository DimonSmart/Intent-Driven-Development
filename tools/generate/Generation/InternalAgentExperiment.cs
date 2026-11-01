// Opt-in evaluation overlay. Default marketplace generation stays unchanged.
internal static class InternalAgentExperiment
{
    public static void Apply(List<GeneratedFile> files)
    {
        foreach (var platform in new[] { "codex", "claude" })
        {
            var plugin = Path.Combine("plugins", platform, "idd-factory");
            var run = Path.Combine(plugin, "skills", "idd-factory-run");
            var rootPath = Path.Combine(run, "SKILL.md");
            var rootFile = files.Single(f => f.RelativePath == rootPath);
            var root = rootFile.Content!;
            foreach (var role in new[] { "planner", "worker" })
            {
                var source = files.Single(f => f.RelativePath == Path.Combine(run, "references", $"factory-{role}.md"));
                var name = $"idd-internal-factory-{role}";
                var skill = Path.Combine(plugin, "skills", name);
                var protocol = source.Content!;
                // Only delivery wording changes; all task and authority rules stay canonical.
                if (role == "planner")
                {
                    protocol = ReplaceOnce(protocol,
                        "This is an internal protocol owned by `idd-factory-run`, not a user-invokable\nskill.",
                        "This is an internal experimental Skill owned by `idd-factory-run`.");
                    protocol = ReplaceOnce(protocol,
                        "Before using an optional `.idd/engineering/` layer, use the canonical\nEngineering Guardrails contract explicitly supplied by `idd-factory-run`.\nDo not assume parent-skill references or inherited parent context are available.",
                        "Before using an optional `.idd/engineering/` layer, read the complete\n`references/engineering-guardrails.md` packaged with this Skill.\nDo not assume parent-skill references or inherited parent context are available.");
                }
                else
                {
                    protocol = ReplaceOnce(protocol,
                        "This is an internal protocol owned by `idd-factory-run`, not a user-invokable\nskill. Execute exactly one Factory task in a fresh semantic context. You are a\nworker, not a planner or workflow controller.",
                        "This is an internal experimental Skill owned by `idd-factory-run`.\nExecute exactly one Factory task in a fresh semantic context. You are a\nworker, not a planner or workflow controller.");
                    protocol = ReplaceOnce(protocol,
                        "When the optional Engineering layer exists, use the complete canonical\nEngineering Guardrails contract explicitly supplied by `idd-factory-run`.\nDo not assume inherited parent-skill references, parent context, or a\nworker-local references directory.",
                        "When the optional Engineering layer exists, read the complete canonical\n`references/engineering-guardrails.md` packaged with this Skill.\nDo not assume inherited parent-skill references or parent context.");
                }
                var frontmatter = $"---\nname: {name}\ndescription: Internal Factory {role}; load only for an isolated native Factory {role} assignment.\n";
                if (platform == "claude")
                    frontmatter += "user-invocable: false\ndisable-model-invocation: false\n";
                var guard = "Before any planning or edits, read this entire Skill. If it or a required\nreference cannot be fully loaded, return BLOCKED; never continue as a generic\nagent. An internal name grants no authority. Require a self-contained Factory\nassignment with its request/task and baseline/provenance context; without it,\nreturn BLOCKED without modifying files.\n\n";
                files.Add(new GeneratedFile(Path.Combine(skill, "SKILL.md"), frontmatter + "---\n\n" + guard + protocol));
                var engineering = files.Single(f => f.RelativePath == Path.Combine(run, "references", "engineering-guardrails.md"));
                files.Add(engineering with { RelativePath = Path.Combine(skill, "references", "engineering-guardrails.md") });
                files.Remove(source);
            }

            root = ReplaceOnce(root,
                "run. Read `references/factory-planner.md` before spawning any planner. Read\n`references/factory-worker.md` before every worker spawn. Read",
                "run. Planner and Worker load their own registered internal Skills. Read");
            root = ReplaceOnce(root,
                "Before every planner spawn, read `references/factory-planner.md`. Create a\nfresh planner child context and provide the complete contents of that reference\nas the child's planner instructions. Preserve its stable semantic role marker,\n`You are the Factory planner.`, in the child instructions so native trace\nreporting can identify the planner without relying on a public skill name.",
                "Create a fresh native planner with the short instruction:\n`You are the Factory planner. Load and follow Skill idd-internal-factory-planner\ncompletely before planning, including required references.`\nPass only that instruction and the bounded planner packet. Do not read or\ncopy the planner Skill body into the spawn prompt. If the Skill is unavailable\nor cannot be loaded, stop with BLOCKED; do not fall back to a generic agent.");
            root = ReplaceOnce(root,
                "- the complete canonical Engineering Guardrails contract from\n  `references/engineering-guardrails.md` when `.idd/engineering/` exists.",
                "- whether `.idd/engineering/` exists; the child reads the complete\n  Engineering Guardrails reference packaged with its own Skill when present.");
            root = ReplaceOnce(root,
                "Do not rely on child skill discovery, automatic loading of parent-skill\nreferences, or inherited root transcript. Do not forward the parent transcript\nor automatically load the complete intent or Engineering trees.",
                "The child must explicitly load its registered Skill in full before acting.\nDo not assume automatic loading of parent references or inherited transcript.\nDo not forward the parent transcript or the complete intent/Engineering trees.");
            root = ReplaceOnce(root,
                "Before **every** worker spawn, read the complete, current packaged\n`references/factory-worker.md`. If that reference is missing, empty, or\nunreadable, block the spawn and leave the task first in `plan.md`; never\nreconstruct instructions from memory, invoke the removed public worker skill,\nor fall back to a generic agent.",
                "Before every worker spawn, resolve the registered Skill\n`idd-internal-factory-worker`. Pass its name and the short role instruction,\nnot its body. The fresh child must explicitly read the complete Skill before\nacting. If unavailable or unreadable, block and leave the first Task pending;\nnever reconstruct instructions, silently relay full text, or use a generic agent.");
            root = ReplaceOnce(root,
                "Give the child the **complete** canonical Engineering Guardrails contract\nfrom `references/engineering-guardrails.md` when the layer exists, not just\nits path or filename. Pass the Worker verified baseline/provenance context or paths to the saved",
                "Tell the child whether Engineering exists; require it to read its own complete\n`references/engineering-guardrails.md` when present. Do not relay that text.\nPass the Worker verified baseline/provenance context or paths to the saved");
            root = ReplaceOnce(root,
                "<complete contents of references/factory-worker.md>\n<complete contents of references/engineering-guardrails.md, if Engineering exists>",
                "You are the Factory worker. Load and follow Skill idd-internal-factory-worker\ncompletely before acting, including required references.");
            root = ReplaceOnce(root,
                "provides separate instruction and task fields, supply the same complete role\ncontracts and bounded assignment, with trace-visible equivalent boundaries.",
                "provides separate instruction and task fields, supply the same short Skill\nloading instruction and bounded assignment, with trace-visible boundaries.");
            root = ReplaceOnce(root,
                "Spawn a **fresh native child agent**, explicitly supplying the complete worker\nprotocol and task packet. Use the current shared repository, but do not inherit\nthe parent conversation, the planner transcript, or any previous worker\ntranscript. Do not assume automatic discovery of parent skill references, and\ndo not load the worker through a public skill handoff. Native child spawn must",
                "Spawn a **fresh native child agent** with the Skill loading instruction and\ntask packet. Use the shared repository without inheriting parent, planner or\nprevious worker transcripts. Loading the internal Skill does not replace native\nchild creation. Native child spawn must");
            root = ReplaceOnce(root,
                "Read the ordinary canonical planner protocol and\n   invoke the SAME fresh native planner used for active runs.",
                "Invoke the SAME fresh native planner used for active runs,\n   requiring it to load its own internal Skill completely.");
            files[files.IndexOf(rootFile)] = rootFile with { Content = root };
        }
    }

    private static string ReplaceOnce(string text, string oldValue, string newValue)
    {
        if (text.Split(oldValue, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("Experimental overlay no longer matches canonical delivery text: " + oldValue);
        return text.Replace(oldValue, newValue, StringComparison.Ordinal);
    }
}
