# ==============================================================================
# Container Registry (ECR) Module: Immutable Tags & Automated Vulnerability Scan
# ==============================================================================

resource "aws_ecr_repository" "api" {
  name                 = "${var.repository_name}-${var.environment}"
  image_tag_mutability = "IMMUTABLE" # Prevents overwriting deployed release images

  image_scanning_configuration {
    scan_on_push = true # Automated CVE vulnerability scan on every push
  }

  encryption_configuration {
    encryption_type = "KMS"
  }

  tags = {
    Name        = "malabak-${var.environment}-ecr"
    Environment = var.environment
  }
}

resource "aws_ecr_lifecycle_policy" "api" {
  repository = aws_ecr_repository.api.name

  policy = jsonencode({
    rules = [
      {
        rulePriority = 1
        description  = "Keep last 30 tagged release images"
        selection = {
          tagStatus     = "tagged"
          tagPrefixList = ["v", "release-"]
          countType     = "imageCountMoreThan"
          countNumber   = 30
        }
        action = {
          type = "expire"
        }
      },
      {
        rulePriority = 2
        description  = "Expire untagged images older than 7 days"
        selection = {
          tagStatus   = "untagged"
          countType   = "sinceImagePushed"
          countUnit   = "days"
          countNumber = 7
        }
        action = {
          type = "expire"
        }
      }
    ]
  })
}
