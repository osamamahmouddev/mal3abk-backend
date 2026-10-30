output "endpoint" {
  value = aws_db_instance.postgres.endpoint
}

output "address" {
  value = aws_db_instance.postgres.address
}

output "port" {
  value = aws_db_instance.postgres.port
}

output "db_name" {
  value = aws_db_instance.postgres.db_name
}

output "admin_username" {
  value = aws_db_instance.postgres.username
}

output "admin_password" {
  value     = random_password.admin_password.result
  sensitive = true
}

output "kms_key_arn" {
  value = aws_kms_key.postgres.arn
}
