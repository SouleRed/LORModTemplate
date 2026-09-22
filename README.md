# Library of Ruina MOD Template

《废墟图书馆》原生 MOD 开发模板，包含 DLL 工程、必要程序集、完整 MOD 文件夹及一键构建部署脚本，不依赖 BaseMod。

## 目录结构

```text
LORModTemplate/
├─ TESTMod v4/                   DLL 源码与 TESTMod.csproj
├─ 必要程序集/
│  ├─ 游戏原版DLL/               编译引用，不打包进 MOD
│  └─ MOD辅助DLL/                Harmony、NAudio 等运行时依赖
├─ MOD根目录/
│  └─ TESTMod/                  完整 MOD 文件夹，可直接改名
│     ├─ StageModInfo.xml        元信息、包 ID、Data 文件清单
│     ├─ Data/                   接待、单位、书页、被动、卡组、掉落、剧情
│     ├─ Resource/               皮肤、卡图、动作音效、剧情图片与音乐
│     └─ Assemblies/
│        ├─ *.dll               部署脚本自动编译/复制
│        ├─ Artwork/            DLL 加载的图片（以文件名作为键）
│        ├─ AB/                 AssetBundle
│        ├─ AudioClips/         WAV / MP3
│        └─ Localize/cn/        中文本地化 XML
├─ BuildTemplate.ps1            只编译
├─ BuildAndDeploy.ps1           编译、打包并部署
└─ BuildAndDeploy.cmd           双击运行的入口
```

DLL 工程与 `必要程序集` 保持并列，工程中的程序集引用使用相对路径。

Data 已提供接待、敌人、双方核心书页、战斗书页、被动、卡组、掉落书、掉落表、书页故事、战斗台词共 11 个有效的空 XML，并在 `StageModInfo.xml` 登记。初始模板没有可玩的接待或实际书页；向相应 XML 添加内容即可。若不使用某种数据，可删除对应的 `InvitationFile` 节点；不要保留指向不存在文件的节点。

`Localize/cn` 包含初始化器支持的十类本地化文件；需要其他语言时复制为 `en`、`jp`、`kr` 等目录。普通本地化只加载当前语言；剧情文本放在 `Data/StoryText/<语言>/`，按当前语言 → 中文 → `StoryText` 根目录回退，配套演出文件放在 `Data/StoryEffect/`。自定义阵型可另加 `Assemblies/Localize/FormationInfo.txt`。

空资源目录用 `.gitkeep` 保留，加载器会忽略相应占位文件，部署副本会排除它们。模板原有的自动补书示例仅在 `TESTMod:114514` 掉落书确实存在时执行。

## 一键构建与部署

环境要求：Windows、PowerShell 7、Visual Studio 2022/2026 或 Build Tools（MSBuild 17.8+、.NET 桌面开发工作负载、.NET Framework 4.8 开发工具）。脚本通过 `vswhere` 自动定位 MSBuild，并还原 `packages.config` 中的 Krafs.Publicizer；首次还原需要能访问 NuGet。

关闭游戏后，双击 `BuildAndDeploy.cmd`，或在仓库根目录执行：

```powershell
.\BuildAndDeploy.ps1
```

脚本默认使用 Release，按以下流程部署：

1. 自动选择 `MOD根目录` 中唯一含 `StageModInfo.xml` 的直接子文件夹。
2. 从 Steam 注册表及 `libraryfolders.vdf` 定位 AppID `1256670` 的 Workshop 目录。优先选择已有同名 MOD 的库，其次游戏安装库；有歧义时报错并提示手动指定。
3. 编译仓库中唯一的 DLL 工程，把实际生成的 DLL 和 Harmony、MonoMod、Mono.Cecil、NAudio 依赖复制到源 MOD 的 `Assemblies`。
4. 校验 XML 与引用路径，暂存整个 MOD 并核对每个文件的 SHA256。
5. 校验通过后删除 Workshop 中的同名旧文件夹，再放入新文件夹，旧文件不会残留；首次部署则创建同名文件夹。

默认映射为：

```text
MOD根目录\TESTMod  →  <Steam库>\steamapps\workshop\content\1256670\TESTMod
```

先查看目标而不构建、复制或部署：

```powershell
.\BuildAndDeploy.ps1 -WhatIf
```

自动定位失败或使用多个 Steam 库时：

```powershell
.\BuildAndDeploy.ps1 -WorkshopRoot 'D:\SteamLibrary\steamapps\workshop\content\1256670'
```

手动路径必须是已存在的 `1256670` 目录。自动定位到游戏安装库时，尚不存在的 Workshop 目录会在正式部署时创建。

如果 `MOD根目录` 中有多个完整 MOD 文件夹，或仓库中有多个 DLL 工程，显式选择：

```powershell
.\BuildAndDeploy.ps1 -ModFolderName '我的新MOD' -ProjectPath 'TESTMod v4\TESTMod.csproj'
.\BuildAndDeploy.ps1 -Configuration Debug
```

同一仓库的多个 MOD 文件夹是供开发者明确选择的源目录，脚本每次只构建并部署选中的一个。应确保所选工程对应所选 MOD 的包 ID。

## 改名与复用

- **部署文件夹名**：直接把 `MOD根目录/TESTMod` 改为所需名称，脚本自动以该名称定位目标，无需修改脚本。仅改文件夹名不会改变包 ID。
- **包 ID**：如果创建独立的新 MOD，统一修改 `StageModInfo.xml` 的 `Workshop/ID`、初始化器的 `packageId`、Harmony ID 和补书代码中的 `PACKAGE_ID` 等 `TESTMod` 标识；显示标题在 `Workshop/Title` 修改。
- **工程与 DLL 名称**：可以修改工程文件夹、`.csproj` 名称及 `AssemblyName`。脚本读取 MSBuild 的实际输出；不硬编码 `TESTMod.dll`。工程与依赖的相对位置仍需有效。

包 ID 和文件夹名可以不同（例如 `MOD根目录/我的接待` 的包 ID 为 `MyMod`）。同名目标若已有不同包 ID，脚本会拒绝覆盖，避免替换另一个 MOD。改文件夹名后不会自动删除旧名称的工坊目录；如旧目录仍有相同包 ID，应将旧目录移出 `1256670`，避免重复加载。

`Assemblies/.template-build.json` 记录脚本生成的主 DLL。以后修改 `AssemblyName` 时，脚本仅移除记录中且哈希未变的旧主 DLL，避免同一个 MOD 同时加载新旧两份程序集。自行添加的 DLL 不参与此清理。

## 部署行为

部署不保留旧版本、不执行自动回滚。删除旧目录前会完成编译、源文件检查和暂存文件的 SHA256 校验。

新版本的暂存目录位于 `<Steam库>/steamapps/workshop/content/.lor-template-deploy/<MOD文件夹名>`，在 `1256670` 之外，不会作为另一个 MOD 加载；成功部署后清理。若部署中途失败，脚本报告错误并保留本次暂存目录，下次执行会提示先检查和处理该目录。旧目录已删除后的失败不会恢复旧文件。

部署以仓库完整 MOD 文件夹为准。工坊目标中独有的资源、旧 DLL 和日志会随整体替换直接删除；应在仓库中维护正式资源。部署不上传创意工坊，也不启动游戏。

## 只编译与验证

```powershell
.\BuildTemplate.ps1
.\BuildTemplate.ps1 -Configuration Debug
```

默认产物为 `TESTMod v4/bin/Release/TESTMod.dll`。只编译不会更新 MOD 文件夹。编译输出及 `MOD根目录/*/Assemblies` 下的 DLL、生成记录不进入 Git；DLL 依赖的版本管理副本在 `必要程序集`。若新增辅助 DLL，需把它纳入依赖源并修改部署脚本的 `$dependencies` 清单，或明确将自带 DLL 纳入版本管理。

验证脚本：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-ModXml.ps1
pwsh -NoProfile -File .\tests\Test-Deployment.ps1
```

前者使用 .NET Framework 和实际游戏数据类型反序列化模板 XML；后者在系统临时目录中复制工程，验证构建、首次部署、整体覆盖、改名及错误拒绝，不修改真实工坊目录。游戏内接待与效果仍需添加实际内容后在游戏中测试。
