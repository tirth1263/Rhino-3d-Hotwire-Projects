# WaveFin Hot-Wire Implementation — Simple Explanation

## Objective

This implementation converts a curved **WaveFin design** into a sequence of straight hot-wire positions.

A hot wire is straight, so it cannot directly follow a curved surface. The C# scripts calculate **41 straight wire positions**. Moving through these positions produces a cutting sweep that approximates the desired WaveFin surface.

```text
WaveFin design
      ↓
Grasshopper inputs
      ↓
C# toolpath calculation
      ↓
41 wire lines and tool planes
      ↓
Rhino cutting geometry and simulation
```

## Rhino — Left Side of the Screenshot

Rhino displays the actual 3D geometry from the top, perspective, front and right views.

Important layers include:

- `00_DESIGN_SOLID`: the final shape we want to manufacture.
- `01_CUTTING_SKIN`: the surface that guides the hot-wire calculation.
- `02_DESIGN_CENTERLINE`: the main path through the middle of the design.
- `A01` to `A04`: results from the four calculation methods.
- `SIMULATION_TRAIL`: wire positions already visited.
- `SIMULATION_ACTIVE_WIRE`: the current wire position.
- `SIMULATION_ACTIVE_MARKER`: the center of the current wire.

The dense colored lines around the design are not extra parts. Each line shows the hot wire at one calculated position.

## Grasshopper — Right Side of the Screenshot

Grasshopper sends geometry and numerical settings into four C# Script components.

The small boxes on the left contain inputs such as the surface, centerline, section count, width, twist and angle count. The large gray boxes execute the C# code. The boxes on the right receive results such as wire lines, tool planes, rails, ruled surfaces and reports.

The connecting curves only transfer data between components.

## The Four C# Methods

### 1. Surface UV

The script moves through the internal UV coordinates of the cutting surface. At each section, it finds a point on each side and connects them with a straight wire line.

### 2. Automatic Boundary Pair

The script examines the outside edges of the surface, selects a suitable pair of opposite boundaries, divides them into matching points and connects those points.

### 3. Centerline Width and Twist

The script divides the centerline into 41 stations. It creates a local plane at each station, changes the wire width from approximately 170 mm to 82 mm and gradually applies approximately 58 degrees of twist.

### 4. Multi-Angle Envelope

The script tests 18 possible wire orientations across 180 degrees at each station. It evaluates the alternatives and selects a suitable orientation for the WaveFin envelope.

## Important Outputs

- **Wire lines:** the hot wire at each moment.
- **Tool planes:** the position and orientation of the tool.
- **Rails:** the paths followed by the two ends of the wire.
- **Ruled surface:** the cutting surface swept by consecutive wire positions.
- **Report:** calculation information and warnings.

## Simulation

The simulation shows the selected multi-angle path progressing from station 1 to station 41.

- **Orange:** previously visited wire positions.
- **Red:** current active wire.
- **Yellow:** center of the active wire.

The start, middle and end screenshots correspond to stations 1, 21 and 41.


## What Has Been Achieved

The implementation generated:

- One WaveFin design and cutting skin.
- Four alternative hot-wire calculation methods.
- 41 wire positions.
- 41 tool-orientation planes.
- Endpoint rails and a ruled cutting surface.
- Start, middle and end geometric simulation views.

This is an accurate **geometric hot-wire sweep**, but it is not yet a production robot program. A real robot implementation still requires the exact robot and cell models, tool calibration, reachability, joint-limit and collision checks, safe approach movements and controller-code generation.

