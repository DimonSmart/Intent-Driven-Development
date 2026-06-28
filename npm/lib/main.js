const fs = require("fs");
const { packageJsonPath } = require("./content-root");
const { printUsage } = require("./usage");
const { readManifest, codingAgents } = require("./manifest");
const { initProject } = require("./init-command");
const { install } = require("./install-command");
const { fail } = require("./errors");

function main(args) {
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
    initProject(args.slice(1));
    return;
  }

  if (command === "install") {
    install(args.slice(1));
    return;
  }

  fail(`Unknown command: ${command}`);
}

module.exports = { main };
