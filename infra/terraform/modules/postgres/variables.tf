variable "environment" {
  type        = string
  description = "Deployment environment"
}

variable "vpc_id" {
  type        = string
  description = "VPC ID"
}

variable "subnet_ids" {
  type        = list(string)
  description = "Isolated private data subnet IDs"
}

variable "app_security_group_id" {
  type        = string
  description = "Security group ID of the application tier allowed to connect"
}

variable "instance_class" {
  type        = string
  description = "RDS instance class"
  default     = "db.t4g.medium"
}

variable "allocated_storage" {
  type        = number
  description = "Allocated storage in GB"
  default     = 50
}

variable "max_allocated_storage" {
  type        = number
  description = "Max storage auto-scaling threshold in GB"
  default     = 200
}

variable "database_name" {
  type        = string
  description = "Database name"
  default     = "malabak"
}

variable "admin_username" {
  type        = string
  description = "Initial master admin username"
  default     = "malabak_root"
}
