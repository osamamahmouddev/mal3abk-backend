variable "environment" {
  type        = string
  description = "Deployment environment"
}

variable "repository_name" {
  type        = string
  description = "Name of the ECR repository"
  default     = "malabak/api"
}
