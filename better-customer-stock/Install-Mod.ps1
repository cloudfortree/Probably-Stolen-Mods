[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Package,
    [Parameter(Mandatory)][string]$GameDir
)

$ErrorActionPreference = 'Stop'
$modId = 'better_customer_stock'
$payload = @('BetterCustomerStock.dll', 'manifest.xml')
$source = (Resolve-Path -LiteralPath $Package).ProviderPath
$game = (Resolve-Path -LiteralPath $GameDir).ProviderPath
$modRoot = Join-Path $game 'Mods'
$destination = Join-Path $modRoot 'BetterCustomerStock'

if (!(Test-Path -LiteralPath (Join-Path $game 'Probably Stolen_Data\Managed\Assembly-CSharp.dll') -PathType Leaf)) {
    throw "找不到《深空当铺》的游戏程序集：$game"
}
foreach ($name in $payload) {
    if (!(Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) {
        throw "安装包缺少 $name。"
    }
}
$manifest = [xml](Get-Content -LiteralPath (Join-Path $source 'manifest.xml') -Raw)
if ($manifest.DocumentElement.ID -cne $modId -or $manifest.DocumentElement.Assemblies.Assembly -cne 'BetterCustomerStock.dll') {
    throw '安装包的模组 ID 或程序集与此项目不匹配。'
}

$mutex = [Threading.Mutex]::new($false, 'Local\ProbablyStolen.BetterCustomerStock.Install')
$locked = $false
$staging = $null
$keepBackup = $false
try {
    try {
        $locked = $mutex.WaitOne([TimeSpan]::FromSeconds(15))
    } catch [Threading.AbandonedMutexException] {
        $locked = $true
    }
    if (!$locked) { throw '另一个安装进程仍在更新「更好的顾客库存」。' }

    $existing = Get-Item -LiteralPath $destination -ErrorAction SilentlyContinue
    if ($existing) {
        if (!$existing.PSIsContainer -or ($existing.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "现有安装位置不是普通目录：$destination"
        }
        $installed = [xml](Get-Content -LiteralPath (Join-Path $destination 'manifest.xml') -Raw)
        if ($installed.DocumentElement.ID -cne $modId) {
            throw "安装位置属于其他模组：$destination"
        }
    }

    # Stage beside Mods so the game never sees a second manifest or a partial first install.
    $staging = Join-Path $game ('.BetterCustomerStock-install-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    foreach ($name in $payload) {
        Copy-Item -LiteralPath (Join-Path $source $name) -Destination (Join-Path $staging $name)
    }

    if (!$existing) {
        [IO.Directory]::CreateDirectory($modRoot) | Out-Null
        [IO.Directory]::Move($staging, $destination)
        $staging = $null
    } else {
        $updated = [Collections.Generic.List[string]]::new()
        try {
            foreach ($name in $payload) {
                $target = Join-Path $destination $name
                $incoming = Join-Path $staging $name
                if ([IO.File]::Exists($target)) {
                    if ((Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $incoming).Hash) {
                        continue
                    }
                    [IO.File]::Replace($incoming, $target, (Join-Path $staging ($name + '.bak')))
                } else {
                    [IO.File]::Move($incoming, $target)
                }
                $updated.Add($name)
            }
        } catch {
            $installError = $_
            for ($index = $updated.Count - 1; $index -ge 0; $index--) {
                $name = $updated[$index]
                $target = Join-Path $destination $name
                $backup = Join-Path $staging ($name + '.bak')
                try {
                    if ([IO.File]::Exists($backup)) {
                        [IO.File]::Move($backup, $target, $true)
                    } else {
                        [IO.File]::Delete($target)
                    }
                } catch {
                    $keepBackup = $true
                    Write-Warning "无法恢复 $name。备份保存在 $staging。"
                }
            }
            throw $installError
        }
    }
    $destination
} finally {
    try {
        if ($staging -and !$keepBackup -and [IO.Directory]::Exists($staging)) {
            foreach ($name in $payload) {
                [IO.File]::Delete((Join-Path $staging $name))
                [IO.File]::Delete((Join-Path $staging ($name + '.bak')))
            }
            [IO.Directory]::Delete($staging)
        }
    } finally {
        if ($locked) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
