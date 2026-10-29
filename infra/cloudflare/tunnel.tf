# ==============================================================================
# Cloudflare Tunnel (cloudflared) - Complete Origin IP Concealment
# ==============================================================================

resource "random_password" "tunnel_secret" {
  length  = 64
  special = false
}

resource "cloudflare_tunnel" "origin_tunnel" {
  account_id = var.cloudflare_account_id
  name       = "malabak-${var.environment}-tunnel"
  secret     = base64encode(random_password.tunnel_secret.result)
}

resource "cloudflare_tunnel_config" "origin_tunnel_config" {
  account_id = var.cloudflare_account_id
  tunnel_id  = cloudflare_tunnel.origin_tunnel.id

  config {
    warp_routing {
      enabled = true
    }

    # Ingress rules routing incoming hostnames to local backend services
    ingress_rule {
      hostname = "api.${var.domain_name}"
      service  = "http://localhost:5000"
      origin_request {
        http_host_header   = "api.${var.domain_name}"
        origin_server_name = "api.${var.domain_name}"
        connect_timeout    = "30s"
        no_tls_verify      = false
      }
    }

    ingress_rule {
      hostname = "admin.${var.domain_name}"
      service  = "http://localhost:3000"
      origin_request {
        http_host_header = "admin.${var.domain_name}"
        connect_timeout  = "30s"
      }
    }

    ingress_rule {
      hostname = "venue.${var.domain_name}"
      service  = "http://localhost:3001"
      origin_request {
        http_host_header = "venue.${var.domain_name}"
        connect_timeout  = "30s"
      }
    }

    ingress_rule {
      hostname = var.domain_name
      service  = "http://localhost:3002"
      origin_request {
        http_host_header = var.domain_name
        connect_timeout  = "30s"
      }
    }

    ingress_rule {
      hostname = "www.${var.domain_name}"
      service  = "http://localhost:3002"
      origin_request {
        http_host_header = var.domain_name
        connect_timeout  = "30s"
      }
    }

    # Catch-all rule returning 404 for unrecognized hostnames
    ingress_rule {
      service = "http_status:404"
    }
  }
}
