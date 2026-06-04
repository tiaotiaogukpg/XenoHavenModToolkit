using System.Windows;
using System.Windows.Controls;



using WpfComboBox = System.Windows.Controls.ComboBox;

using WpfUserControl = System.Windows.Controls.UserControl;



namespace XenoHavenModToolkit.Controls;



public partial class BuildingFieldPicker : WpfUserControl

{

    private IReadOnlyList<LabeledIdOption> workbenchOptions = [];
    private IReadOnlyList<LabeledIdOption> productionLineOptions = [];

    public event EventHandler? TypeChanged;



    public BuildingFieldPicker()

    {

        InitializeComponent();

        BindStringCombo(TypeCombo, BuildingFieldOptions.DefaultTypes);

        BindIntCombo(DirectionCombo, BuildingFieldOptions.DefaultDirections);

        TypeCombo.SelectionChanged += TypeCombo_SelectionChanged;

        ApplyTypeVisibility();

    }



    public string SelectedType => TypeCombo.SelectedValue as string ?? string.Empty;



    public int SelectedDirection => DirectionCombo.SelectedItem is int value ? value : 0;

    public int EffectiveDirection => BuildingFieldOptions.RequiresFixedDirection(SelectedType) ? 1 : SelectedDirection;



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
        SimulateIdCombo.ItemsSource = productionLineOptions;
        SimulateIdCombo.DisplayMemberPath = nameof(LabeledIdOption.Display);
        SimulateIdCombo.SelectedValuePath = nameof(LabeledIdOption.Id);

        if (productionLineOptions.Count > 0)
        {
            SimulateIdCombo.SelectedValue = productionLineOptions[0].Id;
        }
    }



    public void SetValues(string type, int direction, int workbenchId, int simulateId)

    {

        SelectString(TypeCombo, type);

        SelectInt(DirectionCombo, direction);

        SelectWorkbench(workbenchId);

        SelectProductionLine(simulateId);

        ApplyTypeVisibility();

    }



    public bool TryValidate(out string message)

    {

        if (string.IsNullOrWhiteSpace(SelectedType))

        {

            message = "请选择 type。";

            return false;

        }



        if (!BuildingFieldOptions.RequiresFixedDirection(SelectedType) && SelectedDirection <= 0)

        {

            message = "请选择 direction。";

            return false;

        }



        if (SelectedWorkbenchId <= 0 || workbenchOptions.All(option => option.Id != SelectedWorkbenchId))

        {

            message = "请选择 workbenchId。";

            return false;

        }

        if (BuildingFieldOptions.RequiresSimulateId(SelectedType) &&
            (SelectedSimulateId <= 0 || productionLineOptions.All(option => option.Id != SelectedSimulateId)))
        {
            message = "请选择 simulateId。请确认 DOC/S-生产线定义.xlsx 已加载。";
            return false;
        }



        message = string.Empty;

        return true;

    }

    private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyTypeVisibility();
        TypeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTypeVisibility()
    {
        var fixedDirection = BuildingFieldOptions.RequiresFixedDirection(SelectedType);
        if (fixedDirection)
        {
            SelectInt(DirectionCombo, 1);
        }

        DirectionCombo.IsEnabled = !fixedDirection;
        var simulateVisibility = BuildingFieldOptions.RequiresSimulateId(SelectedType)
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        SimulateIdLabel.Visibility = simulateVisibility;
        SimulateIdCombo.Visibility = simulateVisibility;
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

    private void SelectProductionLine(int simulateId)
    {
        if (productionLineOptions.Count == 0)
        {
            return;
        }

        if (productionLineOptions.Any(option => option.Id == simulateId))
        {
            SimulateIdCombo.SelectedValue = simulateId;
            return;
        }

        SimulateIdCombo.SelectedValue = productionLineOptions[0].Id;
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


