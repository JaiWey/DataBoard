output "app_url" {
  description = "Where DataBoard answers once an image has been deployed."
  value       = "https://${local.host}"
}

output "public_ip" {
  description = "The server's fixed address. With your own domain, point its A record here (unless Route 53 does it)."
  value       = aws_eip.app.public_ip
}

output "dns_record" {
  description = "The DNS record to make when you use your own domain and Route 53 is not making it."
  value       = var.domain_name == "" ? "none needed: ${local.host} is a name that points at the address by itself" : "${var.domain_name}  A  ${aws_eip.app.public_ip}"
}

output "aws_region" {
  value = var.aws_region
}

output "release_bucket" {
  description = "Where deploy.ps1 uploads the built application for the server to fetch."
  value       = aws_s3_bucket.releases.bucket
}

output "instance_id" {
  description = "The server. Open a shell on it with: aws ssm start-session --target <this id>"
  value       = aws_instance.server.id
}

output "db_endpoint" {
  description = "The database (reachable only from the server)."
  value       = aws_db_instance.db.address
}

output "next_step" {
  description = "What to do after terraform apply."
  value       = "Run  ./infra/deploy.ps1  from the repository root to build the application, upload it and start it."
}
