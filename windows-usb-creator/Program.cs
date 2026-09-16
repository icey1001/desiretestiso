using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DesireUsbCreator
{
    internal class Program
    {
        [STAThread]
        public static void Main()
        {
            try
            {
                if (!IsAdministrator())
                {
                    RestartElevated();
                    return;
                }

                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs args)
                {
                    try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesireUSB-crash.log"), DateTime.Now + " " + args.ExceptionObject + Environment.NewLine); }
                    catch { }
                };

                Application app = new Application();
                DesireWindow window = new DesireWindow();
                app.Run(window.Window);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Desire USB Creator", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsAdministrator()
        {
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static void RestartElevated()
        {
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = exe;
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            try { Process.Start(psi); }
            catch { }
        }
    }

    internal sealed class DesireWindow
    {
        public Window Window { get; private set; }

        private TextBox isoPathBox;
        private ComboBox usbCombo;
        private TextBlock usbDetailsText;
        private TextBlock isoStatusText;
        private TextBlock isoReadyStatus;
        private TextBlock usbReadyStatus;
        private TextBlock sizeReadyStatus;
        private TextBlock stageText;
        private TextBlock progressText;
        private TextBox logBox;
        private ProgressBar progressBar;
        private Button flashButton;
        private Button buildIsoButton;
        private CheckBox debugToggle;
        private CheckBox verifyCheck;
        private Slider zoomSlider;
        private Grid scaleRoot;
        private Ellipse cursorGlow;
        private Ellipse cursorDot;
        private Image logoImage;

        private readonly List<LogEntry> logs = new List<LogEntry>();
        private readonly object logLock = new object();
        private DiskInfo selectedDisk;
        private Process buildProcess;
        private bool autoFlashAfterBuild;
        private bool busy;

        public DesireWindow()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string xamlPath = Path.Combine(baseDir, "MainWindow.xaml");
            if (!File.Exists(xamlPath))
                throw new FileNotFoundException("MainWindow.xaml is missing next to DesireUSB.exe.", xamlPath);

            using (FileStream fs = File.OpenRead(xamlPath))
            {
                Window = (Window)XamlReader.Load(fs);
            }

            BindControls();
            WireEvents();
            LoadLogo();
            AddLog("Desire USB Creator started with administrator privileges.", false);
            AddLog("System disk protection is enabled; only USB disks are listed.", false);
            AutoDetectIso();
            RefreshUsbList();
            RefreshPreflight();
        }

        private T Find<T>(string name) where T : class
        {
            return Window.FindName(name) as T;
        }

        private void BindControls()
        {
            isoPathBox = Find<TextBox>("IsoPathBox");
            usbCombo = Find<ComboBox>("UsbCombo");
            usbDetailsText = Find<TextBlock>("UsbDetailsText");
            isoStatusText = Find<TextBlock>("IsoStatusText");
            isoReadyStatus = Find<TextBlock>("IsoReadyStatus");
            usbReadyStatus = Find<TextBlock>("UsbReadyStatus");
            sizeReadyStatus = Find<TextBlock>("SizeReadyStatus");
            stageText = Find<TextBlock>("StageText");
            progressText = Find<TextBlock>("ProgressText");
            logBox = Find<TextBox>("LogBox");
            progressBar = Find<ProgressBar>("ProgressBar");
            flashButton = Find<Button>("FlashButton");
            buildIsoButton = Find<Button>("BuildIsoButton");
            debugToggle = Find<CheckBox>("DebugToggle");
            verifyCheck = Find<CheckBox>("VerifyCheck");
            zoomSlider = Find<Slider>("ZoomSlider");
            scaleRoot = Find<Grid>("ScaleRoot");
            cursorGlow = Find<Ellipse>("CursorGlow");
            cursorDot = Find<Ellipse>("CursorDot");
            logoImage = Find<Image>("LogoImage");
        }

        private void WireEvents()
        {
            Find<Button>("BrowseIsoButton").Click += BrowseIso_Click;
            Find<Button>("RefreshUsbButton").Click += delegate { RefreshUsbList(); };
            Find<Button>("ClearLogButton").Click += delegate { lock (logLock) logs.Clear(); RefreshLog(); };
            buildIsoButton.Click += delegate { StartBuild(false); };
            flashButton.Click += FlashButton_Click;
            usbCombo.SelectionChanged += UsbCombo_SelectionChanged;
            isoPathBox.TextChanged += delegate { RefreshPreflight(); };
            debugToggle.Checked += delegate { RefreshLog(); };
            debugToggle.Unchecked += delegate { RefreshLog(); };
            zoomSlider.ValueChanged += ZoomSlider_ValueChanged;
            Window.MouseMove += Window_MouseMove;
            Window.Closing += Window_Closing;
        }

        private void LoadLogo()
        {
            try
            {
                string logo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo.png");
                if (File.Exists(logo))
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(logo, UriKind.Absolute);
                    image.EndInit();
                    logoImage.Source = image;
                }
            }
            catch (Exception ex)
            {
                AddLog("Logo load warning: " + ex.Message, true);
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                Point p = e.GetPosition(Window);
                Canvas.SetLeft(cursorGlow, p.X - cursorGlow.Width / 2.0);
                Canvas.SetTop(cursorGlow, p.Y - cursorGlow.Height / 2.0);
                Canvas.SetLeft(cursorDot, p.X - cursorDot.Width / 2.0);
                Canvas.SetTop(cursorDot, p.Y - cursorDot.Height / 2.0);
            }
            catch { }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (scaleRoot == null) return;
            ScaleTransform scale = scaleRoot.LayoutTransform as ScaleTransform;
            if (scale == null)
            {
                scale = new ScaleTransform(1, 1);
                scaleRoot.LayoutTransform = scale;
            }
            scale.ScaleX = e.NewValue;
            scale.ScaleY = e.NewValue;
        }

        private void BrowseIso_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "ISO images (*.iso)|*.iso|All files (*.*)|*.*";
            dlg.Title = "Select DesireVFIO ISO";
            if (dlg.ShowDialog(Window) == true)
            {
                isoPathBox.Text = dlg.FileName;
                AddLog("Selected ISO: " + dlg.FileName, false);
            }
        }

        private void UsbCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedDisk = usbCombo.SelectedItem as DiskInfo;
            if (selectedDisk == null)
            {
                usbDetailsText.Text = "No USB selected.";
            }
            else
            {
                usbDetailsText.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    "Disk {0}\n{1}\nSize: {2}\nSerial: {3}\nMounted volumes: {4}\nDevice: {5}",
                    selectedDisk.Index,
                    selectedDisk.Model,
                    FormatBytes(selectedDisk.Size),
                    string.IsNullOrWhiteSpace(selectedDisk.Serial) ? "(not reported)" : selectedDisk.Serial,
                    selectedDisk.DriveLetters.Count == 0 ? "(none)" : string.Join(", ", selectedDisk.DriveLetters.ToArray()),
                    selectedDisk.DeviceId);
                AddLog("USB selected: " + selectedDisk.DisplayName, false);
            }
            RefreshPreflight();
        }

        private void RefreshUsbList()
        {
            if (busy) return;
            try
            {
                int? oldIndex = selectedDisk == null ? (int?)null : selectedDisk.Index;
                List<DiskInfo> disks = DiskScanner.GetUsbDisks();
                usbCombo.ItemsSource = disks;
                selectedDisk = null;
                if (oldIndex.HasValue)
                {
                    DiskInfo match = disks.FirstOrDefault(d => d.Index == oldIndex.Value);
                    if (match != null) usbCombo.SelectedItem = match;
                }
                if (usbCombo.SelectedItem == null && disks.Count == 1)
                    usbCombo.SelectedIndex = 0;
                if (disks.Count == 0)
                    AddLog("No eligible USB disks detected. Insert a USB drive and press Refresh.", false);
                else
                    AddLog("Detected " + disks.Count + " eligible USB disk(s).", true);
                RefreshPreflight();
            }
            catch (Exception ex)
            {
                AddLog("USB scan failed: " + ex.Message, false);
                MessageBox.Show(Window, "USB scan failed:\n\n" + ex.Message, "Desire USB Creator", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoDetectIso()
        {
            string found = FindIso();
            if (!string.IsNullOrEmpty(found))
            {
                isoPathBox.Text = found;
                AddLog("Auto-detected ISO: " + found, false);
            }
            else
            {
                isoPathBox.Text = string.Empty;
                AddLog("No local DesireVFIO ISO found yet. Get ISO can download the configured prebuilt release.", false);
            }
        }

        private string FindIso()
        {
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DesireVFIO", "cache");
            if (Directory.Exists(cacheDir))
            {
                string[] cached = Directory.GetFiles(cacheDir, "*.iso", SearchOption.TopDirectoryOnly);
                string preferredCached = cached.FirstOrDefault(p => Path.GetFileName(p).IndexOf("DesireVFIO", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrEmpty(preferredCached)) return preferredCached;
                if (cached.Length > 0) return cached[0];
            }

            string current = AppDomain.CurrentDomain.BaseDirectory;
            DirectoryInfo dir = new DirectoryInfo(current);
            for (int level = 0; level < 5 && dir != null; level++, dir = dir.Parent)
            {
                string payloadDir = Path.Combine(dir.FullName, "payload");
                if (Directory.Exists(payloadDir))
                {
                    string[] payload = Directory.GetFiles(payloadDir, "*.iso", SearchOption.TopDirectoryOnly);
                    string preferredPayload = payload.FirstOrDefault(p => Path.GetFileName(p).IndexOf("DesireVFIO", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!string.IsNullOrEmpty(preferredPayload)) return preferredPayload;
                    if (payload.Length > 0) return payload[0];
                }
                string direct = Path.Combine(dir.FullName, "DesireVFIO-Fedora-44-KDE-x86_64.iso");
                if (File.Exists(direct)) return direct;
                string outDir = Path.Combine(dir.FullName, "out-windows");
                if (Directory.Exists(outDir))
                {
                    string[] matches = Directory.GetFiles(outDir, "*.iso", SearchOption.TopDirectoryOnly);
                    string preferred = matches.FirstOrDefault(p => Path.GetFileName(p).IndexOf("DesireVFIO", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!string.IsNullOrEmpty(preferred)) return preferred;
                    if (matches.Length > 0) return matches[0];
                }
                string outLinux = Path.Combine(dir.FullName, "out");
                if (Directory.Exists(outLinux))
                {
                    string[] matches = Directory.GetFiles(outLinux, "*.iso", SearchOption.TopDirectoryOnly);
                    string preferred = matches.FirstOrDefault(p => Path.GetFileName(p).IndexOf("DesireVFIO", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!string.IsNullOrEmpty(preferred)) return preferred;
                }
            }
            return null;
        }

        private string FindReleaseManifest()
        {
            DirectoryInfo dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (int level = 0; level < 6 && dir != null; level++, dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "release.ini");
                if (File.Exists(path)) return path;
                string nested = Path.Combine(dir.FullName, "windows-usb-creator", "release.ini");
                if (File.Exists(nested)) return nested;
            }
            return null;
        }

        private Dictionary<string, string> ReadReleaseManifest()
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = FindReleaseManifest();
            if (string.IsNullOrEmpty(path)) return values;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return values;
        }

        private string GetReleaseValue(Dictionary<string, string> values, string key, string fallback)
        {
            string value;
            return values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
        }

        private string ComputeFileSha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private void StartBuild(bool flashAfter)
        {
            // Kept under the original method name so older UI wiring continues to work.
            if (busy) return;

            string existing = FindIso();
            if (!string.IsNullOrEmpty(existing))
            {
                isoPathBox.Text = existing;
                AddLog("Using local DesireVFIO ISO: " + existing, false);
                if (flashAfter) BeginFlash();
                return;
            }

            Dictionary<string, string> release = ReadReleaseManifest();
            string url = GetReleaseValue(release, "download_url", string.Empty);
            string imageName = GetReleaseValue(release, "image_name", "DesireVFIO-Fedora-44-KDE-x86_64.iso");
            string expectedSha = GetReleaseValue(release, "sha256", string.Empty).Replace(" ", string.Empty).ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show(Window,
                    "No prebuilt DesireVFIO image is bundled and release.ini does not contain a download_url.\n\n" +
                    "For a completely plain-Windows experience, distribute this app with the ISO under the payload folder, or set download_url in release.ini to your hosted prebuilt DesireVFIO ISO.\n\n" +
                    "You can also press Browse and select an ISO manually.",
                    "DesireVFIO image source not configured", MessageBoxButton.OK, MessageBoxImage.Information);
                AddLog("No local ISO and no release download URL is configured.", false);
                return;
            }

            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DesireVFIO", "cache");
            Directory.CreateDirectory(cacheDir);
            string destination = Path.Combine(cacheDir, imageName);
            string partial = destination + ".partial";
            try { if (File.Exists(partial)) File.Delete(partial); } catch { }

            autoFlashAfterBuild = flashAfter;
            busy = true;
            SetControlsEnabled(false);
            buildIsoButton.IsEnabled = false;
            flashButton.IsEnabled = false;
            stageText.Text = "Downloading DesireVFIO image...";
            progressBar.IsIndeterminate = false;
            progressBar.Value = 0;
            progressText.Text = "0%";
            AddLog("Downloading prebuilt DesireVFIO image from configured release source.", false);
            AddLog("URL: " + url, true);

            System.Net.WebClient client = new System.Net.WebClient();
            client.Headers.Add("User-Agent", "DesireUSB/1.0");
            client.DownloadProgressChanged += delegate(object sender, System.Net.DownloadProgressChangedEventArgs e)
            {
                Window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    progressBar.Value = e.ProgressPercentage;
                    progressText.Text = e.ProgressPercentage + "%";
                    stageText.Text = "Downloading DesireVFIO image...";
                }));
            };
            client.DownloadFileCompleted += delegate(object sender, AsyncCompletedEventArgs e)
            {
                Window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        if (e.Cancelled || e.Error != null)
                            throw e.Error ?? new Exception("Image download was cancelled.");

                        stageText.Text = "Verifying downloaded image...";
                        progressBar.IsIndeterminate = true;
                        if (!string.IsNullOrWhiteSpace(expectedSha))
                        {
                            string actual = ComputeFileSha256(partial);
                            AddLog("Downloaded ISO SHA-256: " + actual, true);
                            if (!actual.Equals(expectedSha, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("The downloaded ISO SHA-256 does not match release.ini.");
                        }

                        if (File.Exists(destination)) File.Delete(destination);
                        File.Move(partial, destination);
                        isoPathBox.Text = destination;
                        stageText.Text = "DesireVFIO image ready";
                        progressText.Text = "READY";
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = 100;
                        AddLog("Prebuilt DesireVFIO image is ready: " + destination, false);
                        busy = false;
                        SetControlsEnabled(true);
                        RefreshPreflight();
                        if (autoFlashAfterBuild) BeginFlash();
                    }
                    catch (Exception ex)
                    {
                        busy = false;
                        progressBar.IsIndeterminate = false;
                        progressBar.Value = 0;
                        progressText.Text = "ERROR";
                        stageText.Text = "Image download failed";
                        SetControlsEnabled(true);
                        try { if (File.Exists(partial)) File.Delete(partial); } catch { }
                        AddLog("Image download failed: " + ex.Message, false);
                        MessageBox.Show(Window, ex.Message, "Desire USB Creator", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        client.Dispose();
                    }
                }));
            };

            try
            {
                client.DownloadFileAsync(new Uri(url), partial);
            }
            catch (Exception ex)
            {
                client.Dispose();
                busy = false;
                SetControlsEnabled(true);
                AddLog("Unable to start image download: " + ex.Message, false);
                MessageBox.Show(Window, ex.Message, "Unable to download DesireVFIO", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FlashButton_Click(object sender, RoutedEventArgs e)
        {
            if (busy) return;
            if (!File.Exists(isoPathBox.Text.Trim()))
            {
                MessageBoxResult result = MessageBox.Show(Window,
                    "The DesireVFIO ISO is not available locally yet.\n\nGet the configured prebuilt image now, then continue directly to USB flashing?",
                    "Get DesireVFIO", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                    StartBuild(true);
                return;
            }
            BeginFlash();
        }

        private void BeginFlash()
        {
            if (busy) return;
            string isoPath = isoPathBox.Text.Trim();
            if (!File.Exists(isoPath))
            {
                MessageBox.Show(Window, "Select or build a valid ISO first.", "Desire USB Creator", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (selectedDisk == null)
            {
                MessageBox.Show(Window, "Select the USB drive you want to overwrite.", "Desire USB Creator", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DiskInfo liveDisk = DiskScanner.GetUsbDisks().FirstOrDefault(d => d.Index == selectedDisk.Index);
            if (liveDisk == null || !liveDisk.MatchesIdentity(selectedDisk))
            {
                MessageBox.Show(Window,
                    "The selected USB device changed or was removed. Press Refresh and select it again.",
                    "Safety check failed", MessageBoxButton.OK, MessageBoxImage.Stop);
                RefreshUsbList();
                return;
            }

            long isoSize = new FileInfo(isoPath).Length;
            if (isoSize <= 0 || isoSize > liveDisk.Size)
            {
                MessageBox.Show(Window,
                    "The selected USB is not large enough for this ISO.",
                    "Safety check failed", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            string volumes = liveDisk.DriveLetters.Count == 0 ? "(none)" : string.Join(", ", liveDisk.DriveLetters.ToArray());
            string warning = string.Format(CultureInfo.InvariantCulture,
                "ALL DATA ON THIS USB WILL BE ERASED.\n\nDisk {0}: {1}\nSize: {2}\nMounted volumes: {3}\n\nISO: {4}\nISO size: {5}\n\nContinue?",
                liveDisk.Index, liveDisk.Model, FormatBytes(liveDisk.Size), volumes, Path.GetFileName(isoPath), FormatBytes(isoSize));

            MessageBoxResult first = MessageBox.Show(Window, warning, "Erase selected USB?", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (first != MessageBoxResult.Yes) return;

            MessageBoxResult second = MessageBox.Show(Window,
                "FINAL CONFIRMATION\n\nThis will overwrite PhysicalDrive" + liveDisk.Index + " (" + liveDisk.Model + ").\n\nThere is no undo. Write the DesireVFIO ISO now?",
                "Final USB write confirmation", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (second != MessageBoxResult.Yes) return;

            selectedDisk = liveDisk;
            bool verify = verifyCheck.IsChecked == true;
            StartFlashWorker(isoPath, liveDisk, verify);
        }

        private void StartFlashWorker(string isoPath, DiskInfo disk, bool verify)
        {
            busy = true;
            SetControlsEnabled(false);
            flashButton.IsEnabled = false;
            progressBar.IsIndeterminate = false;
            progressBar.Value = 0;
            progressText.Text = "0%";
            stageText.Text = "Preparing USB...";
            AddLog("Final safety check passed for Disk " + disk.Index + " (" + disk.Model + ").", false);
            AddLog("Beginning raw image write to " + disk.DeviceId + ".", false);

            BackgroundWorker worker = new BackgroundWorker();
            worker.WorkerReportsProgress = true;
            worker.DoWork += delegate(object sender, DoWorkEventArgs e)
            {
                BackgroundWorker bw = (BackgroundWorker)sender;
                FlashRequest req = (FlashRequest)e.Argument;
                e.Result = RawImager.WriteAndVerify(req.IsoPath, req.Disk, req.Verify, delegate(int percent, string stage, string detail)
                {
                    bw.ReportProgress(percent, new ProgressInfo(stage, detail));
                });
            };
            worker.ProgressChanged += delegate(object sender, ProgressChangedEventArgs e)
            {
                progressBar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                progressText.Text = e.ProgressPercentage + "%";
                ProgressInfo info = e.UserState as ProgressInfo;
                if (info != null)
                {
                    stageText.Text = info.Stage;
                    if (!string.IsNullOrEmpty(info.Detail)) AddLog(info.Detail, true);
                }
            };
            worker.RunWorkerCompleted += delegate(object sender, RunWorkerCompletedEventArgs e)
            {
                busy = false;
                SetControlsEnabled(true);
                if (e.Error != null)
                {
                    progressBar.Value = 0;
                    progressText.Text = "ERROR";
                    stageText.Text = "Flash failed";
                    AddLog("USB write failed: " + e.Error.Message, false);
                    MessageBox.Show(Window,
                        "The USB write failed. No other disk was touched.\n\n" + e.Error.Message + "\n\nClose File Explorer windows using the USB, unplug/replug it if needed, then try again.",
                        "Desire USB Creator", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    FlashResult result = e.Result as FlashResult;
                    progressBar.Value = 100;
                    progressText.Text = "100%";
                    stageText.Text = "USB ready";
                    if (result != null && result.Verified)
                    {
                        AddLog("Verification passed. SHA-256: " + result.ImageHash, false);
                    }
                    else
                    {
                        AddLog("Image write completed.", false);
                    }
                    MessageBox.Show(Window,
                        "DesireVFIO was written successfully to the selected USB." + (result != null && result.Verified ? "\n\nVerification passed." : "") + "\n\nYou can now reboot and select the USB from your UEFI boot menu.",
                        "USB ready", MessageBoxButton.OK, MessageBoxImage.Information);
                    RefreshUsbList();
                }
            };

            worker.RunWorkerAsync(new FlashRequest(isoPath, disk, verify));
        }

        private void SetControlsEnabled(bool enabled)
        {
            isoPathBox.IsEnabled = enabled;
            usbCombo.IsEnabled = enabled;
            Find<Button>("BrowseIsoButton").IsEnabled = enabled;
            Find<Button>("RefreshUsbButton").IsEnabled = enabled;
            buildIsoButton.IsEnabled = enabled;
            flashButton.IsEnabled = enabled;
            verifyCheck.IsEnabled = enabled;
        }

        private void RefreshPreflight()
        {
            string iso = isoPathBox == null ? string.Empty : isoPathBox.Text.Trim();
            bool isoOk = File.Exists(iso);
            if (isoOk)
            {
                long len = new FileInfo(iso).Length;
                isoStatusText.Text = "Ready • " + FormatBytes(len) + " • " + Path.GetFileName(iso);
                isoStatusText.Foreground = BrushFrom("#56E0A0");
                isoReadyStatus.Text = "● ISO ready";
                isoReadyStatus.Foreground = BrushFrom("#56E0A0");
            }
            else
            {
                Dictionary<string, string> release = ReadReleaseManifest();
                string url = GetReleaseValue(release, "download_url", string.Empty);
                bool canAcquire = !string.IsNullOrWhiteSpace(url);
                isoStatusText.Text = canAcquire ? "ISO not local • Get ISO can download the prebuilt release" : "Select a .iso or configure release.ini";
                isoStatusText.Foreground = BrushFrom("#FFC45E");
                isoReadyStatus.Text = canAcquire ? "● Prebuilt ISO available to download" : "● ISO source not configured";
                isoReadyStatus.Foreground = BrushFrom("#FFC45E");
            }

            if (selectedDisk != null)
            {
                usbReadyStatus.Text = "● USB target selected: Disk " + selectedDisk.Index;
                usbReadyStatus.Foreground = BrushFrom("#56E0A0");
            }
            else
            {
                usbReadyStatus.Text = "● Select a USB";
                usbReadyStatus.Foreground = BrushFrom("#FFC45E");
            }

            if (isoOk && selectedDisk != null)
            {
                long isoSize = new FileInfo(iso).Length;
                bool enough = selectedDisk.Size >= isoSize;
                sizeReadyStatus.Text = enough ? "● USB is large enough" : "● USB is too small";
                sizeReadyStatus.Foreground = BrushFrom(enough ? "#56E0A0" : "#FF657E");
            }
            else
            {
                sizeReadyStatus.Text = "● Size check pending";
                sizeReadyStatus.Foreground = BrushFrom("#A4A0BA");
            }
        }

        private Brush BrushFrom(string hex)
        {
            return (Brush)new BrushConverter().ConvertFromString(hex);
        }

        private void AddLog(string text, bool debugOnly)
        {
            lock (logLock)
            {
                logs.Add(new LogEntry(DateTime.Now, text, debugOnly));
                if (logs.Count > 2000) logs.RemoveRange(0, 500);
            }
            RefreshLog();
        }

        private void DispatchLog(string text, bool debugOnly)
        {
            Window.Dispatcher.BeginInvoke(new Action(delegate { AddLog(text, debugOnly); }));
        }

        private void RefreshLog()
        {
            if (logBox == null) return;
            bool showDebug = debugToggle != null && debugToggle.IsChecked == true;
            StringBuilder sb = new StringBuilder();
            lock (logLock)
            {
                foreach (LogEntry entry in logs)
                {
                    if (entry.DebugOnly && !showDebug) continue;
                    sb.Append('[').Append(entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("] ");
                    if (entry.DebugOnly) sb.Append("DEBUG  ");
                    sb.AppendLine(entry.Text);
                }
            }
            logBox.Text = sb.ToString();
            logBox.ScrollToEnd();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (busy)
            {
                MessageBoxResult result = MessageBox.Show(Window,
                    "A build or USB write is still running. Closing now may leave the USB incomplete.\n\nClose anyway?",
                    "Operation in progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                    e.Cancel = true;
            }
        }

        public static string FormatBytes(long bytes)
        {
            double value = bytes;
            string[] units = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (value >= 1024 && i < units.Length - 1)
            {
                value /= 1024;
                i++;
            }
            return value.ToString(value >= 10 || i == 0 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " " + units[i];
        }
    }

    internal sealed class LogEntry
    {
        public DateTime Time;
        public string Text;
        public bool DebugOnly;
        public LogEntry(DateTime time, string text, bool debugOnly) { Time = time; Text = text; DebugOnly = debugOnly; }
    }

    internal sealed class ProgressInfo
    {
        public string Stage;
        public string Detail;
        public ProgressInfo(string stage, string detail) { Stage = stage; Detail = detail; }
    }

    internal sealed class FlashRequest
    {
        public string IsoPath;
        public DiskInfo Disk;
        public bool Verify;
        public FlashRequest(string isoPath, DiskInfo disk, bool verify) { IsoPath = isoPath; Disk = disk; Verify = verify; }
    }

    internal sealed class FlashResult
    {
        public bool Verified;
        public string ImageHash;
    }

    internal sealed class DiskInfo
    {
        public int Index;
        public string Model;
        public string Serial;
        public string DeviceId;
        public long Size;
        public List<string> DriveLetters = new List<string>();

        public string DisplayName
        {
            get { return string.Format(CultureInfo.InvariantCulture, "Disk {0} — {1} — {2}", Index, Model, DesireWindow.FormatBytes(Size)); }
        }

        public bool MatchesIdentity(DiskInfo other)
        {
            if (other == null) return false;
            if (Index != other.Index || Size != other.Size) return false;
            if (!string.Equals((Model ?? string.Empty).Trim(), (other.Model ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrWhiteSpace(Serial) && !string.IsNullOrWhiteSpace(other.Serial) &&
                !string.Equals(Serial.Trim(), other.Serial.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }

    internal static class DiskScanner
    {
        public static List<DiskInfo> GetUsbDisks()
        {
            int systemIndex = GetSystemDiskIndex();
            List<DiskInfo> result = new List<DiskInfo>();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Index,Model,Size,SerialNumber,InterfaceType,PNPDeviceID,DeviceID FROM Win32_DiskDrive"))
            {
                foreach (ManagementObject disk in searcher.Get())
                {
                    string iface = Convert.ToString(disk["InterfaceType"], CultureInfo.InvariantCulture) ?? string.Empty;
                    string pnp = Convert.ToString(disk["PNPDeviceID"], CultureInfo.InvariantCulture) ?? string.Empty;
                    bool usb = iface.Equals("USB", StringComparison.OrdinalIgnoreCase) || pnp.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!usb) continue;

                    int index = Convert.ToInt32(disk["Index"], CultureInfo.InvariantCulture);
                    if (index == systemIndex) continue;

                    long size = 0;
                    object rawSize = disk["Size"];
                    if (rawSize != null) long.TryParse(Convert.ToString(rawSize, CultureInfo.InvariantCulture), out size);
                    if (size <= 0) continue;

                    DiskInfo info = new DiskInfo();
                    info.Index = index;
                    info.Model = (Convert.ToString(disk["Model"], CultureInfo.InvariantCulture) ?? "USB Disk").Trim();
                    info.Serial = (Convert.ToString(disk["SerialNumber"], CultureInfo.InvariantCulture) ?? string.Empty).Trim();
                    info.DeviceId = Convert.ToString(disk["DeviceID"], CultureInfo.InvariantCulture) ?? ("\\\\.\\PhysicalDrive" + index);
                    info.Size = size;
                    info.DriveLetters = GetDriveLetters(index);
                    result.Add(info);
                }
            }
            return result.OrderBy(d => d.Index).ToList();
        }

        private static int GetSystemDiskIndex()
        {
            try
            {
                string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
                string drive = systemRoot.TrimEnd('\\');
                string logicalPath = "Win32_LogicalDisk.DeviceID=\"" + drive.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                using (ManagementObjectSearcher partitions = new ManagementObjectSearcher("ASSOCIATORS OF {" + logicalPath + "} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                {
                    foreach (ManagementObject partition in partitions.Get())
                    {
                        string rel = partition.Path.RelativePath;
                        using (ManagementObjectSearcher disks = new ManagementObjectSearcher("ASSOCIATORS OF {" + rel + "} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                        {
                            foreach (ManagementObject disk in disks.Get())
                                return Convert.ToInt32(disk["Index"], CultureInfo.InvariantCulture);
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        private static List<string> GetDriveLetters(int diskIndex)
        {
            List<string> letters = new List<string>();
            try
            {
                using (ManagementObjectSearcher partitions = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskPartition WHERE DiskIndex=" + diskIndex))
                {
                    foreach (ManagementObject partition in partitions.Get())
                    {
                        string rel = partition.Path.RelativePath;
                        using (ManagementObjectSearcher logicals = new ManagementObjectSearcher("ASSOCIATORS OF {" + rel + "} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                        {
                            foreach (ManagementObject logical in logicals.Get())
                            {
                                string id = Convert.ToString(logical["DeviceID"], CultureInfo.InvariantCulture);
                                if (!string.IsNullOrEmpty(id) && !letters.Contains(id)) letters.Add(id);
                            }
                        }
                    }
                }
            }
            catch { }
            return letters;
        }
    }

    internal static class RawImager
    {
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FSCTL_LOCK_VOLUME = 0x00090018;
        private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
        private const uint FSCTL_UNLOCK_VOLUME = 0x0009001C;
        private const int BufferSize = 4 * 1024 * 1024;

        public static FlashResult WriteAndVerify(string isoPath, DiskInfo disk, bool verify, Action<int, string, string> progress)
        {
            List<SafeFileHandle> lockedVolumes = new List<SafeFileHandle>();
            SafeFileHandle diskHandle = null;
            try
            {
                progress(1, "Locking USB volumes...", "Target: " + disk.DeviceId);
                LockAndDismountVolumes(disk, lockedVolumes, progress);

                diskHandle = NativeMethods.CreateFile(disk.DeviceId, GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (diskHandle == null || diskHandle.IsInvalid)
                    throw new IOException("Unable to open " + disk.DeviceId + " for raw writing. Windows error " + Marshal.GetLastWin32Error() + ".");

                long total = new FileInfo(isoPath).Length;
                byte[] buffer = new byte[BufferSize];
                using (FileStream input = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                using (FileStream output = new FileStream(diskHandle, FileAccess.ReadWrite, BufferSize, false))
                {
                    long written = 0;
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        written += read;
                        int pct = (int)Math.Min(80, Math.Max(2, (written * 80L) / total));
                        progress(pct, "Writing DesireVFIO...", "Wrote " + DesireWindow.FormatBytes(written) + " / " + DesireWindow.FormatBytes(total));
                    }
                    output.Flush(true);

                    FlashResult result = new FlashResult();
                    if (verify)
                    {
                        progress(82, "Verifying USB...", "Calculating SHA-256 of the ISO and flashed bytes.");
                        input.Position = 0;
                        output.Position = 0;
                        byte[] isoHash = ComputeHash(input, total, delegate(long done)
                        {
                            int pct = 82 + (int)((done * 8L) / total);
                            progress(Math.Min(90, pct), "Verifying source image...", null);
                        });
                        byte[] diskHash = ComputeHash(output, total, delegate(long done)
                        {
                            int pct = 90 + (int)((done * 9L) / total);
                            progress(Math.Min(99, pct), "Verifying flashed USB...", null);
                        });
                        if (!ByteArraysEqual(isoHash, diskHash))
                            throw new IOException("Verification failed: the USB bytes do not match the ISO SHA-256.");
                        result.Verified = true;
                        result.ImageHash = BitConverter.ToString(isoHash).Replace("-", string.Empty).ToLowerInvariant();
                    }
                    progress(100, "USB ready", "Raw image write completed successfully.");
                    return result;
                }
            }
            finally
            {
                foreach (SafeFileHandle handle in lockedVolumes)
                {
                    try
                    {
                        uint ignored;
                        NativeMethods.DeviceIoControl(handle, FSCTL_UNLOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out ignored, IntPtr.Zero);
                        handle.Close();
                    }
                    catch { }
                }
                if (diskHandle != null && !diskHandle.IsClosed)
                {
                    try { diskHandle.Close(); } catch { }
                }
            }
        }

        private static void LockAndDismountVolumes(DiskInfo disk, List<SafeFileHandle> handles, Action<int, string, string> progress)
        {
            foreach (string drive in disk.DriveLetters)
            {
                string path = "\\\\.\\" + drive;
                SafeFileHandle h = NativeMethods.CreateFile(path, GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == null || h.IsInvalid)
                    throw new IOException("Could not open volume " + drive + " for dismount. Close Explorer/windows using the USB and try again.");

                bool locked = false;
                for (int attempt = 0; attempt < 8 && !locked; attempt++)
                {
                    uint ignored;
                    locked = NativeMethods.DeviceIoControl(h, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out ignored, IntPtr.Zero);
                    if (!locked) Thread.Sleep(350);
                }
                if (!locked)
                {
                    h.Close();
                    throw new IOException("Windows could not lock volume " + drive + ". Close File Explorer and any program using the USB, then try again.");
                }

                uint result;
                if (!NativeMethods.DeviceIoControl(h, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out result, IntPtr.Zero))
                {
                    h.Close();
                    throw new IOException("Windows could not dismount volume " + drive + ".");
                }
                handles.Add(h);
                progress(1, "Locking USB volumes...", "Dismounted " + drive);
            }
        }

        private static byte[] ComputeHash(Stream stream, long count, Action<long> progress)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] buffer = new byte[BufferSize];
                long remaining = count;
                long done = 0;
                while (remaining > 0)
                {
                    int need = (int)Math.Min(buffer.Length, remaining);
                    int read = stream.Read(buffer, 0, need);
                    if (read <= 0) throw new EndOfStreamException("Unexpected end of device during verification.");
                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                    remaining -= read;
                    done += read;
                    if (progress != null) progress(done);
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);
                return sha.Hash;
            }
        }

        private static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }

    internal static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out uint lpBytesReturned,
            IntPtr lpOverlapped);
    }
}
