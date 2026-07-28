# Steam Workshop MOD 上传逻辑说明

> 文档状态：二次审查修订（2026-07-23）  
> 证据范围：KeplerthUploader 反编译 + 当前 XenoHaven Mod Toolkit 源码。  
> 标注约定：  
> - **【源码事实】**：本地有类/函数/字段/文件证据  
> - **【设计建议】**：本工具尚未实现，仅供后续开发  
> - **【惯例/待确认】**：Steam 常见行为或证据不足，不得写成已证实实现

---

## 0. 本轮审查：问题清单与十条达标

审查对象：本文件当前正文（非缺失稿）。

### 0.1 发现的问题（修订前）

1. **§0 过时**：仍写“原稿不存在”，对实现读者无用，应改为针对现行正文的审查结论。  
2. **§7 标题误标【源码事实】**：小节混入“应何时保存/建议写入”，事实与建议边界模糊。  
3. **§3.2 越界陈述**：称 `steam_appid.txt`“通常供 Init 识别”——反编译**未读该文件**，属 Steamworks 惯例，不应贴在纯源码事实旁不加标签。  
4. **首次流程不完整**：未写清选目录时复制到 `Upload Folder`、以及 `dataPath` 被设为上传根目录的实际行为；未写 `OnCreateItemResult` **不检查** `m_eResult` 仍继续 `StartItemUpdate`（实现时须规避）。  
5. **更新流程不完整**：未区分 `QueryAllMods` 与 `QueryAllModsSelf`（Refresh）的 App 参数差异。  
6. **API 白名单缺 CallResult 机制说明**：账号/上传指导未强调必须 `RunCallbacks`，否则回调不到。  
7. **§10 指导偏薄**：未映射本工具的 `icon.png`/`screenshot.png`/`main.xml` 到 `SetItem*`；未给出首次/更新判定伪代码与错误处理清单。  
8. **§12 结论混入设计语气**（“必须写入”）易被当成 Keplerth/现状事实。  
9. **CreateItem“成功”措辞过满**：源码拿到回调后未判 `m_eResult`，文档写“成功”易误导。  
10. **无十条对照表**：读者无法一眼看到缺口是否已补。

### 0.2 十条标准达标情况（修订后目标）

| # | 标准 | 修订前 | 修订后落点 |
|---|------|--------|------------|
| 1 | 完整首次发布流程 | 主路径有，缺目录复制与 Create 失败未拦截 | §4（补全） |
| 2 | 完整更新流程 | 主路径有，缺 Refresh 查询变体 | §5（补全） |
| 3 | 区分 Mod ID / Publisher ID / PublishedFileId | 已有，保留并收紧措辞 | §2 |
| 4 | PublishedFileId 何时保存 | 事实/建议混在 §7 | §7 拆成事实对照 + 建议 |
| 5 | 区分源码事实与设计建议 | 大体有，个别越界 | 全文标签收紧 |
| 6 | Keplerth 结论有源码证据 | 附录有，正文部分缺函数名 | §3–6 证据行补全 |
| 7 | 无未经证实的 Steam API | 白名单大致正确；惯例未标注 | §6 + 惯例标签 |
| 8 | 无自相矛盾 | AppId 双值、§7 标签矛盾 | §11 + 措辞修正 |
| 9 | 能指导 Steam 账号集成 | 偏简 | §9 加清单 |
| 10 | 能指导 MOD 上传实现 | 偏简 | §10 加映射与伪代码 |

---

## 1. 证据与材料范围

### 1.1 【源码事实】KeplerthUploader（无原工程源码，仅二进制 + 反编译）

路径：`D:\Unity\XenoHavenModTool\KeplerthUploader\`

| 文件 | 作用 |
|------|------|
| `ModUploader.exe` | WinForms 上传程序 |
| `Steamworks.NET.dll` | Steamworks C# 绑定 |
| `steam_api.dll` / `steam_api64.dll` | Steam 原生 API |
| `steam_appid.txt` | 单行内容 `925420` |

反编译关键类型（分析产物，非业务仓库源码）：  
`ModUploader.Program`、`ModUploader.Uploader`、`ModUploader.ModInfo`、`FileHelper`、`ModUploader.GuidFrom`

### 1.2 【源码事实】当前 XenoHaven Mod Toolkit

`app/NewModWindow.*`、`app/ModInfoWindow.*`、`app/MainWindow.xaml.cs`、`app/app.csproj`、样例 `main.xml`、`docs/SteamModDevGuide.md`

### 1.3 【待确认】未纳入证据

- `references/steam-workshop/*.plan.md`：未找到  
- 游戏端是否/如何写入 `steamPublishedFileId`：本仓库无游戏上传源码

---

## 2. ID 语义区分（强制）

| 名称 | 含义 | 本工具 | Keplerth | 定位 Workshop Item？ |
|------|------|--------|----------|----------------------|
| **App ID** | Steam 应用 ID | 未找到 | 硬编码 `747200`；旁路文件 `925420` | 创建/更新 API 使用代码里的 `Uploader.AppId`（747200） |
| **Mod ID** | 游戏内容 Mod 基础 ID | `main.xml` `<id>` / `modBaseId` | `GuidFrom` 生成 hash 数字，**未**进 UGC API | **否** |
| **Publisher ID** | 常指“发布者”类 ID | **名称与字段均未找到** | **未找到** | **不适用** |
| **PublishedFileId** | Workshop 作品条目 ID | XML：`steamPublishedFileId`（`long`，新建 `0`） | `PublishedFileId_t` / `m_nPublishedFileId` | **是**（更新主键） |
| **SteamID** | 用户账号 | 未找到 | `SteamUser.GetSteamID()` | 查“我发布的列表”，不是 Item 主键 |
| **Update Handle** | 单次更新会话句柄 | 未找到 | `UGCUpdateHandle_t`（`StartItemUpdate` 返回） | 仅当次会话 |

### 2.1 Publisher ID ≠ PublishedFileId

**【源码事实】** 两处代码均无 Publisher ID。  
本工具槽位 **`steamPublishedFileId`** 语义 = **PublishedFileId**。口头称 Publisher ID 视为错误命名，实现禁止沿用。

### 2.2 Mod ID 不参与 Workshop 定位

**【源码事实 · Keplerth】** `CreateItem` / `StartItemUpdate` 只用 `AppId` + `PublishedFileId`（见 `ModInfo.CreateItem`、`Publish_Click`、`OnCreateItemResult`）。  
**【源码事实 · 本工具】** `<id>` 与 Steam API 无调用关系。

---

## 3. 【源码事实】Keplerth：技术栈与账号

### 3.1 上传通道

| 结论 | 证据 |
|------|------|
| 使用 Steamworks.NET → `steam_api` / UGC | `Program.Main`：`SteamAPI.Init`；`ModInfo`：`SteamUGC.*`；目录内 `Steamworks.NET.dll`、`steam_api*.dll` |
| 主路径未见 SteamCMD / Web API / 外部上传 exe | 反编译无对应调用；`Process.Start` 仅开 URL（协议/帮助） |

### 3.2 App ID（两套数值，勿混为一谈）

| 来源 | 值 | 【源码事实】用途 |
|------|-----|------------------|
| `Uploader` 构造硬编码 | `747200` | `AppId = new AppId_t(747200u)`；`CreateItem` / `StartItemUpdate` / `CreateQueryUserUGCRequest(..., AppId, ...)` |
| `steam_appid.txt` | `925420` | 文件存在；**程序代码未读取该文件** |

**【惯例】** Steamworks 在非 Steam 启动时常靠旁路 `steam_appid.txt` 决定 Init 所属 App。  
本游戏正式 App ID 为 **`3461270`**（已写入工具）；Keplerth 参考实现中的 `747200` / `925420` **不要照抄**。

### 3.3 账号身份

| 步骤 | 证据 |
|------|------|
| Init 成功才进 UI；失败静默退出 | `Program.Main`：`if (SteamAPI.Init()) { ... }`，无 else 提示 |
| 取当前用户 | `Uploader` 构造：`UserId = SteamUser.GetSteamID()` |
| 显示名 | `SteamFriends.GetFriendPersonaName(UserId)` |
| 回调泵 | `Program.RunSteam`：每 4s `SteamAPI.RunCallbacks()` |

→ **本机已登录 Steam 客户端会话**；无账密、无 token 文件。

---

## 4. 【源码事实】Keplerth：首次发布流程

### 4.1 端到端步骤

```text
Program.Main
  → SteamAPI.Init() 成功 → Uploader 主窗 → QueryAllMods()
  → Add（AddNewMod_Click）
       IsAddNewMod = true
       ModInfo.OnDialogShow()：默认勾选 Update Data/Image/Info/Tags/Visibility
       ShowDialog(ModInfo)
  →（可选）Browse 内容目录（dataBrowse_Click）
       提示 Copying…；DeleteFolder(StartupPath\Upload Folder\)
       CopyDirectory(所选路径 → StartupPath\Upload Folder\{末级文件夹名})
       dataPath.Text = StartupPath\Upload Folder   ← 注意：设为根，不是子目录
       dataPath_TextChanged：若找到 …\About\Preview.png 则填 ImagePath
  → Publish（Publish_Click）
       按勾选校验目录/预览文件/标题/版本非空
       Close；SetButtonEnableFalse()
       CreateItem()
  → SteamUGC.CreateItem(AppId, EWorkshopFileType.k_EWorkshopFileTypeFirst)
  → OnCreateItemResult（注意：仅 Console.WriteLine(m_eResult)，未因失败 return）
       UGCUpdateHandle = StartItemUpdate(AppId, pCallback.m_nPublishedFileId)
       若需协议 → 打开 workshoplegalagreement URL
       SubmitUpdate()
  → SubmitUpdate
       勾选则 SetItemContent / SetItemPreview / SetItemTitle+Description
       SetTags（Language|Mod + version 文本）；SetVisibility
       SubmitItemUpdate(handle, "UpdateNewDate")
       IsUpdating=true；ThreadSetUpdateProgress
  → OnSubmitItemUpdateResult
       Log「Result:…」；恢复按钮；ThreadQueryAllMods()
```

### 4.2 首次流程中的 PublishedFileId

| 步骤 | 行为 | 写入本地 Mod/配置？ |
|------|------|---------------------|
| `CreateItemResult_t.m_nPublishedFileId` | 传入 `StartItemUpdate` | **否** |
| 提交后 | 仅刷新内存列表 `ModsData` | **否** |

**【源码事实】** Keplerth **不**把 PublishedFileId 写入任何 Mod 文件。

### 4.3 实现时须知晓的缺陷（仍属源码观察）

- `OnCreateItemResult` **不判断** `m_eResult` / `bIOFailure` 即继续更新——本工具实现时应先判成功再 `StartItemUpdate`。  
- `dataPath` 赋值为 `Upload Folder` 根路径：依赖复制后目录结构；属参考实现细节，勿盲搬。

---

## 5. 【源码事实】Keplerth：已有作品更新流程

### 5.1 端到端步骤

```text
QueryAllMods()（构造时与多数刷新）
  → CreateQueryUserUGCRequest(
       SteamUser.GetSteamID().GetAccountID(),
       k_EUserUGCList_Published,
       k_EUGCMatchingUGCType_Items,
       CreationOrderDesc,
       SteamUtils.GetAppID(),   // creator App
       Uploader.AppId,          // consumer App = 747200
       page=1)
  → SetLanguage(schinese)；SendQueryUGCRequest
  → OnSteamUGCQueryCompleted：GetQueryUGCResult → ModsData；ListView 显示 PublishedFileId

用户选中行（Mods_SelectedIndexChanged）
  → Uploader.PublishedFileId = 该项 m_nPublishedFileId
  → 详情区展示 Title/Description/PublishedFileId/时间等

Edit（EditData_Click）
  → 要求 PublishedFileId.m_PublishedFileId != 0
  → IsAddNewMod = false
  → OnDialogShow()：默认不勾选各 Update*；预填 Title/Description
  → ShowDialog(ModInfo)

Publish_Click
  → 不 CreateItem
  → StartItemUpdate(AppId, Uploader.PublishedFileId)
  → SubmitUpdate()（同首次的 SetItem* + SubmitItemUpdate）
```

### 5.2 Refresh 变体（勿与主查询混用）

**【源码事实】** 按钮 `button1` → `QueryAllModsSelf()`：  
`CreateQueryUserUGCRequest(..., AppId_t.Invalid, SteamUtils.GetAppID(), 1)`——参数与 `QueryAllMods` **不同**。本工具若做校验查询，须明确用哪套 App 参数，不能照抄两个入口而不分辨。

### 5.3 更新时 PublishedFileId 从哪读

**【源码事实】** Steam 已发布列表 → 选中项 → 静态字段 `Uploader.PublishedFileId` → `StartItemUpdate`。  
不是本地文件，不是 Mod ID。

### 5.4 首次 vs 更新判定

**【源码事实】** `Uploader.IsAddNewMod`：`true`→`CreateItem()`；`false`→`StartItemUpdate(..., PublishedFileId)`（`Publish_Click`）。

---

## 6. 【源码事实】Keplerth 已证实调用的 Steam API（白名单）

仅下列出现在主路径反编译中；**未列出的 API 不得写成“Keplerth 已使用”。**

| API | 位置 |
|-----|------|
| `SteamAPI.Init` / `RunCallbacks` / `Shutdown` | `Program` |
| `SteamUser.GetSteamID` | `Uploader` |
| `SteamFriends.GetFriendPersonaName`；`GetPersonaName`（UserInfo 调试） | `Uploader` |
| `SteamUtils.GetAppID` | `QueryAllMods` / `QueryAllModsSelf` |
| `SteamUGC.CreateQueryUserUGCRequest` | 同上 |
| `SteamUGC.SetLanguage` | `QueryAllMods`；查询回调内 |
| `SteamUGC.SendQueryUGCRequest` | `QueryAllMods` |
| `SteamUGC.GetQueryUGCResult` | `OnSteamUGCQueryCompleted` |
| `SteamUGC.CreateItem` | `ModInfo.CreateItem` |
| `SteamUGC.StartItemUpdate` | `OnCreateItemResult`；`Publish_Click`（更新） |
| `SteamUGC.SetItemContent` / `SetItemPreview` / `SetItemTitle` / `SetItemDescription` | `SubmitUpdate` |
| `SteamUGC.SetItemTags` / `SetItemVisibility` | `SetTags` / `SetVisibility` |
| `SteamUGC.SubmitItemUpdate` | `SubmitUpdate` |
| `SteamUGC.GetItemUpdateProgress` | `SetUpdateProgress` |
| `SteamUGC.DeleteItem` | `DeleteMod_Click` |

异步：`CallResult<T>.Create` / `.Set`（CreateItem / Submit / Query / Delete）。

**【源码事实】** 主上传路径未见 SteamCMD、HTTP Web API、以 `ISteamRemoteStorage` Publish 系列为通道。

---

## 7. PublishedFileId 的保存：事实对照 vs 设计建议

### 7.1 【源码事实】对照表（只描述已发生行为）

| 场景 | Keplerth | 本工具现状 |
|------|----------|------------|
| CreateItem 回调拿到 ID | 仅用于 `StartItemUpdate`，不写文件 | Submit **成功**后再写入 `main.xml` |
| Submit 完成 | 刷新 Steam 列表，不写文件 | 写回 `steamPublishedFileId` 并刷新总览 |
| 本地槽位 | 无 | `main.xml` 有 `steamPublishedFileId`，新建为 `0`；上传成功由工具写入 |
| 文档称“游戏会更新该字段” | — | 仅 `SteamModDevGuide.md` 陈述 → **【待确认】** |

### 7.2 【设计建议】本工具应在何时写入 `steamPublishedFileId`

> 非现有行为。

| 方案 | 时机 | 取舍 |
|------|------|------|
| **推荐** | `OnSubmitItemUpdateResult` 且 `m_eResult` 成功后写入（新建用本次会话已持有的 PublishedFileId） | 避免半发布；与“可更新”一致 |
| 最小可用 | `CreateItem` 回调判成功后立即写入 | 可重试更新，但可能留下未完成内容的 ID |
| 禁止 | 用 Mod ID/随机数冒充；Steam 未成功前写非 0 并当已发布 | — |

**判定分支建议：** `steamPublishedFileId == 0` → 首次；`!= 0` → 更新（并可选向 Steam 校验归属）。

---

## 8. 【源码事实】当前 XenoHaven Mod Toolkit 现状

| 能力 | 现状 | 证据 |
|------|------|------|
| Steam 账号集成 | **有**：启动 `SteamAPI.InitEx`，显示 PersonaName / SteamID / AppID；后台 `RunCallbacks`；失败弹窗 + 重连 | `app/SteamSession.cs`、`MainWindow` 顶栏 |
| App ID | 常量 `SteamAppIds.XenoHaven` + `steam_appid.txt`（**`3461270`**） | `app/SteamAppIds.cs`、`app/steam_appid.txt` |
| Workshop 上传 | **有**：`CreateItem` / `StartItemUpdate` / `SetItem*` / `SubmitItemUpdate`；进度轮询；成功写回 `steamPublishedFileId` | `SteamWorkshopPublisher`、`WorkshopUploadWindow`、主窗「上传工坊」 |
| Mod ID | 有，`<id>` | `NewModWindow` / `ModInfoWindow` |
| PublishedFileId 槽位 | 有，`steamPublishedFileId`，默认 0；**上传成功后由工具写入** | 同上；`WorkshopUploadWindow` |
| Publisher ID | 无 | 全库无符号 |

本地编辑 Mod 与 Steam 会话解耦：Steam 未连接时仍可编辑；「上传工坊」需已连接 Steam。

---

## 9. 【设计建议】后续 Steam 账号集成清单

> 对齐 Keplerth 已验证模式，并避开其缺陷。

1. 要求用户本机 **Steam 客户端运行且已登录**。  
2. 上传模块启动时 `SteamAPI.Init()`（或等价）；**失败必须弹可读错误**（勿静默）。  
3. 将 **`steam_appid.txt`（本游戏 App ID = `3461270`）** 与代码内 `SteamAppIds.XenoHaven` **设为同一值**，放在 exe 旁。  
4. `SteamUser.GetSteamID()` + 显示 PersonaName，确认发布身份。  
5. 独立线程或定时泵 **`SteamAPI.RunCallbacks()`**（否则 CallResult 不触发）。  
6. 不采用账密 UI；不把 SteamCMD 当默认账号方案。  
7. 协议：检测/引导 Workshop Legal Agreement URL（Keplerth：`OnCreateItemResult` / 链接控件）。

---

## 10. 【设计建议】后续 MOD 上传实现清单

### 10.1 与本工具资源的建议映射

| Workshop 字段 | 建议来源（本工具） | 说明 |
|---------------|-------------------|------|
| Title | `main.xml` `<name>` | — |
| Description | `main.xml` `<description>` | — |
| Preview | 优先 `screenshot.png`，否则 `icon.png` | Keplerth 用独立预览路径；勿假设 `About\Preview.png` |
| Content 目录 | Mod 根目录，或“导出发布目录”（若日后实现） | 产品定是否排除 `.meta` |
| 本地 PublishedFileId | `main.xml` `<steamPublishedFileId>` | 上传成功后由**工具**写入（建议），勿等待未证实的“游戏写回” |
| Mod ID `<id>` | 不变 | 不参与 Steam 定位 |

### 10.2 首次发布（建议步骤）

1. 校验 Mod 与 `main.xml`（name/description/图标等）。  
2. 若 `steamPublishedFileId != 0`，改走更新，勿再 `CreateItem`。  
3. `CreateItem(appId, Community/First)`；**检查回调 `m_eResult`**。  
4. `StartItemUpdate(appId, publishedFileId)`。  
5. `SetItemTitle` / `SetItemDescription` / `SetItemPreview` / `SetItemContent`（按需 Tags/Visibility）。  
6. `SubmitItemUpdate` + 进度（`GetItemUpdateProgress`）+ `RunCallbacks`。  
7. Submit **成功**后写入并保存 `steamPublishedFileId`。  
8. `<id>` 保持不变。

### 10.3 更新（建议步骤）

1. 读 `steamPublishedFileId`；为 0 则转首次。  
2. （可选）查询当前用户已发布列表，确认 ID 仍属于自己。  
3. `StartItemUpdate(appId, publishedFileId)`。  
4. 按需 `SetItem*`（可只更新内容）。  
5. `SubmitItemUpdate`；成功后 ID 通常不变，无需改 Mod ID。

### 10.4 建议伪代码（分支）

```text
id = read main.xml steamPublishedFileId
if id == 0:
    CreateItem → (check result) → StartItemUpdate(newId) → SetItem* → Submit
    on submit OK: write steamPublishedFileId = newId
else:
    StartItemUpdate(id) → SetItem* → Submit
```

### 10.5 明确不要做

- Mod ID ≠ PublishedFileId；不要发明未定义的 Publisher ID 字段名。  
- 不要照搬 Keplerth 的 `747200`、`Alpha 15` 标签、`About\Preview.png`、`Upload Folder` 赋值方式。  
- 不要把 §7.2/§9/§10 写成“已经实现”。  
- 不要调用 §6 白名单之外的 API 却标注为“Keplerth 已验证”。

---

## 11. 已知矛盾与待确认

| 项 | 说明 | 状态 |
|----|------|------|
| Keplerth `747200` vs 其旁路文件 `925420` | 仅参考上传器历史现象 | 与本游戏无关 |
| 本游戏 App ID | **`3461270`**（已确认） | 已写入 `SteamAppIds` / `steam_appid.txt` |
| 创意工坊介绍页 | https://steamcommunity.com/workshop/about/?appid=3461270 | 已写入 `SteamAppIds.WorkshopAboutUrl` |
| Submit `k_EResultInvalidParam` | 常见：Workshop General 未启用 ISteamUGC 文件传输 / 无 workshop depot；或本机 `appinfo.vdf` 缓存过期。CreateItem 成功≠内容上传通道已通 | 见 Steam `logs\Workshop_log.txt` |
| “游戏写入 steamPublishedFileId” | 仅工具文档 | 【待确认】 |
| Publisher ID | 代码中不存在 | 若业务坚持该词，需另定义并改名存储 |
| plan.md | 缺失 | 补文件后需再审 |

**已消除的文档内矛盾：** §7 不再把“应保存”标成源码事实；`steam_appid.txt` 作用改为惯例/待确认。

---

## 12. 压缩结论

| 类型 | 结论 |
|------|------|
| 【源码事实】 | 参考实现 = Steamworks.NET UGC；首次 `CreateItem`→`StartItemUpdate`→`SetItem*`→`SubmitItemUpdate`；更新用已有 `PublishedFileId`；Keplerth 不落盘 ID；本工具已实现上传并写回 `steamPublishedFileId` |
| 【源码事实】 | Mod ID ≠ PublishedFileId；无 Publisher ID |
| 【设计建议】 | 账号 = 本机 Steam 会话 + Init + 统一 App ID + RunCallbacks；上传成功后由工具写回 `steamPublishedFileId`；用该字段分支首次/更新 |
| 【已确认】 | 本游戏 App ID = **`3461270`**；工坊介绍页 = `workshop/about/?appid=3461270` |
| 【待确认】 | 游戏是否写回 `steamPublishedFileId` |

---

## 附录 A. Keplerth 符号索引

| 符号 | 反编译文件 |
|------|------------|
| `Main` / `RunSteam` / `Exit` | `ModUploader/Program.cs` |
| `AppId` / `PublishedFileId` / `IsAddNewMod` / `QueryAllMods` / `QueryAllModsSelf` | `ModUploader/Uploader.cs` |
| `CreateItem` / `Publish_Click` / `SubmitUpdate` / `OnCreateItemResult` / `OnSubmitItemUpdateResult` / `dataBrowse_Click` | `ModUploader/ModInfo.cs` |
| `CopyDirectory` / `DeleteFolder` | `FileHelper.cs` |

## 附录 B. 本工具符号索引

| 符号 | 文件 |
|------|------|
| `modBaseId` / `steamPublishedFileId` | `app/NewModWindow.xaml.cs`、`app/ModInfoWindow.xaml.cs` |
| `TryDetermineCurrentModBaseId` | `app/MainWindow.xaml.cs` |
| 字段说明（含“游戏更新”陈述） | `docs/SteamModDevGuide.md` |
