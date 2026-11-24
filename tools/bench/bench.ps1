# Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
# Measures one Windows build: time until its window shows, then memory (private bytes and working
# set of the whole process tree, WebView2/Chromium children included) and CPU while idle.
# Usage: bench.ps1 -Exe <path> -Name <label> [-IdleSeconds 600] [-Out results.json]
param([string]$Exe, [string]$Name, [int]$IdleSeconds = 600, [string]$Out = "bench-$Name.json")
$ErrorActionPreference = 'Stop'

function Get-Tree([int]$rootId) {
  $all = Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name
  $ids = [System.Collections.Generic.HashSet[int]]::new(); [void]$ids.Add($rootId)
  do {
    $added = 0
    foreach ($p in $all) { if ($ids.Contains([int]$p.ParentProcessId) -and $ids.Add([int]$p.ProcessId)) { $added++ } }
  } while ($added -gt 0)
  $ids | ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
}
function Snap([int]$rootId) {
  $t = @(Get-Tree $rootId)
  [pscustomobject]@{
    procs   = $t.Count
    privMB  = [math]::Round((($t | Measure-Object PrivateMemorySize64 -Sum).Sum) / 1MB, 1)
    wsMB    = [math]::Round((($t | Measure-Object WorkingSet64 -Sum).Sum) / 1MB, 1)
    cpuSec  = (($t | Measure-Object TotalProcessorTime -Sum -Property { $_.TotalProcessorTime.TotalSeconds }).Sum)
  }
}

$sw = [Diagnostics.Stopwatch]::StartNew()
$root = Start-Process -FilePath $Exe -PassThru
$shown = $null
while ($sw.Elapsed.TotalSeconds -lt 90) {
  $win = @(Get-Tree $root.Id) | Where-Object { $_.MainWindowHandle -ne 0 -and $_.MainWindowTitle }
  if ($win) { $shown = $sw.Elapsed.TotalSeconds; break }
  Start-Sleep -Milliseconds 100
}
Start-Sleep -Seconds 20                     # let the page finish loading and settle
$start = Snap $root.Id
$samples = @()
$cpu0 = $start.cpuSec; $t0 = Get-Date
for ($i = 0; $i -lt [math]::Floor($IdleSeconds / 30); $i++) {
  Start-Sleep -Seconds 30
  $s = Snap $root.Id
  $samples += [pscustomobject]@{ t = $i * 30 + 30; privMB = $s.privMB; wsMB = $s.wsMB; procs = $s.procs }
}
$end = Snap $root.Id
$idleCpu = [math]::Round(100 * ($end.cpuSec - $cpu0) / ((Get-Date) - $t0).TotalSeconds / [Environment]::ProcessorCount, 2)
$r = [pscustomobject]@{
  name = $Name; windowShownSec = if ($shown) { [math]::Round($shown, 2) } else { $null }
  settledPrivMB = $start.privMB; settledWsMB = $start.wsMB; processes = $start.procs
  endPrivMB = $end.privMB; endWsMB = $end.wsMB; driftPrivMB = [math]::Round($end.privMB - $start.privMB, 1)
  idleCpuPct = $idleCpu; idleSeconds = $IdleSeconds; samples = $samples
}
$r | ConvertTo-Json -Depth 4 | Set-Content $Out
Get-Tree $root.Id | Stop-Process -Force -ErrorAction SilentlyContinue
$r | Select-Object name, windowShownSec, settledPrivMB, settledWsMB, processes, endPrivMB, driftPrivMB, idleCpuPct | Format-List
