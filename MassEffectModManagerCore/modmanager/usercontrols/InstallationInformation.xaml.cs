﻿using MassEffectModManagerCore.modmanager.objects;
using MassEffectModManagerCore.ui;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Serilog;
using MassEffectModManagerCore.modmanager.helpers;
using MassEffectModManagerCore.modmanager.localizations;
using MassEffectModManagerCore.modmanager.memoryanalyzer;
using ME3ExplorerCore.Helpers;
using ME3ExplorerCore.Packages;

namespace MassEffectModManagerCore.modmanager.usercontrols
{
    /// <summary>
    /// Interaction logic for InstallationInformation.xaml
    /// </summary>
    public partial class InstallationInformation : MMBusyPanelBase
    {
        public GameTarget SelectedTarget { get; set; }
        public GameTarget PreviousTarget { get; set; }
        public string BackupLocationString { get; set; }
        public ObservableCollectionExtended<GameTarget> InstallationTargets { get; } = new ObservableCollectionExtended<GameTarget>();
        public ObservableCollectionExtended<InstalledDLCMod> DLCModsInstalled { get; } = new ObservableCollectionExtended<InstalledDLCMod>();
        public bool SFARBeingRestored { get; private set; }
        public bool BasegameFilesBeingRestored { get; private set; }

        public InstallationInformation(List<GameTarget> targetsList, GameTarget selectedTarget)
        {
            MemoryAnalyzer.AddTrackedMemoryItem(@"Installation Information Panel", new WeakReference(this));
            DataContext = this;
            InstallationTargets.AddRange(targetsList);
            LoadCommands();
            InitializeComponent();
            SelectedTarget = selectedTarget;
        }
        public ICommand RestoreAllModifiedSFARs { get; set; }
        public ICommand RestoreSPModifiedSFARs { get; set; }
        public ICommand RestoreMPModifiedSFARs { get; set; }
        public ICommand RestoreAllModifiedBasegame { get; set; }
        public ICommand CloseCommand { get; set; }
        public ICommand RemoveTargetCommand { get; set; }

        private void LoadCommands()
        {
            RestoreAllModifiedSFARs = new GenericCommand(RestoreAllSFARs, CanRestoreAllSFARs);
            RestoreMPModifiedSFARs = new GenericCommand(RestoreMPSFARs, CanRestoreMPSFARs);
            RestoreSPModifiedSFARs = new GenericCommand(RestoreSPSFARs, CanRestoreSPSFARs);
            RestoreAllModifiedBasegame = new GenericCommand(RestoreAllBasegame, CanRestoreAllBasegame);
            CloseCommand = new GenericCommand(ClosePanel, CanClose);
            RemoveTargetCommand = new GenericCommand(RemoveTarget, CanRemoveTarget);
        }

        private bool CanRestoreMPSFARs()
        {
            return IsPanelOpen && !Utilities.IsGameRunning(SelectedTarget.Game) && SelectedTarget.HasModifiedMPSFAR() && !SFARBeingRestored;
        }
        private bool CanRestoreSPSFARs()
        {
            return IsPanelOpen && !Utilities.IsGameRunning(SelectedTarget.Game) && SelectedTarget.HasModifiedSPSFAR() && !SFARBeingRestored;
        }

        private bool CanRemoveTarget() => SelectedTarget != null && !SelectedTarget.RegistryActive;

        private void RemoveTarget()
        {
            Utilities.RemoveCachedTarget(SelectedTarget);
            ClosePanel(new DataEventArgs(@"ReloadTargets"));
        }

        private void ClosePanel() { ClosePanel(DataEventArgs.Empty); }

        private void ClosePanel(DataEventArgs args)
        {
            foreach (var installationTarget in InstallationTargets)
            {
                SelectedTarget.ModifiedBasegameFilesView.Filter = null;
                installationTarget.DumpModifiedFilesFromMemory(); //will prevent memory leak
            }

            OnClosing(args);
        }

        private bool RestoreAllBasegameInProgress;

        private bool CanRestoreAllBasegame()
        {
            return IsPanelOpen && !Utilities.IsGameRunning(SelectedTarget.Game) && SelectedTarget?.ModifiedBasegameFiles.Count > 0 && !RestoreAllBasegameInProgress && BackupService.GetGameBackupPath(SelectedTarget.Game) != null; //check if ifles being restored
        }

        public string ModifiedFilesFilterText { get; set; }

        private bool FilterBasegameObject(object obj)
        {
            if (!string.IsNullOrWhiteSpace(ModifiedFilesFilterText) && obj is GameTarget.ModifiedFileObject mobj)
            {
                return mobj.FilePath.Contains(ModifiedFilesFilterText, StringComparison.InvariantCultureIgnoreCase);
            }
            return true;
        }

        public void OnModifiedFilesFilterTextChanged()
        {
            SelectedTarget?.ModifiedBasegameFilesView.Refresh();
        }

        private void RestoreAllBasegame()
        {
            bool restore = false;
            if (SelectedTarget.TextureModded)
            {
                if (!Settings.DeveloperMode)
                {
                    M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_dialogRestoringFilesWhileAlotIsInstalledNotAllowed), M3L.GetString(M3L.string_cannotRestoreSfarFiles), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                else
                {
                    var res = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_dialogRestoringFilesWhileAlotIsInstalledNotAllowedDevMode), M3L.GetString(M3L.string_invalidTexturePointersWarning), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    restore = res == MessageBoxResult.Yes;

                }
            }
            else
            {
                restore = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoreAllModifiedFilesQuestion), M3L.GetString(M3L.string_confirmRestoration), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

            }
            if (restore)
            {
                NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"RestoreAllBasegameFilesThread");
                nbw.DoWork += (a, b) =>
                {
                    RestoreAllBasegameInProgress = true;
                    var restorableFiles = SelectedTarget.ModifiedBasegameFiles.Where(x => x.CanRestoreFile()).ToList();
                    //Set UI states
                    foreach (var v in restorableFiles)
                    {
                        v.Restoring = true;
                    }
                    //Restore files
                    foreach (var v in restorableFiles) //to list will make sure this doesn't throw concurrent modification
                    {
                        v.RestoreFile(true);
                    }
                };
                nbw.RunWorkerCompleted += (a, b) =>
                {
                    if (b.Error != null)
                    {
                        Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                    }
                    RestoreAllBasegameInProgress = false;
                    if (SelectedTarget.Game == MEGame.ME3)
                    {
                        AutoTOC.RunTOCOnGameTarget(SelectedTarget);
                    }
                    CommandManager.InvalidateRequerySuggested();
                };
                nbw.RunWorkerAsync();
            }
        }

        private void RestoreSPSFARs()
        {
            bool restore;
            checkSFARRestoreForBackup();
            if (SelectedTarget.TextureModded)
            {
                if (!Settings.DeveloperMode)
                {
                    M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoringSfarsAlotBlocked), M3L.GetString(M3L.string_cannotRestoreSfarFiles), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                else
                {
                    var res = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoringSfarsAlotDevMode), M3L.GetString(M3L.string_invalidTexturePointersWarning), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    restore = res == MessageBoxResult.Yes;

                }
            }
            else
            {
                restore = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoreSPSfarsQuestion), M3L.GetString(M3L.string_confirmRestoration), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

            }
            if (restore)
            {
                foreach (var v in SelectedTarget.ModifiedSFARFiles)
                {
                    if (v.IsSPSFAR)
                    {
                        SelectedTarget.RestoreSFAR(v, true);
                    }
                }
            }
        }

        private void RestoreMPSFARs()
        {
            bool restore;
            checkSFARRestoreForBackup();
            if (SelectedTarget.TextureModded)
            {
                if (!Settings.DeveloperMode)
                {
                    M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoringSfarsAlotBlocked), M3L.GetString(M3L.string_cannotRestoreSfarFiles), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                else
                {
                    var res = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoringSfarsAlotDevMode), M3L.GetString(M3L.string_invalidTexturePointersWarning), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    restore = res == MessageBoxResult.Yes;

                }
            }
            else
            {
                restore = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoreMPSfarsQuestion), M3L.GetString(M3L.string_confirmRestoration), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

            }
            if (restore)
            {
                foreach (var v in SelectedTarget.ModifiedSFARFiles)
                {
                    if (v.IsMPSFAR)
                    {
                        SelectedTarget.RestoreSFAR(v, true);
                    }
                }
            }
        }

        private void RestoreAllSFARs()
        {
            bool restore;
            checkSFARRestoreForBackup();
            if (SelectedTarget.TextureModded)
            {
                if (!Settings.DeveloperMode)
                {
                    M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoringSfarsAlotBlocked), M3L.GetString(M3L.string_cannotRestoreSfarFiles), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                else
                {
                    var res = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoringSfarsAlotDevMode), M3L.GetString(M3L.string_invalidTexturePointersWarning), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    restore = res == MessageBoxResult.Yes;
                }
            }
            else
            {
                restore = M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoreAllModifiedSfarsQuestion), M3L.GetString(M3L.string_confirmRestoration), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }
            if (restore)
            {
                foreach (var v in SelectedTarget.ModifiedSFARFiles)
                {
                    SelectedTarget.RestoreSFAR(v, true);
                }
            }
        }

        private void checkSFARRestoreForBackup()
        {
            var bup = BackupService.GetGameBackupPath(SelectedTarget.Game);
            if (bup == null)
            {
                M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_dialog_restoringSFARWithoutBackup),
                    M3L.GetString(M3L.string_backupNotAvailable), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool CanRestoreAllSFARs()
        {
            return IsPanelOpen && !Utilities.IsGameRunning(SelectedTarget.Game) && SelectedTarget.ModifiedSFARFiles.Count > 0 && !SFARBeingRestored;
        }

        /// <summary>
        /// This method is run on a background thread so all UI calls needs to be wrapped
        /// </summary>
        private void PopulateUI()
        {
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += (a, b) =>
            {
                bool deleteConfirmationCallback(InstalledDLCMod mod)
                {
                    if (Utilities.IsGameRunning(SelectedTarget.Game))
                    {
                        M3L.ShowDialog(Window.GetWindow(this),
                            M3L.GetString(M3L.string_interp_cannotDeleteModsWhileXIsRunning,
                                Utilities.GetGameName(SelectedTarget.Game)), M3L.GetString(M3L.string_gameRunning),
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    if (SelectedTarget.TextureModded)
                    {
                        var res = M3L.ShowDialog(Window.GetWindow(this),
                            M3L.GetString(M3L.string_interp_deletingXwhileAlotInstalledUnsupported, mod.ModName),
                            M3L.GetString(M3L.string_deletingWillPutAlotInUnsupportedConfig), MessageBoxButton.YesNo,
                            MessageBoxImage.Error);
                        return res == MessageBoxResult.Yes;
                    }

                    return M3L.ShowDialog(Window.GetWindow(this),
                        M3L.GetString(M3L.string_interp_removeXFromTheGameInstallationQuestion, mod.ModName),
                        M3L.GetString(M3L.string_confirmDeletion), MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes;
                }

                void notifyDeleted()
                {
                    PopulateUI();
                }

                SelectedTarget.PopulateDLCMods(true, deleteConfirmationCallback, notifyDeleted);
                SelectedTarget.PopulateExtras();
                SelectedTarget.PopulateTextureInstallHistory();
                bool restoreBasegamefileConfirmationCallback(string filepath)
                {
                    if (Utilities.IsGameRunning(SelectedTarget.Game))
                    {
                        M3L.ShowDialog(Window.GetWindow(this),
                            M3L.GetString(M3L.string_interp_cannotRestoreFilesWhileXIsRunning,
                                Utilities.GetGameName(SelectedTarget.Game)), M3L.GetString(M3L.string_gameRunning),
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    if (SelectedTarget.TextureModded && filepath.RepresentsPackageFilePath())
                    {
                        if (!Settings.DeveloperMode)
                        {
                            M3L.ShowDialog(Window.GetWindow(this),
                                M3L.GetString(M3L.string_interp_restoringXWhileAlotInstalledIsNotAllowed,
                                    Path.GetFileName(filepath)), M3L.GetString(M3L.string_cannotRestorePackageFiles),
                                MessageBoxButton.OK, MessageBoxImage.Error);
                            return false;
                        }
                        else
                        {
                            var res = M3L.ShowDialog(Window.GetWindow(this),
                                M3L.GetString(M3L.string_interp_restoringXwhileAlotInstalledLikelyBreaksThingsDevMode,
                                    Path.GetFileName(filepath)),
                                M3L.GetString(M3L.string_invalidTexturePointersWarning), MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            return res == MessageBoxResult.Yes;

                        }
                    }

                    bool? holdingShift = Keyboard.Modifiers == ModifierKeys.Shift;
                    if (!holdingShift.Value) holdingShift = null;
                    return holdingShift ?? M3L.ShowDialog(Window.GetWindow(this),
                        M3L.GetString(M3L.string_interp_restoreXquestion, Path.GetFileName(filepath)),
                        M3L.GetString(M3L.string_confirmRestoration), MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes;
                }

                bool restoreSfarConfirmationCallback(string sfarPath)
                {
                    if (Utilities.IsGameRunning(SelectedTarget.Game))
                    {
                        M3L.ShowDialog(Window.GetWindow(this),
                            M3L.GetString(M3L.string_interp_cannotRestoreFilesWhileXIsRunning,
                                Utilities.GetGameName(SelectedTarget.Game)), M3L.GetString(M3L.string_gameRunning),
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    if (SelectedTarget.TextureModded)
                    {
                        if (!Settings.DeveloperMode)
                        {
                            M3L.ShowDialog(Window.GetWindow(this), M3L.GetString(M3L.string_restoringSfarsAlotBlocked),
                                M3L.GetString(M3L.string_cannotRestoreSfarFiles), MessageBoxButton.OK,
                                MessageBoxImage.Error);
                            return false;
                        }
                        else
                        {
                            var res = M3L.ShowDialog(Window.GetWindow(this),
                                M3L.GetString(M3L.string_restoringSfarsAlotDevMode),
                                M3L.GetString(M3L.string_invalidTexturePointersWarning), MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            return res == MessageBoxResult.Yes;

                        }
                    }

                    //Todo: warn of unpacked file deletion
                    bool? holdingShift = Keyboard.Modifiers == ModifierKeys.Shift;
                    if (!holdingShift.Value) holdingShift = null;
                    return holdingShift ?? M3L.ShowDialog(Window.GetWindow(this),
                        M3L.GetString(M3L.string_interp_restoreXquestion, sfarPath),
                        M3L.GetString(M3L.string_confirmRestoration), MessageBoxButton.YesNo,
                        MessageBoxImage.Warning) == MessageBoxResult.Yes;
                }

                void notifyStartingSfarRestoreCallback()
                {
                    SFARBeingRestored = true;
                }

                void notifyStartingBasegameFileRestoreCallback()
                {
                    BasegameFilesBeingRestored = true;
                }

                void notifyRestoredCallback(object itemRestored)
                {
                    if (itemRestored is GameTarget.ModifiedFileObject mf)
                    {
                        Application.Current.Dispatcher.Invoke(delegate
                        {
                            SelectedTarget.ModifiedBasegameFiles.Remove(mf);
                        });
                        bool resetBasegameFilesBeingRestored = SelectedTarget.ModifiedBasegameFiles.Count == 0;
                        if (!resetBasegameFilesBeingRestored)
                        {
                            resetBasegameFilesBeingRestored =
                                !SelectedTarget.ModifiedBasegameFiles.Any(x => x.Restoring);
                        }

                        if (resetBasegameFilesBeingRestored)
                        {
                            BasegameFilesBeingRestored = false;
                        }
                    }
                    else if (itemRestored is GameTarget.SFARObject ms)
                    {
                        if (!ms.IsModified)
                        {
                            SelectedTarget.ModifiedSFARFiles.Remove(ms);
                        }

                        bool resetSfarsBeingRestored = SelectedTarget.ModifiedSFARFiles.Count == 0;
                        if (!resetSfarsBeingRestored)
                        {
                            resetSfarsBeingRestored = !SelectedTarget.ModifiedSFARFiles.Any(x => x.Restoring);
                        }

                        if (resetSfarsBeingRestored)
                        {
                            SFARBeingRestored = false;
                        }
                    }
                    else if (itemRestored == null)
                    {
                        //restore failed.
                        bool resetSfarsBeingRestored = SelectedTarget.ModifiedSFARFiles.Count == 0;
                        if (!resetSfarsBeingRestored)
                        {
                            resetSfarsBeingRestored = !SelectedTarget.ModifiedSFARFiles.Any(x => x.Restoring);
                        }

                        if (resetSfarsBeingRestored)
                        {
                            SFARBeingRestored = false;
                        }

                        bool resetBasegameFilesBeingRestored = SelectedTarget.ModifiedBasegameFiles.Count == 0;
                        if (!resetBasegameFilesBeingRestored)
                        {
                            resetBasegameFilesBeingRestored =
                                !SelectedTarget.ModifiedBasegameFiles.Any(x => x.Restoring);
                        }

                        if (resetBasegameFilesBeingRestored)
                        {
                            BasegameFilesBeingRestored = false;
                        }
                    }
                }

                SelectedTarget.PopulateModifiedBasegameFiles(restoreBasegamefileConfirmationCallback,
                    restoreSfarConfirmationCallback,
                    notifyStartingSfarRestoreCallback,
                    notifyStartingBasegameFileRestoreCallback,
                    notifyRestoredCallback);
                SFARBeingRestored = false;

                SelectedTarget.PopulateASIInfo();
                SelectedTarget.PopulateBinkInfo();

                if (SelectedTarget != null && !SelectedTarget.TextureModded)
                {
                    NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"BasegameSourceIdentifier");
                    nbw.DoWork += (a, b) =>
                    {
                        foreach (var v in SelectedTarget.ModifiedBasegameFiles.ToList())
                        {
                            v.DetermineSource();
                        }
                    };
                    nbw.RunWorkerAsync();
                }
            };
            bw.RunWorkerAsync();
        }

        public void OnSelectedTargetChanged()
        {
            if (PreviousTarget != null)
            {
                PreviousTarget.ModifiedBasegameFilesView.Filter = null;
            }
            DLCModsInstalled.ClearEx();

            //Get installed mod information
            //NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"InstallationInformationDataPopulator");
            //nbw.DoWork += (a, b) =>
            //{
            if (SelectedTarget != null)
            {
                PopulateUI();
                var backupLoc = BackupService.GetGameBackupPath(SelectedTarget.Game);
                if (backupLoc != null)
                {
                    BackupLocationString = M3L.GetString(M3L.string_interp_backupAtX, backupLoc);
                }
                else
                {
                    BackupLocationString = M3L.GetString(M3L.string_noBackupForThisGame);
                }

                SelectedTarget.ModifiedBasegameFilesView.Filter = FilterBasegameObject;
            }
            else
            {
                BackupLocationString = null;
            }
            //};
            //nbw.RunWorkerCompleted += (await, b) =>
            //{
            //    if (b.Error != null)
            //    {
            //        Log.Error($@"Error in installation information data populator: {b.Error.Message}");
            //    }
            //};
            //nbw.RunWorkerAsync();
            PreviousTarget = SelectedTarget;
        }

        public class InstalledDLCMod : INotifyPropertyChanged
        {
            private string dlcFolderPath;

            //Fody uses this property on weaving
#pragma warning disable
public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore
            public string EnableDisableText => DLCFolderName.StartsWith(@"xDLC") ? M3L.GetString(M3L.string_enable) : M3L.GetString(M3L.string_disable);
            public string EnableDisableTooltip { get; set; }
            public string ModName { get; private set; }
            public string DLCFolderName { get; private set; }
            public string DLCFolderNameString { get; private set; }
            public string InstalledBy { get; private set; }
            public string Version { get; private set; }
            public string InstallerInstanceBuild { get; private set; }

            public ObservableCollectionExtended<string> IncompatibleDLC { get; } = new ObservableCollectionExtended<string>();
            public ObservableCollectionExtended<string> ChosenInstallOptions { get; } = new ObservableCollectionExtended<string>();

            private MEGame game;
            private static readonly SolidColorBrush DisabledBrushLightMode = new SolidColorBrush(Color.FromArgb(0xff, 232, 26, 26));
            private static readonly SolidColorBrush DisabledBrushDarkMode = new SolidColorBrush(Color.FromArgb(0xff, 247, 88, 77));

            private Func<InstalledDLCMod, bool> deleteConfirmationCallback;
            private Action notifyDeleted;
            public SolidColorBrush TextColor
            {
                get
                {
                    if (DLCFolderName.StartsWith('x'))
                    {
                        return Settings.DarkTheme ? DisabledBrushDarkMode : DisabledBrushLightMode;
                    }
                    return Application.Current.FindResource(AdonisUI.Brushes.ForegroundBrush) as SolidColorBrush;
                }
            }
            /// <summary>
            /// Indicates that this mod was installed by ALOT Installer or Mod Manager.
            /// </summary>
            public bool InstalledByManagedSolution { get; private set; }
            public void OnDLCFolderNameChanged()
            {
                dlcFolderPath = Path.Combine(Directory.GetParent(dlcFolderPath).FullName, DLCFolderName);
                parseMetaCmm(DLCFolderName.StartsWith('x'), false);
                TriggerPropertyChangedFor(nameof(TextColor));
            }

            public InstalledDLCMod(string dlcFolderPath, MEGame game, Func<InstalledDLCMod, bool> deleteConfirmationCallback, Action notifyDeleted, bool modNamePrefersTPMI)
            {
                this.dlcFolderPath = dlcFolderPath;
                this.game = game;
                var dlcFolderName = DLCFolderNameString = Path.GetFileName(dlcFolderPath);
                if (App.ThirdPartyIdentificationService[game.ToString()].TryGetValue(dlcFolderName.TrimStart('x'), out var tpmi))
                {
                    ModName = tpmi.modname;
                }
                else
                {
                    ModName = dlcFolderName;
                }

                DLCFolderName = dlcFolderName;
                this.deleteConfirmationCallback = deleteConfirmationCallback;
                this.notifyDeleted = notifyDeleted;
                DeleteCommand = new RelayCommand(DeleteDLCMod, CanDeleteDLCMod);
                EnableDisableCommand = new GenericCommand(ToggleDLC, CanToggleDLC);

            }

            private void parseMetaCmm(bool disabled, bool modNamePrefersTPMI)
            {
                DLCFolderNameString = DLCFolderName.TrimStart('x'); //this string is not to show M3L.GetString(M3L.string_disabled)
                var metaFile = Path.Combine(dlcFolderPath, @"_metacmm.txt");
                if (File.Exists(metaFile))
                {
                    InstalledByManagedSolution = true;
                    InstalledBy = M3L.GetString(M3L.string_installedByModManager); //Default value when finding metacmm.
                    MetaCMM mcmm = new MetaCMM(metaFile);
                    if (DLCFolderNameString != ModName && mcmm.ModName != ModName)
                    {
                        DLCFolderNameString += $@" ({ModName})";
                        if (!modNamePrefersTPMI || ModName == null)
                        {
                            ModName = mcmm.ModName;
                        }
                    }

                    Version = mcmm.Version;
                    InstallerInstanceBuild = mcmm.InstalledBy;
                    if (int.TryParse(InstallerInstanceBuild, out var _))
                    {
                        InstalledBy = M3L.GetString(M3L.string_installedByModManager);
                    }
                    else
                    {
                        InstalledBy = M3L.GetString(M3L.string_interp_installedByX, InstallerInstanceBuild);
                    }

                    // MetaCMM Extended
                    if (mcmm.OptionsSelectedAtInstallTime != null)
                    {
                        ChosenInstallOptions.ReplaceAll(mcmm.OptionsSelectedAtInstallTime);
                    }
                    if (mcmm.IncompatibleDLC != null)
                    {
                        IncompatibleDLC.ReplaceAll(mcmm.IncompatibleDLC);
                    }
                }
                else
                {
                    InstalledBy = M3L.GetString(M3L.string_notInstalledByModManager);
                }
                if (disabled)
                {
                    DLCFolderNameString += @" - " + M3L.GetString(M3L.string_disabled);
                }
            }

            private void ToggleDLC()
            {
                var source = dlcFolderPath;
                var dlcdir = Directory.GetParent(dlcFolderPath).FullName;
                var isBecomingDisabled = DLCFolderName.StartsWith(@"DLC"); //about to change to xDLC, so it's becoming disabled
                var newdlcname = DLCFolderName.StartsWith(@"xDLC") ? DLCFolderName.TrimStart('x') : @"x" + DLCFolderName;
                var target = Path.Combine(dlcdir, newdlcname);
                try
                {
                    Directory.Move(source, target);
                    DLCFolderName = newdlcname;
                    dlcFolderPath = target;
                    EnableDisableTooltip = M3L.GetString(isBecomingDisabled ? M3L.string_tooltip_enableDLC : M3L.string_tooltip_disableDLC);
                }
                catch (Exception e)
                {
                    Log.Error(@"Unable to toggle DLC: " + e.Message);
                }
                //TriggerPropertyChangedFor(nameof(DLCFolderName));
            }
            private void TriggerPropertyChangedFor(string propertyname)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
            }
            private bool CanToggleDLC() => (game == MEGame.ME3 || DLCFolderName.StartsWith('x')) && !Utilities.IsGameRunning(game);

            public bool EnableDisableVisible => game == MEGame.ME3 || DLCFolderName.StartsWith('x');
            public ICommand DeleteCommand { get; set; }
            public GenericCommand EnableDisableCommand { get; set; }
            private bool CanDeleteDLCMod(object obj) => !Utilities.IsGameRunning(game);

            private void DeleteDLCMod(object obj)
            {
                if (obj is GameTarget gt)
                {
                    bool? holdingShift = Keyboard.Modifiers == ModifierKeys.Shift;
                    if (!holdingShift.Value) holdingShift = null;
                    var confirmDelete = holdingShift ?? deleteConfirmationCallback?.Invoke(this);
                    if (confirmDelete.HasValue && confirmDelete.Value)
                    {
                        Log.Information(@"Deleting DLC mod from target: " + dlcFolderPath);
                        Utilities.DeleteFilesAndFoldersRecursively(dlcFolderPath);
                        notifyDeleted?.Invoke();
                    }

                }
            }

            public void ClearHandlers()
            {
                deleteConfirmationCallback = null;
                notifyDeleted = null;
            }
        }



        private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            Utilities.OpenExplorer(SelectedTarget.TargetPath);
        }

        public override void HandleKeyPress(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && CanClose())
            {
                ClosePanel(DataEventArgs.Empty);
            }
        }

        private bool CanClose()
        {
            return !SFARBeingRestored && !BasegameFilesBeingRestored;
        }

        public override void OnPanelVisible()
        {
        }

        private void OpenASIManager_Click(object sender, RequestNavigateEventArgs e)
        {
            ClosePanel(new DataEventArgs(@"ASIManager"));
        }

        private void OpenALOTInstaller_Click(object sender, RoutedEventArgs e)
        {
            ClosePanel(new DataEventArgs(@"ALOTInstaller"));
        }
    }
}
