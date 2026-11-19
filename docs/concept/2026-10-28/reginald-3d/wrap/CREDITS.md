# Credits

## Microsoft Rocketbox Avatar Library (MIT License)

The face geometry (eyelids, mouth cavity, teeth, tongue, eyeballs), the 52 ARKit blendshapes, the eye/mouth-interior
texture and the eyelid texture detail in `reginald-wrap.glb` are derived from the Microsoft Rocketbox avatar **Male_Adult_19**.

- Source: https://github.com/microsoft/Microsoft-Rocketbox
- File used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_19/Export/Male_Adult_19_facial.fbx
- Texture used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_19/Textures/m250_head_color.tga
- License: https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md

### Why Male_Adult_19
All 21 Rocketbox male adults were ranked against Reginald's front landmarks (`scripts/rank_templates.py`; `../template-rank.txt`). M19 is the closest face shape (0.057; next M12 0.0601, used for Jace) and a middle-aged bearded man with brow lines, close to the concept's age and look. Male_Adult_14 was also built for comparison: cleaner open eyes but a visible mask edge on the forehead; M19 kept.

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
repainted, eyeballs scaled and placed, eyeball motion removed from the eyeLook* keys (the eye bones turn the eyes).
Build: `../../wrap-pipeline/characters/reginald.json` (see `../../wrap-pipeline/README.md`).

## Reginald

Body, hair, clothes and skin colour come from the project's Tripo AI generated model (`../reginald-source.glb`).

## Tools (not shipped in the GLB)

Landmarks: MediaPipe FaceLandmarker (`face_landmarker.task`, Apache License 2.0).
