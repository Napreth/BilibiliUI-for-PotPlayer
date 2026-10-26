# 安装、卸载与验证

项目处于 pre-alpha，支持 Windows 10/11 x64 和 64 位 PotPlayer。

## 安装包

关闭所有 PotPlayer 窗口，运行 `BilibiliBehaviors-版本-win-x64-setup.exe`，选择包含 `PotPlayerMini64.exe` 或 `PotPlayer64.exe` 的现有安装目录。安装包不包含 PotPlayer，会请求写入播放器目录所需的管理员权限。交互设置和备份属于安装时的 Windows 账户，安装与卸载应使用同一账户。

安装程序保存鼠标、快捷键和皮肤选择，备份已有同名皮肤及启动扩展，安装 DLL 并启用 BilibiliLite。首次备份在 `%LOCALAPPDATA%\BilibiliPotPlayer\installation`；升级保留首次备份。不要删除备份。

兼容的原生翻译脚本会保留原文件名并添加译文回传适配，原始字节另行备份；`BilibiliTranslation.dll` 同时安装在翻译脚本目录。卸载恢复匹配的原文件，并保留用户后续更新。引擎、账户和语言仍由原生设置管理，支持范围见[原生字幕翻译](native-subtitle-translation.md)。

原生自定义快捷键会暂时清空。操作映射见项目 README。启动扩展在 `Extension/Media/PlayParse/MediaPlayParse - BilibiliBehaviors.as`，宿主和 DLL 在 `Extension/BilibiliBehaviors/`。没有托盘、自启、服务或计划任务。

第三方许可证和声明统一安装到 `Extension/BilibiliBehaviors/LICENSES/`。FFmpeg、libass 及字体依赖的原始源码、配置和构建脚本在相邻的 `third-party/`。若播放器目录允许当前账户写入，可使用安装参数 `/CURRENTUSER`，将卸载入口登记到当前账户。

从旧版 BilibiliInput 迁移时，先用旧版恢复脚本恢复，或使用安装参数 `/LEGACYBACKUP="原 original-input.json 完整路径"`。迁移导入原始备份并移除旧启动扩展。

## 卸载

关闭 PotPlayer，在 Windows“已安装的应用”中卸载 Bilibili Behaviors for PotPlayer。卸载先恢复原设置、快捷键和皮肤，再删除清单中记录的本项目文件；已有同名资源会恢复。用户的 `caption-options.json` 和原始备份保留。重新安装时重新捕获当前设置，并归档旧备份。

卸载不递归删除 PotPlayer 目录。路径必须位于安装根目录内，不能穿过目录链接。备份或安装清单缺失时保留文件，避免失去恢复依据。

## 开发构建

```powershell
.\build\package.ps1
```

先执行 `git submodule update --init --recursive` 初始化 DanmakuFactory。构建需要 Git、系统 Framework64 C# 编译器、x64 MinGW-w64 GCC、GNU Make、Bash、CMake、Python（含 pip）和 PowerShell。依赖来源、Git 提交和 SHA-256 在 `build/dependencies.lock.json`。首次构建下载固定 FFmpeg、libass、字体及 PCRE2 依赖源码；Meson、Ninja 与 Inno Setup 编译器准备在 `.local/tools`，不修改全局 Python 环境。

FFmpeg 动态库读取容器内文本字幕，无需安装 ffmpeg.exe。构建禁用 GPL、nonfree、version3、外部库和网络；保留容器读取、文本字幕解码及 SubRip 编码。原始源码、许可和配置随安装包提供。

`build/libass.ps1` 构建 libass 共享库，固定选项在 `build/libass-options.json`。使用系统 DirectWrite 选字，FreeType、HarfBuzz 和 FriBidi 静态链接进 libass；无需安装字体引擎或调整 PATH。FreeType 采用 FTL 许可。可以用 ABI 兼容的共享库替换随包 DLL；源码与重建配置随包提供。

`build/danmaku-factory.ps1` 从固定子模块构建转换 DLL，PCRE2 按锁定版本静态构建。`-DependenciesOnly` 准备上游核心和 PCRE2 静态库，`-BridgeOnly` 只编译本项目的桥接 DLL；`build/native.ps1` 使用后者，复用已准备的静态库。修改上游源码后先重新运行依赖构建。安装包保存实际使用的核心源码快照、Git 版本及本地修改标记，不安装上游 CLI 或 GUI。许可证统一放在 `LICENSES/`；源码和构建脚本位于 `third-party/danmaku-factory/`。

`build/out/runtime` 是开发运行目录，`build/out/package` 是安装载荷，`build/out/dist` 保存安装 EXE。构建产物不提交；仓库保留原生 DLL、第三方库与皮肤包。

## 验证

```powershell
.\build\managed.ps1 -Test
.\scripts\dev.ps1 status
.\scripts\dev.ps1 ui-status
.\scripts\test-installation.ps1
```

检查包括输入、布局、元数据、字幕解析、模块依赖、实际 DLL 提取 MKV SRT／ASS 和 MP4 mov_text，以及 libass 的字体附件、运动、变换、透明度、裁剪、卡拉 OK 和开关清屏像素检查。安装检查使用临时播放器目录及隔离注册表，验证首次备份、升级、卸载、二进制快捷键、资源恢复和重新安装。

真实窗口检查使用 `build/out/runtime/BilibiliBehaviors.Tests.exe --window-check`、`--aspect-check`、`--chrome-check` 等。它们会操作测试播放器，应在专用测试环境使用。`--status` 与 `--ui-status` 只读。

## CI

| 触发方式 | 外部依赖 | 本项目与检查 | 安装 EXE 工件 |
| --- | --- | --- | --- |
| 普通 push／pull request | 命中缓存时复用，缺失时编译一次并保存 | 每次编译原生桥接 DLL、C# 和皮肤，验证逻辑、真实 DLL／像素渲染、安装升级卸载 | 不生成 |
| Actions 页面手动 Run workflow | 从源码重新构建并保存新缓存 | 完整检查 | 生成 |
| 推送任意 tag | 从源码重新构建并保存新缓存 | 完整检查 | 生成 |

缓存按 Windows runner、GCC 版本、依赖锁文件、构建脚本／选项及 DanmakuFactory 源码内容区分，不跨依赖版本回退。只保存可复用的 DLL／静态库、导入库、头文件、许可和源码包，不保存带绝对路径的 Meson／CMake 构建目录；恢复前校验全部文件 SHA-256。普通运行仍准备安装载荷并执行安装恢复检查。

外部构建成功后立即保存缓存，后续项目编译或测试失败也不会丢失它。手动／tag 完整构建用独立缓存键刷新同一依赖版本，后续可见该缓存的分支运行使用最新一份。缓存被平台清理时会再次编译。CI 不自动发布 Release。

可用本机安装的 `act` 执行同一份 Windows 工作流：

```powershell
.\scripts\test-ci.ps1
# 再次执行以验证依赖缓存命中；Offline 避免重复拉取已下载的 Actions
.\scripts\test-ci.ps1 -Offline
# 完整构建，或模拟 tag 推送（不会创建真实 tag）
.\scripts\test-ci.ps1 -Event workflow_dispatch -Offline
.\scripts\test-ci.ps1 -Tag local-ci-check -Offline
```

使用 Windows 原生宿主模式，需要 Node.js、Git 及开发构建工具，不需要 Docker；Actions、依赖缓存和安装包工件保存在 `.local/act/`。脚本设置系统模块搜索路径，避免从 PowerShell 7 启动 `act` 时影响 Windows PowerShell 5.1；每次运行使用独立 ID，允许完整构建保存新缓存。脚本不载入本机 `.env`／`.secrets` 等配置，缓存和工件服务仅绑定到本机回环地址。本地执行可验证工作流步骤和分支，GitHub 托管 runner 的操作系统、权限及缓存作用域仍以远端运行为准。

`act` 完整构建将三个工件导出到 `.local/act/artifacts/<run ID>/`，并逐项核对复制后的哈希。GitHub 使用 `actions/upload-artifact` 上传；本地导出避开 `act 0.2.89` 工件服务与新版上传客户端的协议／签名兼容问题，不模拟 GitHub 上传服务。
