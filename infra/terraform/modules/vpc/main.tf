# ==============================================================================
# VPC and Private Networking Module
# Implements strict multi-tier isolation: Ingress -> App -> Data
# ==============================================================================

resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_hostnames = true
  enable_dns_support   = true

  tags = {
    Name        = "malabak-${var.environment}-vpc"
    Environment = var.environment
  }
}

# 1. Public Ingress Subnets (Load Balancer / Cloudflare Tunnel / NAT)
resource "aws_subnet" "public" {
  count                   = length(var.public_subnet_cidrs)
  vpc_id                  = aws_vpc.main.id
  cidr_block              = var.public_subnet_cidrs[count.index]
  availability_zone       = var.availability_zones[count.index]
  map_public_ip_on_launch = false

  tags = {
    Name        = "malabak-${var.environment}-public-${count.index + 1}"
    Tier        = "Public"
    Environment = var.environment
  }
}

# 2. Private App Subnets (Container / ECS / EKS / EC2)
resource "aws_subnet" "private_app" {
  count             = length(var.private_app_subnet_cidrs)
  vpc_id            = aws_vpc.main.id
  cidr_block        = var.private_app_subnet_cidrs[count.index]
  availability_zone = var.availability_zones[count.index]

  tags = {
    Name        = "malabak-${var.environment}-private-app-${count.index + 1}"
    Tier        = "App"
    Environment = var.environment
  }
}

# 3. Isolated Private Data Subnets (PostgreSQL & Redis - No internet access)
resource "aws_subnet" "private_data" {
  count             = length(var.private_data_subnet_cidrs)
  vpc_id            = aws_vpc.main.id
  cidr_block        = var.private_data_subnet_cidrs[count.index]
  availability_zone = var.availability_zones[count.index]

  tags = {
    Name        = "malabak-${var.environment}-private-data-${count.index + 1}"
    Tier        = "Data"
    Environment = var.environment
  }
}

# Internet Gateway for Outbound Egress via NAT
resource "aws_internet_gateway" "igw" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name        = "malabak-${var.environment}-igw"
    Environment = var.environment
  }
}

# Elastic IP for NAT Gateway
resource "aws_eip" "nat" {
  domain = "vpc"

  tags = {
    Name        = "malabak-${var.environment}-nat-eip"
    Environment = var.environment
  }
}

# NAT Gateway in Public Subnet
resource "aws_nat_gateway" "nat" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id

  tags = {
    Name        = "malabak-${var.environment}-nat"
    Environment = var.environment
  }

  depends_on = [aws_internet_gateway.igw]
}

# Route Tables
resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.igw.id
  }

  tags = {
    Name        = "malabak-${var.environment}-public-rt"
    Environment = var.environment
  }
}

resource "aws_route_table" "private_app" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = aws_nat_gateway.nat.id
  }

  tags = {
    Name        = "malabak-${var.environment}-private-app-rt"
    Environment = var.environment
  }
}

# Isolated Route Table for Database (No default route to internet)
resource "aws_route_table" "private_data" {
  vpc_id = aws_vpc.main.id

  tags = {
    Name        = "malabak-${var.environment}-private-data-rt"
    Environment = var.environment
  }
}

# Route Table Associations
resource "aws_route_table_association" "public" {
  count          = length(aws_subnet.public)
  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_route_table_association" "private_app" {
  count          = length(aws_subnet.private_app)
  subnet_id      = aws_subnet.private_app[count.index].id
  route_table_id = aws_route_table.private_app.id
}

resource "aws_route_table_association" "private_data" {
  count          = length(aws_subnet.private_data)
  subnet_id      = aws_subnet.private_data[count.index].id
  route_table_id = aws_route_table.private_data.id
}

# Security Groups
resource "aws_security_group" "app" {
  name        = "malabak-${var.environment}-app-sg"
  description = "Security group for ASP.NET Core API application instances"
  vpc_id      = aws_vpc.main.id

  egress {
    description = "Allow all outbound traffic"
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name        = "malabak-${var.environment}-app-sg"
    Environment = var.environment
  }
}
