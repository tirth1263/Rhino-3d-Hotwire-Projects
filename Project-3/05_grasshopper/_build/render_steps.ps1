# ---------------------------------------------------------------------------
# Renders one image per configuration, driving the REAL FL01_mesh_to_planes.gh
# so every picture is of geometry the definition actually produced.
#
#   powershell -File _build\render_steps.ps1
#
# These are renders, not screen captures - Rhino runs headless here and there
# is no window to photograph. Each image says so on its face.
# ---------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$sys   = 'C:\Program Files\Rhino 8\System'
$ghDir = 'C:\Program Files\Rhino 8\Plug-ins\Grasshopper'
$build = $PSScriptRoot
$out   = Split-Path -Parent $build
$imgs  = Join-Path $out 'renders_hotwire'

[Reflection.Assembly]::LoadFrom("$sys\RhinoCommon.dll")   | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\GH_IO.dll")       | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\Grasshopper.dll") | Out-Null

$refs = @("$sys\RhinoCommon.dll", "$ghDir\GH_IO.dll", "$ghDir\Grasshopper.dll",
          'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll')
Add-Type -Path @("$build\GhBuild.cs", "$build\CellBuild.cs", "$build\RenderCell.cs") `
         -ReferencedAssemblies $refs -ErrorAction Stop

# CellBuild is asked for the tool mesh directly here, without a full build
# running first, so it has to be told where the project is.
$root = Split-Path -Parent $out
[CellBuild]::SetRoot($root)

$core = New-Object Rhino.Runtime.InProcess.RhinoCore (([string[]]@('/nosplash','/notemplate')), ([Rhino.Runtime.InProcess.WindowStyle]::NoWindow))
try {
    [Rhino.PlugIns.PlugIn]::LoadPlugIn([Guid]'B45A29B1-4343-4035-989E-044E8580D9CF') | Out-Null
    ([Rhino.RhinoApp]::GetPlugInObject('Grasshopper')).RunHeadless() | Out-Null

    $io = New-Object Grasshopper.Kernel.GH_DocumentIO
    $io.Open((Join-Path $out 'FL01_mesh_to_planes.gh')) | Out-Null
    $d = $io.Document; $d.Enabled = $true

    $sl = @{}; $prc = $null; $hw = $null; $fl = $null
    foreach ($o in $d.Objects) {
        if ($o -is [Grasshopper.Kernel.Special.GH_NumberSlider]) { $sl[$o.NickName] = $o }
        if ($o.NickName -eq 'KUKA|prc') { $prc = $o }
        if ($o.NickName -eq 'HOTWIRE')  { $hw  = $o }
        if ($o.NickName -eq 'FL-01')    { $fl  = $o }
    }
    function S($n,$v){ $sl[$n].SetSliderValue([decimal]$v); $sl[$n].ExpireSolution($false) }

    function Grab($title, $sub) {
        $d.NewSolution($true)

        $sc = New-Object RenderCell+Scene
        $sc.Title = $title; $sc.Sub = $sub

        foreach ($op in $hw.Params.Output) {
            switch ($op.NickName) {
                'Targets'   { foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Targets.Add($v.Value) } } }
                'WireLines' { foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Wires.Add($v.Value) } } }
                'FlangePts' { foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Flange.Add($v.Value) } } }
            }
        }
        # the part, as the definition currently has it placed
        foreach ($ip in $fl.Params.Input) {
            if ($ip.NickName -eq 'geo') {
                foreach ($b in $ip.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Part = $v.Value } }
            }
        }

        # the hotwire itself, carried onto one representative target.
        # The tool mesh is in FLANGE coordinates and the tool plane says where
        # the TCP sits within them, so mapping toolPlane -> target is exactly
        # the transform the robot applies.
        $toolPlane = $null
        foreach ($op in $hw.Params.Output) {
            if ($op.NickName -eq 'ToolPlane') {
                foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $toolPlane = $v.Value } }
            }
        }
        if ($null -ne $toolPlane -and $sc.Targets.Count -gt 0) {
            $mid = [int]($sc.Targets.Count / 2)
            $x = [Rhino.Geometry.Transform]::PlaneToPlane($toolPlane, $sc.Targets[$mid])
            $tm = [CellBuild]::ReducedTool().DuplicateMesh()
            $tm.Transform($x) | Out-Null
            $sc.Tool = $tm
        }

        $e = $prc.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Error).Count
        $w = $hw.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Warning).Count
        if ($e -gt 0) { $sc.Verdict = 'KUKA|prc: UNREACHABLE'; $sc.Bad = $true }
        elseif ($w -gt 0) { $sc.Verdict = "hotwire: $w warning(s)"; $sc.Bad = $true }
        else { $sc.Verdict = 'KUKA|prc: CLEAN'; $sc.Bad = $false }
        return $sc
    }

    $scenes = New-Object 'System.Collections.Generic.List[RenderCell+Scene]'

    # 1 - the problem
    S 'frameMode' 0; S 'part X' 1273; S 'part Y' -20; S 'pass select' 0
    $scenes.Add((Grab 'Step 1 - the problem' `
      'frameMode 0 KEEP. FL-01 hands out a RADIAL Z, so the wire swings right round the loop and somewhere in every pass points back at the robot.'))

    # 2 - literal force
    S 'frameMode' 2
    $scenes.Add((Grab 'Step 2 - forcing Z literally' `
      'frameMode 2 WIRE. Z forced to the cardinal +X. The wire now points straight at the part - end-on. It would melt a pocket, not cut.'))

    # 3 - the fix
    S 'frameMode' 1
    $scenes.Add((Grab 'Step 3 - the fix, CUT mode' `
      'frameMode 1. The cardinal drives the ARM (red X); the wire (blue Z) lies across the travel and tangent - vertical, spanning the part height.'))

    # 4..7 - AUTO from each side
    S 'part X' 1273;  S 'part Y' -20;   $scenes.Add((Grab 'Step 4 - AUTO, part in front'  'cardinal 0 AUTO picks +X. Approach follows the part with no input from you.'))
    S 'part X' 20;    S 'part Y' 1273;  $scenes.Add((Grab 'Step 5 - AUTO, part to the left'  'Same definition, part moved. AUTO picks +Y.'))
    S 'part X' -20;   S 'part Y' -1273; $scenes.Add((Grab 'Step 6 - AUTO, part to the right' 'AUTO picks -Y.'))
    S 'part X' -1273; S 'part Y' 20;    $scenes.Add((Grab 'Step 7 - the blind spot behind'  'AUTO picks -X correctly, but axis 1 cannot wrap that far. 175-185 deg is unreachable at any orientation.'))

    # 8 - too close
    S 'part X' 800; S 'part Y' 0
    $scenes.Add((Grab 'Step 8 - too CLOSE, not too far' `
      'part X 800. The flange sits 422 mm back from the cut, so it lands inside the robot. Moving work nearer is not automatically the fix.'))

    # 9, 10 - the cut orientation variable
    S 'part X' 1273; S 'part Y' -20
    S 'cutOrient' 2
    $scenes.Add((Grab 'Step 9 - cutOrient 2, wire laid down' `
      'Wire ALONG the travel. It slides down the kerf it already made and removes nothing. Measured wire verticality 0.00.'))

    S 'cutOrient' 0
    $scenes.Add((Grab 'Step 10 - cutOrient 0, wire VERTICAL' `
      'Wire straight up and down, spanning the full height of the upright part. Measured verticality 1.00. This is the shipped default.'))

    # 11 - the working setup
    $scenes.Add((Grab 'Step 11 - the shipped setup' `
      'part 1273 / -20 / 302, frameMode 1, cardinal AUTO, cutOrient 0. All 12 passes solve clean.'))

    Write-Host ([RenderCell]::Run($imgs, $scenes))
    Write-Host "wrote $($scenes.Count) renders to $imgs"
    $d.Dispose()
}
finally { $core.Dispose() }
