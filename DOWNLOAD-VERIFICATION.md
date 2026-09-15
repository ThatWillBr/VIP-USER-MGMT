# Download verification

Verify the installer before running it. For VIP 1132 v3.0.18, the expected SHA-256 is:

```text
703A2C57B7B9EAF14976A0EC2A5CB88EE6888DBBFE3F8C1CE032473C6C54E9ED
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.18.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,708,299 bytes.

The corresponding source, build files, and release history are available in this repository.
