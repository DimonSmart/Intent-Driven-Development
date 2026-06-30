const { readManifest, codingAgents, codingAgentCapabilities } = require("./manifest");
const { fail } = require("./errors");
const { ensureNoUnknownArgs, valueAfter, valuesAfter } = require("./fs-utils");
const { resolvePacks, isDefaultPackSelection } = require("./pack-resolver");
const { collectCodingAgentFiles } = require("./install-planner");
const { copyPlannedFiles } = require("./file-installer");
const { supportsGeneratedSkills, supportsManualOnlySkills } = require("./entry-builder");
const { selectedSkills } = require("./pack-resolver");

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

function validatePackCodingAgentCapabilities(manifest, selectedCodingAgents, selectedPacks) {
  const skills = selectedSkills(manifest, selectedPacks);
  if (skills.size === 0) {
    return;
  }

  const containsManualOnlySkills = Array.from(skills).some((skill) => manifest.skills && manifest.skills[skill]?.invocation === "manual");
  if (!containsManualOnlySkills) {
    return;
  }

  const manualIncompatible = selectedCodingAgents.filter((codingAgent) => !supportsManualOnlySkills(manifest, codingAgent));
  if (manualIncompatible.length > 0) {
    const packText = selectedPacks.length === 1
      ? `Pack '${selectedPacks[0]}' contains`
      : `Selected packs '${selectedPacks.join(", ")}' contain`;
    fail(`${packText} manual-only skills, but CodingAgent '${manualIncompatible[0]}' does not support manual-only skill invocation.
Manual-only skills must not be installed as auto-selectable skills.
Use a CodingAgent that supports manual-only skills or install core without the factory pack.`);
  }
}

module.exports = { install };
