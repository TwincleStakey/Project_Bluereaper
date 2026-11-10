param(
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Unity.com'
)

$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$validationRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'Temp/BossPatternValidation'))
$expectedRoot = [System.IO.Path]::Combine($projectRoot, 'Temp', 'BossPatternValidation')
if ($validationRoot -ne $expectedRoot) { throw 'Unexpected validation directory.' }

# Only rebuild the disposable copy; the open Editor and its scenes remain untouched.
if (Test-Path -LiteralPath $validationRoot) { Remove-Item -LiteralPath $validationRoot -Recurse -Force }
$runtime = Join-Path $validationRoot 'Assets/Runtime'
$testFolder = Join-Path $validationRoot 'Assets/Tests'
$resources = Join-Path $validationRoot 'Assets/Resources'
foreach ($folder in @($runtime, $testFolder, $resources, "$validationRoot/Packages", "$validationRoot/ProjectSettings")) {
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
}

$scriptsRoot = Join-Path $projectRoot 'Assets/_Project/Scripts'
foreach ($folder in @('Core', 'HitBox', 'Boss/Patterns', 'Reward', 'Stage', 'UI')) {
    foreach ($source in Get-ChildItem -LiteralPath (Join-Path $scriptsRoot $folder) -Filter '*.cs' -Recurse) {
        $relative = $source.FullName.Substring($scriptsRoot.Length + 1)
        $target = Join-Path $runtime $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $source.FullName -Destination $target
        if (Test-Path -LiteralPath ($source.FullName + '.meta')) {
            Copy-Item -LiteralPath ($source.FullName + '.meta') -Destination ($target + '.meta')
        }
    }
}
Set-Content -LiteralPath "$runtime/Runtime.asmdef" -Value '{"name":"BossPatternValidation.Runtime","references":["UnityEngine.UI","Unity.TextMeshPro"]}'
Set-Content -LiteralPath "$testFolder/Tests.asmdef" -Value '{"name":"BossPatternValidation.PlayModeTests","references":["BossPatternValidation.Runtime","UnityEngine.UI","Unity.TextMeshPro"],"optionalUnityReferences":["TestAssemblies"]}'
foreach ($testSource in Get-ChildItem -LiteralPath "$PSScriptRoot/PlayMode" -Filter '*.cs') {
    Copy-Item -LiteralPath $testSource.FullName -Destination $testFolder
}
foreach ($source in Get-ChildItem -LiteralPath "$projectRoot/Assets/_Project/Anims/Boss" -File) {
    Copy-Item -LiteralPath $source.FullName -Destination $resources
}
foreach ($folder in @('Data/Bosses', 'Data/Rewards', 'Prefabs/Boss', 'Prefabs/Projectiles')) {
    foreach ($source in Get-ChildItem -LiteralPath "$projectRoot/Assets/_Project/$folder" -File) {
        Copy-Item -LiteralPath $source.FullName -Destination $resources
    }
}

$manifest = Get-Content -LiteralPath "$projectRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$dependencies = [ordered]@{}
foreach ($entry in $manifest.dependencies.PSObject.Properties) {
    if ($entry.Name.StartsWith('com.unity.modules.')) { $dependencies[$entry.Name] = $entry.Value }
}
foreach ($packageName in @('com.unity.test-framework', 'com.unity.ext.nunit', 'com.unity.ugui')) {
    $package = Get-ChildItem -LiteralPath "$projectRoot/Library/PackageCache" -Directory -Filter "$packageName@*" | Select-Object -First 1
    if (-not $package) { throw "Installed package not found: $packageName" }
    $dependencies[$packageName] = 'file:' + $package.FullName.Replace('\', '/')
}
@{ dependencies = $dependencies } | ConvertTo-Json | Set-Content -LiteralPath "$validationRoot/Packages/manifest.json"
foreach ($file in @('ProjectVersion.txt', 'TagManager.asset')) {
    Copy-Item -LiteralPath "$projectRoot/ProjectSettings/$file" -Destination "$validationRoot/ProjectSettings"
}

New-Item -ItemType Directory -Path "$projectRoot/Logs" -Force | Out-Null
$resultPath = "$projectRoot/Logs/BossPatternPlayMode.xml"
$logPath = "$projectRoot/Logs/BossPatternPlayMode.log"
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$arguments = @('-batchmode', '-nographics', '-projectPath', "`"$validationRoot`"", '-runTests', '-testPlatform', 'PlayMode', '-testResults', "`"$resultPath`"", '-logFile', "`"$logPath`"")
$validationUnity = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -PassThru
$validationUnity.WaitForExit()
if (-not (Test-Path -LiteralPath $resultPath)) { throw "No test results. See $logPath" }
[xml]$results = Get-Content -LiteralPath $resultPath
$summary = $results.'test-run'
Write-Output "Boss pattern PlayMode: $($summary.passed) passed, $($summary.failed) failed, $($summary.skipped) skipped."
if ($summary.failed -ne '0' -or [int]$summary.passed -eq 0) { exit 1 }
