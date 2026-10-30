# ==============================================================================
# Cloudflare DNS Baseline & DNSSEC Configuration
# ==============================================================================

# Enable DNSSEC with automated DS record generation
resource "cloudflare_zone_dnssec" "malabak_dnssec" {
  zone_id = var.cloudflare_zone_id
}

# 1. API Gateway Hostname (Proxied)
resource "cloudflare_record" "api" {
  zone_id = var.cloudflare_zone_id
  name    = "api"
  content = var.enable_cloudflare_tunnel ? "${cloudflare_tunnel.origin_tunnel.id}.cfargotunnel.com" : var.origin_ip
  type    = var.enable_cloudflare_tunnel ? "CNAME" : "A"
  proxied = true
  ttl     = 1 # Auto when proxied
  comment = "Malabak Production API Gateway - Always Proxied behind Cloudflare Edge"
}

# 2. Admin Portal Hostname (Proxied + Zero Trust Cloudflare Access)
resource "cloudflare_record" "admin" {
  zone_id = var.cloudflare_zone_id
  name    = "admin"
  content = var.enable_cloudflare_tunnel ? "${cloudflare_tunnel.origin_tunnel.id}.cfargotunnel.com" : var.origin_ip
  type    = var.enable_cloudflare_tunnel ? "CNAME" : "A"
  proxied = true
  ttl     = 1
  comment = "Malabak Admin Web Dashboard - Protected by Zero Trust Access"
}

# 3. Venue Portal Hostname (Proxied)
resource "cloudflare_record" "venue" {
  zone_id = var.cloudflare_zone_id
  name    = "venue"
  content = var.enable_cloudflare_tunnel ? "${cloudflare_tunnel.origin_tunnel.id}.cfargotunnel.com" : var.origin_ip
  type    = var.enable_cloudflare_tunnel ? "CNAME" : "A"
  proxied = true
  ttl     = 1
  comment = "Malabak Venue Owner & Staff Management Portal"
}

# 4. Root Domain Hostname (Proxied)
resource "cloudflare_record" "root" {
  zone_id = var.cloudflare_zone_id
  name    = "@"
  content = var.enable_cloudflare_tunnel ? "${cloudflare_tunnel.origin_tunnel.id}.cfargotunnel.com" : var.origin_ip
  type    = var.enable_cloudflare_tunnel ? "CNAME" : "A"
  proxied = true
  ttl     = 1
  comment = "Malabak Main Landing and Player Web Portal"
}

# 5. WWW Subdomain Hostname (Proxied)
resource "cloudflare_record" "www" {
  zone_id = var.cloudflare_zone_id
  name    = "www"
  content = var.domain_name
  type    = "CNAME"
  proxied = true
  ttl     = 1
  comment = "CNAME alias for root domain"
}

# Staging Hostnames (For Staging Environment)
resource "cloudflare_record" "staging_api" {
  zone_id = var.cloudflare_zone_id
  name    = "staging-api"
  content = var.enable_cloudflare_tunnel ? "${cloudflare_tunnel.origin_tunnel.id}.cfargotunnel.com" : var.origin_ip
  type    = var.enable_cloudflare_tunnel ? "CNAME" : "A"
  proxied = true
  ttl     = 1
  comment = "Malabak Staging API Gateway"
}

# Security TXT Records: SPF, DMARC (prevent origin IP leakage via mail)
resource "cloudflare_record" "spf" {
  zone_id = var.cloudflare_zone_id
  name    = "@"
  content = "v=spf1 include:_spf.google.com ~all"
  type    = "TXT"
  proxied = false
  ttl     = 3600
  comment = "Strict SPF record using Google Workspace - No origin server IP exposed"
}

resource "cloudflare_record" "dmarc" {
  zone_id = var.cloudflare_zone_id
  name    = "_dmarc"
  content = "v=DMARC1; p=reject; rua=mailto:dmarc-reports@${var.domain_name}; pct=100; adkim=s; aspf=s"
  type    = "TXT"
  proxied = false
  ttl     = 3600
  comment = "DMARC Reject Policy for Malabak Domain"
}
