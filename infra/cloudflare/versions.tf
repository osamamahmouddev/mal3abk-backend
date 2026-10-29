terraform {
  required_version = ">= 1.5.0"

  required_providers {
    cloudflare = {
      source  = "cloudflare/cloudflare"
      version = "~> 4.35"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.6"
    }
  }

  backend "s3" {
    bucket         = "malabak-terraform-state"
    key            = "cloudflare/terraform.tfstate"
    region         = "me-central-1"
    encrypt        = true
    dynamodb_table = "malabak-terraform-locks"
  }
}
