using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Windows;
using System.ComponentModel;
using FileBackup.ViewModels;
using FileBackup.Views;

namespace FileBackup
{
    public partial class App : Application
    {
        public static Window app;

        private void Application_Startup(object sender, StartupEventArgs e)
        {

            // For catching Global uncaught exception
            AppDomain currentDomain = AppDomain.CurrentDomain;
            currentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnhandledExceptionOccured);

            app = new MainWindow();
            app.Show();

            if (e.Args.Length == 1) //make sure an argument is passed
            {
                FileInfo file = new FileInfo(e.Args[0]);
                if (file.Exists) //make sure it's actually a file
                {
                    // Here, add you own code
                    // ((MainViewModel)app.DataContext).OpenFile(file.FullName);
                }
            }
        }

        static void UnhandledExceptionOccured(object sender, UnhandledExceptionEventArgs args)
        {
            //var path = Directory.GetCurrentDirectory() + "\\FileBackupLog\\log.txt";
            var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                                    + "\\FileBackup\\log.txt";
            var fileInfo = new FileInfo(path);
            fileInfo.Directory?.Create();

            // Show a message before closing application
            var dialogService = new MvvmDialogs.DialogService();
            dialogService.ShowMessageBox((INotifyPropertyChanged)(app.DataContext),
                "Oops, something went wrong and the application must close. Please find a " +
                "report on the issue at: " + path + Environment.NewLine +
                "If the problem persist, please contact zhi-ee.poh@keysight.com",
                "Unhandled Error",
                MessageBoxButton.OK);

            Exception e = (Exception)args.ExceptionObject;
            var errorWriter = new StreamWriter(path);
            errorWriter.WriteLine(e);
            errorWriter.Close();
        }
    }
}
