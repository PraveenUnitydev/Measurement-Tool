# Publishing vehicles to DAS (engineers)

Engineers publish their own vehicles straight from Unity. There's no need to send FBX files to the developer, rebuild the
app, or upload anything to the bucket by hand. Every DAS PC sees the new version, with an "update available" notice.

## How it works

```
Unity (engineer)                         VRSP (vrc.mahindra.com)                 DAS app (every PC)
DAS > Publish Vehicle
  sign in (Microsoft) ─────────────────► publishing session (separate from
                                         the DAS app session)
  Publish ─ start ─────────────────────► new version + folder
          ─ build ONLY this vehicle
            (its own Addressables catalog)
          ─ upload ────────────────────► Bundles/v/<id>/<version>/ (signed links)
          ─ commit ────────────────────► checks every file, then catalog.json
                                         points at the new version ──────────► update notice; Vehicles screen
                                         (previous 5 versions kept)            shows "Update needed"
```

* Each version gets its own folder, so publishing never touches the version people are using.
* Each vehicle has its own small catalog, so **the app no longer has to be built on the PC that built the vehicles**.
  The app and the vehicle builds only need the same Unity, Addressables and URP versions, and the same scripts and shaders.
* Materials, look and the thumbnail framing are set by hand, as before.

## One-time setup (developer)

1. **VRSP**: merge `das-publish` (it includes `das-thumbnails`) and deploy. The server's storage account needs
   permission to sign upload links and list files in the bucket (*Storage Object Admin* on the bucket, or *Storage Object
   Creator* plus *Storage Object Viewer*). Bucket **CORS is not needed**, because Unity isn't a browser.
2. **App**: build the DAS app once from `das-storage` (or later). It loads per-vehicle catalogs.
3. **Shared Unity project for engineers**: a copy of the DAS project (same Git repo or branch), with the same
   Unity version (6000.0.63f1), the same Addressables (2.7.6) and URP versions, and the same scripts and shaders
   (`VehiclePrefabData`, the DAS Shader Graphs, `Scripts/Shaders`). Build target: **Windows**.
   - Use this copy for publishing, not the PC you build the app on. A publish build writes Addressables build state
     into the project, so rebuild your normal Addressables content before you next build the app.
4. **Who may publish what**: in the VRSP admin dashboard's **DAS** tab, each person with DAS access has a
   **Publishes: … Edit** line. Enter vehicle ids (`xuv700, thar`), or `*` for all vehicles plus permission to add new ones.
   DAS Admins can publish everything and can switch a vehicle back to an earlier version.

## Publishing (engineer)

1. Open the shared project. Import or replace your FBX, update the prefab and its materials, and check the look in the Scene view.
2. **DAS > Publish Vehicle…**
3. **Sign in with Microsoft**. The browser shows a code; check it matches, then confirm.
4. Pick your **vehicle** (or **+ New vehicle…** if you're allowed to add one) and drag its **prefab** in from the Project window.
5. Check the **details** (name, manufacturer, model year, category) and write **what changed**.
6. **Thumbnail**: frame the vehicle in the Scene view (a ¾ front view on a plain background) and click **Capture Scene view**, or
   choose a picture. For an existing vehicle you can skip this to keep the current thumbnail.
7. Fix any **errors** in the checks. These cover empty or pink materials, missing scripts and no meshes. Warnings, such as
   scale, non-URP shaders or a very heavy mesh, are worth a look but don't block publishing.
8. **Publish**. The window builds the vehicle, uploads it and makes it live. Keep Unity open until it says *live*.

If an upload fails, nothing goes live. Click Publish again (it uses a new version).

## Switching back (DAS Admin)

In **DAS > Publish Vehicle** → **Published versions**, click **Switch back** on an earlier version. The last 5
versions are kept. On the server, every catalog that is replaced is also saved in `AssesmentSystem/catalog-history/`.

## Moving the existing vehicles

The 59 vehicles in the old single catalog keep working as they are. To move one to the new way, publish it once with
the tool (pick it in the list; it needs to be assigned to you, or you need to be an Admin). From then on it updates on its own.
DAS PCs see "Update needed" for it once, because its files change.

## First run: what to check

The publish tool calls the Addressables build from code. It was written against the Addressables 2.7.6 source but
hasn't been run inside Unity yet. On the first publish, check:
- the Console shows no build errors, and `ServerData/DASPublish/<id>/<version>/` contains `catalog_<id>.json` (or `.bin`),
  a `.hash` file and `StandaloneWindows64/*.bundle`;
- after it says *live*, start the DAS app: the vehicle shows "Update needed", the download works and the vehicle opens;
- your Addressables Groups window looks as it did before (the tool puts every setting back).
