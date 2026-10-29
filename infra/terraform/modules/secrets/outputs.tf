output "app_db_secret_arn" {
  value = aws_secretsmanager_secret.app_db.arn
}

output "migrator_db_secret_arn" {
  value = aws_secretsmanager_secret.migrator_db.arn
}

output "readonly_db_secret_arn" {
  value = aws_secretsmanager_secret.readonly_db.arn
}

output "redis_secret_arn" {
  value = aws_secretsmanager_secret.redis.arn
}

output "jwt_secret_arn" {
  value = aws_secretsmanager_secret.jwt_keys.arn
}

output "app_db_password" {
  value     = random_password.app_db_password.result
  sensitive = true
}

output "migrator_db_password" {
  value     = random_password.migrator_db_password.result
  sensitive = true
}

output "readonly_db_password" {
  value     = random_password.readonly_db_password.result
  sensitive = true
}
