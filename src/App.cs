using System;
using System.Windows;
using System.Windows.Threading;

namespace Gallerizz
{
    // Point d'entrée. Une fenêtre, une image, et fermer ferme — rien ne traîne en zone de notification.
    internal static class App
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += OnUnhandled;
            string file = args.Length > 0 ? args[0] : null;
            var window = new MainWindow(file);
            app.Run(window);
        }

        private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("Erreur inattendue : " + e.Exception.Message, "Gallerizz",
                MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
