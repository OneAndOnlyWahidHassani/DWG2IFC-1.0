using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Autodesk.Windows;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Plant3D.IfcExporter.UI
{
    public class RelayCommand : ICommand
    {
        private readonly Action _action;
        public RelayCommand(Action action) => _action = action;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _action();
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }

    public static class RibbonManager
    {
        private const string TabId = "ID_DWG2IFC_TAB";
        private const string TabTitle = "DWG2IFC";

        public static void Initialize()
        {
            try
            {
                AcApp.Idle += OnApplicationIdle;
                AcApp.DocumentManager.DocumentActivated += OnDocumentActivated;
                AcApp.DocumentManager.DocumentCreated += OnDocumentCreated;

                if (ComponentManager.Ribbon != null)
                {
                    EnsureRibbonTab();
                }
                else
                {
                    ComponentManager.ItemInitialized += ComponentManager_ItemInitialized;
                }
            }
            catch (Exception ex)
            {
                var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[Plant3D IFC Exporter] Failed to create ribbon: {ex.Message}\n");
            }
        }

        private static void OnApplicationIdle(object? sender, EventArgs e)
        {
            var ribbon = ComponentManager.Ribbon;
            if (ribbon != null)
            {
                EnsureRibbonTab();
                var existingTab = ribbon.Tabs.FirstOrDefault(t => t.Id == TabId);
                if (existingTab != null && existingTab.Panels.Count > 0)
                {
                    AcApp.Idle -= OnApplicationIdle;
                }
            }
        }

        private static void OnDocumentActivated(object? sender, Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs e)
        {
            EnsureRibbonTab();
        }

        private static void OnDocumentCreated(object? sender, Autodesk.AutoCAD.ApplicationServices.DocumentCollectionEventArgs e)
        {
            EnsureRibbonTab();
        }

        private static void ComponentManager_ItemInitialized(object? sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon != null)
            {
                ComponentManager.ItemInitialized -= ComponentManager_ItemInitialized;
                EnsureRibbonTab();
            }
        }

        public static void EnsureRibbonTab(bool activate = false)
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return;

                void Execute()
                {
                    try
                    {
                        var existingTab = ribbon.Tabs.FirstOrDefault(t => t.Id == TabId);
                        if (existingTab == null)
                        {
                            BuildRibbonTab(ribbon, activate);
                        }
                        else
                        {
                            if (!existingTab.IsVisible) existingTab.IsVisible = true;
                            if (!existingTab.IsEnabled) existingTab.IsEnabled = true;
                            if (existingTab.Panels.Count == 0)
                            {
                                PopulatePanels(existingTab);
                            }
                            if (activate)
                            {
                                existingTab.IsActive = true;
                            }
                        }
                        ribbon.UpdateLayout();
                    }
                    catch (Exception ex)
                    {
                        var ed = AcApp.DocumentManager.MdiActiveDocument?.Editor;
                        ed?.WriteMessage($"\n[Plant3D IFC Exporter] Ribbon update error: {ex.Message}\n");
                    }
                }

                if (ribbon.Dispatcher.CheckAccess())
                {
                    Execute();
                }
                else
                {
                    ribbon.Dispatcher.BeginInvoke(new Action(Execute));
                }
            }
            catch { }
        }

        private static void BuildRibbonTab(RibbonControl ribbon, bool activate = false)
        {
            var tab = ribbon.Tabs.FirstOrDefault(t => t.Id == TabId);
            if (tab == null)
            {
                tab = new RibbonTab
                {
                    Title = TabTitle,
                    Id = TabId,
                    IsVisible = true,
                    IsEnabled = true
                };
                ribbon.Tabs.Add(tab);
            }
            else
            {
                tab.IsVisible = true;
                tab.IsEnabled = true;
            }

            PopulatePanels(tab);

            if (activate)
            {
                tab.IsActive = true;
            }
        }

        private static void PopulatePanels(RibbonTab tab)
        {
            tab.Panels.Clear();

            // 1. Panel: Model Export
            var panelSourceExport = new RibbonPanelSource
            {
                Title = "Model Export",
                Id = "ID_PANEL_PLANT_IFC_EXPORT"
            };
            var panelExport = new RibbonPanel { Source = panelSourceExport };
            tab.Panels.Add(panelExport);

            // Large Button: Export to IFC
            var btnExport = new RibbonButton
            {
                Text = "Export\nto IFC",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                LargeImage = IconHelper.CreateIfcIcon(32),
                Image = IconHelper.CreateIfcIcon(16),
                CommandHandler = new RelayCommand(() =>
                {
                    var doc = AcApp.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                    {
                        var win = new ExportSettingsWindow(doc);
                        AcApp.ShowModalWindow(win);
                    }
                }),
                ToolTip = new RibbonToolTip
                {
                    Title = "Export model to IFC",
                    Content = "Open the settings dialog and export the Plant 3D model to IFC4 or IFC2x3 with database attributes."
                }
            };
            panelSourceExport.Items.Add(btnExport);

            // Row of sub-buttons (Quick export / Selection)
            var rowExport = new RibbonRowPanel();

            var btnQuick = new RibbonButton
            {
                Text = "Quick IFC4",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Image = IconHelper.CreateIfcIcon(16),
                CommandHandler = new RelayCommand(() =>
                {
                    AcApp.DocumentManager.MdiActiveDocument?.SendStringToExecute("PLANTTOIFC ", true, false, false);
                }),
                ToolTip = new RibbonToolTip
                {
                    Title = "Quick export",
                    Content = "Export the current drawing to IFC4 using the last saved settings."
                }
            };

            var btnSelected = new RibbonButton
            {
                Text = "Selected objects",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Image = IconHelper.CreateIfcIcon(16),
                CommandHandler = new RelayCommand(() =>
                {
                    var doc = AcApp.DocumentManager.MdiActiveDocument;
                    if (doc != null)
                    {
                        var config = Settings.ExportConfig.Load();
                        config.Scope = Settings.ExportScopeOption.SelectedObjects;
                        var win = new ExportSettingsWindow(doc);
                        AcApp.ShowModalWindow(win);
                    }
                }),
                ToolTip = new RibbonToolTip
                {
                    Title = "Export selection",
                    Content = "Export only the piping or equipment components currently selected in the drawing."
                }
            };

            rowExport.Items.Add(btnQuick);
            rowExport.Items.Add(new RibbonRowBreak());
            rowExport.Items.Add(btnSelected);
            panelSourceExport.Items.Add(rowExport);

            // 2. Panel: Tools and Help
            var panelSourceTools = new RibbonPanelSource
            {
                Title = "Tools",
                Id = "ID_PANEL_PLANT_IFC_TOOLS"
            };
            var panelTools = new RibbonPanel { Source = panelSourceTools };
            tab.Panels.Add(panelTools);

            var rowTools = new RibbonRowPanel();

            var btnFolder = new RibbonButton
            {
                Text = "Plugin folder",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Image = IconHelper.CreateSettingsIcon(16),
                CommandHandler = new RelayCommand(() =>
                {
                    try
                    {
                        var pluginsDir = Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "Autodesk",
                            "ApplicationPlugins",
                            "Plant3D_IfcExporter.bundle"
                        );
                        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
                        Process.Start(new ProcessStartInfo("explorer.exe", pluginsDir) { UseShellExecute = true });
                    }
                    catch { }
                }),
                ToolTip = new RibbonToolTip
                {
                    Title = "Install folder",
                    Content = "Open the plugin directory in Windows Explorer."
                }
            };

            var btnAbout = new RibbonButton
            {
                Text = "About",
                ShowText = true,
                ShowImage = true,
                Size = RibbonItemSize.Standard,
                Image = IconHelper.CreateSettingsIcon(16),
                CommandHandler = new RelayCommand(() =>
                {
                    System.Windows.MessageBox.Show(
                        "AutoCAD Plant 3D IFC Exporter\nVersion: 1.0.0 (2025/2026)\n\n" +
                        "Open-source plugin for exporting process piping and equipment to IFC4/IFC2x3.\n" +
                        "Preserves semantics, line properties, specifications, and diameters.\n\n" +
                        "Original author: baidurovlabs.ru (https://baidurovlabs.ru)\n" +
                        "Huge thanks to the upstream project, none of this would exist without their work.\n\n" +
                        "English-localized fork maintained by: Wahid Hassani\n" +
                        "GitHub: https://github.com/OneAndOnlyWahidHassani\n" +
                        "Contact: via GitHub inbox\n\n" +
                        "License: GNU GPLv3 (both original and fork)",
                        "About DWG2IFC — baidurovlabs.ru / Wahid Hassani fork",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information
                    );
                }),
                ToolTip = new RibbonToolTip
                {
                    Title = "About",
                    Content = "Version info and export capabilities."
                }
            };

            rowTools.Items.Add(btnFolder);
            rowTools.Items.Add(new RibbonRowBreak());
            rowTools.Items.Add(btnAbout);
            panelSourceTools.Items.Add(rowTools);
        }
    }
}
