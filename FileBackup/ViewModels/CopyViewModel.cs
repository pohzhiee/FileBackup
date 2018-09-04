using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Deployment.Application;
using System.ComponentModel;
using FileBackup.Utils;
using System.Windows.Input;
using WinForms = System.Windows.Forms;
using System.Diagnostics;
using System.Windows;
using Ookii.Dialogs.Wpf;
using FileBackup.Views;
using MvvmDialogs;

namespace FileBackup.ViewModels
{
    internal class CopyViewModel : ViewModelBase
    {
        internal CopyViewModel()
        {
            if (File.Exists(settingsPath))
            {
                Deserialize(settingsPath);
            }
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

        private long _progress = 0;
        public long Progress {
            get => _progress;
            set
            {
                _progress = value;
                NotifyPropertyChanged("Progress");
            }
        }
        #endregion


        #region PrivateFields
        static private int filesProcessed = 0;
        static private long? totalFiles = null;
        private readonly DialogService DialogServiceInstance = new DialogService();
        private StreamWriter directoryLogFileWriter;
        private StreamWriter fileLogFileWriter;
        private bool createNewDirectory = false;
        #endregion

        public ICommand CopyButtonPressedCommand => new RelayCommand(async()=>await CopyButtonPressed());
        public ICommand FolderSelectButtonPressedCommand => new RelayCommand<String>((String id)=>FolderSelectButtonPressed(id), (String a)=>true);
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

        private void FolderSelectButtonPressed(String _id)
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
            IsCopying = true;
            var countFilesTask = Task.Run(()=>totalFiles = Directory.GetFiles(SourcePath, "*.*", SearchOption.AllDirectories).Count());
            directoryLogFileWriter = File.AppendText($"{OutputPath}\\DirectoryLog.txt");
            fileLogFileWriter = File.AppendText($"{OutputPath}\\FileLog.txt");
            await directoryLogFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");
            await fileLogFileWriter.WriteLineAsync($"<{DateTime.UtcNow} (UTC)>");
            Task copyFileTask = Task.Run(async () => await CopyFiles(SourcePath, DestinationPath));

            Task updateProgressTask = Task.Run(async () =>
            {
                while (IsCopying)
                {
                    if (totalFiles != null)
                    {
                        Progress = filesProcessed * 10000 / (long)totalFiles;
                        FileProgress = $"{filesProcessed}/{totalFiles}";
                    }
                    else
                        FileProgress = $"{filesProcessed}/???";
                    await Task.Delay(100);
                }
            });
            await copyFileTask;
            await countFilesTask;
            IsCopying = false;
            await updateProgressTask;
        }

        private async Task CopyButtonPressed()
        {
            try
            {
                await CopyButtonInternal();
                //await incrementTask; //for debugging progress bar
            }
            catch (DirectoryNotFoundException e) //redundant since it is already verified that it exists after scanning the directory for number of files
            {
                var dialogService = new MvvmDialogs.DialogService();
                dialogService.ShowMessageBox(this,
                    e.Message,
                    "Directory not found",
                    MessageBoxButton.OK);
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
                filesProcessed = 0;
                FileProgress = "";
                totalFiles = null;
                createNewDirectory = false;
                directoryLogFileWriter?.Close();
                fileLogFileWriter?.Close();
                directoryLogFileWriter = null;
                fileLogFileWriter = null;
                Serialize();
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
                    Console.WriteLine($"Directory created at {destinationDirectory}");
                    await directoryLogFileWriter.WriteLineAsync($"Directory created at {destinationDirectory}");
                }

            }
            else //top level directory exists
            {
                createNewDirectory = true;
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
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
                        filesProcessed++;
                    }
                    else if (destFileTime > sourceFileTime) //destination file is newer than source file
                    {
                        await fileLogFileWriter.WriteLineAsync($"File at {destPath} (edited {sourceFileTime}) copied to {destPath + ".old"}");
                        filesProcessed++;
                        sourceFileInfo.CopyTo(destPath + ".old", true);
                    }
                    else //destination file is older than source file
                    {
                        await fileLogFileWriter.WriteLineAsync($"File at {destPath} (edited {destFileTime}) renamed to {destPath + ".old"}");
                        filesProcessed++;
                        destFileInfo.CopyTo(destPath + ".old", true);
                        sourceFileInfo.CopyTo(destPath, true);
                    }
                }
                else //case whereby file does not exist in destination folder
                {
                    sourceFileInfo.CopyTo(destPath, false);
                    filesProcessed++;
                    Console.WriteLine($"File {sourceFileInfo.Name} copied to {destPath} PROCESSED:{filesProcessed}");
                    await fileLogFileWriter.WriteLineAsync($"File {sourceFileInfo.Name} copied to {destPath}");
                }
            }

            // When copying subdirectories, copy them and their contents to new location.
            foreach (DirectoryInfo subdir in dirs)
            {
                string temppath = Path.Combine(destinationDirectory, subdir.Name);
                await CopyFiles(subdir.FullName, temppath);
            }
        }

        private void Deserialize(String filePath)
        {
            var data = new BinaryReader(File.OpenRead(filePath));
            SourcePath = data.ReadString();
            DestinationPath = data.ReadString();
            data.Close();
        }

        public void Serialize()
        {
            var br = new BinaryWriter(File.OpenWrite(settingsPath));
            br.Write(SourcePath);
            br.Write(DestinationPath);
            br.Close();
        }

    }
}