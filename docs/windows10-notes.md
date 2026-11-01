# Windows 10 source notes

## Source ISO (Task 1, measured 2026-11-01)

- File: `Win10_22H2_English_x64v1.iso` on Proxmox `local`, 6,140,975,104 bytes, volume label `CCCOMA_X64FRE_EN-US_DV9`.
- `install.wim`: 5,181,270,654 bytes, 11 editions, no `ei.cfg`. Windows 10 Pro is **index 6**.
  Indexes: 1 Home, 2 Home N, 3 Home Single Language, 4 Education, 5 Education N, 6 Pro, 7 Pro N,
  8 Pro Education, 9 Pro Education N, 10 Pro for Workstations, 11 Pro N for Workstations.
- Pro image build: 10.0.19045 (guest OS reports 19045.0 for the DISM host).

## Edge / WebView2 in the Pro image (read-only mount via DISM on build host 9200)

- `Program Files (x86)\Microsoft\Edge` present (Chromium Edge **92.0.902.67**, `msedge.exe`) and `Program Files (x86)\Microsoft\EdgeUpdate`.
- No `EdgeCore`, no `EdgeWebView` directory under either Program Files root: **no WebView2 Runtime in this ISO**.
- Legacy EdgeHTML is present in System32 (`edgehtml.dll`, `MicrosoftEdgeCP.exe`, ...) and ten `microsoft-windows-edge*` WinSxS directories.
- Consequence: `edge-files` removal must keep the Edge and EdgeUpdate trees (x86 tree is the one that exists); WebView2 preservation logic has nothing to preserve and is not needed.

## Inventory fixtures

Raw DISM output for index 6 lives in `tests/fixtures/win10-19045/` (`appx.txt` provisioned apps, `caps.txt` capabilities, `feat.txt` features).

## Build host

VM 9200 `tiny10-build-host`: Windows 10 Pro 19045, Audit mode (built-in Administrator), QGA installed from virtio-win-0.1.240 ISO (build host only; templates use the pinned QGA 110.2.3). Source VMs must use `scsi0` (virtio-scsi-single), never `sata0`.
