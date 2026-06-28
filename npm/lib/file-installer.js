const fs = require("fs");
const path = require("path");
const { fail } = require("./errors");
const { sha256 } = require("./hash");

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

module.exports = { copyPlannedFiles };
