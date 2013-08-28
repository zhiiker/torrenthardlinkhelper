using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.Command;
using Ookii.Dialogs.Wpf;
using TorrentHardLinkHelper.HardLink;
using TorrentHardLinkHelper.Locate;
using TorrentHardLinkHelper.Models;
using TorrentHardLinkHelper.Torrents;

namespace TorrentHardLinkHelper.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private static readonly IList<string> _outputNameTypes = new[] { "Torrent Name", "Torrent Title", "Custom" };

        private string _torrentFile;
        private string _sourceFolder;
        private string _outputBaseFolder;
        private string _outputName;
        private string _status;
        private string _outputNameType;
        private bool _isOutputNameReadonly;
        private Torrent _torrent;
        private IList<FileSystemFileInfo> _fileSystemFileInfos;
        private IList<EntityModel> _fileSystemEntityModel;
        private IList<EntityModel> _torrentEntityModel;

        private LocateResult _locateResult;

        private Style _expandAllStyle;
        private Style _collapseAllStyle;

        private RelayCommand _selectTorrentFileCommand;
        private RelayCommand _selectSourceFolderCommand;
        private RelayCommand _selectOuptputBaseFolderCommand;
        private RelayCommand _analyseCommand;
        private RelayCommand _linkCommand;
        private RelayCommand<TreeView> _expandCommand;
        private RelayCommand<TreeView> _collapseCommand;
        private RelayCommand<SelectionChangedEventArgs> _outputNameTypeChangedCommand;

        public MainViewModel()
        {
            this.InitCommands();
            this.InitStyles();
            this.Set(() => this.IsOutputNameReadonly, ref this._isOutputNameReadonly, true);
            this.UpdateStatusFormat("Ready.");
        }

        private void InitCommands()
        {
            this._selectTorrentFileCommand = new RelayCommand(() =>
            {
                var dialog = new VistaOpenFileDialog();
                dialog.Title = "Select one torrent to open";
                dialog.Filter = "Torrent Files|*.torrent";
                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                dialog.ShowDialog();
                if (dialog.FileName != null)
                {
                    this.Set(() => TorrentFile, ref this._torrentFile, dialog.FileName);
                    this.OpenTorrent();
                }
            });

            this._selectSourceFolderCommand = new RelayCommand(() =>
            {
                var dialog = new VistaFolderBrowserDialog();
                dialog.ShowNewFolderButton = true;
                dialog.ShowDialog();
                if (dialog.SelectedPath != null)
                {
                    this.Set(() => this.SourceFolder, ref this._sourceFolder, dialog.SelectedPath);
                    this.Set(() => this.FileSystemEntityModel, ref this._fileSystemEntityModel,
                        new[] { EntityModel.Load(this._sourceFolder) });
                }
            });

            this._selectOuptputBaseFolderCommand = new RelayCommand(() =>
            {
                var dialog = new VistaFolderBrowserDialog();
                dialog.ShowNewFolderButton = true;
                dialog.ShowDialog();
                if (dialog.SelectedPath != null)
                {
                    this.Set(() => this.OutputBaseFolder, ref this._outputBaseFolder, dialog.SelectedPath);
                }
            });

            this._analyseCommand = new RelayCommand(Analyse,
                () => !string.IsNullOrEmpty(this._torrentFile) && !string.IsNullOrEmpty(this._sourceFolder));

            this._linkCommand = new RelayCommand(Link,
                () => !string.IsNullOrEmpty(this._outputBaseFolder) && !string.IsNullOrEmpty(this._outputName) && this._locateResult != null);

            this._outputNameTypeChangedCommand =
                new RelayCommand<SelectionChangedEventArgs>(
                    args => ChangeOutputFolderNmae(args.AddedItems[0].ToString()));

            this._expandCommand = new RelayCommand<TreeView>(tv => { tv.ItemContainerStyle = this._expandAllStyle; });
            this._collapseCommand = new RelayCommand<TreeView>(tv => { tv.ItemContainerStyle = this._collapseAllStyle; });
        }

        private void InitStyles()
        {
            this._expandAllStyle = new Style(typeof(TreeViewItem));
            this._collapseAllStyle = new Style(typeof(TreeViewItem));

            this._expandAllStyle.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, true));
            this._collapseAllStyle.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, false));
        }

        private void UpdateStatusFormat(string format, params object[] args)
        {
            this.Set(() => this.Status, ref this._status, string.Format(format, args));
        }

        private void OpenTorrent()
        {
            if (string.IsNullOrEmpty(this._torrentFile))
            {
                return;
            }
            try
            {
                this._torrent = Torrent.Load(this._torrentFile);
                this.ChangeOutputFolderNmae(this._outputNameType);
                this.Set(() => this.TorrentEntityModel, ref this._torrentEntityModel,
                    new[] { EntityModel.Load(this._torrent) });
            }
            catch (Exception ex)
            {
                UpdateStatusFormat("Load torrent failed, exception message: {0}", ex.Message);
            }
        }

        private void ChangeOutputFolderNmae(string nameType)
        {
            if (nameType == "Custom")
            {
                this.Set(() => this.IsOutputNameReadonly, ref this._isOutputNameReadonly, false);
            }
            else
            {
                this.Set(() => this.IsOutputNameReadonly, ref this._isOutputNameReadonly, true);
            }
            if (this._torrent == null)
            {
                this.Set(() => this.OutputName, ref this._outputName, "");
                return;
            }
            switch (nameType)
            {
                case "Torrent Name":
                    this.Set(() => this.OutputName, ref this._outputName,
                        Path.GetFileNameWithoutExtension(this._torrentFile));
                    this.Set(() => this.IsOutputNameReadonly, ref this._isOutputNameReadonly, true);
                    break;
                case "Torrent Title":
                    this.Set(() => this.OutputName, ref this._outputName, this._torrent.Name);
                    this.Set(() => this.IsOutputNameReadonly, ref this._isOutputNameReadonly, true);
                    break;
            }
        }

        private async void Analyse()
        {
            this.UpdateStatusFormat("Locating...");
            LocateResult result = await Locate();

            this.UpdateStatusFormat("Successfully located {0} of {1} file(s). Mathched {2} of {3} file(s) on disk.",
                result.LocatedCount,
                result.LocatedCount + result.UnlocatedCount,
                result.TorrentFileLinks.Where(c => c.State == LinkState.Located)
                    .Select(c => c.LinkedFsFileInfo.FilePath)
                    .Distinct()
                    .Count(), this._fileSystemFileInfos.Count);
            this._locateResult = result;

            EntityModel.Update(this._fileSystemEntityModel[0],
                result.TorrentFileLinks.Where(c => c.State == LinkState.Located).Select(c => c.LinkedFsFileInfo));
            this.RaisePropertyChanged(() => this.FileSystemEntityModel);

            this.Set(() => this.TorrentEntityModel, ref this._torrentEntityModel,
                new[] { EntityModel.Load(this._torrent.Name, result) });
        }

        private Task<LocateResult> Locate()
        {
            return Task<LocateResult>.Factory.StartNew(() =>
            {
                //if (this._fileSystemFileInfos == null)
                //{
                this._fileSystemFileInfos = FileSystemFileSearcher.SearchFolder(this._sourceFolder);
                //}
                var locater = new TorrentFileLocater(this._torrent, this._fileSystemFileInfos);
                LocateResult result = locater.Locate();
                return result;
            });
        }

        private void Link()
        {
            if (Path.GetPathRoot(this._outputBaseFolder) != Path.GetPathRoot(this._sourceFolder))
            {
                this.UpdateStatusFormat("Link failed, the output basefolder and the source folder must be in the same driver!");
                return;
            }

            this.UpdateStatusFormat("Linking...");
            var helper = new HardLinkHelper(this._locateResult.TorrentFileLinks, this._outputName, this._outputBaseFolder);
            helper.HardLink();
            this.UpdateStatusFormat("Done.");
        }

        #region Properties

        public string TorrentFile
        {
            get { return this._torrentFile; }
            set { this._torrentFile = value; }
        }

        public string SourceFolder
        {
            get { return this._sourceFolder; }
            set { this._sourceFolder = value; }
        }

        public string OutputBaseFolder
        {
            get { return this._outputBaseFolder; }
            set { this._outputBaseFolder = value; }
        }

        public string OutputName
        {
            get { return this._outputName; }
            set { this._outputName = value; }
        }

        public string OutputNameType
        {
            get { return this._outputNameType; }
            set { this._outputNameType = value; }
        }

        public string Status
        {
            get { return this._status; }
        }

        public IList<string> OutputNameTypes
        {
            get { return _outputNameTypes; }
        }

        public bool IsOutputNameReadonly
        {
            get { return this._isOutputNameReadonly; }
        }

        public IList<EntityModel> FileSystemEntityModel
        {
            get { return this._fileSystemEntityModel; }
        }

        public IList<EntityModel> TorrentEntityModel
        {
            get { return this._torrentEntityModel; }
        }

        #endregion

        #region Commands

        public RelayCommand SelectTorrentFileCommand
        {
            get { return this._selectTorrentFileCommand; }
        }

        public RelayCommand SelectSourceFolder
        {
            get { return this._selectSourceFolderCommand; }
        }

        public RelayCommand SelectOutputBaseFolder
        {
            get { return this._selectOuptputBaseFolderCommand; }
        }

        public RelayCommand AnalyseCommand
        {
            get { return this._analyseCommand; }
        }

        public RelayCommand LinkCommand
        {
            get { return this._linkCommand; }
        }

        public RelayCommand<SelectionChangedEventArgs> OutputNameTypeChangedCommand
        {
            get { return this._outputNameTypeChangedCommand; }
        }

        public RelayCommand<TreeView> ExpandAllCommand
        {
            get { return this._expandCommand; }
        }

        public RelayCommand<TreeView> CollapseAllCommand
        {
            get { return this._collapseCommand; }
        }

        #endregion
    }
}