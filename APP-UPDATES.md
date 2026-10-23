# DAS app updates (no more copying the exe)

DAS admins publish a new version of the DAS app from Unity. Every DAS PC is offered it and updates itself, downloading
only the files that changed. Nobody copies or installs anything by hand after the one-time setup below.

## How it works

```
Unity (DAS admin)                         VRSP (vrc.mahindra.com)                  DAS app (every PC)
DAS > Publish App Update
  sign in (Microsoft, DAS Admin) ───────► only DAS Admins may publish
  build DAS (Windows 64-bit)
  list every file + checksum,
  SIGN the list with the private key ───► checks the signature (DAS_APP_PUBLIC_KEY)
  upload only files the server           store files by checksum:
  doesn't have yet ─────────────────────► AssesmentSystem/das-app/blobs/<sha256>
  commit ───────────────────────────────► release live (everyone) or pilot ─────► after sign-in and every 3 h:
                                                                                  "DAS 1.4.0 is available"
                                                                                  (optional: Update now / Later)
                                                                                  checks the signature with the key
                                                                                  built into DAS, downloads changed
                                                                                  files, checks each checksum
                                                                                  DASUpdater.exe: waits for DAS to
                                                                                  close, replaces files (puts the old
                                                                                  ones back if anything fails),
                                                                                  restarts DAS
```

* **Updates are optional.** People choose *Update now* (DAS restarts in a minute) or *Later* (the update is installed when
  they quit DAS). *Ctrl+Shift+U* checks right away.
* **Only changed files move.** A 400 MB DAS where only the code changed is a few MB to publish and to download. Files are
  stored by checksum, so a file shared by several versions is uploaded once.
* **Signed.** DAS installs only releases signed with the DAS signing key, so even someone with access to the server or the
  bucket cannot push a program onto the PCs.
* **Pilot group.** A release can go to a few people first (VRSP > Admin > DAS > *DAS app updates* > Edit pilot group),
  then to everyone with one click.
* **Taking a version back.** *Withdraw* in the same panel: PCs on it are asked to switch to the newest version that's still out.
* Files people make inside the DAS folder (logs, exports) are never touched. An update removes only files that the previous
  release itself installed.
* The last result is shown at the next start (for example "DAS was updated to 1.4.0"). The updater's log is in
  `%USERPROFILE%\AppData\LocalLow\<company>\<product>\AppUpdate\<version>\update.log`.

## Where DAS lives on each PC

DAS updates its own folder, so the folder must be one the person can change. **Use `D:\DAS`** (or `C:\DAS` on PCs without a
D: drive) on every PC, and start DAS from a desktop shortcut to `D:\DAS\DAS.exe`.
Copy the folder **as the person who uses the PC** (not "Run as administrator", not an admin account). Files copied or
built by a program running as administrator belong to Administrators, and a normal account can't change them. DAS checks
this before downloading: if the account can't change the folder, nothing is downloaded or changed, and the message says
what to do (copy DAS to a folder the person creates, e.g. `D:\DAS`, or ask IT for Modify permission on the folder).
The updater never asks for administrator rights and never changes permissions.

Don't run DAS straight from Unity's build output folder on the developer PC: Unity (especially when started as
administrator) creates those files, and the publish window rebuilds into that folder. Copy the build to `D:\DAS` and test there.

## Security software (SentinelOne) and code signing

`DASUpdater.exe` replaces program files of another program, which is exactly what security software watches for, so an
**unsigned** updater can be flagged or blocked (it was flagged by SentinelOne on 23 Oct 2026). Before rolling out to
many PCs:

1. Get the company **code-signing certificate** from IT (in the Windows certificate store or on a USB token, or a .pfx).
2. In Unity: *DAS > App Updates > Code Signing...*: enter its thumbprint (or choose the .pfx; its password is read from
   the `DAS_SIGN_PFX_PASSWORD` environment variable, never saved), and press **Test**. It needs `signtool.exe` from the
   Windows SDK ("Signing Tools for Desktop Apps"). Then switch on **Required for every build**.
   Every Windows build now signs the DAS exe and `DASUpdater.exe` (Unity's own files are signed by Unity) before the
   release list is made.
3. Ask IT to **allow DAS by signer** in SentinelOne (and in any application control), and give them
   `Scripts/AppUpdate/Updater~/DasUpdater.cs` to review if they want: it only copies the downloaded, checksum-verified
   files into the DAS folder, keeps the old ones until it has finished, and starts DAS again.

Until then, test on a PC where IT has allowed the updater.

## One-time setup (PK)

1. **Make the signing key** (once, ever). In Unity: *DAS > App Updates > Create Signing Key...*
   * Save the private key **outside the project**, e.g. `Documents\das-app-signing-key.xml`. Unity refuses a path inside
     the project, so it never ends up in Git or in a build.
   * **Back it up** (password manager or an encrypted drive). Anyone with it can publish DAS updates. If it's lost, PCs need
     one more DAS copied by hand with a new key.
   * Unity writes the public key into `Scripts/AppUpdate/DasAppUpdateKey.cs`. **Commit that file.**
   * The line for VRSP is put on the clipboard (`DAS_APP_PUBLIC_KEY=<RSAKeyValue>...`). *DAS > App Updates > Copy VRSP Key
     Line* copies it again later.
2. **VRSP server:** add that line to `backend/.env` and restart VRSP so it reads it (with PM2: `pm2 restart <vrsp app name> --update-env`; `pm2 ls` shows the name). Until it's there,
   publishing stops with "App updates aren't set up on the server".
3. **VRSP code:** merge the `das-app-updates` branch (server routes + the *DAS app updates* admin panel) and deploy.
4. **The last manual copy.** Build DAS once (File > Build, or the publish window) with the key in place. Every Windows build
   now contains `DASUpdater.exe` and `das-release.txt`. Copy that folder to `D:\DAS` on every PC one last time. From then on
   every update arrives by itself.
   * Easiest: publish that build as the first release (step below) and copy the build folder it made. Then the PCs and the
     server agree on what's installed from the start.

## Publishing an update

1. *DAS > Publish App Update...* and sign in with your Microsoft account (you need **Admin** in VRSP > DAS access).
2. Check the version (it suggests the next one, e.g. 1.3.0 → 1.3.1; versions are `major.minor.patch` and must go up),
   write what's new (people see it in the update dialog), choose **Pilot group** or **Everyone**, and pick the build folder.
3. *Build and publish.* Unity sets Player Settings > Version, builds, signs, uploads only new files and commits.
4. In VRSP > Admin > DAS > *DAS app updates* you see which version each person runs, and can move a pilot release to
   everyone, or withdraw one.

## Checks (no Unity needed)

`sh Scripts/Storage/Tests~/run-all.sh` runs everything, including:
* `Scripts/AppUpdate/Tests~/run.sh`: versions, the signed list, the update plan, the real `DASUpdater.exe` (replace, remove,
  put back on failure) and a cross-check that VRSP and DAS sign and verify the same way.
* `Scripts/AppUpdate/Tests~/e2e/e2e.js`: end to end with VRSP's real controller over HTTP and the real updater. It covers
  publish 1.0.0 → pilot 1.0.1 → everyone → withdraw → back to 1.0.0, a damaged download, a wrong key, a non-admin
  publishing, and DAS not closing. It needs the VRSP repo next to this one (or `VRSP_BACKEND`), with `npm install` done.
