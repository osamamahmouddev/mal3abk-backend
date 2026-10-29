output "vpc_id" {
  value = module.vpc.vpc_id
}

output "postgres_endpoint" {
  value = module.postgres.endpoint
}

output "redis_endpoint" {
  value = module.redis.primary_endpoint_address
}

output "storage_bucket_id" {
  value = module.storage.bucket_id
}

output "ecr_repository_url" {
  value = module.ecr.repository_url
}

output "app_db_secret_arn" {
  value = module.secrets.app_db_secret_arn
}
