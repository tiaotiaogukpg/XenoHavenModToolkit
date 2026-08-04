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
        ModeText.Text = isCreate ? Loc.Get("Str.Workshop.ModeCreate") : Loc.Get("Str.Workshop.ModeUpdate");
        PublishedFileIdBox.Text = isCreate ? Loc.Get("Str.Workshop.PublishedFileIdNew") : publishedFileId.ToString();
        TitleBox.Text = title;
        DescriptionBox.Text = description;
        ChangeNoteBox.Text = isCreate ? "Initial upload" : "Update";
        PreviewPathBox.Text = SteamWorkshopPublisher.ResolvePreviewPath(modRoot);
        ContentPathBox.Text = modRoot;
        OpenItemButton.IsEnabled = true;
        OpenItemButton.Content = isCreate ? Loc.Get("Str.Workshop.OpenHub") : Loc.Get("Str.Workshop.OpenItem");
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
            System.Windows.MessageBox.Show(this, Loc.Get("Str.Workshop.TitleRequired"), Loc.Get("Str.Workshop.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            System.Windows.MessageBox.Show(this, Loc.Get("Str.Workshop.DescriptionRequired"), Loc.Get("Str.Workshop.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var previewPath = PreviewPathBox.Text.Trim();
        if (!File.Exists(previewPath))
        {
            System.Windows.MessageBox.Show(this, Loc.Format("Str.Workshop.PreviewMissing", previewPath), Loc.Get("Str.Workshop.Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SetBusy(true);
        StatusText.Text = string.Empty;
        ProgressText.Text = Loc.Get("Str.Workshop.Starting");
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
                ProgressText.Text = Loc.Get("Str.Workshop.UploadFailed");
                StatusText.Text = result.ErrorMessage ?? Loc.Get("Str.UnknownReason");

                // CreateItem 已成功但 Submit 失败时，仍写回 ID，避免下次再 Create 出空条目。
                if (result.PublishedFileId != 0)
                {
                    publishedFileId = result.PublishedFileId;
                    PublishedFileIdBox.Text = publishedFileId.ToString();
                    ModeText.Text = Loc.Get("Str.Workshop.ModeUpdate");
                    OpenItemButton.Content = Loc.Get("Str.Workshop.OpenItem");
                    OpenItemButton.ToolTip =
                        $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
                    ChangeNoteBox.Text = "Update";

                    if (TryWritePublishedFileId(publishedFileId, out var writeError))
                    {
                        uploadSucceeded = true;
                        StatusText.Text += Loc.Format("Str.Workshop.PartialCreateSaved", publishedFileId);
                    }
                    else
                    {
                        StatusText.Text += Loc.Format("Str.Workshop.PartialCreateWriteFail", publishedFileId, writeError ?? string.Empty);
                    }
                }

                if (!string.IsNullOrWhiteSpace(result.PreviewUsedPath))
                {
                    PreviewPathBox.Text = result.PreviewUsedPath;
                }

                System.Windows.MessageBox.Show(
                    this,
                    StatusText.Text,
                    Loc.Get("Str.Workshop.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            publishedFileId = result.PublishedFileId;
            PublishedFileIdBox.Text = publishedFileId.ToString();
            ModeText.Text = Loc.Get("Str.Workshop.ModeUpdate");
            OpenItemButton.IsEnabled = true;
            OpenItemButton.Content = Loc.Get("Str.Workshop.OpenItem");
            OpenItemButton.ToolTip =
                $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}";
            ChangeNoteBox.Text = "Update";

            if (!TryWritePublishedFileId(publishedFileId, out var error))
            {
                StatusText.Text = Loc.Format("Str.Workshop.CreateOkWriteFail", publishedFileId, error ?? string.Empty);
                System.Windows.MessageBox.Show(
                    this,
                    StatusText.Text,
                    Loc.Get("Str.Workshop.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                uploadSucceeded = true;
                StatusText.Text = result.WasCreate
                    ? Loc.Format("Str.Workshop.CreateSuccess", publishedFileId)
                    : Loc.Format("Str.Workshop.UpdateSuccess", publishedFileId);
            }

            ProgressBar.IsIndeterminate = false;
            ProgressBar.Value = 100;
            ProgressText.Text = Loc.Get("Str.Workshop.Done");

            if (result.NeedsLegalAgreement)
            {
                var agree = System.Windows.MessageBox.Show(
                    this,
                    Loc.Get("Str.Workshop.LegalPrompt"),
                    Loc.Get("Str.Workshop.LegalTitle"),
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
                    Loc.Get("Str.Workshop.Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            ProgressBar.IsIndeterminate = false;
            ProgressText.Text = Loc.Get("Str.Workshop.UploadFailed");
            StatusText.Text = ex.Message;
            System.Windows.MessageBox.Show(this, ex.Message, Loc.Get("Str.Workshop.Title"), MessageBoxButton.OK, MessageBoxImage.Error);
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
