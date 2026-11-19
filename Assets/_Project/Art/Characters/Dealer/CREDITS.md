# Credits

## Microsoft Rocketbox Avatar Library (MIT License)

The face geometry (eyelids, mouth cavity, teeth, tongue, eyeballs), the 52 ARKit blendshapes, the eye/mouth-interior
texture and the eyelid texture (lashes, crease) in `dealer-wrap.glb` are derived from the Microsoft Rocketbox avatar
**Female_Adult_07**.

- Source: https://github.com/microsoft/Microsoft-Rocketbox
- File used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Female_Adult_07/Export/Female_Adult_07_facial.fbx
- Texture used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Female_Adult_07/Textures/f007_head_color.tga
- License: https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md

### Why Female_Adult_07
All 14 unveiled female adults (01–05, 07–09, 11–15, 17) were rendered from the front and their MediaPipe landmarks
compared with the dealer's (`scripts/rank_templates.py`: 2-D similarity-aligned RMS over all 468 points plus the face
oval, in inter-ocular units). Top three: F04 0.0294, **F07 0.0296**, F17 0.0302. F07 was chosen because its face oval is
the closest of all (0.0080 vs 0.0104 for F04) and its proportions match the dealer's round face almost exactly
(width/height 0.891 vs 0.890, eye-corner span/width 0.644 vs 0.645); it also reads as mid-30s. Only the template's
geometry, rig and eye/lid/mouth texture are used; the skin colour is the dealer's own.

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

Modifications: head cropped to the face mask, non-ARKit shape keys removed, non-rigidly fitted to the dealer's face,
skin re-textured with the dealer's own colour map (baked), eyelid region re-coloured to the dealer's skin, irises
repainted brown with a limbal ring and a small catchlight, sclera repainted a soft off-white, inner-lid rim recoloured from
the dealer's skin, mouth-wall texture darkened, eyeballs scaled, moved and rotated so the pupils aim level at 1 m,
upper lids lifted 10 deg and lower lids raised 2 deg in the basis with lid shape keys compensated, eyeball motion removed from the eyeLook* keys
(the eye bones turn the eyes).

## Dealer

Body, hair, clothes and skin colour come from the project's Tripo AI generated dealer model (`../dealer-12k.glb`).

## Tools (not shipped in the GLB)

Landmarks: MediaPipe FaceLandmarker (`face_landmarker.task`, Apache License 2.0).
