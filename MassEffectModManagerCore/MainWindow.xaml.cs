﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Xml;
using System.Xml.Linq;
using AdonisUI;
using MassEffect3.Coalesce;
using MassEffectModManagerCore.modmanager;
using MassEffectModManagerCore.modmanager.asi;
using MassEffectModManagerCore.modmanager.gameini;
using MassEffectModManagerCore.modmanager.helpers;
using MassEffectModManagerCore.modmanager.localizations;
using MassEffectModManagerCore.modmanager.me3tweaks;
using MassEffectModManagerCore.modmanager.memoryanalyzer;
using MassEffectModManagerCore.modmanager.nexusmodsintegration;
using MassEffectModManagerCore.modmanager.objects;
using MassEffectModManagerCore.modmanager.usercontrols;
using MassEffectModManagerCore.modmanager.windows;
using MassEffectModManagerCore.ui;
using ME3ExplorerCore;
using ME3ExplorerCore.GameFilesystem;
using ME3ExplorerCore.Gammtek.Extensions.Collections.Generic;
using ME3ExplorerCore.Packages;
using ME3ExplorerCore.TLK.ME2ME3;
using Microsoft.AppCenter.Analytics;
using Microsoft.AppCenter.Crashes;
using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Dialogs;
using Serilog;
using ME3ExplorerCore.Helpers;
using Pathoschild.FluentNexus.Models;
using Mod = MassEffectModManagerCore.modmanager.objects.mod.Mod;

namespace MassEffectModManagerCore
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public string CurrentOperationText { get; set; } = M3L.GetString(M3L.string_startingUp);

        public bool IsBusy { get; set; }

        /// <summary>
        /// Content of the current Busy Indicator modal
        /// </summary>
        public object BusyContent { get; set; }

        public string CurrentDescriptionText { get; set; } = DefaultDescriptionText;
        private static readonly string DefaultDescriptionText = M3L.GetString(M3L.string_selectModOnLeftToGetStarted);
        private readonly string[] SupportedDroppableExtensions = { @".rar", @".zip", @".7z", @".exe", @".tpf", @".mod", @".mem", @".me2mod", @".xml", @".bin", @".tlk", @".par" };
        public string ApplyModButtonText { get; set; } = M3L.GetString(M3L.string_applyMod);
        public string InstallationTargetText { get; set; } = M3L.GetString(M3L.string_installationTarget);
        public bool ME1ASILoaderInstalled { get; set; }
        public bool ME2ASILoaderInstalled { get; set; }
        public bool ME3ASILoaderInstalled { get; set; }
        public bool ME1ModsVisible { get; set; } = true;
        public bool ME2ModsVisible { get; set; } = true;
        public bool ME3ModsVisible { get; set; } = true;

        public bool ME1NexusEndorsed { get; set; }
        public bool ME2NexusEndorsed { get; set; }
        public bool ME3NexusEndorsed { get; set; }

        public string VisitWebsiteText { get; set; }
        public string ME1ASILoaderText { get; set; }
        public string ME2ASILoaderText { get; set; }
        public string ME3ASILoaderText { get; set; }
        public string EndorseM3String { get; set; } = M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);

        private int oldFailedBindableCount = 0;

        public string NoModSelectedText
        {
            get
            {
                var retvar = M3L.GetString(M3L.string_selectModOnLeftToGetStarted);
                //TODO: Implement Localized Tips Service
                if (LoadedTips.Count > 0)
                {
                    var randomTip = LoadedTips.RandomElement();
                    retvar += $"\n\n---------------------------------------------\n{randomTip}"; //do not localize
                }

                return retvar;
            }
        }

        public string FailedModsString { get; set; }
        public string NexusLoginInfoString { get; set; } = M3L.GetString(M3L.string_loginToNexusMods);

        /// <summary>
        /// User controls that are queued for displaying when the previous one has closed.
        /// </summary>
        private Queue<MMBusyPanelBase> queuedUserControls = new Queue<MMBusyPanelBase>();

        /// <summary>
        /// The backend libraries and game targets have initially loaded
        /// </summary>
        public bool StartedUp { get; set; }
        /// <summary>
        /// The currently selected mod
        /// </summary>
        public Mod SelectedMod { get; set; }
        /// <summary>
        /// Mods currently visible in the left panel
        /// </summary>
        public ObservableCollectionExtended<Mod> VisibleFilteredMods { get; } = new ObservableCollectionExtended<Mod>();
        /// <summary>
        /// All mods that successfully loaded.
        /// </summary>
        public ObservableCollectionExtended<Mod> AllLoadedMods { get; } = new ObservableCollectionExtended<Mod>();
        /// <summary>
        /// All mods that failed to load
        /// </summary>
        public ObservableCollectionExtended<Mod> FailedMods { get; } = new ObservableCollectionExtended<Mod>();
        public ObservableCollectionExtended<GameTarget> InstallationTargets { get; } = new ObservableCollectionExtended<GameTarget>();

        private BackgroundTaskEngine backgroundTaskEngine;

        //private ModLoader modLoadSer;
        public MainWindow()
        {
            if (App.UpgradingFromME3CMM/* || true*/)
            {
                App.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                //Show migration window before we load the main UI
                Log.Information(@"Migrating from ME3CMM - showing migration dialog");
                new ME3CMMMigrationWindow().ShowDialog();
                App.Current.MainWindow = this;
                App.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            LoadCommands();
            SetTheme();
            InitializeComponent();
            languageMenuItems = new Dictionary<string, MenuItem>()
            {
                {@"int", LanguageINT_MenuItem},
                {@"rus", LanguageRUS_MenuItem},
                {@"pol", LanguagePOL_MenuItem},
                {@"deu", LanguageDEU_MenuItem},
                {@"bra", LanguageBRA_MenuItem},
                //{@"fra", LanguageFRA_MenuItem}
                //{@"esn", LanguageESN_MenuItem}
            };

            //Change language if not INT
            if (App.InitialLanguage != @"int")
            {
                SetApplicationLanguage(App.InitialLanguage, true);
            }

            CheckProgramDataWritable();
            AttachListeners();
            //Must be done after UI has initialized
            //if (InstallationTargets.Count > 0)
            //{
            //    SelectedGameTarget = InstallationTargets[0];
            //}

            Storyboard openLoadingSpinner = null, closeLoadingSpinner = null;

            backgroundTaskEngine = new BackgroundTaskEngine(updateText =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    CurrentOperationText = updateText;
                });
            },
                () =>
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        if (openLoadingSpinner == null)
                        {
                            openLoadingSpinner = FindResource(@"OpenLoadingSpinner") as Storyboard;
                        }

                        Storyboard.SetTarget(openLoadingSpinner, LoadingSpinner_Image);
                        openLoadingSpinner.Begin();
                    });
                },
                () =>
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        if (closeLoadingSpinner == null)
                        {
                            closeLoadingSpinner = FindResource(@"CloseLoadingSpinner") as Storyboard;
                        }
                        Storyboard.SetTarget(closeLoadingSpinner, LoadingSpinner_Image);
                        closeLoadingSpinner.Begin();
                    });
                }
            );
        }

        private void CheckProgramDataWritable()
        {
            Log.Information(@"Checking settings.ini is writable (ProgramData check)...");
            var settingsResult = Settings.SaveTest();
            if (settingsResult == Settings.SettingsSaveResult.FAILED_UNAUTHORIZED)
            {
                Log.Error(@"No permissions to appdata! Prompting for user to grant consent");
                var result = M3L.ShowDialog(null, M3L.GetString(M3L.string_dialog_multiUserProgramDataWindowsRestrictions), M3L.GetString(M3L.string_grantingWritePermissions), MessageBoxButton.OKCancel, MessageBoxImage.Error);
                if (result == MessageBoxResult.OK)
                {
                    bool done = Utilities.CreateDirectoryWithWritePermission(Utilities.GetAppDataFolder(), true);
                    if (done)
                    {
                        Log.Information(@"Granted permissions to ProgramData");
                    }
                    else
                    {
                        Log.Error(@"User declined consenting permissions to ProgramData!");
                        M3L.ShowDialog(null, M3L.GetString(M3L.string_dialog_programWillNotRunCorrectly), M3L.GetString(M3L.string_programDataAccessDenied), MessageBoxButton.OK, MessageBoxImage.Error);

                    }
                }
                else
                {
                    Log.Error(@"User denied granting permissions!");
                    M3L.ShowDialog(null, M3L.GetString(M3L.string_dialog_programWillNotRunCorrectly), M3L.GetString(M3L.string_programDataAccessDenied), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                Log.Information(@"settings.ini is writable");
            }
        }

        public async void RefreshNexusStatus(bool languageUpdateOnly = false)
        {
            if (NexusModsUtilities.HasAPIKey)
            {
                NexusLoginInfoString = M3L.GetString(M3L.string_endorsementsEnabled);
                if (!languageUpdateOnly)
                {
                    var loggedIn = await AuthToNexusMods();
                    if (loggedIn == null)
                    {
                        Log.Error(@"Error authorizing to NexusMods, did not get response from server or issue occurred while checking credentials. Setting not authorized");
                        SetNexusNotAuthorizedUI();
                    }
                }

                //prevent reseting ui to not authorized
                return;
            }

            SetNexusNotAuthorizedUI();
        }

        private void SetNexusNotAuthorizedUI()
        {
            NexusLoginInfoString = M3L.GetString(M3L.string_loginToNexusMods);
            NexusUsername = null;
            NexusUserID = 0;
            ME1NexusEndorsed = ME2NexusEndorsed = ME3NexusEndorsed = false;
            EndorseM3String = M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
        }

        private async Task<User> AuthToNexusMods(bool languageUpdateOnly = false)
        {
            if (languageUpdateOnly)
            {
                if (NexusUsername != null)
                {
                    EndorseM3String = (ME1NexusEndorsed || ME2NexusEndorsed || ME3NexusEndorsed) ? M3L.GetString(M3L.string_endorsedME3TweaksModManagerOnNexusMods) : M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
                }
                else
                {
                    EndorseM3String = M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
                }
                return null;
            }

            Log.Information(@"Authenticating to NexusMods...");
            var userInfo = await NexusModsUtilities.AuthToNexusMods();
            if (userInfo != null)
            {
                Log.Information(@"Authenticated to NexusMods");
                NexusUsername = userInfo.Name;
                NexusUserID = userInfo.UserID;

                //ME1
                var me1Status = await NexusModsUtilities.GetEndorsementStatusForFile(@"masseffect", 149, NexusUserID);
                ME1NexusEndorsed = me1Status ?? false;

                //ME2
                var me2Status = await NexusModsUtilities.GetEndorsementStatusForFile(@"masseffect2", 248, NexusUserID);
                ME2NexusEndorsed = me2Status ?? false;

                //ME3
                var me3Status = await NexusModsUtilities.GetEndorsementStatusForFile(@"masseffect3", 373, NexusUserID);
                ME3NexusEndorsed = me3Status ?? false;

                EndorseM3String = (ME1NexusEndorsed || ME2NexusEndorsed || ME3NexusEndorsed) ? M3L.GetString(M3L.string_endorsedME3TweaksModManagerOnNexusMods) : M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
            }
            else
            {
                Log.Information(@"Did not authenticate to NexusMods. May not be logged in or there was network issue");
                EndorseM3String = M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
            }

            return userInfo;
        }

        private void AttachListeners()
        {
            FailedMods.PublicPropertyChanged += (a, b) =>
            {
                if (b.PropertyName == @"BindableCount")
                {
                    bool isopening = FailedMods.BindableCount > 0 && oldFailedBindableCount == 0;
                    bool isclosing = FailedMods.BindableCount == 0 && oldFailedBindableCount > 0;
                    if (FailedMods.BindableCount > 0)
                    {
                        FailedModsString = M3L.GetString(M3L.string_interp_XmodsFailedToLoad, FailedMods.BindableCount.ToString());
                    }
                    else
                    {
                        FailedModsString = @"";
                    }

                    if (isclosing || isopening)
                    {
                        Application.Current.Dispatcher.Invoke(delegate
                        {
                            Storyboard sb = this.FindResource(isopening ? @"OpenWebsitePanel" : @"CloseWebsitePanel") as Storyboard;
                            if (sb.IsSealed)
                            {
                                sb = sb.Clone();
                            }

                            Storyboard.SetTarget(sb, FailedModsPopupPanel);
                            sb.Begin();
                        });
                    }

                    oldFailedBindableCount = FailedMods.BindableCount;
                }
            };
        }
        public ICommand OpenASIManagerCommand { get; set; }
        public ICommand OpenTutorialCommand { get; set; }
        public ICommand OriginInGameOverlayDisablerCommand { get; set; }
        public ICommand ModdescEditorCommand { get; set; }
        public ICommand LaunchEGMSettingsCommand { get; set; }
        public ICommand OfficialDLCTogglerCommand { get; set; }
        public ICommand ImportArchiveCommand { get; set; }
        public ICommand ReloadModsCommand { get; set; }
        public ICommand ConflictDetectorCommand { get; set; }
        public ICommand ApplyModCommand { get; set; }
        public ICommand RestoreCommand { get; set; }
        public ICommand CheckForContentUpdatesCommand { get; set; }
        public ICommand AddTargetCommand { get; set; }
        public ICommand RunGameConfigToolCommand { get; set; }
        public ICommand Binkw32Command { get; set; }
        public ICommand StartGameCommand { get; set; }
        public ICommand ShowinstallationInformationCommand { get; set; }
        public ICommand BackupCommand { get; set; }
        public ICommand DeployModCommand { get; set; }
        public ICommand DeleteModFromLibraryCommand { get; set; }
        public ICommand SubmitTelemetryForModCommand { get; set; }
        public ICommand SelectedModCheckForUpdatesCommand { get; set; }
        public ICommand RestoreModFromME3TweaksCommand { get; set; }
        public ICommand GrantWriteAccessCommand { get; set; }
        public ICommand AutoTOCCommand { get; set; }
        public ICommand ConsoleKeyKeybinderCommand { get; set; }
        public ICommand LoginToNexusCommand { get; set; }
        public GenericCommand EndorseSelectedModCommand { get; set; }
        public ICommand CreateTestArchiveCommand { get; set; }
        public ICommand LaunchIniModderCommand { get; set; }
        public ICommand EndorseM3OnNexusCommand { get; set; }
        public ICommand DownloadModMakerModCommand { get; set; }
        public ICommand UpdaterServiceCommand { get; set; }
        public ICommand UpdaterServiceSettingsCommand { get; set; }
        public ICommand MixinLibraryCommand { get; set; }
        public ICommand BatchModInstallerCommand { get; set; }
        public ICommand ImportDLCModFromGameCommand { get; set; }
        public ICommand BackupFileFetcherCommand { get; set; }
        public ICommand OpenModDescCommand { get; set; }
        public ICommand CheckAllModsForUpdatesCommand { get; set; }
        public ICommand CustomKeybindsInjectorCommand { get; set; }
        public ICommand NexusModsFileSearchCommand { get; set; }
        private void LoadCommands()
        {
            ReloadModsCommand = new GenericCommand(ReloadMods, CanReloadMods);
            ApplyModCommand = new GenericCommand(CallApplyMod, CanApplyMod);
            CheckForContentUpdatesCommand = new GenericCommand(CheckForContentUpdates, NetworkThreadNotRunning);
            AddTargetCommand = new GenericCommand(AddTarget, () => ModsLoaded);
            RunGameConfigToolCommand = new RelayCommand(RunGameConfigTool, CanRunGameConfigTool);
            Binkw32Command = new RelayCommand(ToggleBinkw32, CanToggleBinkw32);
            StartGameCommand = new GenericCommand(StartGame, CanStartGame);
            ShowinstallationInformationCommand = new GenericCommand(ShowInstallInfo, CanShowInstallInfo);
            BackupCommand = new GenericCommand(ShowBackupPane, ContentCheckNotInProgress);
            RestoreCommand = new GenericCommand(ShowRestorePane, ContentCheckNotInProgress);
            DeployModCommand = new GenericCommand(ShowDeploymentPane, CanShowDeploymentPane);
            DeleteModFromLibraryCommand = new GenericCommand(DeleteModFromLibraryWrapper, CanDeleteModFromLibrary);
            ImportArchiveCommand = new GenericCommand(OpenArchiveSelectionDialog, CanOpenArchiveSelectionDialog);
            SubmitTelemetryForModCommand = new GenericCommand(SubmitTelemetryForMod, CanSubmitTelemetryForMod);
            SelectedModCheckForUpdatesCommand = new GenericCommand(CheckSelectedModForUpdate, SelectedModIsME3TweaksUpdatable);
            RestoreModFromME3TweaksCommand = new GenericCommand(RestoreSelectedMod, SelectedModIsME3TweaksUpdatable);
            GrantWriteAccessCommand = new GenericCommand(() => CheckTargetPermissions(true, true), HasAtLeastOneTarget);
            AutoTOCCommand = new GenericCommand(RunAutoTOCOnTarget, HasME3Target);
            ConsoleKeyKeybinderCommand = new GenericCommand(OpenConsoleKeyKeybinder, CanOpenConsoleKeyKeybinder);
            LoginToNexusCommand = new GenericCommand(ShowNexusPanel, CanShowNexusPanel);
            EndorseSelectedModCommand = new GenericCommand(EndorseWrapper, CanEndorseMod);
            CreateTestArchiveCommand = new GenericCommand(CreateTestArchive, CanCreateTestArchive);
            LaunchIniModderCommand = new GenericCommand(OpenMEIM, CanOpenMEIM);
            EndorseM3OnNexusCommand = new GenericCommand(EndorseM3, CanEndorseM3);
            DownloadModMakerModCommand = new GenericCommand(OpenModMakerPanel, CanOpenModMakerPanel);
            UpdaterServiceCommand = new GenericCommand(OpenUpdaterServicePanel, CanOpenUpdaterServicePanel);
            UpdaterServiceSettingsCommand = new GenericCommand(OpenUpdaterServicePanelEditorMode);
            MixinLibraryCommand = new GenericCommand(OpenMixinManagerPanel, CanOpenMixinManagerPanel);
            BatchModInstallerCommand = new GenericCommand(OpenBatchModPanel, CanOpenBatchModPanel);
            ImportDLCModFromGameCommand = new GenericCommand(OpenImportFromGameUI, CanOpenImportFromUI);
            BackupFileFetcherCommand = new GenericCommand(OpenBackupFileFetcher);
            ConflictDetectorCommand = new GenericCommand(OpenConflictDetector);
            OfficialDLCTogglerCommand = new GenericCommand(OpenOfficialDLCToggler);
            LaunchEGMSettingsCommand = new GenericCommand(() =>
            {
                var target = GetCurrentTarget(MEGame.ME3);
                if (target != null)
                {
                    LaunchExternalTool(ExternalToolLauncher.EGMSettings, target.TargetPath);
                }
            }, CanLaunchEGMSettings);
            OpenModDescCommand = new GenericCommand(OpenModDesc);
            CheckAllModsForUpdatesCommand = new GenericCommand(CheckAllModsForUpdatesWrapper, () => ModsLoaded);
            CustomKeybindsInjectorCommand = new GenericCommand(OpenKeybindsInjector, () => ModsLoaded && InstallationTargets.Any(x => x.Game == MEGame.ME3));
            ModdescEditorCommand = new GenericCommand(OpenModDescEditor, CanOpenModdescEditor);
            OriginInGameOverlayDisablerCommand = new GenericCommand(OpenOIGDisabler, () => ModsLoaded && InstallationTargets.Any());
            OpenTutorialCommand = new GenericCommand(OpenTutorial, () => App.TutorialService != null && App.TutorialService.Any());
            OpenASIManagerCommand = new GenericCommand(OpenASIManager, NetworkThreadNotRunning);
            NexusModsFileSearchCommand = new GenericCommand(OpenNexusSearch); // no conditions for this
        }

        private void OpenNexusSearch()
        {
            var nexusSearchPanel = new NexusFileQueryPanel();
            nexusSearchPanel.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(nexusSearchPanel);
        }

        private void OpenTutorial()
        {
            new IntroTutorial().Show();
        }

        private void OpenOIGDisabler()
        {
            var oigDisabler = new OIGODisabler();
            oigDisabler.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(oigDisabler);
        }

        private bool CanOpenModdescEditor() => SelectedMod != null && Settings.DeveloperMode;

        private void OpenModDescEditor()
        {
            if (SelectedMod != null)
            {
                new ModDescEditor(SelectedMod).Show();
            }
        }

        private void OpenKeybindsInjector()
        {
            var conflictDetectorPanel = new KeybindsInjectorPanel();
            conflictDetectorPanel.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(conflictDetectorPanel);
        }

        private void OpenModDesc()
        {
            if (File.Exists(SelectedMod.ModDescPath))
            {
                using Process shellOpener = new Process();
                shellOpener.StartInfo.FileName = SelectedMod.ModDescPath;
                shellOpener.StartInfo.UseShellExecute = true;
                shellOpener.Start();
            }
        }

        private bool CanLaunchEGMSettings()
        {
            var target = GetCurrentTarget(MEGame.ME3);
            if (target != null)
            {
                return VanillaDatabaseService.GetInstalledDLCMods(target).Contains(@"DLC_MOD_EGM");
            }
            return false;
        }

        private void OpenOfficialDLCToggler()
        {
            var dlcToggler = new OfficialDLCToggler();
            dlcToggler.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(dlcToggler);
        }

        private void OpenConflictDetector()
        {
            var conflictDetectorPanel = new ConflictDetectorPanel();
            conflictDetectorPanel.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(conflictDetectorPanel);
        }

        private void OpenBackupFileFetcher()
        {
            var fetcher = new BackupFileFetcher();
            fetcher.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(fetcher);
        }

        private bool CanOpenConsoleKeyKeybinder()
        {
            return InstallationTargets.Any();
        }

        private void OpenConsoleKeyKeybinder()
        {
            var consoleKeybindingPanel = new ConsoleKeybindingPanel();
            consoleKeybindingPanel.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(consoleKeybindingPanel);
        }

        private bool CanOpenImportFromUI() => !IsLoadingMods;

        private void OpenImportFromGameUI()
        {
            Log.Information(@"Opening Import DLC mod from game panel");
            var importerPanel = new ImportInstalledDLCModPanel();
            importerPanel.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is Mod importedMod)
                {
                    LoadMods(importedMod);
                }
            };
            ShowBusyControl(importerPanel);
        }

        private bool CanOpenBatchModPanel()
        {
            return !IsLoadingMods;
        }

        private void OpenBatchModPanel()
        {
            var batchLibrary = new BatchModLibrary();
            batchLibrary.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is BatchLibraryInstallQueue queue)
                {
                    var target = batchLibrary.SelectedGameTarget;
                    //Install queue

                    bool continueInstalling = true;
                    int modIndex = 0;

                    //recursive. If someone is installing enough mods to cause a stack overflow exception, well, congrats, you broke my code.
                    void modInstalled(bool successful)
                    {
                        continueInstalling &= successful;
                        if (continueInstalling && queue.ModsToInstall.Count > modIndex)
                        {
                            ApplyMod(queue.ModsToInstall[modIndex], target, batchMode: true, installCompressed: queue.InstallCompressed, installCompletedCallback: modInstalled);
                            modIndex++;
                        }
                        else if (SelectedGameTarget.Game == MEGame.ME3)
                        {
                            //End
                            var autoTocUI = new AutoTOC(SelectedGameTarget);
                            autoTocUI.Close += (a1, b1) => { ReleaseBusyControl(); };
                            ShowBusyControl(autoTocUI);
                        }
                    }

                    modInstalled(true); //kick off first installation
                }
            };
            ShowBusyControl(batchLibrary);
        }

        private void OpenMixinManagerPanel()
        {
            var mixinManager = new MixinManager();
            mixinManager.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is string moddescpath)
                {
                    LoadMods(moddescpath);
                }
            };
            ShowBusyControl(mixinManager);
        }

        private bool CanOpenMixinManagerPanel()
        {
            return true;
        }

        private void OpenUpdaterServicePanelEditorMode()
        {
            var updaterServicePanel = new UpdaterServicePanel();
            updaterServicePanel.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(updaterServicePanel);
        }

        private void OpenUpdaterServicePanel()
        {
            var updaterServicePanel = new UpdaterServicePanel(SelectedMod);
            updaterServicePanel.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(updaterServicePanel);
        }

        private bool CanOpenUpdaterServicePanel() => SelectedMod != null && SelectedMod.ModClassicUpdateCode > 0;

        private void OpenModMakerPanel()
        {
            var modmakerPanel = new ModMakerPanel();
            modmakerPanel.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is Mod m)
                {
                    LoadMods(m);
                }
            };
            ShowBusyControl(modmakerPanel);
        }

        private bool CanOpenModMakerPanel()
        {
            //todo: Check for backup
            return true;
        }

        private bool CanEndorseM3()
        {
            return NexusUserID != 0 && (!ME1NexusEndorsed && !ME2NexusEndorsed && !ME3NexusEndorsed);
        }

        private void EndorseM3()
        {
            if (!ME1NexusEndorsed)
            {
                Log.Information(@"Endorsing M3 (ME1)");
                NexusModsUtilities.EndorseFile(@"masseffect", true, 149, NexusUserID, (newStatus) =>
                {
                    ME1NexusEndorsed = newStatus;
                    EndorseM3String = (ME1NexusEndorsed || ME2NexusEndorsed || ME3NexusEndorsed) ? M3L.GetString(M3L.string_endorsedME3TweaksModManagerOnNexusMods) : M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
                });
            }

            if (!ME2NexusEndorsed)
            {
                Log.Information(@"Endorsing M3 (ME2)");
                NexusModsUtilities.EndorseFile(@"masseffect2", true, 248, NexusUserID, (newStatus) =>
                {
                    ME2NexusEndorsed = newStatus;
                    EndorseM3String = (ME1NexusEndorsed || ME2NexusEndorsed || ME3NexusEndorsed) ? M3L.GetString(M3L.string_endorsedME3TweaksModManagerOnNexusMods) : M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
                });
            }

            if (!ME3NexusEndorsed)
            {
                Log.Information(@"Endorsing M3 (ME3)");
                NexusModsUtilities.EndorseFile(@"masseffect3", true, 373, NexusUserID, (newStatus) =>
                {
                    ME3NexusEndorsed = newStatus;
                    EndorseM3String = (ME1NexusEndorsed || ME2NexusEndorsed || ME3NexusEndorsed) ? M3L.GetString(M3L.string_endorsedME3TweaksModManagerOnNexusMods) : M3L.GetString(M3L.string_endorseME3TweaksModManagerOnNexusMods);
                });
            }
        }

        private void EnableME1ConsoleWrapper()
        {
            //TODO: Add way to change keys
            EnableME1Console();
        }

        private void OpenMEIM()
        {
            new ME1IniModder().Show();
        }

        private bool CanCreateTestArchive() => SelectedMod != null && SelectedMod.GetJob(ModJob.JobHeader.ME2_RCWMOD) == null;

        private void CreateTestArchive()
        {
            var testArchiveGenerator = new TestArchiveGenerator(SelectedMod);
            testArchiveGenerator.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(testArchiveGenerator);
        }

        private void EndorseWrapper()
        {
            if (SelectedMod.IsEndorsed)
            {
                var unendorseresult = M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_unendorseMod, SelectedMod.ModName), M3L.GetString(M3L.string_confirmUnendorsement), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (unendorseresult == MessageBoxResult.Yes)
                {
                    UnendorseMod();
                }
            }
            else
            {
                EndorseMod();
            }
        }

        private bool CanEndorseMod() => NexusModsUtilities.HasAPIKey && SelectedMod != null && SelectedMod.NexusModID > 0 && SelectedMod.CanEndorse && !IsEndorsingMod;

        private void EndorseMod()
        {
            if (SelectedMod != null)
            {
                Log.Information(@"Endorsing mod: " + SelectedMod.ModName);
                CurrentModEndorsementStatus = M3L.GetString(M3L.string_endorsing);
                IsEndorsingMod = true;
                SelectedMod.EndorseMod(EndorsementCallback, true, NexusUserID);
            }
        }

        private void UnendorseMod()
        {
            if (SelectedMod != null)
            {
                Log.Information(@"Unendorsing mod: " + SelectedMod.ModName);
                CurrentModEndorsementStatus = M3L.GetString(M3L.string_unendorsing);
                IsEndorsingMod = true;
                SelectedMod.EndorseMod(EndorsementCallback, false, NexusUserID);
            }
        }

        private void EndorsementCallback(Mod m, bool newStatus)
        {
            IsEndorsingMod = false;
            if (SelectedMod == m)
            {
                UpdatedEndorsementString();
            }
        }

        private bool CanShowNexusPanel()
        {
            return true; //might make some condition later.
        }

        private void ShowNexusPanel()
        {
            var nexusModsLoginPane = new NexusModsLogin();
            nexusModsLoginPane.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(nexusModsLoginPane);
        }

        private bool CanEnableME1Console()
        {
            var installed = InstallationTargets.Any(x => x.Game == MEGame.ME1);
            if (installed)
            {
                var iniFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"BioWare", @"Mass Effect", @"Config", @"BIOInput.ini");
                return File.Exists(iniFile);
            }

            return false;
        }

        private void EnableME1Console(string consoleKeyValue = @"Tilde", string typeKeyValue = @"Tab")
        {
            var installed = InstallationTargets.Any(x => x.Game == MEGame.ME1);
            if (installed)
            {
                var iniFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"BioWare", @"Mass Effect", @"Config", @"BIOInput.ini");
                if (File.Exists(iniFile))
                {
                    var ini = DuplicatingIni.LoadIni(iniFile);
                    var engineConsole = ini.Sections.FirstOrDefault(x => x.Header == @"Engine.Console");
                    if (engineConsole != null)
                    {
                        var consoleKey = engineConsole.Entries.FirstOrDefault(x => x.Key == @"ConsoleKey");
                        if (consoleKey == null)
                        {
                            engineConsole.Entries.Add(new DuplicatingIni.IniEntry(@"ConsoleKey=" + consoleKeyValue));
                        }

                        var typeKey = engineConsole.Entries.FirstOrDefault(x => x.Key == @"TypeKey");
                        if (typeKey == null)
                        {
                            engineConsole.Entries.Add(new DuplicatingIni.IniEntry(@"TypeKey=" + typeKeyValue));
                        }

                        try
                        {
                            File.WriteAllText(iniFile, ini.ToString());
                            Analytics.TrackEvent(@"Enabled the ME1 console", new Dictionary<string, string>() { { @"Succeeded", @"true" } });
                            M3L.ShowDialog(this, M3L.GetString(M3L.string_dialogConsoleEnabled), M3L.GetString(M3L.string_consoleEnabled));
                        }
                        catch (Exception e)
                        {
                            Log.Error(@"Unable to enable console: " + e.Message);
                            // see if file is read only.
                            if (File.Exists(iniFile))
                            {
                                try
                                {
                                    var fi = new FileInfo(iniFile);
                                    if (fi.IsReadOnly)
                                    {
                                        //unmark read only
                                        fi.IsReadOnly = false;
                                        File.WriteAllText(iniFile, ini.ToString());
                                        fi.IsReadOnly = true;
                                        Analytics.TrackEvent(@"Enabled the ME1 console", new Dictionary<string, string>() { { @"Succeeded", @"true" } });
                                        M3L.ShowDialog(this, M3L.GetString(M3L.string_dialogConsoleEnabled), M3L.GetString(M3L.string_consoleEnabled));
                                        return;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(@"Attempted to unset/reset read-only flag, failed: " + ex.Message);
                                }
                            }

                            Analytics.TrackEvent(@"Enabled the ME1 console", new Dictionary<string, string>() { { @"Succeeded", @"false" } });
                            Crashes.TrackError(e);
                            M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_unableToModifyBioinputIni, e.Message), M3L.GetString(M3L.string_couldNotEnableConsole), MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            }
        }

        public bool HasAtLeastOneTarget() => InstallationTargets.Any();

        private bool HasME3Target()
        {
            return InstallationTargets.Any(x => x.Game == MEGame.ME3);
        }

        private void CheckSelectedModForUpdate()
        {
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += (a, b) => { CheckModsForUpdates(new List<Mod>(new[] { SelectedMod })); };
            bw.RunWorkerAsync();

        }

        private void RestoreSelectedMod()
        {
            BackgroundWorker bw = new BackgroundWorker();
            bw.DoWork += (a, b) => { CheckModsForUpdates(new List<Mod>(new[] { SelectedMod }), true); };
            bw.RunWorkerAsync();
        }

        private bool SelectedModIsME3TweaksUpdatable() => SelectedMod?.IsUpdatable ?? false;


        private void SubmitTelemetryForMod()
        {
            var telemetryPane = new TPMITelemetrySubmissionForm(SelectedMod);
            telemetryPane.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(telemetryPane);
        }

        private bool CanSubmitTelemetryForMod() => SelectedMod?.GetJob(ModJob.JobHeader.CUSTOMDLC) != null;

        private void OpenArchiveSelectionDialog()
        {
            OpenFileDialog m = new OpenFileDialog
            {
                Title = M3L.GetString(M3L.string_selectModArchive),
                Filter = M3L.GetString(M3L.string_supportedFiles) + @"|*.zip;*.rar;*.7z;*.exe;*.me2mod"
            };
            var result = m.ShowDialog(this);
            if (result.Value)
            {
                Analytics.TrackEvent(@"User opened mod archive for import", new Dictionary<string, string> { { @"Method", @"Manual file selection" }, { @"Filename", Path.GetFileName(m.FileName) } });
                var archiveFile = m.FileName;
                Log.Information(@"Opening archive user selected: " + archiveFile);
                openModImportUI(archiveFile);
            }
        }

        private bool CanOpenArchiveSelectionDialog()
        {
            return App.ThirdPartyImportingService != null && App.ThirdPartyIdentificationService != null && !ContentCheckInProgress;
        }

        private bool CanDeleteModFromLibrary() => SelectedMod != null && !ContentCheckInProgress;

        private void DeleteModFromLibraryWrapper()
        {
            DeleteModFromLibrary(SelectedMod);
        }

        public bool DeleteModFromLibrary(Mod selectedMod)
        {
            var confirmationResult = M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_dialogDeleteSelectedModFromLibrary, selectedMod.ModName), M3L.GetString(M3L.string_confirmDeletion), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirmationResult == MessageBoxResult.Yes)
            {
                Log.Information(@"Deleting mod from library: " + selectedMod.ModPath);
                if (Utilities.DeleteFilesAndFoldersRecursively(selectedMod.ModPath))
                {
                    AllLoadedMods.Remove(selectedMod);
                    VisibleFilteredMods.Remove(selectedMod);
                    FailedMods.Remove(selectedMod); //make sure to remove it from this in case it's failed mods panel calling this.
                    return true;
                }

                //LoadMods();
            }
            return false;
        }

        private void ShowDeploymentPane()
        {
            if (SelectedMod.InstallationJobs.Count == 1 && SelectedMod.GetJob(ModJob.JobHeader.ME2_RCWMOD) != null)
            {
                Log.Error(M3L.GetString(M3L.string_rcwModsCannotBeDeployed));
                M3L.ShowDialog(this, M3L.GetString(M3L.string_rcwModsCannotBeDeployedDescription),
                    M3L.GetString(M3L.string_cannotDeployMe2modFiles), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // TODO: Move this into archive panel
            GameTarget vt = GetCurrentTarget(SelectedMod.Game);
            if (vt == null)
            {
                Log.Error($@"Cannot deploy mod, no current game install for {SelectedMod.Game} is available");
                M3L.ShowDialog(this,
                    M3L.GetString(M3L.string_interp_dialog_cannotDeployModNoTarget, SelectedMod.Game),
                    M3L.GetString(M3L.string_cannotDeployMod), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var bup = BackupService.GetGameBackupPath(SelectedMod.Game);
            if (bup != null && Directory.Exists(bup))
            {
                var archiveDeploymentPane = new ArchiveDeployment(SelectedMod);
                archiveDeploymentPane.Close += (a, b) =>
                {
                    ReleaseBusyControl();
                    if (b.Data is List<Mod> modsForTPMI)
                    {
                        // Show form for each mod
                        foreach (var m in modsForTPMI)
                        {
                            var telemetryPane = new TPMITelemetrySubmissionForm(m);
                            telemetryPane.Close += (a, b) => { ReleaseBusyControl(); };
                            ShowBusyControl(telemetryPane);
                        }
                    }
                };
                ShowBusyControl(archiveDeploymentPane);
            }
            else
            {
                Log.Error($@"Cannot deploy mod, no backup for {SelectedMod.Game} is available");
                M3L.ShowDialog(this,
                    M3L.GetString(M3L.string_interp_dialog_cannotDeployModNoBackup, SelectedMod.Game),
                    M3L.GetString(M3L.string_cannotDeployMod), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowUpdateCompletedPane()
        {
            var message = M3L.GetString(M3L.string_interp_modManagerHasBeenUpdatedTo, App.UpdatedFrom.ToString(), App.AppVersionAbout);
            var updateCompletedPanel = new UpdateCompletedPanel(M3L.GetString(M3L.string_updateCompleted), message);
            updateCompletedPanel.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(updateCompletedPanel);
        }

        private bool CanShowDeploymentPane()
        {
            return SelectedMod != null && Settings.DeveloperMode;
        }

        private bool ContentCheckNotInProgress()
        {
            return !ContentCheckInProgress;
        }

        /// <summary>
        /// Shows or queues the specified control
        /// </summary>
        /// <param name="control">Control to show or queue</param>
        private void ShowBusyControl(MMBusyPanelBase control)
        {
            if (queuedUserControls.Count == 0 && !IsBusy)
            {
                IsBusy = true;
                BusyContent = control;
            }
            else
            {
                queuedUserControls.Enqueue(control);
            }
        }

        /// <summary>
        /// Shows or queues the specified control
        /// </summary>
        /// <param name="control">Control to show or queue</param>
        private void ReleaseBusyControl()
        {
            if (queuedUserControls.Count == 0)
            {
                BusyContent = null;
                IsBusy = false;
                System.Threading.Tasks.Task.Factory.StartNew(() =>
                {
                    // this is to force some items that are no longer relevant to be cleaned up.
                    // for some reason commands fire even though they are no longer attached to the interface
                    Thread.Sleep(3000);
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                });
            }
            else
            {
                var control = queuedUserControls.Dequeue();
                //control.OnPanelVisible();
                BusyContent = control;
            }
        }

        private void ShowBackupPane()
        {
            var backupCreator = new BackupCreator(InstallationTargets.ToList(), SelectedGameTarget, this);
            backupCreator.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is string result)
                {
                    if (result == @"ALOTInstaller")
                    {
                        BootALOTInstallerPassthrough(ExternalToolLauncher.ALOTInstaller);
                    }
                }
            };
            ShowBusyControl(backupCreator);
        }

        private void ShowRestorePane()
        {
            var restoreManager = new RestorePanel(InstallationTargets.ToList(), SelectedGameTarget);
            restoreManager.Close += (a, b) =>
            {
                if (b.Data is bool refresh && refresh)
                {
                    PopulateTargets(SelectedGameTarget);
                }

                ReleaseBusyControl();
            };
            ShowBusyControl(restoreManager);
        }

        private void ShowInstallInfo()
        {
            var installationInformation = new InstallationInformation(InstallationTargets.ToList(), SelectedGameTarget);
            installationInformation.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is string result)
                {
                    if (result == @"ASIManager")
                    {
                        // This is kinda risky since when the control unloads it might dump the list and 
                        // the SelectedTarget might be null. I think the worst case is that it simply won't select the tab/target in ASI Manager.
                        var selectedTarget = installationInformation.SelectedTarget;

                        var asiManager = new ASIManagerPanel(selectedTarget);
                        asiManager.Close += (a, b) => { ReleaseBusyControl(); };
                        ShowBusyControl(asiManager);
                        Analytics.TrackEvent(@"Launched ASI Manager", new Dictionary<string, string>()
                        {
                            {@"Invocation method", @"Installation Information"}
                        });
                    }
                    if (result == @"ALOTInstaller")
                    {
                        BootALOTInstallerPassthrough(ExternalToolLauncher.ALOTInstaller);
                    }

                    if (result == @"ReloadTargets")
                    {
                        PopulateTargets();
                    }
                }
            };
            ShowBusyControl(installationInformation);
        }

        /// <summary>
        /// Boots ALOT Installer, using the specified tool name. Passes through the current active targets in M3, if they are supported.
        /// </summary>
        /// <param name="alotInstallerToolName"></param>
        private void BootALOTInstallerPassthrough(string alotInstallerToolName)
        {
            var arguments = "";
            var me1Target = GetCurrentTarget(MEGame.ME1);
            var me2Target = GetCurrentTarget(MEGame.ME2);
            var me3Target = GetCurrentTarget(MEGame.ME3);
            if (me1Target != null && me1Target.Supported)
            {
                arguments += $"--me1path \"{me1Target.TargetPath}\" "; //do not localize
            }
            if (me2Target != null && me2Target.Supported)
            {
                arguments += $"--me2path \"{me2Target.TargetPath}\" "; //do not localize
            }
            if (me3Target != null && me3Target.Supported)
            {
                arguments += $"--me3path \"{me3Target.TargetPath}\" "; //do not localize
            }
            LaunchExternalTool(alotInstallerToolName, arguments);
        }

        private bool CanShowInstallInfo()
        {
            return SelectedGameTarget != null && SelectedGameTarget.IsValid && SelectedGameTarget.Selectable && !ContentCheckInProgress;
        }

        private void CallApplyMod()
        {
            ApplyMod(SelectedMod);
        }

        private void StartGame()
        {
            SelectedGameTarget.ReloadGameTarget(false);
            var game = Utilities.GetGameName(SelectedGameTarget.Game);

            BackgroundTask gameLaunch = backgroundTaskEngine.SubmitBackgroundJob(@"GameLaunch", M3L.GetString(M3L.string_interp_launching, game), M3L.GetString(M3L.string_interp_launched, game));

            try
            {
                Task.Run(() => GameLauncher.LaunchGame(SelectedGameTarget))
                    .ContinueWith(x =>
                    {
                        backgroundTaskEngine.SubmitJobCompletion(gameLaunch);
                    });
            }
            catch (Exception e)
            {
                backgroundTaskEngine.SubmitJobCompletion(gameLaunch); // This ensures message is cleared out of queue
                if (e is Win32Exception w32e)
                {
                    if (w32e.NativeErrorCode == 1223)
                    {
                        //Admin canceled.
                        return; //we don't care.
                    }
                }
                Log.Error(@"Error launching game: " + e.Message);
            }

            //Detect screen resolution - useful info for scene modders
            string resolution = @"Could not detect";
            var iniFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"BioWare");
            try
            {
                switch (SelectedGameTarget.Game)
                {
                    case MEGame.ME1:
                        {
                            iniFile = Path.Combine(iniFile, @"Mass Effect", @"Config", @"BIOEngine.ini");
                            if (File.Exists(iniFile))
                            {
                                var dini = DuplicatingIni.LoadIni(iniFile);
                                var section = dini.Sections.FirstOrDefault(x => x.Header == @"WinDrv.WindowsClient");
                                if (section != null)
                                {
                                    var resx = section.Entries.FirstOrDefault(x => x.Key == @"StartupResolutionX");
                                    var resy = section.Entries.FirstOrDefault(x => x.Key == @"StartupResolutionY");
                                    if (resx != null && resy != null)
                                    {
                                        resolution = $@"{resx.Value}x{resy.Value}";
                                    }
                                }
                            }
                        }
                        break;
                    case MEGame.ME2:
                    case MEGame.ME3:
                        {
                            iniFile = Path.Combine(iniFile, @"Mass Effect " + SelectedGameTarget.Game.ToString().Substring(2), @"BIOGame", @"Config", @"Gamersettings.ini");
                            if (File.Exists(iniFile))
                            {
                                var dini = DuplicatingIni.LoadIni(iniFile);
                                var section = dini.Sections.FirstOrDefault(x => x.Header == @"SystemSettings");
                                if (section != null)
                                {
                                    var resx = section.Entries.FirstOrDefault(x => x.Key == @"ResX");
                                    var resy = section.Entries.FirstOrDefault(x => x.Key == @"ResY");
                                    if (resx != null && resy != null)
                                    {
                                        resolution = $@"{resx.Value}x{resy.Value}";
                                    }
                                }
                            }
                        }
                        break;
                }

                Analytics.TrackEvent(@"Launched game", new Dictionary<string, string>()
                {
                    {@"Game", game.ToString()},
                    {@"Screen resolution", resolution}
                });
            }
            catch (Exception e)
            {
                Log.Error(@"Error trying to detect screen resolution: " + e.Message);
            }
            //var exePath = MEDirectories.ExecutablePath(SelectedGameTarget);
            //Process.Start(exePath);

        }

        /// <summary>
        /// Updates boot target and returns the HRESULT of the update command for registry.
        /// Returns -3 if no registry update was performed.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        private int UpdateBootTarget(GameTarget target)
        {
            string exe = @"reg";
            var args = new List<string>();
            string regPath = null;
            switch (target.Game)
            {
                case MEGame.ME1:
                    {
                        var existingPath = ME1Directory.DefaultGamePath;
                        if (existingPath != null)
                        {
                            regPath = @"HKLM\SOFTWARE\Wow6432Node\BioWare\Mass Effect";
                        }
                    }
                    break;
                case MEGame.ME2:
                    {
                        var existingPath = ME2Directory.DefaultGamePath;
                        if (existingPath != null)
                        {
                            regPath = @"HKLM\SOFTWARE\Wow6432Node\BioWare\Mass Effect 2";
                        }
                    }

                    break;
                case MEGame.ME3:
                    {
                        var existingPath = ME3Directory.DefaultGamePath;
                        if (existingPath != null)
                        {
                            regPath = @"HKLM\SOFTWARE\Wow6432Node\BioWare\Mass Effect 3";
                        }
                    }
                    break;
            }

            if (regPath != null)
            {
                //is set in registry
                args.Add(@"add");
                args.Add(regPath);
                args.Add(@"/v");
                args.Add(target.Game == MEGame.ME3 ? @"Install Dir" : @"Path");
                args.Add(@"/t");
                args.Add(@"REG_SZ");
                args.Add(@"/d");
                args.Add($"{target.TargetPath.TrimEnd('\\')}\\\\"); // do not localize
                // ^ Strip ending slash. Then append it to make sure there is ending slash. Reg will interpret final \ as an escape, so we do \\ (as documented on ss64)
                args.Add(@"/f");

                return Utilities.RunProcess(exe, args, waitForProcess: true, requireAdmin: true);
            }

            return -3;
        }

        private bool CanStartGame()
        {
            //Todo: Check if this is origin game and if target will boot
            return SelectedGameTarget != null && SelectedGameTarget.Selectable /*&& SelectedGameTarget.RegistryActive*/;
        }

        private bool CanToggleBinkw32(object obj)
        {
            if (obj is string str && Enum.TryParse(str, out MEGame game))
            {
                var target = GetCurrentTarget(game);
                if (target != null && !Utilities.IsGameRunning(game))
                {
                    return File.Exists(Utilities.GetBinkw32File(target));
                }
            }

            return false;
        }

        private void ToggleBinkw32(object obj)
        {
            if (obj is string str && Enum.TryParse(str, out MEGame game))
            {
                var target = GetCurrentTarget(game);
                if (target == null) return; //can't toggle this
                if (Utilities.IsGameRunning(game))
                {
                    M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_dialogCannotInstallBinkWhileGameRunning, Utilities.GetGameName(game)), M3L.GetString(M3L.string_gameRunning), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                bool install = false;
                switch (game)
                {
                    case MEGame.ME1:
                        install = !ME1ASILoaderInstalled;
                        break;
                    case MEGame.ME2:
                        install = !ME2ASILoaderInstalled;
                        break;
                    case MEGame.ME3:
                        install = !ME3ASILoaderInstalled;
                        break;
                }

                if (install)
                {
                    Utilities.InstallBinkBypass(target);
                }
                else
                {
                    Utilities.UninstallBinkBypass(target);
                }


                UpdateBinkStatus(target.Game);
            }
        }

        private void RunGameConfigTool(object obj)
        {
            if (obj is string str && Enum.TryParse(str, out MEGame game))
            {
                var target = GetCurrentTarget(game);
                if (target != null)
                {
                    var configTool = Utilities.GetGameConfigToolPath(target);
                    try
                    {
                        Utilities.RunProcess(configTool, "", false, true, false, false);
                    }
                    catch (Exception e)
                    {
                        // user may have canceled running it. seems it sometimes requires admin
                        Log.Error($@"Error running config tool for {game}: {e.Message}");
                    }
                }
            }
        }

        private bool CanRunGameConfigTool(object obj)
        {
            if (obj is string str && Enum.TryParse(str, out MEGame game))
            {
                var target = GetCurrentTarget(game);
                if (target != null)
                {
                    var configTool = Utilities.GetGameConfigToolPath(target);
                    return File.Exists(configTool);
                }
            }

            return false;
        }

        private void AddTarget()
        {
            Log.Information(@"User is adding new modding target");
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = M3L.GetString(M3L.string_selectGameExecutable);
            string filter = $@"{M3L.GetString(M3L.string_gameExecutable)}|MassEffect.exe;MassEffect2.exe;MassEffect3.exe"; //only partially localizable.
            ofd.Filter = filter;
            if (ofd.ShowDialog() == true)
            {
                MEGame gameSelected = MEGame.Unknown;
                var filename = Path.GetFileName(ofd.FileName);
                if (filename.Equals(@"MassEffect3.exe", StringComparison.InvariantCultureIgnoreCase)) gameSelected = MEGame.ME3;
                if (filename.Equals(@"MassEffect2.exe", StringComparison.InvariantCultureIgnoreCase)) gameSelected = MEGame.ME2;
                if (filename.Equals(@"MassEffect.exe", StringComparison.InvariantCultureIgnoreCase)) gameSelected = MEGame.ME1;

                if (gameSelected != MEGame.Unknown)
                {
                    string result = Path.GetDirectoryName(Path.GetDirectoryName(ofd.FileName));

                    if (gameSelected == MEGame.ME3)
                        result = Path.GetDirectoryName(result); //up one more because of win32 directory.
                                                                //Test for cmmvanilla
                    if (File.Exists(Path.Combine(result, @"cmmvanilla")))
                    {
                        M3L.ShowDialog(this, M3L.GetString(M3L.string_dialogCannotAddTargetCmmVanilla), M3L.GetString(M3L.string_errorAddingTarget), MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var pendingTarget = new GameTarget(gameSelected, result, false);
                    string failureReason = pendingTarget.ValidateTarget();

                    if (failureReason == null)
                    {
                        Analytics.TrackEvent(@"Attempted to add game target", new Dictionary<string, string>()
                        {
                            {@"Game", pendingTarget.Game.ToString()},
                            {@"Result", @"Success"},
                            {@"Supported", pendingTarget.Supported.ToString()}
                        });
                        Utilities.AddCachedTarget(pendingTarget);
                        PopulateTargets(pendingTarget);
                    }
                    else
                    {
                        Analytics.TrackEvent(@"Attempted to add game target", new Dictionary<string, string>()
                        {
                            {@"Game", pendingTarget.Game.ToString()},
                            {@"Result", @"Failed, " + failureReason},
                            {@"Supported", pendingTarget.Supported.ToString()}
                        });
                        Log.Error(@"Could not add target: " + failureReason);
                        M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_dialogUnableToAddGameTarget, failureReason), M3L.GetString(M3L.string_errorAddingTarget), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

            }
        }

        public bool ContentCheckInProgress { get; set; } = true; //init is content check
        private bool NetworkThreadNotRunning() => !ContentCheckInProgress;

        private void CheckForContentUpdates()
        {
            PerformStartupNetworkFetches(false);
        }

        public bool IsLoadingMods { get; set; }
        public List<string> LoadedTips { get; } = new List<string>();
        public bool ModsLoaded { get; private set; } = false;
        public GameTarget SelectedGameTarget { get; set; }

        private bool CanReloadMods()
        {
            return !IsLoadingMods;
        }

        private bool CanApplyMod()
        {
            if (SelectedMod == null)
            {
                ApplyModButtonText = M3L.GetString(M3L.string_selectMod);
                return false;
            }

            if (SelectedGameTarget == null)
            {
                ApplyModButtonText = M3L.GetString(M3L.string_noTarget);
                return false;

            }

            if (SelectedGameTarget.Game != SelectedMod.Game)
            {
                ApplyModButtonText = M3L.GetString(M3L.string_cannotInstallToThisGame);
                return false;
            }

            ApplyModButtonText = M3L.GetString(M3L.string_applyMod);
            return true;
        }

        /// <summary>
        /// Applies a mod to the current or forced target. This method is asynchronous, it must run on the UI thread but it will immediately yield once the installer begins.
        /// </summary>
        /// <param name="mod">Mod to install</param>
        /// <param name="forcedTarget">Forced target to install to</param>
        /// <param name="batchMode">Causes ME3 autotoc to skip at end of install</param>
        /// <param name="installCompletedCallback">Callback when mod installation either succeeds for fails</param>

        private void ApplyMod(Mod mod, GameTarget forcedTarget = null, bool batchMode = false, bool? installCompressed = null, Action<bool> installCompletedCallback = null)
        {
            if (!Utilities.IsGameRunning(mod.Game))
            {
                BackgroundTask modInstallTask = backgroundTaskEngine.SubmitBackgroundJob(@"ModInstall", M3L.GetString(M3L.string_interp_installingMod, mod.ModName), M3L.GetString(M3L.string_interp_installedMod, mod.ModName));
                var modInstaller = new ModInstaller(mod, forcedTarget ?? SelectedGameTarget, installCompressed, batchMode: batchMode);
                modInstaller.Close += (a, b) =>
                {

                    if (!modInstaller.InstallationSucceeded)
                    {
                        if (modInstaller.InstallationCancelled)
                        {
                            modInstallTask.finishedUiText = M3L.GetString(M3L.string_installationAborted);
                            ReleaseBusyControl();
                            backgroundTaskEngine.SubmitJobCompletion(modInstallTask);
                            installCompletedCallback?.Invoke(false);
                            return;
                        }
                        else
                        {
                            modInstallTask.finishedUiText = M3L.GetString(M3L.string_interp_failedToInstallMod, mod.ModName);
                            installCompletedCallback?.Invoke(false);
                        }
                    }

                    //Run AutoTOC if ME3 and not batch mode
                    if (!modInstaller.InstallationCancelled && SelectedGameTarget.Game == MEGame.ME3 && !batchMode)
                    {
                        var autoTocUI = new AutoTOC(SelectedGameTarget);
                        autoTocUI.Close += (a1, b1) =>
                        {
                            ReleaseBusyControl();
                            backgroundTaskEngine.SubmitJobCompletion(modInstallTask);
                        };
                        ShowBusyControl(autoTocUI);
                        ReleaseBusyControl();
                    }
                    else
                    {
                        ReleaseBusyControl();
                        backgroundTaskEngine.SubmitJobCompletion(modInstallTask);
                    }

                    if (modInstaller.InstallationSucceeded)
                    {
                        installCompletedCallback?.Invoke(true);
                        if (ExternalToolLauncher.IsSupportedToolID(mod.PostInstallToolLaunch))
                        {
                            Log.Information(@"Launching post-install tool as specified by mod: " + mod.PostInstallToolLaunch);
                            if (mod.PostInstallToolLaunch == @"EGMSettings")
                            {
                                LaunchExternalTool(mod.PostInstallToolLaunch, SelectedGameTarget.TargetPath);
                            }
                            else
                            {
                                // no args
                                LaunchExternalTool(mod.PostInstallToolLaunch);
                            }
                        }
                    }
                };
                ShowBusyControl(modInstaller);
            }
            else
            {
                Log.Error($@"Blocking install of {mod.ModName} because {Utilities.GetGameName(mod.Game)} is running.");
                M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_dialogCannotInstallModsWhileGameRunning, Utilities.GetGameName(mod.Game)), M3L.GetString(M3L.string_cannotInstallMod), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReloadMods()
        {
            LoadMods();
        }

        private void CheckTargetPermissions(bool promptForConsent = true, bool showDialogEvenIfNone = false)
        {
            var targetsNeedingUpdate = InstallationTargets.Where(x => x.Selectable && !x.IsTargetWritable()).ToList();
            bool me1AGEIAKeyNotWritable = false;
            //if (InstallationTargets.Any(x => x.Game == MEGame.ME1))
            //{
            //    //Check AGEIA
            //    try
            //    {
            //        var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\AGEIA Technologies", true);
            //        if (key != null)
            //        {
            //            key.Close();
            //        }
            //        else
            //        {
            //            Log.Information(@"ME1 AGEIA Technologies key is not present or is not writable.");
            //            me1AGEIAKeyNotWritable = true;
            //        }
            //    }
            //    catch (SecurityException)
            //    {
            //        Log.Information(@"ME1 AGEIA Technologies key is not writable.");
            //        me1AGEIAKeyNotWritable = true;
            //    }
            //}

            if (targetsNeedingUpdate.Count > 0 || me1AGEIAKeyNotWritable)
            {
                if (promptForConsent)
                {
                    Log.Information(@"Some game paths/keys are not writable. Prompting user.");
                    bool result = false;
                    Application.Current.Dispatcher.Invoke(delegate { result = M3L.ShowDialog(this, M3L.GetString(M3L.string_dialogUACPreConsent), M3L.GetString(M3L.string_someTargetsKeysWriteProtected), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes; });
                    if (result)
                    {
                        Analytics.TrackEvent(@"Granting write permissions", new Dictionary<string, string>() { { @"Granted?", @"Yes" } });
                        try
                        {
                            Utilities.EnableWritePermissionsToFolders(targetsNeedingUpdate.Select(x => x.TargetPath).ToList(), me1AGEIAKeyNotWritable);
                        }
                        catch (Exception e)
                        {
                            Log.Error(@"Error granting write permissions: " + App.FlattenException(e));
                        }
                    }
                    else
                    {
                        Log.Warning(@"User denied permission to grant write permissions");
                        Analytics.TrackEvent(@"Granting write permissions", new Dictionary<string, string>() { { @"Granted?", @"No" } });
                    }
                }
                else
                {
                    Analytics.TrackEvent(@"Granting write permissions", new Dictionary<string, string>() { { @"Granted?", @"Implicit" } });
                    Utilities.EnableWritePermissionsToFolders(targetsNeedingUpdate.Select(x => x.TargetPath).ToList(), me1AGEIAKeyNotWritable);
                }
            }
            else if (showDialogEvenIfNone)
            {
                M3L.ShowDialog(this, M3L.GetString(M3L.string_allTargetsWritable), M3L.GetString(M3L.string_targetsWritable), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        //Fody uses this property on weaving
#pragma warning disable
        public event PropertyChangedEventHandler PropertyChanged;
#pragma warning restore


        private void ModManager_ContentRendered(object sender, EventArgs e)
        {
            if (App.BootingUpdate)
            {
                ShowUpdateCompletedPane();
            }

            if (!App.IsOperatingSystemSupported())
            {
                string osList = string.Join("\n - ", App.SupportedOperatingSystemVersions); //do not localize
                Log.Error(@"This operating system is not supported.");
                M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_dialog_unsupportedOS, osList), M3L.GetString(M3L.string_unsupportedOperatingSystem), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                // Only show if OS is supported
                var lastCrash = Crashes.GetLastSessionCrashReportAsync().Result;
                if (Settings.DeveloperMode && lastCrash?.StackTrace != null)
                {
                    // Show messagebox?
                }
            }

            // Run on background thread as we don't need the result of this
            NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"NexusModsInitialAuthentication");
            nbw.DoWork += (a, b) => RefreshNexusStatus();
            nbw.RunWorkerAsync();

            var syncContext = TaskScheduler.FromCurrentSynchronizationContext();
            ME3ExplorerCoreLib.SetSynchronizationContext(syncContext);
            IsEnabled = false;
            Task.Run(() =>
            {
                Log.Information(@"Ensuring default ASI assets are present");
                ASIManager.ExtractDefaultASIResources();

                Log.Information(@"Initializing ME3ExplorerCore library");
                #region INIT CORE LIB
                MEPackageHandler.GlobalSharedCacheEnabled = false; // Do not use the package caching system
                ME3ExplorerCoreLib.InitLib(syncContext, x =>
                {
                    Log.Error($@"Error saving package: {x}");
                });
                T2DLocalizationShim.SetupTexture2DLocalizationShim();
                #endregion
                //debugMethod();
                CurrentOperationText = M3L.GetString(M3L.string_loadingTargets);
                PopulateTargets();
            }).ContinueWithOnUIThread(x =>
            {
                IsEnabled = true;
                if (!Settings.ShowedPreviewPanel)
                {
                    ShowPreviewPanel();
                }
                else
                {
                    LoadMods();
                }

                PerformStartupNetworkFetches(true);
                if (BackupNagSystem.ShouldShowNagScreen(InstallationTargets.ToList()))
                {
                    ShowBackupNag();
                }
                collectHardwareInfo();
                StartedUp = true;
            });

        }

        private void collectHardwareInfo()
        {
            NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"HardwareInventory");
            nbw.DoWork += (a, b) =>
            {
                var data = new Dictionary<string, string>();
                try
                {
                    ManagementObjectSearcher mosProcessor = new ManagementObjectSearcher(@"SELECT * FROM Win32_Processor");
                    foreach (ManagementObject moProcessor in mosProcessor.Get())
                    {
                        // For seeing AMD vs Intel (for ME1 lighting)
                        if (moProcessor[@"name"] != null)
                        {
                            data[@"Processor"] = moProcessor[@"name"].ToString();
                            App.IsRunningOnAMD = data[@"Processor"].Contains(@"AMD");
                        }
                    }

                    data[@"BetaMode"] = Settings.BetaMode.ToString();
                    data[@"DeveloperMode"] = Settings.DeveloperMode.ToString();

                    App.SubmitAnalyticTelemetryEvent(@"Hardware Info", data);
                }
                catch //(Exception e)
                {

                }
            };
            nbw.RunWorkerAsync();
        }

        private void ShowPreviewPanel()
        {
            var previewPanel = new FirstRunPanel();
            previewPanel.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is bool loadMods)
                {
                    LoadMods();
                }
                // if user speeds through, this might not be available yet. oh well
                if (App.TutorialService != null && App.TutorialService.Any())
                {
                    new IntroTutorial().Show();
                }
            };
            ShowBusyControl(previewPanel);
        }

        private void UpdateBinkStatus(MEGame game)
        {
            var target = GetCurrentTarget(game);
            if (target == null)
            {
                switch (game)
                {
                    case MEGame.ME1:
                        ME1ASILoaderInstalled = false;
                        ME1ASILoaderText = M3L.GetString(M3L.string_gameNotInstalled);
                        break;
                    case MEGame.ME2:
                        ME2ASILoaderInstalled = false;
                        ME2ASILoaderText = M3L.GetString(M3L.string_gameNotInstalled);
                        break;
                    case MEGame.ME3:
                        ME3ASILoaderInstalled = false;
                        ME3ASILoaderText = M3L.GetString(M3L.string_gameNotInstalled);
                        break;
                }

                return; //don't check or anything
            }

            var binkME1InstalledText = M3L.GetString(M3L.string_binkAsiLoaderInstalled);
            var binkME1NotInstalledText = M3L.GetString(M3L.string_binkAsiLoaderNotInstalled);
            var binkInstalledText = M3L.GetString(M3L.string_binkAsiBypassInstalled);
            var binkNotInstalledText = M3L.GetString(M3L.string_binkAsiBypassNotInstalled);

            switch (game)
            {
                case MEGame.ME1:
                    ME1ASILoaderInstalled = Utilities.CheckIfBinkw32ASIIsInstalled(target);
                    ME1ASILoaderText = ME1ASILoaderInstalled ? binkME1InstalledText : binkME1NotInstalledText;
                    break;
                case MEGame.ME2:
                    ME2ASILoaderInstalled = Utilities.CheckIfBinkw32ASIIsInstalled(target);
                    ME2ASILoaderText = ME2ASILoaderInstalled ? binkInstalledText : binkNotInstalledText;
                    break;
                case MEGame.ME3:
                    ME3ASILoaderInstalled = Utilities.CheckIfBinkw32ASIIsInstalled(target);
                    ME3ASILoaderText = ME3ASILoaderInstalled ? binkInstalledText : binkNotInstalledText;
                    break;
            }
        }

        /// <summary>
        /// Gets current target that matches the game. If selected target does not match, the first one in the list used (active). THIS CAN RETURN A NULL OBJECT!
        /// </summary>
        /// <param name="game">Game to find target for</param>
        /// <returns>Game matching target. If none
        /// is found, this return null.</returns>
        private GameTarget GetCurrentTarget(MEGame game)
        {
            if (SelectedGameTarget != null)
            {
                if (SelectedGameTarget.Game == game) return SelectedGameTarget;
            }

            return InstallationTargets.FirstOrDefault(x => x.Game == game);
        }

        public void LoadMods(Mod modToHighlight = null, bool forceUpdateCheckOnCompletion = false)
        {
            LoadMods(modToHighlight?.ModPath, forceUpdateCheckOnCompletion);
        }

        /// <summary>
        /// Reload mods. Highlight the specified mod that matches the path if any
        /// </summary>
        /// <param name="modpathToHighlight"></param>
        public void LoadMods(string modpathToHighlight, bool forceUpdateCheckOnCompletion = false)
        {
            try
            {
                Utilities.EnsureModDirectories();
            }
            catch (Exception e)
            {
                Log.Error(@"Unable to ensure mod directories: " + e.Message);
                Crashes.TrackError(e);
                M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_dialogUnableToCreateModLibraryNoPermissions, e.Message), M3L.GetString(M3L.string_errorCreatingModLibrary), MessageBoxButton.OK, MessageBoxImage.Error);
                var folderPicked = ChooseModLibraryPath(false);
                if (folderPicked)
                {
                    LoadMods();
                }
                else
                {
                    Log.Error(@"Unable to create mod library. Mod Manager will now exit.");
                    Crashes.TrackError(new Exception(@"Unable to create mod library"), new Dictionary<string, string>() { { @"Executable location", App.ExecutableLocation } });
                    M3L.ShowDialog(this, M3L.GetString(M3L.string_unableToCreateModLibrary), M3L.GetString(M3L.string_errorCreatingModLibrary), MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(1);
                }

                return;
            }

            VisibleFilteredMods.ClearEx();
            AllLoadedMods.ClearEx();
            FailedMods.ClearEx();
            IsLoadingMods = true;

            NamedBackgroundWorker bw = new NamedBackgroundWorker(@"ModLoaderThread");
            bw.WorkerReportsProgress = true;
            bw.DoWork += (a, args) =>
            {

                bool canCheckForModUpdates = OnlineContent.CanFetchContentThrottleCheck(); //This is here as it will fire before other threads can set this value used in this session.
                ModsLoaded = false;
                var uiTask = backgroundTaskEngine.SubmitBackgroundJob(@"ModLoader", M3L.GetString(M3L.string_loadingMods), M3L.GetString(M3L.string_loadedMods));
                Log.Information(@"Loading mods from mod library: " + Utilities.GetModsDirectory());
                var me3modDescsToLoad = Directory.GetDirectories(Utilities.GetME3ModsDirectory()).Select(x => (game: MEGame.ME3, path: Path.Combine(x, @"moddesc.ini"))).Where(x => File.Exists(x.path));
                var me2modDescsToLoad = Directory.GetDirectories(Utilities.GetME2ModsDirectory()).Select(x => (game: MEGame.ME2, path: Path.Combine(x, @"moddesc.ini"))).Where(x => File.Exists(x.path));
                var me1modDescsToLoad = Directory.GetDirectories(Utilities.GetME1ModsDirectory()).Select(x => (game: MEGame.ME1, path: Path.Combine(x, @"moddesc.ini"))).Where(x => File.Exists(x.path));
                var modDescsToLoad = me3modDescsToLoad.Concat(me2modDescsToLoad).Concat(me1modDescsToLoad);

                foreach (var moddesc in modDescsToLoad)
                {
                    var mod = new Mod(moddesc.path, moddesc.game);
                    if (mod.ValidMod)
                    {
                        //Application.Current.Dispatcher.Invoke(delegate
                        //{
                        AllLoadedMods.Add(mod);
                        if (ME1ModsVisible && mod.Game == MEGame.ME1 || ME2ModsVisible && mod.Game == MEGame.ME2 || ME3ModsVisible && mod.Game == MEGame.ME3)
                        {
                            VisibleFilteredMods.Add(mod);
                        }
                        //});
                    }
                    else
                    {
                        FailedMods.Add(mod);
                    }
                }

                Application.Current.Dispatcher.Invoke(delegate { VisibleFilteredMods.Sort(x => x.ModName); });

                if (modpathToHighlight != null)
                {
                    args.Result = VisibleFilteredMods.FirstOrDefault(x => x.ModPath == modpathToHighlight);

                    //telemetry for importing issues
                    var targetMod = AllLoadedMods.FirstOrDefault(x => x.ModPath == modpathToHighlight);
                    if (File.Exists(modpathToHighlight) && targetMod == null)
                    {
                        //moddesc.ini exists but it did not load
                        Log.Error(@"Mod to highlight failed to load! Path: " + modpathToHighlight);
                        Crashes.TrackError(new Exception(@"Mod set to highlight but not in list of loaded mods"), new Dictionary<string, string>()
                        {
                            { @"Moddesc path", modpathToHighlight }
                        });
                    }
                }

                backgroundTaskEngine.SubmitJobCompletion(uiTask);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoModSelectedText)));

                ModsLoaded = true;
                if (canCheckForModUpdates)
                {
                    CheckAllModsForUpdates();
                }
                else if (forceUpdateCheckOnCompletion && args.Result is Mod highlightedMod && highlightedMod.IsUpdatable)
                {
                    CheckModsForUpdates(new List<Mod>(new[] { highlightedMod }));
                }
            };
            bw.RunWorkerCompleted += (a, b) =>
            {
                ModsLoaded = true;
                if (b.Error != null)
                {
                    Log.Error(@"Exception occurred in ModLoader thread: " + b.Error.Message);
                }
                IsLoadingMods = false;
                if (b.Result is Mod m)
                {
                    ModsList_ListBox.SelectedItem = m;
                    ModsList_ListBox.ScrollIntoView(m);
                }
            };
            bw.RunWorkerAsync();
        }

        /// <summary>
        /// Calls CheckAllModsForUpdates(). This method should be called from the UI thread.
        /// </summary>
        private void CheckAllModsForUpdatesWrapper()
        {
            NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"Mod update check");
            nbw.DoWork += (a, b) => CheckAllModsForUpdates();
            nbw.RunWorkerAsync();
        }

        private void CheckAllModsForUpdates()
        {
            var updatableMods = VisibleFilteredMods.Where(x => x.IsUpdatable).ToList();
            if (updatableMods.Count > 0)
            {
                CheckModsForUpdates(updatableMods);
            }
        }

        private void CheckModsForUpdates(List<Mod> updatableMods, bool restoreMode = false)
        {
            Log.Information($@"Checking {updatableMods.Count} mods for updates. Turn on mod update logging to view which mods");
            if (Settings.LogModUpdater)
            {
                foreach (var m in updatableMods)
                {
                    Log.Information($@" >> Checking for updates to {m.ModName} {m.ParsedModVersion}");
                }
            }

            BackgroundTask bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"ModCheckForUpdates", M3L.GetString(M3L.string_checkingModsForUpdates), M3L.GetString(M3L.string_modUpdateCheckCompleted));
            void updateCheckProgressCallback(string newStr)
            {
                backgroundTaskEngine.SubmitBackgroundTaskUpdate(bgTask, newStr);
            }

            var updateManifestModInfos = OnlineContent.CheckForModUpdates(updatableMods, restoreMode, updateCheckProgressCallback);
            if (updateManifestModInfos != null)
            {
                //Calculate CLASSIC Updates
                var updates = updateManifestModInfos.Where(x => x.updatecode > 0 && (x.applicableUpdates.Count > 0 || x.filesToDelete.Count > 0)).ToList();
                foreach (var v in updates)
                {
                    Log.Information($@"Classic mod out of date: {v.mod.ModName} {v.mod.ParsedModVersion}, server version: {v.LocalizedServerVersionString}");
                }

                //Calculate MODMAKER Updates
                foreach (var mm in updatableMods.Where(x => x.ModModMakerID > 0))
                {
                    var matchingServerMod = updateManifestModInfos.FirstOrDefault(x => x is OnlineContent.ModMakerModUpdateInfo mmui && mmui.ModMakerId == mm.ModModMakerID);
                    if (matchingServerMod != null)
                    {
                        var serverVer = Version.Parse(matchingServerMod.versionstr + @".0"); //can't have single digit version
                        if (serverVer > mm.ParsedModVersion || restoreMode)
                        {
                            if (!restoreMode)
                            {
                                Log.Information($@"ModMaker mod out of date: {mm.ModName} {mm.ParsedModVersion}, server version: {serverVer}");
                            }
                            else
                            {
                                Log.Information($@"Restore mode: Show ModMaker mod {mm.ModName} as out of date. Server version: {serverVer}");
                            }
                            matchingServerMod.mod = mm;
                            updates.Add(matchingServerMod);
                            matchingServerMod.SetLocalizedInfo();
                        }
                    }
                }

                //Calculate NEXUSMOD Updates
                foreach (var mm in updatableMods.Where(x => x.NexusModID > 0 && x.ModClassicUpdateCode == 0)) //check zero as Mgamerz's mods will list me3tweaks with a nexus code still for integrations
                {
                    var matchingUpdateInfoForMod = updateManifestModInfos.OfType<OnlineContent.NexusModUpdateInfo>().FirstOrDefault(x => x.NexusModsId == mm.NexusModID
                                                                                                                                   && Enum.Parse<MEGame>(@"ME" + x.GameId) == mm.Game
                                                                                                                                   && updates.All(y => !y.mod.Equals(x.mod)));
                    if (matchingUpdateInfoForMod != null)
                    {
                        if (Version.TryParse(matchingUpdateInfoForMod.versionstr, out var serverVer))
                        {
                            if (serverVer > mm.ParsedModVersion)
                            {
                                // We need to make a clone in the event a mod uses duplicate code, such as Project Variety
                                OnlineContent.NexusModUpdateInfo clonedInfo = new OnlineContent.NexusModUpdateInfo(matchingUpdateInfoForMod) { mod = mm };
                                updates.Add(clonedInfo);
                                clonedInfo.SetLocalizedInfo();
                                Log.Information($@"NexusMods mod out of date: {mm.ModName} {mm.ParsedModVersion}, server version: {serverVer}");

                            }
                        }
                        else
                        {
                            Log.Error($@"Cannot parse nexusmods version of mod, skipping update check for {mm.ModName}. Server version string is { matchingUpdateInfoForMod.versionstr}");
                        }
                    }
                }

                updates = updates.Distinct().ToList();
                if (updates.Count > 0)
                {
                    Application.Current.Dispatcher.Invoke(delegate
                    {
                        var modUpdatesNotificationDialog = new ModUpdateInformation(updates);
                        modUpdatesNotificationDialog.Close += (sender, args) =>
                        {
                            ReleaseBusyControl();
                            if (args.Data is bool reloadMods && reloadMods)
                            {
                                LoadMods(updates.Count == 1 ? updates[0].mod : null);
                            }
                        };
                        ShowBusyControl(modUpdatesNotificationDialog);
                    });
                }
            }
            else
            {
                bgTask.finishedUiText = M3L.GetString(M3L.string_errorCheckingForModUpdates);
            }

            backgroundTaskEngine.SubmitJobCompletion(bgTask);
        }

        private void PopulateTargets(GameTarget selectedTarget = null)
        {
            RepopulatingTargets = true;
            InstallationTargets.ClearEx();
            SelectedGameTarget = null;
            MEDirectories.ReloadGamePaths(true); //this is redundant on the first boot but whatever.
            Log.Information(@"Populating game targets");
            List<GameTarget> targets = new List<GameTarget>();
            bool foundMe1Active = false;
            bool foundMe2Active = false;
            if (ME3Directory.DefaultGamePath != null && Directory.Exists(ME3Directory.DefaultGamePath))
            {
                var target = new GameTarget(MEGame.ME3, ME3Directory.DefaultGamePath, true);
                var failureReason = target.ValidateTarget();
                if (failureReason == null)
                {
                    Log.Information(@"Current boot target for ME3: " + target.TargetPath);
                    targets.Add(target);
                    Utilities.AddCachedTarget(target);
                }
                else
                {
                    Log.Error(@"Current boot target for ME3 is invalid: " + failureReason);
                }
            }

            if (ME2Directory.DefaultGamePath != null && Directory.Exists(ME2Directory.DefaultGamePath))
            {
                var target = new GameTarget(MEGame.ME2, ME2Directory.DefaultGamePath, true);
                var failureReason = target.ValidateTarget();
                if (failureReason == null)
                {
                    Log.Information(@"Current boot target for ME2: " + target.TargetPath);
                    targets.Add(target);
                    Utilities.AddCachedTarget(target);
                    foundMe2Active = true;
                }
                else
                {
                    Log.Error(@"Current boot target for ME2 is invalid: " + failureReason);
                }
            }

            if (ME1Directory.DefaultGamePath != null && Directory.Exists(ME1Directory.DefaultGamePath))
            {
                var target = new GameTarget(MEGame.ME1, ME1Directory.DefaultGamePath, true);
                var failureReason = target.ValidateTarget();
                if (failureReason == null)
                {
                    Log.Information(@"Current boot target for ME1: " + target.TargetPath);
                    targets.Add(target);
                    Utilities.AddCachedTarget(target);
                    foundMe1Active = true;
                }
                else
                {
                    Log.Error(@"Current boot target for ME1 is invalid: " + failureReason);
                }
            }

            // Read steam locations
            void addSteamTarget(string targetPath, bool foundActiveAlready, MEGame game)
            {
                if (!string.IsNullOrWhiteSpace(targetPath)
                    && Directory.Exists(targetPath)
                    && !targets.Any(x => x.TargetPath.Equals(targetPath, StringComparison.InvariantCultureIgnoreCase)))
                {
                    var target = new GameTarget(game, targetPath, !foundActiveAlready);
                    var failureReason = target.ValidateTarget();
                    if (failureReason == null)
                    {
                        Log.Information($@"Found Steam game for {game}: " + target.TargetPath);
                        // Todo: Figure out how to insert at correct index
                        targets.Add(target);
                        Utilities.AddCachedTarget(target);
                    }
                    else
                    {
                        Log.Error($@"Steam version of {game} at {targetPath} is invalid: {failureReason}");
                    }
                }
            }

            // ME1
            addSteamTarget(Utilities.GetRegistrySettingString(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 17460",
                @"InstallLocation"), foundMe1Active, MEGame.ME1);

            // ME2
            addSteamTarget(Utilities.GetRegistrySettingString(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 24980",
                @"InstallLocation"), foundMe2Active, MEGame.ME2);

            Log.Information(@"Loading cached targets");
            targets.AddRange(Utilities.GetCachedTargets(MEGame.ME3, targets));
            targets.AddRange(Utilities.GetCachedTargets(MEGame.ME2, targets));
            targets.AddRange(Utilities.GetCachedTargets(MEGame.ME1, targets));

            // Load the targets


            // ORDER THE TARGETS
            targets = targets.Distinct().ToList();
            List<GameTarget> finalList = new List<GameTarget>();
            GameTarget aTarget = targets.FirstOrDefault(x => x.Game == MEGame.ME3 && x.RegistryActive);
            if (aTarget != null) finalList.Add(aTarget);
            aTarget = targets.FirstOrDefault(x => x.Game == MEGame.ME2 && x.RegistryActive);
            if (aTarget != null) finalList.Add(aTarget);
            aTarget = targets.FirstOrDefault(x => x.Game == MEGame.ME1 && x.RegistryActive);
            if (aTarget != null) finalList.Add(aTarget);

            if (targets.Count > finalList.Count)
            {
                finalList.Add(new GameTarget(MEGame.Unknown, $@"==================={M3L.GetString(M3L.string_otherSavedTargets)}===================", false) { Selectable = false, IsCustomOption = true });
            }

            finalList.AddRange(targets.Where(x => x.Game == MEGame.ME3 && !x.RegistryActive));
            finalList.AddRange(targets.Where(x => x.Game == MEGame.ME2 && !x.RegistryActive));
            finalList.AddRange(targets.Where(x => x.Game == MEGame.ME1 && !x.RegistryActive));

            InstallationTargets.ReplaceAll(finalList);

            if (selectedTarget != null)
            {
                //find new corresponding target
                var newTarget = targets.FirstOrDefault(x => x.TargetPath == selectedTarget.TargetPath);
                if (newTarget != null)
                {
                    SelectedGameTarget = newTarget;
                }
            }
            else if (!string.IsNullOrWhiteSpace(Settings.LastSelectedTarget) && InstallationTargets.FirstOrDefaultOut(x => !x.IsCustomOption && x.TargetPath.Equals(Settings.LastSelectedTarget), out var matchingTarget))
            {
                SelectedGameTarget = matchingTarget;
            }
            else
            {
                if (InstallationTargets.Count > 0)
                {
                    var firstSelectableTarget = InstallationTargets.FirstOrDefault(x => x.Selectable);
                    if (firstSelectableTarget != null)
                    {
                        SelectedGameTarget = firstSelectableTarget;
                    }
                }
            }

            BackupService.SetInstallStatuses(InstallationTargets);
            RepopulatingTargets = false;
        }

        public async void OnSelectedModChanged()
        {
            if (SelectedMod != null)
            {
                SetWebsitePanelVisibility(SelectedMod.ModWebsite != Mod.DefaultWebsite);

                if (SelectedGameTarget == null || SelectedGameTarget.Game != SelectedMod.Game)
                {
                    // Update the target
                    var installTarget = InstallationTargets.FirstOrDefault(x => x.RegistryActive && x.Game == SelectedMod.Game);
                    if (installTarget != null)
                    {
                        SelectedGameTarget = installTarget;
                    }
                }

                if (SelectedMod.BannerBitmap == null)
                {
                    SelectedMod.LoadBannerImage(); // Method will check if it's null
                }

                VisitWebsiteText = SelectedMod.ModWebsite != Mod.DefaultWebsite ? M3L.GetString(M3L.string_interp_visitSelectedModWebSite, SelectedMod.ModName) : "";

                if (NexusModsUtilities.HasAPIKey)
                {
                    if (SelectedMod.NexusModID > 0)
                    {
                        if (SelectedMod.IsOwnMod)
                        {
                            CurrentModEndorsementStatus = M3L.GetString(M3L.string_cannotEndorseOwnMod);
                        }
                        else
                        {
                            CurrentModEndorsementStatus = M3L.GetString(M3L.string_gettingEndorsementStatus);

                            var endorsed = await SelectedMod.GetEndorsementStatus(NexusUserID);
                            if (endorsed != null)
                            {
                                if (SelectedMod != null)
                                {
                                    //mod might have changed since we did BG thread wait.
                                    if (SelectedMod.CanEndorse)
                                    {
                                        UpdatedEndorsementString();
                                    }
                                    else
                                    {
                                        CurrentModEndorsementStatus = M3L.GetString(M3L.string_cannotEndorseMod);
                                    }
                                }
                            }
                            else
                            {
                                // null = self mod
                                CurrentModEndorsementStatus = M3L.GetString(M3L.string_cannotEndorseOwnMod);

                            }
                        }

                        EndorseSelectedModCommand.RaiseCanExecuteChanged();
                        //CommandManager.InvalidateRequerySuggested();
                    }
                    else
                    {
                        CurrentModEndorsementStatus = $@"{M3L.GetString(M3L.string_cannotEndorseMod)} ({M3L.GetString(M3L.string_notLinkedToNexusMods)})";
                    }
                }
                else
                {
                    CurrentModEndorsementStatus = $@"{M3L.GetString(M3L.string_cannotEndorseMod)} ({M3L.GetString(M3L.string_notAuthenticated)})";
                }
                //CurrentDescriptionText = newSelectedMod.DisplayedModDescription;
            }
            else
            {
                VisitWebsiteText = "";
                SetWebsitePanelVisibility(false);
                CurrentDescriptionText = DefaultDescriptionText;
            }

            CanApplyMod(); // This sets the text. Good design MG
        }

        private void UpdatedEndorsementString()
        {
            if (SelectedMod != null)
            {
                if (SelectedMod.IsEndorsed)
                {
                    CurrentModEndorsementStatus = M3L.GetString(M3L.string_modEndorsed);
                }
                else
                {
                    CurrentModEndorsementStatus = M3L.GetString(M3L.string_endorseMod);
                }
            }
        }

        private void SetWebsitePanelVisibility(bool open)
        {
            Storyboard sb = this.FindResource(open ? @"OpenWebsitePanel" : @"CloseWebsitePanel") as Storyboard;
            if (sb.IsSealed)
            {
                sb = sb.Clone();
            }

            Storyboard.SetTarget(sb, VisitWebsitePanel);
            sb.Begin();
        }

        private void RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Utilities.OpenWebpage(e.Uri.AbsoluteUri);
        }

        private void ExitApplication_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void OpenModFolder_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMod != null)
            {
                Utilities.OpenExplorer(SelectedMod.ModPath);
            }
        }

        private void OpenME3Tweaks_Click(object sender, RoutedEventArgs e)
        {
            Utilities.OpenWebpage(@"https://me3tweaks.com/");
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutPanel();
            aboutWindow.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(aboutWindow);
        }

        private void ModManagerWindow_Closing(object sender, CancelEventArgs e)
        {
            //Settings.Save();
        }

        private void FailedMods_LinkClick(object sender, RequestNavigateEventArgs e)
        {
            var failedModsPanel = new FailedModsPanel(FailedMods.ToList());
            failedModsPanel.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is Mod failedmod)
                {
                    BackgroundWorker bw = new BackgroundWorker();
                    bw.DoWork += (a, b) => { CheckModsForUpdates(new List<Mod>(new Mod[] { failedmod }), true); };
                    bw.RunWorkerAsync();
                }
            };
            ShowBusyControl(failedModsPanel);
        }

        private void OpenModsDirectory_Click(object sender, RoutedEventArgs e)
        {
            Utilities.OpenExplorer(Utilities.GetModsDirectory());
        }

        private const int STARTUP_FAIL_CRITICAL_FILES_MISSING = 1;

        public void PerformStartupNetworkFetches(bool firstStartupCheck)
        {
            NamedBackgroundWorker bw = new NamedBackgroundWorker(@"ContentCheckNetworkThread");
            bw.WorkerReportsProgress = true;
            bw.ProgressChanged += (sender, args) =>
            {
                //Help items loading
                if (args.UserState is List<SortableHelpElement> sortableHelpItems)
                {
                    setDynamicHelpMenu(sortableHelpItems);
                }
            };
            bw.DoWork += (a, b) =>
            {
                Log.Information(@"Start of content check network thread. First startup check: " + firstStartupCheck);

                BackgroundTask bgTask;
                if (firstStartupCheck)
                {

                    UpdateBinkStatus(MEGame.ME1);
                    UpdateBinkStatus(MEGame.ME2);
                    UpdateBinkStatus(MEGame.ME3);
                    bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"EnsureCriticalFiles", M3L.GetString(M3L.string_downloadingRequiredFiles), M3L.GetString(M3L.string_requiredFilesDownloaded));
                    if (!OnlineContent.EnsureCriticalFiles())
                    {
                        //Critical files not loaded!

                        b.Result = STARTUP_FAIL_CRITICAL_FILES_MISSING;
                        bgTask.finishedUiText = M3L.GetString(M3L.string_failedToDownloadRequiredFiles);
                        backgroundTaskEngine.SubmitJobCompletion(bgTask);
                        return;
                    }

                    backgroundTaskEngine.SubmitJobCompletion(bgTask);

                    var updateCheckTask = backgroundTaskEngine.SubmitBackgroundJob(@"UpdateCheck", M3L.GetString(M3L.string_checkingForModManagerUpdates), M3L.GetString(M3L.string_completedModManagerUpdateCheck));
                    try
                    {
                        OnlineContent.FetchOnlineStartupManifest(Settings.BetaMode);
                        if (App.ServerManifest != null && int.TryParse(App.ServerManifest[@"latest_build_number"], out var latestServerBuildNumer))
                        {
                            if (latestServerBuildNumer > App.BuildNumber)
                            {
                                Log.Information(@"Found update for Mod Manager: Build " + latestServerBuildNumer);

                                Application.Current.Dispatcher.Invoke(delegate
                                {
                                    var updateAvailableDialog = new ProgramUpdateNotification();
                                    updateAvailableDialog.Close += (sender, args) => { ReleaseBusyControl(); };
                                    ShowBusyControl(updateAvailableDialog);
                                });
                            }
#if !DEBUG
                            else if (latestServerBuildNumer == App.BuildNumber)
                            {
                                if (App.ServerManifest.TryGetValue(@"build_md5", out var md5) && !string.IsNullOrWhiteSpace(md5))
                                {
                                    var localmd5 = Utilities.CalculateMD5(App.ExecutableLocation);
                                    if (localmd5 != md5)
                                    {
                                        //Update is available.
                                        {
                                            Log.Information(@"MD5 of local exe doesn't match server version, minor update detected.");
                                            Application.Current.Dispatcher.Invoke(delegate
                                            {
                                                var updateAvailableDialog = new ProgramUpdateNotification(localmd5);
                                                updateAvailableDialog.UpdateMessage = M3L.GetString(M3L.string_interp_minorUpdateAvailableMessage, App.BuildNumber.ToString());
                                                updateAvailableDialog.Close += (sender, args) => { ReleaseBusyControl(); };
                                                ShowBusyControl(updateAvailableDialog);
                                            });
                                        }
                                    }
                                }
                            }
#endif
                            else
                            {
                                Log.Information(@"Mod Manager is up to date");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        //Error checking for updates!
                        Log.Error(@"Checking for updates failed: " + App.FlattenException(e));
                        updateCheckTask.finishedUiText = M3L.GetString(M3L.string_failedToCheckForUpdates);
                    }

                    backgroundTaskEngine.SubmitJobCompletion(updateCheckTask);

                    if (App.ServerManifest != null)
                    {
                        bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"MixinFetch", M3L.GetString(M3L.string_loadingMixins), M3L.GetString(M3L.string_loadedMixins));
                        try
                        {
                            //Mixins
                            MixinHandler.ServerMixinHash = App.ServerManifest[@"mixinpackagemd5"];
                            if (!MixinHandler.IsMixinPackageUpToDate())
                            {
                                //Download new package.
                                var memoryPackage = OnlineContent.DownloadToMemory(MixinHandler.MixinPackageEndpoint, hash: MixinHandler.ServerMixinHash);
                                if (memoryPackage.errorMessage != null)
                                {
                                    Log.Error(@"Error fetching mixin package: " + memoryPackage.errorMessage);
                                    bgTask.finishedUiText = M3L.GetString(M3L.string_failedToUpdateMixinPackage);
                                }
                                else
                                {
                                    File.WriteAllBytes(MixinHandler.MixinPackagePath, memoryPackage.result.ToArray());
                                    Log.Information(@"Wrote ME3Tweaks Mixin Package to disk");
                                    MixinHandler.LoadME3TweaksPackage();
                                }
                            }
                            else
                            {
                                Log.Information(@"ME3Tweaks Mixin Package is up to date");
                                MixinHandler.LoadME3TweaksPackage();
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(@"Error fetching mixin package: " + e.Message);
                            bgTask.finishedUiText = M3L.GetString(M3L.string_errorLoadingMixinPackage);

                        }

                        backgroundTaskEngine.SubmitJobCompletion(bgTask);

                        var hasUpdatedLocalization = OnlineContent.HasUpdatedLocalization(App.CurrentLanguage);
                        if (hasUpdatedLocalization.HasValue)
                        {
                            SetApplicationLanguage(App.CurrentLanguage, false); //Force update of localization
                        }
                    }
                    else
                    {
                        // load cached (will load nothing if there is no local file)
                        MixinHandler.LoadME3TweaksPackage();
                    }
                }

                //bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"EnsureStaticFiles", M3L.GetString(M3L.string_downloadingStaticFiles), M3L.GetString(M3L.string_staticFilesDownloaded));
                //success = OnlineContent.EnsureStaticAssets();
                //if (!success)
                //{
                //    Application.Current.Dispatcher.Invoke(delegate { M3L.ShowDialog(this, M3L.GetString(M3L.string_dialogCouldNotDownloadStaticAssets), M3L.GetString(M3L.string_missingAssets), MessageBoxButton.OK, MessageBoxImage.Error); });
                //    bgTask.finishedUiText = M3L.GetString(M3L.string_failedToDownloadStaticFiles);
                //}

                //backgroundTaskEngine.SubmitJobCompletion(bgTask);

                bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"LoadTutorialService", M3L.GetString(M3L.string_checkingTutorialAssets), M3L.GetString(M3L.string_checkedTutorialAssets));
                App.TutorialService = OnlineContent.FetchTutorialManifest(!firstStartupCheck);
                OnlineContent.TouchupTutorial();
                backgroundTaskEngine.SubmitJobCompletion(bgTask);

                bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"LoadDynamicHelp", M3L.GetString(M3L.string_loadingDynamicHelp), M3L.GetString(M3L.string_loadingDynamicHelp));
                var helpItemsLoading = OnlineContent.FetchLatestHelp(App.CurrentLanguage, false, !firstStartupCheck);
                bw.ReportProgress(0, helpItemsLoading);
                backgroundTaskEngine.SubmitJobCompletion(bgTask);

                bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"ThirdPartyServicesFetch", M3L.GetString(M3L.string_loadingThirdPartyServices), M3L.GetString(M3L.string_loadedThirdPartyServices));
                App.ThirdPartyIdentificationService = OnlineContent.FetchThirdPartyIdentificationManifest(!firstStartupCheck);
                App.BasegameFileIdentificationService = OnlineContent.FetchBasegameFileIdentificationServiceManifest(!firstStartupCheck);
                App.ThirdPartyImportingService = OnlineContent.FetchThirdPartyImportingService(!firstStartupCheck);
                ASIManager.LoadManifest(false, !firstStartupCheck);
                backgroundTaskEngine.SubmitJobCompletion(bgTask);

                bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"LoadTipsService", M3L.GetString(M3L.string_loadingTipsService), M3L.GetString(M3L.string_loadedTipsService));
                try
                {
                    App.TipsService = OnlineContent.FetchTipsService(!firstStartupCheck);
                    SetTipsForLanguage();
                }
                catch (Exception e)
                {
                    Log.Error(@"Failed to load tips service: " + e.Message);
                }
                backgroundTaskEngine.SubmitJobCompletion(bgTask);


                if (firstStartupCheck)
                {
                    bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"WritePermissions", M3L.GetString(M3L.string_checkingWritePermissions), M3L.GetString(M3L.string_checkedUserWritePermissions));
                    CheckTargetPermissions(true);
                    backgroundTaskEngine.SubmitJobCompletion(bgTask);
                }

                if (OnlineContent.CanFetchContentThrottleCheck())
                {
                    Settings.LastContentCheck = DateTime.Now;
                }

                Log.Information(@"End of content check network thread");
                b.Result = 0; //all good
            };
            bw.RunWorkerCompleted += (a, b) =>
            {
                if (b.Error != null)
                {
                    Log.Error(@"Exception occurred in NetworkFetch thread: " + b.Error.Message);
                }
                else if (b.Result is int i)
                {
                    if (i != 0)
                    {
                        switch (i)
                        {
                            case STARTUP_FAIL_CRITICAL_FILES_MISSING:
                                var res = M3L.ShowDialog(this, M3L.GetString(M3L.string_dialogCriticalFilesMissing), M3L.GetString(M3L.string_requiredFilesNotDownloaded), MessageBoxButton.OK, MessageBoxImage.Error);
                                Environment.Exit(1);
                                break;
                        }
                    }
                    else
                    {
                        ContentCheckInProgress = false;
                    }
                }

                NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"BackupCheck");
                nbw.DoWork += (a, b) =>
                {
                    var me1CheckRequired = BackupService.GetGameBackupPath(MEGame.ME1) == null && BackupService.GetGameBackupPath(MEGame.ME1, false) != null;
                    var me2CheckRequired = BackupService.GetGameBackupPath(MEGame.ME2) == null && BackupService.GetGameBackupPath(MEGame.ME2, false) != null;
                    var me3CheckRequired = BackupService.GetGameBackupPath(MEGame.ME3) == null && BackupService.GetGameBackupPath(MEGame.ME3, false) != null;

                    if (me1CheckRequired || me2CheckRequired || me3CheckRequired)
                    {
                        var bgTask = backgroundTaskEngine.SubmitBackgroundJob(@"BackupCheck", M3L.GetString(M3L.string_checkingBackups), M3L.GetString(M3L.string_finishedCheckingBackups));
                        if (me1CheckRequired) VanillaDatabaseService.CheckAndTagBackup(MEGame.ME1);
                        if (me2CheckRequired) VanillaDatabaseService.CheckAndTagBackup(MEGame.ME2);
                        if (me3CheckRequired) VanillaDatabaseService.CheckAndTagBackup(MEGame.ME3);

                        backgroundTaskEngine.SubmitJobCompletion(bgTask);
                    }
                };
                nbw.RunWorkerAsync();
                CommandManager.InvalidateRequerySuggested(); //refresh bindings that depend on this

                //byte[] bytes = File.ReadAllBytes(@"C:\Users\mgame\Source\Repos\ME3Tweaks\MassEffectModManager\MassEffectModManagerCore\Deployment\Releases\ME3TweaksModManagerExtractor_6.0.0.99.exe");
                //MemoryStream ms = new MemoryStream(bytes);
                //SevenZipExtractor sve = new SevenZipExtractor(ms);
                //sve.ExtractArchive(@"C:\users\public\documents");
            };
            ContentCheckInProgress = true;
            bw.RunWorkerAsync();
        }

        //string convertKey(string pcKey, StringRef sref)
        //{
        //    switch (pcKey)
        //    {
        //        case "[Shared_SquadMove1]":
        //            return "[XBoxB_Btn_DPadL]";
        //        case "[Shared_SquadMove2]":
        //            return "[XBoxB_Btn_DPadR]";
        //        case "[Shared_Melee]":
        //            return "[XBoxB_Btn_B]";
        //        default:
        //            Debug.WriteLine("Unknown UI key " + pcKey);
        //            break;
        //    }

        //    return null;
        //}

        private void debugMethod()
        {
            //var mixinP = @"X:\m3modlibrary\ME3\RealisticGravOLD";
            //foreach (var mp in Directory.GetFiles(mixinP, "*.pcc", SearchOption.AllDirectories))
            //{
            //    var packageName = Path.GetFileName(mp);
            //    var dirname = Directory.GetParent(packageName).Parent.Parent.Name;


            //    MemoryStream fileData = null;
            //    if (dirname == "BASEGAME")
            //    {
            //        fileData = VanillaDatabaseService.FetchBasegameFile(MEGame.ME3, packageName);
            //    }
            //    else
            //    {
            //        var map = ModJob.GetHeadersToDLCNamesMap(MEGame.ME3);
            //        var header = ModMakerCompiler.DefaultFoldernameToHeader(dirname);
            //        fileData = VanillaDatabaseService.FetchFileFromVanillaSFAR(map[header], packageName);
            //    }

            //    if (dirname == "BASEGAME")
            //    {
            //        var package = MEPackageHandler.OpenMEPackageFromStream(fileData);
            //        fileData = package.SaveToStream(false, false, true);
            //    }
            //}
        }

        /// <summary>
        /// Refreshes the dynamic help list
        /// </summary>
        /// <param name="sortableHelpItems"></param>
        private void setDynamicHelpMenu(List<SortableHelpElement> sortableHelpItems)
        {
            //Replacing the dynamic help menu
            //DynamicHelp_MenuItem.Items.RemoveAll(x=>x.Tag is string str && str == "DynamicHelp");

            var dynamicMenuItems = RecursiveBuildDynamicHelpMenuItems(sortableHelpItems);

            //Clear old items out
            for (int i = HelpMenuItem.Items.Count - 1; i > 0; i--)
            {
                if (HelpMenuItem.Items[i] is MenuItem menuItem && menuItem.Tag is string str && str == @"DynamicHelp")
                {
                    HelpMenuItem.Items.Remove(menuItem);
                }
            }

            dynamicMenuItems.Reverse(); //we are going to insert these in reverse order
            var dynamicHelpHeaderIndex = HelpMenuItem.Items.IndexOf(DynamicHelp_MenuItem) + 1;
            foreach (var v in dynamicMenuItems)
            {
                HelpMenuItem.Items.Insert(dynamicHelpHeaderIndex, v);
            }
        }

        private void SetTipsForLanguage()
        {
            if (App.TipsService != null)
            {
                if (App.TipsService.TryGetValue(App.CurrentLanguage, out var tips))
                {
                    LoadedTips.ReplaceAll(tips);
                }
                else
                {
                    LoadedTips.Clear();
                }

                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NoModSelectedText)));
            }
        }

        private List<MenuItem> RecursiveBuildDynamicHelpMenuItems(List<SortableHelpElement> sortableHelpItems)
        {
            //Todo: Localized version
            List<MenuItem> dynamicMenuItems = new List<MenuItem>();
            foreach (var item in sortableHelpItems)
            {
                MenuItem m = new MenuItem()
                {
                    Header = item.Title,
                    ToolTip = item.ToolTip,
                    Tag = @"DynamicHelp"
                };
                if (!string.IsNullOrEmpty(item.URL))
                {
                    //URL HelpItem
                    m.Click += (o, eventArgs) => Utilities.OpenWebpage(item.URL);
                }
                else if (!string.IsNullOrEmpty(item.ModalTitle))
                {
                    //Modal dialog
                    item.ModalText = Utilities.ConvertBrToNewline(item.ModalText);
                    m.Click += (o, eventArgs) => { new DynamicHelpItemModalWindow(item) { Owner = this }.ShowDialog(); };
                }

                dynamicMenuItems.Add(m);

                if (item.Children.Count > 0)
                {
                    var children = RecursiveBuildDynamicHelpMenuItems(item.Children);
                    foreach (var v in children)
                    {
                        m.Items.Add(v);
                    }
                }
            }

            return dynamicMenuItems;
        }

        private void GenerateStarterKit_Clicked(object sender, RoutedEventArgs e)
        {
            MEGame g = MEGame.Unknown;
            if (sender == GenerateStarterKitME1_MenuItem) g = MEGame.ME1;
            if (sender == GenerateStarterKitME2_MenuItem) g = MEGame.ME2;
            if (sender == GenerateStarterKitME3_MenuItem) g = MEGame.ME3;
            new StarterKitGeneratorWindow(g) { Owner = this }.ShowDialog();
        }

        private void LaunchExternalTool_Clicked(object sender, RoutedEventArgs e)
        {
            string tool = null;
            if (sender == ALOTInstaller_MenuItem)
            {
                BootALOTInstallerPassthrough(ExternalToolLauncher.ALOTInstaller);
                return;
            }
            if (sender == MassEffectRandomizer_MenuItem) tool = ExternalToolLauncher.MER;
            if (sender == ME3Explorer_MenuItem) tool = ExternalToolLauncher.ME3Explorer;
            if (sender == ME3ExplorerBeta_MenuItem) tool = ExternalToolLauncher.ME3Explorer_Beta;
            if (sender == MassEffectModder_MenuItem) tool = ExternalToolLauncher.MEM;
            //if (sender == EGMSettings_MenuItem) tool = ExternalToolLauncher.EGMSettings; //EGM settings has it's own command and it not invoked through this menu
            if (tool == null) throw new Exception(@"LaunchExternalTool handler set but no relevant tool was specified! This is a bug. Please report it to Mgamerz on Discord");
            LaunchExternalTool(tool);
        }

        private void LaunchExternalTool(string tool, string arguments = null)
        {
            if (tool != null)
            {
                Analytics.TrackEvent(@"Launched external tool", new Dictionary<string, string>()
                {
                    {@"Tool name", tool},
                    {@"Arguments", arguments}
                });
                var exLauncher = new ExternalToolLauncher(tool, arguments);
                exLauncher.Close += (a, b) => { ReleaseBusyControl(); };
                ShowBusyControl(exLauncher);
            }
        }

        private void OpenASIManager()
        {
            Analytics.TrackEvent(@"Launched ASI Manager", new Dictionary<string, string>()
            {
                {@"Invocation method", @"Menu"}
            });
            var exLauncher = new ASIManagerPanel();
            exLauncher.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(exLauncher);
        }

        private bool RepopulatingTargets;
        private Dictionary<string, MenuItem> languageMenuItems;

        public void OnSelectedGameTargetChanged()
        {
            if (!RepopulatingTargets && SelectedGameTarget != null)
            {
                //Settings.Save();
                if (!SelectedGameTarget.RegistryActive)
                {
                    UpdateBinkStatus(SelectedGameTarget.Game);
                    try
                    {
                        var hresult = UpdateBootTarget(SelectedGameTarget);
                        if (hresult == -3) return; //do nothing.
                        if (hresult == 0)
                        {
                            //rescan
                            PopulateTargets(SelectedGameTarget);
                            SelectedGameTarget.UpdateLODs(Settings.AutoUpdateLODs2K);
                        }

                        Analytics.TrackEvent(@"Changed to non-active target", new Dictionary<string, string>()
                        {
                            {@"New target", SelectedGameTarget.Game.ToString()},
                            {@"ALOT Installed", SelectedGameTarget.TextureModded.ToString()}
                        });
                    }
                    catch (Win32Exception ex)
                    {
                        Log.Warning(@"Win32 exception occurred updating boot target. User maybe pressed no to the UAC dialog?: " + ex.Message);
                    }
                }

                Settings.LastSelectedTarget = SelectedGameTarget?.TargetPath;
            }
        }

        private void UploadLog_Click(object sender, RoutedEventArgs e)
        {
            var logUploaderUI = new LogUploader();
            logUploaderUI.Close += (a, b) => { ReleaseBusyControl(); };
            ShowBusyControl(logUploaderUI);
        }


        public string CurrentModEndorsementStatus { get; private set; } = M3L.GetString(M3L.string_endorseMod);
        public bool IsEndorsingMod { get; private set; }
        public string NexusUsername { get; set; }
        public int NexusUserID { get; set; }

        public bool CanOpenMEIM()
        {
            //ensure not already open
            foreach (var window in Application.Current.Windows)
            {
                if (window is ME1IniModder) return false;
            }

            var installed = InstallationTargets.Any(x => x.Game == MEGame.ME1);
            if (installed)
            {
                var iniFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"BioWare", @"Mass Effect", @"Config", @"BIOGame.ini");
                return File.Exists(iniFile);
            }

            return false;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string ext = Path.GetExtension(files[0]).ToLower();
                Log.Information(@"File dropped onto interface: " + files[0]);
                switch (ext)
                {
                    case @".rar":
                    case @".7z":
                    case @".zip":
                    case @".exe":
                        App.SubmitAnalyticTelemetryEvent(@"User opened mod archive for import", new Dictionary<string, string>
                        {
                            {@"Method", @"Drag & drop"},
                            {@"Filename", Path.GetFileName(files[0])}
                        });
                        openModImportUI(files[0]);
                        break;
                    //TPF, .mod, .mem
                    case @".tpf":
                    case @".mod":
                    case @".mem":
                        App.SubmitAnalyticTelemetryEvent(@"User redirected to MEM/ALOT Installer", new Dictionary<string, string> { { @"Filename", Path.GetFileName(files[0]) } });
                        M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_dialog_installingTextureMod, ext), M3L.GetString(M3L.string_nonModManagerModFound), MessageBoxButton.OK, MessageBoxImage.Warning);
                        break;
                    case @".me2mod":
                        App.SubmitAnalyticTelemetryEvent(@"User opened me2mod file", new Dictionary<string, string> { { @"Filename", Path.GetFileName(files[0]) } });
                        openModImportUI(files[0]);
                        break;
                    case @".xaml":
                        if (Settings.DeveloperMode)
                        {
                            LoadExternalLocalizationDictionary(files[0]);
                        }
                        break;
                    case @".bin":
                        //Check for Coalesced
                        {
                            using var fs = new FileStream(files[0], FileMode.Open, FileAccess.Read);
                            var magic = fs.ReadInt32();
                            if (magic == 0x666D726D) //fmrm (backwards)
                            {

                                NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"Coalesced Decompiler");
                                var task = backgroundTaskEngine.SubmitBackgroundJob(@"CoalescedDecompile", M3L.GetString(M3L.string_decompilingCoalescedFile), M3L.GetString(M3L.string_decompiledCoalescedFile));
                                nbw.DoWork += (a, b) =>
                                {
                                    var dest = Path.Combine(Directory.GetParent(files[0]).FullName, Path.GetFileNameWithoutExtension(files[0]));
                                    Log.Information($@"Deompiling coalesced file: {files[0]} -> {dest}");
                                    Converter.ConvertToXML(files[0], dest);
                                    Log.Information(@"Decompiled coalesced file");
                                };
                                nbw.RunWorkerCompleted += (a, b) =>
                                {
                                    if (b.Error != null)
                                    {
                                        Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                                    }
                                    backgroundTaskEngine.SubmitJobCompletion(task);
                                };
                                nbw.RunWorkerAsync();
                            }
                        }

                        break;
                    case @".xml":
                        //Check if it's ModMaker sideload, coalesced manifest, or TLK
                        {
                            try
                            {
                                var xmldocument = XDocument.Load(files[0]);
                                var rootElement = xmldocument.Root;
                                if (rootElement.Name == @"ModMaker")
                                {
                                    //Modmaker Mod, sideload
                                    var modmakerPanel = new ModMakerPanel()
                                    {
                                        LocalFileOption = true,
                                        LocalFilePath = files[0]
                                    };

                                    modmakerPanel.Close += (a, b) =>
                                    {
                                        ReleaseBusyControl();
                                        if (b.Data is Mod m)
                                        {
                                            LoadMods(m);
                                        }
                                    };
                                    ShowBusyControl(modmakerPanel);
                                    break;
                                }



                                if (rootElement.Name == @"CoalesceFile")
                                {
                                    bool failedToCompileCoalesced = false;
                                    void errorCompilingCoalesced(string message)
                                    {
                                        Application.Current.Dispatcher.Invoke(delegate
                                        {
                                            failedToCompileCoalesced = true;
                                            M3L.ShowDialog(this, message, M3L.GetString(M3L.string_errorCompilingCoalesced), MessageBoxButton.OK, MessageBoxImage.Error);
                                        });
                                    }

                                    //Coalesced manifest
                                    NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"Coalesced Compiler");
                                    var task = backgroundTaskEngine.SubmitBackgroundJob(@"CoalescedCompile", M3L.GetString(M3L.string_compilingCoalescedFile), M3L.GetString(M3L.string_compiledCoalescedFile));
                                    nbw.DoWork += (a, b) =>
                                    {
                                        var dest = Path.Combine(Directory.GetParent(files[0]).FullName, rootElement.Attribute(@"name").Value);
                                        Log.Information($@"Compiling coalesced file: {files[0]} -> {dest}");
                                        try
                                        {
                                            Converter.ConvertToBin(files[0], dest);
                                            Log.Information(@"Compiled coalesced file");
                                        }
                                        catch (Exception e)
                                        {
                                            Log.Error($@"Error compiling Coalesced file: {e.Message}:");
                                            Log.Error(App.FlattenException(e));
                                            errorCompilingCoalesced(M3L.GetString(M3L.string_interp_exceptionOccuredWhileCompilingCoalsecedFileX, e.Message));
                                        }
                                    };
                                    nbw.RunWorkerCompleted += (a, b) =>
                                    {
                                        if (b.Error != null)
                                        {
                                            Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                                        }

                                        if (failedToCompileCoalesced) task.finishedUiText = M3L.GetString(M3L.string_errorCompilingCoalesced);
                                        backgroundTaskEngine.SubmitJobCompletion(task);
                                    };
                                    nbw.RunWorkerAsync();
                                    break;
                                }

                                bool failedToCompileTLK = false;
                                void errorCompilingTLK(string message)
                                {
                                    Application.Current.Dispatcher.Invoke(delegate
                                    {
                                        failedToCompileTLK = true;
                                        M3L.ShowDialog(this, message, M3L.GetString(M3L.string_errorCompilingTLK), MessageBoxButton.OK, MessageBoxImage.Error);
                                    });
                                }

                                // Tankmaster's uses a capital T where ME3Explorer used lowercase t
                                if (rootElement.Name == @"TlkFile")
                                {
                                    //TLK file - ensure it's the manifest one
                                    var sourceName = rootElement.Attribute(@"source");
                                    if (sourceName != null)
                                    {
                                        //This is a manifest file
                                        /*
                                         * Manifest File
                                         * Folder with same name
                                         * |-> TLK.xml files
                                         */
                                        NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"TLKTranspiler - CompileTankmaster");
                                        var task = backgroundTaskEngine.SubmitBackgroundJob(@"TranspilerCompile", M3L.GetString(M3L.string_compilingTLKFile), M3L.GetString(M3L.string_compiledTLKFile));
                                        nbw.DoWork += (a, b) => { TLKTranspiler.CompileTLKManifest(files[0], rootElement, errorCompilingTLK); };
                                        nbw.RunWorkerCompleted += (a, b) =>
                                        {
                                            if (b.Error != null)
                                            {
                                                Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                                            }
                                            if (failedToCompileTLK) task.finishedUiText = M3L.GetString(M3L.string_compilingFailed);
                                            backgroundTaskEngine.SubmitJobCompletion(task);
                                        };
                                        nbw.RunWorkerAsync();
                                    }
                                    else
                                    {
                                        //Is this a straight up TLK?
                                        NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"TLKTranspiler - CompileTankmaster");
                                        var task = backgroundTaskEngine.SubmitBackgroundJob(@"TranspilerCompile", M3L.GetString(M3L.string_compilingTLKFile), M3L.GetString(M3L.string_compiledTLKFile));
                                        nbw.DoWork += (a, b) => { TLKTranspiler.CompileTLKManifestStrings(files[0], rootElement, errorCompilingTLK); };
                                        nbw.RunWorkerCompleted += (a, b) =>
                                        {
                                            if (b.Error != null)
                                            {
                                                Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                                            }
                                            if (failedToCompileTLK) task.finishedUiText = M3L.GetString(M3L.string_compilingFailed);
                                            backgroundTaskEngine.SubmitJobCompletion(task);
                                        };
                                        nbw.RunWorkerAsync();
                                    }
                                }
                                else if (rootElement.Name == @"tlkFile") //ME3Explorer style
                                {
                                    NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"TLKTranspiler - CompileME3Exp");
                                    var task = backgroundTaskEngine.SubmitBackgroundJob(@"TranspilerCompile", M3L.GetString(M3L.string_compilingTLKFile), M3L.GetString(M3L.string_compiledTLKFile));
                                    nbw.DoWork += (a, b) => { TLKTranspiler.CompileTLKME3Explorer(files[0], rootElement, errorCompilingTLK); };
                                    nbw.RunWorkerCompleted += (a, b) =>
                                    {
                                        if (b.Error != null)
                                        {
                                            Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                                        }
                                        if (failedToCompileTLK) task.finishedUiText = M3L.GetString(M3L.string_compilingFailed);
                                        backgroundTaskEngine.SubmitJobCompletion(task);
                                    };
                                    nbw.RunWorkerAsync();
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(@"Error loading XML file that was dropped onto UI: " + ex.Message);
                                M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_errorReadingXmlFileX, ex.Message), M3L.GetString(M3L.string_errorReadingXmlFile), MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                        break;
                    case @".tlk":
                        {
                            //Break down into xml file
                            NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"TLK decompiler");
                            var task = backgroundTaskEngine.SubmitBackgroundJob(@"TLKDecompile", M3L.GetString(M3L.string_decompilingTLKFile), M3L.GetString(M3L.string_decompiledTLKFile));
                            nbw.DoWork += (a, b) =>
                            {
                                var dest = Path.Combine(Directory.GetParent(files[0]).FullName, Path.GetFileNameWithoutExtension(files[0]) + @".xml");
                                Log.Information($@"Decompiling TLK file: {files[0]} -> {dest}");
                                var tf = new TalkFile();
                                tf.LoadTlkData(files[0]);
                                tf.DumpToFile(dest);
                                Log.Information(@"Decompiled TLK file");
                            };
                            nbw.RunWorkerCompleted += (a, b) =>
                            {
                                if (b.Error != null)
                                {
                                    Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                                }
                                backgroundTaskEngine.SubmitJobCompletion(task);
                            };
                            nbw.RunWorkerAsync();

                        }
                        break;
                    case @".par":
                        {
#if DEBUG
                            var contents = PARTools.DecodePAR(File.ReadAllBytes(files[0]));
                            Debug.WriteLine(contents);
#endif
                        }
                        break;
                }
            }
        }

        private void openModImportUI(string archiveFile)
        {
            Log.Information(@"Opening Mod Archive Importer for file " + archiveFile);
            var modInspector = new ModArchiveImporter(archiveFile);
            modInspector.Close += (a, b) =>
            {

                if (b.Data is List<Mod> modsImported)
                {
                    ReleaseBusyControl();
                    LoadMods(modsImported.Count == 1 ? modsImported.FirstOrDefault() : null, true);
                }
                else if (b.Data is (Mod compressedModToInstall, bool compressed))
                {
                    ReleaseBusyControl();
                    var installTarget = InstallationTargets.FirstOrDefault(x => x.RegistryActive && x.Game == compressedModToInstall.Game);
                    if (installTarget != null)
                    {
                        SelectedGameTarget = installTarget;
                        ApplyMod(compressedModToInstall, installCompressed: compressed);
                    }
                    else
                    {
                        M3L.ShowDialog(this, M3L.GetString(M3L.string_interp_cannotInstallModGameNotInstalled, Utilities.GetGameName(compressedModToInstall.Game)), M3L.GetString(M3L.string_gameNotInstalled), MessageBoxButton.OK, MessageBoxImage.Error);
                        ReleaseBusyControl();
                    }
                }
                else
                {
                    ReleaseBusyControl();
                }
            };
            ShowBusyControl(modInspector);
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                string ext = Path.GetExtension(files[0]).ToLower();
                if (!SupportedDroppableExtensions.Contains(ext))
                {
                    if (!Settings.DeveloperMode || ext != @".xaml") //dev mode supports .xaml file drop for localization
                    {
                        e.Effects = DragDropEffects.None;
                    }
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }

            e.Handled = true;
        }

        private void RunAutoTOCOnTarget()
        {
            var target = GetCurrentTarget(MEGame.ME3);
            if (target != null)
            {
                var task = backgroundTaskEngine.SubmitBackgroundJob(@"AutoTOC", M3L.GetString(M3L.string_runningAutoTOC), M3L.GetString(M3L.string_ranAutoTOC));
                var autoTocUI = new AutoTOC(target);
                autoTocUI.Close += (a, b) =>
                {
                    backgroundTaskEngine.SubmitJobCompletion(task);
                    ReleaseBusyControl();
                };
                ShowBusyControl(autoTocUI);
            }
            else
            {
                Log.Error(@"AutoTOC game target was null! This shouldn't be possible");
            }
        }

        private void ChangeSetting_Clicked(object sender, RoutedEventArgs e)
        {
            //When this method is called, the value has already changed. So check against the opposite boolean state.
            var callingMember = (MenuItem)sender;
            if (callingMember == SetModLibraryPath_MenuItem)
            {
                ChooseModLibraryPath(true);
            }
            else if (callingMember == DarkMode_MenuItem)
            {
                SetTheme();
            }
            else if (callingMember == BetaMode_MenuItem && Settings.BetaMode)
            {
                var result = M3L.ShowDialog(this, M3L.GetString(M3L.string_dialog_optingIntoBeta), M3L.GetString(M3L.string_enablingBetaMode), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    Settings.BetaMode = false; //turn back off.
                    return;
                }
            }
            else if (callingMember == EnableTelemetry_MenuItem && !Settings.EnableTelemetry)
            {
                //user trying to turn it off 
                var result = M3L.ShowDialog(this, M3L.GetString(M3L.string_dialogTurningOffTelemetry), M3L.GetString(M3L.string_turningOffTelemetry), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.No)
                {
                    Settings.EnableTelemetry = true; //keep on.
                    return;
                }

                Log.Warning(@"Turning off telemetry :(");
                //Turn off telemetry.
                Analytics.SetEnabledAsync(false);
                Crashes.SetEnabledAsync(false);
            }
            else if (callingMember == EnableTelemetry_MenuItem)
            {
                //turning telemetry on
                Log.Information(@"Turning on telemetry :)");
                Analytics.SetEnabledAsync(true);
                Crashes.SetEnabledAsync(true);
            }
            else
            {
                //unknown caller. Might just be settings on/off for logging.
            }
        }

        internal void SetTheme()
        {
            ResourceLocator.SetColorScheme(Application.Current.Resources, Settings.DarkTheme ? ResourceLocator.DarkColorScheme : ResourceLocator.LightColorScheme);
        }

        internal bool ChooseModLibraryPath(bool loadModsAfterSelecting)
        {
            CommonOpenFileDialog m = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                EnsurePathExists = true,
                Title = M3L.GetString(M3L.string_selectModLibraryFolder)
            };
            if (m.ShowDialog(this) == CommonFileDialogResult.Ok)
            {
                Settings.ModLibraryPath = m.FileName;
                if (loadModsAfterSelecting)
                {
                    LoadMods();
                }

                return true;
            }
            else
            {
                return false;
            }
        }

        private void Documentation_Click(object sender, RoutedEventArgs e)
        {
            Utilities.OpenWebpage(@"https://github.com/ME3Tweaks/ME3TweaksModManager/tree/master/documentation");
        }

        private void OpenMemoryAnalyzer_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            new MemoryAnalyzer().Show();
#endif
        }

        private void ToggleME3Visibility_Click(object sender, RoutedEventArgs e)
        {
            ME3ModsVisible = !ME3ModsVisible;
            FilterMods();
        }

        private void ToggleME2Visibility_Click(object sender, RoutedEventArgs e)
        {
            ME2ModsVisible = !ME2ModsVisible;
            FilterMods();
        }

        private void ToggleME1Visibility_Click(object sender, RoutedEventArgs e)
        {
            ME1ModsVisible = !ME1ModsVisible;
            FilterMods();
        }

        private void FilterMods()
        {
            var allMods = AllLoadedMods.ToList();
            if (!ME1ModsVisible)
                allMods.RemoveAll(x => x.Game == MEGame.ME1);
            if (!ME2ModsVisible)
                allMods.RemoveAll(x => x.Game == MEGame.ME2);
            if (!ME3ModsVisible)
                allMods.RemoveAll(x => x.Game == MEGame.ME3);

            VisibleFilteredMods.ReplaceAll(allMods);
            VisibleFilteredMods.Sort(x => x.ModName);
        }

        private void ChangeLanguage_Clicked(object sender, RoutedEventArgs e)
        {
            string lang = @"int";
            if (sender == LanguageINT_MenuItem)
            {
                lang = @"int";
            }
            else if (sender == LanguagePOL_MenuItem)
            {
                lang = @"pol";
            }
            else if (sender == LanguageRUS_MenuItem)
            {
                lang = @"rus";
            }
            else if (sender == LanguageDEU_MenuItem)
            {
                lang = @"deu";
            }
            else if (sender == LanguageBRA_MenuItem)
            {
                lang = @"bra";
            }
            //else if (sender == LanguageESN_MenuItem)
            //{
            //    lang = @"esn";
            //}
            //else if (sender == LanguageFRA_MenuItem)
            //{
            //    lang = @"fra";
            //}
            //else if (sender == LanguageCZE_MenuItem)
            //{
            //    lang = @"cze";
            //}
            SetApplicationLanguage(lang, false);
        }

        /// <summary>
        /// Sets the UI language. This will save the settings if it is not startup.
        /// </summary>
        /// <param name="lang"></param>
        /// <param name="startup"></param>
        /// <param name="forcedDictionary"></param>
        public void SetApplicationLanguage(string lang, bool startup, ResourceDictionary forcedDictionary = null)
        {

            Log.Information(@"Setting language to " + lang);
            Application.Current.Dispatcher.Invoke(async () =>
            {

                foreach (var item in languageMenuItems)
                {
                    item.Value.IsChecked = item.Key == lang;
                }

                //Set language.
                Task.Run(async () => { await OnlineContent.InternalSetLanguage(lang, forcedDictionary, startup); }).Wait();

                App.CurrentLanguage = Settings.Language = lang;
                SetTipsForLanguage();
                RefreshNexusStatus(true);
                try
                {
                    var localizedHelpItems = OnlineContent.FetchLatestHelp(lang, true, false);
                    setDynamicHelpMenu(localizedHelpItems);
                }
                catch (Exception e)
                {
                    Log.Error(@"Could not set localized dynamic help: " + e.Message);
                }

                if (SelectedMod != null)
                {
                    // This will force strings to update
                    var sm = SelectedMod;
                    SelectedMod = null;
                    SelectedMod = sm;
                }

                if (!startup)
                {
                    if (forcedDictionary == null)
                    {
                        //Settings.Save(); //save this language option
                    }
                    await AuthToNexusMods(languageUpdateOnly: true); //this call will immediately return
                    FailedMods.RaiseBindableCountChanged();
                    CurrentOperationText = M3L.GetString(M3L.string_setLanguageToX);
                    VisitWebsiteText = (SelectedMod != null && SelectedMod.ModWebsite != Mod.DefaultWebsite) ? M3L.GetString(M3L.string_interp_visitSelectedModWebSite, SelectedMod.ModName) : "";
                }
            });
        }

        private void LoadExternalLocalizationDictionary(string filepath)
        {
            string filename = Path.GetFileNameWithoutExtension(filepath);
            string extension = Path.GetExtension(filepath);
            if (App.SupportedLanguages.Contains(filename) && extension == @".xaml" && Settings.DeveloperMode)
            {
                //Load external dictionary
                try
                {
                    var extDictionary = (ResourceDictionary)XamlReader.Load(new XmlTextReader(filepath));
                    SetApplicationLanguage(filename, false, extDictionary);
                }
                catch (Exception e)
                {
                    Log.Error(@"Error loading external localization file: " + e.Message);
                }
            }
        }

        private void ReloadSelectedMod_Click(object sender, RoutedEventArgs e)
        {
            Mod m = new Mod(SelectedMod.ModDescPath, MEGame.Unknown);
        }

        private void StampCurrentTargetWithALOT_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedGameTarget != null)
            {
                SelectedGameTarget.StampDebugALOTInfo();
                SelectedGameTarget.ReloadGameTarget();
            }
        }

        private void StripCurrentTargetALOTMarker_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedGameTarget != null)
            {
                SelectedGameTarget.StripALOTInfo();
                SelectedGameTarget.ReloadGameTarget();
            }
        }

        private void DebugPrintReferencedFiles_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMod != null)
            {
                var refed = SelectedMod.GetAllRelativeReferences();
                Debug.WriteLine(@"Referenced files:");
                foreach (var refx in refed)
                {
                    Debug.WriteLine(refx);
                }
            }
        }

        private void DebugPrintInstallationQueue_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMod != null)
            {
                var queues = SelectedMod.GetInstallationQueues(InstallationTargets.FirstOrDefault(x => x.Game == SelectedMod.Game));
                Debug.WriteLine(@"Installation Queue:");
                foreach (var job in queues.Item1)
                {
                    foreach (var file in job.Value.unpackedJobMapping)
                    {
                        Debug.WriteLine($@"[UNPACKED {job.Key.Header.ToString()}] {file.Value.FilePath} => {file.Key}");
                    }
                }

                foreach (var job in queues.Item2)
                {
                    foreach (var file in job.Item3)
                    {
                        Debug.WriteLine($@"[SFAR {job.job.Header.ToString()}] {file.Value.FilePath} => {file.Key}");
                    }
                }
            }
        }

        private void ShowBackupNag_Click(object sender, RoutedEventArgs e)
        {
            ShowBackupNag();
        }

        private void ShowBackupNag()
        {
            var nagPanel = new BackupNagSystem(
                InstallationTargets.Any(x => x.Game == MEGame.ME1),
                InstallationTargets.Any(x => x.Game == MEGame.ME2),
                InstallationTargets.Any(x => x.Game == MEGame.ME3)
            );
            nagPanel.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is bool openBackup && openBackup)
                {
                    ShowBackupPane();
                }

            };
            ShowBusyControl(nagPanel);
        }

        private void ShowWelcomePanel_Click(object sender, RoutedEventArgs e)
        {
            ShowPreviewPanel();
        }

        private void OpenME3TweaksModMaker_Click(object sender, RoutedEventArgs e)
        {
            Utilities.OpenWebpage(@"https://me3tweaks.com/modmaker");
        }

        private void Donations_Click(object sender, RoutedEventArgs e)
        {
            Utilities.OpenWebpage(@"https://me3tweaks.com/donations");
        }

        private void MEMVanillaDBViewer_Click(object sender, RoutedEventArgs e)
        {
            var previewPanel = new MEMVanillaDBViewer();
            previewPanel.Close += (a, b) =>
            {
                ReleaseBusyControl();
                if (b.Data is bool loadMods)
                {
                    LoadMods();
                }
            };
            ShowBusyControl(previewPanel);
        }

        private void ListAllInstallableFiles_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedMod != null)
            {
                var files = SelectedMod.GetAllInstallableFiles();
                ListDialog l = new ListDialog(files, M3L.GetString(M3L.string_interp_allInstallableFiles, SelectedMod.ModName), M3L.GetString(M3L.string_description_allInstallableFiles), this);
                l.Show();

            }
        }
    }
}
