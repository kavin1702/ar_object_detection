Title: Note AR: fix LOCK, auto-detect note by cover colour, edge dimension callouts

## What changed and why

**LOCK & INSPECT never worked on device.** `ARPlaneManager` had been disabled in `AR_Detection.unity` since 15adede, so no table surface was ever detected and the outline could not be anchored. Plane detection is back on (horizontal), the Setup Tool keeps it on, and `NoteMarkerTracker` re-enables it at runtime as a safety net. LOCK is only clickable once a surface is found (still clickable in the Editor for testing).

**The notebook itself is the marker.** The plain teal cover scores 0/100 in ARCore's `arcoreimg`, so ARCore image tracking cannot work for it. New `NoteColorDetector` + `NoteColorBlobDetector` find the notebook in the CPU camera image by its cover colour (taken from the note image in the Setup Tool). Camera pixels are mapped to the screen with the same display matrix that ARCore's background shader uses. The detector projects the four corners onto the detected table at cover height, checks the size against the profile, and snaps the green box onto the notebook; the user confirms with LOCK.

**The locked outline stays on the notebook.** Device video showed the locked outline sliding off the notebook as the phone moved (ARCore drift on glossy, plain floor tiles). After LOCK the detector keeps running and gently pulls the outline back onto the notebook: it uses a dead zone, ignores jumps over 12 cm or 30°, and never flips 180°. This can be turned off with "Keep Outline On Note After LOCK".

**Dimensions like the reference image.** Width (W) / Length (L) / Depth (T) edge callouts with arrows, extension lines and dark label plates, shown live while scanning and after LOCK. Runtime `TextMesh` labels now get a font and material, so they render on device.

**Cleanup and build fixes**
- Removed 11 unused legacy QC/metrology scripts and the template sample scene. Removed 9 ghost alignment boxes and 8 legacy components from the scene.
- The reference image library is kept only when ARCore can use the cover; otherwise ARCore's image-library build step fails ("not enough keypoints" / "No valid input image found"). `NoteMarkerBuildGuard` stops broken builds made through File > Build.
- The Roslyn DLLs used by MCP for Unity are now Editor-only, so they are no longer packed into the APK.
- The Setup Tool saves only its own assets (fixes the "immutable packages were unexpectedly altered" warning), shows the ARCore quality score, the cover colour and debug options. BuildHelper reports the real APK size (~39 MB, not 956 MB).
- Removed the committed `Assets/XR/Temp` folder (BuildHelper already deletes it before building).

## For reviewers
- `origin/main` is still at the first commit, so this PR also contains the earlier commits 617bbe8 → 22298a3.
- Verified in Unity 6000.3.11f1: no console errors; `TestARPipeline` passes 28/28 (includes a synthetic slanted-notebook detection test). Detection was also checked on a photo of the notebook and on frames from device videos.
- **Not yet verified on device:** the cover-colour detection, the camera-to-screen mapping and the post-LOCK correction. If the in-app debug thumbnail looks upside down, tick "Flip Camera Image".
- The profile still has 240 × 180 × 6 mm; set the real notebook size in Tools > Note AR Setup Tool.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
