# Download verification

Verify the installer before running it. For VIP 1132 v3.0.14, the expected SHA-256 is:

```text
003A53B1CDF3B6F8690674F2A776B255A9FCE737CCDC55E476A76BDCAAFF991C
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.14.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,699,471 bytes.

The corresponding source, build files, and release history are available in this repository.
