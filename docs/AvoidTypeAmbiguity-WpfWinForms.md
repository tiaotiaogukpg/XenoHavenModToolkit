# 如何规避 WPF + WinForms 同名类型二义性（CS0104）

本项目同时启用了 **WPF**（`System.Windows.*`）与 **WinForms**（`System.Windows.Forms.*`）。两套 UI 框架里存在大量**同名类型**，例如：

- `MessageBox`
- `DragEventArgs`
- `DataFormats`
- `OpenFileDialog`

因此在新增功能时，只要 `using` 的命名空间碰到同名类型，就容易触发 **CS0104 不明确的引用**。

## 另一个高频坑：可访问性不一致（CS0051/CS0053）

本项目很多数据结构/窗口类会用 `internal` 限制在程序集内使用；但 WPF 的 `Window`/控件类通常默认写成 `public`。

当 **公开成员**（`public` 方法/构造函数/属性）暴露了 **更低可见性** 的类型时，就会触发：

- **CS0051**：方法参数类型可访问性低于方法
- **CS0053**：属性类型可访问性低于属性

### 规避原则（推荐）

- **Window 构造函数默认用 `internal`**（除非你真的要给外部程序集调用）。
- **Result/Settings 之类返回给主窗口的类型**：
  - 要么把返回属性改成 `internal`
  - 要么把类型提升为 `public`

### 快速自检

如果你新增了 `internal record/class`，然后在某个 `public` 成员签名里用到了它（参数/返回值/属性类型），那 99% 会报 CS0051/CS0053。

## 规避原则（本项目推荐）

### 1) WinForms 统一用别名引用

本项目已经采用：

- `using WinForms = System.Windows.Forms;`

以后任何 WinForms 类型统一写成：

- `WinForms.FolderBrowserDialog`
- `WinForms.DialogResult`

不要写 `using System.Windows.Forms;`，否则更容易把同名类型带进来。

### 2) WPF 同名类型要么“全限定名”，要么“统一别名”

当你需要用到 WPF 的拖拽/剪贴板/数据格式等，推荐在文件顶部集中声明别名：

```csharp
using WpfApplication = System.Windows.Application;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
```

然后在代码里只使用别名：

- `WpfApplication.Current`
- `WpfDataFormats.FileDrop`
- `WpfDragEventArgs`
- `WpfDragDropEffects.Copy`

这能从根上避免后续再引入 WinForms 同名类型时把你“炸回 CS0104”。

### 3) MessageBox 必须明确使用 WPF 版本

项目里同时存在：

- `System.Windows.MessageBox`（WPF）
- `System.Windows.Forms.MessageBox`（WinForms）

建议：

- **在 WPF Window/控件里**：始终写 `System.Windows.MessageBox.Show(...)`
- 不要只写 `MessageBox.Show(...)`

### 4) 新增代码前做一次“同名风险自检”

你只要用到以下关键字，就要想到可能会撞名：

- `MessageBox`
- `Drag`*
- `DataFormats`
- `OpenFileDialog`

此时优先用 **全限定名/别名**，不要依赖 `using` 自动解析。

## 这次问题的具体修复方式（示例）

`MainWindow.xaml.cs` 里拖拽导入图片用了：

- `DragEventArgs`
- `DataFormats.FileDrop`

因为 WinForms 也有同名类型，导致 CS0104。

解决方式：

- 显式使用 WPF 的 `System.Windows.DataFormats`
- 或统一用 `WpfDataFormats` 别名（项目已采用）

## 如何“保证以后不会再犯”

- **约定写法**：WinForms 只用 `WinForms.`*；WPF 的同名类型统一用 `System.Windows.`* 或 `Wpf*` 别名。
- **代码评审点**：看到 `using System.Windows.Forms;` 或看到裸写 `MessageBox/DragEventArgs/DataFormats` 就立刻改成别名/全限定名。
- **落地策略**：以后新增 UI 相关功能时，先在文件顶部补齐常用别名（`WpfDataFormats/WpfDragEventArgs/...`），再写事件与逻辑代码。

