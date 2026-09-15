# Download verification

Verify the installer before running it. For VIP 1132 v3.0.16, the expected SHA-256 is:

```text
11CA5ADD6F5D200CC9143BBD178060841FD4D3C442992830E66C7E6A9E7671C7
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.16.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,708,816 bytes.

The corresponding source, build files, and release history are available in this repository.
