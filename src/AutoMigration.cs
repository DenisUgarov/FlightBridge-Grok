using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using System.Xml.Linq;
using System.Diagnostics;
namespace FSMigrator {
public sealed class Installation {
 public string Year, Edition, InstallPath, Root, Account; public bool Active;
 public List<Profile> Profiles=new List<Profile>();
 public int Rejected;
 public override string ToString(){return Year+" · "+Edition+" · профилей: "+Profiles.Count;}
}
public sealed class AutomaticPlan {
 public List<Installation> Stores=new List<Installation>();
 public List<Plan> Changes=new List<Plan>();
 public List<string> Issues=new List<string>();
 public List<string> Notices=new List<string>();
 public bool Ready {get{return Changes.Count>0 && Issues.Count==0;}}
}
public static class AutoMigration {
 static string Vdf(string text,string key){var m=Regex.Match(text,"\""+Regex.Escape(key)+"\"\\s*\"([^\"]*)\"");return m.Success?m.Groups[1].Value.Replace("\\\\","\\"):null;}

 // Product constants (not machine paths / account ids).
 public const string SteamAppId2020="1250410";
 public const string SteamAppId2024="2537590";
 public const string StorePackagePrefix2020="Microsoft.FlightSimulator_";
 public const string StorePackagePrefix2024="Microsoft.Limitless_";

 public sealed class DiscoveryReport {
  public string SteamRoot;
  public bool SteamFromRegistry;
  public bool SteamFromFallback;
  public bool PackagesFolderExists;
  public bool UserDataExists;
  public int SteamAccountFolders;
  public List<string> SteamRootsTried=new List<string>();
  public List<string> SteamLibraries=new List<string>();
  public List<string> PackageFolderNames=new List<string>();
  public List<string> Notes=new List<string>();
  public List<Installation> Stores=new List<Installation>();
 }

 /// <summary>Default Steam install guesses when the registry is empty (no admin needed).</summary>
 public static List<string> DefaultSteamFallbackRoots(){
  var list=new List<string>();
  try{
   string x86=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
   string pf=Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
   if(!string.IsNullOrEmpty(x86)) list.Add(Path.Combine(x86,"Steam"));
   if(!string.IsNullOrEmpty(pf)){
    string candidate=Path.Combine(pf,"Steam");
    if(list.Count==0||!string.Equals(list[0],candidate,StringComparison.OrdinalIgnoreCase)) list.Add(candidate);
   }
  }catch{}
  return list;
 }

 static bool LooksLikeSteamRoot(string root){
  try{
   if(string.IsNullOrWhiteSpace(root)||!Directory.Exists(root)) return false;
   return Directory.Exists(Path.Combine(root,"steamapps"))||Directory.Exists(Path.Combine(root,"userdata"));
  }catch{return false;}
 }

 /// <summary>preferred → HKCU SteamPath → HKLM WOW6432Node InstallPath → fallbackRoots.</summary>
 public static string ResolveSteamRoot(string preferred,IEnumerable<string> fallbackRoots,out bool fromRegistry,out bool fromFallback,List<string> tried){
  fromRegistry=false;fromFallback=false;
  if(tried==null) tried=new List<string>();
  if(!string.IsNullOrWhiteSpace(preferred)){
   tried.Add(preferred);
   if(LooksLikeSteamRoot(preferred)) return Path.GetFullPath(preferred);
  }
  string reg=null;
  try{using(var k=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")){if(k!=null)reg=k.GetValue("SteamPath") as string;}}catch{}
  if(string.IsNullOrEmpty(reg)) try{using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")){if(k!=null)reg=k.GetValue("InstallPath") as string;}}catch{}
  if(!string.IsNullOrEmpty(reg)){
   tried.Add(reg);
   if(LooksLikeSteamRoot(reg)){fromRegistry=true;return Path.GetFullPath(reg);}
  }
  if(fallbackRoots!=null){
   foreach(string fb in fallbackRoots){
    if(string.IsNullOrWhiteSpace(fb)) continue;
    tried.Add(fb);
    if(LooksLikeSteamRoot(fb)){fromFallback=true;return Path.GetFullPath(fb);}
   }
  }
  return null;
 }

 public static string ReadActiveSteamAccount(){
  try{return Convert.ToString(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess","ActiveUser",null));}catch{return null;}
 }

 // SteamID64 → AccountID (userdata folder name). Pure Steam arithmetic, not a machine path.
 public const ulong SteamId64Base=76561197960265728UL;
 public static string AccountIdFromSteamId64(string steamId64){
  ulong id; if(!ulong.TryParse(steamId64,out id)||id<SteamId64Base) return null;
  return (id-SteamId64Base).ToString();
 }

 /// <summary>AccountID from config/loginusers.vdf entry with MostRecent=1. Null if missing.</summary>
 public static string ReadMostRecentAccountId(string steamRoot){
  if(string.IsNullOrWhiteSpace(steamRoot)) return null;
  string path=Path.Combine(steamRoot,"config","loginusers.vdf");
  if(!File.Exists(path)) return null;
  string text=File.ReadAllText(path);
  // Prefer the SteamID64 whose block contains MostRecent "1" (compact or multi-line VDF).
  var mostRecent=Regex.Matches(text,"\"(7656[0-9]{13})\"\\s*\\{([^{}]*)\\}",RegexOptions.Singleline);
  foreach(Match block in mostRecent){
   if(!Regex.IsMatch(block.Groups[2].Value,"\"MostRecent\"\\s*\"1\"")) continue;
   string account=AccountIdFromSteamId64(block.Groups[1].Value);
   if(account!=null) return account;
  }
  // Broader: SteamID64 appears before MostRecent "1" within a short window.
  foreach(Match m in Regex.Matches(text,"\"(7656[0-9]{13})\".{0,400}?\"MostRecent\"\\s*\"1\"",RegexOptions.Singleline)){
   string account=AccountIdFromSteamId64(m.Groups[1].Value);
   if(account!=null) return account;
  }
  return null;
 }

 public static List<Installation> Discover(){
  var tried=new List<string>();bool fromReg,fromFb;
  string steam=ResolveSteamRoot(null,DefaultSteamFallbackRoots(),out fromReg,out fromFb,tried);
  return Probe(steam,Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),ReadActiveSteamAccount(),null).Stores;
 }

 public static List<Installation> Discover(string steam,string local){return Discover(steam,local,null);}
 public static List<Installation> Discover(string steam,string local,string activeAccount){
  return Probe(steam,local,activeAccount,null).Stores;
 }

 /// <summary>
 /// Full discovery with a redacted-friendly report. When steamFallbacks is non-null and steam is empty/missing,
 /// those roots are tried (empty-registry scenario). Pass null steamFallbacks in tests that supply an explicit steam root.
 /// </summary>
 public static DiscoveryReport Probe(string steam,string local,string activeAccount,IEnumerable<string> steamFallbacks){
  var report=new DiscoveryReport();
  bool fromReg=false,fromFb=false;
  var tried=new List<string>();
  string resolved;
  if(steamFallbacks!=null){
   resolved=ResolveSteamRoot(steam,steamFallbacks,out fromReg,out fromFb,tried);
  }else if(!string.IsNullOrWhiteSpace(steam)&&LooksLikeSteamRoot(steam)){
   tried.Add(steam);resolved=Path.GetFullPath(steam);
  }else{
   if(!string.IsNullOrWhiteSpace(steam)) tried.Add(steam);
   resolved=null;
   if(!string.IsNullOrWhiteSpace(steam)&&!Directory.Exists(steam)) report.Notes.Add("Provided Steam root does not exist");
   else if(!string.IsNullOrWhiteSpace(steam)) report.Notes.Add("Provided path does not look like a Steam install");
  }
  report.SteamRootsTried=tried;
  report.SteamRoot=resolved;
  report.SteamFromRegistry=fromReg;
  report.SteamFromFallback=fromFb;

  var found=new List<Installation>();
  var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  if(!string.IsNullOrEmpty(resolved)){
   libraries.Add(resolved);
   string v=Path.Combine(resolved,"steamapps","libraryfolders.vdf");
   if(File.Exists(v)){
    foreach(Match m in Regex.Matches(File.ReadAllText(v),"\"path\"\\s*\"([^\"]+)\""))
     libraries.Add(m.Groups[1].Value.Replace("\\\\","\\"));
   }else report.Notes.Add("libraryfolders.vdf not found under Steam root");
   foreach(string lib in libraries) report.SteamLibraries.Add(lib);
   string users=Path.Combine(resolved,"userdata");
   report.UserDataExists=Directory.Exists(users);
   if(report.UserDataExists) report.SteamAccountFolders=Directory.GetDirectories(users).Length;
   else report.Notes.Add("Steam userdata folder missing");

   foreach(string id in new[]{SteamAppId2020,SteamAppId2024}){
    var installs=new List<string>();
    foreach(var lib in libraries){
     string manifest=Path.Combine(lib,"steamapps","appmanifest_"+id+".acf");
     if(!File.Exists(manifest)) continue;
     string dir=Vdf(File.ReadAllText(manifest),"installdir");
     if(dir!=null){string install=Path.Combine(lib,"steamapps","common",dir);if(Directory.Exists(install))installs.Add(install);}
    }
    if(!Directory.Exists(users)) continue;
    foreach(string user in Directory.GetDirectories(users)){
     string root=Path.Combine(user,id);
     if(!Directory.Exists(Path.Combine(root,"remote"))) continue;
     var s=new Installation{
      Year=id==SteamAppId2020?"2020":"2024",Edition="Steam",Root=Path.GetFullPath(root),
      Account=Path.GetFileName(user),InstallPath=installs.Count==1?installs[0]:null,
      Active=!string.IsNullOrEmpty(activeAccount)&&Path.GetFileName(user)==activeAccount
     };
     foreach(string f in Directory.GetFiles(Path.Combine(root,"remote"),"inputprofile_*")){
      if(!Regex.IsMatch(Path.GetFileName(f),@"^inputprofile_(?:inputprofile_)?[0-9]+$")) continue;
      try{s.Profiles.Add(Profile.Read(f));}
      catch(Exception ex){if(!(ex is InvalidDataException||ex is IOException||ex is System.Xml.XmlException||ex is InvalidOperationException||ex is UnauthorizedAccessException))throw;s.Rejected++;}
     }
     found.Add(s);
    }
   }
   if(!found.Any(s=>s.Edition=="Steam"&&s.Year=="2020")) report.Notes.Add("No Steam MSFS 2020 profiles (app "+SteamAppId2020+")");
   if(!found.Any(s=>s.Edition=="Steam"&&s.Year=="2024")) report.Notes.Add("No Steam MSFS 2024 profiles (app "+SteamAppId2024+")");
  }else{
   report.Notes.Add("Steam install not found (registry empty and fallbacks missed)");
  }

  if(!string.IsNullOrEmpty(local)){
   string packages=Path.Combine(local,"Packages");
   report.PackagesFolderExists=Directory.Exists(packages);
   if(report.PackagesFolderExists){
    foreach(string dir in Directory.GetDirectories(packages)){
     string name=Path.GetFileName(dir);
     report.PackageFolderNames.Add(name);
     string year=null;
     if(name.StartsWith(StorePackagePrefix2020,StringComparison.OrdinalIgnoreCase)) year="2020";
     else if(name.StartsWith(StorePackagePrefix2024,StringComparison.OrdinalIgnoreCase)) year="2024";
     else continue;
     string root=Path.Combine(dir,"SystemAppData","wgs");
     if(!Directory.Exists(root)){report.Notes.Add("Store package "+name+" has no SystemAppData\\\\wgs");continue;}
     var s=new Installation{Year=year,Edition="Microsoft Store / WGS",Root=root,Account="current-windows-user",Active=true};
     foreach(string f in SafeFiles(root)){
      if(Path.GetFileName(f)=="containers.index"||Path.GetFileName(f).StartsWith("container.")) continue;
      try{s.Profiles.Add(Profile.Read(f));}
      catch(InvalidDataException){}catch(System.Xml.XmlException){}catch(InvalidOperationException){}catch(ArgumentException){}catch(UnauthorizedAccessException){s.Rejected++;}catch(IOException){s.Rejected++;}
     }
     found.Add(s);
    }
    if(!found.Any(s=>s.Edition!=null&&s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0&&s.Year=="2020"))
     report.Notes.Add("No Store MSFS 2020 package matching "+StorePackagePrefix2020+"*");
    if(!found.Any(s=>s.Edition!=null&&s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0&&s.Year=="2024"))
     report.Notes.Add("No Store MSFS 2024 package matching "+StorePackagePrefix2024+"*");
   }else report.Notes.Add("Packages folder missing under LocalAppData");
  }else report.Notes.Add("LocalAppData root not provided");

  if(found.Count==0) report.Notes.Add("No MSFS profile stores discovered at all");
  report.Stores=found;
  return report;
 }

 public static IEnumerable<string> SafeFiles(string root){
  CheckPath(root);foreach(string f in Directory.GetFiles(root)){CheckPath(f);yield return f;}foreach(string d in Directory.GetDirectories(root))foreach(string f in SafeFiles(d))yield return f;
 }
 public static void CheckPath(string path){var p=Path.GetFullPath(path);while(!string.IsNullOrEmpty(p)){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки и перенаправленные каталоги не поддерживаются.");p=Path.GetDirectoryName(p);}}
 static string Norm(string s){return Regex.Replace((s??"").ToUpperInvariant(),@"[^\p{L}\p{N}]","");}
 static long Product(Profile p){string s=(string)p.Device.Attribute("ProductID")??"";long n;return s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?(long.TryParse(s.Substring(2),System.Globalization.NumberStyles.HexNumber,null,out n)?n:-1):(long.TryParse(s,out n)?n:-1);}
 public static List<Profile> CompatibleSources(Installation source,Profile target){
  var compatible=new List<Profile>();
  // Primary device identity: ProductID + normalized DeviceName (unchanged).
  var deviceMatches=source.Profiles.Where(s=>Product(s)>=0&&Product(s)==Product(target)&&Norm((string)s.Device.Attribute("DeviceName"))==Norm((string)target.Device.Attribute("DeviceName"))).ToList();
  // GUID is only a tie-breaker when several ProductID+DeviceName candidates remain.
  // Do NOT warn when a single candidate has a different GUID (2020/2024 fixtures differ).
  if(deviceMatches.Count>1){
   string guid=((string)target.Device.Attribute("GUID")??"").Trim();
   if(guid.Length>0){
    var byGuid=deviceMatches.Where(s=>string.Equals(((string)s.Device.Attribute("GUID")??"").Trim(),guid,StringComparison.OrdinalIgnoreCase)).ToList();
    if(byGuid.Count==1) deviceMatches=byGuid;
   }
  }
  foreach(var candidate in deviceMatches){
   try{var preview=Engine.Analyze(candidate,target,true);if(preview.Copied>0||preview.Axes>0)compatible.Add(candidate);}catch(InvalidDataException){}
  }
  return compatible;
 }
 public static AutomaticPlan Build(List<Installation> stores){return Build(stores,new Dictionary<string,string>());}
 public static AutomaticPlan Build(List<Installation> stores,Dictionary<string,string> choices){
  var a=new AutomaticPlan{Stores=stores};var sources=stores.Where(s=>s.Year=="2020").ToList();var targets=stores.Where(s=>s.Year=="2024").ToList();
  var pairs=(from sourceItem in sources from targetItem in targets where sourceItem.Edition!="Steam"||targetItem.Edition!="Steam"||sourceItem.Account==targetItem.Account select new{Source=sourceItem,Target=targetItem}).ToList();
  var activePairs=pairs.Where(pair=>pair.Source.Active&&pair.Target.Active).ToList();if(activePairs.Count==1)pairs=activePairs;
  if(pairs.Count!=1){a.Issues.Add(pairs.Count==0?"Запустите обе игры под нужной учётной записью и сохраните хотя бы один профиль управления.":"Найдено несколько аккаунтов с профилями. Оставьте активным нужный аккаунт Steam и повторите диагностику.");return a;}
  var source=pairs[0].Source;var target=pairs[0].Target;a.Stores=new List<Installation>{source,target};
  if((source.Edition=="Steam"&&source.InstallPath==null)||(target.Edition=="Steam"&&target.InstallPath==null)){a.Issues.Add("Проверьте установку обеих игр в Steam, затем повторите диагностику.");return a;}
  if(source.Rejected+target.Rejected>0){a.Issues.Add("Есть непрочитанные профили: сохраните диагностический отчёт для проверки формата.");return a;}
  bool alreadyCurrent=false;
  foreach(var t in target.Profiles){
   if(t.Xml.Root.Element("FriendlyName")==null){a.Notices.Add("Один профиль MSFS 2024 оставлен без изменений: его формат не содержит имени.");continue;}
   if(string.Equals((string)t.Xml.Root.Element("FriendlyName").Attribute("Locked"),"true",StringComparison.OrdinalIgnoreCase))continue;
   var candidates=CompatibleSources(source,t);
   var exact=candidates.Where(s=>Norm(s.Name)==Norm(t.Name)).ToList();
   // A short target name such as R66 may match a source name prefixed by the device name.
   if(exact.Count==0&&Norm(t.Name).Length>=3)exact=candidates.Where(s=>Norm(s.Name).EndsWith(Norm(t.Name),StringComparison.Ordinal)).ToList();
   if(exact.Count==1)candidates=exact; string chosen;if(choices.TryGetValue(t.Path,out chosen))candidates=candidates.Where(s=>s.Path==chosen).ToList();
   if(candidates.Count!=1){a.Notices.Add("Профиль «"+t.Name+"» устройства "+(string)t.Device.Attribute("DeviceName")+" оставлен без изменений: безопасное соответствие не найдено.");continue;}
   try{var p=Engine.Analyze(candidates[0],t,true);var friendly=p.Output.Root.Element("FriendlyName");friendly.ReplaceWith(new XElement(t.Xml.Root.Element("FriendlyName")));p.OutputName=t.Name;if((p.Copied>0||p.Axes>0)&&!XNode.DeepEquals(p.Output,t.Xml))a.Changes.Add(p);else if(p.Copied>0||p.Axes>0){alreadyCurrent=true;a.Notices.Add("Профиль «"+t.Name+"» уже содержит эти настройки.");}else a.Notices.Add("Профиль «"+t.Name+"» оставлен без изменений: совместимых настроек нет.");}catch(InvalidDataException){a.Notices.Add("Профиль «"+t.Name+"» оставлен без изменений: устройство нельзя сопоставить безопасно.");}
  }
  if(a.Changes.Count==0&&a.Issues.Count==0)a.Issues.Add(alreadyCurrent?"Все совместимые настройки уже перенесены. Повторная запись не требуется.":"Сначала один раз сохраните пользовательский профиль управления в MSFS 2024, закройте игру и повторите запуск Flight Bridge.");return a;
 }
 public static void RequireClosed(){RequireClosed(null);}
 public static void RequireClosed(IEnumerable<Installation> stores){
  bool steam=stores==null||stores.Any(s=>s.Edition=="Steam");
  foreach(var p in Process.GetProcesses()){
   using(p){
    string n;try{n=p.ProcessName;}catch{continue;}
    // MSFS 2020: FlightSimulator; MSFS 2024: FlightSimulator2024 (and other FlightSimulator* variants).
    bool sim=n.Equals("FlightSimulator",StringComparison.OrdinalIgnoreCase)
     ||n.Equals("FlightSimulator2024",StringComparison.OrdinalIgnoreCase)
     ||n.StartsWith("FlightSimulator",StringComparison.OrdinalIgnoreCase);
    bool steamProc=steam&&(n.Equals("steam",StringComparison.OrdinalIgnoreCase)||n.Equals("steamwebhelper",StringComparison.OrdinalIgnoreCase)||n.Equals("steamservice",StringComparison.OrdinalIgnoreCase));
    if(sim||steamProc) throw new IOException(steam?"Полностью закройте Steam, MSFS 2020 и MSFS 2024, затем повторите действие. / Fully close Steam, MSFS 2020 and MSFS 2024, then try again.":"Полностью закройте MSFS 2020 и MSFS 2024, затем повторите действие. / Fully close MSFS 2020 and MSFS 2024, then try again.");
   }
  }
 }
 public static string RedactedReport(AutomaticPlan a){return "Flight Bridge 0.5.1\r\n"+string.Join("\r\n",a.Stores.Select(s=>s.Year+"; "+s.Edition+"; profiles="+s.Profiles.Count+"; unreadable="+s.Rejected+"; install="+(s.InstallPath!=null)))+"\r\nPlans="+a.Changes.Count+"; skipped="+a.Notices.Count+"; blockers="+a.Issues.Count+"\r\nNo user names, paths, device GUIDs or profile names included.";}
}
}


