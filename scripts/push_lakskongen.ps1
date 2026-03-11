# Push Lakskongen feature branches and create draft PRs
# Usage:
# 1) Set environment variable GIT_REMOTE (or pass as first arg):
#    $env:GIT_REMOTE = 'git@github.com:youruser/yourrepo.git'
#    ./push_lakskongen.ps1
# 2) Or run: ./push_lakskongen.ps1 <git-remote-url>

param(
    [string]$RemoteUrl
)

if (-not $RemoteUrl) {
    $RemoteUrl = $env:GIT_REMOTE
}

if (-not $RemoteUrl) {
    Write-Error "No remote URL provided. Set GIT_REMOTE or pass the remote URL as an argument."
    exit 1
}

Set-Location -Path "$PSScriptRoot/.."
$repoPath = Resolve-Path -Path ".\salmon-sim"
Write-Output "Using repo: $repoPath"

# Add origin if missing
$remotes = git -C salmon-sim remote
if (-not ($remotes -match 'origin')) {
    Write-Output "Adding origin: $RemoteUrl"
    git -C salmon-sim remote add origin $RemoteUrl
} else {
    Write-Output "Origin already configured"
}

$branches = @('feature/fish/growth-model','feature/feed/fcr','feature/ui/dashboard')
foreach ($b in $branches) {
    # push if branch exists locally
    $exists = git -C salmon-sim branch --list $b
    if ($exists) {
        Write-Output "Pushing branch $b"
        git -C salmon-sim push --set-upstream origin $b
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Push failed for $b"
        } else {
            # create draft PR if gh is available
            if (Get-Command gh -ErrorAction SilentlyContinue) {
                $title = "feat: $b"
                $bodyFile = "salmon-sim/PR_TEMPLATES/${b.Replace('/','_')}_PR.md"
                if (Test-Path $bodyFile) {
                    gh pr create --fill --base main --head $b --draft --body-file $bodyFile
                } else {
                    gh pr create --title $title --body "Draft PR for $b" --base main --head $b --draft
                }
            } else {
                Write-Output "gh CLI not found; skipping PR creation for $b"
            }
        }
    } else {
        Write-Output "Branch $b not found locally; skipping"
    }
}

Write-Output "Done. If any pushes failed, check git status in salmon-sim and re-run with correct credentials."