using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FSMigrator;

class UpdaterTests {
 static int count;
 static void Check(bool b, string n) { if (!b) throw new Exception(n); Console.WriteLine("PASS " + n); count++; }

 static int Main() {
  try {
   VersionCompare();
   DisabledZeroHttp();
   ManualBypasses();
   NewerDetected();
   SkippedAndNext();
   RemindAfterClock();
   PrereleaseDraftIgnored();
   HttpErrorsSilent();
   MissingShaEntry();
   HashMismatchUnchanged();
   InterruptedDownloadUnchanged();
   PortableSwapAndCleanup();
   SettingsRoundtrip();
   Console.WriteLine(count + " updater tests passed.");
   return 0;
  } catch (Exception ex) {
   Console.Error.WriteLine(ex);
   return 1;
  }
 }

 static void VersionCompare() {
  Check(Updater.CompareVersions("0.5.10", "0.5.9") > 0, "0.5.10 > 0.5.9");
  Check(Updater.CompareVersions("0.5.9", "0.5.10") < 0, "0.5.9 < 0.5.10");
  Check(Updater.CompareVersions("v0.6.0", "0.6.0") == 0, "strip v equal");
  Check(Updater.CompareVersions("0.6.0", "0.5.1") > 0, "0.6.0 > 0.5.1");
  Check(Updater.CompareVersions("1.0.0", "1.0") == 0, "trailing zero equal");
  Check(Updater.CompareVersions("0.5.1", "0.5.1") == 0, "equal");
 }

 static string Sha(string text) {
  using (var sha = SHA256.Create()) {
   byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
   var sb = new StringBuilder(hash.Length * 2);
   for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
   return sb.ToString();
  }
 }

 static string ReleaseJson(string tag, bool draft, bool prerelease, string body, params string[] assetPairs) {
  // assetPairs: name,url,name,url...
  var assets = new StringBuilder();
  assets.Append('[');
  for (int i = 0; i + 1 < assetPairs.Length; i += 2) {
   if (i > 0) assets.Append(',');
   assets.Append("{\"name\":\"").Append(assetPairs[i]).Append("\",\"browser_download_url\":\"")
    .Append(assetPairs[i + 1]).Append("\"}");
  }
  assets.Append(']');
  return "{\"tag_name\":\"" + tag + "\",\"draft\":" + (draft ? "true" : "false")
   + ",\"prerelease\":" + (prerelease ? "true" : "false")
   + ",\"body\":\"" + (body ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\",\"assets\":" + assets + "}";
 }

 sealed class FakeHttp : IUpdateHttp {
  public int Calls;
  public Func<string, string> Strings;
  public Action<string, string, IProgress<int>> OnDownload;
  public Exception ThrowOnGet;
  public Exception ThrowOnDownload;
  public string GetString(string url, IDictionary<string, string> headers, int timeoutMs) {
   Calls++;
   if (ThrowOnGet != null) throw ThrowOnGet;
   if (Strings != null) return Strings(url);
   return "";
  }
  public void Download(string url, string destinationPath, IDictionary<string, string> headers, int timeoutMs, IProgress<int> progress) {
   Calls++;
   if (ThrowOnDownload != null) throw ThrowOnDownload;
   if (OnDownload != null) OnDownload(url, destinationPath, progress);
   else File.WriteAllText(destinationPath, "payload");
  }
 }

 static string TempDir() {
  string d = Path.Combine(Path.GetTempPath(), "FlightBridge-updater-" + Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(d);
  return d;
 }

 static IUpdater Make(FakeHttp http, string dir, string exeName, Func<DateTime> clock, string installDir = null) {
  string exe = Path.Combine(dir, exeName);
  if (!File.Exists(exe)) File.WriteAllText(exe, "current-exe");
  return new Updater(http, clock ?? (() => DateTime.UtcNow), Path.Combine(dir, "settings"), exe, installDir ?? Path.Combine(dir, "not-install"));
 }

 static void DisabledZeroHttp() {
  var http = new FakeHttp { Strings = url => { throw new Exception("network"); } };
  string dir = TempDir();
  var u = Make(http, dir, "FlightBridge.exe", null);
  var s = new UpdateSettings { Enabled = false };
  var info = u.CheckAsync(s, false).Result;
  Check(info == null, "disabled returns null");
  Check(http.Calls == 0, "disabled zero HTTP calls");
 }

 static void ManualBypasses() {
  string payload = "NEWBYTES";
  string hash = Sha(payload);
  string shaBody = hash + "  FlightBridge.exe\n";
  var http = new FakeHttp();
  http.Strings = url => {
   if (url.Contains("releases/latest"))
    return ReleaseJson("v0.9.0", false, false, "notes",
     "FlightBridge.exe", "https://example.test/FlightBridge.exe",
     "SHA256.txt", "https://example.test/SHA256.txt");
   if (url.Contains("SHA256")) return shaBody;
   return "";
  };
  string dir = TempDir();
  DateTime now = new DateTime(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);
  var u = Make(http, dir, "FlightBridge.exe", () => now);

  // disabled + manual still checks
  var disabled = new UpdateSettings { Enabled = false, SkippedVersion = "0.9.0", RemindAfter = now.AddDays(3) };
  var info = u.CheckAsync(disabled, true).Result;
  Check(info != null && info.Version == "0.9.0", "manual bypasses disabled/skip/remind");
  Check(http.Calls >= 2, "manual performed HTTP");
 }

 static void NewerDetected() {
  string hash = Sha("x");
  var http = new FakeHttp();
  http.Strings = url => {
   if (url.Contains("releases/latest"))
    return ReleaseJson("v9.9.9", false, false, "hello",
     "FlightBridge.exe", "https://example.test/FlightBridge.exe",
     "SHA256.txt", "https://example.test/SHA256.txt");
   if (url.Contains("SHA256")) return hash + "  FlightBridge.exe\r\n";
   return "";
  };
  string dir = TempDir();
  var u = Make(http, dir, "FlightBridge.exe", null);
  var info = u.CheckAsync(new UpdateSettings(), false).Result;
  Check(info != null, "newer detected");
  Check(info.Version == "9.9.9", "version parsed");
  Check(info.AssetName == "FlightBridge.exe", "portable asset");
  Check(info.Sha256 == hash, "sha attached");
  Check(info.DownloadUrl.Contains("FlightBridge.exe"), "download url");
  Check(info.Notes == "hello", "notes");

  // installed → Setup asset
  string install = Path.Combine(dir, "Programs", "FlightBridge");
  Directory.CreateDirectory(install);
  File.WriteAllText(Path.Combine(install, "FlightBridge.exe"), "installed");
  string setupHash = Sha("setup");
  http.Strings = url => {
   if (url.Contains("releases/latest"))
    return ReleaseJson("v9.9.9", false, false, "",
     "FlightBridge-Setup.exe", "https://example.test/FlightBridge-Setup.exe",
     "FlightBridge.exe", "https://example.test/FlightBridge.exe",
     "SHA256.txt", "https://example.test/SHA256.txt");
   if (url.Contains("SHA256")) return setupHash + "  FlightBridge-Setup.exe\n" + hash + "  FlightBridge.exe\n";
   return "";
  };
  IUpdater installed = new Updater(http, null, Path.Combine(dir, "settings-i"), Path.Combine(install, "FlightBridge.exe"), install);
  var info2 = installed.CheckAsync(new UpdateSettings(), false).Result;
  Check(info2 != null && info2.AssetName == "FlightBridge-Setup.exe", "installed uses Setup asset");
 }

 static void SkippedAndNext() {
  string hash = Sha("a");
  string currentTag = "v0.8.0";
  var http = new FakeHttp();
  http.Strings = url => {
   if (url.Contains("releases/latest"))
    return ReleaseJson(currentTag, false, false, "",
     "FlightBridge.exe", "https://example.test/FlightBridge.exe",
     "SHA256.txt", "https://example.test/SHA256.txt");
   if (url.Contains("SHA256")) return hash + "  FlightBridge.exe\n";
   return "";
  };
  string dir = TempDir();
  var u = Make(http, dir, "FlightBridge.exe", null);
  var skipped = new UpdateSettings { Enabled = true, SkippedVersion = "0.8.0" };
  Check(u.CheckAsync(skipped, false).Result == null, "skipped version not offered");
  Check(u.CheckAsync(skipped, true).Result != null, "manual still offers skipped");

  currentTag = "v0.8.1";
  Check(u.CheckAsync(skipped, false).Result != null
   && u.CheckAsync(skipped, false).Result.Version == "0.8.1", "next version after skip is offered");
 }

 static void RemindAfterClock() {
  var http = new FakeHttp();
  http.Strings = url => {
   if (url.Contains("releases/latest"))
    return ReleaseJson("v0.7.0", false, false, "",
     "FlightBridge.exe", "https://example.test/e",
     "SHA256.txt", "https://example.test/s");
   return Sha("z") + "  FlightBridge.exe\n";
  };
  string dir = TempDir();
  DateTime now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
  DateTime clock = now;
  var u = Make(http, dir, "FlightBridge.exe", () => clock);
  u.RemindLater(7);
  var loaded = u.LoadSettings();
  Check(loaded.RemindAfter.HasValue && loaded.RemindAfter.Value == now.AddDays(7), "RemindLater writes +7 days");
  Check(u.CheckAsync(u.LoadSettings(), false).Result == null, "remind future → null");
  clock = now.AddDays(8);
  Check(u.CheckAsync(u.LoadSettings(), false).Result != null, "after remind window → update");
 }

 static void PrereleaseDraftIgnored() {
  var http = new FakeHttp();
  http.Strings = url => ReleaseJson("v9.0.0", true, false, "", "FlightBridge.exe", "https://example.test/e", "SHA256.txt", "https://example.test/s");
  string dir = TempDir();
  var u = Make(http, dir, "FlightBridge.exe", null);
  Check(u.CheckAsync(new UpdateSettings(), false).Result == null, "draft ignored");
  http.Strings = url => ReleaseJson("v9.0.0", false, true, "", "FlightBridge.exe", "https://example.test/e", "SHA256.txt", "https://example.test/s");
  Check(u.CheckAsync(new UpdateSettings(), false).Result == null, "prerelease ignored");
 }

 static void HttpErrorsSilent() {
  string dir = TempDir();
  foreach (var ex in new Exception[] {
   new WebException("403", null, WebExceptionStatus.ProtocolError, null),
   new WebException("429", null, WebExceptionStatus.ProtocolError, null),
   new TimeoutException("timeout"),
   new Exception("boom")
  }) {
   var http = new FakeHttp { ThrowOnGet = ex };
   var u = Make(http, dir, "FlightBridge.exe", null);
   Check(u.CheckAsync(new UpdateSettings(), false).Result == null, "error → null: " + ex.GetType().Name);
  }
 }

 static void MissingShaEntry() {
  var http = new FakeHttp();
  http.Strings = url => {
   if (url.Contains("releases/latest"))
    return ReleaseJson("v0.9.0", false, false, "",
     "FlightBridge.exe", "https://example.test/e",
     "SHA256.txt", "https://example.test/s");
   return Sha("other") + "  FlightBridge-portable.zip\n";
  };
  string dir = TempDir();
  var u = Make(http, dir, "FlightBridge.exe", null);
  Check(u.CheckAsync(new UpdateSettings(), false).Result == null, "missing SHA256 entry → no update");
 }

 static void HashMismatchUnchanged() {
  string dir = TempDir();
  string exe = Path.Combine(dir, "FlightBridge.exe");
  File.WriteAllText(exe, "ORIGINAL");
  string before = File.ReadAllText(exe);
  var http = new FakeHttp();
  http.OnDownload = (url, dest, p) => { File.WriteAllText(dest, "TAMPERED"); if (p != null) p.Report(100); };
  IUpdater u = new Updater(http, null, Path.Combine(dir, "settings"), exe, Path.Combine(dir, "other"));
  var info = new UpdateInfo {
   Version = "1.0.0", AssetName = "FlightBridge.exe", Sha256 = Sha("EXPECTED"),
   DownloadUrl = "https://example.test/FlightBridge.exe"
  };
  var result = u.ApplyAsync(info, null).Result;
  Check(!result.Ok, "hash mismatch Ok=false");
  Check(result.Message != null && result.Message.IndexOf("как прежде", StringComparison.Ordinal) >= 0, "russian fail message");
  Check(File.ReadAllText(exe) == before, "exe unchanged after mismatch");
  Check(!File.Exists(exe + ".old"), "no .old after mismatch");
 }

 static void InterruptedDownloadUnchanged() {
  string dir = TempDir();
  string exe = Path.Combine(dir, "FlightBridge.exe");
  File.WriteAllText(exe, "ORIGINAL");
  var http = new FakeHttp { ThrowOnDownload = new IOException("interrupted") };
  IUpdater u = new Updater(http, null, Path.Combine(dir, "settings"), exe, Path.Combine(dir, "other"));
  var info = new UpdateInfo {
   Version = "1.0.0", AssetName = "FlightBridge.exe", Sha256 = Sha("x"),
   DownloadUrl = "https://example.test/FlightBridge.exe"
  };
  var result = u.ApplyAsync(info, null).Result;
  Check(!result.Ok, "interrupted Ok=false");
  Check(File.ReadAllText(exe) == "ORIGINAL", "exe unchanged after interrupt");
 }

 static void PortableSwapAndCleanup() {
  string dir = TempDir();
  string exe = Path.Combine(dir, "FlightBridge.exe");
  File.WriteAllText(exe, "OLD-EXE");
  string payload = "NEW-EXE-CONTENT";
  string hash = Sha(payload);
  var http = new FakeHttp();
  http.OnDownload = (url, dest, p) => { File.WriteAllText(dest, payload); if (p != null) { p.Report(50); p.Report(100); } };
  IUpdater u = new Updater(http, null, Path.Combine(dir, "settings"), exe, Path.Combine(dir, "other"));
  var info = new UpdateInfo {
   Version = "1.2.3", AssetName = "FlightBridge.exe", Sha256 = hash,
   DownloadUrl = "https://example.test/FlightBridge.exe"
  };
  var result = u.ApplyAsync(info, new Progress<int>()).Result;
  Check(result.Ok && result.RestartRequired, "portable swap ok + restart");
  Check(File.ReadAllText(exe) == payload, "new exe in place");
  Check(File.Exists(exe + ".old") && File.ReadAllText(exe + ".old") == "OLD-EXE", ".old created");
  u.CleanupOnStart();
  Check(!File.Exists(exe + ".old"), "CleanupOnStart removes .old");
 }

 static void SettingsRoundtrip() {
  string dir = TempDir();
  DateTime now = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc);
  var u = Make(new FakeHttp(), dir, "FlightBridge.exe", () => now);
  var s = new UpdateSettings { Enabled = false, SkippedVersion = "0.5.2", RemindAfter = now.AddDays(7) };
  u.SaveSettings(s);
  var loaded = u.LoadSettings();
  Check(loaded.Enabled == false, "Enabled roundtrip");
  Check(loaded.SkippedVersion == "0.5.2", "SkippedVersion roundtrip");
  Check(loaded.RemindAfter.HasValue && loaded.RemindAfter.Value == now.AddDays(7), "RemindAfter roundtrip");
  u.SkipVersion(new UpdateInfo { Version = "v1.2.3" });
  Check(u.LoadSettings().SkippedVersion == "1.2.3", "SkipVersion strips v");
 }
}
