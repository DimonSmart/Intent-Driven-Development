const fs = require("fs");
const path = require("path");
const { fail } = require("./errors");

function normalize(value) {
  return value.replace(/\\/g, "/");
}

function readRequired(filePath) {
  if (!fs.existsSync(filePath)) {
    fail(`Required bundled file not found: ${filePath}`);
  }

  return fs.readFileSync(filePath, "utf8");
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

module.exports = {
  normalize,
  readRequired,
  listFiles,
  copyDirectory,
  ensureNoUnknownArgs,
  valuesAfter,
  valueAfter
};
