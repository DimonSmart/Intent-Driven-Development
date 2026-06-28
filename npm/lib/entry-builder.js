const path = require("path");
const { contentRoot } = require("./content-root");
const { codingAgentCapabilities } = require("./manifest");
const { selectedSkills } = require("./pack-resolver");
const { fail } = require("./errors");
const { normalize, readRequired } = require("./fs-utils");
const { sha256 } = require("./hash");

function buildEntry(manifest, codingAgent, entryMode, selectedPacks) {
  const entryPoint = manifest.entryPoints[codingAgent];
  if (!entryPoint) {
    fail(`No entry point configured for CodingAgent: ${codingAgent}`);
  }

  const blocks = [
    readRequired(path.join(contentRoot, "src", "adapters", codingAgent, "entry.md")),
    readRequired(path.join(contentRoot, "src", "canonical", "packs", "intent-driven-development.md"))
      .replace("{{skillGuidance}}", buildSkillGuidance(manifest, codingAgent, selectedPacks))
      .replace("{{workflowGuidance}}", buildWorkflowGuidance(manifest, codingAgent))
  ];

  if (entryMode === "full") {
    blocks.push(readCanonicalMethodology());
  }

  const content = `${blocks.map((block) => block.trim()).join("\n\n")}\n`;
  const buffer = Buffer.from(content, "utf8");
  return {
    relativePath: normalize(entryPoint),
    sourcePath: null,
    content: buffer,
    hash: sha256(buffer)
  };
}

function buildSkillGuidance(manifest, codingAgent, selectedPacks) {
  if (!supportsGeneratedSkills(manifest, codingAgent)) {
    return `This CodingAgent does not use generated IDD skills. Keep IDD work focused and
read only the documents needed for the current task.`;
  }

  const skills = Array.from(selectedSkills(manifest, selectedPacks)).sort().map((skill) => `- \`${skill}\``).join("\n");
  const blocks = [
    `Use installed IDD skills for specific workflows:\n${skills}`,
    `## IDD Workflow Routing

Use \`spec-brainstorm\` when product intent is unclear.
Use \`spec-change\` when durable product behavior must change.
Use \`spec-implement\` for one focused behavior already covered by
\`.specs/\`, then use \`spec-check-implementation\`.
Use \`spec-new-document\` only for a new durable product area, ADR, or
spike.`
  ];

  if (selectedPacks.includes("factory")) {
    blocks.push(`## IDD Factory Routing

Use factory skills only for planned implementation orchestration,
multi-step execution, task slicing, or factory role based work.

- Use \`factory-create-work-plan\` to create a temporary Factory Work Plan.
- Use \`factory-execute-work-plan\` to execute an explicit Factory Work Plan.
- Use \`factory-review-task\` after each bounded task.
- Use \`factory-review-work-result\` after all tasks are complete.
- Use \`factory-finish-work\` to summarize and clean temporary factory artifacts.

Factory work plans are temporary execution state.
They are not specs and must not be stored in \`.specs/\`.
Do not read old factory work plans unless the user explicitly provides the exact path.`);
  }

  return blocks.join("\n\n");
}

function buildWorkflowGuidance(manifest, codingAgent) {
  return supportsGeneratedSkills(manifest, codingAgent)
    ? "This file and installed IDD skills are workflow guidance.\nThey are not product specifications."
    : "This file is workflow guidance.\nIt is not a product specification.";
}

function readCanonicalMethodology() {
  const methodologyRoot = path.join(contentRoot, "src", "canonical", "methodology");
  const names = [
    "intent-driven-development.md",
    "numbering.md",
    "document-types.md",
    "semantic-changes.md",
    "coding-agent-workflow.md"
  ];

  return names
    .map((name) => readRequired(path.join(methodologyRoot, name)).trim())
    .join("\n\n");
}

function supportsGeneratedSkills(manifest, codingAgent) {
  const capabilities = codingAgentCapabilities(manifest) && codingAgentCapabilities(manifest)[codingAgent];
  if (!capabilities) {
    fail(`Bundled manifest does not define codingAgentCapabilities for CodingAgent: ${codingAgent}`);
  }

  return capabilities.supportsSkills === true;
}

module.exports = {
  buildEntry,
  supportsGeneratedSkills
};
