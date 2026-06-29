# SP2 Fuselage Splitter

SP2 Fuselage Splitter is a BepInEx designer tool for **SimplePlanes 2**. It divides a selected fuselage longitudinally into a configurable number of connected parts while keeping the generated pieces aligned with the original part.

The tool integrates directly into the game's **Part Properties** panel. When a supported fuselage is selected, a native-styled **Fuselage Splitter** button appears immediately below **Edit Fuselage Shape**.

## Features

- Split a fuselage into 2–32 longitudinal pieces.
- Preserve the original part's position and orientation across the generated pieces.
- Optionally replace the original part or retain it alongside the generated pieces.
- Reconnect the generated pieces into a continuous chain.
- Remap the original part's external connections when replacing it.
- Preserve decal targeting when replacing a fuselage referenced by custom part IDs.
- Create a designer undo step before modifying the craft.
- Support modern `JFuselage.State` and legacy `Fuselage.State` parts.
- Use a native-styled interface inside the existing Part Properties widget.
- Dump the selected part's runtime XML for troubleshooting.

## Important disclaimer

> **Position and orientation are preserved, but exact shape fidelity is not guaranteed.**
>
> SimplePlanes 2 supports many complex corner, cutting, slicing, smoothing, and surface configurations. Although the generated parts are guaranteed to retain the intended position and orientation of the split fuselage, some shape attributes cannot always be interpolated exactly.
>
> This tool works best with simple fuselage shapes. Do not push your luck with heavily sliced fuselages or parts using complex corner and surface types. Inspect the result after splitting and use Undo immediately if the geometry does not match the original.

Back up important crafts before using experimental designer tools.

## How it works

The splitter reads the selected part's runtime XML, creates a copy for each requested segment, and divides the original fuselage offset evenly along its local longitudinal axis.

For modern fuselages, numeric attributes from `SectionA` and `SectionB` are interpolated at each segment boundary. For legacy fuselages, the front and rear scales and related numeric attributes are interpolated instead. Each generated part receives a new ID and the same rotation as the source, while its position is shifted to the correct segment center.

When **Delete original** is enabled, connections using the front or rear attach points are moved to the first or last generated piece. Other connections are assigned to the nearest segment using the connected part's position. Decal custom-part targets referencing the original fuselage are expanded to the generated piece IDs so the craft remains loadable and the projection still resolves. The generated pieces are then connected end-to-end.

Some discrete or sparsely serialized properties cannot be smoothly interpolated. These include certain cutting values, corner modes, boolean surface options, and modifier-specific data. Such properties may be copied or switched between the source end states.

## Usage

1. Start SimplePlanes 2 and open a craft in the designer.
2. Select a supported fuselage part.
3. Open **Part Properties** with the wrench button.
4. Click **Fuselage Splitter**, directly below **Edit Fuselage Shape**.
5. Choose the number of pieces.
6. Select whether the original part should be deleted.
7. Click **Split Selected**.
8. Inspect the geometry and attached parts before continuing.

## Supported parts

- Modern `JFuselage.State` body and hollow fuselages.
- Legacy `Fuselage.State` fuselages.

Currently unsupported or potentially unreliable:

- Cone-style `JFuselage` parts. These are rejected rather than approximated.
- Heavily sliced fuselages whose cutting values change between their end sections.
- Parts with complex or changing corner and surface types.
- Symmetrical parts where both sides are expected to be split automatically.
- Fuselages carrying modifiers that should not be duplicated across every generated piece.
- Unusual side attachments or multi-point connections that cannot be mapped cleanly to one segment.

## Installation

1. Install BepInEx 5.x for the 64-bit Windows version of SimplePlanes 2.
2. Run the game once so BepInEx creates its folders.
3. Copy `SP2FuselageSplitter.dll` into:

```text
SimplePlanes 2\BepInEx\plugins\
```

4. Restart SimplePlanes 2.
5. Check `BepInEx\LogOutput.log` if the button does not appear.

## Usage examples

![](assets/usage-example-1.gif)

![](assets/usage-example-2.gif)

## Building from source

The project targets .NET Framework 4.7.2 and references assemblies from the installed game and BepInEx.

By default, the project expects SimplePlanes 2 at:

```text
C:\Program Files (x86)\Steam\steamapps\common\SimplePlanes 2
```

Build a release DLL with:

```powershell
dotnet build .\SP2FuselageSplitter.csproj -c Release
```

The compiled plugin is written to:

```text
bin\Release\SP2FuselageSplitter.dll
```

To build against a different installation directory:

```powershell
dotnet build .\SP2FuselageSplitter.csproj -c Release -p:GameDir="D:\Path\To\SimplePlanes 2"
```

## Uninstalling

Delete `SP2FuselageSplitter.dll` from `BepInEx\plugins`, then restart the game.

## Project status

This is an experimental, unofficial community tool. It is not affiliated with or endorsed by Jundroo.
