# 异星家园（XenoHaven）基础 MOD 开发指南

本文档基于当前已验证的 Mod 样例与 `ModManager.cs` 行为整理。它不是官方 SDK 文档，而是面向基础内容 Mod 作者的实用约定说明。

工具操作步骤（打开工具、新建/编辑 Mod 与组件）见 [SteamModDevGuide.md](./SteamModDevGuide.md)。

## 1. MOD 加载模型

XenoHaven 当前的 Mod 系统属于“文件夹扫描 + XML 定义 + 启动时注册”的 Unity 内容扩展架构。

大致流程：

```text
游戏启动
  -> ModManager 扫描 Mod 根目录
  -> 读取每个 Mod 文件夹内的 XML 定义
  -> 读取本地启用配置
  -> 加载已启用 Mod 的内容
  -> 注册到游戏系统
```

当前最明确、已验证的扩展方向是 Thing/Buildings，也就是建筑与可放置物体。

## 2. 基础目录结构

一个最小可用的建筑 Mod 推荐结构如下：

```text
MyBuildingMod/
  main.xml
  icon.png
  screenshot.png
  Thing/
    Buildings/
      Buildings.xml
      images/
        1.png
        icon/
          1.png
```

说明：

- `main.xml`：Mod 的元信息。
- `icon.png`：Mod 在列表中的图标，固定放在 Mod 根目录。
- `screenshot.png`：Mod 在详情/对话框中的截图，固定放在 Mod 根目录。
- `Thing/Buildings/Buildings.xml`：建筑/物体定义列表。
- `Thing/Buildings/images/<id>.png`：该 Thing 在地图上的显示图片。
- `Thing/Buildings/images/icon/<id>.png`：该 Thing 在背包、拆除后物品等场景中的图标。
- `*.meta` 是 Unity 生成的资源追踪文件，对发布版 Mod 无用，发布前应删除。

## 3. main.xml

`main.xml` 目前已验证的基本结构：

```xml
<?xml version="1.0" encoding="utf-8"?>
<defs xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <name>Example Building Mod</name>
  <auth>Author</auth>
  <version>1.0.0</version>
  <description>Mod description</description>
</defs>
```

字段含义：

- `name`：Mod 名称。
- `auth`：作者。
- `version`：Mod 版本，默认与游戏最新版一致（目前 `1.0.0`）。
- `description`：描述，必填。

XenoHaven MOD Toolkit 会以 UTF-8（无 BOM）写回 XML，并保持多行缩进，避免中文内容或外部工具处理时出现编码问题。

## 4. Buildings.xml

`Thing/Buildings/Buildings.xml` 的根节点应为：

```xml
<ArrayOfModBuildingXML>
```

每个建筑/物体条目使用：

```xml
<ModBuildingXML>
  <id>1</id>
  <name>Example Production Line</name>
  <type>PRODUCTION_LINE</type>
  <direction>1</direction>
  <capbility>10</capbility>
  <size>
    <x>1</x>
    <y>1</y>
  </size>
  <workbenchId>100083</workbenchId>
  <simulateId>302323</simulateId>
  <materials>
    <ModCraftMaterialData>
      <id>100055</id>
      <count>10</count>
    </ModCraftMaterialData>
  </materials>
  <health>100</health>
  <barrier>true</barrier>
</ModBuildingXML>
```

关键规则：

- `id` 必须唯一。重复 ID 会导致加载冲突或失败。
- `size.x` 和 `size.y` 表示占地尺寸，应为正整数。
- `materials` 表示建造材料列表，每个 `ModCraftMaterialData` 包含材料 `id` 与数量 `count`。
XenoHaven MOD Toolkit 会在 Building 编辑窗中从 `DOC/K-可用材料表.xlsx` 加载材料下拉（界面显示 `木头-100055`，保存 XML 时写入 `100055`）；数量必须为 1–200 的正整数。
- `workbenchId` 必须从 `DOC/K-可用工作台.xlsx` 中选择（界面显示 `木工桌-100083`，保存 XML 时写入 `100083`）。
- `type` 为 `PRODUCTION_LINE` 时必须填写 `simulateId`，它表示这个 Mod 建筑要模拟哪一条原版生产线；XenoHaven MOD Toolkit 会从 `DOC/S-生产线定义.xlsx` 加载下拉（界面显示 `炼油厂-302323`，保存 XML 时写入 `302323`）。
- `barrier` 表示是否作为阻挡/障碍处理。

注意：字段名 `capbility` 按当前可行样例保留拼写，不要自行改成 `capability`，否则游戏端可能无法反序列化。

## 5. 图片命名规则

图片不写入 `Buildings.xml` 字段，而是通过 `id` 与文件名约定对应。

Mod 自身还有两张根目录图片，文件名固定：

```text
icon.png
screenshot.png
```

- `icon.png`：Mod 列表图标。
- `screenshot.png`：Mod 详情截图。
- 新建、保存 Mod 信息、导出发布版时，XenoHaven MOD Toolkit 会校验这两个文件是否存在。

对于 `ModBuildingXML.id = 70000001`（示例 Mod 基础值 70000000 + 本地序号 1）：

```text
Thing/Buildings/images/70000001.png
Thing/Buildings/images/icon/70000001.png
```

含义：

- `images/<id>.png`：建筑/物体在地图上的显示图。
- `images/icon/<id>.png`：建筑被拆除后进入背包、或作为物品显示时的图标。

这个规则也适用于其它 Thing 类型：地图图使用 `images/<id>.png`，物品图标使用 `images/icon/<id>.png`。当前工具第一版优先支持 `Thing/Buildings`。

## 6. Unity .meta 文件

如果 Mod 是从 Unity 工程目录中拷贝出来的，可能会包含许多 `.meta` 文件。

这些文件是 Unity 编辑器的资源数据库信息，对 XenoHaven Mod 运行时没有意义。发布 Mod 时建议删除全部 `.meta` 文件。

XenoHaven MOD Toolkit 提供：

- 一键清理当前 Mod 内的 `.meta` 文件。
- 导出发布版 Mod：复制目录时自动跳过 `.meta`。

## 7. 游戏数据表（DOC）

工具从仓库根目录 `DOC/` 读取 Excel（构建/发布时会复制到程序目录下的 `DOC/`）：


| 文件             | 用途                                  |
| -------------- | ----------------------------------- |
| `K-可用材料表.xlsx` | 制造公式材料下拉                            |
| `K-可用工作台.xlsx` | `workbenchId` 下拉                    |
| `S-生产线定义.xlsx` | `PRODUCTION_LINE` 的 `simulateId` 下拉 |


表头需包含「名称」与「ID」列（也支持 `name`/`id` 等别名）。界面统一显示为 `名称-ID`，写入 XML 的仍是数字 ID。

## 8. 游戏端 PRODUCTION_LINE 对接

`PRODUCTION_LINE` 的工具端 XML 字段需要游戏端配套读取：

- `ModBuildingXML` 增加 `public int simulateId;`。
- `ModBuildingType` 增加 `PRODUCTION_LINE`。
- 在 `ProductionLineComponent` 同目录创建 `ModProductlineComponent : MonoBehaviour`，用于保存当前 Mod 建筑的 `simulateId`。
- `ProductionLineComponent` 查找公式时优先读取 `ModProductlineComponent`；存在且 `simulateId > 0` 时使用 `ProductLineDatabase.Instance.Find(simulateId)`，否则保留原来的 `ProductLineDatabase.Instance.Find(creature.ID)`。

## 9. 使用 XenoHaven MOD Toolkit

基本流程：

1. 打开工具。
2. 选择一个 Mod 根目录，例如 `MyBuildingMod/`。
3. 在左侧树中选中对应节点后，顶栏按钮才会启用（类似资源管理器）：
  - 选中当前 Mod 根或 `main.xml` → **Mod 信息**
  - 选中 `Buildings.xml` → **新建 Building**
  - 选中某个 Building 节点（或右侧磁贴）→ **编辑 Building** / **删除 Building**
  - 选中当前 Mod 根 → **导出发布版** / **清理 .meta**
4. 编辑 `main.xml` 和 `Thing/Buildings/Buildings.xml`（双击 Building 节点可打开编辑窗）。
5. 在右侧选择一个建筑条目可同步树选中并预览图片。
6. 导入“地图显示图”或“物品图标”，工具会自动保存为 `<id>.png`。
7. 保存 XML。
8. 导出发布版 Mod 或清理 `.meta`。

## 9. 当前限制

- 当前游戏端大概率不支持热重载。修改 Mod 后通常需要重启游戏或重新进入存档。
- 当前工具第一版优先支持 `Thing/Buildings`。
- 工具只做基础静态校验，不能完全替代游戏内实测。
- 物品 ID、工作台 ID、材料 ID 是否真实存在，仍需要结合游戏数据验证。

## 10. 建议的开发习惯

- 每个 Mod 使用唯一目录名。
- 每个 Thing 使用唯一 `id`。
- 修改 XML 后先用工具校验，再进游戏测试。
- 发布前清理 `.meta`。
- 不要随意改动已验证 XML 字段名，尤其是 `capbility` 这类与游戏端反序列化强绑定的字段。

