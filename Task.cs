using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Renci.SshNet;
using System.Text.RegularExpressions;

namespace FRENDS.FileExtender
{
    public class Task
    {
        public class WindowsInput
        {
            /// <summary>
            /// Directory to retrieve information from.
            /// </summary>
            [DefaultValue(@"C:\")]
            public string PathToDirectory { get; set; }
        }

        public class SSHCommand
        {
            public String Command { get; set; }
        }

        public enum SSHBashType { LinuxBash, WindowsCMD }

        public class SSHInput
        {
            /// <summary>
            /// Directory to retrieve information from.
            /// </summary>
            public SSHBashType SSHBashType { get; set; }

            /// <summary>
            /// Directory or drive to run the operations against. For Windows non-SSH use network UNC path or regular path. For SSH use for eg. /mnt/q for Linux and plain single drive letter for Windows.
            /// </summary>
            [DefaultValue(@"/mnt/q or q")]
            public string PathToDirectory { get; set; }

            /// <summary>
            /// Directory to retrieve information from.
            /// </summary>
            [DefaultValue(@"myserver")]
            public string HostName { get; set; }
            /// <summary>
            /// Directory to retrieve information from.
            /// </summary>
            [DefaultValue(@"user")]
            public string UserName { get; set; }
            /// <summary>
            /// Directory to retrieve information from.
            /// </summary>
            [DefaultValue(@"password")]
            [PasswordPropertyText(true)]
            public string Password { get; set; }
        }


        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

        public static JToken GetDirectoryInfoWindows(WindowsInput WindowsInput)
        {
            ulong FreeBytesAvailable;
            ulong TotalNumberOfBytes;
            ulong TotalNumberOfFreeBytes;

            bool success = GetDiskFreeSpaceEx(WindowsInput.PathToDirectory,
                                      out FreeBytesAvailable,
                                      out TotalNumberOfBytes,
                                      out TotalNumberOfFreeBytes);


            JObject returnObject = new JObject();

            double freeSpacePerc = Math.Truncate((Convert.ToDouble(FreeBytesAvailable) / Convert.ToDouble(TotalNumberOfBytes)) * 100) / 100;
            double freeSpace = Math.Truncate((Convert.ToDouble(FreeBytesAvailable) / 1024 / 1024) * 100) / 100;
            double totalSpace = Math.Truncate((Convert.ToDouble(TotalNumberOfBytes) / 1024 / 1024) * 100) / 100;

            returnObject.Add("FreeSpacePercentage", freeSpacePerc);
            returnObject.Add("FreeSpaceMB", freeSpace);
            returnObject.Add("TotalSpaceMB", totalSpace);

            return JToken.FromObject(returnObject);
        }

        public static JToken GetOldestFileWindows(WindowsInput WindowsInput, string useless)
        {
            return JToken.FromObject(FindOldestFile(WindowsInput.PathToDirectory));
        }

        private static FileInfo FindOldestFile(string directory)
        {
            if (!Directory.Exists(directory))
                throw new ArgumentException();

            DirectoryInfo parent = new DirectoryInfo(directory);
            FileInfo[] children = parent.GetFiles();
            if (children.Length == 0)
                return null;

            FileInfo oldest = children[0];
            foreach (var child in children.Skip(1))
            {
                if (child.CreationTime < oldest.CreationTime)
                    oldest = child;
            }

            return oldest;
        }

        public static JToken GetDirectoryInfoSSH(SSHInput SSHInput)
        {
            JObject returnObject = new JObject();
            double result = 0.00;
            double freeSpace = 0.00;
            double totalSpace = 0.00;
            using (var client = new SshClient(SSHInput.HostName, SSHInput.UserName, SSHInput.Password))
            {
                client.Connect();
                switch (SSHInput.SSHBashType)
                {
                    case SSHBashType.LinuxBash:
                        SshCommand linuxCommand = client.RunCommand("df " + SSHInput.PathToDirectory);
                        String splitResult = linuxCommand.Result.TrimEnd('\n').Split('\n')[1];
                        Regex rgx = new Regex(@"\s(\d+%)");
                        MatchCollection matches = rgx.Matches(splitResult);
                        double availableSpace = 1 - (Convert.ToDouble(matches[0].Value.TrimStart().TrimEnd('%')) / 100);
                        result = availableSpace;
                        break;
                    case SSHBashType.WindowsCMD:
                        SshCommand winCommand = client.RunCommand("fsutil volume diskfree " + SSHInput.PathToDirectory);
                        String[] winResult = winCommand.Result.TrimEnd('\n').Split('\n');
                        String[] regexResults = new string[3];

                        for (int i = 0; i < winResult.Length; i++)
                        {
                            Regex rgx2 = new Regex(@"\s:\s(\d+)");
                            MatchCollection matches2 = rgx2.Matches(winResult[i]);
                            regexResults[i] = matches2[0].Value.TrimStart().TrimStart(':').TrimStart();
                        }

                        totalSpace = Math.Truncate(((Convert.ToDouble(regexResults[1])) / 1024 / 1024) * 100) / 100;
                        result = Math.Truncate((Convert.ToDouble(regexResults[2]) / Convert.ToDouble(regexResults[1])) * 100) / 100;
                        freeSpace = Math.Truncate(((Convert.ToDouble(regexResults[2]) / 1024 / 1024)) * 100) / 100;
                        break;
                }
                client.Disconnect();
            }

            returnObject.Add("FreeSpacePercentage", result);
            returnObject.Add("FreeSpaceMB", freeSpace);
            returnObject.Add("TotalSpaceMB", totalSpace);

            return returnObject;
        }
        public static String DeleteOldestSSH(SSHInput SSHInput)
        {
            String result = "";

            using (var client = new SshClient(SSHInput.HostName, SSHInput.UserName, SSHInput.Password))
            {
                client.Connect();
                switch (SSHInput.SSHBashType)
                {
                    case SSHBashType.WindowsCMD:
                        SshCommand winCommand = client.RunCommand("dir " + SSHInput.PathToDirectory + " /O:D");
                        String startSubString = winCommand.Result.Substring(winCommand.Result.IndexOf("Directory of"));
                        String firstFileEntryString = startSubString.Substring(startSubString.IndexOf('\n')).TrimStart('\n').TrimStart('\r').TrimStart('\n');
                        String[] resultFilesArray = firstFileEntryString.Split('\n');
                        result = resultFilesArray[0].Substring(36).TrimEnd();
                        SshCommand winCommandDelete = client.RunCommand(@"del """ + SSHInput.PathToDirectory + result + @"""");
                        break;
                    case SSHBashType.LinuxBash:
                        SshCommand linuxCommand = client.RunCommand("find " + SSHInput.PathToDirectory + " -type f -printf '%T+ %p\n' | sort | head -n 1 ");
                        result = linuxCommand.Result.Substring(linuxCommand.Result.IndexOf("/")).TrimEnd();
                        SshCommand linuxCommandDelete = client.RunCommand("rm " + SSHInput.PathToDirectory + "/" + result);
                        break;
                }
                client.Disconnect();
            }
            return result;
        }
    }
}