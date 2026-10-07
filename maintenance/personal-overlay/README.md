# 个人插件兼容覆盖层

此目录用于放置不直接合并到官方 upstream 的个人维护内容。

其中 `Pigeon.UnifierTSL.Manager` 是个人版 TSM 风格管理器：

- 沿用 PGame-TSManager 的深色/浅色玻璃 UI 风格；
- 支持多个 UnifierTSL 发布目录；
- 支持启动、停止、全部启动、全部停止；
- 支持控制台输出、stdin 指令和运行概览；
- 支持 3 个 UnifierTSL 实例控制台同屏显示和独立启停；
- 支持打开服务器目录、`config/config.json`、`plugins` 和日志目录；
- `manager.json` 只描述管理器实例，不改官方 UnifierTSL 配置。

建议目录：

```text
personal-overlay/
├─ plugin-overrides/     单个插件的源码兼容补丁
├─ config-templates/     个人服默认配置模板
├─ scripts/              构建前后处理脚本
└─ manifests/            插件清单和版本锁定
```

规则：

- 官方 upstream 文件保持可合并。
- 个人改动优先放到 overlay 或 maintenance。
- 插件兼容先编译、再和 Dimensions/TsWeb.Sync 做联合加载测试。
- 不把数据库、日志、世界文件和私人配置提交到 Git。
