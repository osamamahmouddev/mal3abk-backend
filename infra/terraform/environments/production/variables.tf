variable "aws_region" {
  type        = string
  description = "AWS region for deployment"
  default     = "me-central-1"
}

variable "environment" {
  type        = string
  description = "Environment name"
  default     = "production"
}
