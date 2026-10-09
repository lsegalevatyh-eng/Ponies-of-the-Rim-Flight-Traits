@echo off
setlocal

rem Folder containing the mod.
set "MOD_ROOT=C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Poniesofhe"

rem Output file will be created next to this BAT.
set "OUT_FILE=%~dp0ModularArmor_all.txt"

rem Path to this BAT, used to load the PowerShell section.
set "BAT_SELF=%~f0"

echo Collecting mod files...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$text = [System.IO.File]::ReadAllText($env:BAT_SELF); $parts = $text -split '(?m)^# POWERSHELL_START\r?$', 2; if ($parts.Count -ne 2) { throw 'PowerShell section not found.' }; & ([scriptblock]::Create($parts[1]))"

set "RESULT=%ERRORLEVEL%"

echo.
if not "%RESULT%"=="0" (
    echo ERROR: Could not create the output file.
)

pause
exit /b %RESULT%

# POWERSHELL_START

$ErrorActionPreference = 'Stop'
$writer = $null

try {
    $root = [System.IO.Path]::GetFullPath($env:MOD_ROOT)
    $output = [System.IO.Path]::GetFullPath($env:OUT_FILE)
    $batFile = [System.IO.Path]::GetFullPath($env:BAT_SELF)

    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "Mod folder not found: $root"
    }

    # Collect the file list before creating the output.
    # Exclude this BAT and its output if they are inside the mod.
    $files = @(
        Get-ChildItem -LiteralPath $root -Recurse -File -Force |
        Where-Object {
            $_.FullName -ne $output -and
            $_.FullName -ne $batFile
        } |
        Sort-Object FullName
    )

    $folders = @(
        Get-ChildItem -LiteralPath $root -Recurse -Directory -Force |
        Sort-Object FullName
    )

    # UTF-8 with BOM: suitable for Windows Notepad.
    $encoding = [System.Text.UTF8Encoding]::new($true)
    $writer = [System.IO.StreamWriter]::new(
        $output,
        $false,
        $encoding
    )

    $writer.WriteLine('FOLDER STRUCTURE')
    $writer.WriteLine('================')
    $writer.WriteLine('"' + $root + '"')

    foreach ($folder in $folders) {
        $writer.WriteLine('"' + $folder.FullName + '"')
    }

    $writer.WriteLine()
    $writer.WriteLine('FILES')
    $writer.WriteLine('=====')

    foreach ($file in $files) {
        $writer.WriteLine()
        $writer.WriteLine('------------------------------------------------------------')
        $writer.WriteLine('"' + $file.FullName + '"')

        # Read only C# and XML. Other files are listed by path only.
        if ($file.Extension.ToLowerInvariant() -in @('.cs', '.xml')) {
            $writer.WriteLine()

            try {
                $content = [System.IO.File]::ReadAllText($file.FullName)
            }
            catch {
                # If a text file cannot be read, leave only its path.
                Write-Warning ("Cannot read: " + $file.FullName)
                continue
            }

            $writer.WriteLine($content)
        }
    }

    $writer.Dispose()
    $writer = $null

    Write-Host 'Done!'
    Write-Host ('Output: ' + $output)
    Write-Host ('Files listed: ' + $files.Count)
}
catch {
    Write-Host ('ERROR: ' + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
finally {
    if ($null -ne $writer) {
        $writer.Dispose()
    }
}