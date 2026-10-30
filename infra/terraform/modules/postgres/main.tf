# ==============================================================================
# Managed PostgreSQL Module: PITR, KMS Encryption, TLS Enforcement & Isolation
# ==============================================================================

# Customer-Managed KMS Key for Database Storage Encryption
resource "aws_kms_key" "postgres" {
  description             = "KMS Key for Malabak PostgreSQL ${var.environment} Storage Encryption"
  deletion_window_in_days = 30
  enable_key_rotation     = true

  tags = {
    Name        = "malabak-${var.environment}-postgres-key"
    Environment = var.environment
  }
}

resource "aws_db_subnet_group" "postgres" {
  name        = "malabak-${var.environment}-postgres-subnet-group"
  subnet_ids  = var.subnet_ids
  description = "Isolated private data subnets for PostgreSQL"

  tags = {
    Name        = "malabak-${var.environment}-postgres-subnet-group"
    Environment = var.environment
  }
}

# Parameter Group: Enforce TLS 1.3 & Force SSL
resource "aws_db_parameter_group" "postgres" {
  name        = "malabak-${var.environment}-postgres17-params"
  family      = "postgres17"
  description = "Security parameters for Malabak PostgreSQL 17"

  parameter {
    name  = "rds.force_ssl"
    value = "1" # Enforce TLS connections strictly
  }

  parameter {
    name  = "ssl_min_protocol_version"
    value = "TLSv1.3"
  }

  parameter {
    name  = "log_connections"
    value = "1"
  }

  parameter {
    name  = "log_disconnections"
    value = "1"
  }

  parameter {
    name  = "log_duration"
    value = "0"
  }

  tags = {
    Environment = var.environment
  }
}

# Security Group: PostgreSQL Database Port 5432
resource "aws_security_group" "postgres" {
  name        = "malabak-${var.environment}-postgres-sg"
  description = "Allows inbound PostgreSQL traffic exclusively from application security group"
  vpc_id      = var.vpc_id

  ingress {
    description     = "PostgreSQL from App Tier"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [var.app_security_group_id]
  }

  tags = {
    Name        = "malabak-${var.environment}-postgres-sg"
    Environment = var.environment
  }
}

# Random password for master admin
resource "random_password" "admin_password" {
  length           = 32
  special          = true
  override_special = "!#$%&*()-_=+[]{}<>:?"
}

# Managed RDS PostgreSQL Instance
resource "aws_db_instance" "postgres" {
  identifier                  = "malabak-${var.environment}-db"
  engine                      = "postgres"
  engine_version              = "17.2"
  instance_class              = var.instance_class
  allocated_storage           = var.allocated_storage
  max_allocated_storage       = var.max_allocated_storage
  storage_type                = "gp3"
  storage_encrypted           = true
  kms_key_id                  = aws_kms_key.postgres.arn

  db_name                     = var.database_name
  username                    = var.admin_username
  password                    = random_password.admin_password.result

  db_subnet_group_name        = aws_db_subnet_group.postgres.name
  vpc_security_group_ids      = [aws_security_group.postgres.id]
  parameter_group_name        = aws_db_parameter_group.postgres.name

  # Point-in-Time Recovery (PITR) & Automated Backups
  backup_retention_period     = 30 # 30 days retention for continuous PITR
  backup_window               = "01:00-03:00"
  maintenance_window          = "Mon:03:30-Mon:05:00"
  copy_tags_to_snapshot       = true
  deletion_protection         = var.environment == "production" ? true : false
  skip_final_snapshot         = var.environment == "production" ? false : true
  final_snapshot_identifier   = "malabak-${var.environment}-db-final-snapshot"

  publicly_accessible         = false
  multi_az                    = var.environment == "production" ? true : false
  auto_minor_version_upgrade  = true

  tags = {
    Name        = "malabak-${var.environment}-postgres"
    Environment = var.environment
  }
}
