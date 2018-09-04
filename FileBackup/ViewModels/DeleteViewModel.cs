using FileBackup.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Deployment.Application;
using System.Reflection;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using Ookii.Dialogs.Wpf;
using MvvmDialogs;
using FileBackup.Views;

namespace FileBackup.ViewModels
{
    internal class DeleteViewModel : ViewModelBase
    {

        internal DeleteViewModel()
        {
            if (File.Exists(settingsPath))
            {
                Deserialize(settingsPath);
            }
        }
        static readonly String settingsPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                                    + "\\FileBackup\\deleteSettings.dat";
        #region Public Properties
        private bool _isBusy = false;
        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                NotifyPropertyChanged("IsBusy");
            }
        }

        private long _progress = 0;
        public long Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                NotifyPropertyChanged("Progress");
            }
        }

        private String _fileProgress;
        public String FileProgress
        {
            get => _fileProgress;
            set
            {
                _fileProgress = value;
                NotifyPropertyChanged("FileProgress");
            }
        }

        private String _folderPath;
        public String FolderPath
        {
            get => _folderPath;
            set
            {
                _folderPath = value;
                NotifyPropertyChanged("FolderPath");
            }
        }

        private DateTime _date = DateTime.Today;
        public DateTime Date
        {
            get { return _date; }
            set { _date = value.Date;
                NotifyPropertyChanged(); }
        }

        public String VersionText
        {
            get
            {
                Version version;
                try
                {
                    version = ApplicationDeployment.CurrentDeployment.CurrentVersion;
                }
                catch (Exception ex)
                {
                    version = Assembly.GetExecutingAssembly().GetName().Version;
                }
                var versionString = string.Format("{4} Version: {0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision, Assembly.GetEntryAssembly().GetName().Name);
                return versionString;
            }
        }
        #endregion

        private readonly DialogService DialogServiceInstance = new DialogService();
        private StreamWriter logFileWriter;
        private int filesProcessed = 0;
        private static long? totalFiles = null;

        //public ICommand FolderSelectButtonPressedCommand => new RelayCommand(() => Console.WriteLine("ASD"), () => true);
        public ICommand FolderSelectCommand => new RelayCommand(() => FolderSelect(), () => true);
        public ICommand DeleteFilesCommand => new RelayCommand(async () => await DeleteFilesPressed());
        public ICommand ShowAboutDialogCommand =>  new RelayCommand(ShowAboutDialog, ()=>true);
        public ICommand BackCommand => new RelayCommand(Back, () => true);
        public ICommand ExitCommand => new RelayCommand(() => App.app.Close(), () => true);

        private void Back()
        {
            var mainWindow = new MainWindow();
            App.app.Close();
            App.app = mainWindow;
            App.app.Show();
        }

        private void ShowAboutDialog()
        {
            AboutViewModel dialog = new AboutViewModel();
            var result = DialogServiceInstance.ShowDialog<About>(this, dialog);
        }

        private void FolderSelect()
        {
            VistaFolderBrowserDialog dialog = new VistaFolderBrowserDialog();
            dialog.ShowNewFolderButton = true;

            if (dialog.ShowDialog() ?? false)
                FolderPath = dialog.SelectedPath;
        }

        private async Task DeleteFilesPressedInternal()
        {
            IsBusy = true;
            var countFilesTask =  Task.Run(() => totalFiles = Directory.GetFiles(FolderPath, "*.*", SearchOption.AllDirectories).Count());
            var filePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                                    + "\\FileBackup\\DeleteLog.txt";
            logFileWriter = File.AppendText(filePath);
            await logFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");

            Task deleteFileTask = Task.Run(async () => await DeleteFiles(FolderPath));
            Task updateProgressTask = Task.Run(async () =>
            {
                while (IsBusy)
                {
                    if (totalFiles != null)
                    {
                        Progress = filesProcessed * 10000 / (long)totalFiles;
                        FileProgress = $"{filesProcessed}/{totalFiles}";
                    }
                    else
                    {
                        FileProgress = $"{filesProcessed}/???";
                    }
                    await Task.Delay(100);
                }
            });
            await deleteFileTask;
            await countFilesTask;
            IsBusy = false;
            await updateProgressTask;
        }

        private async Task DeleteFilesPressed()
        {

            try
            {
                await DeleteFilesPressedInternal();
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                Debug.WriteLine(e.Message);
                var dialogService = new DialogService();
                String currentDir = Directory.GetCurrentDirectory();
                var path = currentDir + "\\ErrorLog.txt";
                dialogService.ShowMessageBox(this,
                    $"{e.Message}\nError details written to {path}",
                    "Delete Files Error",
                    MessageBoxButton.OK);
                var errorWriter = File.AppendText(path);
                await errorWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");
                await errorWriter.WriteLineAsync("Message:");
                await errorWriter.WriteLineAsync(e.Message);
                await errorWriter.WriteLineAsync("Target Site:");
                await errorWriter.WriteLineAsync(e.TargetSite.ToString());
                await errorWriter.WriteLineAsync("Source:");
                await errorWriter.WriteLineAsync(e.Source);
                await errorWriter.WriteLineAsync("Full: ");
                await errorWriter.WriteLineAsync(e.ToString());
                errorWriter.Close();
            }
            finally
            {
                IsBusy = false;
                Progress = 0;
                filesProcessed = 0;
                FileProgress = "";
                totalFiles = null;
                logFileWriter.Close();
                Serialize();
            }
        }

        private async Task DeleteFiles(String directoryPath)
        {

            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + directoryPath);
            }
            DirectoryInfo dir = new DirectoryInfo(directoryPath);
            DirectoryInfo[] dirs = dir.GetDirectories();
            FileInfo[] files = dir.GetFiles();
            foreach (var file in files)
            {
                if(file.LastWriteTime < Date)
                {
                    file.Delete();
                    Debug.WriteLine($"{file.FullName} deleted");
                    await logFileWriter.WriteLineAsync($"{file.FullName} deleted");
                    filesProcessed++;
                }
            }
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(directoryPath, subdir.Name);
                await DeleteFiles(temppath);
            }
        }


        private void Deserialize(String filePath)
        {
            var data = new BinaryReader(File.OpenRead(filePath));
            FolderPath = data.ReadString();
            var dateString = data.ReadString();
            Date = DateTime.ParseExact(dateString, "O", CultureInfo.InvariantCulture);
            data.Close();
        }

        public void Serialize()
        {
            var br = new BinaryWriter(File.OpenWrite(settingsPath));
            br.Write(FolderPath);
            string dateString = Date.ToString("O");
            br.Write(dateString);
            br.Close();
        }
    }
}
 