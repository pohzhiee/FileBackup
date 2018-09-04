using MvvmDialogs;
using System;
using System.Deployment.Application;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using FileBackup.Converters;

namespace FileBackup.ViewModels
{
    class AboutViewModel : ViewModelBase, IModalDialogViewModel
    {
        public bool? DialogResult { get { return false; } }

        public string Content
        {
            get
            {
                return "FileBackup" + Environment.NewLine +
                       "Created by Poh Zhi-Ee" + Environment.NewLine +
                       "2018";
            }
        }

        public string VersionText
        {
            get
            {
                Version version;
                try
                {
                    version =  ApplicationDeployment.CurrentDeployment.CurrentVersion;
                }
                catch (Exception ex)
                {
                    version = Assembly.GetExecutingAssembly().GetName().Version;
                }
                var versionString = string.Format("{4} Version: {0}.{1}.{2}.{3}", version.Major, version.Minor, version.Build, version.Revision, Assembly.GetEntryAssembly().GetName().Name);
                return versionString;
            }
        }
    }
}
