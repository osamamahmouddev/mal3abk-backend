variable "environment" {
  type        = string
  description = "Deployment environment"
}

variable "bucket_prefix" {
  type        = string
  description = "Prefix for S3 bucket name"
  default     = "malabak-private-storage"
}
