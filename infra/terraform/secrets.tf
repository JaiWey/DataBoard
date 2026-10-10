# The settings the server needs and must not have written into its start-up script. They are SecureString parameters in
# Systems Manager Parameter Store (the standard tier is free; Secrets Manager would cost about $0.40 a secret a month).
# The server's role may read just these. Their values are also in the Terraform state file, so keep that private.

resource "random_password" "jwt" {
  length  = 64
  special = false
}

resource "aws_ssm_parameter" "db_connection" {
  name        = "/${var.project_name}/db-connection-string"
  description = "The connection string of the DataBoard database."
  type        = "SecureString"
  value       = "Host=${aws_db_instance.db.address};Port=5432;Database=databoard;Username=databoard;Password=${random_password.db.result};SSL Mode=Require;Trust Server Certificate=true"
}

resource "aws_ssm_parameter" "jwt_key" {
  name        = "/${var.project_name}/jwt-key"
  description = "The key that signs sign-in tokens. Changing it signs everybody out."
  type        = "SecureString"
  value       = random_password.jwt.result
}

locals {
  github_enabled = var.github_client_id != "" && var.github_client_secret != ""
}

resource "aws_ssm_parameter" "github_client_id" {
  count = local.github_enabled ? 1 : 0

  name  = "/${var.project_name}/github-client-id"
  type  = "SecureString"
  value = var.github_client_id
}

resource "aws_ssm_parameter" "github_client_secret" {
  count = local.github_enabled ? 1 : 0

  name  = "/${var.project_name}/github-client-secret"
  type  = "SecureString"
  value = var.github_client_secret
}
