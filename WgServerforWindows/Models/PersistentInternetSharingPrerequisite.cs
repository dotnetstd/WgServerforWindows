using System;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Management;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using WgServerforWindows.Cli.Options;
using WgServerforWindows.Properties;
using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public class PersistentInternetSharingPrerequisite : PrerequisiteItem
    {
        #region PrerequisiteItem members

        private readonly INetworkService _networkService;

        public PersistentInternetSharingPrerequisite(INetworkService networkService) : base
        (
            title: Resources.PersistentInternetSharingTitle,
            successMessage: Resources.PersistentInternetSharingSucecss,
            errorMessage: Resources.PersistentInternetSharingError,
            resolveText: Resources.PersistentInternetSharingResolve,
            configureText: Resources.PersistentInternetSharingDisable
        )
        {
            _networkService = networkService;
        }

        public override BooleanTimeCachedProperty Fulfilled => _fulfilled ??= new BooleanTimeCachedProperty(TimeSpan.FromSeconds(1), () =>
        {
            return _networkService.IsPersistentIcsEnabled();
        });
        private BooleanTimeCachedProperty _fulfilled;

        public override void Resolve()
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            _networkService.SetPersistentIcs(true);

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        public override void Configure()
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            _networkService.SetPersistentIcs(false);

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        public override string Category => Resources.InternetConnectionSharing;

        #endregion
    }
}
