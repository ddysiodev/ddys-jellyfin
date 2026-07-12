# 架构说明

`ddys-jellyfin` 是独立 Jellyfin Server 插件，不依赖 Emby 插件仓库，也不要求本地运行 PHP 或 Git。

## 模块

- `Plugin.cs`：Jellyfin 插件入口，注册配置页并提供统一配置读取。
- `Configuration/`：Jellyfin 管理后台配置对象和 HTML 配置页。
- `Api/`：DDYS API 客户端、响应兼容解析、缓存、超时、直链判断。
- `Channel/`：Jellyfin Channel 树、分类、常用搜索、详情、播放源映射。
- `Controllers/`：登录态 HTTP API，提供诊断、搜索、详情和清缓存接口。
- `Providers/`：DDYS 外部 ID provider。

## 边界处理

- API Base、Site Base 会去掉末尾 `/`，空值回落到默认地址。
- 首页、分页、超时、缓存时间全部做范围限制。
- 搜索词为空时不访问远端 API，直接返回空结果。
- DDYS 返回非 JSON、HTTP 错误或 envelope failure 时会抛出明确错误；频道侧会转换成可见错误项。
- `sources`、`related` 接口异常会回落为空对象，避免详情页整体不可用。
- `.m3u8`、`.mpd` 会映射为 `hls`、`dash` 容器；网盘、磁力等非直链会保留为说明项。
- 节点 ID 采用 URL-safe Base64，解析异常会回落到根目录，避免坏 ID 打断频道浏览。
