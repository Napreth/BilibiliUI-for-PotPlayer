# 模块架构

PotPlayer 的 .as 播放扩展启动 `BilibiliBehaviors.exe --from-player`。薄 EXE 进入 Runtime，业务运行在独立宿主进程；原生 PlayerLayout hook 在播放器 UI 线程处理几何。音视频播放、解码和画面渲染由 PotPlayer 完成，文本字幕和弹幕在宿主内交给 libass 渲染。

| 程序集 | 依赖 | 职责 |
|---|---|---|
| Contracts | 系统库 | 会话接口、媒体状态接口、输入配置和容器字幕轨道 |
| Platform | Contracts | Win32／PotPlayer 适配、DPI、分层窗口与通用绘制 |
| Input | Contracts、Platform | 键鼠 hook、输入状态机、360° 手势 |
| Windowing | Contracts、Platform | 响应尺寸、侧栏意图、比例锁定、原生布局桥接 |
| Media | Contracts、Platform | MediaInfo、元数据补充探测、音视频选轨及界面 |
| Captions | Contracts、Platform | 时间文本、来源、主副选择、弹幕、设置及 FFmpeg／libass 适配调用 |
| Playback | Contracts、Platform、Media | 控制栏、进度、音量、倍速及媒体参数绘制 |
| Runtime | 上述模块 | 模块版本检查、单实例、窗口会话、统一更新和释放 |

程序集完整名称均为 `BilibiliBehaviors.模块名.dll`。构建为 internal 类型生成有限的 InternalsVisibleTo，只开放给引用图中直接依赖它的模块及测试程序集。新增依赖必须修改构建图；不扫描并执行任意 DLL。

`IMediaContext` 使字幕、窗口及相机不再依赖 `StreamControls`。`IInputSession` 把输入与窗口组合分开；`IChromeContext` 由 Runtime 适配字幕／窗口状态，Playback 不引用 Captions 或 Windowing。公共窗口从 SpeedSurface 抽为 Platform 的 LayeredSurface。

宿主统一持有约 15 ms 的更新计时器，每 500 ms 检查同会话播放器；每个 HWND 创建独立控件。后台解析结果经队列和代次检查回到 UI 更新。没有托盘、登录自启、服务或计划任务；最后一个播放器退出后清理输入、控件、字幕偏好及布局 hook。

模块按同一版本发布。当前 .NET Framework 不提供独立程序集热卸载，更新时关闭播放器并重新启动宿主。

## 原生模块

- `PlayerLayout.dll` 保留已有提交前几何和重入保护。
- `BilibiliTranslation.dll` 将原生 AngelScript 翻译扩展返回的有界文字传递给 Captions，校验会话后作为独立字幕行合成。原生设置管理引擎和语言，异步回传不触发弹幕重排；扩展适配和安装恢复见[原生字幕翻译](native-subtitle-translation.md)。
- `BilibiliTimedText.dll` 使用 FFmpeg 公共 API 枚举第 n 条字幕，回传保留样式头和覆盖标签的 ASS 事件给统一分类器，并保留诊断用 SRT 导出。C# 不复制 FFmpeg ABI 结构体。只接受本地文件，并限制提取时长、内容大小和条目数。识别和拆分规则见 [字幕与弹幕自动分类](algorithms/caption-classification.md)。
- 三个 FFmpeg LGPL DLL 独立分发并可替换；精确源码、配置和许可随安装包提供。系统 ffmpeg.exe 不参与运行。
- `BilibiliAss.dll` 使用 libass 公共 API 管理字体、轨道与帧，合成预乘 BGRA 后交给现有透明窗口。`libass-9.dll` 动态分发，字体依赖静态链接于该 DLL；每个播放器的渲染上下文随媒体更换重建、随窗口关闭释放。细节见 [渲染说明](rendering.md)。
- `BilibiliDanmakuFactory.dll` 链接固定子模块的 ASS 写入器及支持代码。托管层解析并校验 Mode 7 参数，转换工作线程通过有界类型接口创建上游弹幕节点，写成 ASS 后释放节点；不启动转换 EXE。CLI、模板与配置加载不参与运行。具体接入和 post-v1 边界见 [弹幕转换](danmaku-conversion.md)。

## 构建与皮肤

`build/managed.ps1` 按引用顺序生成 8 个 DLL、入口和测试 EXE。测试引用实际业务 DLL。`build/package.ps1` 组合原生模块、DLL、DSF 和许可源码，用固定版本 Inno Setup 生成安装包。

皮肤源码由固定 Default.dsf 底包和 9 个覆盖文件组成。构建验证底包哈希，合并后生成包含 47 个文件的完整 DSF。未修改的默认图片和其他布局保留在底包中。
