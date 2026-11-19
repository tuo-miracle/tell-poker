# Credits

## Microsoft Rocketbox Avatar Library (MIT License)

The face geometry (eyelids, mouth cavity, teeth, tongue, eyeballs), the 52 ARKit blendshapes, the eye/mouth-interior
texture and the eyelid texture detail in `erik-wrap.glb` are derived from the Microsoft Rocketbox avatar **Male_Adult_16**.

- Source: https://github.com/microsoft/Microsoft-Rocketbox
- File used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_16/Export/Male_Adult_16_facial.fbx
- Texture used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_16/Textures/m019_head_color.tga
- License: https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md

### Why Male_Adult_16
All 21 Rocketbox male adults were ranked against Erik's front landmarks (`scripts/rank_templates.py`; `../template-rank.txt`). M16 is second by shape (0.0618; first M19 0.0606 went to Reginald), a white adult male of about Erik's age with the same eye-corner span / face width (0.604 vs 0.604). Most of Erik's lower face is beard from the Tripo body, so the lids and eyes decide the likeness.

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
Build: `../../wrap-pipeline/characters/erik.json` (see `../../wrap-pipeline/README.md`).

## Erik

Body, hair, clothes and skin colour come from the project's Tripo AI generated model (`../erik-source.glb`).

## Tools (not shipped in the GLB)

Landmarks: MediaPipe FaceLandmarker (`face_landmarker.task`, Apache License 2.0).
