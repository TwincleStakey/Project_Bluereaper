# RoomRun third-party assets

- "Oriental_stone_lantern" by kebijini (Sketchfab), CC BY 4.0 -
  https://sketchfab.com/3d-models/oriental-stone-lantern-b582ab0acb67424e88a12320734fe789
  `Resources/StageLantern/` (copy of the snow stage's decimated version). StageMood.cs places eight of these just
  outside each field arena as the lit lanterns. Credit the author in the shipped game.

- "Chevalier Sword" by rubenve (Sketchfab), CC BY 4.0 -
  https://sketchfab.com/3d-models/chevalier-sword-b2662f2666a844e8a1bd0e7c4a7672d8
  Lives in `Assets/Game/LocoSplit/Art/Greatsword/`; carried by the temporary greatsword player
  (`LocoSplit/Generated/RR_GreatswordPlayer.prefab`, dev panel 양손검 캐릭터). Credit the author in the shipped game.
  Animations: Mixamo (Adobe), Y Bot greatsword pack.

- "Free Slash VFX" by MaykerStudio (Unity Asset Store, free, Standard Unity Asset Store EULA) -
  https://assetstore.unity.com/packages/vfx/shaders/free-slash-vfx-309498
  Imported to `Assets/Free Slash VFX/` without its Demo folder. GreatswordCombat plays "Slash Eletric VFX" on each
  greatsword swing (its "Slash World" ground-mark children are switched off: that shader graph renders magenta in Unity 6,
  and the "Flying" sparks get URP's particle shader with the pack's own texture).
- Greatsword clips (Great Sword Run/Backward Run/Strafe Run L+R, Combo Slash, Power/Downward/Low Slash, High Spin Attack):
  Mixamo (Adobe), downloaded on Y Bot, in `Assets/Game/LocoSplit/Art/MixamoRR/`.
- Cut grass bits use the blade texture of "Stylized grass" by firdaussahak (see Assets/Art/LookStudy/CREDITS.md).
