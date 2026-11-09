# Reed field (갈대밭 / 억새밭) — credits

## Made for this project
- Reed clumps (RF_Susuki_A/B/C, RF_Susuki_Broken, RF_Susuki_Far, RF_Reed_A/B, RF_LowGrass, RF_Litter) and the broken boardwalk (RF_Boardwalk_A/B/C):
  generated in Blender 5.2 by `BlenderSource~/build_reeds.py` (run in background). Work .blend and previews stay in
  `ArtSource/ReedField/` (gitignored).
- Shaders (`Shaders/`), field placement, wind, trample hook and spirits (`Scripts/`), scene builder (`Editor/RFBuilder.cs`).

## Reused from this project (unchanged, referenced only)
- Dead trees, 장승, 솟대 and crows: `Assets/Game/AbandonedVillage/Models/AV_*.fbx` (made for the abandoned village test).
- Trample map: `Assets/Art/LookStudy` (LookStudyTrail + LookStudyTrampleFeed).

## Poly Haven (https://polyhaven.com, CC0)
- Textures in `Textures/`: brown_mud_leaves_01, dry_ground_01, brown_mud_02, river_small_rocks (ground), weathered_planks (boardwalk). Diffuse + normal (GL), imported at 1k.

## ChiliMilk/URP_Water (MIT) — https://github.com/ChiliMilk/URP_Water
- `ThirdParty/URP_Water/`: WaterSurface_single (surface normal map), WaterFoam, RippleNormal, Circle textures, Water.shadergraph and WaterFX.shader (original, kept for reference), LICENSE.txt.
- `WaterFX_Unlit.shader` is a modified copy of WaterFX.shader (light mode changed so a plain camera can render the ripple buffer).
- The ripple-buffer technique (particles writing normals into _WaterFXTexture) follows this package and Unity BoatAttack.

## Unity Technologies BoatAttack (Unity Companion License) — https://github.com/Unity-Technologies/BoatAttack
- `ThirdParty/BoatAttack/WaterSplashes.tif`: 4x4 splash/spray flipbook used by the footstep splash particles. LICENSE.md included.

## Made for this project (water)
- `Shaders/ReedShallowWater.shader`: shallow wetland water (depth-based clarity, shoreline foam, sky reflection, glints) on the URP_Water textures and ripple buffer.
- `Scripts/RFWater.cs`: ripple camera, footstep ripples, spray and droplets.
