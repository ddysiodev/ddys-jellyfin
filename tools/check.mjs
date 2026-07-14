import { spawnSync } from 'node:child_process';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const version = '0.1.1';
const failures = [];
const forbiddenDirs = new Set(['.git', 'bin', 'obj', 'node_modules', 'package', 'dist', 'build', 'coverage', 'releases']);
const textFilePattern = /\.(cs|csproj|mjs|js|json|md|txt|ps1|ya?ml|xml|html|gitignore|gitattributes)$/iu;
const secretPattern = /ghp_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|npm_[A-Za-z0-9_]+|sk-[A-Za-z0-9_-]+/u;
const mojibakeCodePoints = [
  0xfffd, 0x9428, 0x93be, 0x9363, 0x527c, 0x93c8, 0x7ec0, 0x8f70,
  0x7de5, 0x9353, 0x6ce6, 0x6d63, 0x5ea3, 0x8930, 0x8fab, 0x93c2,
  0x626e, 0x6578, 0x9411, 0x93bc, 0x6ec5, 0x5132, 0x568e, 0x74a7,
  0x52ec, 0x93bb, 0x612c, 0x942e, 0x53a0, 0x6220, 0x6d0f, 0x7ebe,
  0x4f78, 0x5a67, 0x612e, 0x7039, 0x590e, 0x95b0, 0x5d87, 0x7586,
  0x9352, 0x55db, 0x68e3, 0x682d, 0x74d2, 0x546e, 0x7f02, 0x64b3,
  0x935a, 0xe21c, 0x705e, 0x66e0, 0x9422, 0x71b8, 0x95be, 0x70ac,
  0x951b
];
const mojibakePattern = new RegExp(`[${mojibakeCodePoints.map((codePoint) => String.fromCodePoint(codePoint)).join('')}]`, 'u');

const required = [
  '.gitattributes',
  '.github/workflows/build.yml',
  '.gitignore',
  'LICENSE',
  'README.md',
  'README.en.md',
  'build.yaml',
  'docs/architecture.md',
  'package.json',
  'src/Jellyfin.Plugin.Ddys/Jellyfin.Plugin.Ddys.csproj',
  'src/Jellyfin.Plugin.Ddys/Plugin.cs',
  'src/Jellyfin.Plugin.Ddys/ThumbImage.png',
  'src/Jellyfin.Plugin.Ddys/Configuration/PluginConfiguration.cs',
  'src/Jellyfin.Plugin.Ddys/Configuration/configPage.html',
  'src/Jellyfin.Plugin.Ddys/Api/DdysClient.cs',
  'src/Jellyfin.Plugin.Ddys/Api/DdysModels.cs',
  'src/Jellyfin.Plugin.Ddys/Channel/DdysChannel.cs',
  'src/Jellyfin.Plugin.Ddys/Channel/DdysNodeId.cs',
  'src/Jellyfin.Plugin.Ddys/Controllers/DdysController.cs',
  'src/Jellyfin.Plugin.Ddys/Providers/DdysExternalId.cs',
  'tests/run.mjs',
  'tools/build-package.ps1',
  'tools/check.mjs'
];

await checkRequiredFiles();
const files = await listFiles(root);
await checkJavaScriptSyntax(files);
await checkPackage();
await checkWorkflow();
await checkGitAttributes();
await checkBuildYaml();
await checkCsproj();
await checkPlugin();
await checkConfiguration();
await checkClient();
await checkChannel();
await checkController();
await checkDocs();
await checkBuildScript();
await checkTextAndGeneratedFiles(files);
await checkPng();

if (failures.length) {
  console.error(failures.map((failure) => `- ${failure}`).join('\n'));
  process.exit(1);
}

console.log(JSON.stringify({ ok: true, package: 'ddys-jellyfin', version, files: files.length }, null, 2));

async function checkRequiredFiles() {
  for (const file of required) {
    assert(await exists(file), `Missing required file: ${file}`);
  }
}

async function checkJavaScriptSyntax(files) {
  for (const file of files) {
    const relative = rel(file);
    if (!/\.(mjs|js)$/iu.test(relative)) continue;
    const result = spawnSync(process.execPath, ['--check', file], { stdio: 'inherit' });
    assert(result.status === 0, `${relative} failed node --check.`);
  }
}

async function checkPackage() {
  const pkg = JSON.parse(await read('package.json'));
  assert(pkg.name === 'ddys-jellyfin', 'package name mismatch.');
  assert(pkg.version === version, 'package version mismatch.');
  assert(pkg.private === true, 'package must remain private; this is a Jellyfin plugin bundle, not an npm library.');
  assert(pkg.type === 'module', 'package must use ESM.');
  assert(pkg.scripts?.check === 'node tools/check.mjs', 'check script mismatch.');
  assert(pkg.scripts?.test === 'node tests/run.mjs', 'test script mismatch.');
  assert(pkg.scripts?.package?.includes('tools/build-package.ps1'), 'package script mismatch.');
  assert(pkg.engines?.node?.includes('>=20'), 'Node engine must be declared.');
}

async function checkWorkflow() {
  const workflow = await read('.github/workflows/build.yml');
  includesAll(workflow, [
    'actions/checkout@v4',
    'actions/setup-dotnet@v4',
    'dotnet-version: "9.0.x"',
    'actions/setup-node@v4',
    'node-version: "24"',
    'node tools/check.mjs',
    'node tests/run.mjs',
    'tools/build-package.ps1',
    `ddys-jellyfin-v${version}.zip`,
    `ddys-jellyfin-v${version}.zip.sha256`,
    'actions/upload-artifact@v4',
    `name: ddys-jellyfin-v${version}`
  ], 'workflow');
}

async function checkGitAttributes() {
  const gitAttributes = await read('.gitattributes');
  includesAll(gitAttributes, ['* text=auto eol=lf', '*.zip binary', '*.sha256 text eol=lf'], '.gitattributes');
}

async function checkBuildYaml() {
  const buildYaml = await read('build.yaml');
  includesAll(buildYaml, [
    'name: "低端影视 DDYS"',
    'guid: "1bb6d203-7ff2-40c1-a0b6-7f8355120b61"',
    `version: "${version}.0"`,
    'targetAbi: "10.11.0.0"',
    'framework: "net9.0"',
    'artifacts:',
    'Jellyfin.Plugin.Ddys.dll'
  ], 'build.yaml');
}

async function checkCsproj() {
  const csproj = await read('src/Jellyfin.Plugin.Ddys/Jellyfin.Plugin.Ddys.csproj');
  includesAll(csproj, [
    '<TargetFramework>net9.0</TargetFramework>',
    `<Version>${version}</Version>`,
    `<AssemblyVersion>${version}.0</AssemblyVersion>`,
    `<FileVersion>${version}.0</FileVersion>`,
    '<Deterministic>true</Deterministic>',
    '<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>',
    'Jellyfin.Controller',
    'Jellyfin.Model',
    'Version="10.11.11"',
    'EmbeddedResource Include="Configuration\\configPage.html"',
    'EmbeddedResource Include="ThumbImage.png"'
  ], 'csproj');
}

async function checkPlugin() {
  const plugin = await read('src/Jellyfin.Plugin.Ddys/Plugin.cs');
  includesAll(plugin, [
    'BasePlugin<PluginConfiguration>',
    'IHasWebPages',
    'PluginName',
    'PluginDescription',
    'UpdateConfiguration',
    'DdysClient.ClearCache',
    '1bb6d203-7ff2-40c1-a0b6-7f8355120b61',
    'configPage.html'
  ], 'plugin');
}

async function checkConfiguration() {
  const config = await read('src/Jellyfin.Plugin.Ddys/Configuration/PluginConfiguration.cs');
  includesAll(config, [
    'ApiBase',
    'SiteBase',
    'ApiKey',
    'SavedSearches',
    'EnableDirectPlay',
    'ShowExternalResources',
    'IncludeRelatedItems',
    'Normalize()',
    'Clamp(',
    'https://ddys.io/api/v1',
    'https://ddys.io',
    `ddys-jellyfin/${version}`
  ], 'configuration');

  const page = await read('src/Jellyfin.Plugin.Ddys/Configuration/configPage.html');
  includesAll(page, [
    'pluginUniqueId',
    'ApiClient.getPluginConfiguration',
    'ApiClient.updatePluginConfiguration',
    `ddys-jellyfin/${version}`,
    'EnableDirectPlay',
    'ShowExternalResources',
    'IncludeRelatedItems'
  ], 'config page');
}

async function checkClient() {
  const client = await read('src/Jellyfin.Plugin.Ddys/Api/DdysClient.cs');
  includesAll(client, [
    '/latest',
    '/hot',
    '/movies',
    '/search',
    '/sources',
    '/related',
    'Authorization',
    'Bearer',
    'SharedHttpClient',
    'ConcurrentDictionary<string, CacheEntry>',
    'CancellationTokenSource.CreateLinkedTokenSource',
    'IsEnvelopeFailure',
    'm3u8',
    'webm',
    'mpd',
    'Clamp(limit ?? options.HomeLimit',
    'ReadSourceGroups',
    'ReadRelated',
    'ReadHeaders'
  ], 'client');
}

async function checkChannel() {
  const channel = await read('src/Jellyfin.Plugin.Ddys/Channel/DdysChannel.cs');
  includesAll(channel, [
    'IChannel',
    `DataVersion => "${version}"`,
    'GetChannelFeatures',
    'GetChannelItems',
    'ParseSavedSearches',
    'MediaSources',
    'Path = resource.Url',
    'RequiredHttpHeaders',
    'ChannelItemType.Media',
    'query is null',
    'dash',
    'ErrorResult',
    'OperationCanceledException',
    'ProviderIds'
  ], 'channel');
}

async function checkController() {
  const controller = await read('src/Jellyfin.Plugin.Ddys/Controllers/DdysController.cs');
  includesAll(controller, [
    '[Authorize]',
    '[Route("DDYS")]',
    'Status',
    'Search',
    'Movies/{slug}',
    'Cache/Clear',
    'string.IsNullOrWhiteSpace(query)',
    'string.IsNullOrWhiteSpace(slug)',
    'CancellationToken cancellationToken'
  ], 'controller');
}

async function checkDocs() {
  const readme = await read('README.md');
  includesAll(readme, [
    'Jellyfin Server 10.11.x',
    '/DDYS/Search',
    `ddys-jellyfin-v${version}.zip`,
    `ddys-jellyfin-v${version}.zip.sha256`,
    'Get-FileHash',
    '确定性 ZIP',
    'node tools/check.mjs',
    '.NET SDK 9.x'
  ], 'README.md');

  const readmeEn = await read('README.en.md');
  includesAll(readmeEn, [
    'Jellyfin Server 10.11.x',
    'Release assets',
    `ddys-jellyfin-v${version}.zip`,
    `ddys-jellyfin-v${version}.zip.sha256`,
    'SHA-256',
    'deterministic ZIP',
    'Local checks'
  ], 'README.en.md');

  const architecture = await read('docs/architecture.md');
  includesAll(architecture, [
    'IChannel',
    'DDYS API',
    '/latest',
    '/hot',
    '/movies',
    '/search',
    '/sources',
    '/related',
    '.sha256',
    '确定性 ZIP',
    'ordinal',
    'meta.json'
  ], 'architecture doc');
}

async function checkBuildScript() {
  const buildScript = await read('tools/build-package.ps1');
  includesAll(buildScript, [
    'ddys-jellyfin-v{0}.zip',
    'dotnet --list-sdks',
    'dotnet publish',
    'DdysZipCrc32',
    '0x04034b50',
    '0x02014b50',
    '0x06054b50',
    '[System.StringComparer]::Ordinal.Compare',
    'Get-FileHash',
    '[System.IO.File]::WriteAllText',
    '[System.Text.Encoding]::ASCII',
    '[System.Text.UTF8Encoding]::new($false)',
    'Assert-InRoot',
    'PathMap',
    'meta.json',
    'releases',
    'sha256'
  ], 'build script');
  assert(!buildScript.includes('Compress-Archive'), 'build script must not use non-deterministic Compress-Archive.');
  assert(!buildScript.includes('CreateEntryFromFile'), 'build script must not use timestamp-dependent ZipFileExtensions.');
  assert(!buildScript.includes('Set-Content -LiteralPath $ShaFile'), 'checksum writer must not add implicit newlines.');
}

async function checkTextAndGeneratedFiles(files) {
  for (const file of files) {
    const relative = rel(file);
    assert(!/\.(log|tmp|cache|tgz|zip|sha256)$/iu.test(relative), `generated file leaked: ${relative}`);
    assert(!/(^|\/)\.env($|\.)/iu.test(relative), `env file leaked: ${relative}`);
    assert(!['package-lock.json', 'pnpm-lock.yaml', 'yarn.lock'].includes(path.basename(relative)), `lockfile leaked: ${relative}`);

    if (!isTextFile(relative)) continue;
    const text = await fs.readFile(file, 'utf8');
    assert(!secretPattern.test(text), `token-like secret found in ${relative}.`);
    assert(!mojibakePattern.test(text), `mojibake-like text found in ${relative}.`);
    assert(!/\r\n/u.test(text), `${relative} contains CRLF line endings.`);
    if (relative.endsWith('.cs')) assertBalancedBraces(relative, text);
  }
}

async function checkPng() {
  const icon = await fs.readFile(path.join(root, 'src/Jellyfin.Plugin.Ddys/ThumbImage.png'));
  assert(icon.subarray(0, 8).toString('hex') === '89504e470d0a1a0a', 'ThumbImage.png is not a PNG.');
}

async function exists(relative) {
  try {
    await fs.access(path.join(root, relative));
    return true;
  } catch {
    return false;
  }
}

async function read(relative) {
  return fs.readFile(path.join(root, relative), 'utf8');
}

async function listFiles(dir, out = []) {
  for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    const relative = rel(full);
    if (entry.isDirectory()) {
      if (entry.name === '.git') {
        continue;
      }
      if (forbiddenDirs.has(entry.name)) {
        failures.push(`forbidden directory leaked: ${relative}`);
        continue;
      }
      await listFiles(full, out);
    } else {
      out.push(full);
    }
  }
  return out;
}

function includesAll(text, fragments, label) {
  for (const fragment of fragments) {
    assert(text.includes(fragment), `${label} missing ${fragment}`);
  }
}

function assertBalancedBraces(file, text) {
  let depth = 0;
  for (const char of text) {
    if (char === '{') depth += 1;
    if (char === '}') depth -= 1;
    assert(depth >= 0, `${file} has an early closing brace.`);
  }
  assert(depth === 0, `${file} has unbalanced braces.`);
}

function isTextFile(relative) {
  return textFilePattern.test(relative) || relative === 'LICENSE' || relative === '.gitignore' || relative === '.gitattributes';
}

function rel(file) {
  return path.relative(root, file).replaceAll(path.sep, '/');
}

function assert(condition, message) {
  if (!condition) failures.push(message);
}
