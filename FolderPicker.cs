using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using OpenFolderDialog = Microsoft.Win32.OpenFolderDialog;

namespace CyberpunkSlideshowWidget
{
    public static class FolderPicker
    {
        private const int RecurseCheckboxControlId = 1001;

        public static (bool Success, string? SelectedPath, bool RecurseSubdirectories) Show(
            Window? owner,
            string? initialFolder,
            bool initialRecurse)
        {
            // Attempt to use the native Windows Common Item Dialog with IFileDialogCustomize
            try
            {
                var dialog = (IFileOpenDialog)new FileOpenDialogRCW();
                try
                {
                    // Set folder picking mode
                    dialog.GetOptions(out uint options);
                    options |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
                    dialog.SetOptions(options);
                    dialog.SetTitle("Select a folder containing images for the slideshow");

                    // Set initial folder if provided and valid
                    if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                    {
                        if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, typeof(IShellItem).GUID, out IShellItem initialFolderItem) == 0 && initialFolderItem != null)
                        {
                            try
                            {
                                dialog.SetFolder(initialFolderItem);
                            }
                            finally
                            {
                                Marshal.FinalReleaseComObject(initialFolderItem);
                            }
                        }
                    }

                    // Add "Recurse subdirectories" check button using IFileDialogCustomize
                    if (dialog is IFileDialogCustomize customize)
                    {
                        customize.AddCheckButton(RecurseCheckboxControlId, "Recurse subdirectories", initialRecurse);
                    }

                    IntPtr hwndOwner = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                    int hr = dialog.Show(hwndOwner);

                    // User clicked "Select Folder" (S_OK / 0)
                    if (hr == 0)
                    {
                        bool recurseResult = initialRecurse;
                        if (dialog is IFileDialogCustomize custom)
                        {
                            try
                            {
                                custom.GetCheckButtonState(RecurseCheckboxControlId, out recurseResult);
                            }
                            catch { }
                        }

                        dialog.GetResult(out IShellItem shellItem);
                        if (shellItem != null)
                        {
                            string path;
                            try
                            {
                                shellItem.GetDisplayName(SIGDN_FILESYSPATH, out path);
                            }
                            finally
                            {
                                Marshal.FinalReleaseComObject(shellItem);
                            }

                            return (true, path, recurseResult);
                        }
                    }

                    // User cancelled
                    return (false, null, initialRecurse);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(dialog);
                }
            }
            catch
            {
                // Fallback to standard WPF OpenFolderDialog if COM dialog customization fails
                var fallbackDialog = new OpenFolderDialog
                {
                    Title = "Select a folder containing images for the slideshow",
                    Multiselect = false
                };

                if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
                {
                    fallbackDialog.InitialDirectory = initialFolder;
                }

                if (owner != null && fallbackDialog.ShowDialog(owner) == true && !string.IsNullOrWhiteSpace(fallbackDialog.FolderName))
                {
                    return (true, fallbackDialog.FolderName, initialRecurse);
                }

                return (false, null, initialRecurse);
            }
        }

        #region Native COM Definitions

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        [ComImport]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        [ClassInterface(ClassInterfaceType.None)]
        private class FileOpenDialogRCW { }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a57e2d55b364")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport]
        [Guid("d57c72ba-1421-490b-861d-451357c197e4")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, uint fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport]
        [Guid("e6fdd21a-163f-4975-9c8c-a69f1ba37034")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialogCustomize
        {
            void EnableOpenDropDown(int dwIDCtl);
            void AddMenu(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void AddPushButton(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void AddComboBox(int dwIDCtl);
            void AddRadioButtonList(int dwIDCtl);
            void AddCheckButton(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel, [MarshalAs(UnmanagedType.Bool)] bool bChecked);
            void AddEditBox(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void AddSeparator(int dwIDCtl);
            void AddText(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetControlLabel(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetControlState(int dwIDCtl, out int pdwState);
            void SetControlState(int dwIDCtl, int dwState);
            void GetEditBoxText(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] out string ppszText);
            void SetEditBoxText(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void GetCheckButtonState(int dwIDCtl, [MarshalAs(UnmanagedType.Bool)] out bool pbChecked);
            void SetCheckButtonState(int dwIDCtl, [MarshalAs(UnmanagedType.Bool)] bool bChecked);
            void AddControlItem(int dwIDCtl, int dwIDItem, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void CheckControlItem(int dwIDCtl, int dwIDItem, [MarshalAs(UnmanagedType.Bool)] bool bChecked);
            void GetControlItemState(int dwIDCtl, int dwIDItem, out int pdwState);
            void SetControlItemState(int dwIDCtl, int dwIDItem, int dwState);
            void GetSelectedControlItem(int dwIDCtl, out int pdwIDItem);
            void SetSelectedControlItem(int dwIDCtl, int dwIDItem);
            void StartVisualGroup(int dwIDCtl, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void EndVisualGroup();
            void MakeProminent(int dwIDCtl);
            void SetControlItemText(int dwIDCtl, int dwIDItem, [MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
        }

        #endregion
    }
}

