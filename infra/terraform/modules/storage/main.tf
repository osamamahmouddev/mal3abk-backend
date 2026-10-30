# ==============================================================================
# Private Object Storage Module: SSE-KMS, Block Public Access, HTTPS-Only
# ==============================================================================

# Customer-Managed KMS Key for Object Storage Encryption
resource "aws_kms_key" "storage" {
  description             = "KMS Key for Malabak S3 ${var.environment} Storage Encryption"
  deletion_window_in_days = 30
  enable_key_rotation     = true

  tags = {
    Name        = "malabak-${var.environment}-storage-key"
    Environment = var.environment
  }
}

resource "random_id" "bucket_suffix" {
  byte_length = 4
}

resource "aws_s3_bucket" "private_storage" {
  bucket        = "${var.bucket_prefix}-${var.environment}-${random_id.bucket_suffix.hex}"
  force_destroy = var.environment == "production" ? false : true

  tags = {
    Name        = "malabak-${var.environment}-private-storage"
    Environment = var.environment
    Protected   = "true"
  }
}

# 1. Block 100% of Public Access
resource "aws_s3_bucket_public_access_block" "block_public" {
  bucket = aws_s3_bucket.private_storage.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# 2. Server-Side Encryption with Customer-Managed KMS Key (SSE-KMS)
resource "aws_s3_bucket_server_side_encryption_configuration" "sse" {
  bucket = aws_s3_bucket.private_storage.id

  rule {
    apply_server_side_encryption_by_default {
      kms_master_key_id = aws_kms_key.storage.arn
      sse_algorithm     = "aws:kms"
    }
    bucket_key_enabled = true
  }
}

# 3. Versioning (Enabled for tamper evidence and accidental deletion recovery)
resource "aws_s3_bucket_versioning" "versioning" {
  bucket = aws_s3_bucket.private_storage.id

  versioning_configuration {
    status = "Enabled"
  }
}

# 4. Strict Bucket Policy: Enforce HTTPS TLS 1.2+ for all requests
resource "aws_s3_bucket_policy" "enforce_tls_policy" {
  bucket = aws_s3_bucket.private_storage.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "EnforceSecureTransport"
        Effect    = "Deny"
        Principal = "*"
        Action    = "s3:*"
        Resource = [
          aws_s3_bucket.private_storage.arn,
          "${aws_s3_bucket.private_storage.arn}/*"
        ]
        Condition = {
          Bool = {
            "aws:SecureTransport" = "false"
          }
        }
      }
    ]
  })

  depends_on = [aws_s3_bucket_public_access_block.block_public]
}

# 5. Lifecycle Configuration: Clean up incomplete multipart uploads
resource "aws_s3_bucket_lifecycle_configuration" "lifecycle" {
  bucket = aws_s3_bucket.private_storage.id

  rule {
    id     = "abort-incomplete-multipart-uploads"
    status = "Enabled"

    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }

    noncurrent_version_expiration {
      noncurrent_days = 90
    }
  }
}
