# Where the built application is handed to the server: deploy.ps1 uploads a release archive here and the server downloads it.
# Private, encrypted, and old releases are deleted after 30 days (the server keeps its own last three).

resource "aws_s3_bucket" "releases" {
  bucket        = "${var.project_name}-releases-${data.aws_caller_identity.current.account_id}"
  force_destroy = true # destroying the stack removes the archives with it
}

resource "aws_s3_bucket_public_access_block" "releases" {
  bucket                  = aws_s3_bucket.releases.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "releases" {
  bucket = aws_s3_bucket.releases.id

  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "releases" {
  bucket = aws_s3_bucket.releases.id

  rule {
    id     = "expire-old-releases"
    status = "Enabled"

    filter {
      prefix = "releases/"
    }

    expiration {
      days = 30
    }

    abort_incomplete_multipart_upload {
      days_after_initiation = 2
    }
  }
}
