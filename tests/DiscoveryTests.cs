using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using FSMigrator;

class DiscoveryTests {
 static int count;
 static void Check(bool v,string m){if(!v)throw new Exception(m);count++;Console.WriteLine("PASS "+m);}
 static int Main(){try{Run();Console.WriteLine(count+" discovery checks passed");return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}

 static void WriteProfile(string path,string fixture){Directory.CreateDirectory(Path.GetDirectoryName(path));File.Copy(fixture,path,true);}
 static void SteamGame(string lib,string appId){
  Directory.CreateDirectory(Path.Combine(lib,"steamapps","common",appId));
  File.WriteAllText(Path.Combine(lib,"steamapps","appmanifest_"+appId+".acf"),"\"installdir\" \""+appId+"\"");
 }
 static void SteamAccountProfiles(string steam,string account,bool y2020,bool y2024){
  if(y2020){string d=Path.Combine(steam,"userdata",account,"1250410","remote");WriteProfile(Path.Combine(d,"inputprofile_1"),"tests/fixtures/2020-fragment.xml");File.WriteAllText(Path.Combine(steam,"userdata",account,"1250410","remotecache.vdf"),"\"inputprofile_1\" { }");}
  if(y2024){string d=Path.Combine(steam,"userdata",account,"2537590","remote");WriteProfile(Path.Combine(d,"inputprofile_1"),"tests/fixtures/2024-fragment.xml");File.WriteAllText(Path.Combine(steam,"userdata",account,"2537590","remotecache.vdf"),"\"inputprofile_1\" { }");}
 }
 static void StorePackage(string local,string year){
  string prefix=year=="2020"?AutoMigration.StorePackagePrefix2020:AutoMigration.StorePackagePrefix2024;
  string family=prefix+"TESTPUBLISHER";
  string root=Path.Combine(local,"Packages",family,"SystemAppData","wgs","folder");
  WriteProfile(Path.Combine(root,"blob"),year=="2020"?"tests/fixtures/2020-fragment.xml":"tests/fixtures/2024-fragment.xml");
 }
 static string NewRoot(string tag){string r=Path.Combine(Path.GetTempPath(),"FlightBridge-disc-"+tag+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(r);return r;}

 static void Run(){
  // Steam + Steam
  {
   string root=NewRoot("ss");string steam=Path.Combine(root,"steam");string lib=Path.Combine(root,"lib");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
   SteamGame(lib,"1250410");SteamGame(lib,"2537590");
   SteamAccountProfiles(steam,"111",true,true);
   var stores=AutoMigration.Discover(steam,local);
   Check(stores.Count(s=>s.Edition=="Steam"&&s.Year=="2020")==1&&stores.Count(s=>s.Edition=="Steam"&&s.Year=="2024")==1,"Steam+Steam discovers both years");
  }
  // Store + Store
  {
   string root=NewRoot("st");string local=Path.Combine(root,"local");
   StorePackage(local,"2020");StorePackage(local,"2024");
   var stores=AutoMigration.Discover(null,local);
   Check(stores.Any(s=>s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0&&s.Year=="2020"),"Store 2020 discovered by prefix");
   Check(stores.Any(s=>s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0&&s.Year=="2024"),"Store 2024 discovered by prefix");
  }
  // Steam 2020 + Store 2024
  {
   string root=NewRoot("mix1");string steam=Path.Combine(root,"steam");string lib=Path.Combine(root,"lib");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
   SteamGame(lib,"1250410");SteamAccountProfiles(steam,"111",true,false);
   StorePackage(local,"2024");
   var stores=AutoMigration.Discover(steam,local);
   Check(stores.Any(s=>s.Edition=="Steam"&&s.Year=="2020")&&stores.Any(s=>s.Year=="2024"&&s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0),"Steam2020+Store2024");
  }
  // Store 2020 + Steam 2024
  {
   string root=NewRoot("mix2");string steam=Path.Combine(root,"steam");string lib=Path.Combine(root,"lib");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
   SteamGame(lib,"2537590");SteamAccountProfiles(steam,"111",false,true);
   StorePackage(local,"2020");
   var stores=AutoMigration.Discover(steam,local);
   Check(stores.Any(s=>s.Edition=="Steam"&&s.Year=="2024")&&stores.Any(s=>s.Year=="2020"&&s.Edition.IndexOf("Store",StringComparison.OrdinalIgnoreCase)>=0),"Store2020+Steam2024");
  }
  // Steam library on another "disk" via libraryfolders.vdf
  {
   string root=NewRoot("lib");string steam=Path.Combine(root,"steam");string other=Path.Combine(root,"otherdisk");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+other.Replace("\\","\\\\")+"\" } }");
   SteamGame(other,"1250410");SteamGame(other,"2537590");
   SteamAccountProfiles(steam,"111",true,true);
   var report=AutoMigration.Probe(steam,local,null,null);
   Check(report.SteamLibraries.Any(l=>string.Equals(Path.GetFullPath(l),Path.GetFullPath(other),StringComparison.OrdinalIgnoreCase)),"extra library from libraryfolders.vdf");
   Check(report.Stores.Count(s=>s.Edition=="Steam")>=2,"profiles found via secondary library install + userdata");
  }
  // Several accounts: only one dual → auto via ChooseSteamAccount
  {
   string root=NewRoot("one");string steam=Path.Combine(root,"steam");string lib=Path.Combine(root,"lib");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
   SteamGame(lib,"1250410");SteamGame(lib,"2537590");
   SteamAccountProfiles(steam,"111",true,true);
   SteamAccountProfiles(steam,"222",true,false); // only 2020
   var dual=Migration.ListSteamAccounts(steam,local).Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
   Check(dual.Count==1&&Migration.ChooseSteamAccount(dual,steam,"0")=="111","single dual account auto-selected");
  }
  // Two dual, ActiveUser=0, MostRecent picks one
  {
   string root=NewRoot("mr");string steam=Path.Combine(root,"steam");string lib=Path.Combine(root,"lib");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
   SteamGame(lib,"1250410");SteamGame(lib,"2537590");
   SteamAccountProfiles(steam,"111",true,true);SteamAccountProfiles(steam,"222",true,true);
   Directory.CreateDirectory(Path.Combine(steam,"config"));
   File.WriteAllText(Path.Combine(steam,"config","loginusers.vdf"),
    "\"users\"{\n\"76561197960265839\"{\"MostRecent\" \"0\"}\n\"76561197960265950\"{\"MostRecent\" \"1\"}\n}");
   // 76561197960265728+111=76561197960265839; +222=76561197960265950
   Check(AutoMigration.ReadMostRecentAccountId(steam)=="222","MostRecent maps to account 222");
   var dual=Migration.ListSteamAccounts(steam,local).Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
   Check(Migration.ChooseSteamAccount(dual,steam,"0")=="222","MostRecent wins when ActiveUser=0");
  }
  // One game missing
  {
   string root=NewRoot("miss");string steam=Path.Combine(root,"steam");string lib=Path.Combine(root,"lib");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
   SteamGame(lib,"1250410");SteamAccountProfiles(steam,"111",true,false);
   var preview=Migration.Prepare(steam,local,null);
   Check(!preview.CanWriteToGame&&preview.Issues.Count>0&&!string.IsNullOrEmpty(preview.NextStep),"one game missing → clear Issue/NextStep");
  }
  // Nothing found
  {
   string root=NewRoot("empty");string local=Path.Combine(root,"local");Directory.CreateDirectory(local);
   var report=AutoMigration.Probe(Path.Combine(root,"nosteam"),local,null,null);
   Check(report.Stores.Count==0,"nothing found → zero stores");
   var preview=Migration.Prepare(Path.Combine(root,"nosteam"),local,null);
   Check(!preview.CanWriteToGame&&preview.Issues.Count>0&&preview.NextStep!=null,"nothing found → Issue + NextStep");
   string diag=Migration.CreateDiagnosticReport(Path.Combine(root,"diag"),preview);
   string text=File.ReadAllText(diag);
   Check(text.IndexOf("Discovery",StringComparison.OrdinalIgnoreCase)>=0,"diagnostic mentions Discovery");
   Check(text.IndexOf("userdata\\111",StringComparison.OrdinalIgnoreCase)<0&&text.IndexOf("<steam:",StringComparison.OrdinalIgnoreCase)<0,"diagnostic masks account ids");
  }
  // Empty registry + fallback root succeeds
  {
   string root=NewRoot("fb");string steam=Path.Combine(root,"ProgramFiles","Steam");string lib=Path.Combine(root,"lib");string local=Path.Combine(root,"local");
   Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
   File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"libraryfolders\"{ \"0\" { \"path\" \""+lib.Replace("\\","\\\\")+"\" } }");
   SteamGame(lib,"1250410");SteamGame(lib,"2537590");SteamAccountProfiles(steam,"111",true,true);
   var report=AutoMigration.Probe(null,local,null,new[]{steam});
   Check(report.SteamFromFallback&&report.Stores.Count>=2,"empty preferred + fallback Steam root works");
  }
 }
}
