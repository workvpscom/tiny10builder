# Bản backup VM Windows 10 tự đóng gói cho Proxmox

[English (default)](proxmox-template.md) | Tiếng Việt

## Scope

tiny10builder build một ISO cài đặt Windows 10 22H2 **Core** có ghim sẵn Cloudbase-Init, QEMU Guest Agent, driver VirtIO storage/serial, giữ lại Microsoft Edge, và tự chạy Audit rồi Sysprep shutdown. Sản phẩm thứ hai là archive `vzdump` của VM đã seal. Cách làm được kế thừa từ bản fork tiny11builder (Windows 11) của tác giả; không cần patch sau khi cài, không sửa node Proxmox, không cần `cicustom`. Các tên nội bộ như thư mục job `tiny11-<guid>`, thư mục staging `tiny11-template`, `C:\ProgramData\Tiny11Template` và chế độ recipe `Tiny11Core` được kế thừa từ bản fork Windows 11 và giữ nguyên để tương thích.

Archive giao ra là một **bản backup VM thông thường**, không phải Proxmox template. Restore và start trực tiếp. Không chạy `qm template`, không clone trong quy trình này, không dùng `cicustom`.

> Core gỡ các thành phần WinSxS và WinRE, đồng thời vô hiệu hoá service và policy của Windows Update và Defender; việc gỡ này không đảo ngược được. Image Core không thể servicing sau khi build. Licensing và activation là trách nhiệm của người vận hành.

## Build the ISO

Build trên máy Windows 10 có .NET 8 SDK và DISM. Lần chạy đã đo dùng VM Windows 10 Pro 19045 (4 vCPU, 8 GB, DISM 10.0.19041.844, .NET SDK 8.0.400).

Media nguồn: `Win10_22H2_English_x64v1.iso` (6,140,975,104 byte), Windows 10 Pro (index 6). DISM báo Version 10.0.19041 / ServicePack Build 2965 (19041.2965); guest đã cài báo 10.0.19045 sau khi servicing.

Các trường recipe đã dùng (recipe đầy đủ và hợp lệ có ở [recipe.proxmox-core.json](recipe.proxmox-core.json); trường JSON `SelectedOptions` phải đúng bằng tập option của Core, và `ImageIndex` phải là index của Windows 10 Pro, tức `6` trên ISO này; builder kiểm tra index Pro trước khi tải bất cứ thứ gì và nêu rõ index đó nếu bạn chọn image khác):

| Trường | Giá trị |
| --- | --- |
| `ImageIndex` | `6` |
| `Flavor` | `Core` |
| `Mode` | `Tiny11Core` |
| `CoreAcknowledged` | `true` |
| `SelectedOptions` | 30 option của `CleanupCatalog.ForMode(Tiny11Core)`, liệt kê trong file ví dụ |
| `RemoveRedundantData` | `true` |
| `TemplateProfile` | `ProxmoxCloudbaseInit` |
| `Compression` | `Recovery` |

Lệnh (tham số `--iso` là thư mục chứa nội dung đã giải nén của ISO):

```text
tiny10 build --recipe recipe.json --iso <folder with ISO contents> --workspace <dir> [--verbose]
```

Exit code: 0 thành công, 1 build lỗi hoặc bị huỷ, 2 lỗi validation/cách dùng.

Builder tải và ghim những gì:

- `oscdimg.exe` được tải từ `msdl.microsoft.com` khi thiếu, ghim SHA-256 `2000160B2C5044691B2F9A0AC308E5207F273D4880A572457AF16D05886BA861`; lần chạy đã đo làm việc này khi có sự đồng ý của người vận hành.
- MSI Cloudbase-Init 1.1.9 và QEMU Guest Agent 110.2.3 được tải và kiểm tra SHA-256. MSI của QEMU Guest Agent không có chữ ký nên hash là kiểm tra duy nhất; chỉ Cloudbase-Init được kiểm tra thêm nhà phát hành Authenticode.
- Driver VirtIO nhúng sẵn (virtio-win 0.1.240, `w10/amd64`) nằm trong mã nguồn. Xem [nguồn gốc driver](virtio-scsi-driver.md).

Thời gian: khoảng 33 phút cho lần chạy hoàn chỉnh đầu tiên. Các bước cuối là dọn component kèm ResetBase, sửa `boot.wim` (`vioscsi` ở mọi index), nén recovery thành `install.esd`, và `oscdimg`.

ISO đầu ra `tiny10-core-proxmox-win10-v1.iso`: 3,169,628,160 byte.

```text
SHA-256: 2CB4BD513AF81B506FD979C3FF2D56B54A3F20D0A059A7D7D2F64D79E137FD67
```

`report.json` ghi SourceBuild 10.0.19041.2965, EdgePreserved true, WebView2Present false. `EdgePreserved` được suy ra từ profile của recipe (ý định), không đo trên image, còn `WebView2Present` chỉ là giá trị giữ chỗ luôn bằng false vì không có gì phát hiện WebView2. Chuyển ISO từ build host sang node bằng sendme 0.36.0 mất 96 giây cho 3 GB; SHA-256 được kiểm tra lại trên node.

## Install and seal the source VM

Tạo VM nguồn bằng `scripts/proxmox/create-source-vm.sh <vmid> <iso-volid> [--storage local-lvm] [--bridge vmbr1]`. Script từ chối VMID dưới 9200 và VMID đã tồn tại, chỉ nhận `--storage local-lvm`, dừng (exit 3) khi `local-lvm` không active, không đọc được hoặc còn dưới 40 GiB trống (thin pool dùng chung với các VM khác), và không bao giờ start VM. Phần cứng được tạo (ví dụ VMID 9210):

| Thiết lập | Giá trị |
| --- | --- |
| Machine | Q35 9.0 (`pc-q35-9.0`) |
| BIOS | OVMF, EFI disk `efitype=4m,pre-enrolled-keys=1`; không TPM |
| `ostype` | `win10` |
| CPU / bộ nhớ | `host`, 2 core, 4096 MB, balloon 0 |
| SCSI controller | `virtio-scsi-single` |
| `scsi0` | 20 GiB |
| NIC | `e1000` |
| QEMU Guest Agent | bật |
| `ide2` | ISO tiny10, cdrom |
| Boot order | `ide2;scsi0` |

Hành vi khi boot: ISO boot qua `efisys.bin`, nên **phải bấm phím khi thấy "Press any key to boot from CD" ở lần boot đầu tiên**, và không được bấm ở các lần reboot sau. Ở các lần reboot sau OVMF chuyển sang `scsi0`. Nếu không bấm phím ở lần boot đầu thì ISO không chạy.

Thao tác của operator trong Setup: trang ngôn ngữ chọn Next, Install now, "I don't have a product key", chấp nhận license, Custom install, chọn đĩa 20 GB. Đĩa hiện ra mà không cần bước Load driver thủ công. Hộp thoại product key và việc chấp nhận license vẫn là thao tác của operator.

Chuyện xảy ra trong Audit: Setup cộng Audit mất khoảng 10 phút thực tế, riêng Audit khoảng 1 phút. QEMU Guest Agent 110.2.3 và Cloudbase-Init 1.1.9 được cài (exit code 0), `vioscsi` được xác nhận healthy, Sysprep generalize thành công và VM tự tắt. Không boot lại VM nguồn đã seal.

Bằng chứng Sysprep. `Sysprep_succeeded.tag` có mặt, và `setuperr.log` chỉ có các lỗi export BCD EFI đã biết (`c000000d`); log không được khẳng định là sạch lỗi. `Finalize-Template.ps1` ghi `finalization-status.json`, nhưng Sysprep `/shutdown` chạy đua với lần ghi cuối của nó, nên file có thể dừng ở `SysprepStarting` thay vì `SysprepReturned`. Vì vậy quy tắc bằng chứng trong các script chấp nhận stage `SysprepReturned` hoặc `SysprepStarting` **và** có `Sysprep_succeeded.tag` **và** không có `Sysprep_failed.tag`. Tag là bắt buộc ở mọi trạng thái được chấp nhận.

## Inspect and back up

Kiểm tra offline (chỉ đọc; VM phải đang tắt):

```bash
scripts/proxmox/offline-inspect.sh <vmid> <dumpdir>/offline-evidence
```

`<outdir>` chưa được tồn tại; thư mục cha của nó phải tồn tại. Script gắn một loop device chỉ đọc và mount phân vùng NTFS với `ro,nosuid,nodev,noexec`. Bố cục bằng chứng: `inspect.exit` (phải chứa `0`), `inspect.log`, `guest/Tiny11Template/`, `guest/Sysprep/`, `guest/cloudbase/`, `vioscsi-installed.sha256` và `evidence.sha256`. Lần chạy đã đo đạt exit 0 và `vioscsi.sys` đã cài có SHA-256 `BE1FD2D12FA178D44CCC40548CC160D695A356566414E88976FC33F9F6C5AFA3`, khớp với pin.

Backup:

```bash
scripts/proxmox/backup.sh <vmid> <dumpdir>
```

`<dumpdir>` phải là đường dẫn tuyệt đối của một thư mục đã tồn tại và còn trống hơn 12 GB (từ chối `/` và thư mục dump toàn node `/var/lib/vz/dump`), còn `<dumpdir>/dump` thì chưa được tồn tại. `backup.sh` yêu cầu thư mục `offline-evidence` ở trên (`inspect.exit` bằng `0` và kiểm tra bằng chứng Sysprep đạt) và VM đang tắt. `offline-inspect.sh` còn ghi `vmid.txt` và `scsi0-volume.txt`; trước mọi thay đổi `backup.sh` yêu cầu `vmid.txt` bằng VMID truyền vào và volume `scsi0` hiện tại bằng `scsi0-volume.txt`, nên bằng chứng của VM khác bị từ chối. Exit code: 2 sai tham số, 3 VM đang chạy hoặc không đủ dung lượng trống, các giá trị khác khác 0 nghĩa là một kiểm tra thất bại.

**Cảnh báo: hook trong `/etc/vzdump.conf`.** `/etc/vzdump.conf` của node có thể khai báo một hook script xoá hoặc upload archive hay xoá các backup cũ. Vì vậy script luôn truyền `--script /bin/true`; đây là bắt buộc. Dòng lệnh đã đo:

```text
vzdump 9210 --mode stop --remove 0 --compress zstd --zstd 1 --bwlimit 65536 --dumpdir /root/t10work/dump --script /bin/true
```

Trước khi thay đổi gì script kiểm tra config nguồn, sau đó đặt boot order và kiểm tra config lần nữa. Config nguồn sau khi lưu: `boot: order=scsi0`, ổ cloud-init `ide0` với `citype configdrive2`, `ide2` trống; không có cờ template, `ciuser`, `cipassword`, `ipconfig`, `cicustom` hay `sshkeys`.

Quy ước thư mục bằng chứng: `<dumpdir>/offline-evidence/` (từ `offline-inspect.sh`) và `<dumpdir>/backup-<UTC>/` chứa `job.log`, `source-before.conf`, `source.conf`, `scsi-before.sha256`, `scsi-after.sha256`, `archive.sha256`, `archive.path`, `exit` và `result.txt` (`BACKUP_PASS`).

Kết quả đã đo: `zstd -t` OK (7,652,973,056 byte sau giải nén). 20 GiB được truyền trong 322 giây (63.6 MiB/s); cả job mất 5 phút 25 giây. SHA-256 đĩa nguồn trước và sau backup giống hệt nhau, `065281622ceb0888626334ed47911c18bfd4d20febb6f5c856e17bb39690e5bd`, và VM nguồn vẫn tắt, không được boot sau khi seal.

## Restore and run

Archive: `vzdump-qemu-9210-2026_11_01-20_53_50.vma.zst`, **3,196,598,863 byte**.

```text
SHA-256: 466e11da2f768c05226dae202f3a62a0ef9617e361c856e53ae60e2eb9dac8e2
```

```bash
sha256sum vzdump-qemu-9210-2026_11_01-20_53_50.vma.zst
scripts/proxmox/restore-accept.sh <archive.vma.zst> <newvmid> <name> [--storage local-lvm]
# restore thủ công tương đương:
qmrestore <archive> <newvmid> --storage <storage> --unique 1
```

`restore-accept.sh` từ chối VMID dưới 9200, VMID đã tồn tại, tên máy không hợp lệ hoặc dài quá 15 ký tự (chỉ chữ, số và dấu gạch ngang, không toàn số, không có gạch ngang ở đầu/cuối) archive hỏng, storage khác `local-lvm`, và `local-lvm` còn dưới 40 GiB trống (exit 3). Script restore với `--unique 1`, dừng nếu kết quả là template, đổi tên VM, rồi in các lệnh cloud-init; script không nhận, không lưu mật khẩu và không bao giờ start VM. Lần restore đã đo mất 3 phút 35 giây.

Đặt các tuỳ chọn Cloud-Init sau **trước lần start đầu tiên** (tự chọn mật khẩu):

```bash
qm set <newvmid> --ciuser Administrator --cipassword '<password>' --ipconfig0 ip=<ip>/<prefix>,gw=<gw> --nameserver <dns>
qm start <newvmid>
```

Hostname tối đa 15 ký tự (giới hạn tên máy của Windows). First boot: trong lần chạy đã đo, QEMU Guest Agent lên khoảng giây thứ 33 và hostname được áp dụng sau một lần reboot vào khoảng giây thứ 142. First boot có thể reboot khi áp dụng hostname.

## Verification

Nghiệm thu trên VM đã restore (VMID 9211, tên `tiny10-acc-a`) dùng `scripts/proxmox/acceptance-body.ps1`, chạy trong guest qua `qm guest exec` với `$AcceptancePassword` do người gọi đặt.

| Kiểm tra | Kết quả |
| --- | --- |
| OS | Microsoft Windows 10 Pro 10.0.19045 |
| Tên máy | `TINY10-ACC-A` |
| Administrator SID-500 | enabled |
| Đăng nhập bằng credential đúng | thành công |
| Credential sai | Win32 error 1326 |
| Mạng | IPv4 10.10.10.192/22, default route 0.0.0.0/0 via 10.10.10.1, DNS 10.10.10.1 |
| Đĩa boot | disk 0 `QEMU HARDDISK`, BusType SAS, boot/system, Healthy |
| Service `vioscsi` | Running / Boot; scsi-boot-driver-observation Healthy |
| Driver | VirtIO SCSI và Serial 100.93.104.24000 |
| Plugin Cloudbase | `tiny10_cloudbase.py` SHA-256 `2F32147F99797493E1192BB46ACB22D80F43288E6EE7B6918C1899882706E6B9`; day-2 state Applied, `access_ready` true (network Static, password Provided) |
| Services | QEMU-GA Running/Auto, cloudbase-init Stopped/Auto, TermService Running/Auto |
| RDP | enabled (`fDenyTSConnections` 0), listener 3389 (v4 và v6), firewall rule enabled, NLA (`UserAuthenticationRequired` 1, `SecurityLayer` 2) |
| Chứng chỉ RDP | SHA-256 `5142DA8AC4E49F3CBDE5B6F16B0362797B7700EA3D2B66400C59482D325D59D7`, khớp với chứng chỉ quan sát được trên đường truyền từ node bằng openssl |
| Edge | `msedge` 92.0.902.67 tại `C:\Program Files (x86)\Microsoft\Edge\Application`; headless screenshot probe Passed (ExitCode 0, PNG 7319 byte), chạy bằng SYSTEM qua QGA, không cần `--no-sandbox` |
| Đĩa | WinSxS sau khi cắt Core: 225 thư mục, khoảng 0.3 GB file; volume C: 21.35 GB, còn trống khoảng 10.97 GB |

Cmdlet đã xác nhận còn trên image đã cắt: `Get-NetIPAddress`, `Get-NetRoute`, `Get-DnsClientServerAddress`, `Get-NetTCPConnection`, `Get-NetFirewallRule`, `Get-Disk`, `Get-Partition`, `Get-Volume`, `Get-CimInstance`. Các Edge keep pattern không cần chỉnh: Edge chạy được ngay sau lần cài đầu.

## Delivered artefacts

| Artefact | Kích thước | SHA-256 |
| --- | --- | --- |
| `tiny10-core-proxmox-win10-v1.iso` | 3,169,628,160 byte | `2CB4BD513AF81B506FD979C3FF2D56B54A3F20D0A059A7D7D2F64D79E137FD67` |
| `vzdump-qemu-9210-2026_11_01-20_53_50.vma.zst` | 3,196,598,863 byte | `466e11da2f768c05226dae202f3a62a0ef9617e361c856e53ae60e2eb9dac8e2` |

## Limitations

- Phiên RDP có xác thực (xfreerdp/mstsc) **chưa** được chạy; chỉ kiểm tra RDP listener, thiết lập NLA và pin chứng chỉ.
- Day-2 reconciliation (đổi hostname/password/IP sau first boot), mở rộng đĩa và load test không được lặp lại.
- Đây không phải chứng nhận live-backup/VSS; backup lấy từ VM đã tắt. Chỉ một server; không test trên node thứ hai.
- WebView2 không có trong media nguồn và không được cung cấp (không bắt buộc).
- Windows 10 22H2 đã hết mainstream support (14/10/2025). Core không thể servicing sau khi build. Media là bản tháng 5/2023 (bên trong Edge 92); Edge chỉ tự cập nhật nếu EdgeUpdate chạy được và VM có online.
- Lỗi export BCD EFI (`c000000d`) có trong `setuperr.log` của Sysprep như trong bằng chứng Windows 11; log không được khẳng định là sạch lỗi.
- Hộp thoại product key và việc chấp nhận license vẫn là thao tác của operator.

## Troubleshooting

- **ISO không boot.** Bấm một phím tại "Press any key to boot from CD" ở lần boot đầu. Không bấm ở các lần reboot sau: OVMF khi đó chuyển sang `scsi0`, đúng như thiết kế.
- **DISM bị từ chối phiên bản trên host Windows 10 (số 6.2.x).** Banner DISM ghi 10.0.19041.844, nhưng các DLL servicing có sẵn (`dismcore.dll`, `DismApi.dll`) mang file version cũ 6.2.19041.746. Builder chuẩn hoá phiên bản tool có Major 6, Minor 2 và Build >= 10240 thành `10.0.<Build>.<Revision>` trước khi so với image; tool Windows 8/8.1 thật (Build < 10240) vẫn bị từ chối.
- **Metadata build 19041 so với 19045.** DISM báo 19041 cho cả họ 2004/20H2/21H1/21H2/22H2. Windows 10 22H2 là enablement package trên nền servicing 2004, nên guest chỉ báo 19045 sau khi servicing. Builder chấp nhận build Client x64 từ 19041 đến 19045 và từ chối Windows 11 (22000+), build cũ hơn, x86/arm64 và Server.
- **Thiếu `oscdimg.exe`.** Builder tải từ `msdl.microsoft.com` và kiểm tra SHA-256 đã ghim ở trên; lần chạy đã đo làm việc này khi có sự đồng ý của người vận hành.
- **`sendme` trên Windows cần Visual C++ runtime.** Đã kiểm chứng trên build host Windows 10: `sendme.exe` 0.36.0 thoát với mã `0xC0000135` (-1073741515, thiếu DLL, `VCRUNTIME140.dll`) cho đến khi cài Microsoft Visual C++ Redistributable x64; sau đó chạy bình thường.
- **`finalization-status.json` hiện `SysprepStarting`.** Đây là kết quả bình thường trong lần chạy đã đo (Sysprep `/shutdown` chạy đua với lần ghi cuối); xem quy tắc bằng chứng Sysprep ở trên. Hãy kiểm tra `Sysprep_succeeded.tag`, đừng chỉ nhìn file JSON.
