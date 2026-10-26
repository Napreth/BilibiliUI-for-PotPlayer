# DanmakuFactory 接入

依赖采用 [hihkm/DanmakuFactory](https://github.com/hihkm/DanmakuFactory) Git 子模块，路径为 `third_party/DanmakuFactory`。Gitlink 和 `build/dependencies.lock.json` 固定同一提交；本阶段不修改上游核心功能。

## 当前链路

1. 原有解析器按内容分类，保留原始高级协议及参数。普通弹幕与标准 ASS／SSA 保持原链路。
2. 文件加载工作线程提取 B 站 XML 或约定 JSON 结构中的 Mode 7。逐条检查参数类型、有限数值、时间、文字和字体长度，失败事件单独保留。
3. `BilibiliDanmakuFactory.dll` 的类型接口创建上游弹幕节点，调用原有排序与 ASS 写入器。避免把 JSON 字符串交给上游 CLI 的定长 XML 解析器再次解析，因此文字中的引号、Unicode 和换行不会破坏参数边界。Mode 7 的文字按字面处理，不允许注入 ASS 覆盖标签。
4. 临时输出读回现有 ASS 事件模型，保存来源事件，归类为高级弹幕，按媒体时钟交给 libass。转换完成即释放原生节点并删除临时文件；暂停、跳转和每帧绘制均不重做转换。

高级来源开关和“高级”过滤均适用于转换轨。不支持／失败的高级事件保持灰色，不因同文件有成功事件而被启用；原文件不改写。渲染遵循转换结果的坐标和时间，不套用普通弹幕的速度／区域设置。使用上游当前的 Mode 7 表现能力，未实现的路径等表现不承诺完整还原。

导入现成 ASS 时，DF 生成器样式和 dan-any Raw 元数据作为分类证据，沿用文件已有的 ASS 表现，不再把 Raw 记录重复送入转换器。Raw 中的高级载荷只保留来源信息，不执行代码或补播被生成器省略的事件；兼容范围见[分类说明](algorithms/caption-classification.md)。

转换器编译为 DLL，运行在现有宿主中，不增加 EXE、常驻进程或托盘。只有临时 ASS 输出，不读取 DanmakuFactory 配置、模板、网络内容或执行弹幕代码。适配器也具备普通滚动／固定类型的 ASS 转换接口，目前普通弹幕播放继续保留现有用户设置与排布。

## 构建与后续改造

```powershell
git submodule update --init --recursive
.\build\package.ps1
# 只重建转换器（先完成 libass 构建以准备 Ninja）
.\build\danmaku-factory.ps1
```

构建使用当前子模块核心源码；包内快照与实际参与编译的文件一致，并记录提交、本地修改标记及 SHA-256。PCRE2 为固定版本的静态构建依赖，无额外运行时 DLL。许可证集中在 `LICENSES/`。

本项目的弹幕格式相关内容将不再更新，现有支持范围保留；独立弹幕库仍处于考虑阶段。Mode 8／9 继续在托管入口和原生接口处拒绝，不能通过改分类启用。
