# Download verification

Verify the installer before running it. For VIP 1132 v3.0.17, the expected SHA-256 is:

```text
F541A1D6C33C51B8FE8E1A796F68B837E389CE35162C03A1CBE2C5DDBDD7C0B6
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.17.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,708,099 bytes.

The corresponding source, build files, and release history are available in this repository.
