import { AxeBuilder } from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

async function expectNoAccessibilityViolations(page: Page) {
  const results = await new AxeBuilder({ page })
    .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
    .analyze();

  expect(results.violations).toEqual([]);
}

async function expectNoPageOverflow(page: Page) {
  const sizes = await page.evaluate(() => ({
    body: document.body.scrollWidth,
    document: document.documentElement.scrollWidth,
    viewport: document.documentElement.clientWidth,
  }));
  expect(Math.max(sizes.body, sizes.document)).toBeLessThanOrEqual(
    sizes.viewport + 1,
  );
}

test("English and Turkish sign-in are accessible and script-free", async ({
  page,
}) => {
  const response = await page.goto("/signin");
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await expect(page.getByRole("heading", { name: "Sign in" })).toBeVisible();
  await expect(page.getByLabel("Email address or phone number")).toBeVisible();
  await expect(page.getByLabel("Password")).toBeVisible();
  await expect(page.locator("script")).toHaveCount(0);
  expect(response?.headers()["content-security-policy"]).toContain(
    "script-src 'none'",
  );
  await expectNoAccessibilityViolations(page);

  await page.getByRole("button", { name: "Türkçe" }).click();
  await expect(page.locator("html")).toHaveAttribute("lang", "tr");
  await expect(page.getByRole("heading", { name: "Giriş yap" })).toBeVisible();
  await expect(
    page.getByLabel("E-posta adresi veya telefon numarası"),
  ).toBeVisible();
  await expect(page.getByLabel("Parola")).toBeVisible();
  await expect(page.getByRole("button", { name: "English" })).toBeVisible();
  await expectNoAccessibilityViolations(page);
});

test("keyboard skip link reaches main content with visible focus", async ({
  page,
}) => {
  await page.goto("/signin");
  await page.keyboard.press("Tab");
  const skipLink = page.getByRole("link", { name: "Skip to content" });
  await expect(skipLink).toBeFocused();
  await expect(skipLink).toBeVisible();
  await page.keyboard.press("Enter");
  await expect(page.locator("#main")).toBeFocused();
});

test("phone width and 200 percent zoom do not create page overflow", async ({
  page,
}) => {
  await page.setViewportSize({ width: 320, height: 720 });
  await page.goto("/signin");
  await expectNoPageOverflow(page);

  await page.evaluate(() => {
    document.documentElement.style.zoom = "2";
  });
  await expectNoPageOverflow(page);
  await expect(page.getByRole("button", { name: "Sign in" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Türkçe" })).toBeVisible();
});

test("invalid sharing links fail safely and accessibly", async ({ page }) => {
  for (const path of [
    "/share/claim?token=invalid",
    "/activate/share?token=invalid",
  ]) {
    await page.goto(path);
    await expect(page.getByRole("heading", { level: 1 })).toHaveCount(1);
    await expect(page.getByRole("alert")).toBeVisible();
    await expect(page.locator("script")).toHaveCount(0);
    await expectNoPageOverflow(page);
    await expectNoAccessibilityViolations(page);
  }
});
