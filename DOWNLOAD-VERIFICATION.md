# Download verification

Verify the installer before running it. For VIP 1132 v3.0.15, the expected SHA-256 is:

```text
446B9D3DACDA265804AA532B6D895269CFF104921DF165A81EDB2B8498CA6FAF
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.15.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,706,402 bytes.

The corresponding source, build files, and release history are available in this repository.
