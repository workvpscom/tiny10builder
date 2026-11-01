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

## Spike result (Task 1, 2026-11-01): stock Win10 Setup honours the answer file

Spike VM 9201 (Q35, OVMF, no TPM, `scsi0` 20G on virtio-scsi-single, e1000), booted from the stock ISO plus a second small ISO carrying `autounattend.xml`:

- Setup consumed the answer file: wiped disk 0, created EFI/MSR/NTFS partitions, installed by `/IMAGE/NAME` = `Windows 10 Pro`. The product-key page still showed (`ProductKey/WillShowUI=Always`, as in the Win11 profile); "I don't have a product key" was chosen by the operator.
- `DriverPaths` pointing at the `virtio-win-0.1.240` ISO (`<drive>:\vioscsi\w10\amd64`) was enough for Setup to see the SCSI disk, and `vioscsi.sys` ended up in `Windows\System32\drivers`.
- `oobeSystem` `Reseal Mode=Audit` entered Audit mode as built-in Administrator; the `auditUser` `RunSynchronousCommand` ran and wrote `C:\spike-marker.txt` (`audit-ok`) before shutting down.
- Offline inspection (`losetup --read-only`, `ntfs3 ro,noexec`): `State.ini` shows `ImageState=IMAGE_STATE_SPECIALIZE_RESEAL_TO_AUDIT`.
- Conclusion: no `winpeshl.ini` override and no `setup_old.exe` are needed on Win10. `vioscsi` still has to be injected into `boot.wim` for the product (Task 6); the spike used external DriverPaths only.
