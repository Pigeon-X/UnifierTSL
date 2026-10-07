# 个人插件兼容覆盖层

此目录用于放置不直接合并到官方 upstream 的个人维护内容。

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