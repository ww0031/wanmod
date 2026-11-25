# 万模 WanMod 3.4

一个给各种游戏用的模组管理器：自动找出电脑里的游戏，在软件里浏览 N网（Nexus Mods）模组并翻译成中文，一键下载、安装、更新。
虚幻引擎游戏支持最完整，其他游戏用通用模式。

## 功能

- **游戏库**：自动扫描 Steam 和 Epic 里装的游戏，带封面（没有封面的会自动从 Steam 商店补一张），最近玩的排在最前，可以搜索；扫描不到的可以选文件夹手动添加。右键可以改名、换封面、从列表移除
- **N网浏览**：在软件里浏览、搜索当前游戏的 N网模组（可以直接输入中文），按点赞、下载量、更新时间或发布时间排序，预览详情后一键安装；不显示 18 禁内容
- **中文翻译**：模组名称、简介、说明和更新日志免费翻译成中文，可随时切回原文。支持腾讯翻译（国内网络直接能用）和 Google 翻译，默认按所在地区自动选择，也可以在设置里指定
- **链接导入**：粘贴 N网模组链接（例如 `https://www.nexusmods.com/phantombladezero/mods/260`）就能导入
- **一键更新**：启动时检查已导入模组的更新，单个或全部一键更新，更新后保留你自己改的名字
- **后台下载**：右下角显示进度，下载完自动安装并删除安装包（带多个选项的模组会留着安装包，方便以后重选）；下载了但没装的，会留在模组页等你安装
- **虚幻引擎游戏**：支持 .pak / .utoc / .ucas 打包模组、蓝图模组、UE4SS Lua 和 DLL 模组；缺少前置组件时提供一键安装
- **其他游戏**：通用模式，下载的模组原样放进你选的文件夹，被替换的游戏文件会先备份，删除模组时还原
- **安全**：每个放进游戏的文件都记录校验和，不会覆盖或删除不是它放进去的文件；下载的内容只解压，从不运行

## 下载

在 [Releases](https://github.com/ww0031/wanmod/releases/latest) 下载，两种任选：

- **安装版（推荐）**：`WanMod-Setup-<版本>.exe`，双击按提示安装。只选一个盘（比如 D 盘）也可以，会自动建好 `D:\WanMod` 文件夹；不需要管理员权限，装完桌面有快捷方式
- **免安装版**：`uemm_phantom-blade-zero_<版本>.zip`（文件名沿用旧名，这样旧版本也能自动更新），解压到可以写入的文件夹后运行 `UEModManager.exe`

需要 [.NET 8 桌面运行时 (x64)](https://dotnet.microsoft.com/download/dotnet/8.0)。

用 N网下载需要在 **设置 → N网** 填写个人 API 密钥。免费账号在模组页面点 **Mod Manager Download** 或 **Slow download**，文件会交给管理器下载。

## 从源码构建

```powershell
pwsh ./publish.ps1
```

先跑全部测试，再生成 `dist\uemm\UEModManager.exe` 和 `dist\release\` 下的压缩包。

## 许可证

GNU 通用公共许可证第 3 版（[LICENSE](LICENSE)），附加条款见 [NOTICE.md](NOTICE.md)。
本软件为修改版，Based on UE MM by KintaroModding and contributors（https://github.com/KintaroModding/uemodmanager）。
