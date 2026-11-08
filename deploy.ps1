<#
.SYNOPSIS
    Publishes the WeatherImages function app, provisions Azure resources from the
    Bicep template, and deploys the compiled code.

.DESCRIPTION
    Steps:
      1. Publish the code with the dotnet CLI.
      2. Create/update Azure resources from infra/main.bicep with the Azure CLI.
      3. Zip-deploy the published function app with the Azure CLI.

.EXAMPLE
    ./deploy.ps1
    ./deploy.ps1 -ResourceGroup ssp-practice -Location westeurope
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup = 'ssp-weatherimg',
    # NOTE: the "Azure for Students" subscription restricts deployments to a set
    # of allowed regions (germanywestcentral, switzerlandnorth, polandcentral,
    # italynorth, spaincentral). westeurope is NOT allowed there.
    [string]$Location = 'germanywestcentral'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$projectPath = Join-Path $root 'WeatherImages/WeatherImages.csproj'
$bicepPath   = Join-Path $root 'infra/main.bicep'
$publishDir  = Join-Path $root 'publish'
$zipPath     = Join-Path $root 'deploy.zip'

Write-Host '==> Checking Azure login...' -ForegroundColor Cyan
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    throw 'Not logged in to Azure. Run "az login" first.'
}
Write-Host ("    Subscription: {0}" -f $account.name)

# Ensure the resource group exists.
Write-Host "==> Ensuring resource group '$ResourceGroup' exists..." -ForegroundColor Cyan
az group create --name $ResourceGroup --location $Location --output none

# ---------------------------------------------------------------------------
# 1. Publish the code with the dotnet CLI
# ---------------------------------------------------------------------------
Write-Host '==> Publishing function app (dotnet publish)...' -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $projectPath -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }

# Package the publish output (host.json must sit at the zip root).
# NOTE: Compress-Archive writes backslash path separators, which the Flex
# Consumption package validator rejects on Linux ("Cannot find required
# .azurefunctions directory at root level"). Build the zip with forward-slash
# entry names instead, and include dot-folders such as .azurefunctions (-Force).
Write-Host '==> Packaging deploy.zip...' -ForegroundColor Cyan
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$publishFull = (Resolve-Path $publishDir).Path.TrimEnd('\') + '\'
$zipStream = [System.IO.File]::Open($zipPath, [System.IO.FileMode]::Create)
$archive = New-Object System.IO.Compression.ZipArchive($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)
Get-ChildItem -Path $publishDir -Recurse -File -Force | ForEach-Object {
    $entryName = ($_.FullName.Substring($publishFull.Length)) -replace '\\', '/'
    $entry = $archive.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $fileStream = [System.IO.File]::OpenRead($_.FullName)
    $fileStream.CopyTo($entryStream)
    $fileStream.Close(); $entryStream.Close()
}
$archive.Dispose(); $zipStream.Close()

# ---------------------------------------------------------------------------
# 2. Create resources in Azure using the Bicep template
# ---------------------------------------------------------------------------
Write-Host '==> Deploying Bicep template...' -ForegroundColor Cyan
$deployName = "weatherimages-$(Get-Date -Format 'yyyyMMddHHmmss')"
$outputsJson = az deployment group create `
    --name $deployName `
    --resource-group $ResourceGroup `
    --template-file $bicepPath `
    --parameters location=$Location `
    --query properties.outputs `
    --output json
if ($LASTEXITCODE -ne 0) { throw 'Bicep deployment failed.' }

$outputs = $outputsJson | ConvertFrom-Json
$functionAppName = $outputs.functionAppName.value
$apiBaseUrl      = $outputs.apiBaseUrl.value
Write-Host ("    Function App: {0}" -f $functionAppName)

# ---------------------------------------------------------------------------
# 3. Deploy the code to the Flex Consumption app (OneDeploy).
#    Flex Consumption disables SCM basic-auth publishing and the old
#    `config-zip` endpoint, and `az functionapp deploy` currently returns
#    HTTP 415 against it. So POST the package to the OneDeploy endpoint
#    directly with an Entra ID (Azure AD) bearer token and an explicit
#    application/zip content type, then poll for completion.
# ---------------------------------------------------------------------------
Write-Host '==> Deploying code (OneDeploy)...' -ForegroundColor Cyan
$scmToken = az account get-access-token --resource 'https://management.azure.com/' --query accessToken -o tsv
$scmHost = "$functionAppName.scm.azurewebsites.net"
$outFile = Join-Path $root 'onedeploy.out'
$http = curl.exe -s -S -w '%{http_code}' -o $outFile -X POST "https://$scmHost/api/publish?type=zip" `
    --header "Authorization: Bearer $scmToken" --header 'Content-Type: application/zip' --data-binary "@$zipPath"
if ($http -ne '202' -and $http -ne '200') {
    throw ("OneDeploy upload failed (HTTP {0}): {1}" -f $http, (Get-Content $outFile -Raw))
}
Remove-Item $outFile -Force -ErrorAction SilentlyContinue

Write-Host '    Waiting for deployment to validate and apply...'
$deployOk = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Seconds 6
    $status = curl.exe -s --header "Authorization: Bearer $scmToken" "https://$scmHost/api/deployments/latest" | ConvertFrom-Json
    if ($status.complete) {
        if ($status.status -eq 4) { $deployOk = $true }
        else { throw ("Deployment failed: {0}" -f $status.status_text) }
        break
    }
}
if (-not $deployOk) { throw 'Deployment did not complete in time.' }

Write-Host ''
Write-Host '==========================================================' -ForegroundColor Green
Write-Host ' Deployment complete.' -ForegroundColor Green
Write-Host ("  API base URL : {0}" -f $apiBaseUrl)
Write-Host ("  Start a job  : POST {0}/jobs" -f $apiBaseUrl)
Write-Host ("  Get status   : GET  {0}/jobs/{{jobId}}" -f $apiBaseUrl)
Write-Host '==========================================================' -ForegroundColor Green
