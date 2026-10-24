# Requires Windows PowerShell 5.1 (included in Windows) and the .NET 10 SDK.
# Builds the public runtime package only; never archives the source directory.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$workDirectory = $null

try {
    $root = $PSScriptRoot
    $propsPath = Join-Path $root 'Directory.Build.props'
    $project = Join-Path $root 'src/MyToDoWidget/MyToDoWidget.csproj'
    [xml]$props = Get-Content -LiteralPath $propsPath -Raw
    $version = [string]$props.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw 'Directory.Build.props must contain a stable release version (for example, 2.0.1).'
    }
    if (-not (Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue)) {
        throw 'Install the .NET 10 SDK, then run build_release.bat again.'
    }

    $releaseName = 'MyToDo2-' + $version + '-win-x64'
    $outputDirectory = Join-Path $root 'dist'
    [void][IO.Directory]::CreateDirectory($outputDirectory)
    # Each run gets an empty directory. Old builds and local state cannot enter the ZIP.
    $workDirectory = Join-Path $outputDirectory ('.release-' + [Guid]::NewGuid().ToString('N'))
    $publishDirectory = Join-Path $workDirectory 'publish'
    $packageDirectory = Join-Path $workDirectory $releaseName
    [void][IO.Directory]::CreateDirectory($publishDirectory)
    [void][IO.Directory]::CreateDirectory($packageDirectory)

    Write-Host ('Building MyToDo2 ' + $version + ' for Windows x64 (standalone)...')
    $publishPath = $publishDirectory + [IO.Path]::DirectorySeparatorChar
    # Windows PowerShell 5.1 can misquote a space-containing native argument that
    # ends in a backslash. MSBuild accepts forward slashes, including the final
    # separator; keep the native path above for filesystem operations only.
    $msbuildPublishPath = $publishPath.Replace('\', '/')
    & dotnet publish $project --configuration Release --runtime win-x64 --self-contained true `
        '-p:PublishProfile=Standalone-win-x64' ('-p:PublishDir=' + $msbuildPublishPath) `
        ('-p:PublishUrl=' + $msbuildPublishPath) --nologo
    if ($LASTEXITCODE -ne 0) {
        throw ('dotnet publish failed (exit code ' + $LASTEXITCODE + '). ZIP and SHA-256 were not updated.')
    }

    # Defense in depth for old/custom publish profiles: local template addresses
    # must not remain in dist or enter the runtime ZIP, even when non-empty.
    foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Force |
            Where-Object { $_.Name -like 'TemplateSource*.json' }) {
        Remove-Item -LiteralPath $file.FullName -Force
    }

    $requiredFiles = @('MyToDo2.exe', 'README.txt', 'LICENSE.md', 'THIRD_PARTY_NOTICES.md')
    foreach ($name in $requiredFiles) {
        $source = Join-Path $publishDirectory $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw ('Required publish file is missing: ' + $name)
        }
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageDirectory $name)
    }

    $licenseFiles = @(Get-ChildItem -LiteralPath (Join-Path $publishDirectory 'licenses') -File -Filter '*.txt')
    if ($licenseFiles.Count -eq 0) { throw 'The publish output is missing third-party license files.' }
    $licenseDirectory = Join-Path $packageDirectory 'licenses'
    [void][IO.Directory]::CreateDirectory($licenseDirectory)
    foreach ($file in $licenseFiles) { Copy-Item -LiteralPath $file.FullName -Destination $licenseDirectory }

    # Fail instead of silently shipping extra settings, symbols, or an incomplete runtime.
    foreach ($file in Get-ChildItem -LiteralPath $publishDirectory -Recurse -File -Force) {
        $relative = $file.FullName.Substring($publishPath.Length).Replace('\', '/')
        if ($relative -notin $requiredFiles -and $relative -notmatch '^licenses/[^/]+\.txt$') {
            throw ('Unexpected publish file: ' + $relative + '. Review the runtime package list before release.')
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipName = $releaseName + '.zip'
    $zipPath = Join-Path $workDirectory $zipName
    [IO.Compression.ZipFile]::CreateFromDirectory($packageDirectory, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
    $hashName = $zipName + '.sha256'
    $hashPath = Join-Path $workDirectory $hashName
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($hashPath, ($hash + '  ' + $zipName + "`r`n"), [Text.Encoding]::ASCII)

    # Replace the previous runtime outputs only after publish, package, and hash succeed.
    $finalPackageDirectory = Join-Path $outputDirectory $releaseName
    if (Test-Path -LiteralPath $finalPackageDirectory) {
        Remove-Item -LiteralPath $finalPackageDirectory -Recurse -Force
    }
    Move-Item -LiteralPath $packageDirectory -Destination $finalPackageDirectory
    Move-Item -LiteralPath $zipPath -Destination (Join-Path $outputDirectory $zipName) -Force
    Move-Item -LiteralPath $hashPath -Destination (Join-Path $outputDirectory $hashName) -Force
    Write-Host ''
    Write-Host ('ZIP:    ' + (Join-Path $outputDirectory $zipName))
    Write-Host ('SHA256: ' + (Join-Path $outputDirectory $hashName))
    Write-Host ('Upload both files to GitHub Release ' + $version + '.')
}
catch {
    [Console]::Error.WriteLine('Release build failed: ' + $_.Exception.Message)
    exit 1
}
finally {
    if ($workDirectory -and (Test-Path -LiteralPath $workDirectory)) {
        Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
