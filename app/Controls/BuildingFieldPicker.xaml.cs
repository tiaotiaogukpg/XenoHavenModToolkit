using System.Windows;
using System.Windows.Controls;

using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace XenoHavenModToolkit.Controls;

public partial class BuildingFieldPicker : WpfUserControl
{
    private IReadOnlyList<LabeledIdOption> workbenchOptions = [];
    private IReadOnlyList<LabeledIdOption> productionLineOptions = [];
    private IReadOnlyList<LabeledIdOption> simulateOptions = [];
    private bool useFarmingGolemSimulateIds;

    public event EventHandler? TypeChanged;

    public BuildingFieldPicker()
    {
        InitializeComponent();
        BindStringCombo(TypeCombo, BuildingFieldOptions.DefaultTypes);
        BindIntCombo(DirectionCombo, BuildingFieldOptions.DefaultDirections);
        TypeCombo.SelectionChanged += TypeCombo_SelectionChanged;
        ApplyTypeVisibility();
    }

    /// <summary>
    /// Dynamic 编辑器用：切换为生物类型列表与农业傀儡 simulateId；生产地与 Buildings 一样可选任意工作台。
    /// </summary>
    internal void UseDynamicFarmingGolemMode()
    {
        useFarmingGolemSimulateIds = true;
        BindStringCombo(TypeCombo, DynamicFieldOptions.DefaultTypes);
        BindIntCombo(DirectionCombo, DynamicFieldOptions.DefaultDirections);
        WorkbenchIdLabel.Text = Loc.Get("Str.DynamicEditor.ProductionSite");
        WorkbenchIdCombo.IsEnabled = true;
        RefreshSimulateOptions(preserveSelection: false);
        ApplyTypeVisibility();
    }

    /// <summary>
    /// Buildings 编辑器用：恢复建筑类型列表与生产线 simulateId。
    /// </summary>
    internal void UseBuildingMode()
    {
        useFarmingGolemSimulateIds = false;
        BindStringCombo(TypeCombo, BuildingFieldOptions.DefaultTypes);
        BindIntCombo(DirectionCombo, BuildingFieldOptions.DefaultDirections);
        WorkbenchIdLabel.Text = "workbenchId";
        WorkbenchIdCombo.IsEnabled = true;
        RefreshSimulateOptions(preserveSelection: false);
        ApplyTypeVisibility();
    }

    public string SelectedType => TypeCombo.SelectedValue as string ?? string.Empty;

    public int SelectedDirection => DirectionCombo.SelectedItem is int value ? value : 0;

    public int EffectiveDirection => RequiresFixedDirection(SelectedType) ? 1 : SelectedDirection;

    public int SelectedWorkbenchId => WorkbenchIdCombo.SelectedValue is int value ? value : 0;

    public int SelectedSimulateId => SimulateIdCombo.SelectedValue is int value ? value : 0;

    internal void InitializeWorkbenches(GameDataCatalog catalog)
    {
        workbenchOptions = catalog.Workbenches.ToList();
        WorkbenchIdCombo.ItemsSource = workbenchOptions;
        WorkbenchIdCombo.DisplayMemberPath = nameof(LabeledIdOption.Display);
        WorkbenchIdCombo.SelectedValuePath = nameof(LabeledIdOption.Id);
        if (workbenchOptions.Count > 0)
        {
            WorkbenchIdCombo.SelectedValue = workbenchOptions[0].Id;
        }
    }

    internal void InitializeProductionLines(GameDataCatalog catalog)
    {
        productionLineOptions = catalog.ProductionLines.ToList();
        RefreshSimulateOptions(preserveSelection: false);
    }

    public void SetValues(string type, int direction, int workbenchId, int simulateId)
    {
        SelectString(TypeCombo, type);
        SelectInt(DirectionCombo, direction);
        SelectWorkbench(workbenchId);
        RefreshSimulateOptions(preserveSelection: false);
        SelectSimulateId(simulateId);
        ApplyTypeVisibility();
    }

    public bool TryValidate(out string message)
    {
        if (string.IsNullOrWhiteSpace(SelectedType))
        {
            message = "请选择 type。";
            return false;
        }

        if (!RequiresFixedDirection(SelectedType) && SelectedDirection <= 0)
        {
            message = "请选择 direction。";
            return false;
        }

        if (SelectedWorkbenchId <= 0 || workbenchOptions.All(option => option.Id != SelectedWorkbenchId))
        {
            message = useFarmingGolemSimulateIds ? "请选择生产地。" : "请选择 workbenchId。";
            return false;
        }

        if (RequiresSimulateId(SelectedType) &&
            (SelectedSimulateId <= 0 || simulateOptions.All(option => option.Id != SelectedSimulateId)))
        {
            message = useFarmingGolemSimulateIds || DynamicFieldOptions.IsFarmingGolem(SelectedType)
                ? "请选择 simulateId（农业傀儡角色）。"
                : "请选择 simulateId。请确认 DOC/S-生产线定义.xlsx 已加载。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private bool RequiresFixedDirection(string type)
        => useFarmingGolemSimulateIds
            ? DynamicFieldOptions.RequiresFixedDirection(type)
            : BuildingFieldOptions.RequiresFixedDirection(type);

    private bool RequiresSimulateId(string type)
        => useFarmingGolemSimulateIds
            ? DynamicFieldOptions.RequiresSimulateId(type)
            : BuildingFieldOptions.RequiresSimulateId(type);

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshSimulateOptions(preserveSelection: true);
        ApplyTypeVisibility();
        TypeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTypeVisibility()
    {
        var fixedDirection = RequiresFixedDirection(SelectedType);
        if (fixedDirection)
        {
            SelectInt(DirectionCombo, 1);
        }

        DirectionCombo.IsEnabled = !fixedDirection;
        var simulateVisibility = RequiresSimulateId(SelectedType)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SimulateIdLabel.Visibility = simulateVisibility;
        SimulateIdCombo.Visibility = simulateVisibility;
    }

    private void RefreshSimulateOptions(bool preserveSelection)
    {
        var previous = preserveSelection ? SelectedSimulateId : 0;
        if (useFarmingGolemSimulateIds || DynamicFieldOptions.IsFarmingGolem(SelectedType))
        {
            simulateOptions = FarmingGolemOptions.SimulateOptions;
        }
        else
        {
            simulateOptions = productionLineOptions;
        }

        SimulateIdCombo.ItemsSource = simulateOptions;
        SimulateIdCombo.DisplayMemberPath = nameof(LabeledIdOption.Display);
        SimulateIdCombo.SelectedValuePath = nameof(LabeledIdOption.Id);

        if (simulateOptions.Count == 0)
        {
            return;
        }

        if (previous > 0 && simulateOptions.Any(option => option.Id == previous))
        {
            SimulateIdCombo.SelectedValue = previous;
        }
        else
        {
            SimulateIdCombo.SelectedValue = simulateOptions[0].Id;
        }
    }

    private void SelectWorkbench(int workbenchId)
    {
        if (workbenchOptions.Count == 0)
        {
            return;
        }

        if (workbenchOptions.Any(option => option.Id == workbenchId))
        {
            WorkbenchIdCombo.SelectedValue = workbenchId;
            return;
        }

        WorkbenchIdCombo.SelectedValue = workbenchOptions[0].Id;
    }

    private void SelectSimulateId(int simulateId)
    {
        if (simulateOptions.Count == 0)
        {
            return;
        }

        if (simulateOptions.Any(option => option.Id == simulateId))
        {
            SimulateIdCombo.SelectedValue = simulateId;
            return;
        }

        SimulateIdCombo.SelectedValue = simulateOptions[0].Id;
    }

    private static void BindStringCombo(WpfComboBox combo, IEnumerable<string> options)
    {
        combo.ItemsSource = options.ToList();
        combo.SelectedValuePath = null;
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static void BindIntCombo(WpfComboBox combo, IEnumerable<int> options)
    {
        combo.DisplayMemberPath = null;
        combo.SelectedValuePath = null;
        combo.ItemsSource = options.ToList();
        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static void SelectString(WpfComboBox combo, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var options = combo.ItemsSource?.Cast<string>().ToList() ??
                      combo.Items.Cast<object>().Select(item => item.ToString() ?? string.Empty).ToList();

        if (!options.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
        {
            options.Insert(0, value);
            combo.ItemsSource = options;
        }

        combo.SelectedItem = options.FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
    }

    private static void SelectInt(WpfComboBox combo, int value)
    {
        var options = combo.ItemsSource?.Cast<int>().ToList() ?? [];
        if (!options.Contains(value))
        {
            options.Insert(0, value);
            combo.ItemsSource = options;
        }

        combo.SelectedItem = value;
    }
}
