# Download verification

Verify the installer before running it. For VIP 1132 v3.0.13, the expected SHA-256 is:

```text
37CA67E0E9507399F806A7B2DBAB094FE3E1ABE7342ECDB4B0E0C8F45D061BFC
```

In PowerShell, after downloading the installer:

```powershell
Get-FileHash "$env:USERPROFILE\Downloads\VIP1132-Setup-3.0.13.exe" -Algorithm SHA256
```

Install only when the reported `Hash` exactly matches the value above. The published installer is 58,701,304 bytes.

The corresponding source, build files, and release history are available in this repository.
