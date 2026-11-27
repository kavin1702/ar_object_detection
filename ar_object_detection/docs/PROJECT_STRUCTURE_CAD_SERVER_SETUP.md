# AR Object Detection: Project Structure, CAD Pipeline and Server Setup

Design addendum | 21 November 2026 | Unity Android application

## 1. Recommended architecture

Keep the existing Unity project at the repository root. Add a CAD preparation workspace and a model distribution server beside it. Prepare STEP geometry on a workstation or conversion worker, import the resulting FBX into Unity, and deliver a validated prefab in the APK or an Android AssetBundle.

```text
CAD preparation / build environment
STEP (.step/.stp)
  -> FreeCAD / Open CASCADE: tessellate CAD surfaces
  -> OBJ mesh + metadata: preserve units and part transforms
  -> Blender: optimize, orient, center, export FBX
  -> Unity Editor: import FBX, assign URP materials, make prefab
  -> APK (first demo) OR Android AssetBundle (server updates)

Model distribution
Admin -> upload / review -> versioned model storage
                             |
                  HTTPS server + model manifest
                             |
                  Unity download + local cache
                             |
               existing AR alignment / lock / outline
```

**Important format boundary:** Unity imports FBX in the Editor. A normal Android Unity player cannot directly import a downloaded FBX. Use platform-specific AssetBundles for this project. A runtime glTF/GLB importer is an alternative that needs an additional package and a different loading path.

### Verified baseline and proposed additions

| Area | Current project | Proposed addition |
|---|---|---|
| Unity | 6000.3.11f1; URP 17.3.0 | Keep the same Editor for content builds |
| AR | AR Foundation / ARCore / ARKit 6.3.3 | Keep detection and tracking on the phone |
| Model display | Optional prefab reference already exists | FBX prefab preparation and downloaded prefab loader |
| Dimensions | Note: W 180, L 240, T 6 mm | Versioned metadata per CAD model |
| Backend | No model server or converter found | Static serving first; asynchronous API later |

This is a proposed structure and setup guide. It does not install a server, implement the proposed modules, or convert an actual STEP file. No STEP model was supplied. Existing detection estimates alignment; importing CAD does not add general CAD-based recognition or calibrated dimensional inspection.

<!-- pagebreak -->

## 2. Unity project structure

Paths below are relative to the repository root. `[existing]` means verified in this project; `[new]` means proposed. Retain the current script locations so Unity asset references remain stable.

```text
ar_object_detection/
|-- Assets/
|   |-- Scenes/
|   |   `-- AR_Detection.unity                    [existing]
|   `-- _Project/
|       |-- Editor/                              [existing]
|       |   |-- BuildHelper.cs
|       |   |-- NoteMarkerToolWindow.cs
|       |   |-- NoteMarkerBuildGuard.cs
|       |   |-- TestARPipeline.cs
|       |   |-- CADPrefabValidator.cs             [new]
|       |   `-- ModelBundleBuilder.cs             [new]
|       |-- Scripts/
|       |   |-- AR/ARSessionController.cs         [existing]
|       |   |-- Detection/                        [existing]
|       |   |   |-- NoteMarkerProfile.cs
|       |   |   |-- NoteMarkerTracker.cs
|       |   |   |-- NoteColorDetector.cs
|       |   |   `-- NoteColorBlobDetector.cs
|       |   |-- Visualization/                    [existing]
|       |   |   |-- NoteOutlineRenderer.cs
|       |   |   `-- DimensionLabel.cs
|       |   |-- UI/                               [existing]
|       |   |   |-- NoteMarkerHUD.cs
|       |   |   `-- SafeAreaFitter.cs
|       |   |-- Networking/                       [new]
|       |   |   |-- ModelCatalogClient.cs
|       |   |   |-- ModelDownloadService.cs
|       |   |   `-- ModelCache.cs
|       |   `-- Models/                           [new]
|       |       |-- ModelManifest.cs
|       |       `-- ModelSelectionController.cs
|       |-- Models/FBX/                           [new]
|       |-- Prefabs/CAD/                          [new]
|       |-- Materials/CAD/                        [new]
|       |-- Settings/CurrentNoteMarkerProfile.asset [existing]
|       `-- Textures/                             [existing]
|-- Packages/                                     [existing]
|-- ProjectSettings/                              [existing]
|-- cad_pipeline/                                 [new; see next page]
|-- server/                                       [new; see next page]
`-- docs/                                         [existing]
```

Use a clean Unity content-build checkout for automated imports. Keep uploaded STEP files and conversion work outside Unity's Assets folder; only validated FBX, materials and prefabs should enter the content project. Commit Unity `.meta` files alongside their assets.

<!-- pagebreak -->

## 3. CAD and server directory structure

The entire layout below is proposed. The static-server setup uses only `server/compose.yaml`, `server/nginx.conf`, and `server/published/`. The API and workers belong to the later automated phase.

```text
cad_pipeline/
|-- input/<model-id>/<version>/source.step
|-- work/<job-id>/                 # temporary, isolated job files
|-- output/<model-id>/<version>/
|   |-- model.fbx                  # Unity Editor input
|   |-- source_metadata.json       # units, transform, CAD bounds
|   `-- conversion_report.json     # counts, errors, mesh checks
|-- scripts/
|   |-- step_to_mesh.py            # run with FreeCAD's environment
|   |-- mesh_to_fbx.py             # run with Blender's Python
|   `-- validate_mesh.py
`-- config/conversion.yaml         # units, axes, quality, limits

server/
|-- compose.yaml                   # local static demo
|-- nginx.conf
|-- published/                     # only approved public demo assets
|   |-- catalog.json
|   `-- models/<model-id>/<version>/android/
|       |-- manifest.json
|       `-- model.bundle
|-- api/                           # later: FastAPI application
|   |-- app/main.py
|   |-- app/routes/models.py
|   |-- app/routes/jobs.py
|   |-- app/services/storage.py
|   |-- app/services/auth.py
|   |-- app/db/                    # models and migrations
|   |-- requirements.lock
|   `-- Dockerfile
|-- workers/
|   |-- conversion/               # FreeCAD + Blender job runner
|   `-- unity_build/              # licensed Unity Editor runner
|-- deploy/compose.production.yaml
|-- .env.example                  # names only; no real credentials
`-- tests/                        # API, permissions, job lifecycle
```

### Storage policy

Keep original CAD sources, intermediate meshes, job logs and final releases separate. Use immutable model IDs and version directories. Store units, source hash, converter versions, axis mapping, mesh statistics and build target with each release. Retain the original STEP as the engineering source of truth; FBX is a tessellated visualization asset.

Ignore local secrets, job work directories and generated bundles in Git. Use private artifact/object storage or an approved large-file workflow for CAD and FBX. Publish the catalog only after every file referenced by the new release exists and passes validation.

<!-- pagebreak -->

## 4. STEP to FBX conversion workflow

### A. Inspect the STEP model in FreeCAD

1. Install a pinned FreeCAD release with Open CASCADE and a pinned Blender release. Record the versions used for accepted outputs. Standard Blender does not directly read STEP; the CAD tessellation stage is required.
2. Open the STEP file in FreeCAD. Check the import for missing bodies, broken geometry, invisible components and unresolved external references.
3. Record source units, part names, assembly placements and authoritative dimensions. FreeCAD commonly works internally in millimeters, but verify the STEP import using a known dimension.
4. Decide the model's intended orientation: Unity X = width, Y = thickness/up, Z = length. Record the transform from the CAD coordinate frame to this display frame.

### B. Tessellate and export a mesh

1. Use FreeCAD's Mesh workbench to create meshes from the selected shapes. A starting surface deviation of 0.1 mm is reasonable for this small demo; validate tighter features and curved edges before accepting it.
2. Include every required part and its assembly placement. Bake instance transforms or preserve an explicit part-transform mapping; check that repeated components are neither missing nor duplicated.
3. Export the resulting meshes as OBJ. OBJ does not reliably carry units or the full CAD hierarchy. Store units and part mappings in `source_metadata.json`. CAD colors/materials may require reconstruction. STL is a fallback when those properties are unnecessary.

### C. Optimize and export FBX in Blender

1. Import the OBJ with an explicit axis mapping. Confirm one known length before modifying geometry. If its coordinates are in millimeters, convert to meters once (0.001 scale) and apply the transform. Never add a second unit conversion on export.
2. Remove hidden internal detail only when it is not needed in AR. Repair normals and obvious mesh defects. Keep important edges, holes and separate parts when needed for inspection reference.
3. Use decimation or alternate levels of detail carefully. For one small visible object, start around 20k-100k triangles, 1-4 materials and textures no larger than 2048 px; profile on the target phone before accepting these budgets.
4. Position the mesh bounds center at the object origin for the existing renderer. Apply rotation and scale. In Blender's working frame, map width to X, thickness to Z/up and length to the chosen forward axis; verify the exported Unity axes instead of assuming an export preset is sufficient.
5. Export selected mesh objects as FBX, with no cameras, lights or animation. Use a documented Unity-oriented axis preset (commonly -Z Forward, Y Up). Confirm imported bounds, signs and normals in Unity. Archive the export settings with the conversion report.

**Dimension acceptance example:** the current note should occupy X = 0.180 m, Y = 0.006 m, Z = 0.240 m in Unity. Compare the mesh bounds with the source CAD after the agreed axis mapping. A visual comparison alone will not catch a 1000x unit error.

<!-- pagebreak -->

## 5. Unity import and model integration

### First delivery: include the model in the APK

1. Copy the validated FBX into `Assets/_Project/Models/FBX/`. Review Unity's unit conversion and importer settings; verify the final bounds in meters rather than forcing a scale factor without checking.
2. Assign URP-compatible materials. Disable unnecessary animation, cameras and lights. Use simple colliders only if required by interaction; the current outline display does not require a detailed mesh collider.
3. Create a prefab under `Assets/_Project/Prefabs/CAD/`. Use a wrapper root with identity rotation and scale `(1,1,1)`. Center the child mesh bounds on that root and correct the axes inside the prefab.
4. In `CurrentNoteMarkerProfile`, set `optionalModelPrefab` and the matching width, length and thickness in millimeters. Keep the marker/color configuration appropriate for the physical object. The existing setup tool is available at Tools > Note AR Setup Tool.
5. Open `Assets/Scenes/AR_Detection.unity`, build for Android and test alignment, rotation, lock and rescan on a supported ARCore device. The repository currently specifies minimum SDK 26 and target SDK 34; review current store requirements separately before a store release.

### Existing renderer contract

`NoteOutlineRenderer.ShowOutline` instantiates the optional prefab and puts its root at the outline's vertical midpoint. For table placement that is +T/2; for a top-cover image anchor it is -T/2. It sets local rotation to identity and does not auto-fit the mesh to the configured dimensions.

Therefore use a centered prefab root. A bottom-origin model would be vertically offset unless a child transform compensates for it. The displayed labels come from profile values; they are not measurements recovered from the downloaded mesh or the camera.

### Server delivery: add a runtime loading path

| New component | Responsibility |
|---|---|
| ModelBundleBuilder | Build Android bundles using Unity 6000.3.11f1 and the app's render configuration |
| ModelCatalogClient | Fetch catalog and manifest; validate schema and app/platform compatibility |
| ModelDownloadService | Download a version to a temporary file; validate size/hash, then commit it to cache |
| ModelCache | Store approved versions under Application.persistentDataPath; retain last usable version |
| ModelSelectionController | Load prefab, clone runtime profile, set model/dimensions and reset alignment |

Use `UnityWebRequestAssetBundle` or file-backed AssetBundle loading after integrity verification, then load the prefab by its published asset name. Ensure dependencies are also built, distributed and loaded. For the first bundle, package the model's materials/textures together and verify dependency output. Retain loaded resources while the model uses them; release them after destroying the instances. Include required shaders/variants and verify them on device.

AssetBundles are platform-specific; Android and iOS need separate builds. Pin the content-build Editor to the app version. Built-in AssetBundle support is available; Addressables is an optional later improvement and is not currently installed in the package manifest.

<!-- pagebreak -->

## 6. Server responsibilities and API design

### Two delivery stages

**Stage 1 - demo:** convert CAD manually, build the prefab/bundle in Unity, and serve a small catalog plus immutable releases from Nginx. A model built into the APK needs no server. A downloaded model needs the Unity loading modules described on the previous page.

**Stage 2 - automation:** FastAPI accepts authorized uploads and manages model metadata; PostgreSQL stores models, releases and job status; Celery with Redis queues conversion work. A conversion worker runs FreeCAD and Blender. A separate licensed Unity worker imports the FBX and builds Android content. Private filesystem storage is adequate for one host; use private object storage when workers run on separate machines.

```text
Admin -> HTTPS API -> private STEP storage -> job queue
                                               |
                     conversion worker -> FBX + validation
                                               |
                     Unity build worker -> Android bundle
                                               |
                     review / publish -> manifest + catalog

Phone -> HTTPS API/catalog -> released bundle -> cache -> AR
```

### Proposed API endpoints (not implemented)

| Method and route | Purpose |
|---|---|
| POST /api/v1/models | Admin creates a model record; returns model ID |
| POST /api/v1/models/{id}/versions | Multipart STEP upload; returns 202 and job ID |
| GET /api/v1/jobs/{jobId} | Progress, current stage, safe error and retry status |
| GET /api/v1/models | List releases visible to the caller |
| GET /api/v1/models/{id}/versions/{version} | Dimensions, platform, file hash and download location |
| POST /api/v1/models/{id}/versions/{version}/publish | Admin releases validated assets |
| GET /health/live and /health/ready | Process liveness and dependency readiness |

Suggested job states: `queued -> converting -> validating -> building -> awaiting_review -> published`, with `failed` and `cancelled` terminal paths. Keep publication separate from successful conversion. Use idempotency keys or source hashes to avoid duplicate jobs, and retry individual stages without replacing an already published version.

The phone should send model IDs and version requests for this workflow. Camera processing, plane tracking, anchor creation and existing color detection remain local; server GPU inference is not required for this design.

<!-- pagebreak -->

## 7. Local server setup: static model distribution

This is a complete configuration example for a local static server, not the automated API. Install Docker Desktop with the WSL 2 backend on Windows, or Docker Engine with Compose on Linux. Create `server/published/` and put a valid `catalog.json` there; `{"schemaVersion":1,"models":[]}` is enough to test serving before building a bundle.

Create `server/compose.yaml`:

```yaml
services:
  model-server:
    image: nginx:stable-alpine
    restart: unless-stopped
    ports:
      - "8080:80"
    volumes:
      - ./published:/usr/share/nginx/html:ro
      - ./nginx.conf:/etc/nginx/conf.d/default.conf:ro
```

Create `server/nginx.conf`:

```nginx
server {
    listen 80;
    server_name _;
    root /usr/share/nginx/html;
    autoindex off;
    location = /health {
        default_type text/plain;
        return 200 "ok\n";
    }
    location ~* \.json$ {
        add_header Cache-Control "no-cache" always;
        try_files $uri =404;
    }
    location / {
        try_files $uri =404;
    }
}
```

Run these commands from the proposed `server/` directory:

```powershell
docker compose config
docker compose up -d
docker compose logs --tail 50 model-server
curl.exe http://localhost:8080/health
curl.exe http://localhost:8080/catalog.json
```

Use `curl` on Linux. Expected results are `ok` and your catalog JSON. Stop the demo with `docker compose down`. Pin a tested Nginx image digest before production deployment.

For a phone on the same trusted Wi-Fi, use `http://<PC-LAN-IP>:8080`, not localhost. Allow inbound TCP 8080 on the Windows private network profile if needed. Android may block cleartext HTTP: prefer trusted HTTPS, or allow HTTP only in a development build and remove that exception for release. This sample exposes the published directory without authentication; put only approved demo assets there.

<!-- pagebreak -->

## 8. Model manifest and publication

The following is an illustrative manifest for the current note dimensions. Replace the placeholder size/hash with actual values from the built bundle. Store it at `server/published/models/note/1.0.0/android/manifest.json`.

```json
{
  "schemaVersion": 1,
  "modelId": "note",
  "version": "1.0.0",
  "displayName": "Note",
  "dimensionsMm": { "width": 180, "length": 240, "thickness": 6 },
  "meshUnits": "m",
  "axes": { "width": "X", "up": "Y", "length": "Z" },
  "pivot": "bounds-center",
  "platform": "Android",
  "unityVersion": "6000.3.11f1",
  "bundlePath": "/models/note/1.0.0/android/model.bundle",
  "prefabAssetName": "assets/_project/prefabs/cad/note.prefab",
  "sha256": "REPLACE_WITH_64_HEX_CHARACTERS",
  "sizeBytes": 0,
  "dependencies": []
}
```

`prefabAssetName` must match the asset name reported by the built bundle; the path above is only an example. Empty dependencies are valid only when the build has no separate bundle dependencies. Serve the manifest over a trusted channel: a checksum detects corruption but does not authenticate an untrusted publisher.

Populate `server/published/catalog.json` after the bundle and manifest are verified:

```json
{
  "schemaVersion": 1,
  "models": [{
    "modelId": "note",
    "version": "1.0.0",
    "manifestPath": "/models/note/1.0.0/android/manifest.json"
  }]
}
```

### Publication and client checks

1. Build for Android and inspect the bundle's asset names and dependencies. Copy the bundle into its versioned release directory. Compute its actual byte length and SHA-256, for example with PowerShell `Get-FileHash -Algorithm SHA256`.
2. Validate metadata against the prefab and original CAD dimensions. Write the completed manifest, then atomically replace the catalog. Do not modify a published version in place.
3. The phone resolves relative paths against the configured server URL, rejects incompatible manifests, verifies the downloaded bundle, and updates the active version only after successful loading.
4. If the network is unavailable, load the previously verified cached version or a model included in the APK. On a first launch with neither available, show a clear download-required state.

Do not infer a detection color or image marker from CAD geometry. Keep those settings local for the first demo; add explicit validated detection configuration to the manifest if remote model selection later requires it.

<!-- pagebreak -->

## 9. Production server setup and acceptance

### Deployment sequence for the automated phase

1. Provision a supported Linux host (for example Ubuntu 24.04 LTS), Docker/Compose, persistent storage and DNS. A reasonable initial single-job pilot is 8 vCPU, 16 GB RAM and 100 GB SSD, adjusted after measuring representative CAD assemblies. A static-only pilot can start at 2 vCPU and 4 GB RAM. These are planning estimates, not measured requirements.
2. Implement the API, database migrations, conversion scripts and Unity bundle builder from the proposed structure. Define pinned dependencies and images. The static Compose example does not provide these services.
3. Configure HTTPS at Nginx or another reverse proxy with a valid certificate. Publish only ports 80/443 as required; restrict administrative access. Keep PostgreSQL, Redis and worker endpoints on the private network.
4. Supply database/storage credentials through the deployment's secret store or protected environment files. Use separate admin and read-only client permissions. Restrict CAD uploads by size and verified format; enforce per-job time, memory and disk limits. Run conversion in isolated directories without network access unless explicitly needed.
5. Install the same Unity Editor and Android build modules as the app on an isolated build runner. Set up an appropriate Unity license and required URP packages/shaders. Do not place activation credentials in uploaded content or published storage.
6. Start the database and queue, apply migrations once, then start the API, conversion worker and build worker. Confirm readiness checks before admitting jobs. Begin with one conversion/build at a time and measure resource use.
7. Persist STEP originals, release metadata and published bundles. Back up the database and artifact store together and perform a restore test. Record job duration, failures, queue depth, disk usage, HTTP errors and app load errors. Retain the previous catalog/version for rollback.

### Acceptance checklist

| Check | Passing result |
|---|---|
| CAD import | All intended parts and assembly placements match the source |
| Units and axes | Known dimensions match in meters; no mirrored or rotated model |
| Tessellation | Critical features are preserved; deviation meets the agreed visual tolerance |
| Prefab alignment | Centered root fits the outline for table and top-cover anchoring |
| Android rendering | Correct URP materials; no missing shaders; acceptable frame time and memory |
| Server delivery | Catalog, manifest, hashes and bundle dependencies are valid |
| Recovery | Interrupted downloads do not replace usable cache; offline fallback works |
| Version changes | Incompatible content is rejected and previous release can be restored |
| AR behavior | Rotation, lock, rescan and tracking recovery work on the physical device |

### Project references

- [Original design PDF](../output/pdf/AR_Note_Object_Detection_Design_Document.pdf): sections 11, 13, 18 and 20 establish model display, profiles, current dimensions and future CAD support.
- [Profile source](../Assets/_Project/Scripts/Detection/NoteMarkerProfile.cs) and [renderer source](../Assets/_Project/Scripts/Visualization/NoteOutlineRenderer.cs): existing prefab field, axes, millimeter conversion and midpoint placement.
- [Current profile](../Assets/_Project/Settings/CurrentNoteMarkerProfile.asset), [package manifest](../Packages/manifest.json) and [Editor version](../ProjectSettings/ProjectVersion.txt): verified configuration baseline.

Recommended delivery order: one manually converted model in the APK, then verified AssetBundle download from the demo server, then authenticated upload/conversion automation. Each stage should pass the relevant checks above before adding the next one.
