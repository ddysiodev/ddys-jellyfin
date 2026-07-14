# 架构说明

`ddys-jellyfin` 是独立 Jellyfin Server 插件，不依赖 Emby 插件仓库，也不要求本地运行 PHP 或 Git。它把 DDYS API 映射成 Jellyfin Channel、可播放媒体项、插件配置页和登录态诊断接口。

## 模块

| 路径 | 说明 |
| --- | --- |
| `src/Jellyfin.Plugin.Ddys/Plugin.cs` | Jellyfin 插件入口，注册配置页并提供统一配置读取。 |
| `src/Jellyfin.Plugin.Ddys/Configuration/` | Jellyfin 管理后台配置对象和 HTML 配置页。 |
| `src/Jellyfin.Plugin.Ddys/Api/` | DDYS API 客户端、响应兼容解析、缓存、超时、直链判断。 |
| `src/Jellyfin.Plugin.Ddys/Channel/` | Jellyfin `IChannel` 树、分类、常用搜索、详情、播放源映射。 |
| `src/Jellyfin.Plugin.Ddys/Controllers/` | 登录态 HTTP API，提供诊断、搜索、详情和清缓存接口。 |
| `src/Jellyfin.Plugin.Ddys/Providers/` | DDYS 外部 ID provider。 |
| `tools/build-package.ps1` | 发布包构建脚本，生成确定性 ZIP 和 `.sha256`。 |
| `.github/workflows/build.yml` | GitHub Actions 发布闸门：检查、测试、打包、上传产物。 |

## API 映射

| Jellyfin 能力 | DDYS API |
| --- | --- |
| 最新更新 | `GET /latest?limit=...` |
| 热门内容 | `GET /hot?limit=...` |
| 分类 | `GET /movies?type=...&page=...&per_page=...` |
| 搜索 | `GET /search?q=...&page=...&per_page=...` |
| 详情 | `GET /movies/{slug}` |
| 资源 | `GET /movies/{slug}/sources` |
| 相关内容 | `GET /movies/{slug}/related` |

## 边界处理

- API Base、Site Base 会去掉末尾 `/`，空值回落到默认地址。
- 首页、分页、超时、缓存时间全部做范围限制。
- 搜索词为空时不访问远端 API，直接返回空结果。
- 详情 slug 为空时控制器直接返回空响应，避免请求空路径。
- DDYS 返回非 JSON、HTTP 错误或 envelope failure 时会抛出明确错误；频道侧会转换成可见错误项。
- `sources`、`related` 接口异常会回落为空对象，避免详情页整体不可用。
- `.m3u8`、`.mpd` 会映射为 `hls`、`dash` 容器；网盘、磁力等非直链会保留为说明项。
- 节点 ID 采用 URL-safe Base64，解析异常会回落到根目录，避免坏 ID 打断频道浏览。
- 资源请求头会映射到 `MediaSourceInfo.RequiredHttpHeaders`，方便需要 Referer/Cookie 的直链。

## 发布策略

发布包版本由 `package.json`、`Jellyfin.Plugin.Ddys.csproj`、`build.yaml` 和 GitHub Actions 统一到 `0.1.1`。ZIP 内容只包含运行需要的插件文件和说明文件：

- `Jellyfin.Plugin.Ddys.dll`
- `Jellyfin.Plugin.Ddys.pdb`
- `LICENSE`
- `README.md`
- `meta.json`

构建脚本不使用 `Compress-Archive`，而是显式写入 ZIP local header、central directory 和 end record；文件名按 ordinal 排序，时间戳固定，校验文件通过 `[System.IO.File]::WriteAllText(..., ASCII)` 写入。这样 CI 产物可以通过远端下载、SHA-256 和 ZIP 条目审计复核。
