# ==============================================================================
# Managed Redis Module: Private Subnet, In-Transit & At-Rest Encryption, Auth
# ==============================================================================

resource "aws_elasticache_subnet_group" "redis" {
  name        = "malabak-${var.environment}-redis-subnet-group"
  subnet_ids  = var.subnet_ids
  description = "Isolated private data subnets for Redis caching and rate limiting"

  tags = {
    Name        = "malabak-${var.environment}-redis-subnet-group"
    Environment = var.environment
  }
}

resource "aws_security_group" "redis" {
  name        = "malabak-${var.environment}-redis-sg"
  description = "Allows inbound Redis traffic exclusively from application security group"
  vpc_id      = var.vpc_id

  ingress {
    description     = "Redis from App Tier"
    from_port       = 6379
    to_port         = 6379
    protocol        = "tcp"
    security_groups = [var.app_security_group_id]
  }

  tags = {
    Name        = "malabak-${var.environment}-redis-sg"
    Environment = var.environment
  }
}

# Cryptographically strong auth token (32 chars)
resource "random_password" "redis_auth_token" {
  length  = 32
  special = false
}

# Redis Replication Group
resource "aws_elasticache_replication_group" "redis" {
  replication_group_id = "malabak-${var.environment}-redis"
  description          = "Malabak Distributed Cache and Rate Limiter"
  engine               = "redis"
  engine_version       = "7.1"
  node_type            = var.node_type
  num_cache_clusters   = var.num_cache_clusters
  parameter_group_name = "default.redis7"
  port                 = 6379
  subnet_group_name    = aws_elasticache_subnet_group.redis.name
  security_group_ids   = [aws_security_group.redis.id]

  # Encryption & Security
  at_rest_encryption_enabled = true
  transit_encryption_enabled = true
  auth_token                 = random_password.redis_auth_token.result

  automatic_failover_enabled = var.environment == "production" ? true : false
  multi_az_enabled           = var.environment == "production" ? true : false
  auto_minor_version_upgrade = true
  maintenance_window         = "sun:03:00-sun:04:30"
  snapshot_retention_limit   = 7
  snapshot_window            = "01:30-02:30"

  tags = {
    Name        = "malabak-${var.environment}-redis"
    Environment = var.environment
  }
}
