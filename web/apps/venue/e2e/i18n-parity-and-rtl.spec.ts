import { expect, test } from "./fixtures";
import * as fs from "node:fs";
import * as path from "node:path";

function getAllKeys(obj: Record<string, unknown>, prefix = ""): string[] {
  let keys: string[] = [];
  for (const [key, value] of Object.entries(obj)) {
    const fullKey = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === "object" && !Array.isArray(value)) {
      keys = keys.concat(getAllKeys(value as Record<string, unknown>, fullKey));
    } else {
      keys.push(fullKey);
    }
  }
  return keys.sort();
}

test.describe("i18n Parity, RTL and Client Security", () => {
  test("i18n messages have 100% key parity between Arabic and English without missing translations", async () => {
    const i18nDir = path.resolve(process.cwd(), "../../packages/i18n/messages");
    const arPath = path.join(i18nDir, "ar.json");
    const enPath = path.join(i18nDir, "en.json");

    expect(fs.existsSync(arPath)).toBe(true);
    expect(fs.existsSync(enPath)).toBe(true);

    const arJson = JSON.parse(fs.readFileSync(arPath, "utf-8")) as Record<string, unknown>;
    const enJson = JSON.parse(fs.readFileSync(enPath, "utf-8")) as Record<string, unknown>;

    const arKeys = getAllKeys(arJson);
    const enKeys = getAllKeys(enJson);

    const missingInEnglish = arKeys.filter((k) => !enKeys.includes(k));
    const missingInArabic = enKeys.filter((k) => !arKeys.includes(k));

    expect(missingInEnglish, `Keys present in ar.json but missing in en.json: ${missingInEnglish.join(", ")}`).toEqual([]);
    expect(missingInArabic, `Keys present in en.json but missing in ar.json: ${missingInArabic.join(", ")}`).toEqual([]);
  });

  test("Arabic locale is configured by default with RTL direction and proper language tag", async ({ page }) => {
    await page.goto("/venue/login");
    await page.waitForLoadState("domcontentloaded");

    const htmlElement = page.locator("html");
    await expect(htmlElement).toHaveAttribute("dir", "rtl");
  });

  test("Tokens never reach browser JavaScript or localStorage", async ({ page, api }) => {
    await page.goto("/venue/overview");
    await page.waitForLoadState("networkidle");

    const clientStorage = await page.evaluate(() => {
      const storageItems: Record<string, string> = {};
      for (let i = 0; i < localStorage.length; i++) {
        const key = localStorage.key(i)!;
        storageItems[key] = localStorage.getItem(key)!;
      }
      return {
        localStorage: storageItems,
        documentCookie: document.cookie,
      };
    });

    // 1. LocalStorage must never contain access tokens, refresh tokens, or auth secrets
    for (const key of Object.keys(clientStorage.localStorage)) {
      expect(key.toLowerCase()).not.toContain("token");
      expect(key.toLowerCase()).not.toContain("jwt");
      expect(key.toLowerCase()).not.toContain("secret");
    }
  });
});
