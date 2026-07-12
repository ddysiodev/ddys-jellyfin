# ddys-jellyfin

低端影视 API 的官方 Jellyfin Server 频道插件。安装后会在 Jellyfin 的频道区域提供 DDYS 浏览、常用搜索、详情资源和直链播放能力。

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

1. 从 GitHub Release 下载 `ddys-jellyfin-v0.1.0.zip`。
2. 解压后把里面的插件文件放入 Jellyfin Server 的插件目录。
3. 重启 Jellyfin Server。
4. 在 Jellyfin 管理后台进入插件设置，按需填写 API Base、API Key 和常用搜索词。

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

Jellyfin 频道本身没有全局搜索 UI 的强制入口，所以插件通过两种方式提供搜索能力：

- 在插件设置里填写常用搜索词，频道根目录会显示搜索入口。
- 登录后调用 `/DDYS/Search?query=关键词&page=1&perPage=24`。

## Release 内容

Release ZIP 内包含编译后的 `Jellyfin.Plugin.Ddys.dll`、调试符号、插件元数据和说明文件。源码仓库还提供 GitHub Actions，用于在标签发布时验证和构建插件包。
