using FileBackup.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Deployment.Application;
using System.Reflection;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Input;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Ookii.Dialogs.Wpf;
using MvvmDialogs;
using FileBackup.Views;

namespace FileBackup.ViewModels
{
    //TODO: message prompt to show how many files deleted
    internal class DeleteViewModel : ViewModelBase
    {

        //-------------- Settings parameters --------------

        private readonly int logMessageLimit = 1000;
        private readonly int numberOfTasks = 10;
        //----------------------------
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

        private double _progress = 0;
        public double Progress
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
        
        public ObservableCollection<string> LogList { get; set; } = new ObservableCollection<string>();
        #endregion

        private int filesDeleted = 0;

        private readonly DialogService DialogServiceInstance = new DialogService();
        private StreamWriter logFileWriter;

        private long _filesProcessed = 0;
        private long FilesProcessed
        {
            get => _filesProcessed;
            set
            {
                _filesProcessed = value;
                NotifyPropertyChanged(nameof(FilesProcessed));
            }
        }
        private long? _totalFiles = null;

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

            var countFilesTask =  Task.Run(() => _totalFiles = Directory.GetFiles(FolderPath, "*.*", SearchOption.AllDirectories).Count());
            var filePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                                    + "\\FileBackup\\DeleteLog.txt";
            logFileWriter = File.AppendText(filePath);
            await logFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");

            var deleteFileTask = Task.Run(async () => await DeleteFiles(FolderPath));

            await deleteFileTask;
            await countFilesTask;
            IsBusy = false;
        }

        private async Task DeleteFilesPressed()
        {

            try
            {
                LogList.Clear();
                await DeleteFilesPressedInternal();
                await Task.WhenAll(_taskList);
                DialogServiceInstance.ShowMessageBox(this, $"{filesDeleted} number of files deleted");
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
                FilesProcessed = 0;
                FileProgress = "";
                _totalFiles = null;
                filesDeleted = 0;
                logFileWriter.Close();
                Serialize();
            }
        }
        

        private readonly List<Task> _taskList = new List<Task>();

        private async Task DeleteFiles(string directoryPath)
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
            var initialFileCount = files.Length;
            var localDeleteCount = 0;
            foreach (var file in files)
            {
                if (file.LastWriteTime < Date)
                {
                    await QueueDelete(file.FullName);
                    var message = $"{file} deleted from {dir.FullName}";
                    Debug.WriteLine(message);
                    await logFileWriter.WriteLineAsync(message);
                    AddToLog(message);

                    localDeleteCount++;
                    FilesProcessed++;
                    filesDeleted++;
                }
                else
                {
                    FilesProcessed++;
                }
            }
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(directoryPath, subdir.Name);
                await DeleteFiles(temppath);
            }
        }

        private async Task QueueDelete(string path)
        {
            if (_taskList.Count < numberOfTasks)
            {
                _taskList.Add(DeleteFile(path));
            }
            else
            {
                var completed = await Task.WhenAny(_taskList);
                _taskList.Remove(completed);
                _taskList.Add(DeleteFile(path));
            }
        }
        
        private async Task DeleteFile(string path)
        {
            await Task.Run(
                () =>
                {
                    File.Delete(path);
                }
            );
        }

        //private async Task DeleteFiles(String directoryPath)
        //{

        //    if (!Directory.Exists(directoryPath))
        //    {
        //        throw new DirectoryNotFoundException(
        //            "Source directory does not exist or could not be found: "
        //            + directoryPath);
        //    }
        //    DirectoryInfo dir = new DirectoryInfo(directoryPath);
        //    DirectoryInfo[] dirs = dir.GetDirectories();
        //    FileInfo[] files = dir.GetFiles();
        //    foreach (var file in files)
        //    {
        //        if(file.LastWriteTime < Date)
        //        {
        //            file.Delete();
        //            Debug.WriteLine($"{file.FullName} deleted");
        //            await logFileWriter.WriteLineAsync($"{file.FullName} deleted");
        //            filesProcessed++;
        //        }
        //    }
        //    foreach (DirectoryInfo subdir in dirs)
        //    {
        //        string temppath = Path.Combine(directoryPath, subdir.Name);
        //        await DeleteFiles(temppath);
        //    }
        //}


        private void Deserialize(String filePath)
        {
            var fileInfo = new FileInfo(filePath);
            fileInfo.Directory?.Create();
            var data = new BinaryReader(File.OpenRead(filePath));
            FolderPath = data.ReadString();
            var dateString = data.ReadString();
            Date = DateTime.ParseExact(dateString, "O", CultureInfo.InvariantCulture);
            data.Close();
        }

        public void Serialize()
        {
            var fileInfo = new FileInfo(settingsPath);
            fileInfo.Directory?.Create();
            var br = new BinaryWriter(File.OpenWrite(settingsPath));
            br.Write(FolderPath ?? "");
            string dateString = Date.ToString("O");
            br.Write(dateString);
            br.Close();
        }

        private void AddToLog(string message)
        {
            while (LogList.Count >= logMessageLimit)
            {
                Debug.WriteLine($"Removing element");
                Application.Current.Dispatcher.BeginInvoke((Action)(() => LogList.RemoveAt(LogList.Count - 1)));
            }

            Debug.WriteLine($"Adding to log list");
            Application.Current.Dispatcher.BeginInvoke((Action)(() => LogList.Insert(0, message)));
        }


        private void OnPropertyChanged(object sender, PropertyChangedEventArgs args)
        {
            switch (args.PropertyName)
            {
                case nameof(FilesProcessed):
                    if (_totalFiles != null)
                    {
                        Progress = FilesProcessed / (double)_totalFiles;
                        FileProgress = $"{FilesProcessed}/{_totalFiles}";
                    }
                    else
                        FileProgress = $"{FilesProcessed}/???";
                    break;
            }
        }
    }
}
 