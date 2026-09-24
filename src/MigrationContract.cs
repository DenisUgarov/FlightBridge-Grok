using System;
using System.Collections.Generic;

namespace FSMigrator {
// Contract owned by prog/export-default-core — remove duplicates on merge.
// Temporary DTO mirror for main 0.5.1; later repoint to Programming's Migration types.
// Product: WriteToGame is main; ExportForImport is fallback only.

public sealed class SkippedBinding {
 public string Action {get;set;}
 public string Context {get;set;}
 public string Reason {get;set;}
 /// <summary>Human text from core when available; UI prefers this over Reason mapping.</summary>
 public string Message {get;set;}
 public SkippedBinding(){}
 public SkippedBinding(string action,string context,string reason){Action=action;Context=context;Reason=reason;Message=HumanMessage(reason,action,context);}
 public SkippedBinding(string action,string context,string reason,string message){Action=action;Context=context;Reason=reason;Message=message??HumanMessage(reason,action,context);}
 public static string HumanMessage(string reason,string action,string context){
  string a=string.IsNullOrEmpty(action)?"this control":action;
  string c=string.IsNullOrEmpty(context)?"its section":context;
  if(reason==SkipReason.NoTargetAction) return "No matching control found in the 2024 profile for "+a+" ("+c+"). / В профиле 2024 нет подходящей команды для "+a+" ("+c+").";
  if(reason==SkipReason.ContextMismatch) return "The control "+a+" exists in 2024 but in a different section than "+c+", and it could not be moved safely. / Команда "+a+" есть в 2024, но в другом разделе, чем "+c+", и безопасно перенести её нельзя.";
  if(reason==SkipReason.CategoryMismatch) return "This binding was skipped because the profile categories do not match. / Привязка пропущена: категории профилей не совпадают.";
  if(reason==SkipReason.DeviceAmbiguous) return "Several devices matched; this binding was left unchanged. / Подходит несколько устройств; привязка не изменена.";
  if(reason==SkipReason.NoTarget2024Profile) return "No matching 2024 profile was found for this 2020 profile. / Для этого профиля 2020 не найден подходящий профиль 2024.";
  return "This binding was skipped. / Эта привязка пропущена.";
 }
}

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
 internal AutomaticPlan UnderlyingPlan {get;set;}
 public bool HasSteamStore {get;set;}
 public bool HasMicrosoftStore {get;set;}
 public MigrationPreview(){Items=new List<PreviewItem>();Issues=new List<string>();Notices=new List<string>();CandidateSteamAccounts=new List<SteamAccount>();}
}

public sealed class SteamAccount {
 public string Id {get;set;}
 public string Name {get;set;}
 public bool HasMsfs2020 {get;set;}
 public bool HasMsfs2024 {get;set;}
 public override string ToString(){return string.IsNullOrEmpty(Name)||Name==Id?("Account "+(Id!=null&&Id.Length>4?Id.Substring(Id.Length-4):Id)):Name;}
}

public sealed class ExportResult {
 public string Folder {get;set;}
 public List<string> Files {get;set;}
 public ExportResult(){Files=new List<string>();}
}

public sealed class BackupInfo {
 public string Manifest {get;set;}
 public string Folder {get;set;}
 public string Label {get;set;}
 public string State {get;set;}
 public DateTime CreatedUtc {get;set;}
 public override string ToString(){return Label??Folder??Manifest??"?";}
}

/// <summary>Normal confirm after preview; bound to the shown MigrationPreview.</summary>
public sealed class WriteConfirmation {
 public MigrationPreview BoundPreview {get;private set;}
 public string AcknowledgedSummary {get;private set;}
 WriteConfirmation(MigrationPreview preview,string summary){BoundPreview=preview;AcknowledgedSummary=summary;}
 public static WriteConfirmation Confirm(MigrationPreview preview){
  if(preview==null)throw new ArgumentNullException("preview");
  return new WriteConfirmation(preview,"ok:"+preview.Items.Count);
 }
 internal bool IsFor(MigrationPreview preview){return preview!=null&&object.ReferenceEquals(BoundPreview,preview);}
}

public interface IMigrationService {
 MigrationPreview Prepare(string steamAccount);
 MigrationPreview Diagnose();
 List<SteamAccount> ListSteamAccounts();
 List<BackupInfo> ListBackups();
 void Restore(BackupInfo backup);
 // folder null/empty => default Documents folders
 string CreateDiagnosticReport(string folder);
 ExportResult ExportForImport(MigrationPreview preview,string folder);
 string WriteToGame(MigrationPreview preview,WriteConfirmation confirmation);
}
}
