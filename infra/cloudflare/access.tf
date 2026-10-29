# ==============================================================================
# Cloudflare Zero Trust Access: Admin Dashboard Security & Least-Privilege RBAC
# ==============================================================================

# Zero Trust Application for Admin Dashboard
resource "cloudflare_access_application" "admin_portal_access" {
  account_id                = var.cloudflare_account_id
  name                      = "Malabak Admin Console"
  domain                    = "admin.${var.domain_name}"
  type                      = "self_hosted"
  session_duration          = "8h"
  auto_redirect_to_identity = true

  # Ensure CORS / options requests pass through for preflight if necessary
  cors_headers {
    allowed_methods  = ["GET", "POST", "PUT", "DELETE", "OPTIONS"]
    allowed_origins  = ["https://admin.${var.domain_name}"]
    allow_all_headers = true
  }
}

# Access Policy: Strictly allow authorized organization email addresses with mandatory 2FA
resource "cloudflare_access_policy" "admin_allowed_users" {
  account_id     = var.cloudflare_account_id
  application_id = cloudflare_access_application.admin_portal_access.id
  name           = "Enforce Malabak Staff With 2FA"
  decision       = "allow"
  precedence     = 1

  include {
    email = var.admin_allowed_emails
  }

  require {
    # Requires hardware security key / TOTP 2FA
    auth_method = "mfa"
  }
}
