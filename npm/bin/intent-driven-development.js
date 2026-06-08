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

  if (command === "list-targets") {
    readManifest().targets.forEach((target) => console.log(target));
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
  intent-driven-development install --target <target> [--entry minimal|none|full] [--force]
  intent-driven-development install --all [--entry minimal|none|full] [--force]
  intent-driven-development list-targets
  intent-driven-development version`);
}

function readManifest() {
  if (!fs.existsSync(manifestPath)) {
    fail(`Bundled manifest not found: ${manifestPath}`);
  }

  return JSON.parse(fs.readFileSync(manifestPath, "utf8"));
}

function install(args) {
  ensureNoUnknownArgs(args, ["--target", "--all", "--entry", "--force"]);

  const manifest = readManifest();
  const force = args.includes("--force");
  const installAll = args.includes("--all");
  const target = valueAfter(args, "--target");
  const entryMode = parseEntryMode(valueAfter(args, "--entry"));

  if (installAll && target) {
    fail("Use either --all or --target <target>, not both.");
  }

  if (!installAll && !target) {
    fail("Missing target. Use --target <target> or --all.");
  }

  const targets = installAll ? manifest.targets : [validateTarget(manifest, target)];
  validateEntryModeCapabilities(manifest, targets, entryMode, installAll);
  const plannedFiles = collectTargetFiles(manifest, targets, entryMode);
  copyPlannedFiles(plannedFiles, process.cwd(), force);
  console.log(`Installed ${targets.join(", ")} with ${entryMode} entry.`);
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

function validateTarget(manifest, target) {
  if (manifest.targets.includes(target)) {
    return target;
  }

  fail(`Unknown target: ${target}\nAvailable targets: ${manifest.targets.join(", ")}`);
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

function validateEntryModeCapabilities(manifest, targets, entryMode, installAll) {
  if (entryMode !== "none") {
    return;
  }

  if (!manifest.targetCapabilities) {
    fail("Bundled manifest does not define targetCapabilities.");
  }

  const incompatible = targets.filter((target) => !supportsGeneratedSkills(manifest, target));
  if (incompatible.length === 0) {
    return;
  }

  if (installAll) {
    fail(`The following targets do not support generated skills: ${incompatible.join(", ")}.
--entry none would install no entry point and no skills for those targets.
Use --entry minimal or install skill-capable targets explicitly.`);
  }

  fail(`Target ${incompatible[0]} does not support generated skills. --entry none would install no entry point and no skills.
Use --entry minimal or --entry full for this target.`);
}

function supportsGeneratedSkills(manifest, target) {
  const capabilities = manifest.targetCapabilities && manifest.targetCapabilities[target];
  if (!capabilities) {
    fail(`Bundled manifest does not define targetCapabilities for target: ${target}`);
  }

  return capabilities.supportsSkills === true;
}

function collectTargetFiles(manifest, targets, entryMode) {
  const byRelativePath = new Map();

  for (const target of targets) {
    const sourceRoot = path.join(contentRoot, "generated", target);
    if (!fs.existsSync(sourceRoot)) {
      fail(`Bundled generated target not found: ${target}`);
    }

    for (const file of listFiles(sourceRoot)) {
      const relativePath = normalize(path.relative(sourceRoot, file));
      if (
        entryMode !== "minimal" &&
        manifest.entryPoints[target] &&
        relativePath === normalize(manifest.entryPoints[target])
      ) {
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

    if (entryMode === "full") {
      const fullEntry = buildFullEntry(manifest, target);
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

  const projectFilesRoot = path.join(contentRoot, "src", "canonical", "project-files", "specs");
  if (fs.existsSync(projectFilesRoot)) {
    for (const file of listFiles(projectFilesRoot)) {
      const relativePath = normalize(path.join(".specs", path.relative(projectFilesRoot, file)));
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

  return Array.from(byRelativePath.values());
}

function buildFullEntry(manifest, target) {
  const entryPoint = manifest.entryPoints[target];
  if (!entryPoint) {
    fail(`No entry point configured for target: ${target}`);
  }

  const blocks = [
    readRequired(path.join(contentRoot, "src", "adapters", target, "entry.md")),
    readRequired(path.join(contentRoot, "src", "canonical", "packs", "intent-driven-development.md"))
      .replace("{{skillGuidance}}", "Use the generated IDD skills when they are available for the target.")
      .replace("{{workflowGuidance}}", "This file and installed IDD skills are workflow guidance.\nThey are not product specifications."),
    readCanonicalMethodology()
  ];

  const content = `${blocks.map((block) => block.trim()).join("\n\n")}\n`;
  const buffer = Buffer.from(content, "utf8");
  return {
    relativePath: normalize(entryPoint),
    sourcePath: null,
    content: buffer,
    hash: sha256(buffer)
  };
}

function readCanonicalMethodology() {
  const methodologyRoot = path.join(contentRoot, "src", "canonical", "methodology");
  const names = [
    "intent-driven-development.md",
    "numbering.md",
    "document-types.md",
    "semantic-changes.md",
    "agent-workflow.md"
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

    if (arg === "--target" || arg === "--entry") {
      index += 1;
    }
  }
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
