import assert from 'node:assert/strict';
import fs from 'node:fs/promises';

const tests = [];
const version = '0.1.1';

test('package, plugin manifest, and project versions stay aligned', async () => {
  const pkg = JSON.parse(await read('package.json'));
  const csproj = await read('src/Jellyfin.Plugin.Ddys/Jellyfin.Plugin.Ddys.csproj');
  const buildYaml = await read('build.yaml');
  assert.equal(pkg.version, version);
  assert.match(csproj, new RegExp(`<Version>${escapeRegExp(version)}</Version>`, 'u'));
  assert.match(csproj, new RegExp(`<AssemblyVersion>${escapeRegExp(version)}\\.0</AssemblyVersion>`, 'u'));
  assert.match(csproj, new RegExp(`<FileVersion>${escapeRegExp(version)}\\.0</FileVersion>`, 'u'));
  assert.match(csproj, /<Deterministic>true<\/Deterministic>/u);
  assert.match(csproj, /<ContinuousIntegrationBuild>true<\/ContinuousIntegrationBuild>/u);
  assert.ok(buildYaml.includes(`version: "${version}.0"`), 'build.yaml version mismatch');
});

test('plugin configuration page and defaults expose Jellyfin settings', async () => {
  const plugin = await read('src/Jellyfin.Plugin.Ddys/Plugin.cs');
  const options = await read('src/Jellyfin.Plugin.Ddys/Configuration/PluginConfiguration.cs');
  const page = await read('src/Jellyfin.Plugin.Ddys/Configuration/configPage.html');
  for (const fragment of [
    'BasePlugin<PluginConfiguration>',
    'IHasWebPages',
    'PluginName',
    'PluginDescription',
    'UpdateConfiguration',
    'DdysClient.ClearCache',
    'configPage.html'
  ]) {
    assert.ok(plugin.includes(fragment), `plugin missing ${fragment}`);
  }
  for (const fragment of [
    'ApiBase',
    'SiteBase',
    'ApiKey',
    'SavedSearches',
    'EnableDirectPlay',
    'ShowExternalResources',
    'IncludeRelatedItems',
    'Normalize()',
    'Clamp(',
    `ddys-jellyfin/${version}`
  ]) {
    assert.ok(options.includes(fragment), `options missing ${fragment}`);
  }
  assert.ok(page.includes('ApiClient.updatePluginConfiguration'), 'config page must save via Jellyfin API');
  assert.ok(page.includes(`ddys-jellyfin/${version}`), 'config page User-Agent fallback mismatch');
});

test('DDYS API client handles endpoints, auth, caching, and media shapes', async () => {
  const client = await read('src/Jellyfin.Plugin.Ddys/Api/DdysClient.cs');
  for (const fragment of [
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
    'ReadMovieList',
    'ReadPagedMovies',
    'ReadSourceGroups',
    'ReadRelated',
    'ReadHeaders',
    'm3u8',
    'webm',
    'mpd'
  ]) {
    assert.ok(client.includes(fragment), `client missing ${fragment}`);
  }
});

test('channel maps DDYS resources to playable Jellyfin media safely', async () => {
  const channel = await read('src/Jellyfin.Plugin.Ddys/Channel/DdysChannel.cs');
  for (const fragment of [
    'IChannel',
    `DataVersion => "${version}"`,
    'GetChannelFeatures',
    'GetChannelItems',
    'ParseSavedSearches',
    'ChannelItemType.Media',
    'MediaSources',
    'Path = resource.Url',
    'RequiredHttpHeaders',
    'SupportsTranscoding = false',
    'ResolveProtocol',
    'GuessContainer',
    'dash',
    'ErrorResult',
    'OperationCanceledException'
  ]) {
    assert.ok(channel.includes(fragment), `channel missing ${fragment}`);
  }
});

test('authenticated controller guards diagnostics and edge inputs', async () => {
  const controller = await read('src/Jellyfin.Plugin.Ddys/Controllers/DdysController.cs');
  for (const fragment of [
    '[Authorize]',
    '[Route("DDYS")]',
    'Status',
    'Search',
    'Movies/{slug}',
    'Cache/Clear',
    'string.IsNullOrWhiteSpace(query)',
    'string.IsNullOrWhiteSpace(slug)',
    'CancellationToken cancellationToken'
  ]) {
    assert.ok(controller.includes(fragment), `controller missing ${fragment}`);
  }
});

test('release workflow and package script are deterministic', async () => {
  const workflow = await read('.github/workflows/build.yml');
  const buildScript = await read('tools/build-package.ps1');
  for (const fragment of [
    'actions/setup-dotnet@v4',
    'dotnet-version: "9.0.x"',
    'node-version: "24"',
    'node tools/check.mjs',
    'node tests/run.mjs',
    'shell: pwsh',
    `ddys-jellyfin-v${version}.zip`,
    `ddys-jellyfin-v${version}.zip.sha256`
  ]) {
    assert.ok(workflow.includes(fragment), `workflow missing ${fragment}`);
  }
  for (const fragment of [
    'DdysZipCrc32',
    '0x04034b50',
    '0x02014b50',
    '0x06054b50',
    '[System.StringComparer]::Ordinal.Compare',
    '[System.IO.File]::WriteAllText',
    '[System.Text.Encoding]::ASCII',
    '[System.Text.UTF8Encoding]::new($false)',
    'dotnet --list-sdks',
    'PathMap',
    'meta.json'
  ]) {
    assert.ok(buildScript.includes(fragment), `build script missing ${fragment}`);
  }
  assert.ok(!buildScript.includes('Compress-Archive'), 'Compress-Archive would make the ZIP timestamp-dependent');
});

test('docs describe release assets and local SDK requirements', async () => {
  const readme = await read('README.md');
  const readmeEn = await read('README.en.md');
  const architecture = await read('docs/architecture.md');
  for (const text of [readme, readmeEn]) {
    assert.ok(text.includes(`ddys-jellyfin-v${version}.zip`), 'release ZIP missing from README');
    assert.ok(text.includes(`ddys-jellyfin-v${version}.zip.sha256`), 'release checksum missing from README');
    assert.ok(text.includes('Get-FileHash'), 'checksum command missing from README');
  }
  assert.ok(readme.includes('.NET SDK 9.x'), 'Chinese README should mention SDK requirement');
  assert.ok(readmeEn.includes('.NET SDK 9.x'), 'English README should mention SDK requirement');
  assert.ok(architecture.includes('确定性 ZIP'), 'architecture doc should describe deterministic ZIP');
  assert.ok(architecture.includes('.sha256'), 'architecture doc should describe checksum asset');
  assert.ok(architecture.includes('meta.json'), 'architecture doc should describe Jellyfin metadata');
});

for (const entry of tests) {
  await entry.fn();
}

console.log(JSON.stringify({ ok: true, package: 'ddys-jellyfin', tests: tests.length }, null, 2));

function test(name, fn) {
  tests.push({ name, fn });
}

function read(relative) {
  return fs.readFile(relative, 'utf8');
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
}
