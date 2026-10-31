import { defineConfig, devices } from "@playwright/test";

/**
 * Venue dashboard E2E (TASK-512). The dashboard runs for real (Next.js dev server, middleware, BFF session cookie); the
 * browser's calls to the BFF are answered from fixtures, so the flows run in CI without the API and its database.
 */
const PORT = 3100;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["github"], ["list"]] : "list",
  timeout: 60_000,
  use: {
    baseURL: `http://localhost:${PORT}`,
    locale: "ar-EG",
    timezoneId: "Africa/Cairo",
    trace: "retain-on-failure",
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    { name: "phone", use: { ...devices["Pixel 7"] } },
  ],
  webServer: {
    command: `pnpm exec next dev -p ${PORT}`,
    url: `http://localhost:${PORT}/venue/login`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      SESSION_SECRET: "e2e-only-session-secret-0123456789abcdef",
      API_URL: "http://127.0.0.1:9",
      NEXT_PUBLIC_API_URL: "",
    },
  },
});
