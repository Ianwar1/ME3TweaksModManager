﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MassEffectModManagerCore.modmanager.helpers;
using MassEffectModManagerCore.modmanager.nexusmodsintegration;
using ME3ExplorerCore.Packages;
using Microsoft.AppCenter.Analytics;
using Pathoschild.FluentNexus.Models;
using Serilog;

namespace MassEffectModManagerCore.modmanager.objects.mod
{
    public partial class Mod
    {
        private bool checkedEndorsementStatus;
        public bool IsEndorsed { get; set; }
        public bool IsOwnMod { get; set; }
        public bool CanEndorse { get; set; }
        //public string EndorsementStatus { get; set; } = "Endorse mod";

        public async Task<bool?> GetEndorsementStatus(int currentuserid)
        {
            if (!NexusModsUtilities.HasAPIKey) return false;
            if (checkedEndorsementStatus) return IsEndorsed;
            try
            {
                var client = NexusModsUtilities.GetClient();
                string gamename = @"masseffect";
                if (Game == MEGame.ME2) gamename += @"2";
                if (Game == MEGame.ME3) gamename += @"3";
                var modinfo = await client.Mods.GetMod(gamename, NexusModID);
                if (modinfo.User.MemberID == currentuserid)
                {
                    IsEndorsed = false;
                    CanEndorse = false;
                    IsOwnMod = true;
                    checkedEndorsementStatus = true;
                    return null; //cannot endorse your own mods
                }
                var endorsementstatus = modinfo.Endorsement;
                if (endorsementstatus != null)
                {
                    if (endorsementstatus.EndorseStatus == Pathoschild.FluentNexus.Models.EndorsementStatus.Undecided || endorsementstatus.EndorseStatus == Pathoschild.FluentNexus.Models.EndorsementStatus.Abstained)
                    {
                        IsEndorsed = false;
                    }
                    else if (endorsementstatus.EndorseStatus == Pathoschild.FluentNexus.Models.EndorsementStatus.Endorsed)
                    {
                        IsEndorsed = true;
                    }

                    CanEndorse = true;
                }
                else
                {
                    IsEndorsed = false;
                    CanEndorse = false;
                }
                checkedEndorsementStatus = true;
                return IsEndorsed;
            }
            catch (Exception e)
            {
                Log.Error(@"Error getting endorsement status: " + e.Message);
                return false; //null would mean own mod. so just say its not endorsed atm.
            }
        }

        /// <summary>
        /// Attempts to endorse/unendorse this mod on NexusMods.
        /// </summary>
        /// <param name="newEndorsementStatus"></param>
        /// <param name="endorse"></param>
        /// <param name="currentuserid"></param>
        public void EndorseMod(Action<Mod, bool> newEndorsementStatus, bool endorse, int currentuserid)
        {
            if (!NexusModsUtilities.HasAPIKey || !CanEndorse) return;
            NamedBackgroundWorker nbw = new NamedBackgroundWorker(@"ModSpecificEndorsement");
            nbw.DoWork += (a, b) =>
            {
                var client = NexusModsUtilities.GetClient();
                string gamename = @"masseffect";
                if (Game == MEGame.ME2) gamename += @"2";
                if (Game == MEGame.ME3) gamename += @"3";
                string telemetryOverride = null;
                try
                {
                    if (endorse)
                    {
                        client.Mods.Endorse(gamename, NexusModID, @"1.0").Wait();
                    }
                    else
                    {
                        client.Mods.Unendorse(gamename, NexusModID, @"1.0").Wait();
                    }
                }
                catch (Exception e)
                {
                    Log.Error(@"Error endorsing/unendorsing: " + e.ToString());
                    telemetryOverride = e.ToString();
                }

                checkedEndorsementStatus = false;
                IsEndorsed = GetEndorsementStatus(currentuserid).Result ?? false;
                Analytics.TrackEvent(@"Set endorsement for mod", new Dictionary<string, string>
                {
                    {@"Endorsed", endorse.ToString() },
                    {@"Succeeded", telemetryOverride ?? (endorse == IsEndorsed).ToString() }
                });

            };
            nbw.RunWorkerCompleted += (a, b) => {
                if (b.Error != null)
                {
                    Log.Error($@"Exception occurred in {nbw.Name} thread: {b.Error.Message}");
                }
                newEndorsementStatus.Invoke(this, IsEndorsed); };
            nbw.RunWorkerAsync();
        }
    }
}
