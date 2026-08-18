import { defineConfig, devices } from "@playwright/test";

const port = 5192;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL: `http://127.0.0.1:${port}`,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: {
    command:
      "dotnet run --no-build --no-launch-profile --configuration Release --project ../src/GiftCardCardholder.Web",
    env: {
      ASPNETCORE_ENVIRONMENT: "Development",
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      Backend__BaseUrl: "http://127.0.0.1:59999",
      BrowserChecks__SkipSessionStoreMaintenance: "true",
      ConnectionStrings__Cardholder:
        "Host=127.0.0.1;Port=1;Database=browser_checks_unused;Username=unused;Password=unused;Timeout=1",
    },
    reuseExistingServer: false,
    timeout: 60_000,
    url: `http://127.0.0.1:${port}/signin`,
  },
  projects: [
    {
      name: "firefox",
      use: { ...devices["Desktop Firefox"] },
    },
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
    {
      name: "mobile-chromium",
      use: { ...devices["Pixel 7"] },
    },
  ],
  reporter: [["list"], ["html", { open: "never" }]],
});
