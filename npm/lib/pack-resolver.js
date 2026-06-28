const { fail } = require("./errors");

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

module.exports = {
  resolvePacks,
  isDefaultPackSelection,
  selectedSkills
};
