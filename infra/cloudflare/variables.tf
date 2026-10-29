variable "cloudflare_api_token" {
  description = "Cloudflare Scoped API Token with Zone.DNS, Zone.WAF, Zone.SSL, and Account.Tunnel permissions"
  type        = string
  sensitive   = true
}

variable "cloudflare_account_id" {
  description = "Cloudflare Account ID for Malabak organization"
  type        = string
}

variable "cloudflare_zone_id" {
  description = "Cloudflare Zone ID for malabak.app"
  type        = string
}

variable "domain_name" {
  description = "Root domain name for the platform"
  type        = string
  default     = "malabak.app"
}

variable "environment" {
  description = "Deployment environment (production or staging)"
  type        = string
  default     = "production"
}

variable "origin_ip" {
  description = "Direct origin IP address (only accessible via Cloudflare IP ranges or Tunnel)"
  type        = string
  default     = "10.0.1.10"
}

variable "admin_allowed_emails" {
  description = "Authorized Google/Okta email accounts allowed through Cloudflare Access for Admin portal"
  type        = list(string)
  default = [
    "admin@malabak.app",
    "security@malabak.app"
  ]
}

variable "enable_cloudflare_tunnel" {
  description = "Whether to route traffic strictly through Cloudflare Tunnel (cloudflared) with zero open inbound ports"
  type        = bool
  default     = true
}
