import { defineConfig, devices } from "@playwright/test";
export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  use: { baseURL: "http://127.0.0.1:5080", trace: "retain-on-failure" },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    {
      name: "mobile",
      use: { ...devices["iPhone 13"], defaultBrowserType: "chromium" },
    },
  ],
  webServer: {
    command:
      "dotnet run --project ../src/AutoPost.Api --urls http://127.0.0.1:5080",
    url: "http://127.0.0.1:5080/api/health",
    reuseExistingServer: !process.env.CI,
    env: {
      APP_MODE: "demo",
      ADMIN_TOKEN: "local-preview-token-at-least-32-characters",
      ASPNETCORE_ENVIRONMENT: "Development",
    },
    timeout: 120000,
  },
});
