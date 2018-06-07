using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace FileCheck
{


    class Program
    {
        static private StreamWriter directoryLogFileWriter = new StreamWriter("DirectoryLog.txt");
        static private StreamWriter fileLogFileWriter = new StreamWriter("FileLog.txt");

        private static async Task DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs = true)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
                Console.WriteLine($"Directory created at {destDirName}");
                await directoryLogFileWriter.WriteLineAsync($"Directory created at {destDirName}");
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo sourceFile in files)
            {
                string destPath = Path.Combine(destDirName, sourceFile.Name);
                if (File.Exists(destPath))
                {
                    FileInfo destFileInfo = new FileInfo(destPath);
                    var destFileTime = destFileInfo.LastWriteTime;
                    var sourceFileTime = sourceFile.LastWriteTime;
                    if(destFileTime == sourceFileTime) 
                    {
                        //Do nothing
                    }
                    else if(destFileTime > sourceFileTime) //destination file is newer than source file
                    {
                        sourceFile.CopyTo(destPath + ".old");
                        await fileLogFileWriter.WriteLineAsync($"File at {destPath} (edited {destFileTime}) replaced with new file (edited {sourceFileTime})");
                    }
                    else //destination file is older than source file
                    {
                        destFileInfo.CopyTo(destPath + ".old");
                        sourceFile.CopyTo(destPath);
                    }
                    if (destFileTime != sourceFileTime)
                    {
                        sourceFile.CopyTo(destPath, true);
                        Console.WriteLine($"File at {destPath} (edited {destFileTime}) replaced with new file (edited {sourceFileTime})");
                    }
                    //Else do nothing because both files are supposedly the same
                }
                else //case whereby file does not exist in destination folder
                {
                    sourceFile.CopyTo(destPath, false);
                    Console.WriteLine($"File {sourceFile.Name} copied to {destPath}");
                    await fileLogFileWriter.WriteLineAsync($"File {sourceFile.Name} copied to {destPath}");
                }
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {

                    string temppath = Path.Combine(destDirName, subdir.Name);
                    await DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }
            //private static void ProcessFolder(String newFolder, String oldFolder)
            //{
            //    DirectoryInfo oldDirectoryInfo = new DirectoryInfo(oldFolder);
            //    DirectoryInfo[] oldFolderDirs = oldDirectoryInfo.GetDirectories();

            //    DirectoryInfo newDirectoryInfo = new DirectoryInfo(newFolder);
            //    DirectoryInfo[] newFolderDirs = newDirectoryInfo.GetDirectories();

            //    foreach (DirectoryInfo folder in oldFolderDirs)
            //    {
            //        foreach (var newFolderDir in newFolderDirs)
            //        {
            //            if (newFolderDir.Name == folder.Name)
            //            {
            //                Console.WriteLine($"New folder contains {folder.Name}");
            //                Console.ReadKey();
            //                ProcessFolder(newFolderDir.FullName, folder.FullName);
            //            }
            //            else
            //            {
            //                DirectoryCopy
            //            }
            //        }
            //    }
            //}
        static void Main(string[] args)
        {
            const string oldFolder = @"C:\Users\zhieepoh\Documents\FolderSource";
            const string newFolder = @"C:\Users\zhieepoh\Documents\FolderDestination";
           
            DirectoryCopy(oldFolder, newFolder).GetAwaiter().GetResult();
            directoryLogFileWriter.Close();
            fileLogFileWriter.Close();
        }
    }
}
