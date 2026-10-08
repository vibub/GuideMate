// Render the SVG master with sharp, then pack PNG frames into a Windows ICO.
// Install sharp in your tooling environment; no application dependency is needed.
// Run: node scripts/Generate-Icon.cjs
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const directory = path.resolve(__dirname, '../assets/branding');
(async () => {
  const source = fs.readFileSync(path.join(directory, 'guidemate.svg'));
  await sharp(source).resize(512, 512).png().toFile(path.join(directory, 'guidemate.png'));
  const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
  const frames = await Promise.all(sizes.map(size => sharp(source, { density: 384 }).resize(size, size).png().toBuffer()));
  const header = Buffer.alloc(6 + 16 * sizes.length);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  frames.forEach((frame, i) => {
    const entry = 6 + 16 * i;
    header[entry] = header[entry + 1] = sizes[i] % 256;
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frame.length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += frame.length;
  });
  fs.writeFileSync(path.join(directory, 'guidemate.ico'), Buffer.concat([header, ...frames]));
  console.log('Generated PNG preview and ICO frames: ' + sizes.join(', '));
})().catch(error => { console.error(error); process.exitCode = 1; });
