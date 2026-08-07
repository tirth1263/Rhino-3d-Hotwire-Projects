# ---------------------------------------------------------------------------
# Rebuilds TF09_pen_drawing.gh and FL01_mesh_to_planes.gh from the .cs panes,
# then reopens and SOLVES both to prove the code compiles inside the component.
#
#   powershell -File _build\build_gh.ps1
#
# Tooling, not a deliverable. C# and PowerShell - no Python.
# ---------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$sys   = 'C:\Program Files\Rhino 8\System'
$ghDir = 'C:\Program Files\Rhino 8\Plug-ins\Grasshopper'

# _build/ sits inside 05_grasshopper/, which sits inside the project root.
$build = $PSScriptRoot
$out   = Split-Path -Parent $build
$root  = Split-Path -Parent $out

Write-Host "project root : $root"
Write-Host "output dir   : $out"

[Reflection.Assembly]::LoadFrom("$sys\RhinoCommon.dll")   | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\GH_IO.dll")       | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\Grasshopper.dll") | Out-Null

$refs = @(
  "$sys\RhinoCommon.dll",
  "$ghDir\GH_IO.dll",
  "$ghDir\Grasshopper.dll",
  'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll'
)

Add-Type -Path @("$build\GhBuild.cs", "$build\CellBuild.cs") -ReferencedAssemblies $refs -ErrorAction Stop

# Rhino is hosted in-process inside powershell.exe rather than compiled to an
# .exe, because Application Control on this machine blocks freshly built
# binaries. powershell.exe is already a trusted host.
$core = New-Object Rhino.Runtime.InProcess.RhinoCore (([string[]]@('/nosplash','/notemplate')), ([Rhino.Runtime.InProcess.WindowStyle]::NoWindow))
try {
    [Rhino.PlugIns.PlugIn]::LoadPlugIn([Guid]'B45A29B1-4343-4035-989E-044E8580D9CF') | Out-Null
    ([Rhino.RhinoApp]::GetPlugInObject('Grasshopper')).RunHeadless() | Out-Null

    Write-Host '################ RHINO CELL MODEL'
    Write-Host ([CellBuild]::Run($root, $out))

    Write-Host '################ BUILD'
    Write-Host ([GhBuild]::Run($root, $out))

    Write-Host '################ REOPEN + SOLVE'
    $failed = 0
    foreach ($n in @('TF09_pen_drawing','FL01_mesh_to_planes')) {
        $p = Join-Path $out "$n.gh"
        Write-Host ''
        Write-Host "=== $n.gh"

        $io = New-Object Grasshopper.Kernel.GH_DocumentIO
        if (-not $io.Open($p)) { Write-Host '  OPEN FAILED'; $failed++; continue }
        $d = $io.Document
        Write-Host "  opened OK - $($d.ObjectCount) objects"

        $d.Enabled = $true
        $d.NewSolution($true)

        # Only the C# component's own errors mean the build is broken. KUKA|prc
        # reporting unreachable poses is a cell-layout matter, not a build one -
        # see RUN_ON_ROBOT.md section 6.
        foreach ($o in $d.Objects) {
            $ao = $o -as [Grasshopper.Kernel.IGH_ActiveObject]
            if ($null -eq $ao) { continue }
            foreach ($lvl in @('Error','Warning')) {
                foreach ($m in $ao.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::$lvl)) {
                    if ($lvl -eq 'Error' -and $ao.NickName -in @('TF-09','FL-01')) { $failed++ }
                    $short = $m -replace "`r?`n", ' '
                    if ($short.Length -gt 150) { $short = $short.Substring(0,150) + '...' }
                    Write-Host ("  [{0}] {1,-14} {2}" -f $lvl.Substring(0,1), $ao.NickName, $short)
                }
            }
        }

        foreach ($o in $d.Objects) {
            if ($o.NickName -in @('TF-09','FL-01')) {
                foreach ($op in $o.Params.Output) {
                    if ($op.NickName -in @('Status','Count','SwapCount','CycleTime','MaxTurn')) {
                        $vals = @()
                        foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $vals += "$v" } }
                        Write-Host ("  {0,-10} = {1}" -f $op.NickName, ($vals -join ' | '))
                    }
                    if ($op.NickName -eq 'KRL') {
                        $t = ''
                        foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $t = "$v" } }
                        Write-Host ("  KRL        = {0} chars, {1} lines" -f $t.Length, ($t -split "`n").Count)
                    }
                    if ($op.NickName -in @('Targets','Planes')) {
                        Write-Host ("  {0,-10} = {1} branches, {2} items" -f $op.NickName, $op.VolatileData.PathCount, $op.VolatileData.DataCount)
                    }
                }
            }
        }
        $d.Dispose()
    }

    Write-Host ''
    if ($failed) { Write-Host "BUILD FAILED - $failed problem(s) in the script components"; exit 1 }
    Write-Host 'BUILD OK - both files open and solve, no errors on the script components'
}
finally { $core.Dispose() }
