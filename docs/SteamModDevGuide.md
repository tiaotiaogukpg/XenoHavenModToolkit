# 异星家园（XenoHaven）Mod 开发指南 — 工具使用篇

本指南说明如何使用 **XenoHaven MOD Toolkit**（主程序 `XenoHavenModTool.exe`）制作建筑类 Mod，适合发布到 Steam 创意工坊或随游戏分发的 Mod 工具包。当前工具第一版主要支持 `Thing/Buildings`（建筑/可放置组件）。

从创建到工坊上传的完整跟做教程见 [异星家园Mod制作完整教程.md](./异星家园Mod制作完整教程.md)。更完整的 XML 约定、目录结构与 `PRODUCTION_LINE` 对接说明见 [ModDevGuide.md](./ModDevGuide.md)。

---

## 1. 如何打开工具

### 1.1 启动程序

在 Windows 上运行 **XenoHaven MOD Toolkit**：

- **Steam 分发**：在 Steam 库中找到该工具（或游戏附带的 Mod 工具），点击「开始游戏」或运行安装目录中的 `XenoHavenModTool.exe`。
- **本地开发 / 便携版**：在工具发布包中运行 `dist/win-x64/XenoHavenModTool.exe`，或在源码目录执行：

```powershell
dotnet run --project .\app\app.csproj
```

启动后主窗口标题为 **XenoHavenModTool**，界面分为：顶栏工具按钮、左侧 Mod 树、右侧组件磁贴预览、底部日志区。

### 1.2 Mods 目录（无需手动配置）

工具启动后会自动使用程序旁的 **`Mods`** 文件夹作为工程总览目录：

- **开发模式**：仓库根目录下的 `Mods/`（例如 `XenoHavenModToolkit/Mods`）。
- **发布版**：可执行文件同级目录下的 `Mods/`（例如 `XenoHavenModTool/Mods`）。

规则：

- 该目录下 **每个一级子文件夹 = 一个 Mod 工程**（不会把 `Mods` 自身或深层子目录当成独立工程）。
- 若 `Mods` 不存在，启动时会自动创建。
- **不需要**首次选择工作目录，也没有工程根路径设置入口。

示例仓库中已包含 `Mods/XenoModDemo`，开发启动后左侧树应能看到该工程。

配置文件位于应用基础目录：`config.json`（开发时在仓库根，发布时在 exe 旁）。

### 1.3 打开已有 Mod

有两种方式：


| 方式             | 操作                                                         |
| -------------- | ---------------------------------------------------------- |
| **从总览树打开（推荐）** | 左侧展开 `Mod` → 点击某个 Mod 文件夹名；顶栏会显示当前 Mod 路径，右侧出现该 Mod 的组件磁贴。 |
| **打开工程**       | 点击顶栏 **「打开工程」**，选择包含 `main.xml` 的任意 Mod 根目录（例如临时查看不在 `Mods` 下的工程）。 |


打开 Mod 后，左侧树会显示 `main.xml`、`icon.png`、`screenshot.png`、`Thing/Buildings/Buildings.xml` 及各组件节点。底部 **CONSOLE / 日志** 会输出载入、校验与保存结果。

![图 1：主界面与「打开工程」](./Images/1.png)

*图 1：主界面布局。顶栏 **「打开工程」** 用于选择任意包含 `main.xml` 的 Mod 文件夹；也可在左侧总览树中直接点击 `Mods` 下列出的 Mod 名称打开。*

> **提示**：在左侧空白处单击可退出当前 Mod，回到总览列表；点击 **「刷新」** 可重新扫描 `Mods` 目录。

---

## 2. 如何创建 Mod

### 2.1 前置条件

- 工具已能扫描到旁路 `Mods` 目录（启动时自动定位）。
- 在总览状态下，或当前未打开其它 Mod 时，顶栏 **「新建MOD」** 可用。

### 2.2 填写 Mod 信息

点击 **「新建MOD」**，在对话框中填写：


| 字段                       | 说明                                                 |
| ------------------------ | -------------------------------------------------- |
| **id**                   | Mod 基础 ID，创建时自动生成，**创建后不可修改**。游戏内最终物品 ID = 基础值 + Building 本地序号。 |
| **steamPublishedFileId** | Steam 创意工坊条目 ID；新建 Mod 固定为 `0`，发布并由游戏/工坊写入后只读显示。   |
| **supportVersion**       | 支持版本，固定为 `1`。                                      |
| **name**                 | Mod 显示名称（必填）。                                      |
| **auth**                 | 作者（必填）。                                            |
| **version**              | Mod 版本号，**默认游戏最新版（目前 1.0.0）**。                     |
| **category**             | 分类，默认 `Building`，用于总览树分组显示。                        |
| **description**          | 描述（必填，支持多行）。                                       |
| **icon.png**             | Mod 列表图标（必填，支持 png / jpg / jpeg / bmp / webp）。     |
| **screenshot.png**       | Mod 详情截图（必填）。                                      |


填写完成后点击 **create**。

### 2.3 创建结果

工具会在 `Mods` 目录下新建文件夹（名称由 `name` 生成，重名时自动加 `_2`、`_3` 等后缀），并生成：

```text
你的Mod名称/
  main.xml
  icon.png
  screenshot.png
  Thing/
    Buildings/
      Buildings.xml    （空的组件列表）
      images/
        icon/
```

创建成功后会自动在左侧树中选中该 Mod，可继续 **新建组件**（见下文）。

---

## 3. 如何编辑 Mod 以及 Mod 的组件

工具中 **「组件」** 指 `Buildings.xml` 里的一条 `ModBuildingXML`（建筑/可放置物体）。顶栏按钮会随左侧树或右侧磁贴的选中项启用/禁用。

### 3.1 编辑 Mod 元信息

在已打开某个 Mod 的前提下，选中以下任一节点后，点击 **「编辑MOD」**：

- Mod 根节点
- `main.xml`
- `Buildings.xml`
- 任意组件节点

在 **编辑MOD** 窗口中可修改：

- **name / auth / version / category / description**
- **icon.png、screenshot.png**（导入/替换，可展开预览）

**id**、**steamPublishedFileId**、**supportVersion** 为只读。点击 **save** 后写入 `main.xml` 并更新根目录图片；校验失败时会在底部日志中提示，且不会保存。

删除整个 Mod：在总览树选中 Mod 文件夹（或已打开该 Mod 时），点击 **「删除MOD」** 并确认（**不可恢复**）。

### 3.2 新建组件

选中 **Mod 根** 或 **`Buildings.xml`** 后，点击顶栏 **「新建组件」**。

![图 3：新建组件按钮位置](./Images/3.png)

*图 3：在左侧树选中当前 Mod 或 `Buildings.xml` 时，顶栏 **「新建组件」** 会高亮可用。*

在 **Building 编辑** 窗口中配置属性并 **保存**，工具会：

1. 将条目写入 `Thing/Buildings/Buildings.xml`
2. 立即保存到磁盘
3. 刷新左侧树与右侧磁贴

组件 **id** 为本地序号（1、2、3…），图片按同名保存；游戏内可与 Mod 基础值合成最终物品 ID，请勿与其它 Mod 冲突。

### 3.3 编辑已有组件

任选其一：

- 左侧树展开 `Thing` → `Buildings.xml` → 点击某个组件节点
- 右侧磁贴区选中对应卡片（可与树同步）
- **双击** 树节点或磁贴

再点击 **「编辑组件」**（或双击直接打开编辑窗）。

![图 4：编辑组件](./Images/4.png)

*图 4：在右侧磁贴或左侧树中选中某个组件后，点击顶栏 **「编辑组件」**，或双击磁贴/树节点，打开 Building 编辑窗口。*

#### 组件字段说明


| 字段                  | 说明                                                                   |
| ------------------- | -------------------------------------------------------------------- |
| **id**              | 只读本地序号（1、2、3…），与图片文件名对应；勿使用 ModId+序号。                                      |
| **name**            | 组件显示名称。                                                              |
| **type**            | `BOX`、`SIMPLE_OBJECT`、`SMALL_LAMP`、`STREET_LIGHT`、`PRODUCTION_LINE`。                 |
| **direction**       | 朝向；`BOX` / `SIMPLE_OBJECT` / `SMALL_LAMP` / `STREET_LIGHT` 固定为 `1`。                    |
| **workbenchId**     | 工作台，从游戏数据表下拉（显示 `名称-ID`，XML 存数字 ID）。                                 |
| **simulateId**      | 仅 **PRODUCTION_LINE** 需要，选择要模拟的原版生产线。                                |
| **capbility**       | 容量（16–96）；`SIMPLE_OBJECT` / `SMALL_LAMP` / `STREET_LIGHT` 不显示此字段。拼写须为 `capbility`（勿写成 `capability`）。 |
| **health**          | 固定为 `10`。                                                            |
| **size.x / size.y** | 占地尺寸（正整数）。                                                           |
| **制造公式**            | 材料从材料表选择，单条数量 1–200。                                                 |
| **碰撞**              | On/Off 滑块写入 `<barrier>true/false</barrier>`；`BOX` 默认 On。 |
| **物品栏图标**           | 保存为 `Thing/Buildings/images/icon/1.png`（按本地 id）。                          |
| **组件图片**            | 地图显示图，保存为 `Thing/Buildings/images/1.png`（按本地 id）。                         |


支持 **拖拽图片** 到预览区，或点击按钮导入；支持 png / jpg / jpeg / bmp / webp。

### 3.4 删除组件

在磁贴或树中选中组件 → **「删除组件」** → 确认。会同时从 `Buildings.xml` 移除条目，并尝试删除对应 `images` 下的图片文件。

### 3.5 界面与数据对照

```text
Mod 根目录
├── main.xml              ← 「编辑MOD」
├── icon.png / screenshot.png
└── Thing/Buildings/
    ├── Buildings.xml     ← 「新建组件」
    ├── images/1.png      ← 组件图片（本地序号）
    └── images/icon/1.png ← 物品栏图标（本地序号）
```

底部日志会显示 XML 校验、保存成功或错误（如重复 id、缺少必填字段等）。

### 3.6 测试与发布注意

- 游戏 **通常不支持 Mod 热重载**，改 XML 或图片后请 **重启游戏或重新进档** 再测。
- 工具校验 **不能代替** 游戏内实测；材料 ID、工作台 ID 等需在 `DOC/` 数据表与游戏中一致。
- 从 Unity 拷贝的 Mod 可能带 `.meta` 文件，发布前请删除（对运行时无意义）。
- 上传到 Steam 创意工坊后，`main.xml` 中的 **steamPublishedFileId** 会由游戏更新；本地新建请保持 `0`。

---

## 相关文档

- [ModDevGuide.md](./ModDevGuide.md) — Mod 目录结构、XML 字段与图片命名等技术约定

