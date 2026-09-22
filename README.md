# Library of Ruina MOD Template

《废墟图书馆》原生 MOD 模板，包含 DLL 工程、完整 MOD 文件夹和一键部署脚本，不依赖 BaseMod。

## 目录结构

- `TESTMod v4`：DLL 源码与 `TESTMod.csproj`。
- `必要程序集/游戏原版DLL`：编译引用，不放入 MOD。
- `必要程序集/MOD辅助DLL`：辅助程序集的引用来源。
- `MOD根目录/TESTMod`：完整 MOD，包含 `StageModInfo.xml`、`Data`、`Resource` 和 `Assemblies`。
- `BuildAndDeploy.ps1`：按 SouleArcaeaMod 的流程构建并部署。
- `BuildAndDeploy.cmd`：双击运行入口。

DLL 工程与 `必要程序集` 保持并列，以保证工程的相对引用路径有效。MOD 中已提供 11 个空 Data XML、10 个中文本地化 XML 和资源目录；初始模板尚无可玩的接待。空目录用 `.gitkeep` 保留。

`Assemblies` 中保留 Harmony、MonoMod、Mono.Cecil 和 NAudio 的运行时依赖，并纳入版本管理；生成的 `TESTMod.dll` 不进入 Git。新增运行时依赖时，将 DLL 放入完整 MOD 文件夹的 `Assemblies`，部署会随整包复制。

## 一键部署

环境需要 PowerShell 7、.NET SDK 及 .NET Framework 4.8 开发工具。首次使用时在 Visual Studio 中还原 NuGet 包，确保 `TESTMod v4/packages/Krafs.Publicizer.2.3.1` 存在；部署脚本与参考版本一样使用 `dotnet msbuild -restore`，不再提供独立的构建脚本或额外的 NuGet 还原流程。

关闭游戏后双击 `BuildAndDeploy.cmd`，或执行：

```powershell
.\BuildAndDeploy.ps1
```

脚本沿用参考版本的五步流程和提示：

1. 编译 Release DLL。
2. 将 DLL 复制到仓库 MOD 文件夹的 `Assemblies`，校验 SHA256。
3. 复制完整 MOD 到工坊中的临时目录，校验 DLL 和清单是否存在。
4. 整体替换同名工坊目录，并校验最终 DLL。
5. 删除本次临时备份。

目标文件夹名称自动取自 `MOD根目录` 中唯一含 `StageModInfo.xml` 的直接子文件夹。例如，将 `TESTMod` 改名为 `我的接待` 后，部署目标自动变为 `<Steam库>/steamapps/workshop/content/1256670/我的接待`。旧目录由脚本自动替换，无须手动删除；目标中独有的旧文件也不会残留。

自动定位 Steam 库存在歧义时，可指定工坊根目录：

```powershell
.\BuildAndDeploy.ps1 -WorkshopRoot 'D:\SteamLibrary\steamapps\workshop\content\1256670'
```

脚本只接受 `-WorkshopRoot`，不提供 `-WhatIf`、`-Configuration`、`-ProjectPath` 或 `-ModFolderName`。请在 `MOD根目录` 中只保留一个用于部署的完整 MOD 文件夹。

## 改名与替换行为

仅修改 MOD 文件夹名，无须修改脚本。包 ID、工程名和程序集名是独立设置；更换编译目标时，按下节说明修改相应配置。只有包 ID 改变时才需要同步修改清单中的 ID、C# 中的包标识及脚本的包 ID 校验值。

与参考脚本一致，替换过程中使用 `<文件夹名>.__deploy_staging` 和 `<文件夹名>.__deploy_backup` 两个临时目录。成功后立即删除临时备份，不保留历史版本；目录切换失败时尝试还原旧目录。若检测到上次遗留的备份，脚本报错提示人工处理；遗留的暂存目录则自动清理后重新复制。

部署以仓库完整 MOD 文件夹为准，不检查现有同名目标的包 ID。清单校验针对源 MOD 的 `<ID>TESTMod</ID>`，与外层文件夹名称无关。改名部署不会删除旧名称的工坊目录。

## 默认编译目标与修改方法

`BuildAndDeploy.ps1` 不会自动搜索工程，默认编译目标和产物路径固定写在脚本中，均相对于仓库根目录：

```powershell
$projectPath = Join-Path $repoRoot 'TESTMod v4\TESTMod.csproj'
$dllSource = Join-Path $repoRoot 'TESTMod v4\bin\Release\TESTMod.dll'
```

默认编译配置为 `Release / AnyCPU`。DLL 名称由 `.csproj` 中的 `<AssemblyName>TESTMod</AssemblyName>` 决定，输出目录由对应配置的 `<OutputPath>` 决定；仅修改 `.csproj` 文件名不会自动改变 DLL 名称。

更换编译目标时，根据实际改动同步修改以下位置。行号对应当前版本，后续编辑后以变量名或代码内容定位为准。

| 改动 | 需要同步修改的位置 |
|---|---|
| 工程文件夹或 `.csproj` 改名 | 脚本第 9 行 `$projectPath`；工程文件夹改变且仍使用相对输出目录时，也调整第 21 行 `$dllSource` |
| 仅 DLL 输出目录改变（文件名不变） | 调整脚本第 21 行 `$dllSource` 中的目录部分，使其与工程实际输出目录一致 |
| DLL 名称改变 | 统一修改脚本第 21 行 `$dllSource`、第 129 行 `$modDll`、第 152 行 `$stagingDll`、第 181 行 `$deployedDll` 中的 DLL 文件名，以及第 153 行的缺失文件提示 |
| MOD 包 ID 改变 | 脚本第 114 行的 `<ID>TESTMod</ID>` 校验，同时统一修改 `StageModInfo.xml` 的 `Workshop/ID` 和 C# 中的相关包标识 |
| 编译配置或平台改变 | 脚本第 122、123 行的 `Release`、`AnyCPU`，并对应调整 DLL 输出路径和相关提示；工程本身也应支持所选配置 |
| 只修改 `MOD根目录` 下的完整 MOD 文件夹名称 | 不用修改脚本，部署目标会自动跟随该文件夹名称 |

修改 DLL 名称后，还需要移除源 MOD 的 `Assemblies` 中旧名称的主 DLL，避免新旧程序集一起部署，并把 `.gitignore` 中的 `/MOD根目录/*/Assemblies/TESTMod.dll` 改为新的主 DLL 名称。辅助依赖 DLL 应继续纳入版本管理。

如果移动了工程文件夹，检查 `.csproj` 中 `RequiredGameAssembliesDir` 和 `RequiredModAssembliesDir` 的相对路径，确保仍能找到 `必要程序集`。自述文件中的编译示例和 `tests` 中固定引用模板名称的测试路径，也应按新工程名称同步调整。

## 仅编译及验证

需要只编译时直接执行：

```powershell
dotnet msbuild '.\TESTMod v4\TESTMod.csproj' -t:Build -p:Configuration=Release -p:Platform=AnyCPU -restore
```

验证命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Test-ModXml.ps1
pwsh -NoProfile -File .\tests\Test-Deployment.ps1
```

XML 验证使用游戏的实际数据类型。部署测试仅操作系统临时目录中的工程副本，检查编译、覆盖、按文件夹命名、暂存清理与切换失败还原，不修改真实工坊。
