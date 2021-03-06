﻿using System;
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
        private SemaphoreSlim uiLogSemaphore = new SemaphoreSlim(1);
        private SemaphoreSlim taskSemaphore = new SemaphoreSlim(1);

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

        private long _sourceRenamedCount;
        private long _destRenamedCount;
        private long _normalCopyCount;
        private long _doNothingCount;

        private long? _totalFiles = null;
        private readonly DialogService _dialogServiceInstance = new DialogService();
        private StreamWriter _directoryLogFileWriter;
        private StreamWriter _fileLogFileWriter;
        private bool _createNewDirectory = false;

        //Verification stuff
        private List<string> _availableDirectoryList = new List<string>();
        private List<string> _processedDirectoryList = new List<string>();


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
            var result = _dialogServiceInstance.ShowDialog<About>(this, dialog);
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
            var getDirectoriesTask = Task.Run(() => GetDirectoryList(SourcePath));
            _directoryLogFileWriter = File.AppendText($"{OutputPath}\\DirectoryLog.txt");
            _fileLogFileWriter = File.AppendText($"{OutputPath}\\FileLog.txt");
            await _directoryLogFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");
            await _fileLogFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");
            Task copyFileTask = Task.Run(() => CopyFiles(SourcePath, DestinationPath));
            
            await copyFileTask;
            await countFilesTask;
            IsCopying = false;
            Debug.WriteLine($"Tasklist count: {_taskList.Count}");
            Debug.WriteLine($"Copying took {stopwatch.Elapsed}");

            await Task.WhenAll(_taskList);
            var message =
                $"Source file older: {_sourceRenamedCount}\nDestination file older: {_destRenamedCount}\nNormal Copied:{_normalCopyCount}\nIgnored:{_doNothingCount}";
            await AddToLog(message);
            _fileLogFileWriter.WriteLine(message);
            _dialogServiceInstance.ShowMessageBox(this, message, "Result");

            if (_processedDirectoryList.Count != 0 && _availableDirectoryList.Count != 0)
            {
                if (_processedDirectoryList.Count == _availableDirectoryList.Count)
                {
                    await AddToLog($"Processed directory count: {_processedDirectoryList.Count}, total directory count: {_availableDirectoryList.Count}");
                }
                else if (_processedDirectoryList.Count > _availableDirectoryList.Count)
                {
                    throw new Exception("Fatal error: Processed directory more than total available directory, please check logic");
                }
                else
                {
                    await AddToLog($"Processed directory count: {_processedDirectoryList.Count}, total directory count: {_availableDirectoryList.Count}");
                    var diff = _availableDirectoryList.Except(_processedDirectoryList);
                    foreach (var item in diff)
                    {
                        await AddToLog($"Directories not processed: {item}");
                    }
                }
            }
        }

        private async Task CopyButtonPressed()
        {
            try
            {
                LogList.Clear();
                await CopyButtonInternal();
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

                _sourceRenamedCount = 0;
                _destRenamedCount = 0;
                _normalCopyCount = 0;
                _doNothingCount = 0;

                _createNewDirectory = false;
                _directoryLogFileWriter?.Close();
                await logSemaphoreSlim.WaitAsync();
                try
                {
                    _fileLogFileWriter?.Close();
                }
                finally
                {
                    logSemaphoreSlim.Release();
                }
                _directoryLogFileWriter = null;
                _fileLogFileWriter = null;
                _taskList.Clear();
                Serialize();
            }
        }
        
        
        private readonly List<Task> _taskList = new List<Task>();

        private void GetDirectoryList(string sourceDirectory)
        {
            _availableDirectoryList.Add(sourceDirectory);
            var dir = new DirectoryInfo(sourceDirectory);
            var dirs = dir.GetDirectories();
            foreach (DirectoryInfo subdir in dirs)
            {
                GetDirectoryList(subdir.FullName);
            }
        }

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
                if (!_createNewDirectory)
                {
                    var dialog = new MvvmDialogs.DialogService();
                    WinForms.DialogResult dialogResult = WinForms.MessageBox.Show("Destination directory does not exist. Create?", "Create Directory Confirmation", WinForms.MessageBoxButtons.YesNo);
                    if (dialogResult == WinForms.DialogResult.Yes)
                    {
                        _createNewDirectory = true;
                    }
                    else
                    {
                        _createNewDirectory = false;
                        return; //terminate the file copying if not creating new directory at destination
                    }
                }
                if (_createNewDirectory)
                {
                    Directory.CreateDirectory(destinationDirectory);
                    var message = $"Directory created at {destinationDirectory}";
                    Debug.WriteLine(message);
                    _directoryLogFileWriter.WriteLine(message);
                    await AddToLog(message);
                }

            }
            else //top level directory exists
            {
                _createNewDirectory = true;
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
                        _doNothingCount++;
                        FilesProcessed++;
                    }
                    else if (destFileTime > sourceFileTime) //destination file is newer than source file
                    {
                        var newDestFileName = $"{destPath}_old.txt";
                        //if the destination already has a _old file, check if it is the same as the source file, if same do nothing
                        if (File.Exists(newDestFileName))
                        {
                            var newDestFileInfo = new FileInfo(newDestFileName);
                            if (newDestFileInfo.LastWriteTime == sourceFileInfo.LastWriteTime)
                            {
                                _doNothingCount++;
                                FilesProcessed++;
                                continue;
                            }
                        }

                        var message =
                            $"[Renamed source] File at {destPath} (edited {sourceFileTime}) copied to {newDestFileName}";

                        await logSemaphoreSlim.WaitAsync();
                        try
                        {
                            _fileLogFileWriter.WriteLine(message);
                        }
                        finally
                        {
                            logSemaphoreSlim.Release();
                        }
                        await AddToLog(message);
                        _sourceRenamedCount++;
                        FilesProcessed++;

                        await QueueCopy(newDestFileName, sourceFileInfo.FullName);
                        
                        
                    }
                    else //destination file is older than source file
                    {

                        var message =
                            $"[Renamed destination] File at {destPath} (edited {destFileTime}) renamed to {destPath + "_old.txt"}";

                        await logSemaphoreSlim.WaitAsync();
                        try
                        {
                            _fileLogFileWriter.WriteLine(message);
                        }
                        finally
                        {
                            logSemaphoreSlim.Release();
                        }
                        await AddToLog(message);
                        _destRenamedCount++;
                        FilesProcessed++;


                        var task1 = QueueCopy($"{destPath}_old.txt", destFileInfo.FullName);
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
                        _fileLogFileWriter.WriteLine(message);
                    }
                    finally
                    {
                        logSemaphoreSlim.Release();
                    }
                    await AddToLog(message);
                    _normalCopyCount++;
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
            _processedDirectoryList.Add(sourceDirectory);
        }

        private async Task QueueCopy(string dest, string source)
        {
            await taskSemaphore.WaitAsync();
            try
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
            finally
            {
                taskSemaphore.Release();
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
                        var result = File.ReadAllBytes(source);AddToLog
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

                        if (!File.Exists(source))
                        {
                            throw new Exception($"Source file does not exist : {source}");
                        }
                        var result = File.ReadAllBytes(source);
                        var origin = new FileInfo(source);

                        FileInfo destFileInfo = null;
                        //File.WriteAllBytes(dest, result);
                        if (File.Exists(dest))
                        {
                            destFileInfo = new FileInfo(dest);
                            using (var fileStream = destFileInfo.OpenWrite())
                            {
                                fileStream.Write(result, 0, result.Length);
                            }
                        }
                        else 
                        {
                            using (var fileStream = File.Create(dest))
                            {
                                fileStream.Write(result, 0, result.Length);
                            }
                            destFileInfo = new FileInfo(dest);
                        }

                        destFileInfo.CreationTime = origin.CreationTime;
                        destFileInfo.LastWriteTime = origin.LastWriteTime;
                        destFileInfo.LastAccessTime = origin.LastAccessTime;
                    }
                    catch (Exception e)
                    {
                        var message =
                            $"[Exception] File at {source} unable to be copied to {dest} due to an exception: {e}";
                        await logSemaphoreSlim.WaitAsync();
                        try
                        {
                            _fileLogFileWriter.WriteLine(message);
                        }
                        finally
                        {
                            logSemaphoreSlim.Release();
                        }
                        await AddToLog(message);
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

        private async Task AddToLog(string message)
        {
            if (LogList == null)
                return;
            await uiLogSemaphore.WaitAsync();
            try
            {
                while (LogList.Count >= logMessageLimit)
                {
                    Debug.WriteLine($"Removing element");
                    await Application.Current.Dispatcher.BeginInvoke((Action) (() => LogList.RemoveAt(LogList.Count - 1)));
                }

                await Application.Current.Dispatcher.BeginInvoke((Action) (() => LogList.Insert(0, message)));
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
            finally
            {
                uiLogSemaphore.Release();
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