const fs = require("fs");
const path = require("path");
const { contentRoot } = require("./content-root");
const { fail } = require("./errors");
const { normalize, listFiles } = require("./fs-utils");
const { sha256 } = require("./hash");
const { selectedSkills } = require("./pack-resolver");
const { buildEntry } = require("./entry-builder");

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

      addPlannedFile(byRelativePath, relativePath, file, null, fs.readFileSync(file));
    }

    if (entryMode !== "none") {
      addBuiltFile(byRelativePath, buildEntry(manifest, codingAgent, entryMode, selectedPacks));
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
        addPlannedFile(byRelativePath, relativePath, file, null, fs.readFileSync(file));
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

function addPlannedFile(byRelativePath, relativePath, sourcePath, content, hashSource) {
  addBuiltFile(byRelativePath, {
    relativePath,
    sourcePath,
    content,
    hash: sha256(hashSource)
  });
}

function addBuiltFile(byRelativePath, file) {
  const existing = byRelativePath.get(file.relativePath);
  if (existing) {
    if (existing.hash !== file.hash) {
      fail(`Conflicting bundled files for path: ${file.relativePath}`);
    }

    return;
  }

  byRelativePath.set(file.relativePath, file);
}

module.exports = { collectCodingAgentFiles };
