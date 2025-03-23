using System.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Automation;
using System.ComponentModel;
using System.Reflection;
using Microsoft.Win32;

namespace CopyDialogLunarLander
{
    /// <summary>
    /// Dummy window that is never shown but needs to exist to prevent the app from exiting.
    /// </summary>
    class MainWindow : Window
    {
    }

    /// <summary>
    /// CopyWatcherApplication application creates a new OverlayWindow for every copy dialog and manages the tray icon.
    /// </summary>
    class CopyWatcherApplication : Application
    {
        struct OverlayWindowData
        {
            public OverlayWindow window;
            public AutomationElement chartView;
            public IntPtr parentOperationStatusWindow;
        }

        struct Game
        {
            public Type type;
            public System.Windows.Forms.ToolStripMenuItem menuItem;
        }

        private System.Windows.Forms.NotifyIcon _notifyIcon;

        private OperationStatusWindowWatcher _watcher = new OperationStatusWindowWatcher();
        private Dictionary<AutomationElement, OverlayWindowData> _overlayWindows = new Dictionary<AutomationElement, OverlayWindowData>();
        private List<Game> _games = new List<Game>();
        private Type _currentGame = null;
        private System.Windows.Forms.ToolStripMenuItem _gameOptions = null;
        private System.Windows.Forms.ToolStripMenuItem _autostart = null;

        const string _autostartRegKey = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";
        const string _autostartName = "CopyDialogLunarLander";

        public CopyWatcherApplication()
        {
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Dummy window that is never shown just so the app doesn't exit if no file ops are active.
            MainWindow = new MainWindow();

            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            _notifyIcon.Icon = CopyDialogLunarLander.Properties.Resources.LunarLander;
            _notifyIcon.Visible = true;
            CreateContextMenu();

            _watcher.ChartViewOpened += OnChartViewOpened;
            _watcher.ChartViewClosed += OnChartViewClosed;
            _watcher.Start();
        }

        private void CreateContextMenu()
        {
            _notifyIcon.ContextMenuStrip = new System.Windows.Forms.ContextMenuStrip();
            {
                var gamesMenu = (System.Windows.Forms.ToolStripMenuItem)_notifyIcon.ContextMenuStrip.Items.Add("Games");
                var type = typeof(OverlayWindow);
                var types = AppDomain.CurrentDomain.GetAssemblies().SelectMany(s => s.GetTypes()).Where(p => type.IsAssignableFrom(p) && p != type && !p.IsAbstract).ToList();
                _currentGame = typeof(LunarLanderOverlayWindow);
                foreach (var derivedType in types)
                {
                    var game = (System.Windows.Forms.ToolStripMenuItem)gamesMenu.DropDownItems.Add("Lunar Lander");
                    game.Checked = derivedType == _currentGame;
                    game.Click += GameChanged;
                    _games.Add(new Game { type = derivedType, menuItem = game });
                }
            }
            _gameOptions = (System.Windows.Forms.ToolStripMenuItem)_notifyIcon.ContextMenuStrip.Items.Add("Game Options");
            _autostart = (System.Windows.Forms.ToolStripMenuItem)_notifyIcon.ContextMenuStrip.Items.Add("Start with Windows");
            _autostart.Click += (s, e) => ToggleAutostart();
            _autostart.Checked = IsAutostartEnabled();
            _notifyIcon.ContextMenuStrip.Items.Add("-");
            _notifyIcon.ContextMenuStrip.Items.Add("Exit Lunar Lander").Click += (s, e) => ExitApplication();

            UpdateGameOptions();
        }

        private void GameChanged(object sender, EventArgs e)
        {
            try
            {
                var game = _games.FirstOrDefault(x => x.menuItem == sender);
                if (game.type != null)// && !game.menuItem.Checked)
                {
                    _currentGame = game.type;
                    _games.ForEach(x => x.menuItem.Checked = x.menuItem == game.menuItem);

                    var values = _overlayWindows.Values.ToList();
                    foreach (var item in values)
                    {
                        DestroyOverlay(item.chartView);
                    }
                    foreach (var item in values)
                    {
                        CreateOverlay(item.chartView, item.parentOperationStatusWindow);
                    }
                    UpdateGameOptions();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
            }
        }

        private void UpdateGameOptions()
        {
            _gameOptions.DropDownItems.Clear();

            MethodInfo info = _currentGame.GetMethod("FillOptions", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

            object value = info.Invoke(null, new object[] { _gameOptions });
        }

        private bool IsAutostartEnabled()
        {
            try
            {
                RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(_autostartRegKey, false);
                var value = registryKey.GetValue(_autostartName);
                string exePath = Assembly.GetEntryAssembly().Location;
                if (value is string && (string)value == exePath)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
            }
            return false;
        }

        private void ToggleAutostart()
        {
            try
            {
                bool enabled = IsAutostartEnabled();
                RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(_autostartRegKey, true);
                if (!enabled)
                {
                    string exePath = Assembly.GetEntryAssembly().Location;
                    registryKey.SetValue(_autostartName, exePath);
                    _autostart.Checked = true;
                }
                else
                {
                    registryKey.DeleteValue(_autostartName);
                    _autostart.Checked = false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
            }
        }

        private void ExitApplication()
        {
            _watcher.Dispose();
            var values = _overlayWindows.Values.ToList();
            foreach (var item in values)
            {
                DestroyOverlay(item.chartView);
            }
            _overlayWindows.Clear();

            _notifyIcon.Dispose();
            _notifyIcon = null;
            MainWindow.Close();
        }

        private void CreateOverlay(AutomationElement chartView, IntPtr parentOperationStatusWindow)
        {
            System.Diagnostics.Debug.WriteLine($"Progress chart opened {chartView.Current.NativeWindowHandle}");
            OverlayWindow ow = (OverlayWindow)Activator.CreateInstance(_currentGame);
            if (!_overlayWindows.ContainsKey(chartView))
            {
                _overlayWindows.Add(chartView, new OverlayWindowData { window = ow, chartView = chartView, parentOperationStatusWindow = parentOperationStatusWindow });
                ow.Wake(chartView, parentOperationStatusWindow);
            }
        }

        private void DestroyOverlay(AutomationElement chartView)
        {
            System.Diagnostics.Debug.WriteLine($"Progress chart closed");
            if (_overlayWindows.ContainsKey(chartView))
            {
                _overlayWindows[chartView].window.Close();
                _overlayWindows.Remove(chartView);
            }
        }

        public void OnChartViewOpened(AutomationElement chartView, IntPtr parentOperationStatusWindow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                CreateOverlay(chartView, parentOperationStatusWindow);
            }));
        }

        public void OnChartViewClosed(AutomationElement chartView, IntPtr parentOperationStatusWindow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DestroyOverlay(chartView);
            }));
        }
    }
}
