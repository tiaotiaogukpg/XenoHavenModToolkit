# 异星家园（XenoHaven）基础 MOD 开发指南

本文档基于当前已验证的 Mod 样例与 `ModManager.cs` 行为整理。它不是官方 SDK 文档，而是面向基础内容 Mod 作者的实用约定说明。

玩家向简版说明见 [异星家园Mod简易说明.md](./异星家园Mod简易说明.md)。工具按钮操作步骤见 [SteamModDevGuide.md](./SteamModDevGuide.md)。

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

- `id` 必须唯一，使用本地序号 **1、2、3…**（不要写成 `ModId + 序号`）。重复 ID 会导致加载冲突或失败。
- `size.x` 和 `size.y` 表示占地尺寸，应为正整数。
- `materials` 表示建造材料列表，每个 `ModCraftMaterialData` 包含材料 `id` 与数量 `count`。
XenoHaven MOD Toolkit 会在 Building 编辑窗中从 `DOC/K-可用材料表.xlsx` 加载材料下拉（界面显示 `木头-100055`，保存 XML 时写入 `100055`）；数量必须为 1–200 的正整数。
- `workbenchId` 必须从 `DOC/K-可用工作台.xlsx` 中选择（界面显示 `木工桌-100083`，保存 XML 时写入 `100083`）。
- `type` 可为 `BOX`、`SIMPLE_OBJECT`、`SMALL_LAMP`、`STREET_LIGHT`、`PRODUCTION_LINE`、`CARPET`；`SMALL_LAMP` / `STREET_LIGHT` / `CARPET` 与 `SIMPLE_OBJECT` 一样固定 `direction = 1`，且不写入 `capbility`。
- `type` 为 `CARPET` 时走独立地毯层：铺在普通地板之上，桌子等静物可叠在地毯上；同格已有地毯则不可再放（预览为红）。不写入碰撞（`barrier` 固定为 `false`）。
- `type` 为 `PRODUCTION_LINE` 时必须填写 `simulateId`，它表示这个 Mod 建筑要模拟哪一条原版生产线；XenoHaven MOD Toolkit 会从 `DOC/S-生产线定义.xlsx` 加载下拉（界面显示 `炼油厂-302323`，保存 XML 时写入 `302323`）。
- **农业傀儡 `FARMING_GOLEM` 不属于 Buildings**，见 `Thing/Dynamic`（下节）。
- `barrier` 表示是否作为阻挡/障碍处理（`true` = 实体碰撞，不可穿过）。工具在 Building 编辑窗提供 **碰撞** 开关；新建时 `BOX` 默认开启，其它类型默认关闭。游戏端对应 `collider.isTrigger = !barrier`，且 `barrier=true` 时会切到 `Barrier` 层（灯类模板默认在 `Static` 层且 isTrigger，仅改字段不够）。

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

对于 `ModBuildingXML.id = 1`（本地序号，从 1 起递增；**不要**写成 `ModId + 序号`）：

```text
Thing/Buildings/images/1.png
Thing/Buildings/images/icon/1.png
```

含义：

- `images/<id>.png`：建筑/物体在地图上的显示图（文件名为本地序号，如 `1.png`、`2.png`）。
- `images/icon/<id>.png`：建筑被拆除后进入背包、或作为物品显示时的图标。

### FARMING_GOLEM（Thing/Dynamic）

农业傀儡属于 **Dynamic** 生物分类，与 Buildings 同级，位于 `Thing/` 下：

```text
Thing/Dynamic/
  Dynamics.xml                 # ArrayOfModDynamicXML / ModDynamicXML
  images/<id>.png              # 1026×1026 拆分总图
  images/icon/<id>.png
  images/parts/<id>/...        # Head1-3 / HandR / Body / HandL / FootR / FootL
```

- `type=FARMING_GOLEM`；固定 `direction=1`；必填 `simulateId`∈{20191,20192,20193}。
- 参考模板：`templates/FarmingGolem/PartsSheetTemplate.png`。
- 游戏端通过 `ModDynamicHandler` 读取 `Thing/Dynamic/Dynamics.xml`，按 `workbenchId` 注册制造配方；最终物品 ID = `main.xml` 的 `id` + `1000` + 本地 `id`（与 Buildings 的 `id + 本地 id` 错开）。外观先复用 `simulateId` 原版 Prefab，部件换皮另开任务。
- `FARMING_GOLEM` 与原版一致：物品类型为 `MONSTER` + `PET`，制造完成后直接刷出受邀工人（不可背包放置）；体力耗尽后必须绑定 **农业傀儡充电站（303671）** 才能继续工作。

游戏运行时 Buildings 用 `ModId + 本地序号`，Dynamic 用 `ModId + 1000 + 本地序号`；磁盘上的图片仍只按本地 `id` 命名。当前工具支持 `Thing/Buildings` 与 `Thing/Dynamic`。

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

## 9. 游戏端 SMALL_LAMP 对接

`SMALL_LAMP` 的工具端 XML 字段与 `SIMPLE_OBJECT` 一致：固定 `direction = 1`，不需要 `simulateId`，也不写入 `capbility`。游戏端需要配套：

- `ModBuildingType` 增加 `SMALL_LAMP`。
- `ModUtils.ConvertCategory` 将 `SMALL_LAMP` 归为 `ItemCategory.BUILDING`，与 `SIMPLE_OBJECT` 一致。
- `ModBuildingData.SetupCreature` 在单向分支中把 `SMALL_LAMP` 与 `SIMPLE_OBJECT` 同等处理：替换根节点 `SpriteRenderer` 的图片，并按 `size` / `barrier` 设置 `BoxCollider2D`（`barrier=true` 时切到 `Barrier` 层）；有 `Low Power UI` 时，换图后对齐到 Mod 贴图本地坐标中心（`sprite.bounds.center`）。
- 在 `Assets/Resources/Mod/Prefabs/` 增加 `mod_template_type_small_lamp_1.prefab`，基于原版 `300059_space_ship_lamp` 保留用电、灯光与缺电提示 `Low Power UI` 组件；需挂 `ModBuilding1Component`，默认层宜为 `Barrier`（勿停在仅可穿过的 `Static` + Trigger）。

## 10. 游戏端 STREET_LIGHT 对接

`STREET_LIGHT` 的工具端 XML 字段与 `SMALL_LAMP` 一致：固定 `direction = 1`，不需要 `simulateId`，也不写入 `capbility`。游戏端需要配套：

- `ModBuildingType` 增加 `STREET_LIGHT`。
- `ModUtils.ConvertCategory` 将 `STREET_LIGHT` 归为 `ItemCategory.BUILDING`，与 `SMALL_LAMP` 一致。
- `ModBuildingData.SetupCreature` 在单向分支中把 `STREET_LIGHT` 与 `SMALL_LAMP` 同等处理（含缺电 UI 对齐贴图中心）。
- 在 `Assets/Resources/Mod/Prefabs/` 增加 `mod_template_type_street_light_1.prefab`，基于原版 `303486_building_303486` 剥离 `ColonyProsperComponent` 等无关组件，保留 `SortingGroup`、`PowerSupplyConsumer`（耗电 100）、大范围 `Point Light 2D`（内径 3 / 外径 12）与缺电提示 `Low Power UI`。

## 11. 游戏端 CARPET 对接

`CARPET` 的工具端 XML 字段与 `SIMPLE_OBJECT` 一致：固定 `direction = 1`，不需要 `simulateId`，也不写入 `capbility`；碰撞固定关闭。

游戏端配套：

- `MapLayer.CARPET`（原未使用的 `DECORATE = 20`）与 `CarpetLayer`（Tilemap，独立于 `FLOOR`）。
- `ModBuildingType` 增加 `CARPET`；`ModUtils.ConvertCategory` 归为 `ItemCategory.FLOOR`（支持按住连铺）。
- `ModBuildingHandler` 注册时 `layer = MapLayer.CARPET`，运行时用贴图创建 `Tile`（无 Creature 模板，不需要 `mod_template_type_carpet_1.prefab`）。地图贴图用 `LoadSpriteNormalizedToUnit` 缩放到约 1×1 格，避免大图溢出。
- `MapManager.CanAddCarpetObject`：占地格已有地毯则不可放；不检查静物层（可铺在桌子下）；桌子仍可放在地毯上。
- Unity：`Map.prefab` 需挂 `CarpetLayer` + Tilemap 子节点，并绑定到 `MapManager.carpetLayer`。

## 12. 使用 XenoHaven MOD Toolkit

基本流程：

1. 打开工具（自动使用程序旁 `Mods` 目录，无需选择工作目录）。
2. 在左侧树中从 `Mods` 下列出的工程中选择一个 Mod（或使用「打开工程」打开任意含 `main.xml` 的目录）。
3. 在左侧树中选中对应节点后，顶栏按钮才会启用（类似资源管理器）：
  - 选中当前 Mod 根或 `main.xml` → **Mod 信息**
  - 选中 `Buildings.xml` → **新建 Building**
  - 选中某个 Building 节点（或右侧磁贴）→ **编辑 Building** / **删除 Building**
  - 选中当前 Mod 根 → **导出发布版** / **清理 .meta**
4. 编辑 `main.xml` 和 `Thing/Buildings/Buildings.xml`（双击 Building 节点可打开编辑窗）。
5. 在右侧选择一个建筑条目可同步树选中并预览图片。
6. 导入“地图显示图”或“物品图标”，工具会自动保存为本地序号文件名（如 `1.png`）。
7. 保存 XML。
8. 导出发布版 Mod 或清理 `.meta`。

## 13. 当前限制

- 当前游戏端大概率不支持热重载。修改 Mod 后通常需要重启游戏或重新进入存档。
- 当前工具第一版优先支持 `Thing/Buildings`。
- 工具只做基础静态校验，不能完全替代游戏内实测。
- 物品 ID、工作台 ID、材料 ID 是否真实存在，仍需要结合游戏数据验证。

## 13. 建议的开发习惯

- 每个 Mod 使用唯一目录名。
- 每个 Thing 使用唯一 `id`。
- 修改 XML 后先用工具校验，再进游戏测试。
- 发布前清理 `.meta`。
- 不要随意改动已验证 XML 字段名，尤其是 `capbility` 这类与游戏端反序列化强绑定的字段。

