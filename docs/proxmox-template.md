# Self-contained Proxmox Windows 10 VM backup

English (default) | [Tiếng Việt](proxmox-template.vi.md)

## Scope

tiny10builder builds a Windows 10 22H2 **Core** installation ISO with pinned Cloudbase-Init, QEMU Guest Agent, VirtIO storage/serial drivers, Microsoft Edge kept, and automatic Audit preparation followed by Sysprep shutdown. A Proxmox `vzdump` archive of the sealed VM is the second deliverable. The approach is derived from the maintainer's tiny11builder (Windows 11) fork; no post-install patch, Proxmox node modification, or `cicustom` is required. Internal names such as the `tiny11-<guid>` job folders, the `tiny11-template` staging folder, `C:\ProgramData\Tiny11Template` and the `Tiny11Core` recipe mode are inherited from the Windows 11 fork and kept for compatibility.

The delivered archive is an **ordinary VM backup**, not a Proxmox template. Restore and start it directly. Do not run `qm template`, do not clone it as part of this workflow, and do not use `cicustom`.

> Core removes WinSxS components and WinRE, and disables the Windows Update and Defender services and policies; the removal is irreversible. A Core image cannot be serviced after the build. Licensing and activation remain the operator's responsibility.

## Build the ISO

Build on a Windows 10 host with the .NET 8 SDK and DISM. The measured run used a Windows 10 Pro 19045 VM (4 vCPU, 8 GB, DISM 10.0.19041.844, .NET SDK 8.0.400).

Source media: `Win10_22H2_English_x64v1.iso` (6,140,975,104 bytes), Windows 10 Pro (index 6). DISM reports it as Version 10.0.19041 / ServicePack Build 2965 (19041.2965); the installed guest reports 10.0.19045 after servicing.

Recipe fields used (a complete, valid example is [recipe.proxmox-core.json](recipe.proxmox-core.json); the JSON field `SelectedOptions` must equal the Core option set exactly, and `ImageIndex` must be the index of Windows 10 Pro, which is `6` on this ISO; the builder checks the Pro index before it downloads anything and names it if you pick another image):

| Field | Value |
| --- | --- |
| `ImageIndex` | `6` |
| `Flavor` | `Core` |
| `Mode` | `Tiny11Core` |
| `CoreAcknowledged` | `true` |
| `SelectedOptions` | the 30 options of `CleanupCatalog.ForMode(Tiny11Core)`, listed in the example file |
| `RemoveRedundantData` | `true` |
| `TemplateProfile` | `ProxmoxCloudbaseInit` |
| `Compression` | `Recovery` |

Command (the `--iso` argument is a folder containing the extracted ISO contents):

```text
tiny10 build --recipe recipe.json --iso <folder with ISO contents> --workspace <dir> [--verbose]
```

Exit codes: 0 success, 1 build failure or cancellation, 2 validation/usage error.

What the builder downloads and pins:

- `oscdimg.exe` is downloaded from `msdl.microsoft.com` when missing, pinned to SHA-256 `2000160B2C5044691B2F9A0AC308E5207F273D4880A572457AF16D05886BA861`; the measured run did this with the operator's approval.
- Cloudbase-Init 1.1.9 and QEMU Guest Agent 110.2.3 MSIs are downloaded and verified by SHA-256. The QEMU Guest Agent MSI is unsigned, so the hash is its only check; only Cloudbase-Init also gets an Authenticode publisher check.
- The embedded VirtIO drivers (virtio-win 0.1.240, `w10/amd64`) are bundled with the source. See [driver provenance](virtio-scsi-driver.md).

Duration: about 33 minutes for the first complete run. The final steps are component cleanup with ResetBase, the `boot.wim` edit (`vioscsi` on every index), recovery compression to `install.esd`, and `oscdimg`.

Output ISO `tiny10-core-proxmox-win10-v1.iso`: 3,169,628,160 bytes.

```text
SHA-256: 2CB4BD513AF81B506FD979C3FF2D56B54A3F20D0A059A7D7D2F64D79E137FD67
```

`report.json` recorded SourceBuild 10.0.19041.2965, EdgePreserved true, WebView2Present false. `EdgePreserved` is derived from the recipe profile (the intent), not measured on the image, and `WebView2Present` is a placeholder that is always false because nothing detects WebView2. Transferring the ISO from the build host to the node with sendme 0.36.0 took 96 s for 3 GB; the SHA-256 was verified on the node.

## Install and seal the source VM

Create the source VM with `scripts/proxmox/create-source-vm.sh <vmid> <iso-volid> [--storage local-lvm] [--bridge vmbr1]`. It refuses VMIDs below 9200 and existing VMIDs, accepts only `--storage local-lvm`, refuses to continue (exit 3) when `local-lvm` is inactive, cannot be read or has less than 40 GiB free (the thin pool is shared with other VMs), and never starts the VM. The hardware it creates (example VMID 9210):

| Setting | Value |
| --- | --- |
| Machine | Q35 9.0 (`pc-q35-9.0`) |
| BIOS | OVMF, EFI disk `efitype=4m,pre-enrolled-keys=1`; no TPM |
| `ostype` | `win10` |
| CPU / memory | `host`, 2 cores, 4096 MB, balloon 0 |
| SCSI controller | `virtio-scsi-single` |
| `scsi0` | 20 GiB |
| NIC | `e1000` |
| QEMU Guest Agent | enabled |
| `ide2` | tiny10 ISO, cdrom |
| Boot order | `ide2;scsi0` |

Boot behaviour: the ISO boots through `efisys.bin`, so **"Press any key to boot from CD" must be answered on the first boot**, and must not be answered on later reboots. On later reboots OVMF falls through to `scsi0`. If the key is not pressed on the first boot the ISO does not start.

Operator actions in Setup: language page Next, Install now, "I don't have a product key", accept the license, Custom install, and pick the 20 GB disk. The disk is visible without a manual Load driver step. The product-key prompt and license acceptance remain operator actions.

What happens in Audit: Setup plus Audit took about 10 minutes wall time, and Audit itself about 1 minute. QEMU Guest Agent 110.2.3 and Cloudbase-Init 1.1.9 install (exit code 0), `vioscsi` is verified healthy, Sysprep generalize succeeds, and the VM powers itself off. Do not boot the sealed source again.

Sysprep evidence. `Sysprep_succeeded.tag` is present, and `setuperr.log` contains only the known BCD EFI export errors (`c000000d`); the logs are not claimed error-free. `Finalize-Template.ps1` writes `finalization-status.json`, but Sysprep `/shutdown` races its last write, so the file can stay at `SysprepStarting` instead of `SysprepReturned`. The evidence rule in the scripts therefore accepts stage `SysprepReturned` or `SysprepStarting` **and** `Sysprep_succeeded.tag` present **and** `Sysprep_failed.tag` absent. The tag is required in every accepted state.

## Inspect and back up

Offline inspection (read-only; the VM must be stopped):

```bash
scripts/proxmox/offline-inspect.sh <vmid> <dumpdir>/offline-evidence
```

`<outdir>` must not exist yet; its parent must. The script attaches a read-only loop device and mounts the NTFS partition `ro,nosuid,nodev,noexec`. Evidence layout: `inspect.exit` (must contain `0`), `inspect.log`, `guest/Tiny11Template/`, `guest/Sysprep/`, `guest/cloudbase/`, `vioscsi-installed.sha256` and `evidence.sha256`. The measured run passed with exit 0 and an installed `vioscsi.sys` SHA-256 `BE1FD2D12FA178D44CCC40548CC160D695A356566414E88976FC33F9F6C5AFA3`, which matches the pin.

Backup:

```bash
scripts/proxmox/backup.sh <vmid> <dumpdir>
```

`<dumpdir>` must be an absolute path of an existing directory with more than 12 GB free (`/` and the node-global `/var/lib/vz/dump` are refused), and `<dumpdir>/dump` must not exist. `backup.sh` requires the `offline-evidence` directory above (`inspect.exit` equal to `0` and a passing Sysprep evidence check) and a stopped VM. `offline-inspect.sh` also records `vmid.txt` and `scsi0-volume.txt`; before any change `backup.sh` requires `vmid.txt` to equal its VMID argument and the live `scsi0` volume to equal `scsi0-volume.txt`, so evidence from another VM is refused. Exit codes: 2 bad arguments, 3 VM running or too little free space, other non-zero values mean a failed check.

**Warning: `/etc/vzdump.conf` hooks.** A node's `/etc/vzdump.conf` can define a hook script that deletes or uploads archives or removes older backups. The script therefore always passes `--script /bin/true`; it is mandatory. The measured command line was:

```text
vzdump 9210 --mode stop --remove 0 --compress zstd --zstd 1 --bwlimit 65536 --dumpdir /root/t10work/dump --script /bin/true
```

Before any change the script checks the source config, then sets the boot order and checks the config again. Source config after saving: `boot: order=scsi0`, an `ide0` cloud-init drive with `citype configdrive2`, `ide2` empty; no template flag, `ciuser`, `cipassword`, `ipconfig`, `cicustom` or `sshkeys`.

Evidence directory convention: `<dumpdir>/offline-evidence/` (from `offline-inspect.sh`) and `<dumpdir>/backup-<UTC>/` containing `job.log`, `source-before.conf`, `source.conf`, `scsi-before.sha256`, `scsi-after.sha256`, `archive.sha256`, `archive.path`, `exit` and `result.txt` (`BACKUP_PASS`).

Measured result: `zstd -t` OK (7,652,973,056 bytes uncompressed). 20 GiB were transferred in 322 s (63.6 MiB/s); the whole job took 5 min 25 s. The source disk SHA-256 before and after the backup was identical, `065281622ceb0888626334ed47911c18bfd4d20febb6f5c856e17bb39690e5bd`, and the source VM stayed stopped and was never booted after sealing.

## Restore and run

Archive: `vzdump-qemu-9210-2026_11_01-20_53_50.vma.zst`, **3,196,598,863 bytes**.

```text
SHA-256: 466e11da2f768c05226dae202f3a62a0ef9617e361c856e53ae60e2eb9dac8e2
```

```bash
sha256sum vzdump-qemu-9210-2026_11_01-20_53_50.vma.zst
scripts/proxmox/restore-accept.sh <archive.vma.zst> <newvmid> <name> [--storage local-lvm]
# equivalent manual restore:
qmrestore <archive> <newvmid> --storage <storage> --unique 1
```

`restore-accept.sh` refuses a VMID below 9200, an existing VMID, an invalid or longer than 15 character computer name (letters, digits and hyphen only, not all digits, no leading or trailing hyphen) and a corrupt archive, any storage other than `local-lvm`, and less than 40 GiB free on `local-lvm` (exit 3). It restores with `--unique 1`, refuses to continue if the result is a template, renames the VM, and prints the cloud-init commands; it takes and stores no password and never starts the VM. The measured restore took 3 min 35 s.

Set these Cloud-Init options **before the first start** (choose the password yourself):

```bash
qm set <newvmid> --ciuser Administrator --cipassword '<password>' --ipconfig0 ip=<ip>/<prefix>,gw=<gw> --nameserver <dns>
qm start <newvmid>
```

The hostname must be at most 15 characters (the Windows computer-name limit). First boot: in the measured run the QEMU Guest Agent was up at about 33 s and the hostname was applied after one reboot at about 142 s. First boot can reboot while applying the hostname.

## Verification

Acceptance on the restored VM (VMID 9211, name `tiny10-acc-a`) used `scripts/proxmox/acceptance-body.ps1`, run inside the guest through `qm guest exec` with `$AcceptancePassword` set by the caller.

| Check | Result |
| --- | --- |
| OS | Microsoft Windows 10 Pro 10.0.19045 |
| Computer name | `TINY10-ACC-A` |
| SID-500 Administrator | enabled |
| Own credential logon | success |
| Wrong credential | Win32 error 1326 |
| Network | IPv4 10.10.10.192/22, default route 0.0.0.0/0 via 10.10.10.1, DNS 10.10.10.1 |
| Boot disk | disk 0 `QEMU HARDDISK`, BusType SAS, boot/system, Healthy |
| `vioscsi` service | Running / Boot; scsi-boot-driver-observation Healthy |
| Drivers | VirtIO SCSI and Serial 100.93.104.24000 |
| Cloudbase plugin | `tiny10_cloudbase.py` SHA-256 `2F32147F99797493E1192BB46ACB22D80F43288E6EE7B6918C1899882706E6B9`; day-2 state Applied, `access_ready` true (network Static, password Provided) |
| Services | QEMU-GA Running/Auto, cloudbase-init Stopped/Auto, TermService Running/Auto |
| RDP | enabled (`fDenyTSConnections` 0), listener on 3389 (v4 and v6), a custom 3389 rule enabled (the built-in `RemoteDesktop-*` rules were disabled in this first run, see Day-2 acceptance), NLA (`UserAuthenticationRequired` 1, `SecurityLayer` 2) |
| RDP certificate | SHA-256 `5142DA8AC4E49F3CBDE5B6F16B0362797B7700EA3D2B66400C59482D325D59D7`, matches the certificate observed on the wire from the node with openssl |
| Edge | `msedge` 92.0.902.67 in `C:\Program Files (x86)\Microsoft\Edge\Application`; headless screenshot probe Passed (ExitCode 0, 7319-byte PNG), run as SYSTEM via QGA without `--no-sandbox` |
| Disk | WinSxS after Core trim: 225 directories, about 0.3 GB of files; C: 21.35 GB volume with about 10.97 GB free |

Cmdlets verified present on the trimmed image: `Get-NetIPAddress`, `Get-NetRoute`, `Get-DnsClientServerAddress`, `Get-NetTCPConnection`, `Get-NetFirewallRule`, `Get-Disk`, `Get-Partition`, `Get-Volume`, `Get-CimInstance`. The Edge keep patterns needed no adjustment: Edge worked after the first install.

## Day-2 acceptance (restored VM, reboot applies the change)

Run on a VM restored from the archive below (`qmrestore --unique 1`), changing one cloud-init value at a time, regenerating the cloud-init image (`qm set`) and rebooting. A 5 MiB random file, a text file and a registry value written before the first change were unchanged (SHA-256 identical) after every step.

| Step | Change | Result |
| --- | --- | --- |
| 1 | Password | old password rejected, new accepted (`LogonUser`) |
| 2 | IP and DNS | 10.10.10.196 to .197 and DNS 10.10.10.1 to 1.1.1.1 applied; TCP 3389 answers on the new address |
| 3 | Hostname (VM name) | `TINY10-V2` to `TINY10-RENAMED2` |
| 4 | Disk +10 GB (`qm resize`) | disk 20 to 30 GB and C: 19.88 to 29.89 GB (`ExtendVolumesPlugin`), data intact |
| 5 | Reboot with no change | nothing re-applied, all values and data unchanged |

Also verified on the same image: a real RDP login with NLA from the maintainer's Mac (`xfreerdp +auth-only`: wrong password `ERRCONNECT_LOGON_FAILURE`, right password accepted), the first-sign-in "Hi" animation (about 40 s, then the desktop) and Settings > Remote Desktop.

Defect found and fixed: with only the custom 3389 rule, RDP worked but Settings showed Remote Desktop **Off**, because the five built-in `RemoteDesktop-*` firewall rules were disabled. `Configure-GuestPolicy.ps1 -ActivateRdp` now enables and verifies them. The archive delivered with this PR was **not** rebuilt from the ISO: its sealed disk was patched offline with `C:\Windows\Setup\Scripts\SetupComplete.cmd` (`Get-NetFirewallRule -Name 'RemoteDesktop-*' | Enable-NetFirewallRule`), which runs after OOBE on first boot. On the restored VM all five rules are enabled and Settings shows On. A fresh ISO build would get the same result from the script fix instead.

## Delivered artefacts

| Artefact | Size | SHA-256 |
| --- | --- | --- |
| `tiny10-core-proxmox-win10-v1.iso` | 3,169,628,160 bytes | `2CB4BD513AF81B506FD979C3FF2D56B54A3F20D0A059A7D7D2F64D79E137FD67` |
| `vzdump-qemu-9213-2026_11_02-00_44_01.vma.zst` (patched, final) | 3,191,470,207 bytes | `7c13a3f594ec384283aba848e9a7f59f9c92f1b3aaa244ed2c6fb344643d86a8` |
| `vzdump-qemu-9210-2026_11_01-20_53_50.vma.zst` (first archive, superseded) | 3,196,598,863 bytes | `466e11da2f768c05226dae202f3a62a0ef9617e361c856e53ae60e2eb9dac8e2` |

## Limitations

- Not tested: load, disk shrink, a second node, changing several values on a machine that is mid-reboot-loop. The final fix to `Configure-GuestPolicy.ps1` was not re-run through a full ISO build; the C# tests were not re-run after it.
- This is not a live-backup/VSS certification; the backup was taken from a stopped VM. One server only; no second-node test.
- WebView2 is absent from the source media and is not provided (not required).
- Windows 10 22H2 is out of mainstream support (14 Oct 2025). Core cannot be serviced after the build. The media is the May 2023 release (Edge 92 inside); Edge updates itself only if EdgeUpdate can run and the VM is online.
- BCD EFI export errors (`c000000d`) are present in the Sysprep `setuperr.log` as in the Windows 11 evidence; the logs are not claimed error-free.
- The product-key prompt and license acceptance remain operator actions.

## Troubleshooting

- **The ISO does not boot.** Press a key at "Press any key to boot from CD" on the first boot. Do not press it on later reboots: OVMF then falls through to `scsi0`, which is the intended behaviour.
- **DISM version rejected on a Windows 10 host (6.2.x numbers).** The DISM banner reads 10.0.19041.844, but the inbox servicing DLLs (`dismcore.dll`, `DismApi.dll`) carry the legacy file version 6.2.19041.746. The builder normalizes a tool version with Major 6, Minor 2 and Build >= 10240 to `10.0.<Build>.<Revision>` before comparing it with the image; genuine Windows 8/8.1 tools (Build < 10240) are still rejected.
- **Build 19041 versus 19045 metadata.** DISM reports 19041 for the whole 2004/20H2/21H1/21H2/22H2 family. Windows 10 22H2 is an enablement package on the 2004 servicing base, so the guest reports 19045 only after servicing. The builder accepts Client x64 builds 19041 to 19045 and rejects Windows 11 (22000+), older builds, x86/arm64 and Server.
- **`oscdimg.exe` missing.** The builder downloads it from `msdl.microsoft.com` and checks the pinned SHA-256 above; the measured run did this with the operator's approval.
- **`sendme` on Windows needs the Visual C++ runtime.** Verified on the Windows 10 build host: `sendme.exe` 0.36.0 exited with code `0xC0000135` (-1073741515, a missing DLL, `VCRUNTIME140.dll`) until the Microsoft Visual C++ Redistributable x64 was installed; it ran afterwards.
- **`finalization-status.json` shows `SysprepStarting`.** Expected in the measured run (Sysprep `/shutdown` races the last write); see the Sysprep evidence rule above. Check `Sysprep_succeeded.tag`, not the JSON alone.
