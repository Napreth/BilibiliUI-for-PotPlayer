// Development-only fixture generation. The product has no Node dependency.
// npm install --prefix .local/dan-any-research/exporter --ignore-scripts
//   @dan-uni/dan-any@2.3.9 @dan-uni/dan-any-ext-ass@2.2.3 base16384@1.0.0
import { createRequire } from 'node:module';
import { mkdirSync, writeFileSync, readFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { dirname, resolve } from 'node:path';
import { gzipSync, brotliCompressSync } from 'node:zlib';
import { createHash } from 'node:crypto';
const here = dirname(fileURLToPath(import.meta.url));
const modules = resolve(here, '../../.local/dan-any-research/exporter/node_modules');
globalThis.require = createRequire(resolve(modules, '../fixture.cjs'));
const moduleAt = path => import(pathToFileURL(resolve(modules, path)).href);
const { UniDB } = await moduleAt('@dan-uni/dan-any/dist/core/main/pure.mjs');
const { DanuniJsonAdapter, DanuniPbTransformer } = await moduleAt('@dan-uni/dan-any/dist/adapters.mjs');
const { AssTransformerLikePluginConfigurator } = await moduleAt('@dan-uni/dan-any-ext-ass/dist/index.js');
const base = await moduleAt('base16384/lib/index.js');
const payload = '[0.25,0.4,"1-1",4,"Static Mode 7 中文",0,0,0.25,0.4,4000,0,true,"Arial",1]';
const records = [
  { progress: 1234, mode: 0, content: 'normal {中文}', color: 0xffffff },
  { progress: 2234, mode: 1, content: 'bottom', color: 0xff0000 },
  { progress: 3234, mode: 2, content: 'top', color: 0xffffff },
  { progress: 4234, mode: 3, content: 'reverse', color: 0xffffff },
  { progress: 5043, mode: 4, content: payload, extra: { bili: { mode: 7, adv: payload } } },
  { progress: 6234, mode: 4, content: 'literal code', extra: { bili: { mode: 8, code: 'not executed' } } },
  { progress: 7234, mode: 4, content: 'literal BAS', extra: { bili: { mode: 9, bas: 'not executed' } } },
].map((record, i) => ({ fontsize: 1, color: 0xffffff, weight: 3, platform: 'bili', ctime: '2026-01-01T00:00:00.000Z', DMID: 'fixture-' + i, ...record }));
const db = await new UniDB().init();
const chunk = await db.import(DanuniJsonAdapter(records));
const output = resolve(here, 'dan-any'); mkdirSync(output, { recursive: true });
const manifest = [];
function save(name, data) {
  writeFileSync(resolve(output, name), data);
  manifest.push({ name, sha256: createHash('sha256').update(data).digest('hex') });
}
const canvas = { measureText: text => ({ width: [...text].length * 25 }) };
const options = { filename: 'synthetic.xml', title: 'Synthetic dan-any interoperability fixture', substyle: { mergeIn: -1 } };
const original = await chunk.plugin(AssTransformerLikePluginConfigurator(canvas, options));
save('v2-zstd-base16384.ass', original);
const gzip = await chunk.plugin(AssTransformerLikePluginConfigurator(canvas, { ...options, raw: { compressType: 'gzip', baseType: 'base64' } }));
save('v2-gzip-base64.ass', gzip);
const body = original.replace(/^;Raw.*(?:\r?\n|$)/gm, '').trimEnd();
const conf = Buffer.from(JSON.stringify({ config: { fontSize: [25,25,36] }, context: { filename: 'synthetic.xml' } }));
const pb = Buffer.from(await chunk.export(DanuniPbTransformer));
save('v2-brotli-base64.ass', `${body}\n;RawCompressType: brotli\n;RawBaseType: base64\n;RawConf: ${brotliCompressSync(conf).toString('base64')}\n;RawPb: ${brotliCompressSync(pb).toString('base64')}\n`);
save('no-raw.ass', body + '\n');
save('removed-event.ass', original.split('\n').filter(line => !line.startsWith('Dialogue:') || !line.includes('normal ｛中文｝')).join('\n'));
const legacy = { config: {}, context: {}, list: records.map(record => ({ extra: { ...record, progress: record.progress / 1000 } })) };
save('v1-gzip-base64.ass', `${body}\n;Raw: ${gzipSync(JSON.stringify(legacy)).toString('base64')}\n`);
const biliy = { config: {}, context: {}, list: [ { time: 2.234, type: 2, fontSizeType: 1, color: { r:255,g:0,b:0 }, content: 'bottom' } ] };
save('biliy-gzip-base64.ass', body.split('\n').filter(line => !line.startsWith('Dialogue:') || line.endsWith('bottom')).join('\n') + `\n;Raw: ${gzipSync(JSON.stringify(biliy)).toString('base64')}\n`);
for (let length = 0; length <= 21; length++) {
  const bytes = Buffer.from(Array.from({ length }, (_, i) => (i * 59 + 17) & 255));
  manifest.push({ base16384: String.fromCharCode(...base.encode(bytes)), hex: bytes.toString('hex') });
}
save('reference.pb', pb);
writeFileSync(resolve(output, 'manifest.json'), JSON.stringify({ generator: '@dan-uni/dan-any-ext-ass@2.2.3', core: '@dan-uni/dan-any@2.3.9', records: records.length, fixtures: manifest }, null, 2) + '\n');
await chunk.delete(); await db.close();
console.log(`Created ${manifest.length} compatibility vectors in ${output}`);
