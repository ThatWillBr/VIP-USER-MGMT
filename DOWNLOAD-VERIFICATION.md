# Download verification

Verify the installer before running it. For VIP 1132 v3.0.19, the expected SHA-256 is:

```text
28A7DC25DE457EF28DC0E9E5985461BD5B4C88E27D17A9AF8AEDAC959097E83C
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.19.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,708,576 bytes.

The corresponding source, build files, and release history are available in this repository.
