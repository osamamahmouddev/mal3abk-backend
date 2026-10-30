// Regenerates src/schema.d.ts from openapi.json. With --fetch, first downloads openapi.json from the
// running API (API_URL, default http://localhost:5000). With --check, fails if schema.d.ts is stale.
import { execFileSync } from "node:child_process";
import { readFileSync, writeFileSync, mkdtempSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const spec = join(root, "openapi.json");
const schema = join(root, "src", "schema.d.ts");
const args = new Set(process.argv.slice(2));

if (args.has("--fetch")) {
  const apiUrl = process.env.API_URL ?? "http://localhost:5000";
  const response = await fetch(`${apiUrl}/swagger/v1/swagger.json`);
  if (!response.ok) throw new Error(`GET ${apiUrl}/swagger/v1/swagger.json -> ${response.status}`);
  writeFileSync(spec, JSON.stringify(await response.json(), null, 1) + "\n");
}

const out = args.has("--check") ? join(mkdtempSync(join(tmpdir(), "api-client-")), "schema.d.ts") : schema;
execFileSync(join(root, "node_modules", ".bin", "openapi-typescript"), [spec, "-o", out], { stdio: "inherit" });

if (args.has("--check") && readFileSync(out, "utf8") !== readFileSync(schema, "utf8")) {
  console.error("src/schema.d.ts is out of date: run `pnpm --filter @malabak/api-client generate`.");
  process.exit(1);
}
