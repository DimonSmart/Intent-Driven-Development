const fs = require("fs");
const path = require("path");
const { contentRoot } = require("./content-root");
const { fail } = require("./errors");
const { copyDirectory, ensureNoUnknownArgs } = require("./fs-utils");

function initProject(args) {
  ensureNoUnknownArgs(args, ["--force"]);
  const force = args.includes("--force");
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

module.exports = { initProject };
