# tiny10builder

tiny10builder builds a Windows 10 22H2 **Core** installation ISO and a Proxmox `vzdump` archive of the sealed VM. It is a command-line rewrite derived from the maintainer's tiny11builder (Windows 11) fork. Microsoft Edge is kept; Cloudbase-Init, the QEMU Guest Agent and VirtIO drivers are embedded; the installed VM runs Audit and then Sysprep automatically.

## Outputs

- A Core ISO (`tiny10-core-proxmox-win10-v1.iso` in the measured run).
- A Proxmox `vzdump` archive (`.vma.zst`) of the sealed source VM. It is an ordinary VM backup, not a Proxmox template.

## Requirements

- A Windows 10 build host with the .NET 8 SDK and DISM.
- A Proxmox node for the install, seal, backup and restore steps (`scripts/proxmox/`).
- Your own Windows 10 22H2 media (not bundled).

## Quick start

1. Copy [docs/recipe.proxmox-core.json](docs/recipe.proxmox-core.json) to `recipe.json` (Core, `TemplateProfile` `ProxmoxCloudbaseInit`, `ImageIndex` 6 = Windows 10 Pro on the official ISO; the JSON field `SelectedOptions` must equal the Core option set, so do not edit it) and extract the Windows 10 ISO to a folder.
2. Build the ISO on the Windows host with the CLI below.
3. Upload the ISO to the node and run `scripts/proxmox/create-source-vm.sh`.
4. Install from the ISO; Audit and Sysprep finish by themselves and the VM powers off.
5. Run `scripts/proxmox/offline-inspect.sh`, then `scripts/proxmox/backup.sh`.
6. Restore with `scripts/proxmox/restore-accept.sh`, set cloud-init, then start. Details: [docs/proxmox-template.md](docs/proxmox-template.md).

## CLI

```text
tiny10 build --recipe <file.json> --iso <path> --workspace <dir> [--verbose]
```

## Pinned payloads

- Cloudbase-Init 1.1.9
- QEMU Guest Agent 110.2.3
- virtio-win 0.1.240 `w10` drivers (vioscsi, vioserial); see [docs/virtio-scsi-driver.md](docs/virtio-scsi-driver.md)

## Limitations

Core is irreversible and cannot be serviced after the build. Windows 10 22H2 is out of mainstream support (14 Oct 2025). WebView2 is not provided. The backup was taken from a stopped VM (no live-backup/VSS certification), on one server only, and an authenticated RDP session was not run. See the Limitations section of the full document.

## License and attribution

See [NOTICE.md](NOTICE.md). Windows media and ADK executables are not bundled; Windows use and activation remain subject to Microsoft's terms.

## Documentation

- [Proxmox template guide (English)](docs/proxmox-template.md)
- [Hướng dẫn tiếng Việt](docs/proxmox-template.vi.md)
