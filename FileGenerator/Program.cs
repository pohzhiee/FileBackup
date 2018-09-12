using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileGenerator
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            bool parseResult = false;
            int numFilesInt;
            do
            {
                Console.Write("Enter number of files to generate: ");
                var numFiles = Console.ReadLine();
                parseResult = int.TryParse(numFiles, out numFilesInt);
            }
            while (!parseResult);

            Console.WriteLine("Enter path: ");
            var path = Console.ReadLine();
            //TODO: check for valid path
            if (path == null)
                return;
            var dirInfo = Directory.CreateDirectory(path);
            for (int i = 0; i < numFilesInt; i++)
            {
                var fileName = $"{dirInfo.FullName}\\{RandomString(10)}.txt";
                var data = RandomString(30);
                File.WriteAllText(fileName, data);
            }

        }
        private static readonly Random random = new Random();
        private static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}
