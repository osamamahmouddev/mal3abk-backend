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

variable "node_type" {
  type        = string
  description = "ElastiCache node type"
  default     = "cache.t4g.small"
}

variable "num_cache_clusters" {
  type        = number
  description = "Number of cache clusters (nodes) for replication"
  default     = 2
}
