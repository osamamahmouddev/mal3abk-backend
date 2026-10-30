output "tunnel_id" {
  description = "The Cloudflare Tunnel ID created for the platform"
  value       = cloudflare_tunnel.origin_tunnel.id
}

output "tunnel_token" {
  description = "Token to run the cloudflared service on the origin server"
  value       = cloudflare_tunnel.origin_tunnel.tunnel_token
  sensitive   = true
}

output "dnssec_status" {
  description = "Status of DNSSEC configuration"
  value       = cloudflare_zone_dnssec.malabak_dnssec.status
}

output "origin_ca_certificate" {
  description = "Origin CA certificate to install on the origin web server"
  value       = cloudflare_origin_ca_certificate.origin_cert.certificate
}
