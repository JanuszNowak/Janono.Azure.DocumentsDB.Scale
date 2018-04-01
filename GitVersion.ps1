$UtcDateTime = (Get-Date).ToUniversalTime()
$FormattedDateTime = (Get-Date -Date $UtcDateTime -Format "yyyyMMdd-HHmmss")
$CI_Version = "$env:GITVERSION_MAJORMINORPATCH-ci-$FormattedDateTime"
Write-Host ("##vso[task.setvariable variable=CI_Version;]$CI_Version")