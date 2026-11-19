#requires -Version 5.1
<#
.SYNOPSIS
  构建拾光安装版：自包含单文件程序与 Inno Setup 安装包。
.DESCRIPTION
  发布产物写入 .artifacts\release\<版本>-<时间戳>\（只含安装包与哈希清单）；
  发布暂存目录写入 .artifacts\staging\<版本>-<时间戳>\，供安装器打包，不作为交付物。
  默认从仓库根目录的 app_audio 复制白噪音音源；可用 -AudioSource 或 -SkipAudio 调整。
  脚本不删除任何既有文件；每次构建使用新的时间戳目录，便于保留历史产物。
.EXAMPLE
  ./scripts/build-release.ps1
.EXAMPLE
  ./scripts/build-release.ps1 -SkipInstaller -SkipAudio
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [string]$AudioSource,
    [string]$OutputRoot,
    [switch]$SkipAudio,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repoRoot

function Read-AppVersion {
    $path = Join-Path $repoRoot 'Lumina.Services\Configuration\HotkeyCatalog.cs'
    $text = Get-Content -Raw -LiteralPath $path
    $match = [regex]::Match($text, 'public const string Version = "([^"]+)"')
    if (-not $match.Success) {
        throw "未找到 AppInfo.Version：$path"
    }

    return $match.Groups[1].Value
}

function Resolve-InnoCompiler {
    $candidates = @()
    if (${env:ProgramFiles(x86)}) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
        $candidates += (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    }
    if ($env:ProgramFiles) {
        $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
        $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    # PATH 上的 iscc（部分安装器会加入 PATH）。
    $fromPath = (Get-Command iscc -ErrorAction SilentlyContinue).Source
    if ($fromPath -and (Test-Path -LiteralPath $fromPath)) {
        return $fromPath
    }

    # 非默认安装盘（如 D:\Inno Setup 7）：从卸载项读取 InstallLocation。
    $uninstallRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    foreach ($root in $uninstallRoots) {
        $entries = Get-ItemProperty -Path $root -ErrorAction SilentlyContinue |
            Where-Object { $_.DisplayName -like '*Inno Setup*' -and $_.InstallLocation }
        foreach ($entry in $entries) {
            $candidate = Join-Path $entry.InstallLocation 'ISCC.exe'
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    # 未安装 Inno Setup 时使用 NuGet 上的免安装发行版（仅还原编译器，不改系统环境）。
    # 选 7.1.0：包内自带简体中文语言文件；免费版限非商业用途（见 docs/P4-05 第 10 节）。
    $packageVersion = '7.1.0'
    $globalPackages = ((dotnet nuget locals global-packages --list) -join "`n")
    $globalPackages = ($globalPackages -replace '(?m)^.*?:\s*', '').Trim()
    if (-not $globalPackages) {
        throw '无法确定 NuGet 全局包目录。'
    }

    $iscc = Join-Path $globalPackages "tools.innosetup\$packageVersion\tools\ISCC.exe"
    if (Test-Path -LiteralPath $iscc) {
        return $iscc
    }

    Write-Host "[info] 本地未安装 Inno Setup，正在从 NuGet 还原 Tools.InnoSetup $packageVersion ..."
    $probeDir = Join-Path $env:TEMP ("lumina-inno-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $probeDir -Force | Out-Null
    $probeProject = Join-Path $probeDir 'InnoTool.csproj'
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Tools.InnoSetup" Version="$packageVersion" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $probeProject -Encoding UTF8

    & dotnet restore $probeProject --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "还原 Tools.InnoSetup 失败（exit $LASTEXITCODE）。"
    }
    if (-not (Test-Path -LiteralPath $iscc)) {
        throw "未找到 ISCC.exe：$iscc"
    }

    return $iscc
}

if (-not $Version) {
    $Version = Read-AppVersion
}
if (-not $AudioSource) {
    $AudioSource = Join-Path $repoRoot 'app_audio'
}
if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot '.artifacts\release'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot "$Version-$stamp"))
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot '.artifacts\staging'))
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $stagingRoot "$Version-$stamp"))
New-Item -ItemType Directory -Path $runDir, $publishDir -Force | Out-Null

Write-Host "==> 发布 $Version ($Runtime) 到 $publishDir"
& dotnet publish 'Lumina.App/Lumina.App.csproj' -c Release -r $Runtime --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败（exit $LASTEXITCODE）。"
}

$builtExe = Join-Path $publishDir 'Lumina.App.exe'
if (-not (Test-Path -LiteralPath $builtExe)) {
    throw "发布输出缺少 Lumina.App.exe：$publishDir"
}
$appExe = Join-Path $publishDir 'Lumina.exe'
Move-Item -LiteralPath $builtExe -Destination $appExe -Force
Write-Host ("[ok] 单文件程序 {0:N1} MB" -f ((Get-Item -LiteralPath $appExe).Length / 1MB))

if (-not $SkipAudio) {
    if (-not (Test-Path -LiteralPath $AudioSource)) {
        throw "音频源不存在：$AudioSource。用 -AudioSource 指定，或加 -SkipAudio 跳过。"
    }

    $audioTarget = Join-Path $publishDir 'Audio'
    New-Item -ItemType Directory -Path $audioTarget -Force | Out-Null
    Get-ChildItem -LiteralPath $AudioSource -Directory | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $audioTarget -Recurse -Force
    }

    $audioExtensions = @('.wav', '.ogg', '.mp3')
    $sourceCount = @(Get-ChildItem -LiteralPath $AudioSource -Recurse -File |
        Where-Object { $audioExtensions -contains $_.Extension.ToLowerInvariant() }).Count
    $copied = @(Get-ChildItem -LiteralPath $audioTarget -Recurse -File |
        Where-Object { $audioExtensions -contains $_.Extension.ToLowerInvariant() })
    if ($sourceCount -ne $copied.Count) {
        throw "音频复制不完整：源 $sourceCount 个，目标 $($copied.Count) 个。"
    }
    $audioSize = ($copied | Measure-Object Length -Sum).Sum
    Write-Host ("[ok] 音源 {0} 个 / {1:N0} MB" -f $copied.Count, ($audioSize / 1MB))
}

$setupPath = $null
if (-not $SkipInstaller) {
    $iscc = Resolve-InnoCompiler
    Write-Host "==> 编译安装包（ISCC: $iscc）"
    & $iscc "/DAppVersion=$Version" "/DPublishDir=$publishDir" "/DOutputDir=$runDir" `
        (Join-Path $repoRoot 'installer\Lumina.iss')
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup 编译失败（exit $LASTEXITCODE）。"
    }

    $setupPath = Join-Path $runDir "Lumina-$Version-win-x64-setup.exe"
    if (-not (Test-Path -LiteralPath $setupPath)) {
        throw "未找到安装包输出：$setupPath"
    }
    Write-Host ("[ok] 安装包 {0:N0} MB" -f ((Get-Item -LiteralPath $setupPath).Length / 1MB))
}

$artifacts = @()
if ($setupPath) {
    $artifacts += $setupPath
}
$hashLines = foreach ($file in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, (Split-Path -Leaf $file)
}
if ($hashLines) {
    $hashLines | Set-Content -LiteralPath (Join-Path $runDir 'SHA256SUMS.txt') -Encoding ASCII
}

$commit = & git rev-parse HEAD 2>$null
$info = @(
    "version=$Version",
    "runtime=$Runtime",
    "built=$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss zzz'))",
    "commit=$commit",
    "singlefile=$([math]::Round((Get-Item -LiteralPath $appExe).Length / 1MB, 1)) MB",
    "installer=$(if ($setupPath) { [math]::Round((Get-Item -LiteralPath $setupPath).Length / 1MB, 1) } else { 'skipped' })",
    "staging=$publishDir"
)
$info | Set-Content -LiteralPath (Join-Path $runDir 'build-info.txt') -Encoding UTF8

Write-Host ''
Write-Host "[ok] 发布产物目录：$runDir"
if ($hashLines) {
    Get-Content -LiteralPath (Join-Path $runDir 'SHA256SUMS.txt')
}
