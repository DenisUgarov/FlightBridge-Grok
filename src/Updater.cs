using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FSMigrator {

public sealed class UpdateInfo {
 public string Version { get; set; }
 public string Notes { get; set; }
 public string AssetName { get; set; }
 public string Sha256 { get; set; }
 public string DownloadUrl { get; set; }
}

public sealed class UpdateResult {
 public bool Ok { get; set; }
 public string Message { get; set; }
 public bool RestartRequired { get; set; }
}

public sealed class UpdateSettings {
 public bool Enabled { get; set; }
 public string SkippedVersion { get; set; }
 public DateTime? RemindAfter { get; set; }
 public UpdateSettings() { Enabled = true; }
}

/// <summary>HTTP surface for updater tests and the default GitHub client.</summary>
public interface IUpdateHttp {
 string GetString(string url, IDictionary<string, string> headers, int timeoutMs);
 void Download(string url, string destinationPath, IDictionary<string, string> headers, int timeoutMs, IProgress<int> progress);
}

/// <summary>Optional instance contract used by the UI branch; static <see cref="Updater"/> facade wraps the same behaviour.</summary>
public interface IUpdater {
 Task<UpdateInfo> CheckAsync(UpdateSettings settings, bool manual = false);
 Task<UpdateResult> ApplyAsync(UpdateInfo info, IProgress<int> progress);
 void SkipVersion(UpdateInfo info);
 void RemindLater(int days = 7);
 UpdateSettings LoadSettings();
 void SaveSettings(UpdateSettings settings);
 void CleanupOnStart();
}

public sealed class DefaultUpdateHttp : IUpdateHttp {
 static DefaultUpdateHttp() {
  try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
 }

 public string GetString(string url, IDictionary<string, string> headers, int timeoutMs) {
  var req = Create(url, headers, timeoutMs);
  using (var resp = (HttpWebResponse)req.GetResponse())
  using (var stream = resp.GetResponseStream())
  using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
   return reader.ReadToEnd();
 }

 public void Download(string url, string destinationPath, IDictionary<string, string> headers, int timeoutMs, IProgress<int> progress) {
  var req = Create(url, headers, timeoutMs);
  using (var resp = (HttpWebResponse)req.GetResponse())
  using (var input = resp.GetResponseStream() ?? Stream.Null)
  using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
   long total = resp.ContentLength;
   var buffer = new byte[64 * 1024];
   long readTotal = 0;
   int n;
   int lastPct = -1;
   while ((n = input.Read(buffer, 0, buffer.Length)) > 0) {
    output.Write(buffer, 0, n);
    readTotal += n;
    if (progress != null && total > 0) {
     int pct = (int)Math.Max(0, Math.Min(100, (readTotal * 100) / total));
     if (pct != lastPct) { lastPct = pct; progress.Report(pct); }
    }
   }
   output.Flush(true);
   if (progress != null) progress.Report(100);
  }
 }

 static HttpWebRequest Create(string url, IDictionary<string, string> headers, int timeoutMs) {
  var req = (HttpWebRequest)WebRequest.Create(url);
  req.Method = "GET";
  req.Timeout = timeoutMs;
  req.ReadWriteTimeout = timeoutMs;
  req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
  if (headers != null) {
   foreach (var kv in headers) {
    if (kv.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)) req.Accept = kv.Value;
    else if (kv.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) req.UserAgent = kv.Value;
    else req.Headers[kv.Key] = kv.Value;
   }
  }
  return req;
 }
}

public sealed class Updater : IUpdater {
 static readonly string LatestUrl = "https://api.github.com/repos/" + ("Denis Ugarov".Replace(" ", "")) + "/FlightBridge-Grok/releases/latest";
 const int CheckTimeoutMs = 5000;
 const int DownloadTimeoutMs = 600000;
 const string FailMessage = "Обновление не удалось, программа работает как прежде";
 const string PortableAsset = "FlightBridge.exe";
 const string SetupAsset = "FlightBridge-Setup.exe";
 const string ShaAsset = "SHA256.txt";

 readonly IUpdateHttp http;
 readonly Func<DateTime> clock;
 readonly string settingsPath;
 readonly string exePath;
 readonly string installDir;
 static readonly Updater DefaultInstance = new Updater();

 public Updater(
  IUpdateHttp http = null,
  Func<DateTime> clock = null,
  string settingsPath = null,
  string exePath = null,
  string installDir = null,
  string settingsDirectory = null) {
  this.http = http ?? new DefaultUpdateHttp();
  this.clock = clock ?? (() => DateTime.UtcNow);
  this.exePath = string.IsNullOrWhiteSpace(exePath)
   ? (AssemblyLocation() ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "FlightBridge.exe"))
   : exePath;
  this.installDir = string.IsNullOrWhiteSpace(installDir) ? DefaultInstallDir() : installDir;
  if (!string.IsNullOrWhiteSpace(settingsPath)) this.settingsPath = settingsPath;
  else if (!string.IsNullOrWhiteSpace(settingsDirectory)) {
   Directory.CreateDirectory(settingsDirectory);
   this.settingsPath = Path.Combine(settingsDirectory, "settings");
  } else this.settingsPath = ResolveDefaultSettingsPath(this.exePath);
 }

 static string AssemblyLocation() {
  try {
   string loc = typeof(Updater).Assembly.Location;
   return string.IsNullOrWhiteSpace(loc) ? null : loc;
  } catch { return null; }
 }

 public static string DefaultInstallDir() {
  return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FlightBridge");
 }

 static string ResolveDefaultSettingsPath(string exe) {
  string exeDir = Path.GetDirectoryName(exe) ?? ".";
  if (IsDirectoryWritable(exeDir)) return Path.Combine(exeDir, "settings");
  string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
  string root = string.IsNullOrWhiteSpace(appData)
   ? exeDir
   : Path.Combine(appData, "FlightBridge");
  try { Directory.CreateDirectory(root); } catch { }
  return Path.Combine(root, "settings");
 }

 static bool IsDirectoryWritable(string dir) {
  try {
   if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
   string probe = Path.Combine(dir, ".fb-write-" + Guid.NewGuid().ToString("N"));
   File.WriteAllText(probe, "1");
   File.Delete(probe);
   return true;
  } catch { return false; }
 }

 public bool IsInstalled() {
  try {
   string dir = Path.GetFullPath(Path.GetDirectoryName(exePath) ?? "");
   string install = Path.GetFullPath(installDir ?? "");
   return string.Equals(dir, install, StringComparison.OrdinalIgnoreCase);
  } catch { return false; }
 }

 IDictionary<string, string> Headers() {
  return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
   { "User-Agent", "FlightBridge/" + CurrentVersion() },
   { "Accept", "application/vnd.github+json" }
  };
 }

 static string CurrentVersion() {
  try { return BuildInfo.Version; } catch { return "0.0.0"; }
 }

 public static int CompareVersions(string a, string b) {
  var pa = ParseVersion(a);
  var pb = ParseVersion(b);
  int n = Math.Max(pa.Length, pb.Length);
  for (int i = 0; i < n; i++) {
   int va = i < pa.Length ? pa[i] : 0;
   int vb = i < pb.Length ? pb[i] : 0;
   if (va != vb) return va.CompareTo(vb);
  }
  return 0;
 }

 static int[] ParseVersion(string v) {
  if (string.IsNullOrWhiteSpace(v)) return new int[] { 0 };
  v = v.Trim();
  if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) v = v.Substring(1);
  var parts = v.Split('.');
  var nums = new int[parts.Length];
  for (int i = 0; i < parts.Length; i++) {
   int n;
   string p = parts[i];
   int cut = 0;
   while (cut < p.Length && char.IsDigit(p[cut])) cut++;
   if (cut == 0 || !int.TryParse(p.Substring(0, cut), NumberStyles.None, CultureInfo.InvariantCulture, out n)) n = 0;
   nums[i] = n;
  }
  return nums;
 }

 // Static facade (defaults). Instance access for injection: cast to IUpdater or use instance helpers below.
 public static Task<UpdateInfo> CheckAsync(UpdateSettings settings, bool manual = false) {
  return DefaultInstance.CheckAsyncCore(settings, manual);
 }
 public static Task<UpdateResult> ApplyAsync(UpdateInfo info, IProgress<int> progress) {
  return DefaultInstance.ApplyAsyncCore(info, progress);
 }
 public static void CleanupOnStart() { DefaultInstance.CleanupOnStartCore(); }
 public static void SkipVersion(UpdateInfo info) { DefaultInstance.SkipVersionCore(info); }
 public static void RemindLater(int days = 7) { DefaultInstance.RemindLaterCore(days); }
 public static UpdateSettings LoadSettings() { return DefaultInstance.LoadSettingsCore(); }
 public static void SaveSettings(UpdateSettings settings) { DefaultInstance.SaveSettingsCore(settings); }

 Task<UpdateInfo> IUpdater.CheckAsync(UpdateSettings settings, bool manual) { return CheckAsyncCore(settings, manual); }
 Task<UpdateResult> IUpdater.ApplyAsync(UpdateInfo info, IProgress<int> progress) { return ApplyAsyncCore(info, progress); }
 void IUpdater.CleanupOnStart() { CleanupOnStartCore(); }
 void IUpdater.SkipVersion(UpdateInfo info) { SkipVersionCore(info); }
 void IUpdater.RemindLater(int days) { RemindLaterCore(days); }
 UpdateSettings IUpdater.LoadSettings() { return LoadSettingsCore(); }
 void IUpdater.SaveSettings(UpdateSettings settings) { SaveSettingsCore(settings); }

 // Public instance entry points for tests (same behaviour as IUpdater).
 public Task<UpdateInfo> CheckAsyncCore(UpdateSettings settings, bool manual = false) {
  try {
   if (settings == null) settings = LoadSettingsCore();
   if (!settings.Enabled && !manual) return Task.FromResult<UpdateInfo>(null);
   if (!manual && settings.RemindAfter.HasValue && settings.RemindAfter.Value > clock())
    return Task.FromResult<UpdateInfo>(null);

   string json = http.GetString(LatestUrl, Headers(), CheckTimeoutMs);
   if (string.IsNullOrWhiteSpace(json)) return Task.FromResult<UpdateInfo>(null);

   bool draft = JsonBool(json, "draft");
   bool prerelease = JsonBool(json, "prerelease");
   if (draft || prerelease) return Task.FromResult<UpdateInfo>(null);

   string tag = JsonString(json, "tag_name");
   if (string.IsNullOrWhiteSpace(tag)) return Task.FromResult<UpdateInfo>(null);
   string version = StripV(tag);
   if (CompareVersions(version, CurrentVersion()) <= 0) return Task.FromResult<UpdateInfo>(null);

   if (!manual && !string.IsNullOrEmpty(settings.SkippedVersion)
    && string.Equals(StripV(settings.SkippedVersion), version, StringComparison.OrdinalIgnoreCase))
    return Task.FromResult<UpdateInfo>(null);

   string assetName = IsInstalled() ? SetupAsset : PortableAsset;
   string downloadUrl = FindAssetUrl(json, assetName);
   string shaUrl = FindAssetUrl(json, ShaAsset);
   if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(shaUrl))
    return Task.FromResult<UpdateInfo>(null);

   string shaBody = http.GetString(shaUrl, Headers(), CheckTimeoutMs);
   string sha = FindSha256(shaBody, assetName);
   if (string.IsNullOrWhiteSpace(sha)) return Task.FromResult<UpdateInfo>(null);

   string notes = JsonString(json, "body") ?? "";
   return Task.FromResult(new UpdateInfo {
    Version = version,
    Notes = notes,
    AssetName = assetName,
    Sha256 = sha.ToLowerInvariant(),
    DownloadUrl = downloadUrl
   });
  } catch {
   return Task.FromResult<UpdateInfo>(null);
  }
 }

 public Task<UpdateResult> ApplyAsyncCore(UpdateInfo info, IProgress<int> progress) {
  try {
   if (info == null || string.IsNullOrWhiteSpace(info.DownloadUrl) || string.IsNullOrWhiteSpace(info.Sha256))
    return Task.FromResult(Fail());

   string temp = Path.Combine(Path.GetTempPath(), "FlightBridge-update-" + Guid.NewGuid().ToString("N") + ".tmp");
   try {
    http.Download(info.DownloadUrl, temp, Headers(), DownloadTimeoutMs, progress);
    string actual = HashFile(temp);
    if (!string.Equals(actual, info.Sha256.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase)) {
     TryDelete(temp);
     return Task.FromResult(Fail());
    }

    bool installed = string.Equals(info.AssetName, SetupAsset, StringComparison.OrdinalIgnoreCase) || IsInstalled();
    if (installed) {
     string setupPath = Path.Combine(Path.GetTempPath(), "FlightBridge-Setup-" + Guid.NewGuid().ToString("N") + ".exe");
     File.Delete(setupPath);
     File.Move(temp, setupPath);
     temp = null;
     Process.Start(new ProcessStartInfo(setupPath) { UseShellExecute = true });
     return Task.FromResult(new UpdateResult { Ok = true, Message = null, RestartRequired = true });
    }

    // Portable: swap exe in place.
    string target = exePath;
    string oldPath = target + ".old";
    TryDelete(oldPath);
    string backup = null;
    try {
     if (File.Exists(target)) {
      File.Move(target, oldPath);
      backup = oldPath;
     }
     File.Move(temp, target);
     temp = null;
     return Task.FromResult(new UpdateResult { Ok = true, Message = null, RestartRequired = true });
    } catch {
     try {
      if (backup != null && File.Exists(backup) && !File.Exists(target))
       File.Move(backup, target);
     } catch { }
     TryDelete(temp);
     return Task.FromResult(Fail());
    }
   } catch {
    TryDelete(temp);
    return Task.FromResult(Fail());
   }
  } catch {
   return Task.FromResult(Fail());
  }
 }

 static UpdateResult Fail() {
  return new UpdateResult { Ok = false, Message = FailMessage, RestartRequired = false };
 }

 public void CleanupOnStartCore() {
  try {
   string dir = Path.GetDirectoryName(exePath);
   if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
   foreach (string file in Directory.GetFiles(dir, "*.old")) {
    try { File.Delete(file); } catch { }
   }
  } catch { }
 }

 public void SkipVersionCore(UpdateInfo info) {
  if (info == null || string.IsNullOrWhiteSpace(info.Version)) return;
  var s = LoadSettings();
  s.SkippedVersion = StripV(info.Version);
  s.RemindAfter = null;
  SaveSettingsCore(s);
 }

 public void RemindLaterCore(int days = 7) {
  if (days < 0) days = 0;
  var s = LoadSettings();
  s.RemindAfter = clock().AddDays(days);
  SaveSettingsCore(s);
 }

 public UpdateSettings LoadSettingsCore() {
  var s = new UpdateSettings { Enabled = true };
  try {
   if (!File.Exists(settingsPath)) return s;
   foreach (var line in File.ReadAllLines(settingsPath)) {
    int i = line.IndexOf('=');
    if (i <= 0) continue;
    string key = line.Substring(0, i).Trim();
    string val = line.Substring(i + 1).Trim();
    if (key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
     s.Enabled = !(val.Equals("0") || val.Equals("false", StringComparison.OrdinalIgnoreCase));
    else if (key.Equals("SkippedVersion", StringComparison.OrdinalIgnoreCase))
     s.SkippedVersion = string.IsNullOrWhiteSpace(val) ? null : val;
    else if (key.Equals("RemindAfter", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(val)) {
     DateTime dt;
     if (DateTime.TryParse(val, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out dt))
      s.RemindAfter = dt;
    }
   }
  } catch { }
  return s;
 }

 public void SaveSettingsCore(UpdateSettings settings) {
  if (settings == null) throw new ArgumentNullException("settings");
  try {
   string dir = Path.GetDirectoryName(settingsPath);
   if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
   File.WriteAllText(settingsPath,
    "Enabled=" + (settings.Enabled ? "1" : "0") + "\r\n" +
    "SkippedVersion=" + (settings.SkippedVersion ?? "") + "\r\n" +
    "RemindAfter=" + (settings.RemindAfter.HasValue
     ? settings.RemindAfter.Value.ToString("o", CultureInfo.InvariantCulture) : "") + "\r\n");
  } catch { }
 }

 static string StripV(string v) {
  if (string.IsNullOrWhiteSpace(v)) return v;
  v = v.Trim();
  if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) return v.Substring(1);
  return v;
 }

 static string HashFile(string path) {
  using (var fs = File.OpenRead(path))
  using (var sha = SHA256.Create()) {
   byte[] hash = sha.ComputeHash(fs);
   var sb = new StringBuilder(hash.Length * 2);
   for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
   return sb.ToString();
  }
 }

 static void TryDelete(string path) {
  if (string.IsNullOrWhiteSpace(path)) return;
  try { if (File.Exists(path)) File.Delete(path); } catch { }
 }

 public static string FindSha256(string shaBody, string assetName) {
  if (string.IsNullOrWhiteSpace(shaBody) || string.IsNullOrWhiteSpace(assetName)) return null;
  using (var reader = new StringReader(shaBody)) {
   string line;
   while ((line = reader.ReadLine()) != null) {
    line = line.Trim();
    if (line.Length == 0) continue;
    // "<hex>  <filename>" (two spaces) or whitespace-separated
    int sep = line.IndexOf("  ", StringComparison.Ordinal);
    string hex, name;
    if (sep > 0) {
     hex = line.Substring(0, sep).Trim();
     name = line.Substring(sep + 2).Trim();
    } else {
     var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
     if (parts.Length < 2) continue;
     hex = parts[0];
     name = parts[parts.Length - 1];
    }
    if (name.Equals(assetName, StringComparison.OrdinalIgnoreCase)
     || name.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase)
     || name.EndsWith("\\" + assetName, StringComparison.OrdinalIgnoreCase)) {
     if (IsHex64(hex)) return hex.ToLowerInvariant();
    }
  }
  }
  return null;
 }

 static bool IsHex64(string s) {
  if (string.IsNullOrEmpty(s) || s.Length != 64) return false;
  for (int i = 0; i < s.Length; i++) {
   char c = s[i];
   if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
  }
  return true;
 }

 static string FindAssetUrl(string json, string assetName) {
  if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(assetName)) return null;
  int pos = 0;
  while (pos < json.Length) {
   int nameAt = IndexOfStringValue(json, "name", pos);
   if (nameAt < 0) return null;
   string name; int afterName;
   if (!TryReadStringValue(json, nameAt, out name, out afterName)) { pos = nameAt + 1; continue; }
   if (!string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)) { pos = afterName; continue; }
   // Search within a nearby window for browser_download_url (same asset object).
   int windowStart = Math.Max(0, nameAt - 40);
   int windowEnd = Math.Min(json.Length, afterName + 800);
   string window = json.Substring(windowStart, windowEnd - windowStart);
   string url = JsonString(window, "browser_download_url");
   if (!string.IsNullOrWhiteSpace(url)) return url;
   pos = afterName;
  }
  return null;
 }

 static int IndexOfStringValue(string json, string key, int start) {
  string pattern = "\"" + key + "\"";
  int at = start;
  while (at < json.Length) {
   int hit = json.IndexOf(pattern, at, StringComparison.Ordinal);
   if (hit < 0) return -1;
   int colon = json.IndexOf(':', hit + pattern.Length);
   if (colon < 0) return -1;
   int j = colon + 1;
   while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
   if (j < json.Length && json[j] == '"') return j;
   at = hit + 1;
  }
  return -1;
 }

 static bool TryReadStringValue(string json, int quoteIndex, out string value, out int after) {
  value = null; after = quoteIndex;
  if (quoteIndex < 0 || quoteIndex >= json.Length || json[quoteIndex] != '"') return false;
  var sb = new StringBuilder();
  int j = quoteIndex + 1;
  while (j < json.Length) {
   char c = json[j++];
   if (c == '\\') {
    if (j >= json.Length) break;
    char e = json[j++];
    if (e == 'u' && j + 4 <= json.Length) {
     int code;
     if (int.TryParse(json.Substring(j, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
      sb.Append((char)code);
     j += 4;
    } else if (e == 'n') sb.Append('\n');
    else if (e == 'r') sb.Append('\r');
    else if (e == 't') sb.Append('\t');
    else sb.Append(e);
   } else if (c == '"') { value = sb.ToString(); after = j; return true; }
   else sb.Append(c);
  }
  return false;
 }

 public static string JsonString(string json, string key) {
  if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return null;
  string pattern = "\"" + key + "\"";
  int i = 0;
  while (true) {
   int at = json.IndexOf(pattern, i, StringComparison.Ordinal);
   if (at < 0) return null;
   int colon = json.IndexOf(':', at + pattern.Length);
   if (colon < 0) return null;
   int j = colon + 1;
   while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
   if (j >= json.Length) return null;
   if (json[j] == 'n' && json.Substring(j).StartsWith("null", StringComparison.Ordinal)) return null;
   if (json[j] != '"') { i = at + 1; continue; }
   var sb = new StringBuilder();
   j++;
   while (j < json.Length) {
    char c = json[j++];
    if (c == '\\') {
     if (j >= json.Length) break;
     char e = json[j++];
     switch (e) {
      case '"': sb.Append('"'); break;
      case '\\': sb.Append('\\'); break;
      case '/': sb.Append('/'); break;
      case 'b': sb.Append('\b'); break;
      case 'f': sb.Append('\f'); break;
      case 'n': sb.Append('\n'); break;
      case 'r': sb.Append('\r'); break;
      case 't': sb.Append('\t'); break;
      case 'u':
       if (j + 4 <= json.Length) {
        int code;
        if (int.TryParse(json.Substring(j, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
         sb.Append((char)code);
        j += 4;
       }
       break;
      default: sb.Append(e); break;
     }
    } else if (c == '"') return sb.ToString();
    else sb.Append(c);
   }
   return sb.ToString();
  }
 }

 public static bool JsonBool(string json, string key) {
  if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key)) return false;
  string pattern = "\"" + key + "\"";
  int at = json.IndexOf(pattern, StringComparison.Ordinal);
  if (at < 0) return false;
  int colon = json.IndexOf(':', at + pattern.Length);
  if (colon < 0) return false;
  int j = colon + 1;
  while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
  if (j + 4 <= json.Length && json.Substring(j, 4).Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
  return false;
 }
}
}
