# ==============================================================================
# Cloudflare WAF: Managed Rulesets, OWASP, Rate Limiting & Bot Defense
# ==============================================================================

# 1. Cloudflare Managed Ruleset (OWASP Core Ruleset & Application Security)
resource "cloudflare_ruleset" "waf_managed_rules" {
  zone_id     = var.cloudflare_zone_id
  name        = "Malabak WAF Managed Ruleset"
  description = "Managed rulesets for OWASP Core Ruleset and Cloudflare Specialized Protections"
  kind        = "zone"
  phase       = "http_request_firewall_managed"

  # Cloudflare Managed Ruleset
  rules {
    action = "execute"
    action_parameters {
      id = "efb79d8ae9a94d03a96e80711fb19182" # Cloudflare Managed Ruleset ID
    }
    expression  = "true"
    description = "Execute Cloudflare Managed Ruleset"
    enabled     = true
  }

  # OWASP Core Ruleset
  rules {
    action = "execute"
    action_parameters {
      id = "4814384a9e5d4991b98157d17e5792f8" # Cloudflare OWASP Core Ruleset ID
      overrides {
        categories {
          category = "paranoia-level-1"
          action   = "block"
          enabled  = true
        }
      }
    }
    expression  = "true"
    description = "Execute OWASP Core Ruleset with Paranoia Level 1 block"
    enabled     = true
  }
}

# 2. Edge Rate Limiting Ruleset
resource "cloudflare_ruleset" "rate_limiting_rules" {
  zone_id     = var.cloudflare_zone_id
  name        = "Malabak Rate Limiting Ruleset"
  description = "Edge rate limiting rules protecting OTP, Auth, and API endpoints"
  kind        = "zone"
  phase       = "http_ratelimit"

  # Rule A: Sensitive OTP Generation Rate Limit (3 requests per minute per IP)
  rules {
    action = "block"
    action_parameters {
      response {
        status_code  = 429
        content_type = "application/problem+json"
        content = jsonencode({
          type      = "https://malabak.app/errors/rate-limit-exceeded"
          title     = "Too Many Requests"
          status    = 429
          errorCode = "RATE_LIMIT_EXCEEDED"
          detail    = "Too many OTP requests from this IP address. Please wait 60 seconds."
        })
      }
    }
    ratelimit {
      characteristics     = ["cf.colo.id", "ip.src"]
      period              = 60
      requests_per_period = 3
      mitigation_timeout  = 60
    }
    expression  = "http.request.uri.path eq \"/api/v1/auth/send-otp\" and http.request.method eq \"POST\""
    description = "Rate limit OTP requests to 3 per minute per IP"
    enabled     = true
  }

  # Rule B: Global API Rate Limit (300 requests per minute per IP)
  rules {
    action = "managed_challenge"
    ratelimit {
      characteristics     = ["cf.colo.id", "ip.src"]
      period              = 60
      requests_per_period = 300
      mitigation_timeout  = 60
    }
    expression  = "starts_with(http.request.uri.path, \"/api/\")"
    description = "Challenge excessive requests exceeding 300 per minute per IP"
    enabled     = true
  }
}

# 3. Custom Firewall Rules (Direct IP rejection, Tor Exit challenge, Threat score)
resource "cloudflare_ruleset" "custom_firewall_rules" {
  zone_id     = var.cloudflare_zone_id
  name        = "Malabak Custom Security Ruleset"
  description = "Custom security filters for bot threat mitigation and origin protection"
  kind        = "zone"
  phase       = "http_request_firewall_custom"

  # Challenge automated bots with threat score > 20 on authentication endpoints
  rules {
    action      = "managed_challenge"
    expression  = "starts_with(http.request.uri.path, \"/api/v1/auth/\") and cf.threat_score gt 20"
    description = "Managed Challenge suspicious clients hitting auth endpoints"
    enabled     = true
  }

  # Challenge Tor Exit Nodes targeting non-public API endpoints
  rules {
    action      = "managed_challenge"
    expression  = "cf.client.bot or ip.geoip.asnum eq 208311 or ip.geoip.asnum eq 62744"
    description = "Managed Challenge high-risk anonymous proxies and Tor exits"
    enabled     = true
  }
}
