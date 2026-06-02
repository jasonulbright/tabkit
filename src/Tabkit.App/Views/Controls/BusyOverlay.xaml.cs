using System.Windows;
using System.Windows.Controls;

namespace Tabkit.App.Views.Controls;

/// <summary>
/// Brand standard async progress overlay for C# WPF-UI pages.
/// Bind <see cref="IsBusy"/>, <see cref="Title"/>, and <see cref="Step"/> from
/// the page's ViewModel; the overlay shows / hides automatically and renders
/// a centered ProgressRing with semibold title + monospace step subtitle.
/// </summary>
public partial class BusyOverlay : UserControl
{
    public BusyOverlay()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.Register(
        nameof(IsBusy), typeof(bool), typeof(BusyOverlay),
        new PropertyMetadata(false, OnIsBusyChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(BusyOverlay),
        new PropertyMetadata("Working...", OnTitleChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(string), typeof(BusyOverlay),
        new PropertyMetadata(string.Empty, OnStepChanged));

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Step
    {
        get => (string)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    private static void OnIsBusyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BusyOverlay self) return;
        self.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BusyOverlay self)
            self.TitleText.Text = e.NewValue as string ?? string.Empty;
    }

    private static void OnStepChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BusyOverlay self)
            self.StepText.Text = e.NewValue as string ?? string.Empty;
    }
}
