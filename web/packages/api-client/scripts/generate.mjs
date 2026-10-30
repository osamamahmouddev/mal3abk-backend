// Regenerates src/schema.d.ts from the API contract, docs/openapi/v1.json. That file is written by the
// API's OpenApiContractIntegrationTests (dotnet test), and CI fails when it is stale. With --check, this
// fails when schema.d.ts no longer matches it.
import { execFileSync } from "node:child_process";
import { readFileSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const spec = join(root, "..", "..", "..", "docs", "openapi", "v1.json");
const schema = join(root, "src", "schema.d.ts");
const check = process.argv.includes("--check");

const out = check ? join(mkdtempSync(join(tmpdir(), "api-client-")), "schema.d.ts") : schema;
execFileSync(join(root, "node_modules", ".bin", "openapi-typescript"), [spec, "-o", out], { stdio: "inherit" });

if (check && readFileSync(out, "utf8") !== readFileSync(schema, "utf8")) {
  console.error("src/schema.d.ts is out of date: run `pnpm --filter @malabak/api-client generate`.");
  process.exit(1);
}
