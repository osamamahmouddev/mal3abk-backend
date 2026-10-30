terraform {
  required_version = ">= 1.5.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.50"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
    tls = {
      source  = "hashicorp/tls"
      version = "~> 4.0"
    }
  }

  backend "s3" {
    bucket         = "malabak-terraform-state"
    key            = "staging/terraform.tfstate"
    region         = "me-central-1"
    encrypt        = true
    dynamodb_table = "malabak-terraform-locks"
  }
}

provider "aws" {
  region = var.aws_region

  default_tags {
    tags = {
      Project     = "Malabak"
      Environment = "staging"
      ManagedBy   = "Terraform"
    }
  }
}
