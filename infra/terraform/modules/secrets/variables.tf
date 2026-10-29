variable "environment" {
  type        = string
  description = "Deployment environment"
}

variable "db_host" {
  type        = string
  description = "PostgreSQL DB host address"
}

variable "db_port" {
  type        = number
  description = "PostgreSQL DB port"
  default     = 5432
}

variable "db_name" {
  type        = string
  description = "Database name"
  default     = "malabak"
}

variable "redis_host" {
  type        = string
  description = "Redis host address"
}

variable "redis_port" {
  type        = number
  description = "Redis port"
  default     = 6379
}

variable "redis_auth_token" {
  type        = string
  description = "Redis auth token"
  sensitive   = true
}
