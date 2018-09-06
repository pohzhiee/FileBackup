using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Deployment.Application;
using System.ComponentModel;
using System.Configuration;
using FileBackup.Utils;
using System.Windows.Input;
using WinForms = System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Ookii.Dialogs.Wpf;
using FileBackup.Views;
using MvvmDialogs;

namespace FileBackup.ViewModels
{
    internal class CopyViewModel : ViewModelBase
    {
        //-------------- Settings parameters --------------

        private readonly int logMessageLimit = 1000;
        private readonly int numberOfTasks = 10;
        //----------------------------


        

        internal CopyViewModel()
        {
            if (File.Exists(settingsPath))
            {
                Deserialize(settingsPath);
            }
            
            PropertyChanged += OnPropertyChanged;
        }
        static readonly String settingsPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                                                    + "\\FileBackup\\copySettings.dat";
        #region PublicProperties
        public String Title
        {
            get { return "Copy Files"; }
        }

        private String _sourcePath;
        public String SourcePath
        {
            get => _sourcePath;
            set
            {
                _sourcePath = value;
                NotifyPropertyChanged("SourcePath");
            }
        }

        private String _destinationPath;
        public String DestinationPath {
            get => _destinationPath;
            set
            {
                _destinationPath = value;
                NotifyPropertyChanged("DestinationPath");
            }
        }

        private String _outputPath = Directory.GetCurrentDirectory();
        public String OutputPath
        {
            get => _outputPath;
            set
            {
                _outputPath = value;
                NotifyPropertyChanged("OutputPath");
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

        private bool _isCopying = false;
        public bool IsCopying {
            get => _isCopying;
            set
            {
                _isCopying = value;
                NotifyPropertyChanged("isCopying");
            }
        }

        private double _progress = 0;
        public double Progress {
            get => _progress;
            set
            {
                _progress = value;
                NotifyPropertyChanged("Progress");
            }
        }

        public ObservableCollection<string> LogList { get; set; } = new ObservableCollection<string>();
        #endregion


        #region PrivateFields

        private SemaphoreSlim logSemaphoreSlim = new SemaphoreSlim(1);

        private long _filesProcessed;
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
        private readonly DialogService DialogServiceInstance = new DialogService();
        private StreamWriter directoryLogFileWriter;
        private StreamWriter fileLogFileWriter;
        private bool createNewDirectory = false;

        #endregion

        public ICommand CopyButtonPressedCommand => new RelayCommand(async()=>await CopyButtonPressed());
        public ICommand FolderSelectButtonPressedCommand => new RelayCommand<String>(FolderSelectButtonPressed, (String a)=>true);
        public ICommand ShowAboutDialogCommand => new RelayCommand(ShowAboutDialog, () => true);
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

        private void FolderSelectButtonPressed(string _id)
        {
            var id = Int32.Parse(_id);
            VistaFolderBrowserDialog dialog = new VistaFolderBrowserDialog();
            dialog.ShowNewFolderButton = true;
            switch (id)
            {
                case 0:
                    if (dialog.ShowDialog() ?? false)
                        SourcePath = dialog.SelectedPath;
                    break;
                case 1:
                    if (dialog.ShowDialog() ?? false)
                        DestinationPath = dialog.SelectedPath;
                    break;
                case 2:
                    if (dialog.ShowDialog() ?? false)
                        OutputPath = dialog.SelectedPath;
                    break;
                default:
                    Debug.WriteLine($"id is {id}");
                    throw new InvalidDataException("Unexpected parameter received in FolderSelectButtonCommand");
            }
        }

        private async Task CopyButtonInternal()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            IsCopying = true;
            var countFilesTask = Task.Run(()=>_totalFiles = Directory.EnumerateFiles(SourcePath, "*.*", SearchOption.AllDirectories).Count());
            directoryLogFileWriter = File.AppendText($"{OutputPath}\\DirectoryLog.txt");
            fileLogFileWriter = File.AppendText($"{OutputPath}\\FileLog.txt");
            await directoryLogFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");
            await fileLogFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");
            Task copyFileTask = Task.Run(() => CopyFiles(SourcePath, DestinationPath));
            
            await copyFileTask;
            await countFilesTask;
            IsCopying = false;
            Debug.WriteLine($"Tasklist count: {_taskList.Count}");
            Debug.WriteLine($"Copying took {stopwatch.Elapsed}");
        }

        private async Task CopyButtonPressed()
        {
            try
            {
                LogList.Clear();
                await CopyButtonInternal();
                await Task.WhenAll(_taskList);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                Debug.WriteLine(e.Message);
                var dialogService = new MvvmDialogs.DialogService();
                String currentDir = Directory.GetCurrentDirectory();
                var path = currentDir + "\\ErrorLog.txt";
                dialogService.ShowMessageBox(this,
                    $"{e.Message}\nError details written to {path}",
                    "Copy File Error",
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
                IsCopying = false;
                Progress = 0;
                FilesProcessed = 0;
                FileProgress = "";
                _totalFiles = null;
                createNewDirectory = false;
                directoryLogFileWriter?.Close();
                await logSemaphoreSlim.WaitAsync();
                try
                {
                    fileLogFileWriter?.Close();
                }
                finally
                {
                    logSemaphoreSlim.Release();
                }
                directoryLogFileWriter = null;
                fileLogFileWriter = null;
                _taskList.Clear();
                Serialize();
            }
        }
        
        
        private readonly List<Task> _taskList = new List<Task>();

        private async Task CopyFiles(string sourceDirectory, string destinationDirectory)
        {
            // Get the subdirectories for the specified directory.

            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirectory);
            }

            DirectoryInfo dir = new DirectoryInfo(sourceDirectory);
            DirectoryInfo[] dirs = dir.GetDirectories();
            //Prompt for confirmation only if top level directory does not exist
            if (!Directory.Exists(destinationDirectory))
            {
                if (!createNewDirectory)
                {
                    var dialog = new MvvmDialogs.DialogService();
                    WinForms.DialogResult dialogResult = WinForms.MessageBox.Show("Destination directory does not exist. Create?", "Create Directory Confirmation", WinForms.MessageBoxButtons.YesNo);
                    if (dialogResult == WinForms.DialogResult.Yes)
                    {
                        createNewDirectory = true;
                    }
                    else
                    {
                        createNewDirectory = false;
                        return; //terminate the file copying if not creating new directory at destination
                    }
                }
                if (createNewDirectory)
                {
                    Directory.CreateDirectory(destinationDirectory);
                    var message = $"Directory created at {destinationDirectory}";
                    Debug.WriteLine(message);
                    directoryLogFileWriter.WriteLine(message);
                    AddToLog(message);
                }

            }
            else //top level directory exists
            {
                createNewDirectory = true;
            }

            // Get the files in the directory and copy them to the new location.
            var files = dir.EnumerateFiles();
            foreach (FileInfo sourceFileInfo in files)
            {
                string destPath = Path.Combine(destinationDirectory, sourceFileInfo.Name);
                if (File.Exists(destPath))
                {
                    FileInfo destFileInfo = new FileInfo(destPath);
                    var destFileTime = destFileInfo.LastWriteTime;
                    var sourceFileTime = sourceFileInfo.LastWriteTime;

                    if (destFileTime == sourceFileTime)
                    {
                        //Do nothing because both files are the same
                        //FilesProcessed++;
                    }
                    else if (destFileTime > sourceFileTime) //destination file is newer than source file
                    {
                        var message =
                            $"[Renamed source] File at {destPath} (edited {sourceFileTime}) copied to {destPath + ".old"}";

                        await logSemaphoreSlim.WaitAsync();
                        try
                        {
                            fileLogFileWriter.WriteLine(message);
                        }
                        finally
                        {
                            logSemaphoreSlim.Release();
                        }
                        AddToLog(message);

                        FilesProcessed++;

                        await QueueCopy($"{destPath}.old", sourceFileInfo.FullName);
                        //sourceFileInfo.CopyTo(destPath + ".old", true);
                    }
                    else //destination file is older than source file
                    {

                        var message =
                            $"[Renamed destination] File at {destPath} (edited {destFileTime}) renamed to {destPath + ".old"}";

                        await logSemaphoreSlim.WaitAsync();
                        try
                        {
                            fileLogFileWriter.WriteLine(message);
                        }
                        finally
                        {
                            logSemaphoreSlim.Release();
                        }
                        AddToLog(message);

                        FilesProcessed++;


                        var task1 = QueueCopy($"{destPath}.old", destFileInfo.FullName);
                        var task2 = QueueCopy($"{destPath}", sourceFileInfo.FullName);
                        await task1;
                        await task2;
                    }
                }
                else //case whereby file does not exist in destination folder
                {
                    var message = $"[Copy] File {sourceFileInfo.Name} copied to {destPath}";

                    Debug.WriteLine(message);
                    await logSemaphoreSlim.WaitAsync();
                    try
                    {
                        fileLogFileWriter.WriteLine(message);
                    }
                    finally
                    {
                        logSemaphoreSlim.Release();
                    }
                    AddToLog(message);
                    FilesProcessed++;

                    await QueueCopy(destPath, sourceFileInfo.FullName);
                }
            }

            // When copying subdirectories, copy them and their contents to new location.
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destinationDirectory, subdir.Name);
                await CopyFiles(subdir.FullName, temppath);
            }
        }

        private async Task QueueCopy(string dest, string source)
        {
            if (_taskList.Count < numberOfTasks)
            {
                _taskList.Add(CopyFile(dest, source));
            }
            else
            {
                var completed = await Task.WhenAny(_taskList);
                _taskList.Remove(completed);
                _taskList.Add(CopyFile(dest, source));
            }
        }
        //private Task CopyFile(string dest, string source)
        //{
        //    return Task.Run(
        //        () =>
        //        {
        //            using (var inStream = File.OpenRead(source))
        //            using (var outStream = File.OpenWrite(dest))
        //            {
        //                byte[] buffer = new byte[1 * 1024 * 1024]; //read in chunks of 1mb
        //                int bytesRead = inStream.Read(buffer, 0, buffer.Length);
        //                while (bytesRead > 0)
        //                {
        //                    outStream.Write(buffer, 0, bytesRead);

        //                    bytesRead = inStream.Read(buffer, 0, buffer.Length);
        //                }
        //            };
        //        }
        //    );
        //}
        /*private async Task CopyFile(string dest, string source)
        {

            await Task.Run(
                async () =>
                {
                    await readSemaphore.WaitAsync();
                    try
                    {
                        var result = File.ReadAllBytes(source);
                        await Task.Run(
                            async () =>
                            {
                                await writeSemaphore.WaitAsync();
                                try
                                {
                                    File.WriteAllBytes(dest, result);
                                }
                                finally
                                {
                                    writeSemaphore.Release();
                                }
                            }
                        );
                    }
                    finally
                    {
                        readSemaphore.Release();
                    }
                }

            );
        }*/
        private async Task CopyFile(string dest, string source)
        {
            await Task.Run(
                async () =>
                {
                    try
                    {
                        var result = File.ReadAllBytes(source);
                        File.WriteAllBytes(dest, result);
                    }
                    catch (Exception e)
                    {
                        var message =
                            $"[Exception] File at {dest} unable to be copied to {dest} due to an exception: {e}";
                        await logSemaphoreSlim.WaitAsync();
                        try
                        {
                            fileLogFileWriter.WriteLine(message);
                        }
                        finally
                        {
                            logSemaphoreSlim.Release();
                        }
                        AddToLog(message);
                    }
                }
            );
        }
        private void Deserialize(String filePath)
        {
            using (var data = new BinaryReader(File.OpenRead(filePath)))
            {
                SourcePath = data.ReadString();
                DestinationPath = data.ReadString();
                data.Close();
            }
        }

        public void Serialize()
        {
            var settingsfileInfo = new FileInfo(settingsPath);
            settingsfileInfo.Directory?.Create();

            using (var br = new BinaryWriter(File.OpenWrite(settingsPath)))
            {
                br.Write(SourcePath ?? "");
                br.Write(DestinationPath ?? "");
                br.Close();
            }
        }

        private void AddToLog(string message)
        {
            if (LogList == null)
                return;

            try
            {
                while (LogList.Count >= logMessageLimit)
                {
                    Debug.WriteLine($"Removing element");
                    Application.Current.Dispatcher.BeginInvoke((Action)(() => LogList.RemoveAt(LogList.Count - 1)));
                }

                Application.Current.Dispatcher.BeginInvoke((Action)(() => LogList.Insert(0, message)));
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
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
                        Debug.WriteLine($"Progress: {FilesProcessed}");
                    }
                    else
                    {
                        FileProgress = $"{FilesProcessed}/???";
                        Debug.WriteLine($"Progress: {FilesProcessed}");
                    }
                    break;
                default:
                    break;
            }
        }

    }
}