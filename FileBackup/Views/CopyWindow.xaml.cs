using System;
using System.Windows;
using System.ComponentModel;
using FileBackup.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace FileBackup.Views
{
    /// <summary>
    /// Interaction logic for CopyWindow.xaml
    /// </summary>
    public partial class CopyWindow : Window
    {
        public CopyWindow()
        {
            InitializeComponent();
            DataContext = new CopyViewModel();
            this.Closing += CopyWindow_Closing;
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
                textBox.ToolTip = "";
            }
        }

        private void CopyWindow_Closing(object sender, CancelEventArgs e)
        {
            ((CopyViewModel)DataContext).Serialize();
        }
    }

}
