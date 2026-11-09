# Ink filters — credits and licences

Screen filters (F10 in Play: Off → 수묵 → 스미에 → 수채). Shader code in `InkFilter.shader` is written for this project
(URP 17 RenderGraph full-screen pass), following the look and techniques of the projects below.

| Asset | Source | Licence | Used for |
|---|---|---|---|
| Stroke_0.psd, Stroke_2.psd, CIP_OutLine_Noise.psd | sacshadow / 3D_ChineseInkPaintingStyleShader (GitHub) | MIT (ThirdParty/sacshadow_ChineseInk/LICENSE.txt) | brush-stroke and ink-bleed textures of the "수묵" pass |
| Voronoi.hlsl, ColourRamp.hlsl | bentoBAUX / Sumi-e-in-3D (GitHub) | MIT (ThirdParty/bentoBAUX_SumiE/LICENSE.txt) | smooth Voronoi brush noise of the "스미에" pass |
| noise.png | Cyanilux / URP_WatercolourShaders (GitHub) | MIT (ThirdParty/Cyanilux_Watercolour/LICENSE.md) | distortion noise of the "수채" pass |
| Paper005 (1K colour) | ambientCG | CC0 | rice-paper texture |
