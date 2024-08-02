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
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;



namespace KKManager.Updater.Windows
{
    public partial class FixZipModsPreviewDialog : Form
    {
        private SynchronizationContext _syncContext;

        public FixZipModsPreviewDialog()
        {
            InitializeComponent();
            _syncContext = SynchronizationContext.Current;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            this.progressBar2.Value = 0;

        }

        private static CancellationTokenSource _cancelSource;
        private static Task _currentTask;
        private static object _lockObject;

        private void button1_Click(object sender, EventArgs e)
        {
            var a = InstallDirectoryHelper.ModsPath.FullName;
            Console.WriteLine(a);
            _cancelSource?.Dispose();
            _cancelSource = new CancellationTokenSource();
            _currentTask = TryReadSideloaderMods(InstallDirectoryHelper.ModsPath.FullName, _cancelSource.Token);
            _lockObject = new object();
        }

        public Task TryReadSideloaderMods(string modDirectory, CancellationToken cancellationToken, SearchOption searchOption = SearchOption.AllDirectories)
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
                    
                    foreach (string file in files)
                    {
                        if (IsValidZipmodExtension(Path.GetExtension(file))) {
                            counterAll++;
                        }
                    }
                    _syncContext.Post(_ =>
                    {
                        this.progressBar2.Maximum = counterAll;
                    }, null);
                    
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, file =>
                    {
                        try
                        {
                            token.ThrowIfCancellationRequested();

                            if (!IsValidZipmodExtension(Path.GetExtension(file))) return;

                            LoadFromFile(file);
                            lock (_lockObject)
                            {
                                counterFinish++;
                                    _syncContext.Post(_ =>
                                    {
                                        this.progressBar2.Value = counterFinish;
                                    }, null);
                               
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

        public void LoadFromFile(string filePath)
        {
            string extractedPath = null;
            try
            {
                extractedPath = ExtractToTempDirectory(filePath);
                var shouldReZip = true;
                var hasUpdate = false;

                if (extractedPath != null)
                {
                    var items = ModItem.ReadFromFolder(extractedPath);
                    var thumbPaths = Directory.EnumerateFiles(extractedPath, "*.png", SearchOption.AllDirectories).ToArray();
                    string[] thumbNames = thumbPaths.Select(Path.GetFileName).ToArray();
                    var unityPaths = Directory.EnumerateFiles(extractedPath, "*.unity3d", SearchOption.AllDirectories).ToArray();
                    var thumbOutputPaths = Array.Empty<string>();


                    foreach (ModItem item in items)
                    {
                        var (result, msg, path) =  GenerateItemImageIfNeeds(filePath, extractedPath, item, thumbNames, thumbPaths, unityPaths);
                        if (result == ModItemResult.MoveImage_Failed || result == ModItemResult.CSVUpdate_Failed)
                        {
                            shouldReZip = false;
                        }
                        if (result == ModItemResult.ExtracImage_Finish || result == ModItemResult.MoveImage_Finish)
                        {
                            if (path != null)
                            {
                                thumbOutputPaths.Append(path);
                            }
                            hasUpdate = true;
                        }
                        else if (result == ModItemResult.ThumbTexValue_NotFound)
                        {
                            AppendLog($"ThumbTexValue_NotFound ({filePath})", System.Drawing.Color.Red);
                        }
                        else if (result == ModItemResult.UnityTexture_NotFound)
                        {
                            AppendLog($"UnityTexture_NotFound ({filePath})", System.Drawing.Color.Red);
                        }
                        else if (result == ModItemResult.MainAB_NotFound)
                        {
                            AppendLog($"MainAB_NotFound ({filePath})", System.Drawing.Color.Red);
                        }
                        else if (result == ModItemResult.UnityFileLoad_Failed)
                        {
                            AppendLog($"UnityFileLoad_Failed ({filePath})", System.Drawing.Color.Red);
                        }
                        else if (result == ModItemResult.Texture2Image_Failed)
                        {
                            AppendLog($"Texture2Image_Failed ({filePath})", System.Drawing.Color.Red);
                        }
                        else if (result == ModItemResult.SaveImage_Failed)
                        {
                            AppendLog($"SaveImage_Failed ({filePath})", System.Drawing.Color.Red);
                        }
                        else if (result == ModItemResult.MoveImage_Failed)
                        {
                            AppendLog($"MoveImage_Failed ({filePath})", System.Drawing.Color.Red);
                        }
                        else if (result == ModItemResult.CSVUpdate_Failed)
                        {
                            AppendLog($"CSVUpdate_Failed ({filePath})", System.Drawing.Color.Red);
                        }
                        else
                        {
                            AppendLog($"Unknown Error", System.Drawing.Color.Red);
                        }
                    }

                    var filesToDelete = thumbPaths.Except(thumbOutputPaths, StringComparer.OrdinalIgnoreCase).ToArray();
                    foreach (var aFilePath in filesToDelete)
                    {
                        try
                        {
                            if (File.Exists(aFilePath))
                            {
                                File.Delete(aFilePath);
                            }
                        }
                        catch
                        {
                        }
                    }
                }
                CleanupTempDirectory(extractedPath);
            }
            catch (Exception ex)
            {
                AppendLog("Unknown Error", System.Drawing.Color.Red);
                CleanupTempDirectory(extractedPath);
            }
        }

        public (ModItemResult, string, string) GenerateItemImageIfNeeds(string oriPath, string extractedPath, ModItem item, string[] thumbNames, string[] thumbPaths, string[] unityPaths)
        {
            var ThumbTex = item.Datas["ThumbTex"];
            var MainAB = item.Datas["MainAB"];
            var ThumbAB = item.Datas["ThumbAB"];
            var Name = item.Datas["Name"];
            string pngPath = null;
            if (ThumbTex == null || ThumbTex.Length == 0)
            {
                return (ModItemResult.ThumbTexValue_NotFound, Name, null);
            }
            else if (thumbNames.Length != 0)
            {
                string searchItem = ThumbTex + ".png";
                int index = thumbNames.ToList().IndexOf(searchItem);
                if (index != -1)
                {
                    pngPath = thumbPaths[index];
                }
            }

            string outputFolder = Path.Combine(extractedPath, "abdata", "chara", "thumb");
            string outputName = GenerateUniqueFileName(outputFolder, Name, ".png");
            string outputPath = Path.Combine(outputFolder, outputName);
            Directory.CreateDirectory(outputFolder);

            if (pngPath == null)
            {
                var mainFilePath = Path.Combine(extractedPath, "abdata", MainAB).Replace('/', '\\');
                var thumbFilePath = Path.Combine(extractedPath, "abdata", ThumbAB).Replace('/', '\\');

                ModItemResult result = ModItemResult.UnKnown_Error;
                string msg = "";
                if (unityPaths.Contains(thumbFilePath))
                {
                    (result, msg) = ExtractPngFromUnityFile(thumbFilePath, ThumbTex, outputPath);
                }
                if (!unityPaths.Contains(thumbFilePath) || result == ModItemResult.UnityTexture_NotFound)
                {
                    if (!unityPaths.Contains(mainFilePath))
                    {
                        return (ModItemResult.MainAB_NotFound, MainAB, null);
                    }
                    (result, msg) = ExtractPngFromUnityFile(mainFilePath, ThumbTex, outputPath);
                }
                if (result == ModItemResult.ExtracImage_Success)
                {
                    lock (_lockObject)
                    {
                        this.pictureBox1.Image = CreateIndependentImage(outputPath);
                    }
                }
                else
                {
                    return (result, msg, null);
                }
            }
            else
            {
                if (pngPath == outputPath)
                {
                    return (ModItemResult.KeepImage_Finish, outputName, null);
                }
                try
                {
                    File.Copy(pngPath, outputPath);
                }
                catch (Exception ex)
                {
                    return (ModItemResult.MoveImage_Failed, ex.Message, null);
                }
            }
            try
            {
                item.UpdateValue("ThumbTex", outputName);
            }
            catch (Exception ex)
            {
                return (ModItemResult.CSVUpdate_Failed, ex.Message, null);
            }

            if (pngPath == null)
            {
                return (ModItemResult.ExtracImage_Finish, outputName, pngPath);
            }
            else
            {
                return (ModItemResult.MoveImage_Finish, outputName, pngPath);
            }
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

        private void AppendLog(string message, System.Drawing.Color color)
        {
            if (richTextBox1.InvokeRequired)
            {
                richTextBox1.Invoke(new Action(() => AppendLog(message, color)));
            }
            else
            {
                richTextBox1.SelectionStart = richTextBox1.TextLength;
                richTextBox1.SelectionLength = 0;
                richTextBox1.SelectionColor = color;
                richTextBox1.AppendText(message + Environment.NewLine);
                richTextBox1.ScrollToCaret();
            }
        }

        public static string GenerateUniqueFileName(string outputFolder, string baseName, string extension)
        {
            string sanitizedBaseName = SanitizeFileName(baseName);
            string fullName = $"{sanitizedBaseName}{extension}";
            string fullPath = Path.Combine(outputFolder, fullName);
            if (!File.Exists(fullPath))
            {
                return fullName;
            }
            int counter = 1;
            do
            {
                fullName = $"{sanitizedBaseName}({counter}){extension}";
                fullPath = Path.Combine(outputFolder, fullName);
                counter++;
            } while (File.Exists(fullPath) && counter < 1000);

            if (counter == 1000)
            {
                fullName = $"{sanitizedBaseName}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
            }
            return fullName;
        }

        public static (ModItemResult, string) ExtractPngFromUnityFile(string unityFilePath, string textureName, string outputPath)
        {
            Texture2D foundTexture = null;
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
                            foundTexture = texture;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return (ModItemResult.UnityFileLoad_Failed, ex.Message);
            }
            if (foundTexture == null)
            {
                return (ModItemResult.UnityTexture_NotFound, "Texture Not Found");
            }

            ImageFormat type = ImageFormat.Png;
            var image = foundTexture.ConvertToImage(true);
            if (image == null)
                return (ModItemResult.Texture2Image_Failed, "Failed to ConvertToImage");

            try
            {
                using (var file = File.OpenWrite(outputPath))
                {
                    image.WriteToStream(file, type);
                }
                return (ModItemResult.ExtracImage_Success, "");
            }
            catch (Exception ex)
            {
                return (ModItemResult.SaveImage_Failed, ex.Message);
            }
        }

        public static string SanitizeFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "untitled";
            }
            string invalid = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            string cleanName = Regex.Replace(fileName, string.Format("[{0}]", Regex.Escape(invalid)), "_");
            cleanName = Regex.Replace(cleanName, @"^[\s.]+", "");
            cleanName = Regex.Replace(cleanName, @"[\s.]+$", "");
            if (string.IsNullOrWhiteSpace(cleanName))
            {
                return "untitled";
            }
            if (cleanName.Length > 255)
            {
                cleanName = cleanName.Substring(0, 255);
            }
            return cleanName;
        }

        public string ExtractToTempDirectory(string zipFilePath)
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
                AppendLog($"Error extracting ZIP file: {ex.Message} ({zipFilePath})", System.Drawing.Color.Red);
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

        public static Image CreateIndependentImage(string filePath)
        {
            using (var originalImage = Image.FromFile(filePath))
            {
                var independentImage = new Bitmap(originalImage.Width, originalImage.Height,
                                                  originalImage.PixelFormat);

                using (var g = Graphics.FromImage(independentImage))
                {
                    g.DrawImage(originalImage, 0, 0, originalImage.Width, originalImage.Height);
                }

                return independentImage;
            }
        }

        private void splitContainer2_Panel2_Paint(object sender, PaintEventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void progressBar2_Click(object sender, EventArgs e)
        {

        }
    }
}
