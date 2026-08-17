[VRC Light Volumes](../README.md) | [How to Use](./HowToUse.md) | **Realtime Dome Mesh Light** | [Best Practices](./BestPractices.md) | [Compatible Shaders](./CompatibleShaders.md)

# Realtime Dome Mesh Light

The Realtime Dome Mesh Light converts one combined screen mesh and its changing video atlas into one shared 3D Light Volume. It is intended for domes, curved video walls and other installations where many screen sections must cast changing colored light without using dozens of realtime Unity lights.

The screen video continues to play at its normal frame rate. The lighting field samples that video at the separately configurable **Light Refresh Rate**.

## Before You Start

You need:

- A `Light Volume Manager` in the open scene.
- One `MeshRenderer` or `SkinnedMeshRenderer` containing the screen surfaces.
- UV0 on that mesh aligned to the live video atlas.
- **Read/Write** enabled in the screen model's import settings.
- The live `RenderTexture` or `CustomRenderTexture` that the screen material displays.
- VRC Light Volumes-compatible shaders on world materials and avatars that should receive the light. Unity's Standard shader does not receive Light Volumes.

> [!IMPORTANT]
> The **Live Video Atlas** is the texture being updated by the video player at runtime. For VideoTXL this is normally its output `CustomRenderTexture`, not the original video URL, a still image or the screen material itself.

## Create The Mesh Light

1. Exit Play Mode and stop any running VRChat world test.
2. Select the combined dome screen object in the Hierarchy.
3. Open `Tools > Light Volumes > Create Realtime Dome Mesh Light`.
4. Assign **Dome Screens** to the combined screen renderer.
5. Assign **Live Video Atlas** to the live video output texture. The tool attempts to find `_MainTex` or `_EmissionMap` from the renderer automatically, but confirm that it selected the changing video texture.
6. Assign the scene's **Light Volume Manager**.
7. Choose a **Dome Center** mode:
   - **Transform Override** is the most reliable choice. Create an empty GameObject at the true center of the dome and assign it as **Center Transform**.
   - **Fit Dome Sphere** works well when the screen mesh forms a mostly spherical dome.
   - **Renderer Bounds** is useful for rectangular or irregular screen arrangements.
8. Leave **Center Offset** at `0, 0, 0` initially. Check that **Resolved Center** and **Estimated Radius** look correct.
9. Start with the recommended VR settings below.
10. Choose an **Asset Folder**, or leave it at `Assets/LightVolumesDome`.
11. Click **Create Realtime Mesh Light Volume**.
12. Save the scene.

The tool creates emitter data, update materials and three 3D textures, connects them to the manager, and publishes the result through one standard additive Light Volume. It does not require a Unity or Bakery lighting bake.

## Recommended VR Starting Settings

| Setting | Suggested value | What it controls |
|---|---:|---|
| Volume Size Scale | `1` | Fits the generated field to the dome diameter. Increase only if the fitted bounds are too small. |
| Nearby Avatar Coverage | `2 m` | Adds coverage around outer screen panels for nearby players. |
| Grid Resolution | `24` or `32` | `24` is lighter; `32` gives smoother spatial lighting. |
| Panel Reach | `2` | Maximum reach from a panel, measured as a multiple of the dome radius during creation. |
| Light Intensity | `2-5` | Overall mesh-light brightness. |
| Screen Color | `1` | Uses the full video color. Lower it for more neutral lighting. |
| Edge Fade | `0.5 m` | Softens the outside boundary of the volume. |
| Back Surface Fade | `0.25` | Prevents screen light leaking behind the panels. |
| Floor Light Boost | `2` | Makes the screen contribution more visible on the floor. |
| Panel Color Spread | `0.02` | Avoids sampling only labels, seams or isolated dark pixels in a panel UV. |
| VR Performance Mode | On | Updates one color field instead of three directional fields. Recommended for VR. |
| Light Refresh Rate | `10-15` | How often the 3D light follows the video. Lower values cost less GPU time. |

## Adjust It Later

Open `Tools > Light Volumes > Realtime Mesh Light Settings`.

This window changes the complete mesh-light setup at once. It includes intensity, color, panel reach, edge fade, back-surface blocking, floor boost, panel color sampling, VR Performance Mode and refresh rate.

- **Apply Stable VR Preset** enables VR Performance Mode and sets the refresh rate to `10` updates per second without replacing the rest of your lighting settings.
- **Refresh Now** immediately updates the generated textures.
- **Rebuild / Advanced** opens the creation window when the mesh, UVs, center, bounds or resolution need to be regenerated.
- **Enabled** controls the published standard Light Volume.

The optional **Avatar Fill Light** is only a fallback for avatar shaders that do not receive the standard Light Volume data. Do not create it when your avatar shader already supports the published Light Volume and you prefer to avoid an extra realtime Unity light.

## After Changing The Mesh Or UVs

Mesh positions, normals and UV samples are prepared when the mesh light is created. They do not automatically change when the model is edited.

1. Stop Play Mode and VRChat testing.
2. Open `Tools > Light Volumes > Realtime Mesh Light Settings`.
3. Click **Rebuild / Advanced**.
4. Select the updated renderer and the same live video atlas and manager.
5. Confirm the center and settings, then click **Create Realtime Mesh Light Volume**.
6. Save the scene and rebuild the world.

Creating again switches the manager to newly generated assets. Previous generated assets remain in the output folder so existing work is not silently deleted. They may be removed manually after the new setup has been tested and the scene has been saved.

## Screen Sections And The 128 Limit

The completion dialog reports how many disconnected mesh sections became emitters. This is not the triangle count. Connected triangles normally become one screen section.

The realtime shader supports up to **128 screen sections**. If the mesh produces more than 128, the tool keeps the 128 largest sections and logs a warning. Excess UV seams or disconnected UV islands can split a physical screen into multiple sections, so a sudden result of exactly `128` after a UV edit may mean the source mesh exceeded the limit.

For predictable results:

- Keep each physical screen panel connected where possible.
- Avoid unnecessary duplicate geometry and tiny disconnected triangles.
- Keep UV0 inside the intended live atlas region.
- Check the Unity Console for the `more than 128 disconnected emitters` warning.

## Existing Point Lights

**Disable other registered light GameObjects** is optional. It only deactivates the Point Light Volume GameObjects registered with the selected manager; it does not delete them or change their settings. Undo can restore them immediately.

Leave this option off during the first setup. After the mesh light is working, enable it or manually disable the old lights to compare visual quality and performance.

## Troubleshooting

### The Create button is disabled

Confirm that all three object fields are assigned, the renderer has a usable mesh, **Read/Write** is enabled, and UV0 exists for every vertex. **Transform Override** also requires a Center Transform.

### The screen changes but the light does not

The **Live Video Atlas** is probably a static texture or the wrong render texture. Assign the actual texture that changes while VideoTXL is playing, then click **Refresh Now**.

### Some panels have no light or the wrong color

- Rebuild after every UV or mesh change.
- Look for the 128-section warning.
- Increase **Panel Color Spread** slightly if UV labels, seams or black pixels are being sampled.
- Confirm the affected panel's UV0 points at the same atlas region displayed by its material.

### Light disappears near an outer screen

Rebuild with more **Nearby Avatar Coverage** or a slightly larger **Volume Size Scale**. Use **Transform Override** if the automatically fitted center is pulling the volume away from one side.

### Light leaks behind the screens

Raise **Back Surface Fade** gradually. Also confirm that the screen mesh normals face toward the dome's receiving area and that the selected center is on the intended lit side.

### The edge of the volume is visible

Increase **Edge Fade**. Make sure the volume extends beyond the normal player area so the fade occurs where players are unlikely to stand.

### The lighting is too slow or too expensive

Enable **VR Performance Mode**, use resolution `24`, and set **Light Refresh Rate** to `10`. The screen remains full frame rate; only the 3D lighting update is reduced.

### Changes temporarily turn the result black

Click **Refresh Now**. If Unity was running a VRChat test while package files or generated assets changed, stop the test, focus Unity, wait for importing to finish, and refresh again.

### Older generated materials need updating

Use `Tools > Light Volumes > Repair Realtime Dome Mesh Light`, then save the scene.
