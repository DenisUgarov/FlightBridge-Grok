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
 public string Label {get;set;}
 public string State {get;set;}
 public override string ToString(){return Label??"?";}
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
