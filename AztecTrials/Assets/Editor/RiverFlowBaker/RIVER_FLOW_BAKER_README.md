# River Flow Baker - User Guide

## Overview

The River Flow Baker is a powerful Unity Editor tool that automatically generates flow maps, velocity maps, foam masks, and obstacle distance maps for stylized river rendering. No fluid simulation or hand-painting required.

## Quick Start

### 1. Setup
- Add a `RiverFlowBakerComponent` to your river GameObject
- Assign the river mesh in the inspector
- Open `Tools > River Flow Baker`

### 2. Configure
- Set river source direction (forward, spline, or manual)
- Choose texture resolution (512 recommended, 1024+ for high quality)
- Configure obstacle detection layers
- Adjust flow and foam parameters

### 3. Bake
- Click **Bake All** in the River Flow Baker window
- Wait for generation to complete
- Visualize results using debug modes

### 4. Export
- Click **Export Textures** to save PNG files
- Textures are saved to the specified export path
- Automatically assign to river materials

## Components

### RiverFlowBakerComponent
Attached to the river GameObject. Stores configuration and generated render textures.

**Key Properties:**
- **River Mesh**: The mesh to bake flow maps for
- **Flow Source Mode**: How flow direction is determined
- **Texture Resolution**: 256, 512, 1024, 2048, or 4096
- **Obstacle Layers**: Which layers contain obstacles (rocks, terrain, etc.)
- **Export Path**: Where to save exported textures

### RiverFlowBakerWindow
Custom editor window accessed via `Tools > River Flow Baker`.

**Sections:**
- **River Settings**: Flow direction, speed, smoothing
- **Obstacle Settings**: Obstacle detection and deflection
- **Bake Settings**: Resolution and quality options
- **Debug Visualization**: Real-time preview modes
- **Output Settings**: Export configuration

## Flow Generation Pipeline

### Step 1: Rasterize River Mesh
The river mesh is rendered into UV space at the chosen resolution.

### Step 2: Generate Initial Flow Field
Flow direction is computed for each texel based on:
- Transform forward vector
- Manual source direction
- Spline tangents (if available)

### Step 3: Obstacle Detection
The system queries physics colliders and terrain to find nearby obstacles. A distance field is generated.

### Step 4: Flow Deflection
Water flow is deflected around obstacles, creating realistic split and rejoin patterns.

### Step 5: Velocity Estimation
Local water speed is estimated based on:
- Channel width (narrower = faster)
- Obstacle constriction
- Curvature
- Distance to banks

### Step 6: Curvature Analysis
Flow path curvature is analyzed to generate secondary patterns (eddies, foam).

### Step 7: Foam Generation
Foam seeds are generated from:
- High velocity areas
- Obstacle proximity
- Curvature peaks
- Bank proximity

### Step 8: Relaxation Passes
Multiple smoothing passes remove noise and create natural flow patterns.

## Generated Textures

### FlowMap (RG channels)
- **R**: Flow X direction (0-1 range)
- **G**: Flow Y direction (0-1 range)
- Decode: `flow = tex * 2 - 1`

### VelocityMap (R channel)
- **R**: Flow speed (0-1 range, 0=slow, 1=fast)
- Used to speed up narrow channels and rapids

### FoamMask (R channel)
- **R**: Foam generation mask (0-1 range)
- 0 = no foam, 1 = heavy foam
- Used for foam placement in shader

### DistanceMap (R channel)
- **R**: Distance to nearest obstacle (0-1 range)
- 0 = obstacle, 1 = open water

## Debug Visualization

Real-time preview modes in the Scene view:

### Flow Arrows
Visualize flow direction and intensity with arrows. Density is adjustable.

### Velocity Heatmap
Color-coded velocity visualization:
- **Blue**: Slow water
- **Green**: Medium speed
- **Red**: Fast water (rapids)

### Foam Preview
Shows where foam will appear on the river surface.

### Obstacle Distance
Visualizes proximity to obstacles (red = close, blue = far).

### Curvature
Shows flow path bending and eddies.

## Configuration Guide

### Flow Strength (0-2)
Controls how much flow direction influences UV distortion in the shader.
- **Low**: Subtle flow
- **High**: Strong directional movement

### Flow Speed (0-4)
Global multiplier for animation speed.
- Affects how fast textures scroll with the flow

### Velocity Smoothing (0-1)
Smoothing factor for velocity field.
- **High**: Broader, more uniform velocity
- **Low**: Sharper transitions, more detail

### Relaxation Passes (0-10)
Number of smoothing passes applied to flow field.
- **Low**: Rougher, more varied flow
- **High**: Smooth, natural flow patterns

### Obstacle Influence Radius (0-10)
How far away obstacles affect flow.
- Larger radius creates distant deflection zones

### Obstacle Deflection Strength (0-2)
How much obstacles bend water flow.
- **Low**: Water mostly ignores obstacles
- **High**: Water sharply deflects around rocks

### Foam Threshold (0-1)
Threshold for foam generation.
- **Low**: More foam overall
- **High**: Foam only in fast areas

### Foam Intensity (0-4)
Overall foam multiplier.
- Controls foam density across the river

## Texture Resolution

| Resolution | Speed | Quality | Use Case |
|---|---|---|---|
| 256 | Very Fast | Poor | Preview |
| 512 | Fast | Good | Mobile, real-time |
| 1024 | Moderate | High | PC, console |
| 2048 | Slow | Very High | High-end |
| 4096 | Very Slow | Extreme | Quality renders |

## Workflow Tips

### For Straight Rivers
- Use **Transform Forward** mode
- Keep flow strength high
- Lower relaxation passes (faster bake)

### For Curved Rivers
- Use **Manual Source Points** or **Spline Driven**
- Increase curvature influence
- Increase relaxation passes for smooth curves

### For Complex Geometry
- Use high resolution (2048+)
- Enable all obstacle layers
- Increase obstacle influence radius
- Use more relaxation passes

### For Mobile Targets
- Use 256-512 resolution
- Lower quality settings
- Reduce relaxation passes
- Export as separate Texture2D assets

## Export and Assignment

### Manual Export
Click **Export Textures** to save PNG files:
- `River_FlowMap.png`
- `River_VelocityMap.png`
- `River_FoamMask.png`

### Automatic Assignment
Generated textures are automatically assigned to materials using:
- `_FlowMap`
- `_VelocityMap`
- `_FoamMask`
- `_RiverBoundsMin`
- `_RiverBoundsSize`

### Custom Path
Set **Export Path** to any folder in your Assets directory.

## Performance

All baking occurs in the Editor. Typical times:

- **256x256**: < 1 second
- **512x512**: 1-2 seconds
- **1024x1024**: 5-10 seconds
- **2048x2048**: 20-40 seconds
- **4096x4096**: 2-5 minutes

Generated textures use ~2MB per 512x512 map.

## Troubleshooting

### "River mesh is not assigned!"
- Select the river GameObject
- Drag the river mesh into the inspector

### Textures not updating
- Click **Refresh** in the window
- Or re-select the component

### Flow looks wrong
- Check flow source direction
- Verify mesh has valid UVs
- Try different relaxation pass counts

### Obstacles not detected
- Check obstacle layer mask settings
- Verify colliders exist on obstacles
- Increase obstacle influence radius

### Foam not generating
- Lower foam threshold value
- Increase foam intensity
- Check velocity map (should have values > 0)

## Advanced Usage

### Scripting
```csharp
using RiverFlowBaker;

var component = gameObject.AddComponent<RiverFlowBakerComponent>();
component.BakeAll();
FlowMapExporter.ExportAllMaps(component);
```

### Custom Shaders
Use generated maps in custom shaders:
```glsl
sampler2D _FlowMap;
sampler2D _VelocityMap;

float2 flow = tex2D(_FlowMap, uv).rg * 2 - 1;
float velocity = tex2D(_VelocityMap, uv).r;
```

## References

- **Shader**: RiverFlowShader (URP compatible)
- **Material GUI**: RiverShaderGUI
- **Scene Gizmos**: FlowDebugRenderer
- **Export**: FlowMapExporter

## License

River Flow Baker is part of the Aztec Trials project.

---

**Version**: 1.0  
**Last Updated**: June 2026
