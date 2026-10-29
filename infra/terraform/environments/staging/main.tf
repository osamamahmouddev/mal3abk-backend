# ==============================================================================
# Malabak Staging Environment Infrastructure Root
# ==============================================================================

module "vpc" {
  source      = "../../modules/vpc"
  environment = var.environment
  vpc_cidr    = "10.10.0.0/16"
}

module "postgres" {
  source                = "../../modules/postgres"
  environment           = var.environment
  vpc_id                = module.vpc.vpc_id
  subnet_ids            = module.vpc.private_data_subnet_ids
  app_security_group_id = module.vpc.app_security_group_id
  instance_class        = "db.t4g.medium"
  allocated_storage     = 30
}

module "redis" {
  source                = "../../modules/redis"
  environment           = var.environment
  vpc_id                = module.vpc.vpc_id
  subnet_ids            = module.vpc.private_data_subnet_ids
  app_security_group_id = module.vpc.app_security_group_id
  node_type             = "cache.t4g.small"
  num_cache_clusters    = 1
}

module "storage" {
  source        = "../../modules/storage"
  environment   = var.environment
  bucket_prefix = "malabak-staging-storage"
}

module "ecr" {
  source      = "../../modules/ecr"
  environment = var.environment
}

module "secrets" {
  source           = "../../modules/secrets"
  environment      = var.environment
  db_host          = module.postgres.address
  db_port          = module.postgres.port
  db_name          = module.postgres.db_name
  redis_host       = module.redis.primary_endpoint_address
  redis_port       = module.redis.port
  redis_auth_token = module.redis.auth_token
}
