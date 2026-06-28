const fs = require("fs");
const path = require("path");
const { contentRoot } = require("./content-root");
const { fail } = require("./errors");

function readManifest() {
  const manifestPath = path.join(contentRoot, "manifest.json");
  if (!fs.existsSync(manifestPath)) {
    fail(`Bundled manifest not found: ${manifestPath}`);
  }

  return JSON.parse(fs.readFileSync(manifestPath, "utf8"));
}

function codingAgents(manifest) {
  return manifest.codingAgents || manifest.targets || [];
}

function codingAgentCapabilities(manifest) {
  return manifest.codingAgentCapabilities || manifest.targetCapabilities;
}

module.exports = {
  readManifest,
  codingAgents,
  codingAgentCapabilities
};
