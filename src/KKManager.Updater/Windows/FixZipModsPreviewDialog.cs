using System;
using System.IO;
using System.Reactive.Subjects;
using System.Threading;
using System.Windows.Forms;
using KKManager.Data.Zipmods;
using KKManager.Functions;
using KKManager.Properties;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Security;
using SharpCompress.Common.Zip;
using Sideloader;
using System.Collections.Generic;
using System.Reflection;


namespace KKManager.Updater.Windows
{
    public partial class FixZipModsPreviewDialog : Form
    {
        public FixZipModsPreviewDialog()
        {
            InitializeComponent();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            var s = Settings.Default;

        }

        private static CancellationTokenSource _cancelSource;
        private static Task _currentTask;
        private static ReplaySubject<SideloaderModInfo> _zipmods;

        private void button1_Click(object sender, EventArgs e)
        {
            var a = InstallDirectoryHelper.ModsPath.FullName;
            Console.WriteLine(a);
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();
            _currentTask = TryReadSideloaderMods(InstallDirectoryHelper.ModsPath.FullName, _zipmods, _cancelSource.Token);
        }

        public static Task TryReadSideloaderMods(string modDirectory, ReplaySubject<SideloaderModInfo> subject, CancellationToken cancellationToken, SearchOption searchOption = SearchOption.AllDirectories)
        {
            Console.WriteLine($"Start loading zipmods from [{modDirectory}]");

            var token = cancellationToken;

            void ReadSideloaderModsAsync()
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    if (!Directory.Exists(modDirectory))
                    {
                        subject.OnCompleted();
                        Console.WriteLine("No zipmod folder detected");
                        return;
                    }

                    var files = Directory.EnumerateFiles(modDirectory, "*.*", searchOption);
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, file =>
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();

                            if (!IsValidZipmodExtension(Path.GetExtension(file))) return;

                            subject.OnNext(LoadFromFile(file));
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to load zipmod from \"{file}\" with error: {ex.ToStringDemystified()}");
                        }
                    });

                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                }
                catch (Exception ex)
                {
                    if (ex is AggregateException aggr)
                        ex = aggr.Flatten().InnerExceptions.First();

                    if (ex is OperationCanceledException)
                        return;

                    if (ex is SecurityException || ex is UnauthorizedAccessException)
                        MessageBox.Show("Could not load information about zipmods because access to the plugins folder was denied. Check the permissions of your mods folder and try again.\n\n" + ex.Message,
                            "Load zipmods", MessageBoxButtons.OK, MessageBoxIcon.Warning);

                    Console.WriteLine("Crash when loading zipmods: " + ex.ToStringDemystified());
                    subject.OnError(ex);
                }
                finally
                {
                    Console.WriteLine($"Finished loading zipmods from [{modDirectory}] in {sw.ElapsedMilliseconds}ms");
                    subject.OnCompleted();
                }
            }

            try
            {
                var task = new Task(ReadSideloaderModsAsync, token, TaskCreationOptions.LongRunning);
                task.Start();
                return task;
            }
            catch (OperationCanceledException)
            {
                return Task.FromCanceled(token);
            }
        }

        public static SideloaderModInfo LoadFromFile(string filename)
        {
            var location = new FileInfo(filename);
            Console.WriteLine(location.FullName);
            return null;
        }

        public static bool IsValidZipmodExtension(string extension)
        {
            var exts = new[]
            {
                ".zip",
                ".zi_",
                ".zipmod",
                ".zi_mod",
            };

            return exts.Any(x => x.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }
    }
}
