variable "aws_region" {
  description = "The AWS region everything is made in (ap-southeast-2 is Sydney, Australia; ap-southeast-4 is Melbourne)."
  type        = string
  default     = "ap-southeast-2"
}

variable "project_name" {
  description = "A short name used in the names of resources and as the prefix of the stored settings."
  type        = string
  default     = "databoard"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,30}$", var.project_name))
    error_message = "Use lower-case letters, digits and hyphens, starting with a letter (2 to 31 characters)."
  }
}

# ---- the server -------------------------------------------------------------------------------------------------

variable "instance_type" {
  description = "The EC2 instance type. t3.micro is in the free tier (use t4g.micro or t4g.small, with architecture = \"arm64\", on accounts whose free tier lists those)."
  type        = string
  default     = "t3.micro"
}

variable "architecture" {
  description = "The CPU of the instance type: x86_64 (t3, t2) or arm64 (t4g). deploy.ps1 builds the application for it."
  type        = string
  default     = "x86_64"

  validation {
    condition     = contains(["x86_64", "arm64"], var.architecture)
    error_message = "architecture must be x86_64 or arm64."
  }
}

variable "data_volume_gb" {
  description = "Size of the disk that holds uploaded CSV data, the encryption keys and the certificates. It outlives the instance."
  type        = number
  default     = 10
}

variable "caddy_version" {
  description = "The version of the Caddy web server (it makes the HTTPS certificate) installed on the server."
  type        = string
  default     = "2.10.0"
}

# ---- the address ------------------------------------------------------------------------------------------------

variable "domain_name" {
  description = "A domain name you own that points (an A record) at the server, for example databoard.example.com. Empty: use a free name made from the server's address, <ip>.sslip.io, which works with HTTPS and needs nothing bought."
  type        = string
  default     = ""
}

variable "route53_zone_id" {
  description = "The Route 53 hosted zone of domain_name. When both are given, the A record is made for you. Empty: make the record yourself where your DNS is."
  type        = string
  default     = ""
}

variable "letsencrypt_email" {
  description = "An address Let's Encrypt can warn when a certificate cannot be renewed. Optional."
  type        = string
  default     = ""
}

# ---- the database -----------------------------------------------------------------------------------------------

variable "db_instance_class" {
  description = "The RDS instance class. db.t4g.micro, db.t3.micro and db.t2.micro are in the free tier; others need allow_paid_database = true."
  type        = string
  default     = "db.t4g.micro"
}

variable "db_engine_version" {
  description = "The PostgreSQL major version. The newest minor version of it is used."
  type        = string
  default     = "17"
}

variable "db_storage_gb" {
  description = "Database storage in GB. The free tier includes 20 (also the least PostgreSQL on RDS accepts)."
  type        = number
  default     = 20
}

variable "db_backup_days" {
  description = "How many days of automated backups to keep (0 turns them off). Accounts on the AWS free plan are limited to 1 day; raise it only on a paid plan. Backups are free up to the size of the database (20 GB)."
  type        = number
  default     = 1
}

variable "allow_paid_database" {
  description = "The database is kept inside the RDS free tier (a micro instance, one zone, 20 GB), and terraform plan fails if db_instance_class or db_storage_gb would leave it. Set to true to go beyond it on purpose, and pay for it."
  type        = bool
  default     = false
}

variable "protect_data" {
  description = "When true the database cannot be deleted by accident (deletion protection, and a final snapshot is taken if it is destroyed). When false (the default here) terraform destroy removes it at once, with no snapshot: whatever is in it is lost."
  type        = bool
  default     = false
}

# ---- sign in with GitHub (optional) -----------------------------------------------------------------------------

variable "github_client_id" {
  description = "Client ID of a GitHub OAuth app, to offer Continue with GitHub. Its callback URL is https://<your host>/api/auth/github/callback. Empty: not offered."
  type        = string
  default     = ""
}

variable "github_client_secret" {
  description = "Client secret of that GitHub OAuth app."
  type        = string
  default     = ""
  sensitive   = true
}

# ---- keeping an eye on the bill ---------------------------------------------------------------------------------

variable "alert_email" {
  description = "Where to send a warning when the month's cost nears monthly_budget_usd. Empty: no budget is made."
  type        = string
  default     = ""
}

variable "monthly_budget_usd" {
  description = "The monthly amount the warning is about."
  type        = number
  default     = 5
}
