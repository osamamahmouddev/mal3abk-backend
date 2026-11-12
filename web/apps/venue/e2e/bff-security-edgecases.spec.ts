import { test, expect } from "@playwright/test";
import { sealData } from "iron-session";
import { SESSION_SECRET, VENUE_ID } from "./fixtures";

test.describe("BFF Security & Session Protection Edge Cases", () => {
  const createValidSessionCookie = async () => {
    return await sealData(
      {
        accessToken: "valid-mock-access-token",
        refreshToken: "valid-mock-refresh-token",
        accessExpiresAt: Date.now() + 3_600_000,
        user: { id: "44444444-4444-4444-8444-444444444444", name: "صاحب الملعب", roles: ["VenueOwner"] },
        csrf: "valid-csrf-secret-12345",
      },
      { password: SESSION_SECRET, ttl: 3600 },
    );
  };

  test("BFF proxy rejects unlisted routes with 404 Bff.PathNotAllowed to prevent SSRF and open proxy", async ({ request }) => {
    // 1. Arbitrary internal/external paths
    const evilResponse = await request.get("/venue/bff/evil-target-url");
    expect(evilResponse.status()).toBe(404);
    const evilBody = await evilResponse.json();
    expect(evilBody.title).toBe("Bff.PathNotAllowed");

    // 2. Admin routes called through Venue BFF
    const adminThroughVenueResponse = await request.get("/venue/bff/api/v1/admin/venues");
    expect(adminThroughVenueResponse.status()).toBe(404);
    const adminBody = await adminThroughVenueResponse.json();
    expect(adminBody.title).toBe("Bff.PathNotAllowed");
  });

  test("BFF proxy blocks anonymous access to protected venue endpoints with 401 Bff.NotSignedIn", async ({ request }) => {
    const unauthenticatedResponse = await request.get("/venue/bff/api/v1/venue-manager/venues");
    expect(unauthenticatedResponse.status()).toBe(401);
    const body = await unauthenticatedResponse.json();
    expect(body.title).toBe("Bff.NotSignedIn");
  });

  test("BFF proxy rejects cross-origin write requests with 403 Bff.CrossOriginRequest", async ({ request }) => {
    const sessionCookie = await createValidSessionCookie();

    const response = await request.post("/venue/bff/api/v1/venue-manager/venues", {
      headers: {
        Origin: "http://malicious-attacker.com",
        "X-CSRF-Token": "valid-csrf-secret-12345",
        Cookie: `malabak-venue=${sessionCookie}`,
      },
      data: { name: "Hacked Venue" },
    });

    expect(response.status()).toBe(403);
    const body = await response.json();
    expect(body.title).toBe("Bff.CrossOriginRequest");
  });

  test("BFF proxy rejects write requests with missing or invalid CSRF tokens with 403 Bff.InvalidCsrfToken", async ({ request, baseURL }) => {
    const sessionCookie = await createValidSessionCookie();

    // 1. Missing CSRF header
    const missingCsrfResponse = await request.post("/venue/bff/api/v1/venue-manager/venues", {
      headers: {
        Origin: baseURL || "http://localhost:3100",
        Cookie: `malabak-venue=${sessionCookie}`,
      },
      data: { name: "Test Venue" },
    });
    expect(missingCsrfResponse.status()).toBe(403);
    const missingBody = await missingCsrfResponse.json();
    expect(missingBody.title).toBe("Bff.InvalidCsrfToken");

    // 2. Wrong CSRF header
    const wrongCsrfResponse = await request.post("/venue/bff/api/v1/venue-manager/venues", {
      headers: {
        Origin: baseURL || "http://localhost:3100",
        "X-CSRF-Token": "tampered-csrf-token",
        Cookie: `malabak-venue=${sessionCookie}`,
      },
      data: { name: "Test Venue" },
    });
    expect(wrongCsrfResponse.status()).toBe(403);
    const wrongBody = await wrongCsrfResponse.json();
    expect(wrongBody.title).toBe("Bff.InvalidCsrfToken");
  });

  test("Web server sets strict security headers and suppresses identity headers", async ({ request }) => {
    const response = await request.get("/venue/login");
    expect(response.status()).toBe(200);

    const headers = response.headers();
    expect(headers["x-content-type-options"]).toBe("nosniff");
    expect(headers["x-frame-options"]).toBe("DENY");
    expect(headers["referrer-policy"]).toBe("strict-origin-when-cross-origin");
    expect(headers["server"]).toBeUndefined();
    expect(headers["x-powered-by"]).toBeUndefined();
  });
});
