<#
.SYNOPSIS
  Builds DataBoard as a self-contained Linux application, uploads it, and tells the server to run it.

.DESCRIPTION
  Run it after `terraform apply`, and again for every update. It needs the .NET 10 SDK and Node (the Angular app is built
  as part of publishing), the AWS CLI signed in to the same account, Terraform, and `tar` (part of Windows 10 and later).

  "Self-contained" means the .NET runtime is inside the build, so the server needs nothing installed for it.

.EXAMPLE
  ./infra/deploy.ps1
  ./infra/deploy.ps1 -Runtime linux-arm64     # for a t4g (Graviton) instance; set architecture = "arm64" in Terraform
#>
param(
  # The CPU of the server: linux-x64 (t3, t2) or linux-arm64 (t4g). Match `architecture` in Terraform.
  [ValidateSet('linux-x64', 'linux-arm64')]
  [string]$Runtime = 'linux-x64'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$tf = Join-Path $PSScriptRoot 'terraform'

function Output([string]$name) {
  $value = terraform "-chdir=$tf" output -raw $name
  if ($LASTEXITCODE -ne 0 -or -not $value) { throw "Could not read the Terraform output '$name'. Has terraform apply finished?" }
  return $value
}

$region = Output 'aws_region'
$bucket = Output 'release_bucket'
$instance = Output 'instance_id'
$url = Output 'app_url'

$work = Join-Path ([IO.Path]::GetTempPath()) ('databoard-release-' + (Get-Date -Format 'yyyyMMddHHmmss'))
$build = Join-Path $work 'app'
$archive = Join-Path $work 'release.tgz'
New-Item -ItemType Directory -Path $build -Force | Out-Null

try {
  Write-Host "Building the application for $Runtime (the Angular app is built too) ..."
  dotnet publish (Join-Path $repoRoot 'src/DataBoard.Server/DataBoard.Server.csproj') `
    -c Release -r $Runtime --self-contained true -o $build --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
  if (-not (Test-Path (Join-Path $build 'DataBoard.Server'))) { throw "The build has no DataBoard.Server program; is the runtime '$Runtime' right?" }

  Write-Host 'Packing ...'
  tar -czf $archive -C $build .
  if ($LASTEXITCODE -ne 0) { throw 'Could not pack the build.' }
  $megabytes = [math]::Round((Get-Item $archive).Length / 1MB)

  $key = "releases/$(Split-Path -Leaf $work).tgz"
  Write-Host "Uploading $megabytes MB to s3://$bucket/$key ..."
  aws s3 cp $archive "s3://$bucket/$key" --region $region --only-show-errors
  if ($LASTEXITCODE -ne 0) { throw 'The upload failed.' }
}
finally {
  Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Telling the server to run it ...'
$commandId = aws ssm send-command --region $region --instance-ids $instance --document-name AWS-RunShellScript `
  --parameters "commands=[`"/opt/databoard/release.sh s3://$bucket/$key`"]" --query Command.CommandId --output text
if ($LASTEXITCODE -ne 0) { throw 'Could not send the command. Is the server up and registered with Systems Manager (it takes a few minutes after the first apply)?' }

aws ssm wait command-executed --region $region --command-id $commandId --instance-id $instance 2>$null
$result = aws ssm get-command-invocation --region $region --command-id $commandId --instance-id $instance --output json | ConvertFrom-Json
Write-Host $result.StandardOutputContent
if ($result.Status -ne 'Success') {
  Write-Host $result.StandardErrorContent
  throw "The server could not start the new release (status: $($result.Status))."
}

Write-Host ''
Write-Host "Done: $url"
Write-Host 'The first time, the certificate takes a minute to be issued; if the page does not open at once, wait and reload.'
