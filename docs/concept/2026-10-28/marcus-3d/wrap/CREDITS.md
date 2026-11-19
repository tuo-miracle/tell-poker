# Credits

## Microsoft Rocketbox Avatar Library (MIT License)

The face geometry (eyelids, mouth cavity, teeth, tongue, eyeballs), the 52 ARKit blendshapes, the eye/mouth-interior
texture and the eyelid texture detail in `marcus-wrap.glb` are derived from the Microsoft Rocketbox avatar **Male_Adult_09**.

- Source: https://github.com/microsoft/Microsoft-Rocketbox
- File used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_09/Export/Male_Adult_09_facial.fbx
- Texture used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_09/Textures/m017_head_color.tga
- License: https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md

### Why Male_Adult_09
All 21 Rocketbox male adults (Male_Adult_01–21) were rendered from the front and their MediaPipe landmarks compared with Marcus's (`scripts/rank_templates.py`; ranking in `../template-rank.txt`). Shape alone ranks M07 first (0.0317) and M09 14th (0.0507), but M09 is the only East Asian adult male head and its eye-corner span / face width (0.602) is the closest to Marcus's (0.597, M07 0.633). The lid shape matters most for likeness after the wrap (the landmark warp takes out the overall face shape), so M09 was chosen.

```
MIT License

Copyright (c) 2020 Microsoft

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

Modifications: head cropped to the face mask, non-ARKit shape keys removed, non-rigidly fitted to the character's face,
skin re-textured with the character's own colour map (baked), eyelid region re-coloured to the character's skin, irises
repainted, eyeballs scaled and placed, eyeball motion removed from the eyeLook* keys (the eye bones turn the eyes), glasses rebuilt as procedural metal frames from the Tripo frame outline.
Build: `../../wrap-pipeline/characters/marcus.json` (see `../../wrap-pipeline/README.md`).

## Marcus

Body, hair, clothes and skin colour come from the project's Tripo AI generated model (`../marcus-source.glb`).

## Tools (not shipped in the GLB)

Landmarks: MediaPipe FaceLandmarker (`face_landmarker.task`, Apache License 2.0).
