import { defineConfig, devices } from "@playwright/test";

/**
 * Admin console E2E (TASK-719). The console runs for real (Next.js dev server, BFF session cookie); its calls to the BFF are
 * answered from fixtures, so permission states, step-up confirmation and the request bodies are checked without the API.
 */
const PORT = 3110;

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
    url: `http://localhost:${PORT}/admin/login`,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: {
      SESSION_SECRET: "e2e-only-session-secret-0123456789abcdef",
      API_URL: "http://127.0.0.1:9",
      NEXT_PUBLIC_API_URL: "",
    },
  },
});
