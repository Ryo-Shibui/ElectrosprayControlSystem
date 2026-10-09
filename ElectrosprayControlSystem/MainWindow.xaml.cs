using System;
using System.Windows;
using ElectrosprayControlSystem.ViewModels;

namespace ElectrosprayControlSystem
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.AttachPlots(VoltagePlot, CurrentPlot);
                await viewModel.InitializeAsync();
            }
        }

        private async void MainWindow_Closed(object sender, EventArgs e)
        {
            if (DataContext is MainViewModel viewModel)
            {
                try
                {
                    await viewModel.ShutdownAsync();
                }
                catch
                {
                }
            }
        }
    }
}
