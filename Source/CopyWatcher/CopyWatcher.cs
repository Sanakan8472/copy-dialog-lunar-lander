using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace CopyDialogLunarLander
{
    /// <summary>
    /// Detects new copy dialogs being opened and closed as well as new charts added to existing dialogs.
    /// </summary>
    class OperationStatusWindowWatcher : IDisposable
    {
        public delegate void ChartViewOpenedHandler(AutomationElement chartView, IntPtr parentOperationStatusWindow);
        public event ChartViewOpenedHandler ChartViewOpened;
        public delegate void ChartViewClosedHandler(AutomationElement chartView, IntPtr parentOperationStatusWindow);
        public event ChartViewClosedHandler ChartViewClosed;

        private PropertyCondition _chartViewCondition = new PropertyCondition(AutomationElement.ClassNameProperty, "ChartView", PropertyConditionFlags.IgnoreCase);
        private PropertyCondition _operationStatusWindowCondition = new PropertyCondition(AutomationElement.ClassNameProperty, "OperationStatusWindow");
        private AutomationEventHandler _eventHandler;
        private Task _backgroundUpdater;
        private bool _runBackgroundTask = true;

        class StatusWindow
        {
            public AutomationElement statusWindow;
            public List<ProgressChart> progressWindows = new List<ProgressChart>();
        }

        struct ProgressChart
        {
            public AutomationElement progressChart;
            public IntPtr nativeHWND;
        }
        private Dictionary<IntPtr, StatusWindow> _trackedWindows = new Dictionary<IntPtr, StatusWindow>();

        public OperationStatusWindowWatcher()
        {
        }

        public void Start()
        {
            {
                // Find all operation windows.
                AutomationElement root = AutomationElement.RootElement;

                AutomationElementCollection elementCollection = root.FindAll(TreeScope.Children, _operationStatusWindowCondition);
                foreach (AutomationElement elem in elementCollection)
                {
                    AddStatusWindow(elem);
                }
            }

            {
                // Configure automation event handler.
                _eventHandler = new AutomationEventHandler(OnWindowOpen);
                Automation.AddAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, TreeScope.Children, _eventHandler);
                Automation.AddAutomationEventHandler(WindowPattern.WindowClosedEvent, AutomationElement.RootElement, TreeScope.Children, _eventHandler);
            }

            // Start background updater.
            _backgroundUpdater = Task.Run(async () =>
            {
                try
                {
                    while (_runBackgroundTask)
                    {
                        await Task.Delay(2000);

                        if (_runBackgroundTask)
                            UpdatStatusWindows();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
                }

            });
        }

        private void AddStatusWindow(AutomationElement statusWindow)
        {
            lock (_trackedWindows)
            {
                try
                {
                    var sw = new StatusWindow();
                    sw.statusWindow = statusWindow;
                    IntPtr nativeRoot = new IntPtr((int)statusWindow.Current.NativeWindowHandle);
                    System.Diagnostics.Debug.WriteLine($"AddStatusWindow: {nativeRoot}");
                    AutomationElementCollection progressCharts = statusWindow.FindAll(TreeScope.Subtree, _chartViewCondition);
                    foreach (AutomationElement pc in progressCharts)
                    {
                        sw.progressWindows.Add(new ProgressChart { progressChart = pc, nativeHWND = new IntPtr((int)pc.Current.NativeWindowHandle) });
                        ChartViewOpened?.Invoke(pc, nativeRoot);
                    }

                    StatusWindow swOld;
                    if (_trackedWindows.TryGetValue(nativeRoot, out swOld))
                    {
                        // This can happen as the explorer does not destroy the window but simply hides it when the last operations finishes.
                        swOld.statusWindow = sw.statusWindow;
                        swOld.progressWindows.AddRange(sw.progressWindows);
                    }
                    else
                    {
                        _trackedWindows.Add(nativeRoot, sw);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
                }
            }
        }

        private void RemoveStatusWindow(AutomationElement statusWindow)
        {
            lock (_trackedWindows)
            {
                try
                {
                    IntPtr nativeRoot = new IntPtr((int)statusWindow.Current.NativeWindowHandle);
                    System.Diagnostics.Debug.WriteLine($"RemoveStatusWindow: {nativeRoot}");

                    StatusWindow sw;
                    if (_trackedWindows.TryGetValue(nativeRoot, out sw))
                    {
                        foreach (var pw in sw.progressWindows)
                        {
                            ChartViewClosed?.Invoke(pw.progressChart, nativeRoot);
                        }
                        _trackedWindows.Remove(nativeRoot);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
                }
            }
        }

        private void UpdatStatusWindows()
        {
            lock (_trackedWindows)
            {

                foreach (var item in _trackedWindows)
                {
                    try
                    {
                        List<IntPtr> oldCharts = item.Value.progressWindows.Select(x => x.nativeHWND).ToList();
                        List<IntPtr> newCharts = new List<IntPtr>();
                        AutomationElementCollection progressCharts = item.Value.statusWindow.FindAll(TreeScope.Subtree, _chartViewCondition);
                        foreach (AutomationElement pc in progressCharts)
                        {
                            IntPtr nativeRoot = new IntPtr((int)pc.Current.NativeWindowHandle);
                            newCharts.Add(nativeRoot);
                        }

                        // Remove old
                        var toRemove = item.Value.progressWindows.Where(x => !newCharts.Contains(x.nativeHWND)).ToList();
                        foreach (var remove in toRemove)
                        {
                            ChartViewClosed?.Invoke(remove.progressChart, item.Key);
                        }
                        item.Value.progressWindows.RemoveAll(x => !newCharts.Contains(x.nativeHWND));

                        // Add new
                        foreach (AutomationElement pc in progressCharts)
                        {
                            IntPtr nativeRoot = new IntPtr((int)pc.Current.NativeWindowHandle);
                            if (!oldCharts.Contains(nativeRoot))
                            {
                                item.Value.progressWindows.Add(new ProgressChart { progressChart = pc, nativeHWND = new IntPtr((int)pc.Current.NativeWindowHandle) });
                                ChartViewOpened?.Invoke(pc, item.Key);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
                    }
                }

            }
        }

        private void OnWindowOpen(object src, AutomationEventArgs e)
        {
            // Make sure the element still exists. Elements such as tooltips
            // can disappear before the event is processed.
            AutomationElement sourceElement;
            try
            {
                sourceElement = src as AutomationElement;
                if (sourceElement.Current.ClassName != "OperationStatusWindow")
                    return;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception: {ex}");
                return;
            }

            if (e.EventId == WindowPattern.WindowOpenedEvent)
                AddStatusWindow(sourceElement);
            else if (e.EventId == WindowPattern.WindowClosedEvent)
                RemoveStatusWindow(sourceElement);
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _runBackgroundTask = false;
                    if (_backgroundUpdater != null)
                    {
                        _backgroundUpdater.Wait();
                        _backgroundUpdater = null;
                    }
                }

                Automation.RemoveAutomationEventHandler(WindowPattern.WindowOpenedEvent, AutomationElement.RootElement, _eventHandler);
                Automation.RemoveAutomationEventHandler(WindowPattern.WindowClosedEvent, AutomationElement.RootElement, _eventHandler);

                disposedValue = true;
            }
        }

        // todo: override a finalizer only if dispose(bool disposing) above has code to free unmanaged resources.
        ~OperationStatusWindowWatcher()
        {
            // do not change this code. put cleanup code in dispose(bool disposing) above.
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
