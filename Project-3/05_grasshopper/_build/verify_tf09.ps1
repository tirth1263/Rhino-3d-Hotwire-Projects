# ---------------------------------------------------------------------------
# The TF-09 evidence run. Sweeps the board switch and the pen lean against the
# REAL definition and prints what KUKA|prc says to each, so the numbers quoted
# in TF09_ORIENTATION.md are reproducible rather than remembered.
#
#   powershell -File _build\verify_tf09.ps1
#
# Reports prc's messages by LEVEL, because the distinction matters here:
#   ERROR   unreachable or collided - the job will not run
#   WARN    possible singularity - it will run, watch the axis speeds
#
# Tooling, not a deliverable.
# ---------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$sys   = 'C:\Program Files\Rhino 8\System'
$ghDir = 'C:\Program Files\Rhino 8\Plug-ins\Grasshopper'
$build = $PSScriptRoot
$out   = Split-Path -Parent $build

[Reflection.Assembly]::LoadFrom("$sys\RhinoCommon.dll")   | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\GH_IO.dll")       | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\Grasshopper.dll") | Out-Null

$core = New-Object Rhino.Runtime.InProcess.RhinoCore (([string[]]@('/nosplash','/notemplate')), ([Rhino.Runtime.InProcess.WindowStyle]::NoWindow))
try {
    [Rhino.PlugIns.PlugIn]::LoadPlugIn([Guid]'B45A29B1-4343-4035-989E-044E8580D9CF') | Out-Null
    ([Rhino.RhinoApp]::GetPlugInObject('Grasshopper')).RunHeadless() | Out-Null

    $io = New-Object Grasshopper.Kernel.GH_DocumentIO
    $io.Open((Join-Path $out 'TF09_pen_drawing.gh')) | Out-Null
    $d = $io.Document; $d.Enabled = $true

    $sl = @{}; $tg = @{}; $prc = $null; $pt = $null; $tf = $null
    foreach ($o in $d.Objects) {
        if ($o -is [Grasshopper.Kernel.Special.GH_NumberSlider])  { $sl[$o.NickName] = $o }
        if ($o -is [Grasshopper.Kernel.Special.GH_BooleanToggle]) { $tg[$o.NickName] = $o }
        if ($o.NickName -eq 'KUKA|prc') { $prc = $o }
        if ($o.NickName -eq 'PENTOOL')  { $pt  = $o }
        if ($o.NickName -eq 'TF-09')    { $tf  = $o }
    }
    function S($n, $v) { $sl[$n].SetSliderValue([decimal]$v); $sl[$n].ExpireSolution($false) }
    function T($n, $v) { $tg[$n].Value = $v; $tg[$n].ExpireSolution($false) }

    function Row($label) {
        $d.NewSolution($true)
        $e = $prc.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Error).Count
        $w = $prc.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Warning).Count
        $pw = $pt.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Warning).Count

        $verdict = if ($e -gt 0) { 'UNREACHABLE' } elseif ($w -gt 0) { 'singularity warn' } else { 'CLEAN' }

        # the board's own Z against the horizontal direction back to the robot
        $dp = $null
        foreach ($op in $pt.Params.Output) {
            if ($op.NickName -ne 'DrawPlane') { continue }
            foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $dp = $v.Value } }
        }
        $facing = 0.0
        if ($null -ne $dp) {
            $toBase = New-Object Rhino.Geometry.Vector3d (-$dp.Origin.X), (-$dp.Origin.Y), 0.0
            if ($toBase.Unitize()) { $facing = $dp.ZAxis.X * $toBase.X + $dp.ZAxis.Y * $toBase.Y }
        }
        Write-Host ("  {0,-30} {1,-18} pentool warn {2}   board Z faces robot {3,5:0.00}" -f `
                    $label, $verdict, $pw, $facing)
    }

    Write-Host '=== 1. penLeanDeg, board VERTICAL at 900 / 0 / 450'
    foreach ($t in 0, 5, 10, 12, 15, 20, 25, 30, 35, 40, -20) {
        S 'penLeanDeg' $t
        Row ("penLeanDeg = {0}" -f $t)
    }
    S 'penLeanDeg' 20

    Write-Host ''
    Write-Host '=== 2. boardOrient, at the shipped lean of 20'
    foreach ($m in 0, 1, 2, 3) {
        S 'boardOrient' $m
        Row ("boardOrient = {0}" -f $m)
    }
    S 'boardOrient' 0

    Write-Host ''
    Write-Host '=== 3. cardinal AUTO follows the board round the cell'
    foreach ($cfg in @(@(900, 0, 'in front'), @(0, 900, 'to the left'), @(0, -900, 'to the right'), @(-900, 0, 'behind'))) {
        S 'board X' $cfg[0]; S 'board Y' $cfg[1]
        Row ("board {0} / {1}  ({2})" -f $cfg[0], $cfg[1], $cfg[2])
    }
    S 'board X' 900; S 'board Y' 0

    Write-Host ''
    Write-Host '=== 4. the reach ring - how far can the sheet be'
    foreach ($x in 500, 600, 700, 900, 1100, 1200, 1300) {
        S 'board X' $x
        Row ("board X = {0}" -f $x)
    }
    S 'board X' 900

    Write-Host ''
    Write-Host '=== 5. the shipped setup, and the self-test'
    Row 'as shipped'
    T 'selfTest' $true
    $d.NewSolution($true)
    foreach ($op in $tf.Params.Output) {
        if ($op.NickName -ne 'SelfTest') { continue }
        foreach ($b in $op.VolatileData.StructureProxy) {
            foreach ($v in $b) {
                foreach ($ln in ("$v" -split "`r?`n")) {
                    if ($ln -match 'RESULT|checks|worst|PASS|FAIL') { Write-Host "  $ln" }
                }
            }
        }
    }
    T 'selfTest' $false

    $d.Dispose()
}
finally { $core.Dispose() }
