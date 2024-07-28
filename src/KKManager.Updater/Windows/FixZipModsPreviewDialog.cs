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
using AssetStudio;
using SharpCompress.Archives.Zip;
using SharpCompress.Common;
using SharpCompress.Archives;
using BrightIdeasSoftware;
using System.Drawing;
using KKManager.Data.Cards;



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

        private void button1_Click(object sender, EventArgs e)
        {
            var a = InstallDirectoryHelper.ModsPath.FullName;
            Console.WriteLine(a);
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();
            _currentTask = TryReadSideloaderMods(InstallDirectoryHelper.ModsPath.FullName, _cancelSource.Token);
        }

        public static Task TryReadSideloaderMods(string modDirectory, CancellationToken cancellationToken, SearchOption searchOption = SearchOption.AllDirectories)
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
                        Console.WriteLine("No zipmod folder detected");
                        return;
                    }

                    var files = Directory.EnumerateFiles(modDirectory, "*.*", searchOption);
                    int counterFinish = 0;
                    int counterAll = 0;
                    object lockObject = new object();
                    foreach (string file in files)
                    {
                        if (IsValidZipmodExtension(Path.GetExtension(file))) {
                            counterAll++;
                        }
                    }
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = cancellationToken }, file =>
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();

                            if (!IsValidZipmodExtension(Path.GetExtension(file))) return;

                            LoadFromFile(file);
                            lock (lockObject)
                            {
                                counterFinish++;
                                Console.WriteLine($"Processed {counterFinish} / {counterAll} files.");
                            }
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
                }
                finally
                {
                    Console.WriteLine($"Finished loading zipmods from [{modDirectory}] in {sw.ElapsedMilliseconds}ms");
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

        public static SideloaderModInfo LoadFromFile(string filePath)
        {
            string extractedPath = null;
            try
            {
                extractedPath = ExtractToTempDirectory(filePath);

                if (extractedPath != null)
                {
                    var items = ModItem.ReadFromFolder(extractedPath);
                    var thumbPaths = Directory.EnumerateFiles(extractedPath, "*.png", SearchOption.AllDirectories).ToArray();
                    string[] thumbNames = thumbPaths.Select(Path.GetFileName).ToArray();
                    var unityPaths = Directory.EnumerateFiles(extractedPath, "*.unity3d", SearchOption.AllDirectories).ToArray();
                    var hasUpdate = false;
                    foreach (ModItem item in items)
                    {
                        hasUpdate = hasUpdate || GenerateItemImageIfNeeds(filePath, extractedPath, item, thumbNames, thumbPaths, unityPaths);
                    }
                }
                CleanupTempDirectory(extractedPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to Mod-LoadFromFile \"{filePath}\" with error: {ex.ToStringDemystified()}");
                CleanupTempDirectory(extractedPath);
            }
            return null;
        }

        public static bool GenerateItemImageIfNeeds(string oriPath, string extractedPath, ModItem item, string[] thumbNames, string[] thumbPaths, string[] unityPaths)
        {
            try
            {
                var ThumbTex = item.Datas["ThumbTex"];
                var MainAB = item.Datas["MainAB"];
                var Name = item.Datas["Name"];
                string foundPath = null;
                if (ThumbTex == null || ThumbTex.Length==0)
                {
                    return false; // Don't know how to generate
                }
                else if (thumbNames.Length != 0)
                {
                    string searchItem = ThumbTex + ".png";
                    int index = thumbNames.ToList().IndexOf(searchItem);
                    if (index != -1)
                    {
                        foundPath = thumbPaths[index];
                    }
                }

                if (foundPath == null)
                {
                    var unityFilePath = Path.Combine(extractedPath, "abdata", MainAB).Replace('/', '\\');
                    if (unityPaths.Contains(unityFilePath))
                    {

                        string outputName = $"{Name}.png";
                        string outputFolder = Path.Combine(extractedPath, "abdata", "chara", "thumb");
                        Directory.CreateDirectory(outputFolder);
                        bool pngData = ExtractPngFromUnityFile(unityFilePath, ThumbTex, Path.Combine(outputFolder, outputName));
                        
                        return true;
                    }
                    else
                    {
                        Console.WriteLine($"Not found MainAB: {MainAB} in unitys: {unityFilePath.Length}");
                    }
                    return true;
                }

                

               
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to GenerateItemImage \"{oriPath}\" with error: {ex.ToStringDemystified()}");
            }
            return false;
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

        public static bool ExtractPngFromUnityFile(string unityFilePath, string textureName, string outputPath)
        {
            try
            {
                var assetsManager = new AssetsManager();
                assetsManager.LoadFiles(unityFilePath);
                foreach (var assetFile in assetsManager.assetsFileList)
                {
                    foreach (var asset in assetFile.Objects)
                    {
                        if (asset is Texture2D texture && texture.m_Name == textureName)
                        {
                            ImageFormat type = ImageFormat.Png;
                            var image = texture.ConvertToImage(true);
                            if (image == null)
                                return false;
                            using (image)
                            {
                                using (var file = File.OpenWrite(outputPath))
                                {
                                    image.WriteToStream(file, type);
                                }
                                return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ExtractPngFromUnityFile: {ex.Message}");
                return false;
            }
        }

        public static string ExtractToTempDirectory(string zipFilePath)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempPath);

            try
            {
                using (var archive = ZipArchive.Open(zipFilePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            entry.WriteToDirectory(tempPath, new ExtractionOptions()
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }
                return tempPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting ZIP file: {ex.Message}");
                Directory.Delete(tempPath, true);
                return null;
            }
        }

        public static void CleanupTempDirectory(string tempPath)
        {
            if (Directory.Exists(tempPath))
            {
                try
                {
                    Directory.Delete(tempPath, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error cleaning up temporary directory: {ex.Message}");
                }
            }
        }
    }
}
