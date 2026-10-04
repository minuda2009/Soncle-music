// Soncle · by minuda2009 (https://github.com/minuda2009) · GPL-3.0-or-later
// electron-builder afterPack hook: give Soncle.exe its own icon and name (Explorer, taskbar, Task
// Manager). electron-builder can only do this through Wine on Linux, so it is done here with resedit
// (pure JavaScript), before the portable exe and the zip are made from the unpacked app.
import fs from 'node:fs';
import path from 'node:path';

export default async function afterPack(ctx) {
  if (ctx.electronPlatformName !== 'win32') return;
  const ResEdit = await import('resedit');
  const info = ctx.packager.appInfo;
  const exe = path.join(ctx.appOutDir, `${info.productFilename}.exe`);
  const bin = ResEdit.NtExecutable.from(fs.readFileSync(exe), { ignoreCert: true });
  const res = ResEdit.NtExecutableResource.from(bin);

  const iconFile = ResEdit.Data.IconFile.from(fs.readFileSync(path.join(ctx.packager.projectDir, 'build', 'icon.ico')));
  const groups = ResEdit.Resource.IconGroupEntry.fromEntries(res.entries);
  const id = groups.length ? groups[0].id : 1, lang = groups.length ? groups[0].lang : 1033;
  ResEdit.Resource.IconGroupEntry.replaceIconsForResource(res.entries, id, lang, iconFile.icons.map((i) => i.data));

  const [vi] = ResEdit.Resource.VersionInfo.fromEntries(res.entries);
  const v = info.version.split('.').map(Number);
  vi.setFileVersion(v[0], v[1], v[2], 0, 1033);
  vi.setProductVersion(v[0], v[1], v[2], 0, 1033);
  const strings = {
    ProductName: info.productName, FileDescription: info.productName, CompanyName: 'minuda2009',
    InternalName: info.productFilename, OriginalFilename: `${info.productFilename}.exe`,
    LegalCopyright: `© ${new Date().getFullYear()} minuda2009 · GPL-3.0-or-later`
  };
  for (const lang of vi.getAllLanguagesForStringValues()) vi.setStringValues(lang, strings);
  vi.outputToResourceEntries(res.entries);

  res.outputResource(bin);
  fs.writeFileSync(exe, Buffer.from(bin.generate()));
  console.log(`  • afterPack: set icon and version info on ${path.basename(exe)}`);
}
