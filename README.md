# ddys-jellyfin

低端影视 API 的官方 Jellyfin Server 频道插件。安装后会在 Jellyfin 的频道区域提供 DDYS 浏览、常用搜索入口、影片详情、分组资源和直链播放能力。

## 功能

- Jellyfin Channel：最新更新、热门内容、电影、剧集、动漫、综艺、纪录片。
- 常用搜索词：在插件设置里填写关键词，频道根目录会生成对应搜索入口。
- 影片详情：展示简介、年份、地区、类型、导演、演员、源站链接。
- 播放资源：对 `.m3u8`、`.mp4`、`.m4v`、`.mkv`、`.mov`、`.flv`、`.avi`、`.ts`、`.webm`、`.mpd` 直链创建 Jellyfin 可播放媒体项。
- 外部资源：网盘、磁力、下载页等非直链资源会保留为说明项。
- 配置项：API Base、Site Base、API Key、分页、缓存、超时、User-Agent、直链播放开关。
- 诊断接口：`/DDYS/Status`、`/DDYS/Search`、`/DDYS/Movies/{slug}`、`/DDYS/Cache/Clear`，需登录 Jellyfin 后访问。

## 安装

适配 Jellyfin Server 10.11.x 和对应的官方插件 SDK。

1. 从 GitHub Releases 下载 `ddys-jellyfin-v0.1.1.zip` 和 `ddys-jellyfin-v0.1.1.zip.sha256`。
2. 校验 ZIP：

   ```powershell
   Get-FileHash .\ddys-jellyfin-v0.1.1.zip -Algorithm SHA256
   Get-Content .\ddys-jellyfin-v0.1.1.zip.sha256
   ```

3. 解压后把里面的插件文件放入 Jellyfin Server 的插件目录。
4. 重启 Jellyfin Server。
5. 在 Jellyfin 管理后台进入插件设置，按需填写 API Base、API Key 和常用搜索词。

常见插件目录：

```text
%UserProfile%\AppData\Local\jellyfin\plugins
%ProgramData%\Jellyfin\Server\plugins
```

默认 API Base：

```text
https://ddys.io/api/v1
```

公开读取接口默认不需要 API Key。配置 API Key 后，插件会向 DDYS API 请求附加：

```http
Authorization: Bearer <apiKey>
```

## 搜索说明

Jellyfin 的 Channel 搜索入口不适合直接暴露任意关键词输入，所以插件通过两种方式提供搜索：

- 在插件设置里填写常用搜索词，频道根目录会显示搜索入口。
- 登录后调用 `/DDYS/Search?query=关键词&page=1&perPage=24`。

## Release 资产

每个发布版本提供两份资产：

- `ddys-jellyfin-v0.1.1.zip`：Jellyfin 插件包，包含 `Jellyfin.Plugin.Ddys.dll`、`Jellyfin.Plugin.Ddys.pdb`、`meta.json`、`README.md` 和 `LICENSE`。
- `ddys-jellyfin-v0.1.1.zip.sha256`：ASCII 编码的 SHA-256 校验文件，不包含隐式换行。

确定性 ZIP 由 `tools/build-package.ps1` 生成，采用固定时间戳、固定排序和显式 ZIP 头写入，避免 `Compress-Archive` 的非确定性元数据。GitHub Actions 会在推送和标签发布时执行 `node tools/check.mjs`、`node tests/run.mjs`，再构建并上传 ZIP 与 `.sha256`。

## 本地开发

```powershell
node tools/check.mjs
node tests/run.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-package.ps1
```

构建插件包需要安装 .NET SDK 9.x；只有 .NET Runtime 的环境可以运行静态检查和测试，但不能执行 `dotnet publish`。
