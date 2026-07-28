using System.Diagnostics;
using System.IO;
using Steamworks;

namespace XenoHavenModToolkit;

internal sealed class WorkshopPublishRequest
{
    public required uint AppId { get; init; }
    public required ulong ExistingPublishedFileId { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string ContentFolder { get; init; }
    public required string PreviewFilePath { get; init; }
    public required string ChangeNote { get; init; }
    public ERemoteStoragePublishedFileVisibility Visibility { get; init; }
        = ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic;
    public bool ExcludeMetaFiles { get; init; } = true;
}

internal sealed class WorkshopPublishProgress
{
    public required string Phase { get; init; }
    public EItemUpdateStatus Status { get; init; }
    public ulong BytesProcessed { get; init; }
    public ulong BytesTotal { get; init; }
}

internal sealed class WorkshopPublishResult
{
    public bool Success { get; init; }
    public ulong PublishedFileId { get; init; }
    public bool WasCreate { get; init; }
    public bool NeedsLegalAgreement { get; init; }
    public string? ErrorMessage { get; init; }
    public string? PreviewUsedPath { get; init; }

    public string? WorkshopItemUrl
        => PublishedFileId == 0
            ? null
            : $"https://steamcommunity.com/sharedfiles/filedetails/?id={PublishedFileId}";
}

/// <summary>
/// Steam Workshop 上传：CreateItem / StartItemUpdate / SetItem* / SubmitItemUpdate。
/// 依赖已启动的 <see cref="SteamSession"/>（含 RunCallbacks）。
/// </summary>
internal static class SteamWorkshopPublisher
{
    private const int TitleMaxLength = 128;
    private const int DescriptionMaxLength = 8000;
    /// <summary>Steam 预览图上限约 1MB；超限常见为 LimitExceeded，仍应主动规避。</summary>
    private const long MaxPreviewBytes = 1L * 1024 * 1024;
    private const long MinPreviewBytes = 16;

    public static async Task<WorkshopPublishResult> PublishAsync(
        WorkshopPublishRequest request,
        IProgress<WorkshopPublishProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Fail("标题不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return Fail("简介不能为空。");
        }

        if (!Directory.Exists(request.ContentFolder))
        {
            return Fail($"内容目录不存在：{request.ContentFolder}");
        }

        if (!File.Exists(request.PreviewFilePath))
        {
            return Fail($"预览图不存在：{request.PreviewFilePath}");
        }

        string? stagingFolder = null;
        ulong publishedFileId = request.ExistingPublishedFileId;
        var wasCreate = request.ExistingPublishedFileId == 0;
        var needsLegal = false;

        try
        {
            var contentFolder = request.ContentFolder;
            if (request.ExcludeMetaFiles)
            {
                progress?.Report(new WorkshopPublishProgress { Phase = "正在准备上传内容（排除 .meta）…" });
                stagingFolder = StageContentExcludingMeta(request.ContentFolder);
                contentFolder = stagingFolder;
            }

            if (!TryResolvePreviewForUpload(request.ContentFolder, request.PreviewFilePath, out var previewPath, out var previewError))
            {
                return Fail(previewError ?? "预览图无效。");
            }

            var appId = new AppId_t(request.AppId);

            if (wasCreate)
            {
                progress?.Report(new WorkshopPublishProgress { Phase = "正在创建创意工坊条目…" });
                var createCall = SteamUGC.CreateItem(appId, EWorkshopFileType.k_EWorkshopFileTypeCommunity);
                var (createResult, createIoFailure) =
                    await AwaitCallResultAsync<CreateItemResult_t>(createCall, cancellationToken).ConfigureAwait(false);

                if (createIoFailure)
                {
                    return Fail("创建条目时 Steam API 通信失败。");
                }

                if (createResult.m_eResult != EResult.k_EResultOK)
                {
                    return Fail(FormatCreateError(createResult.m_eResult));
                }

                publishedFileId = createResult.m_nPublishedFileId.m_PublishedFileId;
                if (publishedFileId == 0)
                {
                    return Fail("创建条目成功但未返回 PublishedFileId。");
                }

                needsLegal |= createResult.m_bUserNeedsToAcceptWorkshopLegalAgreement;
            }

            progress?.Report(new WorkshopPublishProgress
            {
                Phase = wasCreate ? "正在提交首次内容…" : "正在提交更新…"
            });

            var updateHandle = SteamUGC.StartItemUpdate(appId, new PublishedFileId_t(publishedFileId));
            if (updateHandle == UGCUpdateHandle_t.Invalid)
            {
                return Fail("StartItemUpdate 返回无效句柄。", publishedFileId, wasCreate, needsLegal, previewPath);
            }

            var title = Truncate(request.Title.Trim(), TitleMaxLength);
            var description = Truncate(request.Description.Trim(), DescriptionMaxLength);

            if (!SteamUGC.SetItemTitle(updateHandle, title))
            {
                return Fail("SetItemTitle 失败。", publishedFileId, wasCreate, needsLegal, previewPath);
            }

            if (!SteamUGC.SetItemDescription(updateHandle, description))
            {
                return Fail("SetItemDescription 失败。", publishedFileId, wasCreate, needsLegal, previewPath);
            }

            if (!SteamUGC.SetItemVisibility(updateHandle, request.Visibility))
            {
                return Fail("SetItemVisibility 失败。", publishedFileId, wasCreate, needsLegal, previewPath);
            }

            if (!SteamUGC.SetItemPreview(updateHandle, previewPath))
            {
                return Fail("SetItemPreview 失败。", publishedFileId, wasCreate, needsLegal, previewPath);
            }

            if (!SteamUGC.SetItemContent(updateHandle, contentFolder))
            {
                return Fail("SetItemContent 失败。", publishedFileId, wasCreate, needsLegal, previewPath);
            }

            var changeNote = string.IsNullOrWhiteSpace(request.ChangeNote)
                ? (wasCreate ? "Initial upload" : "Update")
                : request.ChangeNote.Trim();

            var submitCall = SteamUGC.SubmitItemUpdate(updateHandle, changeNote);
            var submitTask = AwaitCallResultAsync<SubmitItemUpdateResult_t>(submitCall, cancellationToken);

            while (!submitTask.IsCompleted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = SteamUGC.GetItemUpdateProgress(updateHandle, out var processed, out var total);
                progress?.Report(new WorkshopPublishProgress
                {
                    Phase = DescribeStatus(status),
                    Status = status,
                    BytesProcessed = processed,
                    BytesTotal = total
                });
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }

            var (submitResult, submitIoFailure) = await submitTask.ConfigureAwait(false);
            if (submitIoFailure)
            {
                return Fail("提交更新时 Steam API 通信失败。", publishedFileId, wasCreate, needsLegal, previewPath);
            }

            if (submitResult.m_eResult != EResult.k_EResultOK)
            {
                return Fail(
                    FormatSubmitError(submitResult.m_eResult),
                    publishedFileId,
                    wasCreate,
                    needsLegal | submitResult.m_bUserNeedsToAcceptWorkshopLegalAgreement,
                    previewPath);
            }

            if (submitResult.m_nPublishedFileId.m_PublishedFileId != 0)
            {
                publishedFileId = submitResult.m_nPublishedFileId.m_PublishedFileId;
            }

            needsLegal |= submitResult.m_bUserNeedsToAcceptWorkshopLegalAgreement;

            progress?.Report(new WorkshopPublishProgress { Phase = "上传完成。" });

            return new WorkshopPublishResult
            {
                Success = true,
                PublishedFileId = publishedFileId,
                WasCreate = wasCreate,
                NeedsLegalAgreement = needsLegal,
                PreviewUsedPath = previewPath
            };
        }
        catch (OperationCanceledException)
        {
            return Fail("上传已取消。", publishedFileId, wasCreate, needsLegal);
        }
        catch (Exception ex)
        {
            return Fail(ex.Message, publishedFileId, wasCreate, needsLegal);
        }
        finally
        {
            if (stagingFolder is not null)
            {
                TryDeleteDirectory(stagingFolder);
            }
        }
    }

    public static void TryOpenWorkshopLegalAgreement()
    {
        try
        {
            if (SteamUGC.ShowWorkshopEULA())
            {
                return;
            }
        }
        catch
        {
            // fall through to browser URL
        }

        TryOpenUrl("https://steamcommunity.com/workshop/workshoplegalagreement");
    }

    public static void TryOpenWorkshopItem(ulong publishedFileId)
    {
        if (publishedFileId == 0)
        {
            TryOpenWorkshopHub();
            return;
        }

        TryOpenUrl($"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}");
    }

    public static void TryOpenWorkshopHub()
        => TryOpenUrl(SteamAppIds.WorkshopAboutUrl);

    /// <summary>
    /// UI 展示用：优先 screenshot.png（且 ≤1MB），否则 icon.png。
    /// </summary>
    public static string ResolvePreviewPath(string modRoot)
    {
        var screenshot = Path.Combine(modRoot, ModRootAssets.ScreenshotRelativePath);
        if (File.Exists(screenshot) && new FileInfo(screenshot).Length is >= MinPreviewBytes and <= MaxPreviewBytes)
        {
            return screenshot;
        }

        var icon = Path.Combine(modRoot, ModRootAssets.IconRelativePath);
        if (File.Exists(icon))
        {
            return icon;
        }

        return File.Exists(screenshot) ? screenshot : icon;
    }

    private static bool TryResolvePreviewForUpload(
        string modRoot,
        string preferredPath,
        out string previewPath,
        out string? error)
    {
        previewPath = string.Empty;
        error = null;

        foreach (var candidate in EnumeratePreviewCandidates(modRoot, preferredPath))
        {
            if (!File.Exists(candidate))
            {
                continue;
            }

            var length = new FileInfo(candidate).Length;
            if (length < MinPreviewBytes || length > MaxPreviewBytes)
            {
                continue;
            }

            if (!IsSupportedPreviewExtension(candidate))
            {
                continue;
            }

            previewPath = Path.GetFullPath(candidate);
            return true;
        }

        var oversized = Path.Combine(modRoot, ModRootAssets.ScreenshotRelativePath);
        if (File.Exists(oversized) && new FileInfo(oversized).Length > MaxPreviewBytes)
        {
            error =
                $"预览图过大（{new FileInfo(oversized).Length / 1024.0 / 1024.0:F2} MB）。" +
                "Steam 要求预览图 < 1 MB。请压缩 screenshot.png，或确保 icon.png 可用作预览。";
            return false;
        }

        error = "未找到可用的预览图（需要 png/jpg，且大小在 16 字节～1 MB 之间）。";
        return false;
    }

    private static IEnumerable<string> EnumeratePreviewCandidates(string modRoot, string preferredPath)
    {
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            yield return preferredPath;
        }

        yield return Path.Combine(modRoot, ModRootAssets.ScreenshotRelativePath);
        yield return Path.Combine(modRoot, ModRootAssets.IconRelativePath);
    }

    private static bool IsSupportedPreviewExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return string.Equals(ext, ".png", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static WorkshopPublishResult Fail(
        string message,
        ulong publishedFileId = 0,
        bool wasCreate = false,
        bool needsLegal = false,
        string? previewUsedPath = null)
        => new()
        {
            Success = false,
            ErrorMessage = message,
            PublishedFileId = publishedFileId,
            WasCreate = wasCreate,
            NeedsLegalAgreement = needsLegal,
            PreviewUsedPath = previewUsedPath
        };

    private static string FormatCreateError(EResult result)
        => result switch
        {
            EResult.k_EResultAccessDenied =>
                "创建条目失败：k_EResultAccessDenied。通常表示该 App 尚未在 Steamworks 开通创意工坊，或当前账号无发布权限。",
            EResult.k_EResultInsufficientPrivilege =>
                "创建条目失败：权限不足。请确认已接受创意工坊协议，且账号有该 App 的工坊发布权限。",
            _ => $"创建条目失败：{result}"
        };

    private static string FormatSubmitError(EResult result)
        => result switch
        {
            EResult.k_EResultInvalidParam =>
                "提交更新失败：k_EResultInvalidParam。\n\n" +
                "常见原因（按优先级）：\n" +
                "1. Steamworks → Workshop → General 未启用 ISteamUGC 文件传输（workshop depot）\n" +
                "2. 本机 Steam 缓存过期：可退出 Steam 后删除 Steam\\appcache\\appinfo.vdf 再登录\n" +
                "3. 预览图无效（过小 <16 字节）；详见 Steam\\logs\\Workshop_log.txt\n\n" +
                "说明：CreateItem 成功但 Submit 带内容失败时，多半是「工坊页面已有 / 可创建空条目」，但内容上传通道未配全。",
            EResult.k_EResultLimitExceeded =>
                "提交更新失败：k_EResultLimitExceeded。预览图必须 < 1 MB，或 Steam Cloud 配额不足。",
            EResult.k_EResultFileNotFound =>
                "提交更新失败：k_EResultFileNotFound。请检查预览图路径与内容目录是否可读。",
            _ => $"提交更新失败：{result}"
        };

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    private static string DescribeStatus(EItemUpdateStatus status)
        => status switch
        {
            EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => "准备配置…",
            EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => "准备内容…",
            EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => "上传内容…",
            EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => "上传预览图…",
            EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => "提交变更…",
            _ => "上传中…"
        };

    private static async Task<(T Result, bool IoFailure)> AwaitCallResultAsync<T>(
        SteamAPICall_t apiCall,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<(T, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var callResult = CallResult<T>.Create((result, ioFailure) =>
        {
            tcs.TrySetResult((result, ioFailure));
        });
        callResult.Set(apiCall);

        await using var registration = cancellationToken.Register(() =>
        {
            callResult.Cancel();
            tcs.TrySetCanceled(cancellationToken);
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    private static string StageContentExcludingMeta(string modRoot)
    {
        var stagingRoot = Path.Combine(
            Path.GetTempPath(),
            "XenoHavenModTool",
            "workshop-upload",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        CopyDirectoryExcludingMeta(modRoot, stagingRoot);
        return stagingRoot;
    }

    private static void CopyDirectoryExcludingMeta(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyDirectoryExcludingMeta(dir, Path.Combine(destDir, name));
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // ignore temp cleanup failures
        }
    }

    private static void TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }
}
