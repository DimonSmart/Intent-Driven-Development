const path = require("path");

const packageRoot = path.resolve(__dirname, "..");
const contentRoot = path.join(packageRoot, "package-content");
const packageJsonPath = path.join(packageRoot, "package.json");

module.exports = {
  packageRoot,
  contentRoot,
  packageJsonPath
};
