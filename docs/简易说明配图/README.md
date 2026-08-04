# 简易说明配图清单

把截图按下面**文件名**放到本文件夹即可。生成 / 更新 Word 时会按文件名自动插入；若某张图尚未补充，Word 里会保留红色占位框提示你补图。

建议格式：`png`（也可用 `jpg`）。建议宽度约 1200～1600 像素，界面文字清晰可读。

| 文件名 | 对应文档位置 | 建议截什么 |
|--------|--------------|------------|
| `01-main-window.png` | 第 2 章 · 工具主界面 | 工具完整主窗口：顶栏、左侧树、右侧磁贴、底部日志 |
| `02-toolbar-buttons.png` | 第 2 章 · 顶栏按钮 | 顶栏工具按钮一排（可标注或框选出各按钮） |
| `03-new-mod-dialog.png` | 第 2 章 · 新建 MOD | 「新建MOD」对话框，含名称/作者/描述与图标导入区 |
| `04-building-editor.png` | 第 2 章 · 新建/编辑组件 | Building 编辑窗口（属性、材料、图片预览） |
| `05-steam-connected.png` | 第 3 章 · 上传步骤 1 | 顶栏显示已连接 Steam 账号的状态 |
| `06-workshop-upload.png` | 第 3 章 · 上传步骤 4 | 「上传到创意工坊」窗口（标题、简介、可见性） |
| `07-workshop-subscribe.png` | 第 3 章 · 下载步骤 2 | Steam 工坊作品页，突出「订阅」按钮 |
| `08-game-mod-list.png` | 第 3 章 · 下载步骤 4 | 游戏内 Mod 列表/启用界面 |

补图完成后，若需要重新生成 Word，可在仓库根目录执行：

```powershell
python .\scripts\export-mod-guide-docx.py
```
