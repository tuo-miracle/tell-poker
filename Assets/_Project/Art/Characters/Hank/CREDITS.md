# Credits

## Microsoft Rocketbox Avatar Library (MIT License)

The face geometry (eyelids, mouth cavity, teeth, tongue, eyeballs), the 52 ARKit blendshapes, and the eye/mouth-interior
texture in `hank-wrap.glb` are derived from the Microsoft Rocketbox avatar **Male_Adult_03**.

- Source: https://github.com/microsoft/Microsoft-Rocketbox
- File used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_03/Export/Male_Adult_03_facial.fbx
- Texture used: https://raw.githubusercontent.com/microsoft/Microsoft-Rocketbox/master/Assets/Avatars/Adults/Male_Adult_03/Textures/m004_head_color.tga
- License: https://github.com/microsoft/Microsoft-Rocketbox/blob/master/LICENSE.md

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

Modifications: head cropped to the face mask, non-ARKit shape keys removed, non-rigidly fitted to Hank's face,
skin re-textured with Hank's own colour map (baked), mouth-wall and eyeball texture regions darkened.

## Hank

Body, hat, clothes and skin colour come from the project's Tripo AI generated Hank model (`../hank-14k-facehd.glb`).
