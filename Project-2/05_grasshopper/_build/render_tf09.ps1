# ---------------------------------------------------------------------------
# One image per step of the TF-09 board switch, driving the REAL
# TF09_pen_drawing.gh so every picture is of geometry the definition actually
# produced - including the verdict, which is read off KUKA|prc rather than
# typed in.
#
#   powershell -File _build\render_tf09.ps1
#
# These are renders, not screen captures. Rhino runs headless here and there is
# no window to photograph. Each image says so on its face.
# ---------------------------------------------------------------------------
$ErrorActionPreference = 'Stop'

$sys   = 'C:\Program Files\Rhino 8\System'
$ghDir = 'C:\Program Files\Rhino 8\Plug-ins\Grasshopper'
$build = $PSScriptRoot
$out   = Split-Path -Parent $build
$imgs  = Join-Path $out 'renders_tf09'

[Reflection.Assembly]::LoadFrom("$sys\RhinoCommon.dll")   | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\GH_IO.dll")       | Out-Null
[Reflection.Assembly]::LoadFrom("$ghDir\Grasshopper.dll") | Out-Null

$refs = @("$sys\RhinoCommon.dll", "$ghDir\GH_IO.dll", "$ghDir\Grasshopper.dll",
          'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll')
Add-Type -Path @("$build\GhBuild.cs", "$build\CellBuild.cs", "$build\RenderCell.cs") `
         -ReferencedAssemblies $refs -ErrorAction Stop

$root = Split-Path -Parent $out
[CellBuild]::SetRoot($root)

$core = New-Object Rhino.Runtime.InProcess.RhinoCore (([string[]]@('/nosplash','/notemplate')), ([Rhino.Runtime.InProcess.WindowStyle]::NoWindow))
try {
    [Rhino.PlugIns.PlugIn]::LoadPlugIn([Guid]'B45A29B1-4343-4035-989E-044E8580D9CF') | Out-Null
    ([Rhino.RhinoApp]::GetPlugInObject('Grasshopper')).RunHeadless() | Out-Null

    $io = New-Object Grasshopper.Kernel.GH_DocumentIO
    $io.Open((Join-Path $out 'TF09_pen_drawing.gh')) | Out-Null
    $d = $io.Document; $d.Enabled = $true

    $sl = @{}; $prc = $null; $pt = $null; $tf = $null
    foreach ($o in $d.Objects) {
        if ($o -is [Grasshopper.Kernel.Special.GH_NumberSlider]) { $sl[$o.NickName] = $o }
        if ($o.NickName -eq 'KUKA|prc') { $prc = $o }
        if ($o.NickName -eq 'PENTOOL')  { $pt  = $o }
        if ($o.NickName -eq 'TF-09')    { $tf  = $o }
    }
    function S($n, $v) { $sl[$n].SetSliderValue([decimal]$v); $sl[$n].ExpireSolution($false) }

    function Grab($title, $sub) {
        $d.NewSolution($true)

        $sc = New-Object RenderCell+Scene
        $sc.Title = $title; $sc.Sub = $sub
        $sc.HeroLabel = 'pen'
        $sc.Legend = 'red = target X (travel)   blue = target Z (down the pen, INTO the paper - ' +
                     'the SHEET Z is the opposite one)   blue dots = the flange'

        # the board, the artwork, and where the wrist has to be
        foreach ($op in $pt.Params.Output) {
            switch ($op.NickName) {
                'Board'     { foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Curves.Add($v.Value) } } }
                'FlangePts' { foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Flange.Add($v.Value) } } }
                'PenLines'  { foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Wires.Add($v.Value) } } }
            }
        }
        # the strokes, as the definition currently has them placed
        foreach ($ip in $tf.Params.Input) {
            if ($ip.NickName -eq 'curves') {
                foreach ($b in $ip.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Curves.Add($v.Value) } }
            }
        }
        # the drawing targets themselves
        foreach ($op in $tf.Params.Output) {
            if ($op.NickName -eq 'Flat') {
                foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $sc.Targets.Add($v.Value) } }
            }
        }

        # the pen tool carried onto one representative target. The mesh is in
        # FLANGE coordinates and ToolPlane says where the nib sits within them,
        # so mapping ToolPlane -> target is exactly the transform the robot
        # applies.
        $toolPlane = $null
        foreach ($op in $pt.Params.Output) {
            if ($op.NickName -eq 'ToolPlane') {
                foreach ($b in $op.VolatileData.StructureProxy) { foreach ($v in $b) { $toolPlane = $v.Value } }
            }
        }
        if ($null -ne $toolPlane -and $sc.Targets.Count -gt 0) {
            $mid = [int]($sc.Targets.Count / 2)
            $x = [Rhino.Geometry.Transform]::PlaneToPlane($toolPlane, $sc.Targets[$mid])
            $tm = [CellBuild]::PenToolMesh().DuplicateMesh()
            $tm.Transform($x) | Out-Null
            $sc.Tool = $tm
        }

        $e = $prc.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Error).Count
        $w = $prc.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Warning).Count
        $pw = $pt.RuntimeMessages([Grasshopper.Kernel.GH_RuntimeMessageLevel]::Warning).Count
        if ($e -gt 0)       { $sc.Verdict = 'KUKA|prc: UNREACHABLE'; $sc.Bad = $true }
        elseif ($pw -gt 0)  { $sc.Verdict = "pen tool: $pw warning(s)"; $sc.Bad = $true }
        elseif ($w -gt 0)   { $sc.Verdict = 'KUKA|prc: runs, singularity warning'; $sc.Bad = $false }
        else                { $sc.Verdict = 'KUKA|prc: CLEAN'; $sc.Bad = $false }
        return $sc
    }

    $scenes = New-Object 'System.Collections.Generic.List[RenderCell+Scene]'

    # 1 - the old arrangement
    S 'boardOrient' 1; S 'board X' 600; S 'board Y' 0; S 'board Z' 200; S 'penLeanDeg' 0
    $scenes.Add((Grab 'Step 1 - the sheet lying flat' `
      'boardOrient 1 FLAT. The sheet is on a table and its normal points at the ceiling, so nothing about it faces the robot. This is where the file started.'))

    # 2 - standing it up, pen still square
    S 'boardOrient' 0; S 'board X' 900; S 'board Z' 450
    $scenes.Add((Grab 'Step 2 - standing it up, pen square to the paper' `
      'boardOrient 0. The sheet Z now runs back at the arm - which is what was asked for - but the pen is dead perpendicular to it, so it points straight down the reach line. Every target is inside the ring and prc still refuses.'))

    # 3 - the fix
    S 'penLeanDeg' 20
    $scenes.Add((Grab 'Step 3 - leaning the pen, the fix' `
      'penLeanDeg 20. Nothing moved. The pen just leans, the wrist has something to bend around, and the same job solves. Measured band: 15 to 30 works, 0 to 10 does not, 40 is too far the other way.'))

    # 4 - the wrong way round
    S 'boardOrient' 3
    $scenes.Add((Grab 'Step 4 - the sheet turned away' `
      'boardOrient 3 AWAY. The sheet faces off into the room and the arm has to reach round the back of its own work to draw on it. zToRobot turns it back and says that it did.'))

    # 5..7 - AUTO round the cell
    S 'boardOrient' 0
    S 'board X' 900;  S 'board Y' 0
    $scenes.Add((Grab 'Step 5 - AUTO, board in front' 'cardinal 0 AUTO picks +X and turns the sheet to face the arm with no input from you.'))
    S 'board X' 0; S 'board Y' 900; S 'slot X0' 420; S 'slot Y' 380
    $scenes.Add((Grab 'Step 6 - AUTO, board to the left' 'Same definition, board moved. AUTO picks +Y and the sheet turns with it. The magazine had to be moved too - it is bolted to the cell and does not follow.'))
    S 'board X' 0; S 'board Y' -900; S 'slot X0' -420; S 'slot Y' -380
    $scenes.Add((Grab 'Step 7 - AUTO, board to the right' 'AUTO picks -Y. Mirror image, and it solves exactly as well.'))

    # 8 - too close
    S 'board X' 600; S 'board Y' 0; S 'slot X0' 380; S 'slot Y' -420
    $scenes.Add((Grab 'Step 8 - too CLOSE, not too far' `
      'board X 600. The flange sits one whole tool length back from the nib, so it lands inside the robot. Bringing work nearer is not automatically the fix.'))

    # 9 - the shipped setup
    S 'board X' 900
    $scenes.Add((Grab 'Step 9 - the shipped setup' `
      'board 900 / 0 / 450, boardOrient 0 VERTICAL, cardinal AUTO, penLeanDeg 20. 669 targets, 3 pen swaps, 1 min 44 s.'))

    Write-Host ([RenderCell]::Run($imgs, $scenes))
    Write-Host "wrote $($scenes.Count) renders to $imgs"
    $d.Dispose()
}
finally { $core.Dispose() }
