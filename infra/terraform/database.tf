# What the RDS free tier covers: 750 hours a month of ONE single-zone micro instance, with 20 GB of general purpose (gp2)
# storage and up to 20 GB of backups. Anything outside this is billed, so the database is checked against it below and
# terraform plan refuses to go on unless allow_paid_database = true says the cost is intended.
locals {
  free_tier_db_classes = ["db.t2.micro", "db.t3.micro", "db.t4g.micro"]
  free_tier_db_storage = 20
}

resource "random_password" "db" {
  length  = 32
  special = false # letters and digits only, so it can sit in a connection string without escaping
}

resource "aws_db_subnet_group" "db" {
  name       = "${var.project_name}-db"
  subnet_ids = data.aws_subnets.default.ids
}

resource "aws_db_instance" "db" {
  identifier = "${var.project_name}-db"

  engine         = "postgres"
  engine_version = var.db_engine_version
  instance_class = var.db_instance_class

  allocated_storage = var.db_storage_gb
  storage_type      = "gp2" # not gp3 or io1: the free tier is general purpose (gp2) storage
  storage_encrypted = true
  # max_allocated_storage is left out on purpose: with it, the storage would grow past 20 GB by itself and be billed.

  db_name  = "databoard"
  username = "databoard"
  password = random_password.db.result
  port     = 5432

  db_subnet_group_name   = aws_db_subnet_group.db.name
  vpc_security_group_ids = [aws_security_group.db.id]
  publicly_accessible    = false
  multi_az               = false # one zone keeps it in the free tier; set true here for a standby in a second zone

  backup_retention_period    = var.db_backup_days
  auto_minor_version_upgrade = true

  # Extras that cost money are off.
  performance_insights_enabled    = false
  monitoring_interval             = 0 # no enhanced monitoring
  enabled_cloudwatch_logs_exports = []

  deletion_protection       = var.protect_data
  skip_final_snapshot       = !var.protect_data
  final_snapshot_identifier = var.protect_data ? "${var.project_name}-db-final" : null
  copy_tags_to_snapshot     = true

  lifecycle {
    precondition {
      condition     = var.allow_paid_database || contains(local.free_tier_db_classes, var.db_instance_class)
      error_message = "${var.db_instance_class} is not in the RDS free tier (${join(", ", local.free_tier_db_classes)}). Choose one of those, or set allow_paid_database = true to pay for a bigger database."
    }

    precondition {
      condition     = var.allow_paid_database || var.db_storage_gb <= local.free_tier_db_storage
      error_message = "The RDS free tier includes ${local.free_tier_db_storage} GB of storage and db_storage_gb is ${var.db_storage_gb}. Lower it, or set allow_paid_database = true to pay for the extra."
    }
  }
}
