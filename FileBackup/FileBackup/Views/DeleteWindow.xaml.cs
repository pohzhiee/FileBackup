using FileBackup.ViewModels;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FileBackup.Views
{
    /// <summary>
    /// Interaction logic for DeleteWindow.xaml
    /// </summary>
    public partial class DeleteWindow : Window
    {
        public DeleteWindow()
        {
            InitializeComponent();
            DataContext = new DeleteViewModel();
            this.Closing += DeleteWindow_Closing;
        }
        private void OnPathChanged(object sender, EventArgs e)
        {
            var textBox = (TextBox)sender;
            var dir = textBox.Text;
            if (!Directory.Exists(dir))
            {
                textBox.Background = System.Windows.Media.Brushes.Red;
            }
            else
            {
                textBox.Background = System.Windows.Media.Brushes.White;
            }
        }

        private void DeleteWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ((DeleteViewModel)DataContext).Serialize();
        }
    }
}
