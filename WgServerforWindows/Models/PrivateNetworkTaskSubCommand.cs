using System;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32.TaskScheduler;
using WgServerforWindows.Cli.Options;
using WgServerforWindows.Properties;

using WgServerforWindows.Services.Interfaces;

namespace WgServerforWindows.Models
{
    public class PrivateNetworkTaskSubCommand : PrerequisiteItem
    {
        private readonly INetworkService _networkService;

        public PrivateNetworkTaskSubCommand(INetworkService networkService) : base
        (
            string.Empty,
            successMessage: Resources.PrivateNetworkTaskSubCommandSuccessMessage,
            errorMessage: Resources.PrivateNetworkTaskSubCommandErrorMessage,
            resolveText: Resources.PrivateNetworkTaskSubCommandResolveText,
            configureText: Resources.PrivateNetworkTaskSubCommandConfigureText
        )
        {
            _networkService = networkService;
        }

        #region PrerequisiteItem members

        /// <inheritdoc/>
        public override BooleanTimeCachedProperty Fulfilled => _fulfilled ??= new BooleanTimeCachedProperty(TimeSpan.FromSeconds(1), () =>
            _networkService.IsTaskEnabled(_privateNetworkTaskUniqueName, Path.Combine(AppContext.BaseDirectory, "ws4w.exe"), typeof(PrivateNetworkCommand).GetVerb()));
        private BooleanTimeCachedProperty _fulfilled;

        /// <inheritdoc/>
        public override void Resolve()
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            _networkService.CreateBootTask(_privateNetworkTaskUniqueName, Path.Combine(AppContext.BaseDirectory, "ws4w.exe"), typeof(PrivateNetworkCommand).GetVerb(), GlobalAppSettings.Instance.BootTaskDelay);

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        public override void Configure()
        {
            WaitCursor.SetOverrideCursor(Cursors.Wait);

            _networkService.DisableTask(_privateNetworkTaskUniqueName);

            Refresh();

            WaitCursor.SetOverrideCursor(null);
        }

        #endregion

        #region Private fields

        private readonly string _privateNetworkTaskUniqueName = "WS4W Private Network (bc87228e-afdb-4815-8786-b5934bcf53e6)";

        #endregion
    }
}
