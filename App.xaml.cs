using System;
using System.Windows;
using System.Windows.Threading;

namespace CyberpunkSlideshowWidget
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static int _isExiting = 0;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Handle unhandled non-UI exceptions safely
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    if (args.ExceptionObject is Exception ex)
                    {
                        MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                finally
                {
                    CleanProcessExit(1);
                }
            };

            // Handle unhandled UI dispatcher exceptions safely
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    MessageBox.Show($"An unexpected UI error occurred: {args.Exception.Message}", "Application Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    args.Handled = true;
                }
                catch
                {
                    CleanProcessExit(1);
                }
            };
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            CleanProcessExit(e.ApplicationExitCode);
        }

        /// <summary>
        /// Guarantees a zero-footprint, immediate clean exit with no lingering background STA, COM,
        /// WIC decoder, or DirectX worker threads remaining in Windows Task Manager.
        /// </summary>
        public static void CleanProcessExit(int exitCode = 0)
        {
            if (System.Threading.Interlocked.Exchange(ref _isExiting, 1) != 0)
            {
                return;
            }

            try
            {
                // Force full generation 2 garbage collection and drain all pending finalizers
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);
            }
            catch
            {
                // Ignore any teardown exceptions during final collection
            }
            finally
            {
                // Guarantees immediate termination of the process and all native worker threads
                Environment.Exit(exitCode);
            }
        }
    }
}

