﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Data.Json;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using WinUIEx;

namespace RegistryPreview
{
    public sealed partial class MainWindow : WindowEx
    {
        /// <summary>
        /// Event handler to grab the main window's size and position before it closes
        /// </summary>
        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
        {
            jsonSettings.SetNamedValue("appWindow.Position.X", JsonValue.CreateNumberValue(appWindow.Position.X));
            jsonSettings.SetNamedValue("appWindow.Position.Y", JsonValue.CreateNumberValue(appWindow.Position.Y));
            jsonSettings.SetNamedValue("appWindow.Size.Width", JsonValue.CreateNumberValue(appWindow.Size.Width));
            jsonSettings.SetNamedValue("appWindow.Size.Height", JsonValue.CreateNumberValue(appWindow.Size.Height));
        }

        /// <summary>
        /// Event that is will prevent the app from closing if the "save file" flag is active
        /// </summary>
        public void Window_Closed(object sender, WindowEventArgs args)
        {
            // Only block closing if the REG file has been edited but not yet saved
            if (saveButton.IsEnabled)
            {
                // if true, the app will not close
                args.Handled = true;

                // ask the user if they want to save, discard or cancel the close; strings must be loaded here and passed to avoid timing issues
                HandleDirtyClosing(
                    resourceLoader.GetString("YesNoCancelDialogTitle"),
                    resourceLoader.GetString("YesNoCancelDialogContent"),
                    resourceLoader.GetString("YesNoCancelDialogPrimaryButtonText"),
                    resourceLoader.GetString("YesNoCancelDialogSecondaryButtonText"),
                    resourceLoader.GetString("YesNoCancelDialogCloseButtonText"));
            }

            // Save app settings
            jsonSettings.SetNamedValue("checkBoxTextBox.Checked", JsonValue.CreateBooleanValue(checkBoxTextBox.IsChecked.Value));
            SaveSettingsFile(settingsFolder, settingsFile);
        }

        /// <summary>
        /// Event that gets fired after the visual tree has been fully loaded; the app opens the reg file from here so it can show a message box successfully
        /// </summary>
        private void GridPreview_Loaded(object sender, RoutedEventArgs e)
        {
            // static flag to track whether the Visual Tree is ready - if the main Grid has been loaded, the tree is ready.
            visualTreeReady = true;

            // Load and restore app settings
            if (jsonSettings.ContainsKey("checkBoxTextBox.Checked"))
            {
                checkBoxTextBox.IsChecked = jsonSettings.GetNamedBoolean("checkBoxTextBox.Checked");
            }

            // Check to see if the REG file was opened and parsed successfully
            if (OpenRegistryFile(App.AppFilename) == false)
            {
                if (File.Exists(App.AppFilename))
                {
                    // Allow Refresh and Edit to be enabled because a broken Reg file might be fixable
                    UpdateToolBarAndUI(false, true, true);
                    UpdateWindowTitle(resourceLoader.GetString("InvalidRegistryFileTitle"));
                    textBox.TextChanged += TextBox_TextChanged;
                    return;
                }
                else
                {
                    UpdateToolBarAndUI(false, false, false);
                    UpdateWindowTitle();
                }
            }
            else
            {
                textBox.TextChanged += TextBox_TextChanged;
            }

            textBox.Focus(FocusState.Programmatic);
        }

        /// <summary>
        /// Uses a picker to select a new file to open
        /// </summary>
        private async void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            // Check to see if the current file has been saved
            if (saveButton.IsEnabled)
            {
                ContentDialog contentDialog = new ContentDialog()
                {
                    Title = resourceLoader.GetString("YesNoCancelDialogTitle"),
                    Content = resourceLoader.GetString("YesNoCancelDialogContent"),
                    PrimaryButtonText = resourceLoader.GetString("YesNoCancelDialogPrimaryButtonText"),
                    SecondaryButtonText = resourceLoader.GetString("YesNoCancelDialogSecondaryButtonText"),
                    CloseButtonText = resourceLoader.GetString("YesNoCancelDialogCloseButtonText"),
                    DefaultButton = ContentDialogButton.Primary,
                };

                // Use this code to associate the dialog to the appropriate AppWindow by setting
                // the dialog's XamlRoot to the same XamlRoot as an element that is already present in the AppWindow.
                if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
                {
                    contentDialog.XamlRoot = this.Content.XamlRoot;
                }

                ContentDialogResult contentDialogResult = await contentDialog.ShowAsync();
                switch (contentDialogResult)
                {
                    case ContentDialogResult.Primary:
                        // Save, then continue the file open
                        SaveFile();
                        break;
                    case ContentDialogResult.Secondary:
                        // Don't save and continue the file open!
                        saveButton.IsEnabled = false;
                        break;
                    default:
                        // Don't open the new file!
                        return;
                }
            }

            // Pull in a new REG file
            FileOpenPicker fileOpenPicker = new FileOpenPicker();
            fileOpenPicker.ViewMode = PickerViewMode.List;
            fileOpenPicker.CommitButtonText = resourceLoader.GetString("OpenButtonText");
            fileOpenPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            fileOpenPicker.FileTypeFilter.Add(".reg");

            // Get the HWND so we an open the modal
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(fileOpenPicker, hWnd);

            StorageFile storageFile = await fileOpenPicker.PickSingleFileAsync();

            if (storageFile != null)
            {
                // mute the TextChanged handler to make for clean UI
                textBox.TextChanged -= TextBox_TextChanged;

                App.AppFilename = storageFile.Path;
                UpdateToolBarAndUI(OpenRegistryFile(App.AppFilename));

                // disable the Save button as it's a new file
                saveButton.IsEnabled = false;

                // Restore the event handler as we're loaded
                textBox.TextChanged += TextBox_TextChanged;
            }
        }

        /// <summary>
        /// Saves the currently opened file in place
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFile();
        }

        /// <summary>
        /// Uses a picker to save out a copy of the current reg file
        /// </summary>
        private async void SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            // Save out a new REG file and then open it
            FileSavePicker fileSavePicker = new FileSavePicker();
            fileSavePicker.CommitButtonText = resourceLoader.GetString("SaveButtonText");
            fileSavePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            fileSavePicker.FileTypeChoices.Add("Registry file", new List<string>() { ".reg" });
            fileSavePicker.SuggestedFileName = resourceLoader.GetString("SuggestFileName");

            // Get the HWND so we an save the modal
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            InitializeWithWindow.Initialize(fileSavePicker, hWnd);

            StorageFile storageFile = await fileSavePicker.PickSaveFileAsync();

            if (storageFile != null)
            {
                App.AppFilename = storageFile.Path;
                SaveFile();
                UpdateToolBarAndUI(OpenRegistryFile(App.AppFilename));
            }
        }

        /// <summary>
        /// Reloads the current REG file from storage
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // mute the TextChanged handler to make for clean UI
            textBox.TextChanged -= TextBox_TextChanged;

            // reload the current Registry file and update the toolbar accordingly.
            UpdateToolBarAndUI(OpenRegistryFile(App.AppFilename), true, true);

            saveButton.IsEnabled = false;

            // restore the TextChanged handler
            textBox.TextChanged += TextBox_TextChanged;
        }

        /// <summary>
        /// Opens the Registry Editor; UAC is handled by the request to open
        /// </summary>
        private void RegistryButton_Click(object sender, RoutedEventArgs e)
        {
            // pass in an empty string as we have no file to open
            OpenRegistryEditor(string.Empty);
        }

        /// <summary>
        /// Merges the currently saved file into the Registry Editor; UAC is handled by the request to open
        /// </summary>
        private async void WriteButton_Click(object sender, RoutedEventArgs e)
        {
            // Check to see if the current file has been saved
            if (saveButton.IsEnabled)
            {
                ContentDialog contentDialog = new ContentDialog()
                {
                    Title = resourceLoader.GetString("YesNoCancelDialogTitle"),
                    Content = resourceLoader.GetString("YesNoCancelDialogContent"),
                    PrimaryButtonText = resourceLoader.GetString("YesNoCancelDialogPrimaryButtonText"),
                    SecondaryButtonText = resourceLoader.GetString("YesNoCancelDialogSecondaryButtonText"),
                    CloseButtonText = resourceLoader.GetString("YesNoCancelDialogCloseButtonText"),
                    DefaultButton = ContentDialogButton.Primary,
                };

                // Use this code to associate the dialog to the appropriate AppWindow by setting
                // the dialog's XamlRoot to the same XamlRoot as an element that is already present in the AppWindow.
                if (ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 8))
                {
                    contentDialog.XamlRoot = this.Content.XamlRoot;
                }

                ContentDialogResult contentDialogResult = await contentDialog.ShowAsync();
                switch (contentDialogResult)
                {
                    case ContentDialogResult.Primary:
                        // Save, then continue the file open
                        SaveFile();
                        break;
                    case ContentDialogResult.Secondary:
                        // Don't save and continue the file open!
                        saveButton.IsEnabled = false;
                        break;
                    default:
                        // Don't open the new file!
                        return;
                }
            }

            // pass in the filename so we can edit the current file
            OpenRegistryEditor(App.AppFilename);
        }

        /// <summary>
        /// Opens the currently saved file in the PC's default REG file editor (often Notepad)
        /// </summary>
        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            // use the REG file's filename and verb so we can respect the selected editor
            Process process = new Process();
            process.StartInfo.FileName = string.Format(CultureInfo.InvariantCulture, "\"{0}\"", App.AppFilename);
            process.StartInfo.Verb = "Edit";
            process.StartInfo.UseShellExecute = true;

            try
            {
                process.Start();
            }
            catch
            {
                ShowMessageBox(
                    resourceLoader.GetString("ErrorDialogTitle"),
                    resourceLoader.GetString("FileEditorError"),
                    resourceLoader.GetString("OkButtonText"));
            }
        }

        /// <summary>
        /// Trigger that fires when a node in treeView is clicked and which populates dataGrid
        /// Can also be fired from elsewhere in the code
        /// </summary>
        private void TreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            TreeViewItemInvokedEventArgs localArgs = args as TreeViewItemInvokedEventArgs;
            TreeViewNode treeViewNode = null;

            // if there are no args, the mouse didn't get clicked but we want to believe it did
            if (args != null)
            {
                treeViewNode = args.InvokedItem as TreeViewNode;
            }
            else
            {
                treeViewNode = treeView.SelectedNode;
            }

            // Grab the object that has Registry data in it from the currently selected treeView node
            RegistryKey registryKey = (RegistryKey)treeViewNode.Content;

            // no matter what happens, clear the ListView of items on each click
            ClearTable();

            // if there's no ListView items stored for the selected node, dataGrid is clear so get out now
            if (registryKey.Tag == null)
            {
                return;
            }

            // if there WAS something in the Tag property, cast it to a list and Populate the ListView
            ArrayList arrayList = (ArrayList)registryKey.Tag;
            listRegistryValues = new List<RegistryValue>();

            for (int i = 0; i < arrayList.Count; i++)
            {
                RegistryValue listViewItem = (RegistryValue)arrayList[i];
                listRegistryValues.Add(listViewItem);
            }

            // create a new binding for dataGrid and reattach it, updating the rows
            Binding listRegistryValuesBinding = new Binding { Source = listRegistryValues };
            dataGrid.SetBinding(DataGrid.ItemsSourceProperty, listRegistryValuesBinding);
        }

        /// <summary>
        /// When the text in textBox changes, reload treeView and possibly dataGrid and reset the save button
        /// </summary>
        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshRegistryFile();
            saveButton.IsEnabled = true;
        }

        /// <summary>
        /// Readonly checkbox is checked, set textBox to read only; also update the font color so it has a hint of being "disabled" (also the hover state!)
        /// </summary>
        private void CheckBoxTextBox_Checked(object sender, RoutedEventArgs e)
        {
            textBox.IsReadOnly = true;
            SolidColorBrush brush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 120, 120, 120)); // (SolidColorBrush)Application.Current.Resources["TextBoxDisabledForegroundThemeBrush"];
            if (brush != null)
            {
                textBox.Foreground = brush;
                textBox.Resources["TextControlForegroundPointerOver"] = brush;
            }
        }

        /// <summary>
        /// Readonly checkbox is unchecked, set textBox to be editable; also update the font color back to a theme friendly foreground (also the hover state!)
        /// </summary>
        private void CheckBoxTextBox_Unchecked(object sender, RoutedEventArgs e)
        {
            textBox.IsReadOnly = false;
            SolidColorBrush brush = (SolidColorBrush)Application.Current.Resources["TextControlForeground"];
            if (brush != null)
            {
                textBox.Foreground = brush;
                textBox.Resources["TextControlForegroundPointerOver"] = brush;
            }
        }
    }
}
