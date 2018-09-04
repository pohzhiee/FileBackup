using FileBackup.ViewModels;
using System;
using System.IO;
using System.Threading.Tasks;
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
        private async void OnPathChanged(object sender, EventArgs e)
        {
            var textBox = (TextBox)sender;
            var dir = textBox.Text;
            bool dirExists = await Task.Run(() => Directory.Exists(dir));
            if (!dirExists)
            {
                textBox.Background = System.Windows.Media.Brushes.Red;
                textBox.ToolTip = "Invalid file path";
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

        private void Button_Click()
        {

        }
    }
}
