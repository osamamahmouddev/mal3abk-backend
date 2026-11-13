import { VENUE_ID, expect, test, venue, waitForCall } from "./fixtures";

/** Photos can be uploaded files ("/api/v1/media/...") or full links (demo data): both must show and both must be removable. */

const EXTERNAL = "https://images.example.com/pitch.jpg?w=1200";
const UPLOADED = "/api/v1/media/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa/large";

test.beforeEach(async ({ api }) => {
  api.on("GET", /^\/api\/v1\/venue-manager\/venues$/, () => [{ ...venue, photos: [EXTERNAL, UPLOADED] }]);
  api.on("GET", /\/payment-methods$/, () => []);
});

test("a photo that is a full link is shown as it is, an uploaded one goes through the proxy", async ({ page }) => {
  await page.goto("/venue/settings");

  const images = page.getByRole("img", { name: "صورة الملعب" });
  await expect(images).toHaveCount(2);
  await expect(images.nth(0)).toHaveAttribute("src", EXTERNAL);
  await expect(images.nth(1)).toHaveAttribute("src", `/venue/bff${UPLOADED}`);
});

test("removing a linked photo saves the venue without it instead of calling the gallery endpoint", async ({ page, api }) => {
  api.on("PUT", new RegExp(`^/api/v1/venue-manager/venues/${VENUE_ID}$`), () => ({ ...venue, photos: [UPLOADED] }));
  await page.goto("/venue/settings");

  await page.getByRole("button", { name: "مسح الصورة" }).first().click();

  const call = await waitForCall(page, api.calls, (c) => c.method === "PUT" && c.path === `/api/v1/venue-manager/venues/${VENUE_ID}`);
  expect(call.body).toMatchObject({ photos: [UPLOADED], name: venue.name });
  expect(api.calls.some((c) => c.method === "DELETE")).toBe(false);
});

test("removing an uploaded photo uses the gallery endpoint with its media id", async ({ page, api }) => {
  api.on("DELETE", /\/gallery\/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa$/, () => ({ ...venue, photos: [EXTERNAL] }));
  await page.goto("/venue/settings");

  await page.getByRole("button", { name: "مسح الصورة" }).nth(1).click();

  await waitForCall(page, api.calls, (c) => c.method === "DELETE" && c.path.endsWith("/gallery/aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
});
