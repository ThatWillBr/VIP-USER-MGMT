# Download verification

Verify the installer before running it. For VIP 1132 v4.0.0, the expected SHA-256 is:

```text
B47112CF5181683429DF50A4E3745DF579C8D18ECFD96607F632D1AEECD7D740
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-4.0.0.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,710,119 bytes. The portable package `VIP1132-Portable-4.0.0.zip` is 5,002,052 bytes and its SHA-256 is `8B29FB06FBF550853F7C5782DBE46791982DEF4954750F70FB8DED6F0FCA4DA0`.

The corresponding source, build files, and release history are available in this repository.
