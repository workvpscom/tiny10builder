# Embedded virtio drivers (vioscsi, vioserial)

The Proxmox template profile embeds two Red Hat virtio drivers under `src/Tiny10Builder.Core/Template/Drivers`:

| Driver | Source directory in the virtio-win ISO | Files |
| --- | --- | --- |
| vioscsi (virtio-scsi boot disk) | `vioscsi/w10/amd64` | `vioscsi.inf`, `vioscsi.cat`, `vioscsi.sys` |
| vioserial (virtio-serial, guest agent channel) | `vioserial/w10/amd64` | `vioser.inf`, `vioser.cat`, `vioser.sys` |

## Provenance

- Source ISO: label `virtio-win-0.1.240`, 627,519,488 bytes, SHA-256 `EBD48258668F7F78E026ED276C28A9D19D83E020FFA080AD69910DC86BBCBCC6`.
- Both INFs: `DriverVer = 07/21/2023,100.93.104.24000`.
- INF `[Manufacturer]` decoration is `NTamd64.6.3`, which covers Windows 8.1 and later, including Windows 10 (build 19045).
- The `w10/amd64` files are byte-identical to the files used by the earlier Windows 11 build of this project; no driver was replaced for Windows 10.
- The constants above are recorded once in `VirtioDriverProvenance` (`src/Tiny10Builder.Core/VirtioDriverProvenance.cs`); the file pins are `TemplatePayloadStager.DriverHashes`. `TemplateDriverTests` asserts that the embedded files, the pins and the INF version/decoration agree.

SHA-256 of the embedded files:

| File | SHA-256 |
| --- | --- |
| `vioscsi.cat` | `F484461B7202A840D6F70ABC2EDFD82A01DD44349D234FAED9166D561D199B6A` |
| `vioscsi.inf` | `E49A966F7313DA4F6130DB00D84B57A167ADA0DE3D877C7CC584A7AED99C5BA2` |
| `vioscsi.sys` | `BE1FD2D12FA178D44CCC40548CC160D695A356566414E88976FC33F9F6C5AFA3` |
| `vioser.cat` | `1E93A77AA320F515E6998DA233FA6F1B0E1DA9409EB7AACEDCFD4D21DF255E4D` |
| `vioser.inf` | `4A8FCFDEEDBF05BF0B02535ACE5156D5DB5F1C229D30532F88AE477ADF82BEED` |
| `vioser.sys` | `B6767019C660903B59A3BB8396C40EDA2DE3CDA51CCC121B229050AE28B619FF` |

## Catalog signature

`Get-AuthenticodeSignature` on the `w10/amd64` `vioscsi.cat` and `vioser.cat` (build host 9200, Windows 10 Pro 19045):

- Status: `Valid` (both)
- Signer thumbprint: `FAC666005546D6BE881A31C1267717879401A950` (both), matching the pin in `Template-State.ps1`, `Prepare-Template.ps1` and `TemplatePayloadStager.cs`
- Signer subject: `CN=Microsoft Windows Hardware Compatibility Publisher, O=Microsoft Corporation, L=Redmond, S=Washington, C=US`

## Upstream license (vioscsi-LICENSE.txt)

    Copyright 2009-2022 Red Hat, Inc. and/or its affiliates.
    Copyright 2016 Google, Inc.
    Copyright 2016 Virtuozzo, Inc.
    Copyright 2007 IBM Corporation
    
    Redistribution and use in source and binary forms, with or without
    modification, are permitted provided that the following conditions
    are met:
    
    Redistributions of source code must retain the above copyright
    notice, this list of conditions and the following disclaimer.
    
    Redistributions in binary form must reproduce the above copyright
    notice, this list of conditions and the following disclaimer in the
    documentation and/or other materials provided with the distribution.
    
    Neither the name of the copyright holder nor the names of its
    contributors may be used to endorse or promote products derived from
    this software without specific prior written permission.
    
    THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
    "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
    LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
    A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
    HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
    SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
    LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
    DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
    THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
    (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
    OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
