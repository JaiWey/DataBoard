# The default VPC is used on purpose: its subnets are public and free, so there is no NAT gateway (about $32 a month)
# to pay for. The database is not reachable from the internet (no public address, and its firewall lets in only the server).

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

resource "aws_security_group" "app" {
  name        = "${var.project_name}-app"
  description = "Web traffic to the DataBoard server. No SSH: it is managed through Systems Manager."
  vpc_id      = data.aws_vpc.default.id

  ingress {
    description      = "HTTP (sends people to HTTPS and proves the domain to Lets Encrypt)"
    from_port        = 80
    to_port          = 80
    protocol         = "tcp"
    cidr_blocks      = ["0.0.0.0/0"]
    ipv6_cidr_blocks = ["::/0"]
  }

  ingress {
    description      = "HTTPS"
    from_port        = 443
    to_port          = 443
    protocol         = "tcp"
    cidr_blocks      = ["0.0.0.0/0"]
    ipv6_cidr_blocks = ["::/0"]
  }

  egress {
    description      = "Out to the internet (image pulls and certificates, and the databases people connect)"
    from_port        = 0
    to_port          = 0
    protocol         = "-1"
    cidr_blocks      = ["0.0.0.0/0"]
    ipv6_cidr_blocks = ["::/0"]
  }
}

resource "aws_security_group" "db" {
  name        = "${var.project_name}-db"
  description = "PostgreSQL, from the DataBoard server only."
  vpc_id      = data.aws_vpc.default.id

  ingress {
    description     = "PostgreSQL from the server"
    from_port       = 5432
    to_port         = 5432
    protocol        = "tcp"
    security_groups = [aws_security_group.app.id]
  }
}
