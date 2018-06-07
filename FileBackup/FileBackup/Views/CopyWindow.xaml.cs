using System;
using System.Windows;
using System.ComponentModel;
using FileBackup.ViewModels;
using System.Diagnostics;
using System.IO;
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


        private bool IsValidPath(String Path)
        {
            return Directory.Exists(Path);
        }

        private void CopyWindow_Closing(object sender, CancelEventArgs e)
        {
            ((CopyViewModel)DataContext).Serialize();
        }
    }

}
