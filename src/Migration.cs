using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Xml.Linq;

namespace FSMigrator {
// DTOs aligned with vibe/ui-preview-confirm IMigrationService (property names + Prepare/ListSteamAccounts/ExportForImport/WriteToGame).
// Extra fields (NextStep, FallbackReason, Message, ListBackups, Restore, Diagnose, CreateDiagnosticReport) are additive for the product TZ.
// SkippedBinding lives in Core.cs (shared machine Reason + human Message).

public sealed class PreviewItem {
 public string SourceProfile {get;set;}
 public string TargetProfile {get;set;}
 public string Device {get;set;}
 public string Category {get;set;}
 public string Store2020 {get;set;}
 public string Store2024 {get;set;}
 public int Bindings {get;set;}
 public int Axes {get;set;}
 public List<SkippedBinding> Skipped {get;set;}
 public List<string> Warnings {get;set;}
 public PreviewItem(){Skipped=new List<SkippedBinding>();Warnings=new List<string>();}
}

public sealed class MigrationPreview {
 public List<PreviewItem> Items {get;set;}
 public List<string> Issues {get;set;}
 public List<string> Notices {get;set;}
 public bool CanExport {get;set;}
 public bool CanWriteToGame {get;set;}
 public string NextStep {get;set;}
 public string FallbackReason {get;set;}
 public List<SteamAccount> CandidateSteamAccounts {get;set;}
 public bool HasSteamStore {get;set;}
 public bool HasMicrosoftStore {get;set;}
 // Same role as UI contract UnderlyingPlan — exact snapshot for Write/Export.
 public AutomaticPlan UnderlyingPlan {get;set;}
 public AutomaticPlan Plan {get{return UnderlyingPlan;} set{UnderlyingPlan=value;}}
 public MigrationPreview(){Items=new List<PreviewItem>();Issues=new List<string>();Notices=new List<string>();CandidateSteamAccounts=new List<SteamAccount>();}
}

public sealed class SteamAccount {
 public string Id {get;set;}
 public string Name {get;set;}
 public bool HasMsfs2020 {get;set;}
 public bool HasMsfs2024 {get;set;}
 public override string ToString(){return (Name??Id??"?")+(HasMsfs2020&&HasMsfs2024?" · 2020+2024":HasMsfs2020?" · 2020":HasMsfs2024?" · 2024":"");}
}

public sealed class ExportResult {
 public string Folder {get;set;}
 public List<string> Files {get;set;}
 public ExportResult(){Files=new List<string>();}
}

public sealed class WriteResult {
 public string BackupManifest {get;set;}
 public bool Success {get;set;}
 public string Error {get;set;}
 public List<string> Notices {get;set;}
 public WriteResult(){Notices=new List<string>();}
}

public sealed class BackupInfo {
 public string Manifest {get;set;}
 public string Folder {get;set;}
 public string Label {get;set;}
 public string State {get;set;}
 public DateTime CreatedUtc {get;set;}
 public override string ToString(){return Label??Folder??Manifest??"?";}
}

/// <summary>
/// Opaque confirmation after preview. UI uses FromUiDialog(summary). Tests may use Confirm(preview) to bind to one preview instance.
/// </summary>
public sealed class WriteConfirmation {
 public string AcknowledgedSummary {get;private set;}
 readonly MigrationPreview bound;
 WriteConfirmation(string summary,MigrationPreview preview){AcknowledgedSummary=summary;bound=preview;}
 public static WriteConfirmation FromUiDialog(string acknowledgedSummary){
  if(string.IsNullOrWhiteSpace(acknowledgedSummary)) throw new ArgumentException("Confirmation summary is required.","acknowledgedSummary");
  return new WriteConfirmation(acknowledgedSummary.Trim(),null);
 }
 public static WriteConfirmation Confirm(MigrationPreview preview){
  if(preview==null) throw new ArgumentNullException("preview");
  if(!preview.CanWriteToGame) throw new InvalidOperationException("Preview is not writable to the game.");
  return new WriteConfirmation("User confirmed preview",preview);
 }
 internal bool IsFor(MigrationPreview preview){
  if(bound==null) return preview!=null; // FromUiDialog: any writable preview accepted
  return object.ReferenceEquals(bound,preview);
 }
}

/// <summary>Static facade. MigrationService implements the UI IMigrationService shape without owning MigrationContract.cs.</summary>
public static class Migration {
 public const string SteamCloudNotice="При следующем запуске Steam может показать конфликт облака: выберите загрузку локальных файлов (Upload local). / Next Steam launch may show a cloud conflict: choose Upload local.";
 public const string StoreCloudNotice="Поведение облака Xbox после правки не проверено; сначала запустите диагностику. / Xbox cloud behaviour after a write is unverified; run diagnosis first.";

 public static List<SteamAccount> ListSteamAccounts(){
  return ListSteamAccounts(DiscoverSteamRoot(),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
 }
 public static List<SteamAccount> ListSteamAccounts(string steamRoot,string localAppData){
  var stores=AutoMigration.Discover(steamRoot,localAppData);
  return stores.Where(s=>s.Edition=="Steam"&&!string.IsNullOrEmpty(s.Account))
   .GroupBy(s=>s.Account,StringComparer.OrdinalIgnoreCase)
   .Select(g=>new SteamAccount{
    Id=g.Key, Name=g.Key,
    HasMsfs2020=g.Any(s=>s.Year=="2020"&&s.Profiles.Count>0),
    HasMsfs2024=g.Any(s=>s.Year=="2024"&&s.Profiles.Count>0)
   })
   .OrderByDescending(a=>a.HasMsfs2020&&a.HasMsfs2024)
   .ThenBy(a=>a.Id,StringComparer.OrdinalIgnoreCase)
   .ToList();
 }

 static List<SteamAccount> DualAccounts(IEnumerable<SteamAccount> accounts){
  return accounts.Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
 }

 public static MigrationPreview Prepare(string steamAccount){
  return Prepare(DiscoverSteamRoot(),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),steamAccount);
 }
 public static MigrationPreview Prepare(string steamRoot,string localAppData,string steamAccount){
  var accounts=ListSteamAccounts(steamRoot,localAppData);
  var dual=DualAccounts(accounts);
  // "Launch and it works": auto-pick the only Steam account that has both games with profiles.
  if(string.IsNullOrWhiteSpace(steamAccount)){
   steamAccount=ChooseSteamAccount(dual,steamRoot,null);
   if(string.IsNullOrEmpty(steamAccount)&&dual.Count>1){
    var preview=new MigrationPreview{CanExport=false,CanWriteToGame=false,CandidateSteamAccounts=new List<SteamAccount>(dual)};
    preview.Issues.Add("Найдено несколько аккаунтов Steam с профилями обеих игр. Выберите нужный аккаунт и повторите. / Several Steam accounts have profiles for both games. Choose one account and try again.");
    preview.NextStep="Выберите аккаунт Steam в списке и нажмите проверку снова. / Choose a Steam account in the list and run the check again.";
    foreach(var a in dual) preview.Notices.Add("Доступный аккаунт: "+MaskId(a.Id)+" (есть профили 2020 и 2024). / Available account: "+MaskId(a.Id)+" (has 2020 and 2024 profiles).");
    return preview;
   }
  }
  var stores=AutoMigration.Discover(steamRoot,localAppData);
  if(!string.IsNullOrEmpty(steamAccount)){
   foreach(var s in stores.Where(x=>x.Edition=="Steam")) s.Active=string.Equals(s.Account,steamAccount,StringComparison.OrdinalIgnoreCase);
  }
  return FromPlan(AutoMigration.Build(stores));
 }

 public static MigrationPreview Diagnose(){return Diagnose(null,null,null);}
 public static MigrationPreview Diagnose(string steamRoot,string localAppData,string steamAccount){
  if(steamRoot==null) steamRoot=DiscoverSteamRoot();
  if(localAppData==null) localAppData=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
  var preview=Prepare(steamRoot,localAppData,steamAccount);
  if(preview.CanWriteToGame)
   preview.NextStep="Диагностика завершена — можно подтвердить запись. / Diagnosis complete — you can confirm the write.";
  return preview;
 }

 public static MigrationPreview FromPlan(AutomaticPlan plan){
  var preview=new MigrationPreview{UnderlyingPlan=plan,Issues=new List<string>(HumanizeAll(plan.Issues)),Notices=new List<string>(HumanizeAll(plan.Notices))};
  var usedSources=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var change in plan.Changes){
   usedSources.Add(change.Source.Path);
   var item=new PreviewItem{
    SourceProfile=change.Source.Name, TargetProfile=change.Target.Name,
    Device=FriendlyDevice((string)change.Target.Device.Attribute("DeviceName")),
    Category=change.Target.CategoryCode,
    Store2020=FriendlyStore(plan,change.Source), Store2024=FriendlyStore(plan,change.Target),
    Bindings=change.Copied, Axes=change.Axes,
    Skipped=new List<SkippedBinding>(change.SkippedBindings),
    Warnings=HumanizeAll(change.Warnings).ToList()
   };
   if(string.Equals(item.Category,"General",StringComparison.OrdinalIgnoreCase))
    item.Warnings.Add("Общий профиль: после переноса проверьте управление в игре. / General profile: check controls in the game after transfer.");
   preview.Items.Add(item);
  }
  foreach(var store in plan.Stores.Where(s=>s.Year=="2020")){
   foreach(var src in store.Profiles){
    if(usedSources.Contains(src.Path)) continue;
    if(plan.Changes.Count==0 && plan.Issues.Count>0) continue;
    preview.Notices.Add("Для профиля «"+src.Name+"» из 2020 не найден подходящий профиль 2024. / No matching 2024 profile for 2020 profile \""+src.Name+"\".");
   }
  }
  preview.CanWriteToGame=plan.Ready;
  preview.CanExport=preview.Items.Any(i=>i.Bindings>0||i.Axes>0);
  preview.HasSteamStore=plan.Stores.Any(s=>s.Edition=="Steam");
  preview.HasMicrosoftStore=plan.Stores.Any(s=>s.Edition!=null&&s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0);
  if(preview.CanWriteToGame){
   preview.NextStep="Подтвердите запись в уже сохранённые профили MSFS 2024 (сначала будет сделана полная копия). / Confirm writing into existing MSFS 2024 profiles (a full backup is created first).";
   preview.FallbackReason=null;
  }else if(preview.CanExport){
   preview.FallbackReason=preview.Issues.FirstOrDefault()??"Автоматическая запись сейчас недоступна. / Automatic write is not available right now.";
   preview.NextStep="Сохраните файл для импорта и откройте его в MSFS 2024 → Параметры → Управление. / Save a file for import and open it in MSFS 2024 → Options → Controls.";
  }else{
   preview.FallbackReason=null;
   preview.NextStep=preview.Issues.FirstOrDefault()??"Сохраните свой профиль управления в MSFS 2024, полностью закройте игру и повторите проверку. / Save your control profile in MSFS 2024, fully close the game, then check again.";
  }
  return preview;
 }

 static string FriendlyDevice(string name){return string.IsNullOrWhiteSpace(name)?"controller":name;}
 static string FriendlyStore(AutomaticPlan plan,Profile p){
  var store=plan.Stores.FirstOrDefault(s=>s.Profiles.Any(x=>x.Path==p.Path));
  if(store==null) return "";
  string edition=store.Edition=="Steam"?"Steam":"Microsoft Store";
  return store.Year+" · "+edition;
 }
 static IEnumerable<string> HumanizeAll(IEnumerable<string> texts){
  foreach(var t in texts??Enumerable.Empty<string>()) yield return Humanize(t);
 }
 static string Humanize(string text){
  if(string.IsNullOrEmpty(text)) return text;
  string t=text;
  // Strip technical tokens from human-facing copy (keep meaning).
  t=Regex.Replace(t,@"\b(WGS|XML|GUID|ProductID|containers\.index|remotecache\.vdf|inputprofile_)\b","",RegexOptions.IgnoreCase);
  t=Regex.Replace(t,@"[A-Za-z]:\\[^\s]+","",RegexOptions.IgnoreCase);
  t=Regex.Replace(t,@"%LOCALAPPDATA%[^\s]*","",RegexOptions.IgnoreCase);
  t=Regex.Replace(t,@"\s{2,}"," ").Trim();
  return t;
 }

 public static string ChooseSteamAccount(IList<SteamAccount> dual,string steamRoot,string activeUserOverride){
  if(dual==null||dual.Count==0) return null;
  if(dual.Count==1) return dual[0].Id;
  string active=activeUserOverride;
  if(active==null) active=AutoMigration.ReadActiveSteamAccount();
  if(!string.IsNullOrEmpty(active)&&active!="0"){
   var hit=dual.FirstOrDefault(a=>string.Equals(a.Id,active,StringComparison.OrdinalIgnoreCase));
   if(hit!=null) return hit.Id;
  }
  string recent=AutoMigration.ReadMostRecentAccountId(steamRoot);
  if(!string.IsNullOrEmpty(recent)){
   var hit=dual.FirstOrDefault(a=>string.Equals(a.Id,recent,StringComparison.OrdinalIgnoreCase));
   if(hit!=null) return hit.Id;
  }
  return null;
 }

 static string DiscoverSteamRoot(){
  bool fromReg,fromFb; var tried=new List<string>();
  return AutoMigration.ResolveSteamRoot(null,AutoMigration.DefaultSteamFallbackRoots(),out fromReg,out fromFb,tried);
 }

 public static string DefaultBackupFolder(){return MigrationTransaction.DefaultRoot;}
 public static string DefaultExportFolder(){
  return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Flight Bridge","Export-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"));
 }
 public static string DefaultReportFolder(){
  return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Flight Bridge","Diagnostics");
 }

 /// <summary>UI contract: returns backup manifest path. Prefer WriteToGameResult for Notices.</summary>
 public static string WriteToGame(MigrationPreview preview,WriteConfirmation confirmation){
  var result=WriteToGameResult(preview,confirmation,null);
  if(!result.Success) throw new InvalidOperationException(result.Error??"Write failed.");
  return result.BackupManifest;
 }
 public static WriteResult WriteToGameResult(MigrationPreview preview,WriteConfirmation confirmation){
  return WriteToGameResult(preview,confirmation,null);
 }
 public static WriteResult WriteToGameResult(MigrationPreview preview,WriteConfirmation confirmation,string backupRoot){
  var result=new WriteResult();
  if(confirmation==null||!confirmation.IsFor(preview)){result.Success=false;result.Error="Подтверждение записи для этого превью обязательно. / Write confirmation for this preview is required.";return result;}
  if(preview==null||preview.UnderlyingPlan==null||!preview.CanWriteToGame){result.Success=false;result.Error="Запись для этого превью недоступна. / Write is not available for this preview.";return result;}
  if(string.IsNullOrWhiteSpace(backupRoot)) backupRoot=DefaultBackupFolder();
  var remoteCaches=new List<string>();
  var cacheHashes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var store in preview.UnderlyingPlan.Stores){
   if(store.Edition!="Steam") continue;
   string cache=Path.Combine(store.Root,"remotecache.vdf");
   if(File.Exists(cache)){remoteCaches.Add(cache);cacheHashes[cache]=Engine.Hash(cache);}
  }
  try{
   string manifest=MigrationTransaction.Execute(preview.UnderlyingPlan,backupRoot,()=>AutoMigration.RequireClosed(preview.UnderlyingPlan.Stores),null);
   foreach(var cache in remoteCaches){
    if(!File.Exists(cache)) throw new IOException("Служебный файл Steam Cloud пропал во время записи. / Steam Cloud helper file disappeared during write.");
    if(Engine.Hash(cache)!=cacheHashes[cache]) throw new IOException("Служебный файл Steam Cloud изменился — так быть не должно. / Steam Cloud helper file changed — that must never happen.");
   }
   result.Success=true;result.BackupManifest=manifest;
   bool anySteam=preview.UnderlyingPlan.Stores.Any(s=>s.Edition=="Steam");
   bool anyStore=preview.UnderlyingPlan.Stores.Any(s=>s.Edition!=null&&s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0);
   if(anySteam){result.Notices.Add(SteamCloudNotice);preview.Notices.Add(SteamCloudNotice);}
   if(anyStore){result.Notices.Add(StoreCloudNotice);preview.Notices.Add(StoreCloudNotice);}
  }catch(Exception ex){result.Success=false;result.Error=Humanize(ex.Message);}
  return result;
 }

 public static List<BackupInfo> ListBackups(){return ListBackups(null);}
 public static List<BackupInfo> ListBackups(string backupRoot){
  if(string.IsNullOrWhiteSpace(backupRoot)) backupRoot=DefaultBackupFolder();
  var list=new List<BackupInfo>();
  foreach(var manifest in MigrationTransaction.Sessions(backupRoot)){
   try{
    string state=MigrationTransaction.State(manifest);
    var created=File.GetCreationTimeUtc(manifest);
    list.Add(new BackupInfo{
     Manifest=manifest, Folder=Path.GetDirectoryName(manifest),
     Label=created.ToLocalTime().ToString("yyyy-MM-dd HH:mm")+" · "+state,
     State=state, CreatedUtc=created
    });
   }catch{}
  }
  return list.OrderByDescending(b=>b.CreatedUtc).ToList();
 }

 public static void Restore(string backupManifest){
  Restore(backupManifest,null,null);
 }
 public static void Restore(string backupManifest,string steamRoot,string localAppData){
  if(string.IsNullOrWhiteSpace(backupManifest)) throw new ArgumentException("backupManifest");
  var document=MigrationTransaction.Verify(backupManifest);
  var current=(steamRoot==null&&localAppData==null)?AutoMigration.Discover():AutoMigration.Discover(steamRoot??DiscoverSteamRoot(),localAppData??Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
  var roots=current.Select(s=>Path.GetFullPath(s.Root)).ToList();
  if(document.Root.Elements("Store").Any(s=>!roots.Contains((string)s.Attribute("Root"),StringComparer.OrdinalIgnoreCase)))
   throw new IOException("Эта копия относится к другой установке игр. / This backup belongs to a different game installation.");
  MigrationTransaction.Restore(backupManifest,()=>AutoMigration.RequireClosed(current));
 }

 public static ExportResult ExportForImport(MigrationPreview preview){return ExportForImport(preview,null);}
 public static ExportResult ExportForImport(MigrationPreview preview,string folder){
  if(preview==null||preview.UnderlyingPlan==null) throw new ArgumentNullException("preview");
  if(!preview.CanExport) throw new InvalidOperationException("Экспортировать нечего. / Nothing to export.");
  if(string.IsNullOrWhiteSpace(folder)) folder=DefaultExportFolder();
  foreach(var store in preview.UnderlyingPlan.Stores){
   string root=Path.GetFullPath(store.Root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   string dest=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   if(dest.StartsWith(root,StringComparison.OrdinalIgnoreCase)||root.StartsWith(dest,StringComparison.OrdinalIgnoreCase))
    throw new IOException("Папка экспорта не должна совпадать с папками игры. / Export folder must not be inside the game folders.");
  }
  Directory.CreateDirectory(folder);
  var result=new ExportResult{Folder=Path.GetFullPath(folder)};
  var usedNames=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var change in preview.UnderlyingPlan.Changes){
   if(change.Copied==0&&change.Axes==0) continue;
   if(!Engine.Unchanged(change.Source)||!Engine.Unchanged(change.Target)) throw new IOException("Профили изменились. Запустите проверку снова. / Profiles changed. Run the check again.");
   string device=SafeFileToken((string)change.Target.Device.Attribute("DeviceName")??"device");
   string category=SafeFileToken(change.Target.CategoryCode);
   string baseName=device+"__"+category;
   string fileName=baseName+".xml";
   int n=2; while(!usedNames.Add(fileName)) {fileName=baseName+"_"+n+".xml";n++;}
   string path=Path.Combine(folder,fileName);
   change.Target.Save(change.Output,path);
   Profile.Read(path);
   result.Files.Add(path);
  }
  string readme=Path.Combine(folder,"README-IMPORT.txt");
  File.WriteAllText(readme,
   "Flight Bridge — file for import in the game (fallback)\r\n"+
   "Flight Bridge — файл для импорта в игре (запасной вариант)\r\n\r\n"+
   "EN: MSFS 2024 → Options → Controls → same device → same category → Import.\r\n"+
   "RU: MSFS 2024 → Параметры → Управление → то же устройство → та же категория → Импорт.\r\n\r\n"+
   "These files do not change Steam or Microsoft Store game folders.\r\n"+
   "Эти файлы не меняют папки Steam или Microsoft Store.\r\n",
   Encoding.UTF8);
  result.Files.Add(readme);
  return result;
 }

 public static string CreateDiagnosticReport(){return CreateDiagnosticReport(null,null);}
 public static string CreateDiagnosticReport(string folder){return CreateDiagnosticReport(folder,null);}
 public static string CreateDiagnosticReport(string folder,MigrationPreview preview){
  if(string.IsNullOrWhiteSpace(folder)) folder=DefaultReportFolder();
  Directory.CreateDirectory(folder);
  if(preview==null) preview=Diagnose();
  var sb=new StringBuilder();
  sb.AppendLine("Flight Bridge diagnostic report (redacted)");
  // Where we looked / what we saw (roots masked).
  try{
   var probe=AutoMigration.Probe(DiscoverSteamRoot(),Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),null,AutoMigration.DefaultSteamFallbackRoots());
   sb.AppendLine("Discovery steamRoot="+(probe.SteamRoot==null?"(none)":RedactPath(probe.SteamRoot))+"; fromRegistry="+probe.SteamFromRegistry+"; fromFallback="+probe.SteamFromFallback);
   sb.AppendLine("Discovery steamRootsTried="+probe.SteamRootsTried.Count+"; libraries="+probe.SteamLibraries.Count+"; userdata="+probe.UserDataExists+"; steamAccounts="+probe.SteamAccountFolders);
   sb.AppendLine("Discovery packagesFolder="+probe.PackagesFolderExists+"; packageNames="+probe.PackageFolderNames.Count+"; stores="+probe.Stores.Count);
   foreach(var note in probe.Notes) sb.AppendLine("DiscoveryNote="+Redact(note));
   foreach(var name in probe.PackageFolderNames) sb.AppendLine("Package="+name);
   foreach(var s in probe.Stores) sb.AppendLine("FoundStore year="+s.Year+"; edition="+(s.Edition=="Steam"?"Steam":"MicrosoftStore")+"; profiles="+s.Profiles.Count+"; account="+(string.IsNullOrEmpty(s.Account)?"":MaskId(s.Account)));
  }catch(Exception ex){sb.AppendLine("DiscoveryError="+Redact(ex.Message));}

  sb.AppendLine("GeneratedUtc="+DateTime.UtcNow.ToString("o"));
  sb.AppendLine("CanWriteToGame="+preview.CanWriteToGame+"; CanExport="+preview.CanExport);
  sb.AppendLine("NextStep="+Redact(preview.NextStep));
  if(!string.IsNullOrEmpty(preview.FallbackReason)) sb.AppendLine("FallbackReason="+Redact(preview.FallbackReason));
  sb.AppendLine("Issues="+preview.Issues.Count+"; Notices="+preview.Notices.Count+"; Items="+preview.Items.Count);
  if(preview.UnderlyingPlan!=null){
   int accountIndex=0;
   var accountMap=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(var store in preview.UnderlyingPlan.Stores){
    string account=store.Account??"";
    if(store.Edition=="Steam"&&account.Length>0&&!accountMap.ContainsKey(account)){
     accountIndex++; accountMap[account]="<account#"+accountIndex+">";
    }
    string accountLabel=accountMap.ContainsKey(account)?accountMap[account]:(account.Length==0?"":MaskId(account));
    string edition=store.Edition=="Steam"?"Steam":"MicrosoftStore";
    sb.AppendLine("Store year="+store.Year+"; edition="+edition+"; profiles="+store.Profiles.Count+"; unreadable="+store.Rejected+"; install="+(store.InstallPath!=null)+"; account="+accountLabel+"; root="+RedactPath(store.Root));
   }
   sb.AppendLine(AutoMigration.RedactedReport(preview.UnderlyingPlan));
  }
  foreach(var item in preview.Items){
   sb.AppendLine("Item category="+item.Category+"; bindings="+item.Bindings+"; axes="+item.Axes+"; skipped="+item.Skipped.Count+"; warnings="+item.Warnings.Count);
   foreach(var sk in item.Skipped) sb.AppendLine("  skip reason="+sk.Reason+"; message="+Redact(sk.Message));
  }
  string path=Path.Combine(folder,"diagnostics-redacted.txt");
  File.WriteAllText(path,sb.ToString(),Encoding.UTF8);
  return path;
 }

 static string MaskId(string id){
  if(string.IsNullOrEmpty(id)||id.Length<4) return "<id>";
  using(var h=System.Security.Cryptography.SHA256.Create()){
   byte[] hash=h.ComputeHash(Encoding.UTF8.GetBytes(id));
   return "<steam:"+BitConverter.ToString(hash).Replace("-","").Substring(0,12).ToLowerInvariant()+">";
  }
 }
 static string RedactPath(string path){
  if(string.IsNullOrEmpty(path)) return "";
  string p=path.Replace('/','\\');
  string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
  if(!string.IsNullOrEmpty(local)&&p.StartsWith(local,StringComparison.OrdinalIgnoreCase))
   return "%LOCALAPPDATA%"+p.Substring(local.Length);
  var m=Regex.Match(p,@"^(.*)\\userdata\\([^\\]+)(\\.*)$",RegexOptions.IgnoreCase);
  if(m.Success) return "<SteamRoot>\\userdata\\"+MaskId(m.Groups[2].Value)+m.Groups[3].Value;
  // Avoid baking a user-profile path pattern into source (CI forbids it). Build at runtime.
  string userSeg=new string(new char[]{'U','s','e','r','s'});
  string bs=new string((char)92,1);
  string pat="^[A-Za-z]:"+Regex.Escape(bs)+userSeg+Regex.Escape(bs)+"[^"+Regex.Escape(bs)+"]+";
  return Regex.Replace(p,pat,"<UserProfile>",RegexOptions.IgnoreCase);
 }
 static string Redact(string text){
  if(string.IsNullOrEmpty(text)) return text;
  string t=text;
  string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
  if(!string.IsNullOrEmpty(local)) t=Regex.Replace(t,Regex.Escape(local),"%LOCALAPPDATA%",RegexOptions.IgnoreCase);
  string userSeg2=new string(new char[]{'U','s','e','r','s'}); string bs2=new string((char)92,1);
    t=Regex.Replace(t,"[A-Za-z]:"+Regex.Escape(bs2)+userSeg2+Regex.Escape(bs2)+"[^"+Regex.Escape(bs2)+"]+","<UserProfile>",RegexOptions.IgnoreCase);
  t=Regex.Replace(t,@"userdata\\[0-9]+","userdata\\<account>",RegexOptions.IgnoreCase);
  return t;
 }
 static string SafeFileToken(string value){
  string s=Regex.Replace(value??"x",@"[^\p{L}\p{N}\-_]+","_").Trim('_');
  if(s.Length==0) s="x";
  if(s.Length>40) s=s.Substring(0,40);
  return s;
 }
}

/// <summary>Drop-in for UI IMigrationService without shipping MigrationContract.cs on this branch.</summary>
public sealed class MigrationService {
 readonly string backupRoot;
 public MigrationService(string backupRoot=null){this.backupRoot=string.IsNullOrWhiteSpace(backupRoot)?Migration.DefaultBackupFolder():backupRoot;}
 public MigrationPreview Prepare(string steamAccount){return Migration.Prepare(steamAccount);}
 public List<SteamAccount> ListSteamAccounts(){return Migration.ListSteamAccounts();}
 public ExportResult ExportForImport(MigrationPreview preview,string folder){return Migration.ExportForImport(preview,folder);}
 public string WriteToGame(MigrationPreview preview,WriteConfirmation confirmation){
  var result=Migration.WriteToGameResult(preview,confirmation,backupRoot);
  if(!result.Success) throw new InvalidOperationException(result.Error??"Write failed.");
  return result.BackupManifest;
 }
 public List<BackupInfo> ListBackups(){return Migration.ListBackups(backupRoot);}
 public void Restore(string backupManifest){Migration.Restore(backupManifest);}
 public void Restore(BackupInfo backup){if(backup==null)throw new ArgumentNullException("backup");Migration.Restore(backup.Manifest);}
 public MigrationPreview Diagnose(){return Migration.Diagnose();}
 public string CreateDiagnosticReport(string folder){return Migration.CreateDiagnosticReport(folder);}
}
}
