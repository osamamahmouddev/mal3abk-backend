# ==============================================================================
# Cloudflare Zero Trust Access (TASK-720)
# admin.*, staging.*, the Hangfire dashboard and internal Swagger are only reachable
# through Access: staff sign in with SSO (Google Workspace) or a one-time PIN sent to an
# allowed email, MFA is required for the admin console, and an optional IP allowlist
# can be added on top. The API also verifies the signed Cf-Access-Jwt-Assertion for
# /hangfire and /swagger (CloudflareAccess:Audiences = the AUD tags output below).
# ==============================================================================

# --- Identity providers --------------------------------------------------------

# One-time PIN to an allowed email: works without any SSO set up.
resource "cloudflare_access_identity_provider" "otp" {
  account_id = var.cloudflare_account_id
  name       = "Malabak one-time PIN"
  type       = "onetimepin"
}

# Google Workspace SSO, only created when credentials are provided.
resource "cloudflare_access_identity_provider" "google" {
  count      = var.google_sso_client_id == "" ? 0 : 1
  account_id = var.cloudflare_account_id
  name       = "Malabak Google Workspace"
  type       = "google-apps"

  config {
    client_id     = var.google_sso_client_id
    client_secret = var.google_sso_client_secret
    apps_domain   = var.staff_email_domain
  }
}

locals {
  access_idps = concat(
    [cloudflare_access_identity_provider.otp.id],
    [for idp in cloudflare_access_identity_provider.google : idp.id]
  )
}

# --- Who counts as staff -------------------------------------------------------

resource "cloudflare_access_group" "staff" {
  account_id = var.cloudflare_account_id
  name       = "Malabak staff"

  include {
    email = var.admin_allowed_emails
  }

  include {
    email_domain = [var.staff_email_domain]
  }
}

resource "cloudflare_access_group" "engineering" {
  account_id = var.cloudflare_account_id
  name       = "Malabak engineering"

  include {
    email = var.engineering_allowed_emails
  }
}

# --- Applications --------------------------------------------------------------

resource "cloudflare_access_application" "admin_portal_access" {
  account_id                = var.cloudflare_account_id
  name                      = "Malabak Admin Console"
  domain                    = "admin.${var.domain_name}"
  type                      = "self_hosted"
  session_duration          = "8h"
  auto_redirect_to_identity = length(local.access_idps) == 1
  allowed_idps              = local.access_idps
  app_launcher_visible      = true

  cors_headers {
    allowed_methods   = ["GET", "POST", "PUT", "DELETE", "OPTIONS"]
    allowed_origins   = ["https://admin.${var.domain_name}"]
    allow_all_headers = true
  }
}

resource "cloudflare_access_application" "staging" {
  account_id       = var.cloudflare_account_id
  name             = "Malabak Staging"
  domain           = "staging.${var.domain_name}"
  type             = "self_hosted"
  session_duration = "24h"
  allowed_idps     = local.access_idps
}

# Path-scoped applications on the API hostname: only these paths are gated, the public API is not.
resource "cloudflare_access_application" "hangfire" {
  account_id       = var.cloudflare_account_id
  name             = "Malabak Hangfire Dashboard"
  domain           = "api.${var.domain_name}/hangfire"
  type             = "self_hosted"
  session_duration = "1h"
  allowed_idps     = local.access_idps
}

resource "cloudflare_access_application" "swagger" {
  account_id       = var.cloudflare_account_id
  name             = "Malabak Internal Swagger"
  domain           = "staging-api.${var.domain_name}/swagger"
  type             = "self_hosted"
  session_duration = "8h"
  allowed_idps     = local.access_idps
}

# --- Policies ------------------------------------------------------------------

resource "cloudflare_access_policy" "admin_allowed_users" {
  account_id     = var.cloudflare_account_id
  application_id = cloudflare_access_application.admin_portal_access.id
  name           = "Malabak staff with MFA"
  decision       = "allow"
  precedence     = 1

  include {
    group = [cloudflare_access_group.staff.id]
  }

  require {
    auth_method = "mfa"
  }

  dynamic "require" {
    for_each = length(var.access_ip_allowlist) > 0 ? [1] : []
    content {
      ip = var.access_ip_allowlist
    }
  }
}

resource "cloudflare_access_policy" "staging_staff" {
  account_id     = var.cloudflare_account_id
  application_id = cloudflare_access_application.staging.id
  name           = "Malabak staff"
  decision       = "allow"
  precedence     = 1

  include {
    group = [cloudflare_access_group.staff.id]
  }

  dynamic "require" {
    for_each = length(var.access_ip_allowlist) > 0 ? [1] : []
    content {
      ip = var.access_ip_allowlist
    }
  }
}

resource "cloudflare_access_policy" "hangfire_engineering" {
  account_id     = var.cloudflare_account_id
  application_id = cloudflare_access_application.hangfire.id
  name           = "Engineering only"
  decision       = "allow"
  precedence     = 1

  include {
    group = [cloudflare_access_group.engineering.id]
  }

  dynamic "require" {
    for_each = length(var.access_ip_allowlist) > 0 ? [1] : []
    content {
      ip = var.access_ip_allowlist
    }
  }
}

resource "cloudflare_access_policy" "swagger_engineering" {
  account_id     = var.cloudflare_account_id
  application_id = cloudflare_access_application.swagger.id
  name           = "Engineering only"
  decision       = "allow"
  precedence     = 1

  include {
    group = [cloudflare_access_group.engineering.id]
  }
}
