# CAD QR Overlay (prototype)

Pick a CAD model, print its QR marker card, stick the card on the real object, scan it,
then press **LOCK & INSPECT**. The CAD wireframe and its dimensions (read from the model,
not measured) stay fixed on the object while you move the phone.

Quick start in Unity:

1. `Tools > CAD QR Overlay > Build Demo Scene` (creates `CadQrOverlay.unity`, the model
   library, materials, and puts the scene first in Build Settings).
2. `Tools > CAD QR Overlay > Export QR Cards (PNG)` and print a card at the size in its file name.
3. Build to an ARCore Android phone (File > Build Profiles > Android > Build And Run).

Scripts (`Scripts/`):

| Script | Job |
| --- | --- |
| `QrEncoder` | Pure C# QR code encoder (byte mode, level L, versions 1-5) |
| `MarkerCardPainter` | Paints the card: black frame, random pattern from the model id, up arrow, QR |
| `QrTextureFactory` | Turns the card into a readable `Texture2D` |
| `CadModelLibrary` | ScriptableObject: the list of CAD models, units, card size and placement |
| `CadModelAnalyzer` | Reads size in mm and sharp "feature edges" from the meshes, builds tube meshes |
| `CadOverlay` | The virtual model: wireframe, bounding box, ghost surfaces, W/H/D labels |
| `CadQrOverlayController` | Runtime image library, follows the card, LOCK with an ARAnchor |
| `CadQrHUD` | Buttons and text, built in code |

The full explanation lives in the project guide document.
