# VIP 1132 User Manager

Version 3 is a clean native-Windows rebuild recovered from the original PyInstaller executable. It keeps the numeric-user workflow while replacing the slow 221 MB one-file Python/Qt bundle with a fast WPF application.

The interface uses a black glass-and-neon visual system with raised controls, cyan hover glows, soft depth, and restrained animated sparkles. The supplied VIP 1132 artwork is embedded as the Windows icon, and the supplied six-second animation loops during both application installation and the full Zoom deployment workflow.

## What it does

The full setup:

1. Stops Zoom processes.
2. Runs Zoom's official CleanZoom tool.
3. Deletes the previous managed numeric Windows account.
4. Creates the next numeric local administrator (the password matches the username, preserving the original workflow).
5. Validates, downloads, and installs the latest 64-bit Zoom Workplace MSI for all users.
6. Opens Zoom interactively as the new Windows user.
7. Reports success only after a visible Zoom window is running in the current desktop session and owned by the new user.

State is stored in `C:\ProgramData\VIP1132\state.json`. Existing numeric local users are detected automatically on first launch, so the rebuilt app continues from the old sequence instead of starting again at user 1.

The app does not automate Zoom's appearance, audio, video, meeting, or advanced settings. Once Zoom opens visibly as the new Windows user, setup is complete.

The Zoom MSI download begins during cleanup and user preparation. Cached CleanZoom and Zoom installer files are reused only while fresh and after their archive/package structure validates successfully. Major workflow phases log their elapsed time so real installations can be profiled without adding fixed delays.

The SUPPORT VIP control opens `https://pnpatvip.com` through the normal Windows default-browser mechanism and sends no app or user data.

## Build

Requirements:

- Windows 10/11 x64
- .NET 8 SDK
- Inno Setup 6 (only for building the installer)

The tiny installer animation host targets the Windows-bundled .NET Framework 4.8; its reference assemblies are restored automatically by the build.

Run:

```powershell
.\scripts\Build.ps1
```

Outputs:

- `dist\VIP1132-Setup-3.0.10.exe` — self-contained installer; no separate .NET install required.
- `dist\VIP1132-portable\` — much smaller framework-dependent build for PCs that already have the .NET 8 Desktop Runtime.

## Security and signing

Numeric passwords are intentionally preserved for compatibility with the original room workflow, but they are weak credentials. Use this only for the isolated local room accounts it was designed for. The generated binaries are unsigned until a Windows code-signing certificate is supplied, so SmartScreen may warn on first launch.
