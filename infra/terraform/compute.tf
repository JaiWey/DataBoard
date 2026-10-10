data "aws_caller_identity" "current" {}

# ---- where the server goes ---------------------------------------------------------------------------------------
# Some zones do not offer every instance type (us-east-1e has no t3, for one), so only the zones that do are considered.

data "aws_ec2_instance_type_offerings" "this" {
  location_type = "availability-zone"

  filter {
    name   = "instance-type"
    values = [var.instance_type]
  }
}

data "aws_subnets" "server" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }

  filter {
    name   = "default-for-az"
    values = ["true"]
  }

  filter {
    name   = "availability-zone"
    values = data.aws_ec2_instance_type_offerings.this.locations
  }
}

data "aws_subnet" "server" {
  id = sort(data.aws_subnets.server.ids)[0]
}

data "aws_ssm_parameter" "ami" {
  name = "/aws/service/ami-amazon-linux-latest/al2023-ami-kernel-default-${var.architecture}"
}

# ---- what the server may do --------------------------------------------------------------------------------------

data "aws_iam_policy_document" "assume" {
  statement {
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ec2.amazonaws.com"]
    }
  }
}

resource "aws_iam_role" "server" {
  name               = "${var.project_name}-server"
  assume_role_policy = data.aws_iam_policy_document.assume.json
}

# Systems Manager lets deploy.ps1 run commands on the server and gives a shell without opening SSH.
resource "aws_iam_role_policy_attachment" "ssm" {
  role       = aws_iam_role.server.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

data "aws_iam_policy_document" "settings" {
  statement {
    actions   = ["s3:GetObject"]
    resources = ["${aws_s3_bucket.releases.arn}/releases/*"]
  }

  statement {
    actions   = ["ssm:GetParameter", "ssm:GetParameters"]
    resources = ["arn:aws:ssm:${var.aws_region}:${data.aws_caller_identity.current.account_id}:parameter/${var.project_name}/*"]
  }

  statement {
    actions   = ["kms:Decrypt"]
    resources = ["*"]

    condition {
      test     = "StringEquals"
      variable = "kms:ViaService"
      values   = ["ssm.${var.aws_region}.amazonaws.com"]
    }
  }
}

resource "aws_iam_role_policy" "settings" {
  name   = "read-settings"
  role   = aws_iam_role.server.id
  policy = data.aws_iam_policy_document.settings.json
}

resource "aws_iam_instance_profile" "server" {
  name = "${var.project_name}-server"
  role = aws_iam_role.server.name
}

# ---- the server --------------------------------------------------------------------------------------------------

# A fixed address, so the name (and the certificate) survive the server being replaced.
resource "aws_eip" "app" {
  domain = "vpc"
}

locals {
  caddy_arch = var.architecture == "arm64" ? "arm64" : "amd64"
  host       = var.domain_name != "" ? var.domain_name : "${replace(aws_eip.app.public_ip, ".", "-")}.sslip.io"
}

# What must outlive the server: uploaded CSV data, the keys that protect stored passwords, and the certificates.
resource "aws_ebs_volume" "data" {
  availability_zone = data.aws_subnet.server.availability_zone
  size              = var.data_volume_gb
  type              = "gp3"
  encrypted         = true

  tags = { Name = "${var.project_name}-data" }
}

resource "aws_instance" "server" {
  ami                         = nonsensitive(data.aws_ssm_parameter.ami.value)
  instance_type               = var.instance_type
  subnet_id                   = data.aws_subnet.server.id
  vpc_security_group_ids      = [aws_security_group.app.id]
  iam_instance_profile        = aws_iam_instance_profile.server.name
  associate_public_ip_address = true

  root_block_device {
    volume_size = 12
    volume_type = "gp3"
    encrypted   = true
  }

  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "required" # IMDSv2 only
    http_put_response_hop_limit = 1
  }

  user_data = templatefile("${path.module}/templates/bootstrap.sh.tftpl", {
    region         = var.aws_region
    param_prefix   = "/${var.project_name}"
    host           = local.host
    email          = var.letsencrypt_email
    github_enabled = local.github_enabled
    caddy_version  = var.caddy_version
    caddy_arch     = local.caddy_arch
    data_volume_id = replace(aws_ebs_volume.data.id, "-", "")
  })
  user_data_replace_on_change = true # the data disk, the database and the address are kept; the server itself is disposable

  # Started once from the newest image of the day; a newer machine image is not a reason to rebuild a running server.
  lifecycle {
    ignore_changes = [ami]
  }

  depends_on = [aws_ssm_parameter.db_connection, aws_ssm_parameter.jwt_key]

  tags = { Name = "${var.project_name}-server" }
}

resource "aws_volume_attachment" "data" {
  device_name                    = "/dev/sdf"
  volume_id                      = aws_ebs_volume.data.id
  instance_id                    = aws_instance.server.id
  stop_instance_before_detaching = true
}

resource "aws_eip_association" "app" {
  instance_id   = aws_instance.server.id
  allocation_id = aws_eip.app.id
}
