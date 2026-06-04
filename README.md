# XenoHaven MOD Toolkit

XenoHaven MOD Toolkit 是一个用于制作异星家园（XenoHaven）基础 MOD 的 C# WPF 桌面工具。

当前版本主要面向 `Thing/Buildings` 类型 Mod，支持编辑 XML、按 ID 导入图片、预览图片，并导出不含 Unity `.meta` 文件的发布版 Mod。

## 功能

- 打开一个 Mod 根目录。
- 浏览 Mod 文件树。
- 编辑 `main.xml`。
- 编辑 `Thing/Buildings/Buildings.xml`。
- 校验 XML 基础结构：
  - `main.xml` 根节点应为 `defs`
  - `Buildings.xml` 根节点应为 `ArrayOfModBuildingXML`
  - `ModBuildingXML.id` 不应重复
  - `size`、`materials` 等基础字段应为有效正整数
- 按 `id` 导入两类图片：
  - 地图显示图：`Thing/Buildings/images/<id>.png`
  - 背包/拆除后物品图标：`Thing/Buildings/images/icon/<id>.png`
- 选中建筑条目时自动预览两类图片。
- 顶栏按钮随左侧树节点选中状态启用（类似 Windows 资源管理器）。
- Building 编辑窗从 `DOC/` 下的 Excel 加载材料、工作台与生产线列表（显示 `名称-ID`，XML 写入数字 ID）。
- 支持 `PRODUCTION_LINE` 类型，并通过 `simulateId` 选择要模拟的生产线。
- 制造公式材料只能从材料表下拉选择，单条数量上限 200。
- 一键删除当前 Mod 内所有 `.meta` 文件。
- 导出发布版 Mod（复制目录并跳过 `.meta`）。

## 工程结构

```text
XenoHavenModToolkit/
  XenoHavenModToolkit.slnx
  app/
    app.csproj
    App.xaml
    MainWindow.xaml
    MainWindow.xaml.cs
  DOC/
    K-可用材料表.xlsx
    K-可用工作台.xlsx
    S-生产线定义.xlsx
  docs/
    ModDevGuide.md
    SteamModDevGuide.md
    Images/          （Steam 指南配图）
  samples/
    ExampleMod/
      main.xml
      Thing/
        Buildings/
          Buildings.xml
          images/
            icon/
```

## 开发环境

- Windows
- .NET SDK（当前工程模板为 `net10.0-windows`）
- WPF 支持

检查 SDK：

```powershell
dotnet --version
```

## 运行

```powershell
dotnet run --project .\app\app.csproj
```

## 构建

```powershell
dotnet build .\XenoHavenModToolkit.slnx
```

## 发布 Windows 便携 EXE

推荐使用仓库内脚本：

```powershell
.\scripts\publish-win-x64.ps1
```

或手动执行：

```powershell
dotnet publish .\app\app.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64
```

输出目录：

```text
dist/win-x64/
```

其中 `XenoHavenModTool.exe` 可作为便携版直接分发。

## 文档


| 文档                                                   | 说明                                       |
| ---------------------------------------------------- | ---------------------------------------- |
| [docs/SteamModDevGuide.md](docs/SteamModDevGuide.md) | **Steam / 玩家向**：如何打开工具、创建 Mod、编辑 Mod 与组件 |
| [docs/ModDevGuide.md](docs/ModDevGuide.md)           | **技术向**：目录结构、XML 字段、图片命名与游戏对接约定          |


## 重要规则

对于 `Thing/Buildings/Buildings.xml` 中的条目：

```xml
<ModBuildingXML>
  <id>1</id>
</ModBuildingXML>
```

对应图片为：

```text
Thing/Buildings/images/1.png
Thing/Buildings/images/icon/1.png
```

- `images/1.png`：地图显示图。
- `images/icon/1.png`：背包/拆除后物品图标。

## 注意

- `.meta` 是 Unity 生成文件，发布版 Mod 不需要。
- 当前工具第一版优先支持 `Thing/Buildings`。
- 工具校验不能代替游戏内实测。

