# ==============================================================================
# Cloudflare TLS & SSL Configuration: Full (strict), HSTS, TLS 1.2+, Origin CA
# ==============================================================================

# Zone-wide SSL/TLS Settings
resource "cloudflare_zone_settings_override" "malabak_tls_settings" {
  zone_id = var.cloudflare_zone_id

  settings {
    # 1. Full (strict): Cloudflare validates the origin server certificate signed by Cloudflare Origin CA
    ssl = "strict"

    # 2. Always redirect HTTP to HTTPS
    always_use_https = "on"

    # 3. Enforce modern TLS minimum version 1.2
    min_tls_version = "1.2"

    # 4. Enable TLS 1.3
    tls_1_3 = "on"

    # 5. Disable 0-RTT replay (protect state-changing endpoints against replay attacks)
    zero_rtt = "off"

    # 6. Enable Automatic HTTPS Rewrites
    automatic_https_rewrites = "on"

    # 7. HTTP Strict Transport Security (HSTS) with 1-year max-age, subdomains, and preload
    security_header {
      strict_transport_security {
        enabled            = true
        max_age            = 31536000 # 1 year
        include_subdomains = true
        preload            = true
        nosniff            = true
      }
    }

    # 8. WebSockets enabled for real-time SignalR notifications and match bookings
    websockets = "on"

    # 9. HTTP/2 and HTTP/3 support
    http2 = "on"
    http3 = "on"

    # 10. Origin Error Page Pass-thru (ensures standard ProblemDetails reach client)
    origin_error_page_pass_thru = "on"
  }
}

# Origin CA Certificate request (valid for 15 years, used between Cloudflare Edge and Origin)
# This certificate is installed on the origin reverse proxy (Nginx / Caddy / Kestrel)
resource "tls_private_key" "origin_key" {
  algorithm = "RSA"
  rsa_bits  = 2048
}

resource "tls_cert_request" "origin_csr" {
  private_key_pem = tls_private_key.origin_key.private_key_pem

  subject {
    common_name  = var.domain_name
    organization = "Malabak Technologies SAE"
  }
}

resource "cloudflare_origin_ca_certificate" "origin_cert" {
  csr                = tls_cert_request.origin_csr.cert_request_pem
  hostnames          = [var.domain_name, "*.${var.domain_name}"]
  request_type       = "origin-rsa"
  requested_validity = 5475 # 15 years
}
