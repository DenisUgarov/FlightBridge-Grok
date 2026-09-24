using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FSMigrator {
/// <summary>
/// Temporary bridge from the UI contract (prog/export-default-core) onto current
/// AutoMigration / Engine.Export / Library.ExportBatch / MigrationTransaction.
/// Replace with Programming's Migration implementation on merge; keep UI calls intact.
/// </summary>
public sealed class LegacyMigrationAdapter : IMigrationService {
 readonly Dictionary<string,string> profileChoices;
 readonly string backupRoot;
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
    HasMsfs2020=g.Any(s=>s.Year=="2020"),
    HasMsfs2024=g.Any(s=>s.Year=="2024")
   })
   .OrderByDescending(a=>a.HasMsfs2020&&a.HasMsfs2024)
   .ThenBy(a=>a.Id,StringComparer.OrdinalIgnoreCase)
   .ToList();
 }

 public MigrationPreview Prepare(string steamAccount){
  var all=AutoMigration.Discover();
  var dual=ListSteamAccounts().Where(a=>a.HasMsfs2020&&a.HasMsfs2024).ToList();
  if(string.IsNullOrWhiteSpace(steamAccount)&&dual.Count>1){
   return new MigrationPreview{
    CanExport=false,
    CanWriteToGame=false,
    Issues=new List<string>{"MultipleSteamAccounts"},
    Notices=new List<string>()
   };
  }
  IEnumerable<Installation> filtered=all;
  if(!string.IsNullOrWhiteSpace(steamAccount))
   filtered=all.Where(s=>s.Edition!="Steam"||string.Equals(s.Account,steamAccount,StringComparison.OrdinalIgnoreCase));
  var plan=AutoMigration.Build(filtered.ToList(),profileChoices);
  return PreviewModel.FromPlan(plan);
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
  if(confirmation==null)throw new InvalidOperationException("WriteToGame requires WriteConfirmation from the UI dialog.");
  if(preview==null)throw new ArgumentNullException("preview");
  if(!preview.CanWriteToGame||preview.UnderlyingPlan==null||!preview.UnderlyingPlan.Ready)
   throw new InvalidOperationException("Direct write is not available for this preview.");
  AutoMigration.RequireClosed(preview.UnderlyingPlan.Stores);
  return MigrationTransaction.Execute(preview.UnderlyingPlan,backupRoot,()=>AutoMigration.RequireClosed(preview.UnderlyingPlan.Stores),null);
 }
}
}
