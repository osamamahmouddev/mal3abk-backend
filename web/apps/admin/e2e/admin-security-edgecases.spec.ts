import { test, expect } from "@playwright/test";
import { sealData } from "iron-session";
import { SESSION_SECRET, ADMIN_ID } from "./fixtures";

test.describe("Admin Console BFF Security & Edge Cases", () => {
  const createAdminSessionCookie = async () => {
    return await sealData(
      {
        accessToken: "admin-access-token",
        refreshToken: "admin-refresh-token",
        accessExpiresAt: Date.now() + 3_600_000,
        user: { id: ADMIN_ID, name: "مدير النظام", roles: ["Admin"] },
        csrf: "admin-csrf-secret-12345",
      },
      { password: SESSION_SECRET, ttl: 3600 },
    );
  };

  test("Admin BFF rejects paths outside whitelisted admin routes with 404 Bff.PathNotAllowed", async ({ request }) => {
    // 1. Non-admin venue manager routes
    const venueResponse = await request.get("/admin/bff/api/v1/venue-manager/venues");
    expect(venueResponse.status()).toBe(404);
    const venueBody = await venueResponse.json();
    expect(venueBody.title).toBe("Bff.PathNotAllowed");

    // 2. Arbitrary external/internal SSRF paths
    const arbitraryResponse = await request.get("/admin/bff/internal-admin-target");
    expect(arbitraryResponse.status()).toBe(404);
    const arbitraryBody = await arbitraryResponse.json();
    expect(arbitraryBody.title).toBe("Bff.PathNotAllowed");
  });

  test("Admin BFF blocks unauthenticated access to admin endpoints with 401 Bff.NotSignedIn", async ({ request }) => {
    const unauthenticatedResponse = await request.get("/admin/bff/api/v1/admin/dashboard");
    expect(unauthenticatedResponse.status()).toBe(401);
    const body = await unauthenticatedResponse.json();
    expect(body.title).toBe("Bff.NotSignedIn");
  });

  test("Admin BFF enforces CSRF on state-changing requests", async ({ request, baseURL }) => {
    const sessionCookie = await createAdminSessionCookie();

    // 1. Missing CSRF header
    const missingCsrfResponse = await request.post("/admin/bff/api/v1/admin/plans", {
      headers: {
        Origin: baseURL || "http://localhost:3110",
        Cookie: `malabak-admin=${sessionCookie}`,
      },
      data: { name: "Pro Plan", price: 1000 },
    });
    expect(missingCsrfResponse.status()).toBe(403);
    const missingBody = await missingCsrfResponse.json();
    expect(missingBody.title).toBe("Bff.InvalidCsrfToken");

    // 2. Cross-origin request
    const crossOriginResponse = await request.post("/admin/bff/api/v1/admin/plans", {
      headers: {
        Origin: "http://attacker-site.com",
        "X-CSRF-Token": "admin-csrf-secret-12345",
        Cookie: `malabak-admin=${sessionCookie}`,
      },
      data: { name: "Pro Plan", price: 1000 },
    });
    expect(crossOriginResponse.status()).toBe(403);
    const crossOriginBody = await crossOriginResponse.json();
    expect(crossOriginBody.title).toBe("Bff.CrossOriginRequest");
  });

  test("Admin console returns strict security headers and suppresses identity headers", async ({ request }) => {
    const response = await request.get("/admin/login");
    expect(response.status()).toBe(200);

    const headers = response.headers();
    expect(headers["x-content-type-options"]).toBe("nosniff");
    expect(headers["x-frame-options"]).toBe("DENY");
    expect(headers["referrer-policy"]).toBe("strict-origin-when-cross-origin");
    expect(headers["server"]).toBeUndefined();
    expect(headers["x-powered-by"]).toBeUndefined();
  });
});
