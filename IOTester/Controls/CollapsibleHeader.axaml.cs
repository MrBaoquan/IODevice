using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System.Windows.Input;

namespace IOTester.Controls;

public partial class CollapsibleHeader : UserControl
{
    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        string
    >(nameof(Title), "标题");

    public static readonly StyledProperty<int> ItemCountProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        int
    >(nameof(ItemCount), 0);

    public static readonly StyledProperty<bool> ShowItemCountProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        bool
    >(nameof(ShowItemCount), false);

    public static readonly StyledProperty<bool> IsExpandedProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        bool
    >(nameof(IsExpanded), true);

    public static readonly StyledProperty<bool> ShowEditButtonsProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        bool
    >(nameof(ShowEditButtons), false);

    public static readonly StyledProperty<object?> ExtraContentProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        object?
    >(nameof(ExtraContent));

    public static readonly StyledProperty<ICommand?> AddCommandProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        ICommand?
    >(nameof(AddCommand));

    public static readonly StyledProperty<ICommand?> ClearCommandProperty =
        AvaloniaProperty.Register<CollapsibleHeader, ICommand?>(nameof(ClearCommand));

    public static readonly StyledProperty<string> AddTooltipProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        string
    >(nameof(AddTooltip), "添加新节点");

    public static readonly StyledProperty<string> ClearTooltipProperty = AvaloniaProperty.Register<
        CollapsibleHeader,
        string
    >(nameof(ClearTooltip), "清空所有节点");

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public int ItemCount
    {
        get => GetValue(ItemCountProperty);
        set => SetValue(ItemCountProperty, value);
    }

    public bool ShowItemCount
    {
        get => GetValue(ShowItemCountProperty);
        set => SetValue(ShowItemCountProperty, value);
    }

    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    public bool ShowEditButtons
    {
        get => GetValue(ShowEditButtonsProperty);
        set => SetValue(ShowEditButtonsProperty, value);
    }

    public object? ExtraContent
    {
        get => GetValue(ExtraContentProperty);
        set => SetValue(ExtraContentProperty, value);
    }

    public ICommand? AddCommand
    {
        get => GetValue(AddCommandProperty);
        set => SetValue(AddCommandProperty, value);
    }

    public ICommand? ClearCommand
    {
        get => GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public string AddTooltip
    {
        get => GetValue(AddTooltipProperty);
        set => SetValue(AddTooltipProperty, value);
    }

    public string ClearTooltip
    {
        get => GetValue(ClearTooltipProperty);
        set => SetValue(ClearTooltipProperty, value);
    }

    public CollapsibleHeader()
    {
        InitializeComponent();
    }

    private void OnChevronClicked(object? sender, PointerPressedEventArgs e)
    {
        IsExpanded = !IsExpanded;
    }
}
