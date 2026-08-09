# Unreal99 project instructions

## Required post-change installation workflow

Whenever code under `src/`, a build script, or an installer script changes, do not stop after a
successful `dotnet build`. Before handing the task back to the user, complete all of the following
on Windows:

1. Build and publish the current game and installer from the working tree with
   `./build-installer.ps1`. The current payload must be under
   `artifacts/installer/payload/`; do not reuse an older `artifacts/game-current` or
   `artifacts/installer-latest` directory.
2. Update the per-user installation with
   `artifacts/installer/Unreal99Installer.exe install --source artifacts/installer/payload`.
   Keep Start menu creation enabled.
3. Verify that `%APPDATA%/Microsoft/Windows/Start Menu/Programs/虛幻競技場 99.lnk` exists and
   resolves to `%LOCALAPPDATA%/Programs/Unreal99/Unreal99.exe`, with no stale command-line
   arguments.
4. Compare SHA-256 hashes of the installed `Unreal99.exe` and `Unreal99.dll` with the newly
   published payload. The task is not complete unless both pairs match.
5. Report the installed target and verification result. Do not launch the installed game merely
   to verify installation unless gameplay testing is also required; launching must never leave a
   visible terminal window.

If a running `Unreal99` process blocks a required publish or installation, first verify that its
executable path belongs to this project's installed game or build output, then terminate that
specific process and continue the update. Do not leave a code change on an old installed build
merely because the game was running.

Documentation-only changes do not require reinstalling the game unless they also change generated
runtime assets included in the published payload.
