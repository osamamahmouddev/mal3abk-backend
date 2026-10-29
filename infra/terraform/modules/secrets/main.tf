# ==============================================================================
# Secrets Manager Module: Centralized Secret Catalog, Least-Privilege Passwords
# ==============================================================================

resource "aws_kms_key" "secrets" {
  description             = "KMS Key for Malabak Secrets Manager ${var.environment}"
  deletion_window_in_days = 30
  enable_key_rotation     = true

  tags = {
    Name        = "malabak-${var.environment}-secrets-key"
    Environment = var.environment
  }
}

# Strong Random Passwords for Least-Privilege DB Roles
resource "random_password" "app_db_password" {
  length  = 32
  special = false
}

resource "random_password" "migrator_db_password" {
  length  = 32
  special = false
}

resource "random_password" "readonly_db_password" {
  length  = 32
  special = false
}

# 1. Database Connection Secret (Application Runtime - Least Privilege malabak_app)
resource "aws_secretsmanager_secret" "app_db" {
  name        = "malabak/${var.environment}/database/app"
  description = "Runtime connection string and credentials for malabak_app"
  kms_key_id  = aws_kms_key.secrets.arn

  tags = {
    Environment = var.environment
  }
}

resource "aws_secretsmanager_secret_version" "app_db" {
  secret_id = aws_secretsmanager_secret.app_db.id
  secret_string = jsonencode({
    host             = var.db_host
    port             = var.db_port
    dbname           = var.db_name
    username         = "malabak_app"
    password         = random_password.app_db_password.result
    connectionString = "Host=${var.db_host};Port=${var.db_port};Database=${var.db_name};Username=malabak_app;Password=${random_password.app_db_password.result};SslMode=Require;"
  })
}

# 2. Database Migration Secret (CI/CD Deploy Pipeline - malabak_migrator)
resource "aws_secretsmanager_secret" "migrator_db" {
  name        = "malabak/${var.environment}/database/migrator"
  description = "Migration credentials for malabak_migrator DDL runner"
  kms_key_id  = aws_kms_key.secrets.arn

  tags = {
    Environment = var.environment
  }
}

resource "aws_secretsmanager_secret_version" "migrator_db" {
  secret_id = aws_secretsmanager_secret.migrator_db.id
  secret_string = jsonencode({
    host             = var.db_host
    port             = var.db_port
    dbname           = var.db_name
    username         = "malabak_migrator"
    password         = random_password.migrator_db_password.result
    connectionString = "Host=${var.db_host};Port=${var.db_port};Database=${var.db_name};Username=malabak_migrator;Password=${random_password.migrator_db_password.result};SslMode=Require;"
  })
}

# 3. Database Readonly Secret (Analytics & Auditing - malabak_readonly)
resource "aws_secretsmanager_secret" "readonly_db" {
  name        = "malabak/${var.environment}/database/readonly"
  description = "Readonly credentials for analytical queries and reports"
  kms_key_id  = aws_kms_key.secrets.arn

  tags = {
    Environment = var.environment
  }
}

resource "aws_secretsmanager_secret_version" "readonly_db" {
  secret_id = aws_secretsmanager_secret.readonly_db.id
  secret_string = jsonencode({
    host             = var.db_host
    port             = var.db_port
    dbname           = var.db_name
    username         = "malabak_readonly"
    password         = random_password.readonly_db_password.result
    connectionString = "Host=${var.db_host};Port=${var.db_port};Database=${var.db_name};Username=malabak_readonly;Password=${random_password.readonly_db_password.result};SslMode=Require;"
  })
}

# 4. Redis Cache & Rate Limiting Secret
resource "aws_secretsmanager_secret" "redis" {
  name        = "malabak/${var.environment}/redis"
  description = "Redis endpoint, port and auth token"
  kms_key_id  = aws_kms_key.secrets.arn

  tags = {
    Environment = var.environment
  }
}

resource "aws_secretsmanager_secret_version" "redis" {
  secret_id = aws_secretsmanager_secret.redis.id
  secret_string = jsonencode({
    host             = var.redis_host
    port             = var.redis_port
    authToken        = var.redis_auth_token
    connectionString = "${var.redis_host}:${var.redis_port},password=${var.redis_auth_token},ssl=True,abortConnect=False"
  })
}

# 5. JWT ES256 Elliptic Curve Key Pair Secret
resource "tls_private_key" "jwt_signing_key" {
  algorithm   = "ECDSA"
  ecdsa_curve = "P256"
}

resource "aws_secretsmanager_secret" "jwt_keys" {
  name        = "malabak/${var.environment}/jwt"
  description = "JWT ES256 private and public signing keys"
  kms_key_id  = aws_kms_key.secrets.arn

  tags = {
    Environment = var.environment
  }
}

resource "aws_secretsmanager_secret_version" "jwt_keys" {
  secret_id = aws_secretsmanager_secret.jwt_keys.id
  secret_string = jsonencode({
    privateKeyPem = tls_private_key.jwt_signing_key.private_key_pem
    publicKeyPem  = tls_private_key.jwt_signing_key.public_key_pem
    keyId         = "malabak-${var.environment}-key-1"
  })
}
