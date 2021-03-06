﻿using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FontAwesome.WPF;
using MassEffectModManagerCore.modmanager.helpers;
using MassEffectModManagerCore.modmanager.localizations;
using MassEffectModManagerCore.modmanager.nexusmodsintegration;
using MassEffectModManagerCore.ui;
using Microsoft.AppCenter.Analytics;
using Pathoschild.Http.Client;
using Serilog;

namespace MassEffectModManagerCore.modmanager.usercontrols
{
    /// <summary>
    /// Interaction logic for NexusModsLogin.xaml
    /// </summary>
    public partial class NexusModsLogin : MMBusyPanelBase
    {
        public string APIKeyText { get; set; }
        public bool IsAuthorized { get; set; }
        public string AuthorizeToNexusText { get; set; }
        public NexusModsLogin()
        {
            DataContext = this;
            LoadCommands();
            InitializeComponent();
        }

        private void SetAuthorized(bool authorized)
        {
            IsAuthorized = authorized;
            string authenticatedString = M3L.GetString(M3L.string_authenticateToNexusMods);
            if (authorized && mainwindow.NexusUsername != null)
            {
                authenticatedString = M3L.GetString(M3L.string_interp_authenticatedAsX, mainwindow.NexusUsername);
            }
            VisibleIcon = authorized;
            if (authorized)
            {
                ActiveIcon = FontAwesomeIcon.CheckCircle;
            }
            AuthorizeToNexusText = authenticatedString;
        }

        public GenericCommand AuthorizeCommand { get; set; }
        public GenericCommand UnlinkCommand { get; set; }
        public GenericCommand CloseCommand { get; set; }
        public bool IsAuthorizing { get; private set; }

        private void LoadCommands()
        {
            AuthorizeCommand = new GenericCommand(AuthorizeWithNexus, CanAuthorizeWithNexus);
            UnlinkCommand = new GenericCommand(UnlinkFromNexus, CanUnlinkWithNexus);
            CloseCommand = new GenericCommand(ClosePanel, CanClose);
        }

        private bool CanClose() => !IsAuthorizing;

        private void ClosePanel()
        {
            OnClosing(DataEventArgs.Empty);
        }

        private bool CanAuthorizeWithNexus() => !IsAuthorized && !IsAuthorizing && (!ManualMode || !string.IsNullOrWhiteSpace(APIKeyText));

        public bool ManualMode { get; set; }
        public string WatermarkText { get; set; } = M3L.GetString(M3L.string_yourAPIKeyWillAppearHere);
        public FontAwesomeIcon ActiveIcon { get; set; }
        public bool SpinIcon { get; set; }
        public bool VisibleIcon { get; set; }
        public void OnIsAuthorizedChanged() => VisibleIcon = IsAuthorized;

        public void OnManualModeChanged()
        {
            WatermarkText = ManualMode ? M3L.GetString(M3L.string_pasteYourAPIKeyHere) : M3L.GetString(M3L.string_yourAPIKeyWillAppearHere);
        }

        private void AuthorizeWithNexus()
        {
            NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"NexusAPICredentialsCheck");
            nbw.DoWork += async (a, b) =>
            {
                IsAuthorizing = true;
                VisibleIcon = true;
                SpinIcon = true;
                ActiveIcon = FontAwesomeIcon.Spinner;
                AuthorizeCommand.RaiseCanExecuteChanged();
                CloseCommand.RaiseCanExecuteChanged();
                AuthorizeToNexusText = M3L.GetString(M3L.string_pleaseWait);
                if (!ManualMode)
                {

                    var apiKeyReceived = await NexusModsUtilities.SetupNexusLogin(x => Debug.WriteLine(x));
                    APIKeyText = apiKeyReceived;
                    Application.Current.Dispatcher.Invoke(delegate { mainwindow.Activate(); });
                }

                if (!string.IsNullOrWhiteSpace(APIKeyText))
                {
                    //Check api key
                    AuthorizeToNexusText = M3L.GetString(M3L.string_checkingKey);
                    try
                    {
                        var authInfo = NexusModsUtilities.AuthToNexusMods(APIKeyText).Result;
                        if (authInfo != null)
                        {
                            using FileStream fs = new FileStream(Path.Combine(Utilities.GetNexusModsCache(), @"nexusmodsapikey"), FileMode.Create);
                            File.WriteAllBytes(Path.Combine(Utilities.GetNexusModsCache(), @"entropy"), NexusModsUtilities.EncryptStringToStream(APIKeyText, fs));
                            fs.Close();
                            mainwindow.NexusUsername = authInfo.Name;
                            mainwindow.NexusUserID = authInfo.UserID;
                            SetAuthorized(true);
                            mainwindow.RefreshNexusStatus();
                            Analytics.TrackEvent(@"Authenticated to NexusMods");
                        }
                        else
                        {
                            Log.Error(@"Error authenticating to nexusmods, no userinfo was returned, possible network issue");
                            mainwindow.NexusUsername = null;
                            mainwindow.NexusUserID = 0;
                            SetAuthorized(false);
                            mainwindow.RefreshNexusStatus();
                        }
                    }
                    catch (ApiException apiException)
                    {
                        Log.Error(@"Error authenticating to NexusMods: " + apiException.ToString());
                        Application.Current.Dispatcher.Invoke(delegate { M3L.ShowDialog(window, M3L.GetString(M3L.string_interp_nexusModsReturnedAnErrorX, apiException.ToString()), M3L.GetString(M3L.string_errorAuthenticatingToNexusMods), MessageBoxButton.OK, MessageBoxImage.Error); });
                    }
                    catch (Exception e)
                    {
                        Log.Error(@"Other error authenticating to NexusMods: " + e.Message);
                    }
                }
                else
                {
                    Log.Error(@"No API key - setting authorized to false for NM");
                    SetAuthorized(false);
                }

                IsAuthorizing = false;
            };
            nbw.RunWorkerCompleted += (a, b) =>
            {
                if (b.Error != null)
                {
                    Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                }
                VisibleIcon = IsAuthorized;
                if (IsAuthorized)
                {
                    ActiveIcon = FontAwesomeIcon.CheckCircle;
                }
                SpinIcon = false;
                AuthorizeCommand.RaiseCanExecuteChanged();
                CloseCommand.RaiseCanExecuteChanged();
            };
            nbw.RunWorkerAsync();
        }

        private void UnlinkFromNexus()
        {
            APIKeyText = "";
            NexusModsUtilities.WipeKeys();
            mainwindow.NexusUsername = null;
            mainwindow.NexusUserID = 0;
            SetAuthorized(false);
            mainwindow.RefreshNexusStatus();
        }

        private bool CanUnlinkWithNexus() => IsAuthorized;

        public override void HandleKeyPress(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && CanClose())
            {
                e.Handled = true;
                OnClosing(DataEventArgs.Empty);
            }
        }

        public override void OnPanelVisible()
        {
            try
            {
                string currentKey = NexusModsUtilities.DecryptNexusmodsAPIKeyFromDisk();
                if (currentKey != null)
                {
                    APIKeyText = currentKey;
                    SetAuthorized(true);
                }
                else
                {
                    SetAuthorized(false);
                }
            }
            catch (Exception e)
            {
                Log.Error(@"Error getting current API Key: " + e.Message);
                SetAuthorized(false);
            }
        }
    }
}
