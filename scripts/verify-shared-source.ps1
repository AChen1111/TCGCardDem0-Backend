param(
    [string]$ParentWorkspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$backendRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$lockPath = Join-Path $backendRoot 'shared-source.lock'
$expectedCommit = (Get-Content -LiteralPath $lockPath -Raw).Trim()
if ($expectedCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'shared-source.lock 必须记录已推送到主仓库的完整 40 位提交 SHA。'
}
$sharedPaths = @('Assets/Shared/Configuration', 'Assets/GameConfiguration', 'Assets/ActivityConfiguration', 'Assets/Scripts/Network/Activities',
    'Assets/Scripts/Network/GameConfig', 'Assets/Scripts/Localization/Generated', 'Assets/Scripts/Duel/Shared', 'TableData', 'Tools/Duel')
& git -C $ParentWorkspace cat-file -e ($expectedCommit + '^{commit}')
if ($LASTEXITCODE -ne 0) { throw "父仓库不存在锁定源码提交 $expectedCommit。" }
& git -C $ParentWorkspace diff --quiet $expectedCommit HEAD -- $sharedPaths
if ($LASTEXITCODE -ne 0) { throw '当前父仓库共享源码树与锁定提交不一致。' }
$changedSources = & git -C $ParentWorkspace status --porcelain -- $sharedPaths
if ($LASTEXITCODE -ne 0 -or $changedSources) {
    throw '共享源码存在未提交改动，不能将工作区结果作为固定 SHA 组合的结果。'
}

& dotnet test (Join-Path $backendRoot 'AChen.Backend.sln') --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw '逻辑构建入口未完成。' }
