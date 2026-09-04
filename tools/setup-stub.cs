using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

// L'installeur de Gallerizz : deballe l'archive embarquee dans un dossier "Gallerizz"
// cree a cote de l'installeur. Pas de registre, pas de droits admin, pas de magie.
internal static class Setup
{
    [STAThread]
    private static void Main()
    {
        try
        {
            string baseDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string target = Path.Combine(baseDir, "Gallerizz");
            using (Stream s = Assembly.GetEntryAssembly().GetManifestResourceStream("app.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                Directory.CreateDirectory(target);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    string dest = Path.Combine(target, entry.FullName.Replace('/', '\\'));
                    string destDir = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(dest, true); // ecrase : reinstaller = mettre a jour
                }
            }
            DialogResult r = MessageBox.Show(
                "Gallerizz est installé dans :\n" + target +
                "\n\nPour en faire votre visualiseur par défaut : clic droit sur une image → Ouvrir avec → Gallerizz." +
                "\n\nOuvrir le dossier ?",
                "Gallerizz — installation terminée", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (r == DialogResult.Yes)
                System.Diagnostics.Process.Start("explorer.exe", "\"" + target + "\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Installation impossible : " + ex.Message, "Gallerizz",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
