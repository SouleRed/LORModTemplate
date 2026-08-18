# Library of Ruina MOD Template

《废墟图书馆》MOD 模板工程及其编译所需程序集的统一版本库。

## 目录结构

- `TESTMod v4`：可直接复制或修改的 DLL 模板工程。
- `必要程序集/游戏原版DLL`：模板及其他 MOD 工程引用的游戏程序集。
- `必要程序集/MOD辅助DLL`：Harmony、Publicizer 相关依赖及其他辅助程序集。

这两个目录必须保持并列。`TESTMod v4/TESTMod.csproj` 使用相对路径 `../必要程序集` 查找引用，因此仓库复制到其他位置后仍可直接编译。

## 编译模板

在仓库根目录执行：

```powershell
.\BuildTemplate.ps1
```

默认编译 Release；也可以显式选择配置：

```powershell
.\BuildTemplate.ps1 -Configuration Debug
.\BuildTemplate.ps1 -Configuration Release
```

生成的 Release DLL 位于：

`TESTMod v4/bin/Release/TESTMod.dll`

本仓库没有对应的运行时 MOD 根目录，因此构建脚本只负责编译，不会部署到 Workshop。

## Git LFS

`必要程序集` 下的 DLL 使用 Git LFS 管理。首次克隆前需安装 Git LFS，克隆后可执行：

```powershell
git lfs install
git lfs pull
```

当前正式 Git 工作目录为：

`C:\Users\SouleRed\Desktop\MOD\LibraryOfRuinaModTemplate`

初始化时指定的两个旧目录仍原样保留。后续修改应在本仓库的对应子目录中进行，避免两份副本发生分叉。
