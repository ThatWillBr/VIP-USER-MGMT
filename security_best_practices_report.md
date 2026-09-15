# VIP 1132 security review — v3.0.13

## Scope and result

Completed September 14, 2026 against the tracked WPF source, installer script, update manifest, and static download page. The build and 28 local validation checks passed. This is a source review, not a third-party penetration test or a code-signing audit.

## Findings

### SEC-001 — High: predictable local administrator credentials

**Location:** `src/VIP1132/Services/SetupWorkflow.cs:91-92`, `src/VIP1132/Services/WindowsUserService.cs:47-53`.

**Evidence:** New numeric accounts use the username as their password and are added to the local Administrators group.

**Impact:** Anyone who can interactively reach the PC and knows the room-account convention can authenticate as that administrator account. This is an intentional compatibility behavior, not a malware indicator, and it is unsuitable for shared or untrusted machines.

**Status:** Accepted product constraint. The application and README disclose it. The proper remediation is unique, protected credentials and least-privilege accounts; that would change the established room workflow.

### SEC-002 — Medium: release packages are not code-signed

**Location:** `installer/VIP1132.iss:15-38`, `README.md:51-53`.

**Evidence:** The release installer has no Authenticode signing step. Windows may show SmartScreen or publisher warnings.

**Impact:** Users must independently validate the download origin and SHA-256. An unsigned warning is not evidence of malware.

**Status:** Open until a Windows code-signing certificate and a signing step are available. Use [DOWNLOAD-VERIFICATION.md](DOWNLOAD-VERIFICATION.md) before installing.

### SEC-003 — Resolved in v3.0.13: failed first-profile launch could repeat setup work

**Location:** `src/VIP1132/Services/UserProfileReadinessService.cs`, `src/VIP1132/Services/SetupWorkflow.cs:58-81`.

**Evidence:** A failed first Windows-profile initialization could leave an unregistered profile directory and the next full setup would replace the account and repeat Zoom installation.

**Resolution:** v3.0.13 preserves only unregistered incomplete profile directories under `C:\ProgramData\VIP1132\ProfileRecovery`, retries the existing account and install, verifies Windows profile registration, and includes the native Win32 error code in failures.

## Review notes

No hard-coded API keys, tokens, browser storage of secrets, `eval`, dynamic HTML insertion, or message-event handlers were found in the reviewed source. Runtime web-server headers and GoDaddy-injected scripts are outside this repository and require separate hosting-level verification.
