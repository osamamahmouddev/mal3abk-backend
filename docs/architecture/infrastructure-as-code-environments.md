# Infrastructure as Code (IaC) & Cloud Environments Architecture

## 1. Overview

This document defines the Infrastructure as Code (IaC) architecture for Malabak's **Staging** and **Production** environments pursuant to **[ADR-0004](file:///home/osama/Desktop/mal3abk-backend/docs/adr/0004-hosting-and-data-residency.md)** and the security directives of **[SECURITY.md](file:///home/osama/Desktop/mal3abk-backend/docs/SECURITY.md)**.

All infrastructure is provisioned strictly through modular Terraform configurations located in [`infra/terraform/`](file:///home/osama/Desktop/mal3abk-backend/infra/terraform/). Manual configuration in cloud consoles is strictly prohibited.

```mermaid
flowchart TD
    subgraph Edge["Perimeter: Cloudflare (Cairo PoP)"]
        CF[Cloudflare Edge DNS / WAF / mTLS / Tunnel]
    end

    subgraph VPC["Multi-Tier Isolated VPC (me-central-1)"]
        subgraph PublicSubnets["Tier 1: Public Subnets"]
            NAT["NAT Gateway (Outbound Egress)"]
        end

        subgraph AppSubnets["Tier 2: Private App Subnets"]
            API1["ASP.NET Core API Node A"]
            API2["ASP.NET Core API Node B"]
            Hangfire["Hangfire Worker Nodes"]
        end

        subgraph DataSubnets["Tier 3: Isolated Data Subnets (No Internet Route)"]
            PG_Primary["PostgreSQL 17 Primary\nPITR 30 Days + TLS 1.3 + KMS"]
            PG_Standby["PostgreSQL Standby (Multi-AZ)"]
            Redis1["Redis Primary (TLS + Auth)"]
            Redis2["Redis Replica"]
        end
    end

    subgraph Services["Managed Cloud Services"]
        S3["Private S3 Storage (SSE-KMS + Block Public)"]
        SM["AWS Secrets Manager (Catalog)"]
        ECR["Amazon ECR (Immutable Tags + Scan on Push)"]
    end

    CF -->|Cloudflare Tunnel / mTLS| AppSubnets
    AppSubnets -->|Egress (Updates/SMS)| NAT
    API1 -->|Port 5432 (TLS)| PG_Primary
    API2 -->|Port 5432 (TLS)| PG_Primary
    API1 -->|Port 6379 (TLS)| Redis1
    API2 -->|Port 6379 (TLS)| Redis1
    API1 -->|Presigned URLs| S3
    API1 -->|Secret Retrieval| SM
```

---

## 2. Network Isolation & Multi-Tier Subnet Topology

The VPC is divided into three isolated tiers across multiple Availability Zones:

1. **Tier 1: Public Ingress Subnets (`10.0.1.0/24`, `10.0.2.0/24`)**:
   - Houses the NAT Gateway for controlled outbound egress.
   - No application services or databases listen in this tier.
2. **Tier 2: Private Application Subnets (`10.0.10.0/24`, `10.0.11.0/24`)**:
   - Contains ASP.NET Core API containers and Hangfire background workers.
   - Outbound traffic routes via NAT Gateway. Inbound traffic enters exclusively via Cloudflare Tunnel or load balancers with Authenticated Origin Pulls.
3. **Tier 3: Isolated Private Data Subnets (`10.0.20.0/24`, `10.0.21.0/24`)**:
   - **Zero Internet Access:** Subnets have no route to internet gateways or NAT gateways.
   - Hosts PostgreSQL 17 clusters and Redis replication groups.
   - Network Security Groups restrict port 5432 and port 6379 strictly to the application security group (`app_security_group_id`).

---

## 3. Managed PostgreSQL with Continuous PITR & TLS 1.3

### 3.1 Point-in-Time Recovery (PITR)
- **Continuous Backup Retention:** Configured for **30 days** (`backup_retention_period = 30`).
- Enables restoration to any microsecond within the 30-day window to recover from accidental corruption or ransomware threats.
- Automated daily snapshots retained with automated tagging and cross-region replication for disaster recovery.

### 3.2 Cryptographic & Security Controls
- **Storage Encryption:** Enforced using a dedicated customer-managed AWS KMS key (`aws_kms_key.postgres`) with automated annual rotation.
- **TLS Enforcement:** Configured via custom DB parameter group:
  - `rds.force_ssl = 1`: Drops unencrypted connections at the socket handshake.
  - `ssl_min_protocol_version = TLSv1.3`: Blocks obsolete TLS versions.
- **Extensions:** Installs `btree_gist` (mandatory for scheduling exclusion constraints) and `uuid-ossp`.

---

## 4. Managed Redis Cluster (In-Transit & At-Rest Encryption)

- **Engine:** Redis 7.1 with ElastiCache Replication Group (Primary + Standby in production).
- **Transport Security:** `transit_encryption_enabled = true` requires TLS certificates for all cache and rate-limiting connections.
- **Storage Security:** `at_rest_encryption_enabled = true` encrypts in-memory swap files and automated backup snapshots.
- **Authentication:** Enforces cryptographically random 32-character `auth_token`.

---

## 5. Private Object Storage with SSE-KMS & Access Policies

All uploaded files (payment transfer receipts, venue commercial registers, player profile photos) reside in private S3 buckets:

1. **Public Access Block:** `block_public_acls`, `block_public_policy`, `ignore_public_acls`, and `restrict_public_buckets` are all set to `true`.
2. **Server-Side Encryption:** Configured with customer-managed KMS key (`SSE-KMS`) with S3 Bucket Keys enabled to optimize KMS cost.
3. **Transport Policy:** Strict bucket policy denies all requests without TLS (`aws:SecureTransport = false`).
4. **Pre-Signed URLs:** File downloads are served strictly through short-lived (15-minute TTL) pre-signed URLs generated server-side. Direct public S3 URLs are never returned to clients.
5. **Lifecycle Management:** Incomplete multipart uploads are aborted automatically after 7 days.

---

## 6. Least-Privilege Database Roles & Secrets Management

In strict adherence to Principle of Least Privilege (PoLP), the application never connects to the database using superuser or DDL credentials:

| Role Name | Granted Permissions | Usage |
|---|---|---|
| `malabak_migrator` | `CREATE`, `ALTER`, `DROP`, `REFERENCES` on schemas | Used exclusively by EF Core migration runner during CI/CD deployment jobs. |
| `malabak_app` | `SELECT`, `INSERT`, `UPDATE`, `DELETE` on tables, `EXECUTE` on functions | Used by runtime ASP.NET Core API pods. **Cannot perform DDL (DROP/ALTER/TRUNCATE).** |
| `malabak_readonly` | `SELECT` on all tables | Used for read replicas, data warehouse sync, and BI audit queries. |

### Centralized Secrets Manager Catalog

All environment credentials are stored in AWS Secrets Manager encrypted with a dedicated KMS key:

- `malabak/<env>/database/app`: Connection string for `malabak_app`.
- `malabak/<env>/database/migrator`: Connection string for `malabak_migrator`.
- `malabak/<env>/database/readonly`: Connection string for `malabak_readonly`.
- `malabak/<env>/redis`: Host, port, and authentication token.
- `malabak/<env>/jwt`: ES256 Elliptic Curve private/public signing keys.

---

## 7. Automated CI/CD Pipeline (`.github/workflows/iac.yml`)

1. **Linting & Formatting:** Validates formatting with `terraform fmt -check -recursive`.
2. **Static Security Analysis:** Trivy scans Terraform configurations for misconfigurations, overly permissive security groups, or plaintext secrets.
3. **Plan & Validation:** Pull requests automatically execute `terraform validate` and `terraform plan`, posting diffs to PR reviews.
4. **Approval Gate:** Production deployments require manual approval from DevOps lead in GitHub Environments.
