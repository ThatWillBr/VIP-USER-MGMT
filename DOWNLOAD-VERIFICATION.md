# Download verification

Verify the installer before running it. For VIP 1132 v3.0.19, the expected SHA-256 is:

```text
3E5B30CBDD9044B47A05CFF50A2FD44EE5074869C8A1347606F56D777C7873E9
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.19.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,708,338 bytes.

The corresponding source, build files, and release history are available in this repository.
