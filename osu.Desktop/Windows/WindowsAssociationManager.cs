// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Localisation;

namespace osu.Desktop.Windows
{
    [SupportedOSPlatform("windows")]
    public static class WindowsAssociationManager
    {
        private const string software_classes = @"Software\Classes";
        private const string software_registered_applications = @"Software\RegisteredApplications";

        /// <summary>
        /// Sub key for setting the icon.
        /// https://learn.microsoft.com/en-us/windows/win32/com/defaulticon
        /// </summary>
        private const string default_icon = @"DefaultIcon";

        /// <summary>
        /// Sub key for setting the command line that the shell invokes.
        /// https://learn.microsoft.com/en-us/windows/win32/com/shell
        /// </summary>
        internal const string SHELL_OPEN_COMMAND = @"Shell\Open\Command";

        private static readonly string exe_path = Path.ChangeExtension(typeof(WindowsAssociationManager).Assembly.Location, ".exe").Replace('/', '\\');

        /// <summary>
        /// Program ID prefix used for file associations. Should be relatively short since the full program ID has a 39 character limit,
        /// see https://learn.microsoft.com/en-us/windows/win32/com/-progid--key.
        /// </summary>
        private const string program_id_prefix = "osu.File";

        private static readonly ApplicationCapability application_capability = new ApplicationCapability(@"osu", @"Software\ppy\osu\Capabilities", "osu!(lazer)");

        private static readonly FileAssociation[] file_associations =
        {
            new FileAssociation(@".osz", WindowsAssociationManagerStrings.OsuBeatmap, Icons.Beatmap),
            new FileAssociation(@".olz", WindowsAssociationManagerStrings.OsuBeatmap, Icons.Beatmap),
            new FileAssociation(@".osr", WindowsAssociationManagerStrings.OsuReplay, Icons.Beatmap),
            new FileAssociation(@".osk", WindowsAssociationManagerStrings.OsuSkin, Icons.Beatmap),
        };

        private static readonly UriAssociation[] uri_associations =
        {
            new UriAssociation(@"osu", WindowsAssociationManagerStrings.OsuProtocol, Icons.Lazer),
            new UriAssociation(@"osump", WindowsAssociationManagerStrings.OsuMultiplayer, Icons.Lazer),
        };

        /// <summary>
        /// Installs file and URI associations.
        /// </summary>
        /// <remarks>
        /// Call <see cref="UpdateDescriptions"/> in a timely fashion to keep descriptions up-to-date and localised.
        /// </remarks>
        public static void InstallAssociations()
        {
            try
            {
                updateAssociations();
                updateDescriptions(null); // write default descriptions in case `UpdateDescriptions()` is not called.
                NotifyShellUpdate();
            }
            catch (Exception e)
            {
                Logger.Error(e, @$"Failed to install file and URI associations: {e.Message}");
            }
        }

        /// <summary>
        /// Updates associations with latest definitions.
        /// </summary>
        /// <remarks>
        /// Call <see cref="UpdateDescriptions"/> in a timely fashion to keep descriptions up-to-date and localised.
        /// </remarks>
        public static void UpdateAssociations()
        {
            try
            {
                updateAssociations();

                // TODO: Remove once UpdateDescriptions() is called as specified in the xmldoc.
                updateDescriptions(null); // always write default descriptions, in case of updating from an older version in which file associations were not implemented/installed

                NotifyShellUpdate();
            }
            catch (Exception e)
            {
                Logger.Error(e, @"Failed to update file and URI associations.");
            }
        }

        public static void UpdateDescriptions(LocalisationManager localisationManager)
        {
            try
            {
                updateDescriptions(localisationManager);
                NotifyShellUpdate();
            }
            catch (Exception e)
            {
                Logger.Error(e, @"Failed to update file and URI association descriptions.");
            }
        }

        public static void UninstallAssociations()
        {
            try
            {
                application_capability.Uninstall();

                foreach (var association in file_associations)
                    association.Uninstall();

                foreach (var association in uri_associations)
                    association.Uninstall();

                NotifyShellUpdate();
            }
            catch (Exception e)
            {
                Logger.Error(e, @"Failed to uninstall file and URI associations.");
            }
        }

        public static void NotifyShellUpdate() => SHChangeNotify(EventId.SHCNE_ASSOCCHANGED, Flags.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

        /// <summary>
        /// Installs or updates associations.
        /// </summary>
        private static void updateAssociations()
        {
            application_capability.Install();

            foreach (var association in file_associations)
                association.Install();

            foreach (var association in uri_associations)
                association.Install();

            application_capability.RegisterFileAssociations(file_associations);
        }

        private static void updateDescriptions(LocalisationManager? localisation)
        {
            application_capability.UpdateDescription(getLocalisedString(application_capability.Description));

            foreach (var association in file_associations)
                association.UpdateDescription(getLocalisedString(association.Description));

            foreach (var association in uri_associations)
                association.UpdateDescription(getLocalisedString(association.Description));

            string getLocalisedString(LocalisableString s) => localisation?.GetLocalisedString(s) ?? s.ToString();
        }

        #region Native interop

        [DllImport("Shell32.dll")]
        private static extern void SHChangeNotify(EventId wEventId, Flags uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum EventId
        {
            /// <summary>
            /// A file type association has changed. <see cref="Flags.SHCNF_IDLIST"/> must be specified in the uFlags parameter.
            /// dwItem1 and dwItem2 are not used and must be <see cref="IntPtr.Zero"/>. This event should also be sent for registered protocols.
            /// </summary>
            SHCNE_ASSOCCHANGED = 0x08000000
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private enum Flags : uint
        {
            SHCNF_IDLIST = 0x0000
        }

        #endregion

        private record ApplicationCapability(string UniqueName, string CapabilityPath, LocalisableString Description)
        {
            /// <summary>
            /// Registers an application capability according to <see href="https://learn.microsoft.com/en-us/windows/win32/shell/default-programs#registering-an-application-for-use-with-default-programs">
            /// Registering an Application for Use with Default Programs</see>.
            /// </summary>
            public void Install()
            {
                using (Registry.CurrentUser.CreateSubKey(CapabilityPath))
                {
                    // create an empty "capability" key, other methods will fill it with information
                }

                using (var registeredApplications = Registry.CurrentUser.OpenSubKey(software_registered_applications, true))
                    registeredApplications?.SetValue(UniqueName, CapabilityPath);
            }

            public void RegisterFileAssociations(FileAssociation[] associations)
            {
                using var capability = Registry.CurrentUser.OpenSubKey(CapabilityPath, true);
                if (capability == null) return;

                using var fileAssociations = capability.CreateSubKey(@"FileAssociations");

                foreach (var association in associations)
                    fileAssociations.SetValue(association.Extension, association.ProgramId);
            }

            public void UpdateDescription(string description)
            {
                using (var capability = Registry.CurrentUser.OpenSubKey(CapabilityPath, true))
                {
                    capability?.SetValue(@"ApplicationDescription", description);
                }
            }

            public void Uninstall()
            {
                using (var registeredApplications = Registry.CurrentUser.OpenSubKey(software_registered_applications, true))
                    registeredApplications?.DeleteValue(UniqueName, throwOnMissingValue: false);

                Registry.CurrentUser.DeleteSubKeyTree(CapabilityPath, throwOnMissingSubKey: false);
            }
        }

        private record FileAssociation(string Extension, LocalisableString Description, string IconPath)
        {
            public string ProgramId => $@"{program_id_prefix}{Extension}";

            /// <summary>
            /// Installs a file extension association in accordance with https://learn.microsoft.com/en-us/windows/win32/com/-progid--key
            /// </summary>
            public void Install()
            {
                using var classes = Registry.CurrentUser.OpenSubKey(software_classes, true);
                if (classes == null) return;

                // register a program id for the given extension
                using (var programKey = classes.CreateSubKey(ProgramId))
                {
                    using (var defaultIconKey = programKey.CreateSubKey(default_icon))
                        defaultIconKey.SetValue(null, IconPath);

                    using (var openCommandKey = programKey.CreateSubKey(SHELL_OPEN_COMMAND))
                        openCommandKey.SetValue(null, $@"""{exe_path}"" ""%1""");
                }

                using (var extensionKey = classes.CreateSubKey(Extension))
                {
                    // set ourselves as the default program
                    extensionKey.SetValue(null, ProgramId);

                    // add to the open with dialog
                    // https://learn.microsoft.com/en-us/windows/win32/shell/how-to-include-an-application-on-the-open-with-dialog-box
                    using (var openWithKey = extensionKey.CreateSubKey(@"OpenWithProgIds"))
                        openWithKey.SetValue(ProgramId, string.Empty);
                }
            }

            public void UpdateDescription(string description)
            {
                using var classes = Registry.CurrentUser.OpenSubKey(software_classes, true);
                if (classes == null) return;

                using (var programKey = classes.OpenSubKey(ProgramId, true))
                    programKey?.SetValue(null, description);
            }

            /// <summary>
            /// Uninstalls the file extension association in accordance with https://learn.microsoft.com/en-us/windows/win32/shell/fa-file-types#deleting-registry-information-during-uninstallation
            /// </summary>
            public void Uninstall()
            {
                using var classes = Registry.CurrentUser.OpenSubKey(software_classes, true);
                if (classes == null) return;

                using (var extensionKey = classes.OpenSubKey(Extension, true))
                {
                    // clear our default association so that Explorer doesn't show the raw ProgramId to users
                    // the null/(Default) entry is used for both ProdID association and as a fallback friendly name, for legacy reasons
                    if (extensionKey?.GetValue(null) is string s && s == ProgramId)
                        extensionKey.SetValue(null, string.Empty);

                    using (var openWithKey = extensionKey?.CreateSubKey(@"OpenWithProgIds"))
                        openWithKey?.DeleteValue(ProgramId, throwOnMissingValue: false);
                }

                classes.DeleteSubKeyTree(ProgramId, throwOnMissingSubKey: false);
            }
        }

        private record UriAssociation(string Protocol, LocalisableString Description, string IconPath)
        {
            /// <summary>
            /// "The <c>URL Protocol</c> string value indicates that this key declares a custom pluggable protocol handler."
            /// See https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/aa767914(v=vs.85).
            /// </summary>
            public const string URL_PROTOCOL = @"URL Protocol";

            /// <summary>
            /// Registers an URI protocol handler in accordance with https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/platform-apis/aa767914(v=vs.85).
            /// </summary>
            public void Install()
            {
                using var classes = Registry.CurrentUser.OpenSubKey(software_classes, true);
                if (classes == null) return;

                using (var protocolKey = classes.CreateSubKey(Protocol))
                {
                    protocolKey.SetValue(URL_PROTOCOL, string.Empty);

                    using (var defaultIconKey = protocolKey.CreateSubKey(default_icon))
                        defaultIconKey.SetValue(null, IconPath);

                    using (var openCommandKey = protocolKey.CreateSubKey(SHELL_OPEN_COMMAND))
                        openCommandKey.SetValue(null, $@"""{exe_path}"" ""%1""");
                }
            }

            public void UpdateDescription(string description)
            {
                using var classes = Registry.CurrentUser.OpenSubKey(software_classes, true);
                if (classes == null) return;

                using (var protocolKey = classes.OpenSubKey(Protocol, true))
                    protocolKey?.SetValue(null, $@"URL:{description}");
            }

            public void Uninstall()
            {
                using var classes = Registry.CurrentUser.OpenSubKey(software_classes, true);
                classes?.DeleteSubKeyTree(Protocol, throwOnMissingSubKey: false);
            }
        }
    }
}
