using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FSMigrator;

class MigrationTests {
 static int count;
 static void Check(bool v,string m){if(!v)throw new Exception(m);count++;Console.WriteLine("PASS "+m);}
 static void Throws(Action a,string m){try{a();}catch{Check(true,m);return;}throw new Exception("Expected rejection: "+m);}
 static int Main(){try{Run();Console.WriteLine(count+" migration facade checks passed");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}

 static string SetupSteamPair(string root){
  string steam=Path.Combine(root,"steam"),lib=Path.Combine(root,"library");
  Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
  File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
  Directory.CreateDirectory(Path.Combine(lib,"steamapps"));
  foreach(string id in new[]{"1250410","2537590"}){
   Directory.CreateDirectory(Path.Combine(lib,"steamapps","common",id));
   File.WriteAllText(Path.Combine(lib,"steamapps","appmanifest_"+id+".acf"),"\"installdir\" \""+id+"\"");
   string dir=Path.Combine(steam,"userdata","123",id,"remote");Directory.CreateDirectory(dir);
   File.Copy(id=="1250410"?"tests/fixtures/2020-fragment.xml":"tests/fixtures/2024-fragment.xml",Path.Combine(dir,"inputprofile_1"));
   File.WriteAllText(Path.Combine(Directory.GetParent(dir).FullName,"remotecache.vdf"),"\"inputprofile_1\" { }");
   File.WriteAllText(Path.Combine(dir,"logbook"),"preserve all non-profile data");
  }
  string targetRoot=Path.Combine(steam,"userdata","123","2537590");
  File.Copy(Path.Combine(targetRoot,"remote","inputprofile_1"),Path.Combine(targetRoot,"remote","inputprofile_2"));
  File.AppendAllText(Path.Combine(targetRoot,"remotecache.vdf"),"\"inputprofile_2\" { }");
  return steam;
 }

 static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"FlightBridge-mig-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string steam=SetupSteamPair(root);string local=Path.Combine(root,"local");string backups=Path.Combine(root,"backups");

  // Diagnose is read-only: hash every fixture file before/after.
  var before=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var f in Directory.GetFiles(steam,"*",SearchOption.AllDirectories)) before[f]=Engine.Hash(f);
  var preview=Migration.Prepare(steam,local,null);
  Check(preview.CanWriteToGame,"prepare ready for write");
  Check(!string.IsNullOrEmpty(preview.NextStep),"next step set");
  foreach(var f in Directory.GetFiles(steam,"*",SearchOption.AllDirectories))
   Check(before[f]==Engine.Hash(f),"diagnose/prepare left "+Path.GetFileName(f)+" unchanged");
  Migration.Diagnose(steam,local,null);
  foreach(var f in Directory.GetFiles(steam,"*",SearchOption.AllDirectories))
   Check(before.ContainsKey(f)&&before[f]==Engine.Hash(f),"diagnose read-only "+Path.GetFileName(f));

  // Export fallback does not touch stores
  string exportDir=Path.Combine(root,"export");
  var exported=Migration.ExportForImport(preview,exportDir);
  Check(exported.Files.Count>=2&&File.Exists(Path.Combine(exportDir,"README-IMPORT.txt")),"export writes files + readme");
  foreach(var f in before.Keys) Check(before[f]==Engine.Hash(f),"export did not modify store "+Path.GetFileName(f));

  // Write without confirmation rejected
  var denied=Migration.WriteToGameResult(preview,null,backups);
  Check(!denied.Success,"write refused without confirmation");
  var bound=WriteConfirmation.Confirm(preview);
  Check(object.ReferenceEquals(bound.BoundPreview,preview),"Confirm binds BoundPreview to the same preview");


  // Confirmation bound to another preview rejected
  var other=Migration.Prepare(steam,local,null);
  var wrong=WriteConfirmation.Confirm(other);
  var mismatched=Migration.WriteToGameResult(preview,wrong,backups);
  Check(!mismatched.Success,"write refused with confirmation from other preview");

  // Successful write: remotecache.vdf hash unchanged; Steam notice present; backup works
  string cache=Path.Combine(steam,"userdata","123","2537590","remotecache.vdf");
  string cacheHash=Engine.Hash(cache);
  string target=preview.UnderlyingPlan.Changes[0].Target.Path;
  string targetBefore=Engine.Hash(target);
  var ok=Migration.WriteToGameResult(preview,WriteConfirmation.Confirm(preview),backups);
  Check(ok.Success&&File.Exists(ok.BackupManifest),"write with confirmation succeeds");
  Check(Engine.Hash(cache)==cacheHash,"remotecache.vdf hash unchanged after write");
  Check(Engine.Hash(target)!=targetBefore,"target profile updated");
  Check(ok.Notices.Any(n=>n.IndexOf("Upload local",StringComparison.OrdinalIgnoreCase)>=0||n.IndexOf("локальн",StringComparison.OrdinalIgnoreCase)>=0),"steam cloud notice");

  // Restore from manifest
  Migration.Restore(ok.BackupManifest,steam,local);
  Check(Engine.Hash(target)==targetBefore,"restore by manifest restores bytes");
  Check(Engine.Hash(cache)==cacheHash,"remotecache.vdf unchanged after restore");

  // Corrupt a file that the journal tracks — Verify must reject
  {
   var journal=XDocument.Load(ok.BackupManifest);
   var fileEl=journal.Root.Elements("Store").SelectMany(s=>s.Elements("File")).First();
   string session=Path.GetDirectoryName(ok.BackupManifest);
   string copy=Path.Combine(session,(string)fileEl.Parent.Attribute("Copy"));
   string tracked=Path.Combine(copy,(string)fileEl.Attribute("Path"));
   File.AppendAllText(tracked,"x");
   Throws(()=>MigrationTransaction.Verify(ok.BackupManifest),"damaged backup rejected by verify");
  }

  // Mid-write failure rolls back (via existing transaction behavior)
  preview=Migration.Prepare(steam,local,null);
  target=preview.UnderlyingPlan.Changes[0].Target.Path; targetBefore=Engine.Hash(target);
  cacheHash=Engine.Hash(cache);
  Throws(()=>MigrationTransaction.Execute(preview.UnderlyingPlan,Path.Combine(root,"fail-backup"),()=>{},n=>{throw new IOException("injected");}),"injected write failure");
  Check(Engine.Hash(target)==targetBefore&&Engine.Hash(cache)==cacheHash,"failure rolls back; remotecache intact");

  // Redacted diagnostic report
  string report=Migration.CreateDiagnosticReport(Path.Combine(root,"diag"),preview);
  string text=File.ReadAllText(report);
  Check(!Regex.IsMatch(text,@"userdata\\123"),"report masks steam account folder");
  string userSeg=new string(new char[]{'U','s','e','r','s'});string bs=new string((char)92,1);
  Check(text.IndexOf("C:"+bs+userSeg+bs,StringComparison.OrdinalIgnoreCase)<0,"report has no raw user profile path");;
  Check(preview.Items.All(i=>i.Skipped.All(s=>!string.IsNullOrEmpty(s.Message)||s.Reason!=null)),"skipped bindings carry reason/message");

  // Auto account: single dual account works with null
  var accounts=Migration.ListSteamAccounts(steam,local);
  Check(accounts.Count(a=>a.HasMsfs2020&&a.HasMsfs2024)==1,"single dual steam account in fixture");

  // Multi-account requires choice
  string dirMulti=Path.Combine(steam,"userdata","999","1250410","remote");Directory.CreateDirectory(dirMulti);
  File.Copy("tests/fixtures/2020-fragment.xml",Path.Combine(dirMulti,"inputprofile_1"));
  Directory.CreateDirectory(Path.Combine(steam,"userdata","999","2537590","remote"));
  File.Copy("tests/fixtures/2024-fragment.xml",Path.Combine(steam,"userdata","999","2537590","remote","inputprofile_1"));
  File.WriteAllText(Path.Combine(steam,"userdata","999","1250410","remotecache.vdf"),"\"inputprofile_1\" { }");
  File.WriteAllText(Path.Combine(steam,"userdata","999","2537590","remotecache.vdf"),"\"inputprofile_1\" { }");
  
  var multi=Migration.Prepare(steam,local,null);
  Check(!multi.CanWriteToGame&&multi.Issues.Count>0&&multi.NextStep!=null,"multiple steam accounts ask to choose");
  Check(multi.Issues.All(i=>i.IndexOf("WGS",StringComparison.OrdinalIgnoreCase)<0&&i.IndexOf("GUID",StringComparison.OrdinalIgnoreCase)<0),"human issues without technical jargon");


  // Steam closed (ActiveUser=0): two dual accounts, MostRecent in loginusers.vdf → auto-pick, no prompt.
  {
   string root2=Path.Combine(Path.GetTempPath(),"FlightBridge-recent-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root2);
   string steam2=SetupSteamPair(root2);
   // Second dual account 999
   string dirRecent=Path.Combine(steam2,"userdata","999","1250410","remote");Directory.CreateDirectory(dirRecent);
   File.Copy("tests/fixtures/2020-fragment.xml",Path.Combine(dirRecent,"inputprofile_1"));
   Directory.CreateDirectory(Path.Combine(steam2,"userdata","999","2537590","remote"));
   File.Copy("tests/fixtures/2024-fragment.xml",Path.Combine(steam2,"userdata","999","2537590","remote","inputprofile_1"));
   File.WriteAllText(Path.Combine(steam2,"userdata","999","1250410","remotecache.vdf"),"\"inputprofile_1\" { }");
   File.WriteAllText(Path.Combine(steam2,"userdata","999","2537590","remotecache.vdf"),"\"inputprofile_1\" { }");
   Directory.CreateDirectory(Path.Combine(steam2,"config"));
   // SteamID64 = AccountID + 76561197960265728 → account 123 → 76561197960265851
   File.WriteAllText(Path.Combine(steam2,"config","loginusers.vdf"),
    "\"users\"{\n\"76561197960265851\"{\"AccountName\" \"alpha\" \"MostRecent\" \"1\"}\n\"76561197960266727\"{\"AccountName\" \"beta\" \"MostRecent\" \"0\"}\n}");
   Check(AutoMigration.ReadMostRecentAccountId(steam2)=="123","loginusers MostRecent maps SteamID64 to account 123");
   var dual=Migration.ListSteamAccounts(steam2,Path.Combine(root2,"local")).Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
   Check(dual.Count==2,"two dual accounts for MostRecent test");
   string picked=Migration.ChooseSteamAccount(dual,steam2,"0");
   Check(picked=="123","ActiveUser=0 + MostRecent auto-selects account 123 without prompt");
   var auto=Migration.Prepare(steam2,Path.Combine(root2,"local"),null);
   // Prepare reads registry ActiveUser (null on Linux) then MostRecent → should be ready or at least not ask to choose
   Check(auto.CandidateSteamAccounts==null||auto.CandidateSteamAccounts.Count==0||auto.CanWriteToGame||auto.CanExport,
    "Prepare with MostRecent does not force account picker");
   Check(auto.Issues.All(i=>i.IndexOf("нескольк",StringComparison.OrdinalIgnoreCase)<0&&i.IndexOf("Several",StringComparison.OrdinalIgnoreCase)<0),
    "Prepare with MostRecent has no multi-account issue");
  }

  // Two dual accounts, no loginusers.vdf, ActiveUser=0 → must ask.
  {
   string root3=Path.Combine(Path.GetTempPath(),"FlightBridge-pick-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root3);
   string steam3=SetupSteamPair(root3);
   string dir3=Path.Combine(steam3,"userdata","999","1250410","remote");Directory.CreateDirectory(dir3);
   File.Copy("tests/fixtures/2020-fragment.xml",Path.Combine(dir3,"inputprofile_1"));
   Directory.CreateDirectory(Path.Combine(steam3,"userdata","999","2537590","remote"));
   File.Copy("tests/fixtures/2024-fragment.xml",Path.Combine(steam3,"userdata","999","2537590","remote","inputprofile_1"));
   File.WriteAllText(Path.Combine(steam3,"userdata","999","1250410","remotecache.vdf"),"\"inputprofile_1\" { }");
   File.WriteAllText(Path.Combine(steam3,"userdata","999","2537590","remotecache.vdf"),"\"inputprofile_1\" { }");
   var dual3=Migration.ListSteamAccounts(steam3,Path.Combine(root3,"local")).Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
   Check(Migration.ChooseSteamAccount(dual3,steam3,"0")==null,"without loginusers.vdf ActiveUser=0 stays ambiguous");
   var ask=Migration.Prepare(steam3,Path.Combine(root3,"local"),null);
   Check(!ask.CanWriteToGame&&ask.Issues.Count>0&&ask.NextStep!=null,"without loginusers Prepare asks to choose");
   Check(ask.Notices.Any(n=>n.IndexOf("аккаунт 1",StringComparison.Ordinal)>=0)&&ask.Notices.Any(n=>n.IndexOf("account 1",StringComparison.Ordinal)>=0),"notices use ordinal account labels");
   Check(ask.Notices.Any(n=>n.IndexOf("аккаунт 2",StringComparison.Ordinal)>=0)&&ask.Notices.Any(n=>n.IndexOf("account 2",StringComparison.Ordinal)>=0),"notices label second account distinctly");
   Check(ask.Notices.All(n=>n.IndexOf("123",StringComparison.Ordinal)<0&&n.IndexOf("999",StringComparison.Ordinal)<0&&n.IndexOf("<steam:",StringComparison.OrdinalIgnoreCase)<0),"notices omit raw ids and hashes");
  }



    // Paths under LocalAppData (Windows %TEMP%) must still mask userdata\<account>.
  {
   string localBase=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   if(string.IsNullOrEmpty(localBase)) localBase=Path.GetTempPath();
   string maskRoot=Path.Combine(localBase,"FlightBridge-masktest-"+Guid.NewGuid().ToString("N"));
   string maskSteam=SetupSteamPair(maskRoot);
   var maskPreview=Migration.Prepare(maskSteam,Path.Combine(maskRoot,"local"),null);
   string maskReport=Migration.CreateDiagnosticReport(Path.Combine(maskRoot,"diag"),maskPreview);
   string maskText=File.ReadAllText(maskReport);
   Check(!Regex.IsMatch(maskText,@"userdata\\123"),"report masks steam account even under LocalAppData/Temp");
   Check(maskText.IndexOf("<account 1>",StringComparison.Ordinal)>=0,"report uses ordinal account label");
   // Unsalted SHA-256 of AccountID "123" (full + old 12-char MaskId prefix) must not appear.
   string sha123="a665a45920422f9d417e4867efdc4fb8a04a1f3fff1fa07e998e86f7f7a27ae3";
   Check(maskText.IndexOf(sha123,StringComparison.OrdinalIgnoreCase)<0,"report has no SHA-256 of account id");
   Check(maskText.IndexOf(sha123.Substring(0,12),StringComparison.OrdinalIgnoreCase)<0,"report has no SHA-256 prefix of account id");
   Check(maskText.IndexOf("<steam:",StringComparison.OrdinalIgnoreCase)<0,"report has no steam: hash labels");
   // Second dual account → distinct ordinals; neither raw id nor its hash.
   string dirMask2=Path.Combine(maskSteam,"userdata","999","1250410","remote");Directory.CreateDirectory(dirMask2);
   File.Copy("tests/fixtures/2020-fragment.xml",Path.Combine(dirMask2,"inputprofile_1"));
   Directory.CreateDirectory(Path.Combine(maskSteam,"userdata","999","2537590","remote"));
   File.Copy("tests/fixtures/2024-fragment.xml",Path.Combine(maskSteam,"userdata","999","2537590","remote","inputprofile_1"));
   File.WriteAllText(Path.Combine(maskSteam,"userdata","999","1250410","remotecache.vdf"),"\"inputprofile_1\" { }");
   File.WriteAllText(Path.Combine(maskSteam,"userdata","999","2537590","remotecache.vdf"),"\"inputprofile_1\" { }");
   var planMulti=AutoMigration.Build(AutoMigration.Discover(maskSteam,Path.Combine(maskRoot,"local")));
   var ids=planMulti.Stores.Where(s=>s.Edition=="Steam"&&!string.IsNullOrEmpty(s.Account)).Select(s=>s.Account).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
   Check(ids.Count>=2,"two steam accounts present for ordinal label test");
   string multiReport=Migration.CreateDiagnosticReport(Path.Combine(maskRoot,"diag-multi"),Migration.FromPlan(planMulti));
   string multiText=File.ReadAllText(multiReport);
   Check(multiText.IndexOf("<account 1>",StringComparison.Ordinal)>=0&&multiText.IndexOf("<account 2>",StringComparison.Ordinal)>=0,"two accounts get distinct ordinal labels");
   Check(!Regex.IsMatch(multiText,@"userdata\\(123|999)"),"multi report masks both raw account folders");
   string sha999="83cf8b609de60036a8277bd0e96135751bbc07eb234256d4b65b893360651bf2";
   Check(multiText.IndexOf(sha999,StringComparison.OrdinalIgnoreCase)<0&&multiText.IndexOf(sha999.Substring(0,12),StringComparison.OrdinalIgnoreCase)<0,"multi report has no SHA-256 of second account");
   Check(multiText.IndexOf(sha123,StringComparison.OrdinalIgnoreCase)<0&&multiText.IndexOf(sha123.Substring(0,12),StringComparison.OrdinalIgnoreCase)<0,"multi report has no SHA-256 of first account");
   try{Directory.Delete(maskRoot,true);}catch{}
  }

  // Saved diagnostic report uses the Steam account chosen on screen (not a different dual account).
  {
   string selRoot=Path.Combine(Path.GetTempPath(),"FlightBridge-selacct-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(selRoot);
   string selSteam=Path.Combine(selRoot,"steam"),selLib=Path.Combine(selRoot,"library"),selLocal=Path.Combine(selRoot,"local");
   Directory.CreateDirectory(Path.Combine(selSteam,"steamapps"));
   File.WriteAllText(Path.Combine(selSteam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\" { \"0\" { \"path\" \""+selLib.Replace("\\","\\\\")+"\" } }");
   Directory.CreateDirectory(Path.Combine(selLib,"steamapps"));
   foreach(string id in new[]{"1250410","2537590"}){
    Directory.CreateDirectory(Path.Combine(selLib,"steamapps","common",id));
    File.WriteAllText(Path.Combine(selLib,"steamapps","appmanifest_"+id+".acf"),"\"installdir\" \""+id+"\"");
   }
   // Account 111: one profile each year. Account 222: two 2020 profiles + one 2024 (distinguishable in the report).
   Action<string,string,string,int> put=delegate(string acct,string app,string fixture,int n){
    string remote=Path.Combine(selSteam,"userdata",acct,app,"remote");Directory.CreateDirectory(remote);
    var sbCache=new System.Text.StringBuilder();
    for(int i=1;i<=n;i++){File.Copy(fixture,Path.Combine(remote,"inputprofile_"+i),true);sbCache.Append("\"inputprofile_"+i+"\" { }");}
    File.WriteAllText(Path.Combine(selSteam,"userdata",acct,app,"remotecache.vdf"),sbCache.ToString());
   };
   put("111","1250410","tests/fixtures/2020-fragment.xml",1);
   put("111","2537590","tests/fixtures/2024-fragment.xml",1);
   put("222","1250410","tests/fixtures/2020-fragment.xml",2);
   put("222","2537590","tests/fixtures/2024-fragment.xml",1);
   var dual=Migration.ListSteamAccounts(selSteam,selLocal).Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
   Check(dual.Count==2,"two dual Steam accounts for selected-account report test");
   // Facade path mirrors MigrationService.CreateDiagnosticReport after Prepare(chosen): Diagnose(root,lad,chosen) then CreateDiagnosticReport(folder, preview).
   string report222=Migration.CreateDiagnosticReport(Path.Combine(selRoot,"diag-222"),Migration.Diagnose(selSteam,selLocal,"222"));
   string text222=File.ReadAllText(report222);
   Check(Regex.IsMatch(text222,@"Store year=2020; edition=Steam; profiles=2;"),"report for second account shows its 2020 profile count");
   string report111=Migration.CreateDiagnosticReport(Path.Combine(selRoot,"diag-111"),Migration.Diagnose(selSteam,selLocal,"111"));
   string text111=File.ReadAllText(report111);
   Check(Regex.IsMatch(text111,@"Store year=2020; edition=Steam; profiles=1;"),"report for first account shows its 2020 profile count");
   Check(text222.IndexOf("111",StringComparison.Ordinal)<0&&text222.IndexOf("222",StringComparison.Ordinal)<0,"second-account report omits raw account ids");
   Check(text111.IndexOf("111",StringComparison.Ordinal)<0&&text111.IndexOf("222",StringComparison.Ordinal)<0,"first-account report omits raw account ids");
   try{Directory.Delete(selRoot,true);}catch{}
  }

// Optional folder defaults
  var preview2=Migration.Prepare(steam,local,"123");
  if(preview2.CanExport){
   var exp=Migration.ExportForImport(preview2,null);
   Check(Directory.Exists(exp.Folder)&&exp.Folder.IndexOf("Flight Bridge",StringComparison.OrdinalIgnoreCase)>=0,"default export folder under Flight Bridge");
  }
 }
}
