# ==============================================================================
# Cloudflare Authenticated Origin Pulls (mTLS Client Certificate Verification)
# ==============================================================================

# Enable Authenticated Origin Pulls at the zone level
resource "cloudflare_authenticated_origin_pulls" "malabak_origin_pulls" {
  zone_id = var.cloudflare_zone_id
  enabled = true
}

# Per-hostname Authenticated Origin Pulls for API gateway
resource "cloudflare_authenticated_origin_pulls_certificate" "api_origin_pull_cert" {
  zone_id     = var.cloudflare_zone_id
  certificate = cloudflare_origin_ca_certificate.origin_cert.certificate
  private_key = tls_private_key.origin_key.private_key_pem
  type        = "per-zone"
}
