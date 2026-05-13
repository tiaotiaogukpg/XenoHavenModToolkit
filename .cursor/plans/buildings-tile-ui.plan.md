# XenoHaven MOD Toolkit：左栏资源树 + Buildings 磁贴主区（统一计划）

## 1. 概述

将主工作区从「左树 + 双 XML 源码 Tab + 右 Inspector」改为 **左窄树 + 中全宽 Building 磁贴 + 底日志**。`Buildings.xml` 不再在主窗编辑；由 **内存权威 + 变更后自动写盘**；`main.xml` 仅经现有对话框维护。顶栏移除「保存 XML」「校验 XML」。建筑 **地图图/图标导入与拖拽** 迁入 **`BuildingEditorWindow`**。

---

## 2. 已确认决策（grill-me）

| 主题 | 结论 |
|------|------|
| 主区布局 | **保留左侧资源树**；**中间全宽** Building 磁贴；**底部日志**保留。 |
| `main.xml` | 主窗**不展示**源码；通过「Mod 信息」、新建 Mod 等**现有流程**读写磁盘。 |
| 顶栏 XML 按钮 | **「保存 XML」「校验 XML」均移除**；写盘失败与校验摘要进**日志**（校验可在代码路径内调用，不设按钮）。 |
| 图片导入 | **迁入 `BuildingEditorWindow`**；磁贴区只展示 + **双击**打开编辑。 |

---

## 3. 主窗口布局（[`app/MainWindow.xaml`](app/MainWindow.xaml)）

- **列结构**：由「树 | 分割 | XML | 分割 | Inspector」改为 **「树 | 分割 | 中间区」**（两列内容 + 一条竖向 `GridSplitter`）。
- **删除**：中间 `TabControl`（`main.xml`、`Buildings.xml` 两 Tab）、`MainXmlEditor`、`BuildingsXmlEditor`；右侧整块（原 `BuildingList`、双图预览、导入按钮等）。
- **中间区**：顶条 **「新建 Building」**（仅 `EnsureModOpen` 时启用）；下方 `ScrollViewer` + `ListBox`/`ListView`，`ItemsPanel` 为 `WrapPanel`（或等效网格）。
- **顶栏**：保留「打开 MOD」「新建 MOD」等与现有一致逻辑；**去掉** `SaveXmlButton`、`ValidateXmlButton`（及对应 `Click` / `UpdateTopBarState` 分支）。

---

## 4. 左栏：资源树（验收标准）

**结构**

- 总览根节点标题为 **「Mod」**；子节点为总览目录下各 Mod 文件夹。
- **已打开某一 Mod 时**，根仍为 **「Mod」**；仅**当前打开**的那条子 Mod 下展开 **Prefabs（若目录存在）→ Thing → Buildings.xml → 各建筑行**；其它 Mod 仍为同级、默认折叠。

**交互**

- 总览态：**单击**某子 Mod 项即 **`OpenModFolder`**，不依赖「打开所选 Mod」。
- 已打开 Mod：**单击另一子 Mod** 即 **切换**到该路径（再次 `OpenModFolder`）。

**排序**

- `Mod` 下子 Mod 列表：**含 `main.xml`（`HasMainXml`）的项排在最前**，其余在后；各段内按文件夹名 **不区分大小写** 排序。

**着色**

- `HasMainXml == true` 的子 Mod 节点：**红色前景**（建议主题键 `Theme.TreeViewModValidForeground`，在 [`Themes/Light.xaml`](app/Themes/Light.xaml) / [`Themes/Dark.xaml`](app/Themes/Dark.xaml) 定义）。
- [`ExplorerTreeView.xaml`](app/Styles/ExplorerTreeView.xaml) 中 `PART_Header` 的 `ContentPresenter` 必须 **`Foreground="{TemplateBinding Foreground}"`**，否则 `TreeViewItem.Foreground` 不生效。

**左栏 UI 精简**

- 移除 **「导航 / 资源树」** 标题与 **「打开所选 Mod」** 按钮；保留 **「刷新」** 与 `TreeView`。

**树形样式**

- 展开：**+ / -**；子级左侧 **虚线导轨**；悬停/选中背景与主题键（如 `Theme.TreeViewLineBrush`、`Theme.TreeViewHoverBackground`）一致。

**与中间区联动**

- 树节点选中 **`Buildings.xml`** 或点击 **「刷新」**：从磁盘 **重载 Buildings 内存**，再解析并 **刷新磁贴**（与第 5 节一致）。

---

## 5. 中间区：Building 磁贴

- **数据源**：`ObservableCollection<BuildingEntry>`（或等价），与解析结果一致。
- **每项**：上图 **`Thing/Buildings/images/icon/{id}.png`**；缺图用占位；下文 **名称 + Id**（具体一行/两行实现时定）。
- **图标槽**：展示与**可点击区域**固定 **256×256** 设备像素；外层 `Width`/`Height` 固定，内层 `Image` `Stretch="Uniform"` 居中；建议 `SnapsToDevicePixels="True"`。
- **双击**：打开 [`BuildingEditorWindow`](app/BuildingEditorWindow.xaml)，数据来自 `TryReadEditableBuildingById` / 现有保存回写链路（改为写内存 + 磁盘，见下节）。
- **单击**（可选）：仅选中高亮。

---

## 6. XML：`Buildings.xml` 与 `main.xml`

**Buildings.xml**

- **单一权威**：主窗私有字段保存全文（`string` 和/或 `XDocument`）；**禁止**再以 `BuildingsXmlEditor.Text` 为真相源。
- **读盘**：打开 Mod、`LoadKnownXmlFiles`、树选中 `Buildings.xml`、刷新 —— 从磁盘读入并覆盖内存。
- **写盘**：新建建筑、编辑弹窗确认等，在内存中改完 `XDocument` 后 **立即** `File.WriteAllText` 至 `Thing/Buildings/Buildings.xml`；失败 **`Log`**；可选 **临时文件 + `File.Replace`**。
- **解析磁贴**：`ParseBuildingsFromEditor` 改为读 **内存字符串**（或从 `XDocument` 序列化），再刷新集合与 UI。
- **无「校验」按钮时**：写盘路径内可调用现有 `ValidateBuildingsXml` 等，将结果写入**日志**。

**main.xml**

- 不在主窗展示；新建 Mod、Mod 信息、导出等保持 **文件读写**，逻辑与现有一致。

**总览根未配置时**

- 打开路径不在总览下的 Mod：左栏可退化为单根（当前实现若有）+ 中间磁贴仍工作；计划验收以「已配置总览」为主路径。

---

## 7. `BuildingEditorWindow` 与新建流程

- **迁入**：原主窗「导入地图图 / 导入物品图标」、拖拽、`WorldImage*` / `IconImage*` 相关逻辑与文案。
- **保存**：确认后更新主窗内存中的 `Buildings.xml`、执行写盘、刷新磁贴与（如需）树。
- **[`AddBuildingWindow`](app/AddBuildingWindow.xaml)**：流程保持；确认后同样 **内存 + 写盘 + 刷新**。

---

## 8. 主要涉及文件

- [`app/MainWindow.xaml`](app/MainWindow.xaml) — 布局、磁贴、顶栏、左栏精简。
- [`app/MainWindow.xaml.cs`](app/MainWindow.xaml.cs) — 内存 XML、`LoadTree` / 树选中 / 刷新、磁贴双击、`UpdateTopBarState`、删除编辑器与保存/校验按钮引用。
- [`app/BuildingEditorWindow.xaml`](app/BuildingEditorWindow.xaml)（及 `.cs`）— 图片区与保存联动。
- [`app/Styles/ExplorerTreeView.xaml`](app/Styles/ExplorerTreeView.xaml) — 确认 `PART_Header` 前景绑定（若尚未）。
- [`app/Themes/Light.xaml`](app/Themes/Light.xaml)、[`app/Themes/Dark.xaml`](app/Themes/Dark.xaml) — `Theme.TreeViewModValidForeground`（若尚未）。

全仓库检索：`MainXmlEditor`、`BuildingsXmlEditor`、`XmlTabs`、`SaveXml`、`ValidateXml` 等，避免残留引用。

---

## 9. 风险

- **外部编辑 `Buildings.xml`**：以「打开 Mod / 点树 Buildings / 刷新」从磁盘覆盖内存为**首版规则**；若后续需要「未保存不覆盖」，再加脏标记与确认框。

---

## 10. 实现 Todo（执行顺序建议）

1. **主窗布局**：两列；删 XML Tab 与右栏；中间磁贴区 + 「新建 Building」。
2. **左栏**：按第 4 节验收（结构、交互、排序、着色、UI 精简、模板、`ExplorerTreeView` 绑定）。
3. **Buildings 内存 + 写盘**：替换所有原 `BuildingsXmlEditor` 读写；封装读/写/解析；树与刷新联动。
4. **磁贴**：`ItemTemplate`、256×256 槽、双击打开编辑器。
5. **顶栏**：移除保存/校验按钮及逻辑。
6. **`BuildingEditorWindow`**：迁入图片导入与拖拽；保存走内存 + 磁盘。
7. **死代码清扫**：全局检索第 8 节所列符号并清理。

---

## 11. 验收自检（完成后）

- 未打开 Mod：新建 Building 不可用；打开后磁贴与子树一致。
- 左栏：`Mod` 父级、排序、红色有效 Mod、无冗余标题/按钮、+/- 与虚线、单击打开/切换 Mod。
- 双击磁贴编辑后磁盘与磁贴、树内建筑行一致。
- 外部改 `Buildings.xml` 后点树或刷新：中间区与解析一致。
- `main.xml` 仍可通过 Mod 信息编辑；无中间 Tab 回归。
