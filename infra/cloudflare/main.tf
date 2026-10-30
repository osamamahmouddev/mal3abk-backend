provider "cloudflare" {
  api_token = var.cloudflare_api_token
}

# Local variables for hostnames and tags
locals {
  root_domain   = var.domain_name
  api_domain    = "api.${var.domain_name}"
  admin_domain  = "admin.${var.domain_name}"
  venue_domain  = "venue.${var.domain_name}"
  web_domain    = var.domain_name
  www_domain    = "www.${var.domain_name}"

  tags = {
    Project     = "Malabak"
    Environment = var.environment
    ManagedBy   = "Terraform"
  }
}
