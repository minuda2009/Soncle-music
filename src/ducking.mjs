// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// Smart ducking: notice when another app (a YouTube video in the browser, a WhatsApp voice note,
// a call…) starts making sound, and tell the renderer to lower (or pause) the music until it stops.
//
// Windows only. A small PowerShell process compiles a C# probe that walks the default output
// device's audio sessions (Core Audio: IAudioSessionManager2 / IAudioMeterInformation) twice a
// second and prints "pid:peak" for every session that is actually producing sound. It only runs
// while music is playing, so it costs nothing when idle.
import { spawn } from 'node:child_process';

const CS = String.raw`
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace SoncleDuck {
  [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo {}
  [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMMDeviceEnumerator {
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int mask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
  }
  [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IMMDevice {
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
  }
  [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioSessionManager2 {
    [PreserveSig] int GetAudioSessionControl(IntPtr a, int b, out IntPtr c);
    [PreserveSig] int GetSimpleAudioVolume(IntPtr a, int b, out IntPtr c);
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
  }
  [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioSessionEnumerator {
    [PreserveSig] int GetCount(out int count);
    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
  }
  [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioSessionControl2 {
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName(out IntPtr name);
    [PreserveSig] int SetDisplayName(IntPtr a, IntPtr b);
    [PreserveSig] int GetIconPath(out IntPtr a);
    [PreserveSig] int SetIconPath(IntPtr a, IntPtr b);
    [PreserveSig] int GetGroupingParam(out Guid a);
    [PreserveSig] int SetGroupingParam(IntPtr a, IntPtr b);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr a);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr a);
    [PreserveSig] int GetSessionIdentifier(out IntPtr a);
    [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr a);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
  }
  [Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IAudioMeterInformation {
    [PreserveSig] int GetPeakValue(out float peak);
  }
  public static class Probe {
    public static string Scan() {
      var sb = new StringBuilder();
      try {
        var en = (IMMDeviceEnumerator)(new MMDeviceEnumeratorCo());
        IMMDevice dev;
        if (en.GetDefaultAudioEndpoint(0, 1, out dev) != 0 || dev == null) return "";
        Guid iid = typeof(IAudioSessionManager2).GUID;
        object o;
        if (dev.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return "";
        var mgr = (IAudioSessionManager2)o;
        IAudioSessionEnumerator se;
        if (mgr.GetSessionEnumerator(out se) != 0) return "";
        int n; se.GetCount(out n);
        for (int i = 0; i < n; i++) {
          IAudioSessionControl2 s;
          if (se.GetSession(i, out s) != 0 || s == null) continue;
          int state; s.GetState(out state);
          if (state != 1) continue;                       // 1 = active
          if (s.IsSystemSoundsSession() == 0) continue;   // S_OK = system sounds: ignore dings
          uint pid; s.GetProcessId(out pid);
          float peak = 0; try { ((IAudioMeterInformation)s).GetPeakValue(out peak); } catch {}
          sb.Append(pid).Append(':').Append(peak.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        }
      } catch (Exception) { }
      return sb.ToString();
    }
  }
}`;

const PS = `
$ErrorActionPreference = 'SilentlyContinue'
Add-Type -TypeDefinition @'
${CS}
'@
[Console]::Out.WriteLine('ready')
while ($true) {
  [Console]::Out.WriteLine([SoncleDuck.Probe]::Scan())
  [Console]::Out.Flush()
  Start-Sleep -Milliseconds 450
}`;

/**
 * @param {object} o
 * @param {() => number[]} o.ownPids   pids of this app (their sound never ducks us)
 * @param {(state: {active: boolean, pids: number[]}) => void} o.onChange
 * @param {(msg: string) => void} [o.log]
 */
export function createDucker({ ownPids, onChange, log = () => {} }) {
  let proc = null, buf = '', active = false, since = 0, quietSince = 0, failed = 0, stopTimer = null;
  const THRESH = 0.012, ON_AFTER = 350, OFF_AFTER = 1400;
  function handle(line) {
    if (line === 'ready') { log('ducking: watching other apps'); return; }
    const mine = new Set(ownPids());
    const loud = [];
    for (const part of line.split(';')) {
      if (!part) continue;
      const [pid, peak] = part.split(':');
      const p = Number(pid);
      if (!p || mine.has(p)) continue;
      if (Number(peak) > THRESH) loud.push(p);
    }
    const now = Date.now();
    if (loud.length) {
      quietSince = 0;
      if (!since) since = now;
      if (!active && now - since >= ON_AFTER) { active = true; onChange({ active: true, pids: loud }); }
    } else {
      since = 0;
      if (!quietSince) quietSince = now;
      if (active && now - quietSince >= OFF_AFTER) { active = false; onChange({ active: false, pids: [] }); }
    }
  }
  function start() {
    clearTimeout(stopTimer);
    if (proc || process.platform !== 'win32' || failed > 2) return;
    try {
      const enc = Buffer.from(PS, 'utf16le').toString('base64');
      proc = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-EncodedCommand', enc], { windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
      proc.stdout.setEncoding('utf8');
      proc.stdout.on('data', (d) => { buf += d; let i; while ((i = buf.indexOf('\n')) >= 0) { const line = buf.slice(0, i).trim(); buf = buf.slice(i + 1); handle(line); } });
      proc.stderr.on('data', (d) => log('ducking: ' + String(d).trim().slice(0, 200)));
      proc.on('exit', (code) => { if (proc) { failed++; log('ducking helper exited ' + code); } proc = null; if (active) { active = false; onChange({ active: false, pids: [] }); } });
    } catch (e) { failed++; log('ducking unavailable: ' + e.message); proc = null; }
  }
  // stop a little after playback stops, so a quick pause/resume doesn't respawn the helper
  function stop(delay = 45000) {
    clearTimeout(stopTimer);
    stopTimer = setTimeout(() => { const p = proc; proc = null; try { p?.kill(); } catch {} if (active) { active = false; onChange({ active: false, pids: [] }); } }, delay);
  }
  return { start, stop, get active() { return active; } };
}
