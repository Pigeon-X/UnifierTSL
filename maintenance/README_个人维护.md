# UnifierTSL 个人维护说明

本分支用于 Personal / Pigeon-X 的 UnifierTSL 维护。

## 目录

```text
maintenance/
├─ config-translation/
│  ├─ TransferPatch.json
│  └─ Apply-SourceLocalization.ps1
├─ personal-overlay/
│  ├─ PGame-UTSLManager/
│  └─ README.md
└─ tools/
   ├─ Build-Personal.ps1
   └─ Publish-Manager.ps1
```

## 汉化边界

`config-translation` 负责 TShock 配置键和 REST Token 字段的中文映射。

- TShockSettings：146 个配置键
- REST TokenData：Username / UserGroupName
- SSC 配置保持英文，不翻译

## 应用汉化

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\maintenance\config-translation\Apply-SourceLocalization.ps1
```

脚本是幂等的，重复执行不会重复添加属性。

## 个人编译

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\maintenance\tools\Build-Personal.ps1 -Configuration Release
```

编译脚本会先应用汉化，再编译：

```text
src/UnifierTSL/UnifierTSL.csproj
```

同时编译 TSM 风格个人管理器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\maintenance\tools\Build-Personal.ps1 -Configuration Release -BuildManager
```

发布自包含管理器：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\maintenance\tools\Publish-Manager.ps1 -Configuration Release
```

管理器不会修改 UnifierTSL 官方配置，只是按 `manager.json` 启动/停止你配置的
UnifierTSL 实例，并提供 TSM 风格的 3 控制台同屏、路径入口和运行概览。
发布时会默认把本机 `UTSL生存服` 复制到管理器的 `UTSL\` 目录，默认实例直接调用内置的
`UTSL\UnifierTSL.exe`；“全启”按 TSM 逻辑逐个等待端口就绪后启动下一台。

按“UTSL 本体就是管理器”的目录结构发布：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File .\maintenance\tools\Publish-UnifierTSL-UI.ps1 `
  -Configuration Release `
  -CorePackagePath "C:\Users\59934\Saved Games\流光核心源码\_发布\UTSL生存服_远程SQLite" `
  -OutputPath "C:\Users\59934\Saved Games\流光核心源码\_发布\UnifierTSL_UI_20261007"
```

该结构下：

- `UnifierTSL.exe`：TSM 风格 UI 管理器；
- `UnifierTSL.Core.exe`：原 UTSL 核心，底层逻辑不变；
- `manager.json`：默认调用同目录 `UnifierTSL.Core.exe`；
- 默认游戏端口 `2020`，REST `7890`。

## 同步官方上游

本地仓库：

```text
origin   Pigeon-X/UnifierTSL
upstream CedaryCat/UnifierTSL
```

检查更新：

```powershell
cd "C:\Users\59934\Saved Games\流光核心源码\_git"
.\UnifierTSL-同步上游.ps1 -Mode Check
```

更新个人 fork 的 main：

```powershell
.\UnifierTSL-同步上游.ps1 -Mode UpdateMain
```

合并到个人维护分支：

```powershell
.\UnifierTSL-同步上游.ps1 -Mode MergePersonal
```

## 个人插件兼容

插件兼容和私人构建配置放在：

```text
maintenance/personal-overlay/
```

不要把私人插件、数据库、世界文件、日志或 Token 提交到 upstream。
## 仓库内同步工具

```powershell
.\maintenance\tools\Sync-Upstream.ps1 -Mode Check
.\maintenance\tools\Sync-Upstream.ps1 -Mode UpdateMain
.\maintenance\tools\Sync-Upstream.ps1 -Mode MergePersonal
```
