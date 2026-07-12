import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

const required = [
  'package.json',
  'README.md',
  'README.en.md',
  'LICENSE',
  'build.yaml',
  'docs/architecture.md',
  '.github/workflows/build.yml',
  'tools/build-package.ps1',
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
  'src/Jellyfin.Plugin.Ddys/Providers/DdysExternalId.cs'
];

const forbiddenDirs = new Set(['.git', 'bin', 'obj', 'node_modules', 'package', 'dist']);

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

async function read(relative) {
  return fs.readFile(path.join(root, relative), 'utf8');
}

async function exists(relative) {
  try {
    await fs.access(path.join(root, relative));
    return true;
  } catch {
    return false;
  }
}

async function listFiles(dir = root, out = []) {
  for (const entry of await fs.readdir(dir, { withFileTypes: true })) {
    if (forbiddenDirs.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) await listFiles(full, out);
    else out.push(full);
  }
  return out;
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

async function main() {
  for (const file of required) {
    assert(await exists(file), `Missing required file: ${file}`);
  }

  const pkg = JSON.parse(await read('package.json'));
  assert(pkg.name === 'ddys-jellyfin', 'package name mismatch.');
  assert(pkg.version === '0.1.0', 'package version mismatch.');

  const csproj = await read('src/Jellyfin.Plugin.Ddys/Jellyfin.Plugin.Ddys.csproj');
  assert(csproj.includes('<TargetFramework>net9.0</TargetFramework>'), 'Jellyfin plugin must target net9.0.');
  assert(csproj.includes('Jellyfin.Controller'), 'Missing Jellyfin.Controller reference.');
  assert(csproj.includes('Jellyfin.Model'), 'Missing Jellyfin.Model reference.');
  assert(csproj.includes('Version="10.11.11"'), 'Jellyfin package version should target 10.11.11.');
  assert(csproj.includes('EmbeddedResource Include="Configuration\\configPage.html"'), 'Config page must be embedded.');
  assert(csproj.includes('EmbeddedResource Include="ThumbImage.png"'), 'Thumb image must be embedded.');

  const plugin = await read('src/Jellyfin.Plugin.Ddys/Plugin.cs');
  for (const fragment of ['BasePlugin<PluginConfiguration>', 'IHasWebPages', 'UpdateConfiguration', 'DdysClient.ClearCache', '1bb6d203-7ff2-40c1-a0b6-7f8355120b61']) {
    assert(plugin.includes(fragment), `Plugin missing ${fragment}.`);
  }

  const channel = await read('src/Jellyfin.Plugin.Ddys/Channel/DdysChannel.cs');
  for (const fragment of ['IChannel', 'GetChannelFeatures', 'GetChannelItems', 'MediaSources', 'RequiredHttpHeaders', 'ChannelItemType.Media', 'query is null', 'dash', 'ErrorResult', 'OperationCanceledException']) {
    assert(channel.includes(fragment), `Channel missing ${fragment}.`);
  }

  const client = await read('src/Jellyfin.Plugin.Ddys/Api/DdysClient.cs');
  for (const fragment of ['/latest', '/hot', '/movies', '/search', '/sources', '/related', 'Authorization', 'Bearer']) {
    assert(client.includes(fragment), `Client missing ${fragment}.`);
  }
  assert(client.includes('SharedHttpClient'), 'Client should reuse HttpClient.');
  assert(client.includes('webm') && client.includes('mpd'), 'Client missing common direct media extensions.');
  assert(client.includes('CreateLinkedTokenSource'), 'Client should link cancellation and timeout tokens.');

  const controller = await read('src/Jellyfin.Plugin.Ddys/Controllers/DdysController.cs');
  for (const fragment of ['[Authorize]', '[Route("DDYS")]', 'Status', 'Search', 'Movies/{slug}', 'Cache/Clear']) {
    assert(controller.includes(fragment), `Controller missing ${fragment}.`);
  }
  assert(controller.includes('string.IsNullOrWhiteSpace(query)'), 'Search controller should handle empty query.');

  const configPage = await read('src/Jellyfin.Plugin.Ddys/Configuration/configPage.html');
  assert(configPage.includes('pluginUniqueId'), 'Config page missing plugin id.');
  assert(configPage.includes('ApiClient.updatePluginConfiguration'), 'Config page must save via Jellyfin API.');

  const buildYaml = await read('build.yaml');
  assert(buildYaml.includes('targetAbi: "10.11.0.0"'), 'build.yaml targetAbi mismatch.');
  assert(buildYaml.includes('framework: "net9.0"'), 'build.yaml framework mismatch.');

  const readme = await read('README.md');
  for (const fragment of ['Jellyfin Server 10.11.x', '常用搜索词', '/DDYS/Search', 'ddys-jellyfin-v0.1.0.zip']) {
    assert(readme.includes(fragment), `README missing ${fragment}.`);
  }
  assert(!readme.includes('## **开发打包**'), 'README contains unwanted developer packaging section.');

  const files = await listFiles();
  for (const file of files) {
    const relative = path.relative(root, file).replaceAll(path.sep, '/');
    assert(!relative.endsWith('.env'), 'Environment files must not be included.');
    if (relative.endsWith('.cs')) assertBalancedBraces(relative, await fs.readFile(file, 'utf8'));
  }

  const allText = (await Promise.all(files.filter((file) => /\.(cs|md|json|ya?ml|ps1|xml|html)$/i.test(file)).map((file) => fs.readFile(file, 'utf8')))).join('\n');
  assert(!/ghp_[A-Za-z0-9_]+/.test(allText), 'GitHub token-like value found.');
  assert(!/npm_[A-Za-z0-9_]+/.test(allText), 'npm token-like value found.');

  const icon = await fs.readFile(path.join(root, 'src/Jellyfin.Plugin.Ddys/ThumbImage.png'));
  assert(icon[0] === 0x89 && icon[1] === 0x50 && icon[2] === 0x4e && icon[3] === 0x47, 'ThumbImage.png is not a PNG.');

  console.log(JSON.stringify({ ok: true, package: 'ddys-jellyfin', files: files.length }, null, 2));
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
