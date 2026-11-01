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

## Build host tooling

- .NET SDK **8.0.400** installed on 9200 from `https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.400/dotnet-sdk-8.0.400-win-x64.exe` (220,878,904 bytes, Authenticode valid, `CN=.NET, O=Microsoft Corporation`), SHA-256 `700A27F3DB3F698FDB11D134D1226F03D11EB6DCBCC2B6C043393172F723E7DE`.
- `scripts/dev/buildhost.sh push|run` copies the working tree to `C:\work\tiny10` on 9200 (throwaway ISO in the `ide2` CD drive) and runs PowerShell there through QGA.

## Media build number (Task 3b)

DISM on the real `Win10_22H2_English_x64v1.iso` reports the Pro image (index 6) as `Version : 10.0.19041` with `ServicePack Build : 2965`, so the parsed image version is 10.0.19041.2965. Windows 10 22H2 is an enablement package on the 2004 servicing base, and DISM reports 19041 for the whole 2004/20H2/21H1/21H2/22H2 family. The installed guest only reports runtime build 19045 after the enablement package applies.

Rule: accept `Version.Major == 10`, build 19041 to 19045 inclusive, Client, x64. Reject Windows 11 (22000+), builds below 19041 (18363, 17763), builds above 19045, x86/arm64 and Server. `ToolLocator.ServicingBase` maps 19041-19045 to 19041.
