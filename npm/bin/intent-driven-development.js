#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const crypto = require("crypto");

const packageRoot = path.resolve(__dirname, "..");
const contentRoot = path.join(packageRoot, "package-content");
const manifestPath = path.join(contentRoot, "manifest.json");
const packageJsonPath = path.join(packageRoot, "package.json");

function main() {
  const args = process.argv.slice(2);
  const command = args[0];

  if (!command || command === "help" || command === "--help" || command === "-h") {
    printUsage();
    return;
  }

  if (command === "list-targets" || command === "list-coding-agents") {
    codingAgents(readManifest()).forEach((codingAgent) => console.log(codingAgent));
    return;
  }

  if (command === "list-packs") {
    Object.keys(readManifest().packs).sort().forEach((pack) => console.log(pack));
    return;
  }

  if (command === "version") {
    const packageJson = JSON.parse(fs.readFileSync(packageJsonPath, "utf8"));
    const manifest = readManifest();
    console.log(`package: ${packageJson.version}`);
    console.log(`manifest: ${manifest.version}`);
    return;
  }

  if (command === "init") {
    ensureNoUnknownArgs(args.slice(1), ["--force"]);
    initProject(args.includes("--force"));
    return;
  }

  if (command === "install") {
    install(args.slice(1));
    return;
  }

  fail(`Unknown command: ${command}`);
}

function printUsage() {
  console.log(`Usage:
  intent-driven-development init [--force]
  intent-driven-development install --target <coding-agent> [--pack <pack>]... [--entry minimal|none|full] [--force]
  intent-driven-development install --coding-agent <coding-agent> [--pack <pack>]... [--entry minimal|none|full] [--force]
  intent-driven-development install --all [--pack <pack>]... [--entry minimal|none|full] [--force]
  intent-driven-development list-targets
  intent-driven-development list-coding-agents
  intent-driven-development list-packs
  intent-driven-development version`);
}

function readManifest() {
  if (!fs.existsSync(manifestPath)) {
    fail(`Bundled manifest not found: ${manifestPath}`);
  }

  return JSON.parse(fs.readFileSync(manifestPath, "utf8"));
}

function install(args) {
  ensureNoUnknownArgs(args, ["--target", "--coding-agent", "--all", "--entry", "--force", "--pack"]);

  const manifest = readManifest();
  const force = args.includes("--force");
  const installAll = args.includes("--all");
  const target = valueAfter(args, "--target");
  const codingAgentOption = valueAfter(args, "--coding-agent");
  if (target && codingAgentOption) {
    fail("Use either --target or --coding-agent, not both.");
  }

  const codingAgent = codingAgentOption || target;
  const entryMode = parseEntryMode(valueAfter(args, "--entry"));
  const selectedPacks = resolvePacks(manifest, valuesAfter(args, "--pack"));

  if (installAll && codingAgent) {
    fail("Use either --all or --target <coding-agent>, not both.");
  }

  if (!installAll && !codingAgent) {
    fail("Missing CodingAgent. Use --target <coding-agent>, --coding-agent <coding-agent>, or --all.");
  }

  const selectedCodingAgents = installAll ? codingAgents(manifest) : [validateCodingAgent(manifest, codingAgent)];
  validateEntryModeCapabilities(manifest, selectedCodingAgents, entryMode, installAll);
  validatePackCodingAgentCapabilities(manifest, selectedCodingAgents, selectedPacks);
  const plannedFiles = collectCodingAgentFiles(manifest, selectedCodingAgents, entryMode, selectedPacks);
  copyPlannedFiles(plannedFiles, process.cwd(), force);
  const packText = isDefaultPackSelection(manifest, selectedPacks) ? "" : ` and packs: ${selectedPacks.join(", ")}`;
  console.log(`Installed ${selectedCodingAgents.join(", ")} with ${entryMode} entry${packText}.`);
}

function initProject(force) {
  const source = path.join(contentRoot, "src", "canonical", "project-files", "specs");
  const destination = path.join(process.cwd(), ".specs");

  if (!fs.existsSync(source)) {
    fail(`Bundled canonical project files not found: ${source}`);
  }

  if (fs.existsSync(destination) && !force) {
    fail("File already exists: .specs\nUse --force to overwrite.");
  }

  copyDirectory(source, destination, force);
  console.log("Initialized .specs.");
}

function codingAgents(manifest) {
  return manifest.codingAgents || manifest.targets || [];
}

function codingAgentCapabilities(manifest) {
  return manifest.codingAgentCapabilities || manifest.targetCapabilities;
}

function validateCodingAgent(manifest, codingAgent) {
  if (codingAgents(manifest).includes(codingAgent)) {
    return codingAgent;
  }

  fail(`Unknown CodingAgent: ${codingAgent}\nAvailable CodingAgents: ${codingAgents(manifest).join(", ")}`);
}

function parseEntryMode(value) {
  if (value === null) {
    return "minimal";
  }

  if (["minimal", "none", "full"].includes(value)) {
    return value;
  }

  fail(`Unknown entry mode: ${value}\nAvailable entry modes: minimal, none, full`);
}

function validateEntryModeCapabilities(manifest, selectedCodingAgents, entryMode, installAll) {
  if (entryMode !== "none") {
    return;
  }

  if (!codingAgentCapabilities(manifest)) {
    fail("Bundled manifest does not define codingAgentCapabilities.");
  }

  const incompatible = selectedCodingAgents.filter((codingAgent) => !supportsGeneratedSkills(manifest, codingAgent));
  if (incompatible.length === 0) {
    return;
  }

  if (installAll) {
    fail(`The following CodingAgents do not support generated skills: ${incompatible.join(", ")}.
--entry none would install no entry point and no skills for those CodingAgents.
Use --entry minimal or install skill-capable CodingAgents explicitly.`);
  }

  fail(`CodingAgent ${incompatible[0]} does not support generated skills. --entry none would install no entry point and no skills.
Use --entry minimal or --entry full for this CodingAgent.`);
}

function supportsGeneratedSkills(manifest, codingAgent) {
  const capabilities = codingAgentCapabilities(manifest) && codingAgentCapabilities(manifest)[codingAgent];
  if (!capabilities) {
    fail(`Bundled manifest does not define codingAgentCapabilities for CodingAgent: ${codingAgent}`);
  }

  return capabilities.supportsSkills === true;
}

function resolvePacks(manifest, requestedPacks) {
  validatePackManifest(manifest);
  const selected = new Set();

  if (requestedPacks.length === 0) {
    for (const [packName, pack] of Object.entries(manifest.packs)) {
      if (pack.default === true) {
        addPackWithDependencies(manifest, packName, selected);
      }
    }
  } else {
    for (const packName of [...new Set(requestedPacks)]) {
      if (!manifest.packs[packName]) {
        fail(`Unknown pack: ${packName}\nAvailable packs: ${Object.keys(manifest.packs).sort().join(", ")}`);
      }

      addPackWithDependencies(manifest, packName, selected);
    }
  }

  return Array.from(selected).sort();
}

function addPackWithDependencies(manifest, packName, selected) {
  for (const dependency of manifest.packs[packName].requires || []) {
    addPackWithDependencies(manifest, dependency, selected);
  }

  selected.add(packName);
}

function isDefaultPackSelection(manifest, selectedPacks) {
  const defaults = Object.entries(manifest.packs)
    .filter(([, pack]) => pack.default === true)
    .map(([name]) => name)
    .sort();
  return defaults.join("\0") === [...selectedPacks].sort().join("\0");
}

function validatePackCodingAgentCapabilities(manifest, selectedCodingAgents, selectedPacks) {
  if (!selectedPacks.includes("factory")) {
    return;
  }

  const incompatible = selectedCodingAgents.filter((codingAgent) => !supportsGeneratedSkills(manifest, codingAgent));
  if (incompatible.length > 0) {
    fail(`Factory pack requires generated skills. Unsupported CodingAgents: ${incompatible.join(", ")}.`);
  }
}

function validatePackManifest(manifest) {
  if (!manifest.packs) {
    fail("Bundled manifest does not define packs.");
  }

  for (const [packName, pack] of Object.entries(manifest.packs)) {
    for (const dependency of pack.requires || []) {
      if (!manifest.packs[dependency]) {
        fail(`Pack '${packName}' requires unknown pack '${dependency}'.`);
      }
    }
  }

  for (const packName of Object.keys(manifest.packs)) {
    validatePackDependencyAcyclic(manifest, packName, new Set(), new Set());
  }
}

function validatePackDependencyAcyclic(manifest, packName, visiting, visited) {
  if (visited.has(packName)) {
    return;
  }

  if (visiting.has(packName)) {
    fail(`Pack dependency cycle includes '${packName}'.`);
  }

  visiting.add(packName);
  for (const dependency of manifest.packs[packName].requires || []) {
    validatePackDependencyAcyclic(manifest, dependency, visiting, visited);
  }

  visiting.delete(packName);
  visited.add(packName);
}

function selectedSkills(manifest, selectedPacks) {
  const skills = new Set();
  for (const packName of selectedPacks) {
    for (const skill of manifest.packs[packName].skills || []) {
      skills.add(skill);
    }
  }

  return skills;
}

function collectCodingAgentFiles(manifest, selectedCodingAgents, entryMode, selectedPacks) {
  const byRelativePath = new Map();
  const skills = selectedSkills(manifest, selectedPacks);

  for (const codingAgent of selectedCodingAgents) {
    const sourceRoot = path.join(contentRoot, "generated", codingAgent);
    if (!fs.existsSync(sourceRoot)) {
      fail(`Bundled generated CodingAgent not found: ${codingAgent}`);
    }

    for (const file of listFiles(sourceRoot)) {
      const relativePath = normalize(path.relative(sourceRoot, file));
      if (
        manifest.entryPoints[codingAgent] &&
        relativePath === normalize(manifest.entryPoints[codingAgent])
      ) {
        continue;
      }

      const skillName = generatedSkillName(relativePath);
      if (skillName && !skills.has(skillName)) {
        continue;
      }

      const content = fs.readFileSync(file);
      const existing = byRelativePath.get(relativePath);
      if (existing) {
        if (existing.hash !== sha256(content)) {
          fail(`Conflicting bundled files for path: ${relativePath}`);
        }

        continue;
      }

      byRelativePath.set(relativePath, {
        relativePath,
        sourcePath: file,
        content: null,
        hash: sha256(content)
      });
    }

    if (entryMode !== "none") {
      const fullEntry = buildEntry(manifest, codingAgent, entryMode, selectedPacks);
      const existing = byRelativePath.get(fullEntry.relativePath);
      if (existing) {
        if (existing.hash !== fullEntry.hash) {
          fail(`Conflicting bundled files for path: ${fullEntry.relativePath}`);
        }

        continue;
      }

      byRelativePath.set(fullEntry.relativePath, fullEntry);
    }
  }

  for (const packName of selectedPacks) {
    for (const projectFile of manifest.packs[packName].projectFiles || []) {
      const projectFilesRoot = path.join(contentRoot, projectFile.source);
      if (!fs.existsSync(projectFilesRoot)) {
        fail(`Bundled project files not found: ${projectFile.source}`);
      }

      for (const file of listFiles(projectFilesRoot)) {
        const relativePath = normalize(path.join(projectFile.destination, path.relative(projectFilesRoot, file)));
      const content = fs.readFileSync(file);
      const existing = byRelativePath.get(relativePath);
      if (existing) {
        if (existing.hash !== sha256(content)) {
          fail(`Conflicting bundled files for path: ${relativePath}`);
        }

        continue;
      }

      byRelativePath.set(relativePath, {
        relativePath,
        sourcePath: file,
        content: null,
        hash: sha256(content)
      });
      }
    }
  }

  return Array.from(byRelativePath.values());
}

function generatedSkillName(relativePath) {
  const parts = normalize(relativePath).split("/");
  const index = parts.indexOf("skills");
  if (index === -1 || index + 1 >= parts.length) {
    return null;
  }

  return parts[index + 1];
}

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

function copyPlannedFiles(files, destinationRoot, force) {
  const conflicts = files
    .filter((file) => {
      const destination = path.join(destinationRoot, file.relativePath);
      return fs.existsSync(destination) && sha256(fs.readFileSync(destination)) !== file.hash;
    })
    .map((file) => file.relativePath);

  if (conflicts.length > 0 && !force) {
    for (const relativePath of conflicts) {
      console.error(`File already exists: ${relativePath}`);
    }

    fail("Use --force to overwrite.");
  }

  for (const file of files) {
    const destination = path.join(destinationRoot, file.relativePath);
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    if (file.content) {
      fs.writeFileSync(destination, file.content);
    } else {
      fs.copyFileSync(file.sourcePath, destination);
    }
  }
}

function copyDirectory(source, destination, force) {
  const files = listFiles(source).map((sourcePath) => ({
    relativePath: normalize(path.relative(source, sourcePath)),
    sourcePath
  }));

  if (fs.existsSync(destination) && !force) {
    fail(`File already exists: ${normalize(path.relative(process.cwd(), destination))}\nUse --force to overwrite.`);
  }

  for (const file of files) {
    const destinationPath = path.join(destination, file.relativePath);
    fs.mkdirSync(path.dirname(destinationPath), { recursive: true });
    fs.copyFileSync(file.sourcePath, destinationPath);
  }
}

function listFiles(root) {
  const result = [];
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    const fullPath = path.join(root, entry.name);
    if (entry.isDirectory()) {
      result.push(...listFiles(fullPath));
    } else if (entry.isFile()) {
      result.push(fullPath);
    }
  }

  return result;
}

function ensureNoUnknownArgs(args, known) {
  for (let index = 0; index < args.length; index += 1) {
    const arg = args[index];
    if (!arg.startsWith("--")) {
      continue;
    }

    if (!known.includes(arg)) {
      fail(`Unknown option: ${arg}`);
    }

    if (arg === "--target" || arg === "--coding-agent" || arg === "--entry" || arg === "--pack") {
      index += 1;
    }
  }
}

function valuesAfter(args, option) {
  const values = [];
  for (let index = 0; index < args.length; index += 1) {
    if (args[index] !== option) {
      continue;
    }

    const value = args[index + 1];
    if (!value || value.startsWith("--")) {
      fail(`Missing value for ${option}.`);
    }

    values.push(value);
    index += 1;
  }

  return values;
}

function valueAfter(args, option) {
  const index = args.indexOf(option);
  if (index === -1) {
    return null;
  }

  const value = args[index + 1];
  if (!value || value.startsWith("--")) {
    fail(`Missing value for ${option}.`);
  }

  return value;
}

function sha256(buffer) {
  return crypto.createHash("sha256").update(buffer).digest("hex");
}

function normalize(value) {
  return value.replace(/\\/g, "/");
}

function readRequired(filePath) {
  if (!fs.existsSync(filePath)) {
    fail(`Required bundled file not found: ${filePath}`);
  }

  return fs.readFileSync(filePath, "utf8");
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

main();
