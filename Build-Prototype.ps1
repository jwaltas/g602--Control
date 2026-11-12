$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$outputs = Join-Path (Split-Path $repo -Parent) 'outputs'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path $outputs | Out-Null
python (Join-Path $repo 'prepare_dependencies.py')
if ($LASTEXITCODE -ne 0) { throw 'Dependency verification failed' }
Copy-Item (Join-Path $repo 'default-macros.json') (Join-Path $outputs 'agent-macros.json')
function Build([string]$name, [string[]]$sources, [string[]]$extra) {
    $arguments = @('/nologo','/optimize+','/platform:x64',("/out:" + (Join-Path $outputs $name))) + $extra
    foreach ($source in $sources) { $arguments += Join-Path $repo ('agent\' + $source) }
    $arguments += Join-Path $repo 'agent\AppInfo.cs'
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
}
$common = @('SequenceRunner.cs','WindowsKeyboardSink.cs')
$product = @('AppPaths.cs','StartupSettings.cs','ProfileStore.cs','MacroEditor.cs') + $common
$ui = @('G602Control.cs','RawInputWindow.cs','TapClassifier.cs','ProfileActivation.cs','RuntimeContext.cs') + $product
$references = @('/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Web.Extensions.dll')
Build 'G602Control.exe' $ui (@('/target:winexe') + $references)
Build 'G602PlaybackCore.dll' $common @('/target:library')
Build 'G602SchedulerChecks.exe' @('SequenceRunner.cs','SchedulerChecks.cs') @('/target:exe','/reference:System.Web.Extensions.dll')
Build 'G602WindowsOutputChecks.exe' ($common + @('WindowsOutputChecks.cs')) @('/target:exe','/reference:System.Web.Extensions.dll')
Build 'G602AppChecks.exe' ($ui + @('AppChecks.cs')) (@('/target:exe','/main:AppChecks') + $references)
Build 'G602TapChecks.exe' @('TapClassifier.cs','TapChecks.cs') @('/target:exe','/reference:System.Web.Extensions.dll')
Build 'G602EditorChecks.exe' (@('MacroEditor.cs','EditorChecks.cs') + $common) @('/target:exe','/reference:System.Web.Extensions.dll')
Build 'G602ActivationChecks.exe' @('ProfileActivation.cs','ActivationChecks.cs') @('/target:exe','/reference:System.Web.Extensions.dll')
Build 'G602ProductChecks.exe' (@('ProductChecks.cs','InstallEngine.cs') + $product) @('/target:exe','/reference:System.Web.Extensions.dll')
Build 'G602Install.exe' (@('Installer.cs','InstallEngine.cs','AppPaths.cs','ProfileStore.cs','MacroEditor.cs') + $common) @('/target:exe','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll')
Build 'G602Open.exe' @('Launcher.cs','AppPaths.cs') @('/target:winexe','/reference:System.Windows.Forms.dll')
python -m unittest discover -s $repo -p 'test_*.py'
if ($LASTEXITCODE -ne 0) { throw 'Python checks failed' }
python (Join-Path $repo 'package_prototype.py')
if ($LASTEXITCODE -ne 0) { throw 'Preparing live configuration failed' }
& (Join-Path $outputs 'G602SchedulerChecks.exe') (Join-Path $outputs 'agent-macros.json') (Join-Path $outputs 'scheduler-checks.json') (Join-Path $outputs 'control-macros.json')
if ($LASTEXITCODE -ne 0) { throw 'Sequence checks failed' }
& (Join-Path $outputs 'G602WindowsOutputChecks.exe') (Join-Path $outputs 'agent-macros.json') (Join-Path $outputs 'windows-output-checks.json') (Join-Path $outputs 'control-macros.json')
if ($LASTEXITCODE -ne 0) { throw 'Windows output checks failed' }
& (Join-Path $outputs 'G602TapChecks.exe') (Join-Path $outputs 'tap-checks.json') (Join-Path $outputs 'control-macros.json')
if ($LASTEXITCODE -ne 0) { throw 'Tap checks failed' }
& (Join-Path $outputs 'G602EditorChecks.exe') (Join-Path $outputs 'control-macros.json') (Join-Path $outputs 'editor-checks.json')
if ($LASTEXITCODE -ne 0) { throw 'Editor checks failed' }
& (Join-Path $outputs 'G602ActivationChecks.exe') (Join-Path $outputs 'activation-checks.json')
if ($LASTEXITCODE -ne 0) { throw 'Activation checks failed' }
& (Join-Path $outputs 'G602ProductChecks.exe') (Join-Path $outputs 'control-macros.json') (Join-Path $outputs 'product-checks.json')
if ($LASTEXITCODE -ne 0) { throw 'Profile/startup/update checks failed' }
& (Join-Path $outputs 'G602AppChecks.exe') (Join-Path $outputs 'prototype-window.png') (Join-Path $outputs 'app-checks.json') (Join-Path $repo 'button-capture-fixture.json')
if ($LASTEXITCODE -ne 0) { throw 'App smoke/replay checks failed' }
python (Join-Path $repo 'package_prototype.py')
if ($LASTEXITCODE -ne 0) { throw 'Packaging failed' }
python (Join-Path $repo 'verify_package.py') (Join-Path $outputs 'G602-Control-0.3.0.zip')
if ($LASTEXITCODE -ne 0) { throw 'Packaged runtime verification failed' }
