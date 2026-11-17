const fs = require("fs");
const path = require("path");
const { compile, optimize, Polyfills } = require("@tailwindcss/node");
const { Scanner } = require("@tailwindcss/oxide");

async function main() {
  const frontendDir = path.resolve(__dirname, "..");
  const inputPath = path.join(frontendDir, "src/app/globals.css");
  const outputPath = path.join(frontendDir, "src/app/globals.generated.css");
  const css = fs.readFileSync(inputPath, "utf8");

  const compiler = await compile(css, {
    from: inputPath,
    base: path.dirname(inputPath),
    shouldRewriteUrls: true,
    onDependency: () => {},
    polyfills: Polyfills.All,
  });

  const sources = (
    compiler.root === "none"
      ? []
      : compiler.root === null
        ? [{ base: path.join(frontendDir, "src"), pattern: "**/*", negated: false }]
        : [{ ...compiler.root, negated: false }]
  ).concat(compiler.sources);

  const scanner = new Scanner({ sources });
  const candidates = [];
  for (const candidate of scanner.scan()) {
    candidates.push(candidate);
  }

  const built = compiler.build(candidates);
  const optimized = optimize(built, { minify: false }).code;
  fs.writeFileSync(outputPath, optimized, "utf8");
  console.log(`Compiled Tailwind CSS (${candidates.length} candidates) -> ${outputPath}`);
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
