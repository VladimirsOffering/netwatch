using System.ComponentModel;
using System.Windows;
using NetWatch.ViewModels;

namespace NetWatch;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        Loaded += (_, _) =>
        {
            if (MainViewModel.IsElevated)
                _viewModel.Start();
        };
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        _viewModel.Dispose();
    }
}
