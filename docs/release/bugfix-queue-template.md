# Bugfix Queue Template

## Queue Fields
| Field | Description |
|---|---|
| ID | Unique issue identifier (for example `BUG-2026-001`) |
| Priority | `P0`, `P1`, `P2`, `P3` |
| Status | `Open`, `Mitigated`, `In Progress`, `Resolved`, `Verified` |
| Affected Version | Version(s) where issue is observed |
| Target Fix Version | Planned patch/minor version |
| Module | Hotkeys/Audio/Inference/Insertion/Installer/etc |
| Environment | Windows build, hardware class, app target |
| Summary | One-line problem statement |
| Repro Steps | Minimal deterministic reproduction |
| Evidence | Paths to logs/reports/screenshots |
| Workaround | Temporary mitigation if available |
| Owner | Responsible engineer |
| Created At | UTC timestamp |
| Updated At | UTC timestamp |

## Queue Table
| ID | Priority | Status | Affected Version | Target Fix Version | Module | Owner | Summary |
|---|---|---|---|---|---|---|---|
| BUG-YYYY-NNN | P1 | Open | 1.2.0 | 1.2.1 | Insertion | <owner> | <summary> |

## Notes
- Keep this queue in sync with known issues and release notes.
- `P0/P1` entries must be reviewed in each rollout checkpoint.
