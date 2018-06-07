using MvvmDialogs;
using System.Windows.Input;
using FileBackup.Views;
using FileBackup.Utils;

namespace FileBackup.ViewModels
{
    class MainViewModel : ViewModelBase
    {
        #region Parameters
        private readonly IDialogService DialogService;

        /// <summary>
        /// Title of the application, as displayed in the top bar of the window
        /// </summary>
        public string Title
        {
            get { return "FileBackup"; }
        }
        #endregion

        #region Constructors
        public MainViewModel()
        {
            // DialogService is used to handle dialogs
            this.DialogService = new MvvmDialogs.DialogService();
        }

        #endregion

        #region Methods

        #endregion

        #region Commands        
        public ICommand ShowAboutDialogCmd { get { return new RelayCommand(OnShowAboutDialog, AlwaysTrue); } }
        public ICommand ExitCmd { get { return new RelayCommand(OnExitApp, AlwaysTrue); } }
        public ICommand CopyCommand => new RelayCommand(onCopyButtonPressed, AlwaysTrue);
        public ICommand DeleteCommand => new RelayCommand(onDeleteButtonPressed, AlwaysTrue);

        private bool AlwaysTrue() { return true; }
        private bool AlwaysFalse() { return false; }
      
        private void OnShowAboutDialog()
        {
            AboutViewModel dialog = new AboutViewModel();
            var result = DialogService.ShowDialog<About>(this, dialog);
        }
        private void OnExitApp()
        {
            System.Windows.Application.Current.MainWindow.Close();
        }
        private void onCopyButtonPressed()
        {
            var copyWindow = new CopyWindow();
            App.app.Close();
            App.app = copyWindow;
            App.app.Show();
        }
        private void onDeleteButtonPressed()
        {
            var deleteWindow = new DeleteWindow();
            App.app.Close();
            App.app = deleteWindow;
            App.app.Show();
        }
        #endregion

        #region Events

        #endregion
    }
}
