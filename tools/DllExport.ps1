<#
  Adds native (unmanaged) exports to a .NET Framework assembly, so Notepad++ can load it as a plugin.

  Every static method marked with [DllExport] gets an ".export" directive. The assembly is
  disassembled with ildasm, patched and reassembled with the ilasm that ships with .NET Framework 4.
  This replaces the old NppPlugin.DllExport MSBuild task, which needed a full Visual Studio install.
#>
param(
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][ValidateSet('x64', 'x86')][string]$Platform,
    [Parameter(Mandatory = $true)][string]$IlDasm,
    [Parameter(Mandatory = $true)][string]$IlAsm
)
$ErrorActionPreference = 'Stop'

foreach ($tool in @($IlDasm, $IlAsm)) {
    if (-not (Test-Path $tool)) { throw "DllExport: tool not found: $tool" }
}

$work = Join-Path ([IO.Path]::GetDirectoryName($Assembly)) 'dllexport.tmp'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory $work | Out-Null

$il = Join-Path $work 'assembly.il'
$res = Join-Path $work 'assembly.res'

& $IlDasm /QUOTEALLNAMES /UNICODE /TYPELIST /NOBAR "/OUT=$il" $Assembly 2>&1 | Out-Null
if (-not (Test-Path $il)) {
    # /NOBAR is not known by every ildasm version
    & $IlDasm /QUOTEALLNAMES /UNICODE /TYPELIST "/OUT=$il" $Assembly
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $il)) { throw "DllExport: ildasm failed ($LASTEXITCODE)" }
}

$lines = [IO.File]::ReadAllLines($il, [Text.Encoding]::Unicode)
$out = New-Object System.Collections.Generic.List[string]
$cdecl = 'modopt([mscorlib]System.Runtime.CompilerServices.CallConvCdecl)'
$methodStart = -1
$exportIndex = 1
$exports = @()

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]

    if ($line -match '^\s*\.corflags\s') {
        $flags = if ($Platform -eq 'x86') { '0x00000002' } else { '0x00000000' }
        $out.Add(".corflags $flags")
        continue
    }

    if ($line -match '^\s*\.method\s') { $methodStart = $out.Count }

    if ($line -match '\.custom\s+instance\s+void\s+.*DllExportAttribute''?::') {
        # Skip the attribute (its blob may continue on the following lines)
        $depth = ([regex]::Matches($line, '\(')).Count - ([regex]::Matches($line, '\)')).Count
        while ($depth -gt 0 -and $i + 1 -lt $lines.Length) {
            $i++
            $depth += ([regex]::Matches($lines[$i], '\(')).Count - ([regex]::Matches($lines[$i], '\)')).Count
        }

        # Find the method name in its header (first quoted identifier followed by '(')
        $header = ($out.GetRange($methodStart, $out.Count - $methodStart) -join "`n")
        $m = [regex]::Match($header, "'([^']+)'\s*\(")
        if (-not $m.Success) { throw "DllExport: cannot find the method name in:`n$header" }
        $name = $m.Groups[1].Value

        if ($Platform -eq 'x86') {
            # cdecl calling convention for 32-bit exports
            for ($k = $methodStart; $k -lt $out.Count; $k++) {
                if ($out[$k] -match [regex]::Escape("'$name'") + '\s*\(') {
                    $out[$k] = $out[$k] -replace ([regex]::Escape("'$name'") + '(\s*\()'), "$cdecl '$name'`$1"
                    break
                }
            }
        }

        $out.Add("    .export [$exportIndex] as '$name'")
        $exports += $name
        $exportIndex++
        continue
    }

    $out.Add($line)
}

if ($exports.Count -eq 0) { throw 'DllExport: no [DllExport] methods found' }
[IO.File]::WriteAllLines($il, $out, [Text.Encoding]::Unicode)

$ilasmArgs = @('/QUIET', '/NOLOGO', '/DLL', "/OUTPUT=$Assembly")
if (Test-Path $res) { $ilasmArgs += "/RESOURCE=$res" }
if ($Platform -eq 'x64') { $ilasmArgs += @('/X64', '/PE64') }
$ilasmArgs += $il

Push-Location $work   # ilasm finds the embedded .resources files next to the .il
try {
    & $IlAsm @ilasmArgs
    if ($LASTEXITCODE -ne 0) { throw "DllExport: ilasm failed ($LASTEXITCODE)" }
}
finally { Pop-Location }

Remove-Item $work -Recurse -Force
Write-Host "DllExport: exported $($exports -join ', ') ($Platform)"
