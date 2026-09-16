# Download verification

Verify the installer before running it. For VIP 1132 v4.0.0, the expected SHA-256 is:

```text
1C10F5380BE1ED90378D6800672FBDA1E8637960C374F4D3901261D9C8A308EE
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-4.0.0.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,710,949 bytes.

The corresponding source, build files, and release history are available in this repository.
