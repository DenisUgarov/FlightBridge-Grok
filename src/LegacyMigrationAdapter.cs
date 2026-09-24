using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FSMigrator {
/// <summary>
/// Bridge from UI contract onto AutoMigration / Library.ExportBatch / MigrationTransaction (0.5.1).
/// Auto-picks the single Steam account that has both game profile folders; selector only if several.
/// </summary>
public sealed class LegacyMigrationAdapter : IMigrationService {
 readonly Dictionary<string,string> profileChoices;
 readonly string backupRoot;
 string lastSteamAccount;
 public LegacyMigrationAdapter(string backupRoot=null,Dictionary<string,string> profileChoices=null){
  this.backupRoot=string.IsNullOrWhiteSpace(backupRoot)?MigrationTransaction.DefaultRoot:backupRoot;
  this.profileChoices=profileChoices??new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 }

 public List<SteamAccount> ListSteamAccounts(){
  var stores=AutoMigration.Discover();
  return stores.Where(s=>s.Edition=="Steam")
   .GroupBy(s=>s.Account??"",StringComparer.OrdinalIgnoreCase)
   .Select(g=>new SteamAccount{
    Id=g.Key,
    Name=g.Key,
    HasMsfs2020=g.Any(s=>s.Year=="2020"&&s.Profiles.Count>0),
    HasMsfs2024=g.Any(s=>s.Year=="2024"&&s.Profiles.Count>0)
   })
   .OrderByDescending(a=>a.HasMsfs2020&&a.HasMsfs2024)
   .ThenBy(a=>a.Id,StringComparer.OrdinalIgnoreCase)
   .ToList();
 }

 public List<SteamAccount> QualifyingSteamAccounts(){
  return ListSteamAccounts().Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
 }

 public MigrationPreview Prepare(string steamAccount){
  lastSteamAccount=steamAccount;
  var all=AutoMigration.Discover();
  var dual=QualifyingSteamAccounts();
  if(string.IsNullOrWhiteSpace(steamAccount)){
   if(dual.Count>1){
    return new MigrationPreview{
     CanExport=false,CanWriteToGame=false,
     Issues=new List<string>{"MultipleSteamAccounts"},
     Notices=new List<string>(),
     NextStep="ChooseSteamAccount",
     FallbackReason=null,
     CandidateSteamAccounts=dual
    };
   }
   if(dual.Count==1)steamAccount=dual[0].Id;
  }
  IEnumerable<Installation> filtered=all;
  if(!string.IsNullOrWhiteSpace(steamAccount))
   filtered=all.Where(s=>s.Edition!="Steam"||string.Equals(s.Account,steamAccount,StringComparison.OrdinalIgnoreCase));
  var plan=AutoMigration.Build(filtered.ToList(),profileChoices);
  return PreviewModel.FromPlan(plan);
 }

 public MigrationPreview Diagnose(){return Prepare(lastSteamAccount);}

 public List<BackupInfo> ListBackups(){
  var list=new List<BackupInfo>();
  foreach(var manifest in MigrationTransaction.Sessions(backupRoot)){
   string state="?";try{state=MigrationTransaction.State(manifest);}catch{state="Invalid";}
   string label=Path.GetFileName(Path.GetDirectoryName(manifest))??"backup";
   list.Add(new BackupInfo{Manifest=manifest,Label=label,State=state});
  }
  return list;
 }

 public void Restore(BackupInfo backup){
  if(backup==null||string.IsNullOrWhiteSpace(backup.Manifest))throw new ArgumentNullException("backup");
  var document=MigrationTransaction.Verify(backup.Manifest);
  var current=AutoMigration.Discover();
  var roots=current.Select(s=>Path.GetFullPath(s.Root)).ToList();
  if(document.Root.Elements("Store").Any(s=>!roots.Contains((string)s.Attribute("Root"),StringComparer.OrdinalIgnoreCase)))
   throw new IOException("WrongInstall");
  MigrationTransaction.Restore(backup.Manifest,()=>AutoMigration.RequireClosed(current));
 }

 public string CreateDiagnosticReport(string folder){
  string root=string.IsNullOrWhiteSpace(folder)
   ?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Flight Bridge")
   :folder;
  Directory.CreateDirectory(root);
  string path=Path.Combine(root,"diagnostics.txt");
  var preview=Diagnose();
  string body=preview.UnderlyingPlan!=null
   ?AutoMigration.RedactedReport(preview.UnderlyingPlan)
   :"No plan\r\naccounts="+ListSteamAccounts().Count;
  File.WriteAllText(path,body);
  return path;
 }

 public ExportResult ExportForImport(MigrationPreview preview,string folder){
  if(preview==null)throw new ArgumentNullException("preview");
  if(preview.UnderlyingPlan==null||preview.UnderlyingPlan.Changes.Count==0)
   throw new InvalidOperationException("Nothing to export.");
  if(!preview.CanExport)throw new InvalidOperationException("Export is not available for this preview.");
  string root=string.IsNullOrWhiteSpace(folder)?Library.DefaultRoot:folder;
  string session=Library.ExportBatch(preview.UnderlyingPlan.Changes,root,true);
  var files=Directory.Exists(session)
   ?Directory.GetFiles(session,"MSFS2024-migrated.xml",SearchOption.AllDirectories).ToList()
   :new List<string>();
  return new ExportResult{Folder=session,Files=files};
 }

 public string WriteToGame(MigrationPreview preview,WriteConfirmation confirmation){
  if(confirmation==null)throw new InvalidOperationException("WriteToGame requires WriteConfirmation.");
  if(preview==null)throw new ArgumentNullException("preview");
  if(confirmation.BoundPreview!=null&&!object.ReferenceEquals(confirmation.BoundPreview,preview))
   throw new InvalidOperationException("WriteConfirmation is bound to a different preview.");
  if(!preview.CanWriteToGame||preview.UnderlyingPlan==null||!preview.UnderlyingPlan.Ready)
   throw new InvalidOperationException("Direct write is not available for this preview.");
  AutoMigration.RequireClosed(preview.UnderlyingPlan.Stores);
  return MigrationTransaction.Execute(preview.UnderlyingPlan,backupRoot,()=>AutoMigration.RequireClosed(preview.UnderlyingPlan.Stores),null);
 }
}
}
