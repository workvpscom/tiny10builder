# tiny10 acceptance body: run inside the restored guest (qm guest exec) with $AcceptancePassword set by the
# caller (prepend `$AcceptancePassword='...';` to the script). Prints one compressed JSON object.
$ErrorActionPreference='Stop'
if([string]::IsNullOrEmpty($AcceptancePassword)){ throw 'AcceptancePassword must be set by the caller' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class NativeLogon {
[DllImport("advapi32.dll", SetLastError=true, CharSet=CharSet.Unicode)] public static extern bool LogonUser(string u,string d,string p,int t,int l,out IntPtr h);
[DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
}
'@
function Test-OwnLogon([string]$p) { $h=[IntPtr]::Zero; $ok=[NativeLogon]::LogonUser('Administrator',$env:COMPUTERNAME,$p,3,0,[ref]$h); $err=[Runtime.InteropServices.Marshal]::GetLastWin32Error(); if($h -ne [IntPtr]::Zero){[void][NativeLogon]::CloseHandle($h)}; @{Success=$ok;Win32Error=$err} }
# Headless Edge screenshot of a small local HTML file. Never throws: a missing or broken Edge is reported, not raised.
function Test-EdgeProbe {
  $edge='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
  $r=[ordered]@{Exists=$false;ExitCode=$null;ScreenshotBytes=0;Passed=$false}
  $dir=$null
  try {
    if(Test-Path -LiteralPath $edge){
      $r.Exists=$true
      $dir=Join-Path $env:TEMP ('edge-probe-'+[Guid]::NewGuid().ToString('N'))
      [void](New-Item -ItemType Directory -Path $dir)
      $html=Join-Path $dir 'probe.html'
      $png=Join-Path $dir 'probe.png'
      Set-Content -LiteralPath $html -Encoding UTF8 -Value '<!doctype html><html><head><meta charset="utf-8"><title>tiny10 edge probe</title></head><body style="background:#fff"><h1>tiny10 edge probe</h1><div style="width:200px;height:100px;background:#0a64c8"></div></body></html>'
      $uri='file:///'+($html -replace '\\','/')
      $edgeArgs=@('--headless','--disable-gpu',('--user-data-dir="'+(Join-Path $dir 'profile')+'"'),('--screenshot="'+$png+'"'),$uri)
      $p=Start-Process -FilePath $edge -ArgumentList $edgeArgs -PassThru -WindowStyle Hidden
      if($p.WaitForExit(60000)){ $r.ExitCode=$p.ExitCode } else { $r.ExitCode=-1; & taskkill.exe /F /T /PID $p.Id | Out-Null }
      if(Test-Path -LiteralPath $png){ $r.ScreenshotBytes=(Get-Item -LiteralPath $png).Length }
      $r.Passed=[bool]((Test-Path -LiteralPath $png) -and $r.ScreenshotBytes -gt 1000)
    }
  } catch { $r.Passed=$false; $r.Error=$_.Exception.Message }
  finally { if($dir){ Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue } }
  $r
}
$rdp=Get-CimInstance -Namespace root/cimv2/TerminalServices -ClassName Win32_TSGeneralSetting -Filter "TerminalName='RDP-tcp'"
$cert=Get-ChildItem Cert:\LocalMachine\'Remote Desktop' | Where-Object Thumbprint -eq $rdp.SSLCertificateSHA1Hash
$sha=[Security.Cryptography.SHA256]::Create()
$result=[ordered]@{
ComputerName=$env:COMPUTERNAME
OS=Get-CimInstance Win32_OperatingSystem | Select-Object Caption,Version,BuildNumber,LastBootUpTime,LocalDateTime
SID500=Get-CimInstance Win32_UserAccount -Filter 'LocalAccount=True' | Where-Object SID -match '-500$' | Select-Object Name,SID,Disabled
OwnCredential=Test-OwnLogon $AcceptancePassword
WrongCredential=Test-OwnLogon ($AcceptancePassword+'Wrong!')
IP=Get-NetIPAddress -AddressFamily IPv4 | Select-Object InterfaceAlias,IPAddress,PrefixLength,AddressState
Routes=Get-NetRoute -AddressFamily IPv4 | Select-Object DestinationPrefix,NextHop,InterfaceAlias,RouteMetric
DNS=Get-DnsClientServerAddress -AddressFamily IPv4 | Select-Object InterfaceAlias,ServerAddresses
Disks=Get-Disk | Select-Object Number,FriendlyName,BusType,Size,IsBoot,IsSystem,HealthStatus,OperationalStatus
CPartition=Get-Partition -DriveLetter C | Select-Object DiskNumber,PartitionNumber,Size
CVolume=Get-Volume -DriveLetter C | Select-Object Size,SizeRemaining,HealthStatus
ScsiDrivers=Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -match 'SCSI|VirtIO' } | Select-Object DeviceName,DriverVersion,InfName,IsSigned
ScsiDevices=Get-CimInstance Win32_PnPEntity | Where-Object { $_.Name -match 'SCSI|VirtIO' } | Select-Object Name,Status,ConfigManagerErrorCode,Service
VioScsi=Get-CimInstance Win32_SystemDriver -Filter "Name='vioscsi'" | Select-Object Name,State,StartMode,PathName
ModuleHash=Get-FileHash 'C:\Program Files\Cloudbase Solutions\Cloudbase-Init\Python\Lib\site-packages\tiny10_cloudbase.py' -Algorithm SHA256
AppliedState=Get-Content 'C:\ProgramData\Tiny11Template\day2-state\state.json' -Raw | ConvertFrom-Json
ScsiObservation=Get-Content 'C:\ProgramData\Tiny11Template\scsi-boot-driver-observation.json' -Raw | ConvertFrom-Json
Services=Get-CimInstance Win32_Service | Where-Object { $_.Name -match 'qemu|cloudbase|TermService' } | Select-Object Name,State,StartMode,PathName
RDPPolicy=Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' | Select-Object fDenyTSConnections
RDPListener=Get-NetTCPConnection -LocalPort 3389 -State Listen -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,State
RDPFirewall=Get-NetFirewallRule -Name 'RemoteDesktop*' | Select-Object Name,@{n='Enabled';e={[string]$_.Enabled}},Direction,Action
RDPCertificateBinding=$rdp | Select-Object SSLCertificateSHA1Hash,SecurityLayer,UserAuthenticationRequired
EdgeProbe=Test-EdgeProbe
RDPCertificateSHA256=if($cert){[BitConverter]::ToString($sha.ComputeHash($cert.RawData)).Replace('-','')}
}
$result | ConvertTo-Json -Depth 12 -Compress
