using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Steamworks;

namespace XenoHavenModToolkit;

public partial class WorkshopUploadWindow : Window
{
    private readonly string modRoot;
    private readonly string mainXmlPath;
    private readonly uint appId;
    private ulong publishedFileId;
    private string mainXmlText;
    private CancellationTokenSource? uploadCts;
    private bool uploadSucceeded;

    public string? UpdatedMainXml { get; private set; }
    public ulong ResultPublishedFileId => publishedFileId;
    public bool UploadSucceeded => uploadSucceeded;

    public WorkshopUploadWindow(string modRoot, string mainXmlText, uint appId)
    {
        InitializeComponent();
        this.modRoot = modRoot;
        this.mainXmlText = mainXmlText;
        this.appId = appId;
        mainXmlPath = Path.Combine(modRoot, "main.xml");

        PrefillFromMainXml();
    }

    private void PrefillFromMainXml()
    {
        string title = string.Empty;
        string description = string.Empty;
        publishedFileId = 0;

        try
        {
            var doc = XDocument.Parse(mainXmlText);
            if (doc.Root?.Name.LocalName == "defs")
            {
                title = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value?.Trim() ?? string.Empty;
                description = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "description")?.Value?.Trim() ?? string.Empty;
                var idText = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "steamPublishedFileId")?.Value;
                if (ulong.TryParse(idText?.Trim(), out var id))
                {
                    publishedFileId = id;
                }
            }
        }
        catch
        {
            // keep defaults; upload will fail validation
        }

        var isCreate = publishedFileId == 0;
        ModeText.Text = isCreate ? "首次发布（CreateItem）" : "更新已有条目（StartItemUpdate）";
        PublishedFileIdBox.Text = isCreate ? "0（上传成功后写入）" : publishedFileId.ToString();
        TitleBox.Text = title;
        DescriptionBox.Text = description;
        ChangeNoteBox.Text = isCreate ? "Initial upload" : "Update";
        PreviewPathBox.Text = SteamWorkshopPublisher.ResolvePreviewPath(modRoot);
        ContentPathBox.Text = modRoot;
        OpenItemButton.IsEnabled = true;
        OpenItemButton.Content = isCreate ? "打开工坊总览" : "打开工坊页";
        OpenItemButton.ToolTip = isCreate
            ? SteamAppIds.WorkshopAboutUrl
            : $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (uploadCts is not null)
        {
            return;
        }

        var title = TitleBox.Text.Trim();
        var description = DescriptionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            System.Windows.MessageBox.Show(this, "标题不能为空。", "上传到创意工坊", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            System.Windows.MessageBox.Show(this, "简介不能为空。", "上传到创意工坊", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previewPath = PreviewPathBox.Text.Trim();
        if (!File.Exists(previewPath))
        {
            System.Windows.MessageBox.Show(this, $"预览图不存在：{previewPath}", "上传到创意工坊", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        StatusText.Text = string.Empty;
        ProgressText.Text = "开始上传…";
        ProgressBar.IsIndeterminate = true;
        ProgressBar.Value = 0;

        uploadCts = new CancellationTokenSource();
        var progress = new Progress<WorkshopPublishProgress>(OnProgress);

        try
        {
            var result = await SteamWorkshopPublisher.PublishAsync(
                new WorkshopPublishRequest
                {
                    AppId = appId,
                    ExistingPublishedFileId = publishedFileId,
                    Title = title,
                    Description = description,
                    ContentFolder = modRoot,
                    PreviewFilePath = previewPath,
                    ChangeNote = ChangeNoteBox.Text,
                    Visibility = ReadVisibility(),
                    ExcludeMetaFiles = ExcludeMetaCheck.IsChecked == true
                },
                progress,
                uploadCts.Token);

            if (!result.Success)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressText.Text = "上传失败";
                StatusText.Text = result.ErrorMessage ?? "未知错误";

                // CreateItem 已成功但 Submit 失败时，仍写回 ID，避免下次再 Create 出空条目。
                if (result.PublishedFileId != 0)
                {
                    publishedFileId = result.PublishedFileId;
                    PublishedFileIdBox.Text = publishedFileId.ToString();
                    ModeText.Text = "更新已有条目（StartItemUpdate）";
                    OpenItemButton.Content = "打开工坊页";
                    OpenItemButton.ToolTip =
                        $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
                    ChangeNoteBox.Text = "Update";

                    if (TryWritePublishedFileId(publishedFileId, out var writeError))
                    {
                        uploadSucceeded = true;
                        StatusText.Text +=
                            $"\n\n已创建条目 PublishedFileId={publishedFileId} 并写入 main.xml；修复 Steamworks 配置后请再次「开始上传」以提交内容。";
                    }
                    else
                    {
                        StatusText.Text +=
                            $"\n\n已创建条目 PublishedFileId={publishedFileId}，但写入 main.xml 失败：{writeError}";
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.PreviewUsedPath))
                {
                    PreviewPathBox.Text = result.PreviewUsedPath;
                }

                System.Windows.MessageBox.Show(
                    this,
                    StatusText.Text,
                    "上传到创意工坊",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            publishedFileId = result.PublishedFileId;
            PublishedFileIdBox.Text = publishedFileId.ToString();
            ModeText.Text = "更新已有条目（StartItemUpdate）";
            OpenItemButton.IsEnabled = true;
            OpenItemButton.Content = "打开工坊页";
            OpenItemButton.ToolTip =
                $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
            ChangeNoteBox.Text = "Update";

            if (!TryWritePublishedFileId(publishedFileId, out var error))
            {
                StatusText.Text = $"上传成功，但写回 steamPublishedFileId 失败：{error}";
                System.Windows.MessageBox.Show(
                    this,
                    $"创意工坊上传成功（PublishedFileId={publishedFileId}），但写入 main.xml 失败：{error}",
                    "上传到创意工坊",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                uploadSucceeded = true;
                StatusText.Text = result.WasCreate
                    ? $"首次发布成功。PublishedFileId={publishedFileId}，已写入 main.xml。"
                    : $"更新成功。PublishedFileId={publishedFileId}。";
            }

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            ProgressText.Text = "完成";

            if (result.NeedsLegalAgreement)
            {
                var agree = System.Windows.MessageBox.Show(
                    this,
                    "Steam 要求你接受创意工坊协议后，条目才会完全公开。是否现在打开协议页面？",
                    "创意工坊协议",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);
                if (agree == MessageBoxResult.Yes)
                {
                    SteamWorkshopPublisher.TryOpenWorkshopLegalAgreement();
                }
            }
            else
            {
                System.Windows.MessageBox.Show(
                    this,
                    StatusText.Text,
                    "上传到创意工坊",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressText.Text = "上传失败";
            StatusText.Text = ex.Message;
            System.Windows.MessageBox.Show(this, ex.Message, "上传到创意工坊", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            uploadCts.Dispose();
            uploadCts = null;
            SetBusy(false);
        }
    }

    private void OnProgress(WorkshopPublishProgress progress)
    {
        ProgressText.Text = progress.Phase;
        if (progress.BytesTotal > 0)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = Math.Clamp(100.0 * progress.BytesProcessed / progress.BytesTotal, 0, 100);
        }
        else
        {
            ProgressBar.IsIndeterminate = true;
        }
    }

    private ERemoteStoragePublishedFileVisibility ReadVisibility()
    {
        if (VisibilityBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            return tag switch
            {
                "FriendsOnly" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityFriendsOnly,
                "Private" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
                _ => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic
            };
        }

        return ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic;
    }

    private bool TryWritePublishedFileId(ulong id, out string? error)
    {
        error = null;
        try
        {
            var doc = XDocument.Parse(mainXmlText);
            if (doc.Root is null || doc.Root.Name.LocalName != "defs")
            {
                error = "main.xml 根节点无效。";
                return false;
            }

            var element = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "steamPublishedFileId");
            if (element is null)
            {
                doc.Root.Add(new XElement("steamPublishedFileId", id.ToString()));
            }
            else
            {
                element.Value = id.ToString();
            }

            var serialized = ModXmlFormatter.Serialize(doc);
            ModXmlIO.WriteAllText(mainXmlPath, serialized);
            mainXmlText = serialized;
            UpdatedMainXml = serialized;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private void SetBusy(bool busy)
    {
        UploadButton.IsEnabled = !busy;
        TitleBox.IsEnabled = !busy;
        DescriptionBox.IsEnabled = !busy;
        ChangeNoteBox.IsEnabled = !busy;
        VisibilityBox.IsEnabled = !busy;
        ExcludeMetaCheck.IsEnabled = !busy;
    }

    private void OpenItem_Click(object sender, RoutedEventArgs e)
    {
        SteamWorkshopPublisher.TryOpenWorkshopItem(publishedFileId);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = uploadSucceeded;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        uploadCts?.Cancel();
        base.OnClosed(e);
    }
}
